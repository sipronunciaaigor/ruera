using System;
using System.Collections.Generic;

using Godot;

using Ruera.Sim;

namespace Ruera.Renderer;

/// <summary>
/// Renders the street graph, depot and producers as primitives (RUE-17, B2,
/// DESIGN.md §5): one Node3D per node/edge/producer, one unit = one metre,
/// map (x, y) -&gt; Godot (x, 0, y). Pure staging of <c>Sim.State</c>; never
/// mutates it (DESIGN.md §2: floats live only in the renderer).
/// </summary>
public partial class WorldView : Node3D
{
    private readonly Dictionary<int, ProducerView> _producerViews = [];

    /// <summary>Map bounding-box centre, in Godot world space — the camera rig frames around this.</summary>
    public Vector3 Center { get; private set; }

    public void Build(Simulation sim)
    {
        var graph = sim.State.Graph ?? throw new InvalidOperationException("WorldView requires a world (street graph).");
        var nodePositions = new Dictionary<int, Vector3>();

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var node in graph.Nodes)
        {
            var position = new Vector3(node.X, 0f, node.Y);
            nodePositions[node.Id] = position;
            minX = Math.Min(minX, position.X);
            maxX = Math.Max(maxX, position.X);
            minZ = Math.Min(minZ, position.Z);
            maxZ = Math.Max(maxZ, position.Z);

            AddChild(new MeshInstance3D
            {
                Name = $"Node{node.Id}",
                Mesh = new SphereMesh { Radius = 4f, Height = 8f },
                Position = position,
            });
        }

        Center = new Vector3((minX + maxX) / 2f, 0f, (minZ + maxZ) / 2f);

        foreach (var edge in graph.Edges)
        {
            var from = nodePositions[edge.From];
            var to = nodePositions[edge.To];

            var edgeView = new EdgeView { Name = $"Edge{edge.Id}" };
            AddChild(edgeView);
            edgeView.Setup(edge, from, to);
            if (!from.IsEqualApprox(to))
                edgeView.LookAt(to, Vector3.Up); // in the tree now: LookAt is safe here
        }

        foreach (var depot in graph.Depots)
        {
            AddChild(new MeshInstance3D
            {
                Name = $"Depot{depot.Id}",
                Mesh = new BoxMesh { Size = new Vector3(40f, 40f, 40f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.40f, 0.25f, 0.10f) },
                Position = nodePositions[depot.Node] + new Vector3(0f, 20f, 0f),
            });
        }

        foreach (var producer in graph.Producers)
        {
            var edge = graph.Edge(producer.Edge);
            var from = nodePositions[edge.From];
            var to = nodePositions[edge.To];
            var midpoint = (from + to) / 2f;
            var direction = from.IsEqualApprox(to) ? Vector3.Forward : (to - from).Normalized();
            var offset = direction.Cross(Vector3.Up) * 15f;

            var producerView = new ProducerView { Name = $"Producer{producer.Id}", Position = midpoint + offset };
            AddChild(producerView);
            producerView.Setup(producer.Id, producer.Archetype);
            _producerViews[producer.Id] = producerView;
        }
    }

    /// <summary>Restages producer heights/colours from the live state (wired to <c>TickResolved</c> in B3).</summary>
    public void Refresh(SimState state)
    {
        foreach (var producer in state.Producers)
        {
            if (_producerViews.TryGetValue(producer.Id, out var view))
                view.UpdateBuffer(producer.BufferGrams, producer.Archetype.BufferGrams);
        }
    }
}
