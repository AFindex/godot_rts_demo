#if TOOLS
using Godot;
using RtsDemo.GodotRuntime;
using RtsDemo.Simulation;
using War3Rts.Maps;

namespace RtsDemo.Editor;

[Tool]
public partial class RtsTerrainEditorPlugin : EditorPlugin
{
    private RtsTerrainAuthoring2D? _legacyActive;
    private War3MapAuthoring3D? _mapActive;
    private EditorDock? _dock;
    private VBoxContainer? _dockContent;
    private Label? _currentState;
    private Label? _status;
    private OptionButton? _tool;
    private OptionButton? _shape;
    private SpinBox? _radius;
    private SpinBox? _strength;
    private SpinBox? _value;
    private FileDialog? _openDialog;
    private FileDialog? _saveDialog;
    private bool _painting;

    public override void _EnterTree()
    {
        AddCustomType(
            "War3MapAuthoring3D",
            "Node3D",
            GD.Load<Script>("res://src/War3Rts/Maps/War3MapAuthoring3D.cs"),
            null);
        BuildDock();
        BuildFileDialogs();
        if (OS.GetCmdlineUserArgs().Contains("--war3-map-editor-smoke"))
            Callable.From(RunEditorSmoke).CallDeferred();
        else if (OS.GetCmdlineUserArgs().Contains("--war3-map-editor-capture"))
            Callable.From(StartEditorCapture).CallDeferred();
    }

    public override void _ExitTree()
    {
        RemoveCustomType("War3MapAuthoring3D");
        if (_dock is not null)
        {
            RemoveDock(_dock);
            _dock.QueueFree();
        }
        _openDialog?.QueueFree();
        _saveDialog?.QueueFree();
        _dock = null;
        _dockContent = null;
        _status = null;
        _currentState = null;
        _legacyActive = null;
        _mapActive = null;
    }

    public override bool _Handles(GodotObject @object) =>
        @object is RtsTerrainAuthoring2D or War3MapAuthoring3D;

    public override void _Edit(GodotObject @object)
    {
        _legacyActive = @object as RtsTerrainAuthoring2D;
        _mapActive = @object as War3MapAuthoring3D;
        SyncControlsFromActive();
        UpdateStatus(_mapActive is not null
            ? $"Opened {_mapActive.CurrentMapName}. Paint in the 3D viewport."
            : _legacyActive is not null
                ? "Legacy terrain node selected. LMB drag paints in 2D."
                : "Create or open a War3 map package.");
    }

    public override void _MakeVisible(bool visible)
    {
        if (_dock is not null) _dock.Visible = visible;
        if (!visible && _painting) CancelPainting();
    }

    public override bool _ForwardCanvasGuiInput(InputEvent @event)
    {
        if (_legacyActive is null || !_legacyActive.IsInsideTree()) return false;
        if (@event is InputEventMouseButton button &&
            button.ButtonIndex == MouseButton.Left)
        {
            if (button.Pressed)
            {
                _painting = _legacyActive.BeginStroke(button.Position);
                return _painting;
            }
            if (_painting)
            {
                var result = _legacyActive.EndStroke();
                _painting = false;
                if (result.Changed) RegisterLegacyUndo(result);
                return true;
            }
        }
        if (_painting && @event is InputEventMouseMotion motion &&
            (motion.ButtonMask & MouseButtonMask.Left) != 0)
        {
            _legacyActive.ContinueStroke(motion.Position);
            return true;
        }
        if (_painting && @event is InputEventKey key && key.Pressed &&
            key.Keycode == Key.Escape)
        {
            CancelPainting();
            return true;
        }
        return false;
    }

    public override int _Forward3DGuiInput(
        Camera3D viewportCamera,
        InputEvent @event)
    {
        if (_mapActive is null || !_mapActive.IsInsideTree())
            return (int)EditorPlugin.AfterGuiInput.Pass;
        if (@event is InputEventMouseButton button &&
            button.ButtonIndex == MouseButton.Left &&
            _mapActive.TryPickOnGround(
                viewportCamera, button.Position, out var point))
        {
            if (button.Pressed)
            {
                _painting = _mapActive.BeginStroke(point);
                return _painting
                    ? (int)EditorPlugin.AfterGuiInput.Stop
                    : (int)EditorPlugin.AfterGuiInput.Pass;
            }
            if (_painting)
            {
                var result = _mapActive.EndStroke();
                _painting = false;
                if (result.Changed) RegisterMapUndo(result);
                return (int)EditorPlugin.AfterGuiInput.Stop;
            }
        }
        if (_painting && @event is InputEventMouseMotion motion &&
            (motion.ButtonMask & MouseButtonMask.Left) != 0 &&
            _mapActive.TryPickOnGround(
                viewportCamera, motion.Position, out var motionPoint))
        {
            _mapActive.ContinueStroke(motionPoint);
            return (int)EditorPlugin.AfterGuiInput.Stop;
        }
        if (_painting && @event is InputEventKey key && key.Pressed &&
            key.Keycode == Key.Escape)
        {
            CancelPainting();
            return (int)EditorPlugin.AfterGuiInput.Stop;
        }
        return (int)EditorPlugin.AfterGuiInput.Pass;
    }

    private void BuildDock()
    {
        _dock = new EditorDock
        {
            Title = "War3 Map Editor",
            DefaultSlot = EditorDock.DockSlot.RightUl
        };
        var scroll = new ScrollContainer
        {
            Name = "War3 Map Editor Scroll",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto
        };
        _dock.AddChild(scroll);
        _dockContent = new VBoxContainer
        {
            Name = "War3 Map Editor Content",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        scroll.AddChild(_dockContent);
        var title = new Label { Text = "WAR3 MAP AUTHORING" };
        title.AddThemeFontSizeOverride("font_size", 16);
        _dockContent.AddChild(title);
        _dockContent.AddChild(new Label
        {
            Text = "Versioned map packages · 3D War3 preview · one UndoRedo action per stroke",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
        var fileRow = new HBoxContainer();
        _dockContent.AddChild(fileRow);
        AddButton(fileRow, "New", NewPressed);
        AddButton(fileRow, "Open", () => _openDialog?.PopupCenteredRatio(0.7f));
        AddButton(fileRow, "Save", SavePressed);
        AddButton(fileRow, "Save As", () => _saveDialog?.PopupCenteredRatio(0.7f));

        _currentState = new Label
        {
            Text = "Current tool: none",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _currentState.AddThemeColorOverride("font_color", new Color("ffd66b"));
        _dockContent.AddChild(_currentState);

        _tool = EnumOption<War3MapTool>("Tool", ToolChanged);
        _shape = EnumOption<War3BrushShape>("Shape", ShapeChanged);
        _radius = Spin("Radius (cells)", 0, 32, 1, 2, value =>
        {
            if (_mapActive is not null) _mapActive.BrushRadiusCells = (int)value;
            UpdateCurrentState();
        });
        _strength = Spin("Strength", 0.05, 32, 0.05, 2, value =>
        {
            if (_mapActive is not null) _mapActive.BrushStrength = (float)value;
            UpdateCurrentState();
        });
        _value = Spin("Tool value", 0, 15, 1, 0, value =>
        {
            if (_mapActive is null) return;
            _mapActive.SurfaceId = (int)value;
            _mapActive.CliffLevel = (int)value;
            _mapActive.OwnerSlot = (int)Math.Clamp(value, 0, 2);
            UpdateCurrentState();
        });

        var directionRow = new HBoxContainer();
        directionRow.AddChild(new Label { Text = "Ramp direction", CustomMinimumSize = new Vector2(110, 0) });
        var direction = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var name in Enum.GetNames<TerrainRampDirection>()) direction.AddItem(name);
        direction.Selected = (int)TerrainRampDirection.PositiveX;
        direction.ItemSelected += index =>
        {
            if (_mapActive is not null)
                _mapActive.RampDirection = (TerrainRampDirection)index;
            UpdateCurrentState();
        };
        directionRow.AddChild(direction);
        _dockContent.AddChild(directionRow);

        var actionRow = new HBoxContainer();
        _dockContent.AddChild(actionRow);
        AddButton(actionRow, "Validate", ValidatePressed);
        AddButton(actionRow, "Validate + Runtime Export", SavePressed);
        _status = new Label
        {
            Text = "Create or open a map package.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _dockContent.AddChild(_status);
        _dockContent.AddChild(new HSeparator());
        _dockContent.AddChild(new Label
        {
            Text = "Compatibility: selecting RtsTerrainAuthoring2D keeps the previous 2D terrain workflow available.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
        var legacyRow = new HBoxContainer();
        _dockContent.AddChild(legacyRow);
        AddButton(legacyRow, "Resize legacy grid", ResizeLegacyPressed);
        AddButton(legacyRow, "Export legacy .tres", ExportLegacyPressed);
        AddDock(_dock);
    }

    private void BuildFileDialogs()
    {
        _openDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Access = FileDialog.AccessEnum.Resources,
            Title = "Open War3 Map Asset",
            Filters = new[] { "*.w3map.json ; War3 Map", "*.json ; JSON" }
        };
        _openDialog.FileSelected += OpenSelected;
        AddChild(_openDialog);
        _saveDialog = new FileDialog
        {
            FileMode = FileDialog.FileModeEnum.SaveFile,
            Access = FileDialog.AccessEnum.Resources,
            Title = "Save War3 Map Asset",
            CurrentFile = "map.w3map.json",
            Filters = new[] { "*.w3map.json ; War3 Map" }
        };
        _saveDialog.FileSelected += SaveSelected;
        AddChild(_saveDialog);
    }

    private void NewPressed()
    {
        var node = EnsureMapNode();
        if (node is null) return;
        var index = Time.GetTicksMsec() % 100000;
        if (!node.CreateNewMap(
                $"new_battlefield_{index}",
                $"New Battlefield {index}",
                128, 96, out var error))
        {
            UpdateStatus($"FAIL: {error}");
            return;
        }
        _mapActive = node;
        _legacyActive = null;
        SyncControlsFromActive();
        UpdateStatus("New 128×96 map created. Add owned gold/trees, validate, then Save As.");
    }

    private void OpenSelected(string path)
    {
        var node = EnsureMapNode();
        if (node is null) return;
        if (!node.LoadMap(path, out var error))
        {
            UpdateStatus($"FAIL: {error}");
            return;
        }
        _mapActive = node;
        _legacyActive = null;
        SyncControlsFromActive();
        UpdateStatus($"Opened {node.CurrentMapName} from {path}.");
    }

    private void SavePressed()
    {
        if (_mapActive is null)
        {
            UpdateStatus("Select or create a War3MapAuthoring3D node first.");
            return;
        }
        if (_mapActive.MapAssetPath.Length == 0)
        {
            _saveDialog?.PopupCenteredRatio(0.7f);
            return;
        }
        SaveSelected(_mapActive.MapAssetPath);
    }

    private void SaveSelected(string path)
    {
        if (_mapActive is null) return;
        var validation = _mapActive.ValidateMap();
        if (!validation.IsValid)
        {
            UpdateStatus("EXPORT BLOCKED\n" + FormatValidation(validation));
            return;
        }
        if (!_mapActive.SaveMap(path, out var result))
        {
            UpdateStatus($"FAIL: {result}");
            return;
        }
        EditorInterface.Singleton.GetResourceFilesystem().Scan();
        UpdateStatus("PASS: " + result);
    }

    private void ValidatePressed()
    {
        if (_mapActive is not null)
        {
            var validation = _mapActive.ValidateMap();
            UpdateStatus(validation.IsValid
                ? "PASS: terrain, ramps, spawns, resource exclusions and path connectivity are valid."
                : FormatValidation(validation));
            return;
        }
        if (_legacyActive is null)
        {
            UpdateStatus("Select an authoring node first.");
            return;
        }
        var legacy = _legacyActive.ValidateAuthoring();
        UpdateStatus(legacy.IsValid
            ? "PASS: legacy terrain is exportable."
            : string.Join("\n", legacy.Issues.Take(8).Select(value =>
                $"{value.Code} [{value.Column},{value.Row}]: {value.Message}")));
    }

    private War3MapAuthoring3D? EnsureMapNode()
    {
        if (_mapActive is not null && _mapActive.IsInsideTree()) return _mapActive;
        var root = EditorInterface.Singleton.GetEditedSceneRoot();
        if (root is null)
        {
            UpdateStatus("Open or create a 3D scene before creating a map editor node.");
            return null;
        }
        var node = new War3MapAuthoring3D { Name = "War3MapAuthoring3D" };
        root.AddChild(node);
        node.Owner = root;
        EditorInterface.Singleton.GetSelection().Clear();
        EditorInterface.Singleton.GetSelection().AddNode(node);
        EditorInterface.Singleton.EditNode(node);
        return node;
    }

    private void RegisterMapUndo(War3MapStrokeResult stroke)
    {
        if (_mapActive is null) return;
        var undo = GetUndoRedo();
        undo.CreateAction(
            $"War3 Map: {_mapActive.CurrentTool}",
            UndoRedo.MergeMode.Disable,
            _mapActive);
        undo.AddDoMethod(
            _mapActive,
            War3MapAuthoring3D.MethodName.RestoreSerialized,
            Variant.From(stroke.AfterJson));
        undo.AddUndoMethod(
            _mapActive,
            War3MapAuthoring3D.MethodName.RestoreSerialized,
            Variant.From(stroke.BeforeJson));
        undo.CommitAction(execute: false);
        UpdateStatus($"Stroke committed as one UndoRedo action: {_mapActive.CurrentTool}.");
    }

    private void RegisterLegacyUndo(TerrainAuthoringStrokeResult stroke)
    {
        if (_legacyActive is null) return;
        var undo = GetUndoRedo();
        undo.CreateAction("Paint RTS Terrain", UndoRedo.MergeMode.Disable, _legacyActive);
        undo.AddDoMethod(
            _legacyActive,
            RtsTerrainAuthoring2D.MethodName.RestoreCellPayload,
            Variant.From(stroke.AfterPayload));
        undo.AddUndoMethod(
            _legacyActive,
            RtsTerrainAuthoring2D.MethodName.RestoreCellPayload,
            Variant.From(stroke.BeforePayload));
        undo.CommitAction(execute: false);
    }

    private void CancelPainting()
    {
        _mapActive?.CancelStroke();
        _legacyActive?.CancelStroke();
        _painting = false;
    }

    private void ResizeLegacyPressed()
    {
        if (_legacyActive is null)
        {
            UpdateStatus("Select an RtsTerrainAuthoring2D node first.");
            return;
        }
        var result = _legacyActive.ResizeAuthoringGrid();
        UpdateStatus($"Resized {result.OldColumns}x{result.OldRows} → " +
                     $"{result.NewColumns}x{result.NewRows}; preserved " +
                     $"{result.CopiedCells} cells.");
    }

    private void ExportLegacyPressed()
    {
        if (_legacyActive is null)
        {
            UpdateStatus("Select an RtsTerrainAuthoring2D node first.");
            return;
        }
        var result = _legacyActive.ExportRuntimeResource();
        UpdateStatus($"{(result.Succeeded ? "PASS" : "FAIL")}: {result.Summary}");
        if (result.Succeeded) EditorInterface.Singleton.GetResourceFilesystem().Scan();
    }

    private OptionButton EnumOption<T>(string label, Action<long> changed)
        where T : struct, Enum
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) });
        var option = new OptionButton { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        foreach (var name in Enum.GetNames<T>()) option.AddItem(name);
        option.ItemSelected += index => changed(index);
        row.AddChild(option);
        _dockContent!.AddChild(row);
        return option;
    }

    private SpinBox Spin(
        string label,
        double minimum,
        double maximum,
        double step,
        double initial,
        Action<double> changed)
    {
        var row = new HBoxContainer();
        row.AddChild(new Label { Text = label, CustomMinimumSize = new Vector2(110, 0) });
        var spin = new SpinBox
        {
            MinValue = minimum,
            MaxValue = maximum,
            Step = step,
            Value = initial,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        spin.ValueChanged += value => changed(value);
        row.AddChild(spin);
        _dockContent!.AddChild(row);
        return spin;
    }

    private static void AddButton(Control parent, string text, Action pressed)
    {
        var button = new Button { Text = text };
        button.Pressed += pressed;
        parent.AddChild(button);
    }

    private void ToolChanged(long index)
    {
        if (_mapActive is not null) _mapActive.CurrentTool = (War3MapTool)index;
        UpdateCurrentState();
    }

    private void ShapeChanged(long index)
    {
        if (_mapActive is not null) _mapActive.BrushShape = (War3BrushShape)index;
        UpdateCurrentState();
    }

    private void SyncControlsFromActive()
    {
        if (_mapActive is not null)
        {
            if (_tool is not null) _tool.Selected = (int)_mapActive.CurrentTool;
            if (_shape is not null) _shape.Selected = (int)_mapActive.BrushShape;
            if (_radius is not null) _radius.Value = _mapActive.BrushRadiusCells;
            if (_strength is not null) _strength.Value = _mapActive.BrushStrength;
        }
        UpdateCurrentState();
    }

    private void UpdateCurrentState()
    {
        if (_currentState is null) return;
        _currentState.Text = _mapActive is null
            ? "Current tool: no War3 map selected"
            : $"Current tool: {_mapActive.CurrentTool} · " +
              $"{_mapActive.BrushShape} r={_mapActive.BrushRadiusCells} " +
              $"strength={_mapActive.BrushStrength:0.##} · value={_value?.Value ?? 0}";
    }

    private static string FormatValidation(War3MapValidationResult validation) =>
        string.Join("\n", validation.Issues.Take(12).Select(value =>
            $"{value.Code} cell=[{value.Column},{value.Row}] object={value.ObjectId}: " +
            value.Message));

    private void UpdateStatus(string text)
    {
        if (_status is not null) _status.Text = text;
    }

    private void RunEditorSmoke()
    {
        var node = new War3MapAuthoring3D { Name = "War3MapEditorSmoke" };
        AddChild(node);
        var created = node.CreateNewMap(
            "editor_smoke", "Editor Smoke", 32, 24, out var error);
        _mapActive = node;
        var before = node.CaptureSerialized();
        node.CurrentTool = War3MapTool.RaiseHeight;
        node.BrushRadiusCells = 3;
        node.BrushStrength = 4f;
        var began = created && node.BeginStroke(new System.Numerics.Vector2(400f, 320f));
        var continued = began && node.ContinueStroke(
            new System.Numerics.Vector2(560f, 384f));
        var stroke = began ? node.EndStroke() : default;
        if (stroke.Changed) RegisterMapUndo(stroke);
        var after = node.CaptureSerialized();
        var undoManager = GetUndoRedo();
        var historyId = undoManager.GetObjectHistoryId(node);
        var history = undoManager.GetHistoryUndoRedo(historyId);
        var undone = stroke.Changed && history is not null && history.Undo() &&
                     node.CaptureSerialized() == before;
        var redone = undone && history is not null && history.Redo() &&
                     node.CaptureSerialized() == after;
        var valid = node.ValidateMap().IsValid;
        var saved = valid && node.SaveMap(
            "user://war3_map_editor_smoke/editor_smoke/map.w3map.json",
            out error);
        var reopened = saved && War3MapCodec.TryLoad(
            "user://war3_map_editor_smoke/editor_smoke/map.w3map.json",
            out var reopenedAsset, out error) && reopenedAsset is not null &&
            War3MapCodec.TryExpand(reopenedAsset, out var reopenedRuntime,
                out var reopenedValidation) && reopenedRuntime is not null &&
            reopenedValidation.IsValid;
        var success = created && began && continued && stroke.Changed &&
                      node.CommittedStrokeCount == 1 && undone && redone &&
                      valid && saved && reopened;
        GD.Print($"WAR3_MAP_EDITOR_SMOKE success={success} created={created} " +
                 $"stroke={stroke.Changed}/count{node.CommittedStrokeCount} " +
                 $"undo={undone} redo={redone} valid={valid} " +
                 $"saved={saved} reopened={reopened} error={error}");
        GetTree().Quit(success ? 0 : 1);
    }

    private async Task RunEditorCaptureAsync()
    {
        DisplayServer.WindowSetMode(DisplayServer.WindowMode.Windowed);
        DisplayServer.WindowSetSize(new Vector2I(1280, 720));
        EditorInterface.Singleton.OpenSceneFromPath(
            "res://war3_rts/War3MapEditor.tscn");
        for (var index = 0; index < 20; index++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var root = EditorInterface.Singleton.GetEditedSceneRoot();
        var node = root?.FindChild(
            "War3MapAuthoring3D", recursive: true, owned: false) as
            War3MapAuthoring3D;
        if (node is null && root is not null)
        {
            node = new War3MapAuthoring3D { Name = "War3MapAuthoring3D" };
            root.AddChild(node);
            node.Owner = root;
            node.LoadMap(
                "res://war3_rts/maps/lordaeron_crossroads/map.w3map.json",
                out _);
        }
        if (node is null)
        {
            GD.Print("WAR3_MAP_EDITOR_CAPTURE success=False error=missing_node");
            GetTree().Quit(1);
            return;
        }
        _mapActive = node;
        EditorInterface.Singleton.GetSelection().Clear();
        EditorInterface.Singleton.GetSelection().AddNode(node);
        EditorInterface.Singleton.EditNode(node);
        EditorInterface.Singleton.SetMainScreenEditor("3D");
        SyncControlsFromActive();
        UpdateStatus("Visual QA capture · built-in map · 3D dual-grid/cliff/ramp/object preview");
        var focus = new InputEventKey
        {
            Keycode = Key.F,
            PhysicalKeycode = Key.F,
            Pressed = true
        };
        Input.ParseInputEvent(focus);
        var editorCamera = EditorInterface.Singleton
            .GetEditorViewport3D(0).GetCamera3D();
        editorCamera.GlobalPosition = new Vector3(80f, 92f, 118f);
        editorCamera.LookAt(new Vector3(80f, 0f, 48f));
        for (var index = 0; index < 40; index++)
            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
        var image = EditorInterface.Singleton.GetBaseControl()
            .GetViewport().GetTexture().GetImage();
        const string path = "user://war3_map_editor_capture.png";
        var result = image.SavePng(path);
        GD.Print($"WAR3_MAP_EDITOR_CAPTURE success={result == Error.Ok} " +
                 $"path={ProjectSettings.GlobalizePath(path)} size={image.GetSize()}");
        GetTree().Quit(result == Error.Ok ? 0 : 1);
    }

    private void StartEditorCapture() => _ = RunEditorCaptureAsync();
}
#endif
