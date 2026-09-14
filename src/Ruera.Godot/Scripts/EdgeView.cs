using System;

using Godot;

using Ruera.Sim.World;

namespace Ruera.Renderer;

/// <summary>
/// One street edge (RUE-17, B2/B4/B7): a clickable box straddling the
/// street, midpoint-positioned then oriented towards the far node once it
/// is in the scene tree (<see cref="Node3D.LookAt"/> requires that, per
/// Godot docs). The collision volume is deliberately much larger than the
/// thin visual mesh (RUE-52/B7) so clicking is forgiving. Left-click raises
/// <see cref="Clicked"/> for the painter (B4) and still prints the id for
/// quick debugging.
/// </summary>
public partial class EdgeView : Area3D
{
    private static readonly Color DefaultColor = new(0.55f, 0.55f, 0.55f);

    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;

    public int EdgeId { get; private set; }

    public event Action<int>? Clicked;

    public void Setup(MapEdge edge, Vector3 fromPosition, Vector3 toPosition)
    {
        EdgeId = edge.Id;
        Position = (fromPosition + toPosition) / 2f;
        InputRayPickable = true;

        var visualSize = new Vector3(8f, 1f, (float)edge.LengthMeters);
        _material = new StandardMaterial3D { AlbedoColor = DefaultColor };
        _mesh = new MeshInstance3D { Name = "Mesh", Mesh = new BoxMesh { Size = visualSize }, MaterialOverride = _material };
        AddChild(_mesh);

        // Clickable volume much wider/taller than the thin visual box (fabri's
        // playtest: painting required a very precise click) -- same length,
        // so it still tracks the street rather than overlapping neighbours.
        var collisionSize = new Vector3(24f, 16f, (float)edge.LengthMeters);
        AddChild(new CollisionShape3D { Shape = new BoxShape3D { Size = collisionSize } });

        InputEvent += OnInputEvent;
    }

    /// <summary>Painting highlight (B4): null reverts to the default street colour.</summary>
    public void SetHighlight(Color? color) => _material.AlbedoColor = color ?? DefaultColor;

    private void OnInputEvent(Node camera, InputEvent @event, Vector3 eventPosition, Vector3 normal, long shapeIdx)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
        {
            GD.Print($"edge {EdgeId}");
            Clicked?.Invoke(EdgeId);
        }
    }
}
