#if TOOLS
using Godot;
using RtsDemo.GodotRuntime;

namespace RtsDemo.Editor;

[Tool]
public partial class RtsTerrainEditorPlugin : EditorPlugin
{
    private RtsTerrainAuthoring2D? _active;
    private EditorDock? _dock;
    private VBoxContainer? _dockContent;
    private Label? _status;
    private bool _painting;

    public override void _EnterTree()
    {
        _dock = new EditorDock
        {
            Title = "RTS Terrain",
            DefaultSlot = EditorDock.DockSlot.RightUl
        };
        _dockContent = new VBoxContainer { Name = "RTS Terrain Content" };
        _dock.AddChild(_dockContent);
        var title = new Label { Text = "RTS TERRAIN AUTHORING" };
        title.AddThemeFontSizeOverride("font_size", 16);
        _dockContent.AddChild(title);
        _dockContent.AddChild(new Label
        {
            Text = "Select RtsTerrainAuthoring2D. Configure brush and overlays " +
                   "in Inspector, then drag LMB in the 2D viewport."
        });
        var resize = new Button { Text = "Initialize / Resize Cell Arrays" };
        resize.Pressed += ResizePressed;
        _dockContent.AddChild(resize);
        var validate = new Button { Text = "Validate Terrain" };
        validate.Pressed += ValidatePressed;
        _dockContent.AddChild(validate);
        var export = new Button { Text = "Validate + Export Runtime .tres" };
        export.Pressed += ExportPressed;
        _dockContent.AddChild(export);
        _status = new Label
        {
            Text = "No authoring node selected.",
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        };
        _dockContent.AddChild(_status);
        AddDock(_dock);
    }

    public override void _ExitTree()
    {
        if (_dock is not null)
        {
            RemoveDock(_dock);
            _dock.QueueFree();
        }
        _dock = null;
        _dockContent = null;
        _status = null;
        _active = null;
    }

    public override bool _Handles(GodotObject @object) =>
        @object is RtsTerrainAuthoring2D;

    public override void _Edit(GodotObject @object)
    {
        _active = @object as RtsTerrainAuthoring2D;
        UpdateStatus(_active is null
            ? "No authoring node selected."
            : "Ready. LMB drag paints; Ctrl+Z uses editor undo history.");
    }

    public override void _MakeVisible(bool visible)
    {
        if (_dock is not null) _dock.Visible = visible;
        if (!visible && _painting)
        {
            _active?.CancelStroke();
            _painting = false;
        }
    }

    public override bool _ForwardCanvasGuiInput(InputEvent @event)
    {
        if (_active is null || !_active.IsInsideTree()) return false;
        if (@event is InputEventMouseButton button &&
            button.ButtonIndex == MouseButton.Left)
        {
            if (button.Pressed)
            {
                _painting = _active.BeginStroke(button.Position);
                return _painting;
            }
            if (_painting)
            {
                var result = _active.EndStroke();
                _painting = false;
                if (result.Changed)
                    RegisterUndo(result);
                return true;
            }
        }
        if (_painting && @event is InputEventMouseMotion motion &&
            (motion.ButtonMask & MouseButtonMask.Left) != 0)
        {
            _active.ContinueStroke(motion.Position);
            return true;
        }
        if (_painting && @event is InputEventKey key && key.Pressed &&
            key.Keycode == Key.Escape)
        {
            _active.CancelStroke();
            _painting = false;
            return true;
        }
        return false;
    }

    private void RegisterUndo(TerrainAuthoringStrokeResult stroke)
    {
        if (_active is null) return;
        var undo = GetUndoRedo();
        undo.CreateAction(
            "Paint RTS Terrain", UndoRedo.MergeMode.Disable, _active);
        undo.AddDoMethod(
            _active,
            RtsTerrainAuthoring2D.MethodName.RestoreCellPayload,
            Variant.From(stroke.AfterPayload));
        undo.AddUndoMethod(
            _active,
            RtsTerrainAuthoring2D.MethodName.RestoreCellPayload,
            Variant.From(stroke.BeforePayload));
        undo.CommitAction(execute: false);
    }

    private void ValidatePressed()
    {
        if (_active is null)
        {
            UpdateStatus("Select an RtsTerrainAuthoring2D node first.");
            return;
        }
        var validation = _active.ValidateAuthoring();
        UpdateStatus(validation.IsValid
            ? "PASS: authoring data is exportable."
            : string.Join("\n", validation.Issues.Take(8).Select(value =>
                $"{value.Code}: {value.Message}")));
    }

    private void ResizePressed()
    {
        if (_active is null)
        {
            UpdateStatus("Select an RtsTerrainAuthoring2D node first.");
            return;
        }
        var result = _active.ResizeAuthoringGrid();
        UpdateStatus(
            $"Resized {result.OldColumns}x{result.OldRows} → " +
            $"{result.NewColumns}x{result.NewRows}; " +
            $"preserved {result.CopiedCells} cells. Validate before export.");
    }

    private void ExportPressed()
    {
        if (_active is null)
        {
            UpdateStatus("Select an RtsTerrainAuthoring2D node first.");
            return;
        }
        var result = _active.ExportRuntimeResource();
        UpdateStatus($"{(result.Succeeded ? "PASS" : "FAIL")}: " +
                     result.Summary);
        if (result.Succeeded) EditorInterface.Singleton.GetResourceFilesystem().Scan();
    }

    private void UpdateStatus(string text)
    {
        if (_status is not null) _status.Text = text;
    }
}
#endif
