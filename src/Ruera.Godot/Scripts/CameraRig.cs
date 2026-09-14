using Godot;

namespace Ruera.Renderer;

/// <summary>
/// Two orthographic cameras over the same 3D scene (RUE-17 B2, DESIGN.md §5):
/// an isometric-ish "main" view and a top-down "vista astratta/tattica",
/// toggled with Tab. Mouse wheel zooms (<see cref="Camera3D.Size"/>);
/// left-drag or middle-drag pans the active camera. Left-drag panning
/// (RUE-53) lets fabri pull any map area out from under a HUD panel instead
/// of relying on panel placement to never cover the map — panels can't be
/// guaranteed clear of a small toy map that fills most of the viewport.
/// EdgeView/ProducerView/CarrierView independently tell a left-drag apart
/// from a click (see their own <c>OnInputEvent</c>), so this doesn't fight
/// with clicking an edge/producer/carrier.
/// </summary>
public partial class CameraRig : Node3D
{
    private const float MinSize = 100f;
    private const float MaxSize = 5000f;
    private const float ZoomStep = 50f;

    private Camera3D _main = null!;
    private Camera3D _abstractTop = null!;
    private bool _panning;
    private Vector2 _lastMousePosition;

    public void Setup(Vector3 mapCenter)
    {
        _main = new Camera3D
        {
            Name = "MainCamera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 1000f,
            Far = 5000f,
        };
        AddChild(_main);
        _main.Position = mapCenter + new Vector3(750f, 900f, 750f);
        _main.LookAt(mapCenter, Vector3.Up);
        _main.Current = true;

        _abstractTop = new Camera3D
        {
            Name = "AbstractTopCamera",
            Projection = Camera3D.ProjectionType.Orthogonal,
            Size = 1000f,
            Far = 5000f,
        };
        AddChild(_abstractTop);
        _abstractTop.Position = mapCenter + new Vector3(0f, 1000f, 0f);
        // Straight down: Up is parallel to the look direction, so hint with Forward instead.
        _abstractTop.LookAt(mapCenter, Vector3.Forward);
        _abstractTop.Current = false;
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        switch (@event)
        {
            case InputEventKey { Pressed: true, Keycode: Key.Tab }:
                _main.Current = !_main.Current;
                _abstractTop.Current = !_abstractTop.Current;
                GetViewport().SetInputAsHandled();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.WheelUp, Pressed: true }:
                Zoom(-ZoomStep);
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.WheelDown, Pressed: true }:
                Zoom(ZoomStep);
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left or MouseButton.Middle } panButton:
                _panning = panButton.Pressed;
                _lastMousePosition = panButton.Position;
                break;

            case InputEventMouseMotion motion when _panning:
                Pan(motion.Position);
                break;
        }
    }

    private Camera3D ActiveCamera => _main.Current ? _main : _abstractTop;

    private void Zoom(float delta)
    {
        var camera = ActiveCamera;
        camera.Size = Mathf.Clamp(camera.Size + delta, MinSize, MaxSize);
    }

    private void Pan(Vector2 mousePosition)
    {
        var delta = mousePosition - _lastMousePosition;
        _lastMousePosition = mousePosition;

        var camera = ActiveCamera;
        var basis = camera.GlobalTransform.Basis;
        var metersPerPixel = camera.Size / 1000f; // viewport-independent enough for a toy map
        camera.Position -= ((basis.X * delta.X) - (basis.Y * delta.Y)) * metersPerPixel;
    }
}
