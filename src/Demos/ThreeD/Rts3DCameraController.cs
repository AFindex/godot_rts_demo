using Godot;
using RtsDemo.Simulation;
using NVector2 = System.Numerics.Vector2;

namespace RtsDemo.Demos.ThreeD;

/// <summary>
/// Reusable camera rig for planar RTS games presented in 3D.
/// It owns presentation input only; command picking remains the demo's responsibility.
/// </summary>
public partial class Rts3DCameraController : Node
{
    [Export(PropertyHint.Range, "4,160,0.5")]
    public float MinimumDistance { get; set; } = 13f;

    [Export(PropertyHint.Range, "4,200,0.5")]
    public float MaximumDistance { get; set; } = 62f;

    [Export(PropertyHint.Range, "4,160,0.5")]
    public float InitialDistance { get; set; } = 30f;

    [Export(PropertyHint.Range, "0,64,1")]
    public float EdgeScrollMargin { get; set; } = 18f;

    [Export(PropertyHint.Range, "0.1,4,0.05")]
    public float KeyboardAndEdgeSpeed { get; set; } = 0.72f;

    [Export(PropertyHint.Range, "0.001,0.1,0.001")]
    public float DragPanSensitivity { get; set; } = 0.018f;

    [Export(PropertyHint.Range, "0.001,0.03,0.001")]
    public float DragRotateSensitivity { get; set; } = 0.009f;

    [Export(PropertyHint.Range, "0.02,0.5,0.01")]
    public float WheelZoomFraction { get; set; } = 0.12f;

    [Export(PropertyHint.Range, "1,30,0.5")]
    public float PositionSmoothing { get; set; } = 13f;

    [Export(PropertyHint.Range, "1,30,0.5")]
    public float ZoomSmoothing { get; set; } = 14f;

    [Export(PropertyHint.Range, "1,30,0.5")]
    public float RotationSmoothing { get; set; } = 15f;

    [Export(PropertyHint.Range, "25,80,1")]
    public float InitialPitchDegrees { get; set; } = 53f;

    public bool IsInitialized => _camera is not null;
    public bool IsAutomationActive => _automationActive;
    public NVector2 Target => _desiredTarget;
    public float Distance => _desiredDistance;
    public float Yaw => _desiredYaw;

    private Camera3D? _camera;
    private SimRect _simulationBounds;
    private NVector2 _currentTarget;
    private NVector2 _desiredTarget;
    private NVector2 _automationTarget;
    private float _currentDistance;
    private float _desiredDistance;
    private float _currentYaw = -0.55f;
    private float _desiredYaw = -0.55f;
    private float _currentPitch;
    private float _desiredPitch;
    private float _automationDistance;
    private float _automationYaw;
    private bool _automationControlsDistance;
    private bool _automationControlsYaw;
    private bool _automationActive;
    private bool _middleMouseDown;
    private Vector2 _pendingPanPixels;
    private Vector2 _pendingRotationPixels;

    /// <summary>
    /// Connects the controller to a camera and defines the legal target area in
    /// simulation coordinates. The camera node may live anywhere in the scene tree.
    /// </summary>
    public void Initialize(Camera3D camera, SimRect simulationBounds)
    {
        ArgumentNullException.ThrowIfNull(camera);
        _camera = camera;
        _simulationBounds = Normalize(simulationBounds);

        var center = (_simulationBounds.Min + _simulationBounds.Max) * 0.5f;
        _currentTarget = center;
        _desiredTarget = center;
        _currentDistance = Math.Clamp(InitialDistance, MinimumDistance, MaximumDistance);
        _desiredDistance = _currentDistance;
        _currentPitch = Mathf.DegToRad(InitialPitchDegrees);
        _desiredPitch = _currentPitch;
        ApplyCameraTransform();
        SetProcess(true);
        SetProcessUnhandledInput(true);
    }

    /// <summary>Moves the camera focus to a simulation position.</summary>
    public void FocusAt(NVector2 simulationPosition, bool immediate = false)
    {
        _desiredTarget = ClampTarget(simulationPosition);
        if (immediate)
        {
            _currentTarget = _desiredTarget;
            ApplyCameraTransform();
        }
    }

    /// <summary>
    /// Gives a recording or smoke-test driver exclusive control over the focus.
    /// Optional distance and yaw values allow authored fly-through shots without
    /// coupling the driver to camera internals.
    /// </summary>
    public void SetAutomationTarget(
        NVector2 simulationPosition,
        float? distance = null,
        float? yaw = null)
    {
        _automationActive = true;
        _automationTarget = ClampTarget(simulationPosition);
        _automationControlsDistance = distance.HasValue;
        _automationControlsYaw = yaw.HasValue;
        _automationDistance = Math.Clamp(
            distance ?? _desiredDistance,
            MinimumDistance,
            MaximumDistance);
        _automationYaw = yaw ?? _desiredYaw;
    }

    /// <summary>Returns camera control to the player without snapping the view.</summary>
    public void ClearAutomationTarget()
    {
        _automationActive = false;
        _automationControlsDistance = false;
        _automationControlsYaw = false;
    }

    public override void _Process(double delta)
    {
        if (_camera is null) return;

        var frameDelta = Math.Clamp((float)delta, 0f, 0.1f);
        if (_automationActive)
        {
            _desiredTarget = _automationTarget;
            if (_automationControlsDistance) _desiredDistance = _automationDistance;
            if (_automationControlsYaw) _desiredYaw = _automationYaw;
        }
        else
        {
            UpdateManualMovement(frameDelta);
            ConsumePointerMotion();
        }

        _desiredTarget = ClampTarget(_desiredTarget);
        _desiredDistance = Math.Clamp(_desiredDistance, MinimumDistance, MaximumDistance);
        _desiredPitch = Math.Clamp(
            _desiredPitch,
            Mathf.DegToRad(32f),
            Mathf.DegToRad(76f));

        var positionWeight = DampWeight(PositionSmoothing, frameDelta);
        var zoomWeight = DampWeight(ZoomSmoothing, frameDelta);
        var rotationWeight = DampWeight(RotationSmoothing, frameDelta);
        _currentTarget = NVector2.Lerp(_currentTarget, _desiredTarget, positionWeight);
        _currentDistance = Mathf.Lerp(_currentDistance, _desiredDistance, zoomWeight);
        _currentYaw = Mathf.LerpAngle(_currentYaw, _desiredYaw, rotationWeight);
        _currentPitch = Mathf.Lerp(_currentPitch, _desiredPitch, rotationWeight);
        ApplyCameraTransform();
    }

    public override void _UnhandledInput(InputEvent inputEvent)
    {
        if (_camera is null || _automationActive) return;

        switch (inputEvent)
        {
            case InputEventMouseButton button when button.ButtonIndex == MouseButton.Middle:
                _middleMouseDown = button.Pressed;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton button when button.Pressed &&
                                                       button.ButtonIndex == MouseButton.WheelUp:
                _desiredDistance *= 1f - WheelZoomFraction;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton button when button.Pressed &&
                                                       button.ButtonIndex == MouseButton.WheelDown:
                _desiredDistance *= 1f + WheelZoomFraction;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseMotion motion when _middleMouseDown:
                if (motion.AltPressed)
                    _pendingRotationPixels += motion.Relative;
                else
                    _pendingPanPixels += motion.Relative;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMagnifyGesture magnify:
                _desiredDistance /= MathF.Max(magnify.Factor, 0.01f);
                GetViewport().SetInputAsHandled();
                break;

            case InputEventPanGesture pan when pan.AltPressed:
                _pendingRotationPixels += pan.Delta * 12f;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventPanGesture pan:
                _pendingPanPixels += pan.Delta * 12f;
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    public override void _Notification(int what)
    {
        if (what == NotificationWMMouseExit)
        {
            // A release outside the window is not guaranteed to reach this node.
            _middleMouseDown = false;
        }
    }

    private void UpdateManualMovement(float delta)
    {
        var forward = GroundForward(_desiredYaw);
        var right = new NVector2(-forward.Y, forward.X);
        var movement = NVector2.Zero;

        if (Input.IsKeyPressed(Key.Up)) movement += forward;
        if (Input.IsKeyPressed(Key.Down)) movement -= forward;
        if (Input.IsKeyPressed(Key.Left)) movement -= right;
        if (Input.IsKeyPressed(Key.Right)) movement += right;

        if (!_middleMouseDown && DisplayServer.WindowIsFocused())
        {
            var viewport = GetViewport();
            var mouse = viewport.GetMousePosition();
            var size = viewport.GetVisibleRect().Size;
            if (mouse.X >= 0f && mouse.Y >= 0f && mouse.X <= size.X && mouse.Y <= size.Y)
            {
                if (mouse.X <= EdgeScrollMargin) movement -= right;
                else if (mouse.X >= size.X - EdgeScrollMargin) movement += right;
                if (mouse.Y <= EdgeScrollMargin) movement += forward;
                else if (mouse.Y >= size.Y - EdgeScrollMargin) movement -= forward;
            }
        }

        if (movement.LengthSquared() <= 0f) return;
        movement = NVector2.Normalize(movement);
        var worldSpeed = _desiredDistance * KeyboardAndEdgeSpeed;
        var simulationSpeed = SimPlane3DTransform.ToSimulationLength(worldSpeed);
        _desiredTarget += movement * simulationSpeed * delta;
    }

    private void ConsumePointerMotion()
    {
        if (_pendingRotationPixels != Vector2.Zero)
        {
            _desiredYaw -= _pendingRotationPixels.X * DragRotateSensitivity;
            _desiredPitch += _pendingRotationPixels.Y * DragRotateSensitivity;
            _pendingRotationPixels = Vector2.Zero;
        }

        if (_pendingPanPixels == Vector2.Zero) return;
        var forward = GroundForward(_desiredYaw);
        var right = new NVector2(-forward.Y, forward.X);
        var scale = SimPlane3DTransform.ToSimulationLength(
            _desiredDistance * DragPanSensitivity);
        _desiredTarget +=
            -right * (_pendingPanPixels.X * scale) +
            forward * (_pendingPanPixels.Y * scale);
        _pendingPanPixels = Vector2.Zero;
    }

    private void ApplyCameraTransform()
    {
        if (_camera is null) return;
        var target = SimPlane3DTransform.ToWorld(_currentTarget);
        var horizontalDistance = MathF.Cos(_currentPitch) * _currentDistance;
        var offset = new Vector3(
            MathF.Sin(_currentYaw) * horizontalDistance,
            MathF.Sin(_currentPitch) * _currentDistance,
            MathF.Cos(_currentYaw) * horizontalDistance);
        _camera.GlobalPosition = target + offset;
        _camera.LookAt(target, Vector3.Up);
    }

    private NVector2 ClampTarget(NVector2 target) => _simulationBounds.Clamp(target);

    private static NVector2 GroundForward(float yaw) =>
        new(-MathF.Sin(yaw), -MathF.Cos(yaw));

    private static float DampWeight(float sharpness, float delta) =>
        1f - MathF.Exp(-MathF.Max(0f, sharpness) * delta);

    private static SimRect Normalize(SimRect bounds) =>
        new(NVector2.Min(bounds.Min, bounds.Max), NVector2.Max(bounds.Min, bounds.Max));
}
