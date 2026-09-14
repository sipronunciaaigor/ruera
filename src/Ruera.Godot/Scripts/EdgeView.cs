using Godot;

using Ruera.Sim.World;

namespace Ruera.Renderer;

/// <summary>
/// One street edge (RUE-17, B2): a clickable box straddling the street,
/// midpoint-positioned then oriented towards the far node once it is in the
/// scene tree (<see cref="Node3D.LookAt"/> requires that, per Godot docs).
/// </summary>
public partial class EdgeView : Area3D
{
    public int EdgeId { get; private set; }

    public void Setup(MapEdge edge, Vector3 fromPosition, Vector3 toPosition)
    {
        EdgeId = edge.Id;
        Position = (fromPosition + toPosition) / 2f;
        InputRayPickable = true;

        var size = new Vector3(8f, 1f, (float)edge.LengthMeters);
        AddChild(new MeshInstance3D { Name = "Mesh", Mesh = new BoxMesh { Size = size } });
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = size } });

        InputEvent += OnInputEvent;
    }

    private void OnInputEvent(Node camera, InputEvent @event, Vector3 eventPosition, Vector3 normal, long shapeIdx)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            GD.Print($"edge {EdgeId}");
    }
}
