using Godot;
using RtsDemo.GodotRuntime;
using RtsDemo.GodotRuntime.Resources;
using RtsDemo.Scenarios;
using RtsDemo.Simulation;

namespace RtsDemo.Demos.TwoD;

public partial class TerrainAuthoringWorkflowDemo : Node2D
{
    private const int VerificationTicks = 360;
    private const string RuntimeOutputPath =
        "user://terrain_authoring_demo_runtime.tres";
    private RtsTerrainAuthoring2D? _authoring;
    private Label? _status;
    private int _ticks;
    private bool _recording;
    private bool _initialValid;
    private bool _orthogonalBrush;
    private bool _brokenRampRejected;
    private bool _coordinateReported;
    private bool _repairedValid;
    private bool _independentOverlaysPainted;
    private bool _exported;
    private bool _reloadExact;
    private ulong _exportHash;
    private TerrainCell _orthogonalBefore;
    private bool _runtimePainting;

    public override void _Ready()
    {
        _recording = OS.GetCmdlineUserArgs().Contains(
            "--terrain-authoring-demo-recording");
        _authoring = GetNode<RtsTerrainAuthoring2D>("Authoring");
        if (_authoring.AuthoringAsset is null)
            throw new InvalidOperationException("Authoring demo asset is missing.");
        _authoring.AuthoringAsset =
            (RtsTerrainAuthoringResource)_authoring.AuthoringAsset.Duplicate(true);
        _authoring.AuthoringAsset.RuntimeOutputPath = RuntimeOutputPath;
        _authoring.SaveAuthoringAssetOnExport = false;
        _initialValid = _authoring.ValidateAuthoring().IsValid;
        CreateHud();
        UpdateHud("Loaded editable Resource; runtime snapshot not mutated.");
    }

    public override void _PhysicsProcess(double delta)
    {
        _ticks++;
        if (_authoring is null) return;
        if (_ticks == 30) PaintOrthogonalSurface();
        if (_ticks == 85) BreakRamp();
        if (_ticks == 150) RepairRamp();
        if (_ticks == 205) PaintIndependentRules();
        if (_ticks == 270) ExportAndReload();
        if (_recording && _ticks >= VerificationTicks) Finish();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_recording || _authoring is null) return;
        if (@event is InputEventMouseButton button &&
            button.ButtonIndex == MouseButton.Left)
        {
            if (button.Pressed)
                _runtimePainting = _authoring.BeginStroke(button.Position);
            else if (_runtimePainting)
            {
                _authoring.EndStroke();
                _runtimePainting = false;
                UpdateHud("Manual brush stroke applied. Press V to validate.");
            }
        }
        else if (_runtimePainting && @event is InputEventMouseMotion motion)
        {
            _authoring.ContinueStroke(motion.Position);
        }
        else if (@event is InputEventKey key && key.Pressed && key.Keycode == Key.V)
        {
            var validation = _authoring.ValidateAuthoring();
            UpdateHud(validation.IsValid
                ? "Manual validation PASS."
                : validation.FirstError?.Message ?? "Validation failed.");
        }
    }

    private void PaintOrthogonalSurface()
    {
        if (_authoring is null || _authoring.AuthoringAsset is null) return;
        TerrainAuthoringResourceConverter.TryConvert(
            _authoring.AuthoringAsset, out var beforeDocument, out _);
        _orthogonalBefore = beforeDocument!.Cell(4, 3);
        _authoring.BrushKind = TerrainBrushKind.Surface;
        _authoring.BrushRadiusCells = 1;
        _authoring.BrushSurfaceId = 1;
        PaintCell(4, 3);
        TerrainAuthoringResourceConverter.TryConvert(
            _authoring.AuthoringAsset, out var afterDocument, out _);
        var after = afterDocument!.Cell(4, 3);
        _orthogonalBrush = after.SurfaceId == 1 &&
                           after.CliffLevel == _orthogonalBefore.CliffLevel &&
                           after.Pathing == _orthogonalBefore.Pathing &&
                           after.Flags == _orthogonalBefore.Flags &&
                           after.RampDirection == _orthogonalBefore.RampDirection;
        UpdateHud("Surface brush changed only SurfaceId; other rules preserved.");
    }

    private void BreakRamp()
    {
        if (_authoring is null) return;
        _authoring.BrushKind = TerrainBrushKind.CliffLevel;
        _authoring.BrushRadiusCells = 0;
        _authoring.BrushCliffLevel = 0;
        PaintCell(11, 5);
        var validation = _authoring.ValidateAuthoring();
        var issue = validation.Issues.FirstOrDefault(value =>
            value.Code == TerrainAuthoringErrorCode.InvalidRampUpperNeighbor);
        _brokenRampRejected = !validation.IsValid &&
                              issue.Code ==
                                  TerrainAuthoringErrorCode.InvalidRampUpperNeighbor;
        _coordinateReported = issue.Column == 10 && issue.Row == 5 &&
                              issue.Message.Contains("[10,5]",
                                  StringComparison.Ordinal);
        UpdateHud("BROKEN RAMP rejected at Cell [10,5]; export is blocked.");
    }

    private void RepairRamp()
    {
        if (_authoring is null) return;
        _authoring.BrushCliffLevel = 1;
        PaintCell(11, 5);
        _repairedValid = _authoring.ValidateAuthoring().IsValid;
        UpdateHud("Ramp repaired: lower 0 → ramp 0..1 → upper 1.");
    }

    private void PaintIndependentRules()
    {
        if (_authoring is null || _authoring.AuthoringAsset is null) return;
        _authoring.BrushKind = TerrainBrushKind.BlocksVision;
        _authoring.BrushEnabled = true;
        _authoring.BrushRadiusCells = 1;
        PaintCell(5, 7);
        _authoring.BrushKind = TerrainBrushKind.Buildable;
        _authoring.BrushEnabled = false;
        PaintCell(7, 2);
        _authoring.BrushKind = TerrainBrushKind.BlocksCreep;
        _authoring.BrushEnabled = true;
        PaintCell(8, 8);
        _authoring.Overlays =
            TerrainAuthoringOverlay.Surface |
            TerrainAuthoringOverlay.Buildable |
            TerrainAuthoringOverlay.Vision |
            TerrainAuthoringOverlay.Ramp |
            TerrainAuthoringOverlay.Creep |
            TerrainAuthoringOverlay.Validation;
        TerrainAuthoringResourceConverter.TryConvert(
            _authoring.AuthoringAsset, out var document, out _);
        var vision = document!.Cell(5, 7);
        var build = document.Cell(7, 2);
        var creep = document.Cell(8, 8);
        _independentOverlaysPainted =
            (vision.Flags & TerrainCellFlags.BlocksVision) != 0 &&
            !build.IsBuildable && build.Pathing == TerrainPathing.Ground &&
            (creep.Flags & TerrainCellFlags.BlocksCreep) != 0;
        _repairedValid &= _authoring.ValidateAuthoring().IsValid;
        UpdateHud("Vision blocking and Buildable painted independently.");
    }

    private void ExportAndReload()
    {
        if (_authoring is null) return;
        var result = _authoring.ExportRuntimeResource();
        _exported = result.Succeeded;
        if (_exported && TerrainMapResourceConverter.TryLoadSnapshot(
                RuntimeOutputPath, out var snapshot, out _) && snapshot is not null &&
            TerrainAuthoringResourceConverter.TryExport(
                _authoring.AuthoringAsset!, out var authored, out _) &&
            authored is not null)
        {
            _reloadExact = snapshot.StableHash == authored.StableHash;
            _exportHash = snapshot.StableHash;
        }
        UpdateHud($"Runtime export/reload exact={_reloadExact}, " +
                  $"hash={_exportHash:X16}.");
    }

    private void PaintCell(int column, int row)
    {
        if (_authoring is null) return;
        var local = new Vector2(
            (column + 0.5f) * TerrainAuthoringDemoDefinition.CellSize,
            (row + 0.5f) * TerrainAuthoringDemoDefinition.CellSize);
        var canvas = _authoring.ToGlobal(local);
        _authoring.BeginStroke(canvas);
        _authoring.EndStroke();
    }

    private void CreateHud()
    {
        var layer = new CanvasLayer();
        AddChild(layer);
        var header = new ColorRect
        {
            Position = Vector2.Zero,
            Size = new Vector2(1280f, 158f),
            Color = new Color(0.025f, 0.045f, 0.07f, 0.96f)
        };
        layer.AddChild(header);
        _status = new Label
        {
            Position = new Vector2(28f, 18f),
            Size = new Vector2(1220f, 132f)
        };
        _status.AddThemeFontSizeOverride("font_size", 17);
        _status.AddThemeColorOverride("font_color", new Color("dcebf5"));
        header.AddChild(_status);
        var legend = new Label
        {
            Position = new Vector2(760f, 182f),
            Size = new Vector2(480f, 430f),
            Text =
                "ORTHOGONAL DATA LAYERS\n\n" +
                "Surface      visual material only\n" +
                "Height       cliff level 0..15\n" +
                "Pathing      Ground / Shallow / Deep / Air\n" +
                "Buildable    independent placement rule\n" +
                "Ramp         direction + adjacent levels\n" +
                "Vision       obstructing terrain\n\n" +
                "Creep        independent allow/block mask\n\n" +
                "Blue circle   Spawn anchor\n" +
                "Yellow circle Resource anchor\n" +
                "Purple circle Objective anchor\n" +
                "Red outline   exact invalid cell\n\n" +
                "Editor: select RtsTerrainAuthoring2D,\n" +
                "paint in 2D view, Validate, then Export."
        };
        legend.AddThemeFontSizeOverride("font_size", 18);
        legend.AddThemeColorOverride("font_color", new Color("c8d7e1"));
        layer.AddChild(legend);
    }

    private void UpdateHud(string phase)
    {
        if (_status is null) return;
        _status.Text =
            "RTS TERRAIN AUTHORING / GODOT RESOURCE → IMMUTABLE SNAPSHOT\n" +
            $"Phase: {phase}\n" +
            $"Initial={_initialValid}  Orthogonal={_orthogonalBrush}  " +
            $"RampReject={_brokenRampRejected}/{_coordinateReported}  " +
            $"Repair={_repairedValid}  Rules={_independentOverlaysPainted}  " +
            $"Export={_exported}/{_reloadExact}";
    }

    private void Finish()
    {
        _recording = false;
        var passed = _initialValid && _orthogonalBrush &&
                     _brokenRampRejected && _coordinateReported &&
                     _repairedValid && _independentOverlaysPainted &&
                     _exported && _reloadExact && _exportHash != 0UL;
        GD.Print($"RTS_TERRAIN_AUTHORING_DEMO_{(passed ? "PASS" : "FAIL")} " +
                 $"ticks={_ticks}, initial={_initialValid}, " +
                 $"orthogonal={_orthogonalBrush}, " +
                 $"ramp={_brokenRampRejected}/{_coordinateReported}/" +
                 $"{_repairedValid}, rules={_independentOverlaysPainted}, " +
                 $"export={_exported}/{_reloadExact}, hash={_exportHash:X16}");
        GetTree().Quit(passed ? 0 : 1);
    }
}
