using Godot;
using RtsDemo.Demos.ThreeD;
using RtsDemo.Demos.War3;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace War3Rts.Maps;

[Tool]
[GlobalClass]
public partial class War3MapAuthoring3D : Node3D
{
    private string _mapAssetPath = string.Empty;
    private War3MapEditDocument? _document;
    private Rts3DTerrainPresenter? _terrainPreview;
    private Node3D? _objectPreview;
    private string? _strokeBefore;
    private NVector2 _lastStrokePoint;
    private bool _painting;
    private bool _previewDirty;
    private double _previewDelay;
    private int _strokeSeed;

    [Export(PropertyHint.File, "*.w3map.json,*.json")]
    public string MapAssetPath
    {
        get => _mapAssetPath;
        set
        {
            if (_mapAssetPath == value) return;
            _mapAssetPath = value;
            if (IsInsideTree() && value.Length > 0)
                LoadMap(value, out _);
        }
    }

    [Export]
    public War3MapTool CurrentTool { get; set; } = War3MapTool.Surface;

    [Export(PropertyHint.Range, "0,32,1")]
    public int BrushRadiusCells { get; set; } = 2;

    [Export(PropertyHint.Range, "0.05,32,0.05")]
    public float BrushStrength { get; set; } = 2f;

    [Export]
    public War3BrushShape BrushShape { get; set; } = War3BrushShape.Circle;

    [Export(PropertyHint.Range, "0,255,1")]
    public int SurfaceId { get; set; } = 3;

    [Export(PropertyHint.Range, "0,15,1")]
    public int CliffLevel { get; set; }

    [Export]
    public TerrainRampDirection RampDirection { get; set; } =
        TerrainRampDirection.PositiveX;

    [Export(PropertyHint.Range, "0,2,1")]
    public int OwnerSlot { get; set; }

    [Export]
    public bool PaintEnabled { get; set; } = true;

    [Export]
    public bool PreviewLightingEnabled { get; set; } = true;

    public bool HasDocument => _document is not null;
    public string CurrentMapId => _document?.Metadata.Id ?? string.Empty;
    public string CurrentMapName => _document?.Metadata.DisplayName ?? string.Empty;
    public int PreviewRebuildCount { get; private set; }
    public int CommittedStrokeCount { get; private set; }

    public string CaptureSerialized() => _document?.CaptureJson() ?? string.Empty;

    public override void _Ready()
    {
        SetProcess(true);
        if (_mapAssetPath.Length > 0) LoadMap(_mapAssetPath, out _);
    }

    public override void _Process(double delta)
    {
        if (!_previewDirty) return;
        _previewDelay -= delta;
        if (_previewDelay > 0d) return;
        _previewDirty = false;
        RebuildPreview();
    }

    public bool CreateNewMap(
        string id,
        string displayName,
        int columns,
        int rows,
        out string error)
    {
        try
        {
            var asset = War3MapCodec.CreateNew(id, displayName, columns, rows);
            if (!War3MapEditDocument.TryCreate(
                    asset, out _document, out var validation) || _document is null)
            {
                error = validation.Summary;
                return false;
            }
            _mapAssetPath = string.Empty;
            QueuePreviewRebuild(immediate: true);
            NotifyPropertyListChanged();
            error = string.Empty;
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public bool LoadMap(string path, out string error)
    {
        if (!War3MapCodec.TryLoad(path, out var asset, out error) || asset is null)
            return false;
        if (!War3MapEditDocument.TryCreate(
                asset, out _document, out var validation) || _document is null)
        {
            error = validation.Summary;
            return false;
        }
        _mapAssetPath = path;
        QueuePreviewRebuild(immediate: true);
        NotifyPropertyListChanged();
        error = string.Empty;
        return true;
    }

    public bool SaveMap(string path, out string error)
    {
        if (_document is null)
        {
            error = "No map document is open.";
            return false;
        }
        var asset = _document.CaptureAsset();
        if (!War3MapCodec.TrySavePackage(
                asset, path, builtIn: false, out var validation, out error))
            return false;
        if (!War3MapCodec.TryLoad(path, out var reloaded, out error) ||
            reloaded is null ||
            !War3MapCodec.TryExpand(reloaded, out var runtime, out validation) ||
            runtime is null ||
            !runtime.StableHashText.Equals(asset.RuntimeHash,
                StringComparison.OrdinalIgnoreCase))
        {
            error = error.Length > 0
                ? error
                : "Post-save reload hash verification failed.";
            return false;
        }
        _mapAssetPath = path;
        error = $"Saved {runtime.Metadata.DisplayName}; hash={runtime.StableHashText}.";
        return true;
    }

    public War3MapValidationResult ValidateMap()
    {
        if (_document is null)
            return new War3MapValidationResult(
                [new War3MapValidationIssue("no_document", "No map document is open.")]);
        var asset = _document.CaptureAsset();
        War3MapCodec.TryExpand(asset, out _, out var validation);
        return validation;
    }

    public bool BeginStroke(NVector2 point)
    {
        if (_document is null || _painting || !_document.Bounds.Contains(point))
            return false;
        _strokeBefore = _document.CaptureJson();
        _lastStrokePoint = point;
        _strokeSeed = unchecked((int)Time.GetTicksMsec());
        _painting = true;
        ApplyAt(point);
        return true;
    }

    public bool ContinueStroke(NVector2 point)
    {
        if (!_painting || _document is null) return false;
        var distance = NVector2.Distance(_lastStrokePoint, point);
        var spacing = MathF.Max(4f, _document.CellSize * 0.35f);
        var steps = Math.Max(1, (int)MathF.Ceiling(distance / spacing));
        var changed = false;
        for (var step = 1; step <= steps; step++)
        {
            var sample = NVector2.Lerp(
                _lastStrokePoint, point, step / (float)steps);
            changed |= ApplyAt(sample);
        }
        _lastStrokePoint = point;
        return changed;
    }

    public War3MapStrokeResult EndStroke()
    {
        if (!_painting || _document is null || _strokeBefore is null)
            return default;
        var before = _strokeBefore;
        var after = _document.CaptureJson();
        _painting = false;
        _strokeBefore = null;
        if (before != after) CommittedStrokeCount++;
        QueuePreviewRebuild(immediate: true);
        return new War3MapStrokeResult(before != after, before, after);
    }

    public void CancelStroke()
    {
        if (_strokeBefore is not null) RestoreSerialized(_strokeBefore);
        _painting = false;
        _strokeBefore = null;
    }

    public void RestoreSerialized(string json)
    {
        if (!War3MapCodec.TryDeserialize(json, out var asset, out _) || asset is null ||
            !War3MapEditDocument.TryCreate(
                asset, out _document, out _) || _document is null)
            return;
        QueuePreviewRebuild(immediate: true);
    }

    public bool TryPickOnGround(
        Camera3D camera,
        Vector2 screenPosition,
        out NVector2 mapPoint)
    {
        mapPoint = default;
        if (_document is null) return false;
        var origin = camera.ProjectRayOrigin(screenPosition);
        var direction = camera.ProjectRayNormal(screenPosition);
        if (MathF.Abs(direction.Y) <= 0.0001f) return false;
        var distance = -origin.Y / direction.Y;
        if (distance <= 0f) return false;
        var world = origin + direction * distance;
        mapPoint = SimPlane3DTransform.ToSimulation(world);
        return _document.Bounds.Contains(mapPoint);
    }

    private bool ApplyAt(NVector2 point)
    {
        if (_document is null) return false;
        var changed = _document.Apply(point, CurrentBrush(), _strokeSeed) > 0;
        if (changed) QueuePreviewRebuild(immediate: false);
        return changed;
    }

    private War3MapBrush CurrentBrush() => new(
        CurrentTool,
        Math.Clamp(BrushRadiusCells, 0, 32),
        Math.Clamp(BrushStrength, 0.05f, 32f),
        BrushShape,
        (ushort)Math.Clamp(SurfaceId, 0, ushort.MaxValue),
        (byte)Math.Clamp(CliffLevel, 0, TerrainMapSnapshot.MaximumCliffLevel),
        RampDirection,
        PaintEnabled,
        Math.Clamp(OwnerSlot, 0, 2));

    private void QueuePreviewRebuild(bool immediate)
    {
        _previewDirty = true;
        _previewDelay = immediate ? 0d : 0.1d;
    }

    private void RebuildPreview()
    {
        if (_document is null ||
            !_document.TryBuildTerrain(out var terrain, out _) || terrain is null)
            return;
        EnsurePreviewLighting();
        _terrainPreview ??= CreateTerrainPreview();
        _terrainPreview.Initialize(
            terrain,
            new War3TerrainMaterialSet(
                War3TerrainBlendStyle.DualGrid,
                classicCliffMeshesEnabled: true),
            cliffStyleMap: TerrainClassicCliffStyleMap.Uniform(terrain, 1));
        RebuildObjectPreview(terrain);
        PreviewRebuildCount++;
    }

    private void EnsurePreviewLighting()
    {
        if (!PreviewLightingEnabled || HasNode("PreviewSun")) return;
        AddChild(new DirectionalLight3D
        {
            Name = "PreviewSun",
            RotationDegrees = new Vector3(-58f, -32f, 0f),
            LightColor = new Color("fff0cf"),
            LightEnergy = 1.35f,
            ShadowEnabled = true
        });
        AddChild(new WorldEnvironment
        {
            Name = "PreviewEnvironment",
            Environment = new Godot.Environment
            {
                BackgroundMode = Godot.Environment.BGMode.Color,
                BackgroundColor = new Color("22313a"),
                AmbientLightSource = Godot.Environment.AmbientSource.Color,
                AmbientLightColor = new Color("b3c1ca"),
                AmbientLightEnergy = 0.72f,
                TonemapMode = Godot.Environment.ToneMapper.Filmic
            }
        });
    }

    private Rts3DTerrainPresenter CreateTerrainPreview()
    {
        var preview = new Rts3DTerrainPresenter { Name = "TerrainPreview" };
        AddChild(preview);
        return preview;
    }

    private void RebuildObjectPreview(TerrainMapSnapshot terrain)
    {
        _objectPreview?.QueueFree();
        _objectPreview = new Node3D { Name = "MapObjectsPreview" };
        AddChild(_objectPreview);
        foreach (var value in _document!.Objects)
        {
            var color = value.Kind switch
            {
                War3MapObjectKind.SpawnPoint => value.OwnerSlot == 1
                    ? new Color("46d8ff")
                    : new Color("ff5c58"),
                War3MapObjectKind.GoldMine => new Color("ffd34d"),
                War3MapObjectKind.Tree => new Color("4fae69"),
                _ => new Color("b78cff")
            };
            var marker = new MeshInstance3D
            {
                Name = $"{value.Kind}_{value.Id}",
                Mesh = value.Kind == War3MapObjectKind.GoldMine
                    ? new BoxMesh { Size = new Vector3(0.7f, 0.45f, 0.6f) }
                    : new CylinderMesh
                    {
                        TopRadius = value.Kind == War3MapObjectKind.SpawnPoint ? 0.34f : 0.1f,
                        BottomRadius = value.Kind == War3MapObjectKind.SpawnPoint ? 0.34f : 0.16f,
                        Height = value.Kind == War3MapObjectKind.SpawnPoint ? 0.08f : 0.65f
                    },
                MaterialOverride = new StandardMaterial3D
                {
                    AlbedoColor = color,
                    EmissionEnabled = true,
                    Emission = color,
                    EmissionEnergyMultiplier = 0.35f
                }
            };
            var height = SimPlane3DTransform.ToWorldLength(
                terrain.HeightAt(value.Position));
            marker.Position = SimPlane3DTransform.ToWorld(value.Position, height + 0.2f);
            _objectPreview.AddChild(marker);
        }
    }
}

public readonly record struct War3MapStrokeResult(
    bool Changed,
    string BeforeJson,
    string AfterJson);
