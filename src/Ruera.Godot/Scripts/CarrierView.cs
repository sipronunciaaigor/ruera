using System;

using Godot;

namespace Ruera.Renderer;

/// <summary>
/// One carrier (RUE-19 B5, RUE-30 B6 direction): a clickable sphere, parked
/// at the depot until B6 wires tick-by-tick movement along its
/// <c>ExecutedLegs</c>. Selection = emissive highlight on the Mesh child.
/// </summary>
public partial class CarrierView : Area3D
{
    private const float Radius = 6f;

    private static readonly Color BaseColor = new(0.85f, 0.85f, 0.20f);
    private static readonly Color SelectionEmission = new(1.0f, 1.0f, 0.4f);

    private StandardMaterial3D _material = null!;

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

    /// <summary>Selection = emissive highlight on the Mesh child, never a geometry change.</summary>
    public void SetSelected(bool selected)
    {
        _material.EmissionEnabled = selected;
        _material.Emission = SelectionEmission;
        _material.EmissionEnergyMultiplier = selected ? 1.2f : 0f;
    }

    private void OnInputEvent(Node camera, InputEvent @event, Vector3 eventPosition, Vector3 normal, long shapeIdx)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            Clicked?.Invoke(CarrierId);
    }
}
