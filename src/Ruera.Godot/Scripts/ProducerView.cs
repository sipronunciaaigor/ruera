using System;
using System.Collections.Generic;

using Godot;

namespace Ruera.Renderer;

/// <summary>
/// One producer (RUE-17 B2, RUE-19 B5): a clickable box beside its edge's
/// midpoint, one colour per archetype, Y scale = clamp(buffer / bufferMax,
/// 0.05, 3), red tint when over the buffer. Selection = emissive highlight
/// on the Mesh child (B5). Pure staging: never mutates <c>Sim.State</c>.
/// </summary>
public partial class ProducerView : Area3D
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
    private static readonly Color SelectionEmission = new(1.0f, 1.0f, 0.4f);

    private MeshInstance3D _mesh = null!;
    private StandardMaterial3D _material = null!;
    private Color _baseColor;

    public int ProducerId { get; private set; }

    public event Action<int>? Clicked;

    public void Setup(int producerId, string archetypeId)
    {
        ProducerId = producerId;
        _baseColor = ArchetypeColors.GetValueOrDefault(archetypeId, Colors.Gray);
        InputRayPickable = true;

        _material = new StandardMaterial3D { AlbedoColor = _baseColor };
        _mesh = new MeshInstance3D
        {
            Name = "Mesh",
            Mesh = new BoxMesh { Size = new Vector3(BoxSize, BoxSize, BoxSize) },
            MaterialOverride = _material,
        };
        AddChild(_mesh);

        // Fixed collision volume spanning the full possible scale range (0.05x-3x)
        // so clicks land regardless of the current buffer-driven height.
        AddChild(new CollisionShape3D
        {
            Shape = new BoxShape3D { Size = new Vector3(BoxSize, BoxSize * 3f, BoxSize) },
            Position = new Vector3(0f, BoxSize * 1.5f, 0f),
        });

        InputEvent += OnInputEvent;
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

    /// <summary>Selection = emissive highlight on the Mesh child (B5), never a geometry change.</summary>
    public void SetSelected(bool selected)
    {
        _material.EmissionEnabled = selected;
        _material.Emission = SelectionEmission;
        _material.EmissionEnergyMultiplier = selected ? 1.2f : 0f;
    }

    private void OnInputEvent(Node camera, InputEvent @event, Vector3 eventPosition, Vector3 normal, long shapeIdx)
    {
        if (@event is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            Clicked?.Invoke(ProducerId);
    }
}
