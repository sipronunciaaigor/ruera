using System;
using System.Collections.Generic;

using Godot;

namespace Ruera.Renderer;

/// <summary>
/// One carrier (RUE-19 B5, RUE-30 B6): a clickable sphere, parked at the
/// depot and tweened along its route on every <c>TickResolved</c>
/// (WorldView.PlayCarrierRoutes). Selection = emissive highlight on the Mesh
/// child. Left-click raises <see cref="Clicked"/> only when the mouse barely
/// moved between press and release (RUE-53): <see cref="CameraRig"/> also
/// pans on left-drag, so a drag that starts on a carrier must not also
/// select it.
/// </summary>
public partial class CarrierView : Area3D
{
    private const float Radius = 6f;
    private const float ClickDragTolerancePx = 6f;

    private static readonly Color BaseColor = new(0.85f, 0.85f, 0.20f);
    private static readonly Color SelectionEmission = new(1.0f, 1.0f, 0.4f);

    private StandardMaterial3D _material = null!;
    private Tween? _routeTween;
    private bool _pressedHere;
    private Vector2 _pressPosition;

    public int CarrierId { get; private set; }

    public event Action<int>? Clicked;

    public void Setup(int carrierId, Vector3 depotPosition)
    {
        CarrierId = carrierId;
        Position = depotPosition + new Vector3(0f, Radius, 0f);
        InputRayPickable = true;

        _material = new StandardMaterial3D { AlbedoColor = BaseColor };
        AddChild(new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new SphereMesh { Radius = Radius, Height = Radius * 2f },
            MaterialOverride = _material,
        });
        AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = Radius } });

        InputEvent += OnInputEvent;
    }

    /// <summary>
    /// Tweens through a ground-level waypoint sequence (the Radius lift is
    /// added here), equal time per hop, over the whole tick's real duration.
    /// Cancels any tween already in flight — a carrier never chases two
    /// routes at once.
    /// </summary>
    public void PlayRoute(IReadOnlyList<Vector3> groundWaypoints, double durationSeconds)
    {
        if (groundWaypoints.Count < 2 || durationSeconds <= 0)
            return;

        if (_routeTween is { } running && running.IsValid())
            running.Kill();

        var lift = new Vector3(0f, Radius, 0f);
        var perHop = durationSeconds / (groundWaypoints.Count - 1);

        _routeTween = CreateTween();
        for (var i = 1; i < groundWaypoints.Count; i++)
            _routeTween.TweenProperty(this, "position", groundWaypoints[i] + lift, perHop);
    }

    /// <summary>Selection = emissive highlight on the Mesh child, never a geometry change.</summary>
    public void SetSelected(bool selected)
    {
        _material.EmissionEnabled = selected;
        _material.Emission = SelectionEmission;
        _material.EmissionEnergyMultiplier = selected ? 1.2f : 0f;
    }

    private void OnInputEvent(Node camera, InputEvent @event, Vector3 eventPosition, Vector3 normal, long shapeIdx)
    {
        switch (@event)
        {
            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true }:
                _pressedHere = true;
                _pressPosition = GetViewport().GetMousePosition();
                break;

            case InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: false } when _pressedHere:
                _pressedHere = false;
                if (GetViewport().GetMousePosition().DistanceTo(_pressPosition) <= ClickDragTolerancePx)
                    Clicked?.Invoke(CarrierId);
                break;

            case InputEventMouseMotion when _pressedHere
                && GetViewport().GetMousePosition().DistanceTo(_pressPosition) > ClickDragTolerancePx:
                _pressedHere = false; // turned into a camera-pan drag (RUE-53), not a click
                break;
        }
    }
}
