using System.Collections.Generic;

using Godot;

namespace Ruera.Renderer;

/// <summary>
/// One producer (RUE-17, B2): a box beside its edge's midpoint, one colour
/// per archetype, Y scale = clamp(buffer / bufferMax, 0.05, 3), red tint when
/// over the buffer. Pure staging: never mutates <c>Sim.State</c>.
/// </summary>
public partial class ProducerView : Node3D
{
    private const float BoxSize = 20f;

    private static readonly Dictionary<string, Color> ArchetypeColors = new()
    {
        ["base:condo-small"] = new Color(0.30f, 0.60f, 1.00f),
        ["base:condo-large"] = new Color(0.10f, 0.30f, 0.80f),
        ["base:shop"] = new Color(0.90f, 0.70f, 0.10f),
        ["base:factory"] = new Color(0.50f, 0.20f, 0.60f),
    };

    private static readonly Color OverflowColor = new(1.00f, 0.15f, 0.15f);

    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;
    private Color _baseColor;

    public int ProducerId { get; private set; }

    public void Setup(int producerId, string archetypeId)
    {
        ProducerId = producerId;
        _baseColor = ArchetypeColors.GetValueOrDefault(archetypeId, Colors.Gray);

        _material = new StandardMaterial3D { AlbedoColor = _baseColor };
        _mesh = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new BoxMesh { Size = new Vector3(BoxSize, BoxSize, BoxSize) },
            MaterialOverride = _material,
        };
        AddChild(_mesh);
        UpdateBuffer(0, 1);
    }

    /// <summary>Refreshed on every <c>TickResolved</c> (B3) from the live <c>ProducerState</c>.</summary>
    public void UpdateBuffer(long bufferGrams, long bufferMaxGrams)
    {
        var ratio = bufferMaxGrams > 0 ? (float)bufferGrams / bufferMaxGrams : 0f;
        var scale = Mathf.Clamp(ratio, 0.05f, 3f);
        _mesh.Scale = new Vector3(1f, scale, 1f);
        _mesh.Position = new Vector3(0f, BoxSize * scale / 2f, 0f); // grows from the ground up
        _material.AlbedoColor = ratio > 1f ? OverflowColor : _baseColor;
    }
}
