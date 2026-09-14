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
    private readonly Dictionary<int, EdgeView> _edgeViews = [];
    private readonly Dictionary<int, CarrierView> _carrierViews = [];
    private readonly Dictionary<int, Vector3> _nodePositions = [];
    private Node3D _arrows = null!;
    private Vector3 _depotPosition;

    /// <summary>Map bounding-box centre, in Godot world space — the camera rig frames around this.</summary>
    public Vector3 Center { get; private set; }

    /// <summary>Raised when a street edge is left-clicked (B4: the painter listens for this).</summary>
    public event Action<int>? EdgeClicked;

    /// <summary>Raised when a producer box is left-clicked (B5: the inspector listens for this).</summary>
    public event Action<int>? ProducerClicked;

    /// <summary>Raised when a carrier sphere is left-clicked (B5: the inspector listens for this).</summary>
    public event Action<int>? CarrierClicked;

    public void Build(Simulation sim)
    {
        var graph = sim.State.Graph ?? throw new InvalidOperationException("WorldView requires a world (street graph).");

        float minX = float.MaxValue, maxX = float.MinValue, minZ = float.MaxValue, maxZ = float.MinValue;
        foreach (var node in graph.Nodes)
        {
            var position = new Vector3(node.X, 0f, node.Y);
            _nodePositions[node.Id] = position;
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
            var from = _nodePositions[edge.From];
            var to = _nodePositions[edge.To];

            var edgeView = new EdgeView { Name = $"Edge{edge.Id}" };
            AddChild(edgeView);
            edgeView.Setup(edge, from, to);
            if (!from.IsEqualApprox(to))
                edgeView.LookAt(to, Vector3.Up); // in the tree now: LookAt is safe here
            edgeView.Clicked += id => EdgeClicked?.Invoke(id);
            _edgeViews[edge.Id] = edgeView;
        }

        foreach (var depot in graph.Depots)
        {
            AddChild(new MeshInstance3D
            {
                Name = $"Depot{depot.Id}",
                Mesh = new BoxMesh { Size = new Vector3(40f, 40f, 40f) },
                MaterialOverride = new StandardMaterial3D { AlbedoColor = new Color(0.40f, 0.25f, 0.10f) },
                Position = _nodePositions[depot.Node] + new Vector3(0f, 20f, 0f),
            });
        }

        // Day-plan assumes a single depot too (DESIGN.md §4; satellite depots are C1).
        _depotPosition = _nodePositions[graph.Depots[0].Node];

        foreach (var producer in graph.Producers)
        {
            var edge = graph.Edge(producer.Edge);
            var from = _nodePositions[edge.From];
            var to = _nodePositions[edge.To];
            var midpoint = (from + to) / 2f;
            var direction = from.IsEqualApprox(to) ? Vector3.Forward : (to - from).Normalized();
            var offset = direction.Cross(Vector3.Up) * 15f;

            var producerView = new ProducerView { Name = $"Producer{producer.Id}", Position = midpoint + offset };
            AddChild(producerView);
            producerView.Setup(producer.Id, producer.Archetype);
            producerView.Clicked += id => ProducerClicked?.Invoke(id);
            _producerViews[producer.Id] = producerView;
        }

        _arrows = new Node3D { Name = "Arrows" };
        AddChild(_arrows);
    }

    /// <summary>Restages producer heights/colours from the live state (wired to <c>TickResolved</c> in B3).</summary>
    public void Refresh(SimState state)
    {
        foreach (var producer in state.Producers)
        {
            if (_producerViews.TryGetValue(producer.Id, out var view))
                view.UpdateBuffer(producer.BufferGrams, producer.Archetype.BufferGrams);
        }

        // New carriers (bought/hired mid-game) get a sphere the first tick they
        // exist; B6 adds tick-by-tick movement along their ExecutedLegs.
        foreach (var carrier in state.Carriers)
        {
            if (_carrierViews.ContainsKey(carrier.Id))
                continue;

            var carrierView = new CarrierView { Name = $"Carrier{carrier.Id}" };
            AddChild(carrierView);
            carrierView.Setup(carrier.Id, _depotPosition);
            carrierView.Clicked += id => CarrierClicked?.Invoke(id);
            _carrierViews[carrier.Id] = carrierView;
        }
    }

    /// <summary>Selection (B5): emissive highlight on the producer's Mesh child.</summary>
    public void SetProducerSelected(int producerId, bool selected)
    {
        if (_producerViews.TryGetValue(producerId, out var view))
            view.SetSelected(selected);
    }

    /// <summary>Selection (B5): emissive highlight on the carrier's Mesh child.</summary>
    public void SetCarrierSelected(int carrierId, bool selected)
    {
        if (_carrierViews.TryGetValue(carrierId, out var view))
            view.SetSelected(selected);
    }

    /// <summary>Painting (B4): tint one edge; null reverts it to the default street colour.</summary>
    public void HighlightEdge(int edgeId, Color? color)
    {
        if (_edgeViews.TryGetValue(edgeId, out var view))
            view.SetHighlight(color);
    }

    public void ClearEdgeHighlights()
    {
        foreach (var view in _edgeViews.Values)
            view.SetHighlight(null);
    }

    /// <summary>Painting (B4): small cones along each leg's node path, in visit order, replacing any previous plan.</summary>
    public void ShowPlan(TourPlan plan)
    {
        foreach (Node child in _arrows.GetChildren())
            child.QueueFree();

        foreach (var leg in plan.Legs)
        {
            for (var i = 0; i < leg.NodePath.Count - 1; i++)
            {
                var from = _nodePositions[leg.NodePath[i]];
                var to = _nodePositions[leg.NodePath[i + 1]];
                if (from.IsEqualApprox(to))
                    continue;

                var arrow = new Node3D { Position = (from + to) / 2f };
                _arrows.AddChild(arrow);
                arrow.AddChild(new MeshInstance3D
                {
                    Name = "Mesh",
                    Mesh = new CylinderMesh { TopRadius = 0f, BottomRadius = 3f, Height = 10f },
                    // Cone tip is local +Y; rotate it onto local -Z so LookAt (which points -Z at the target) aims the tip forward.
                    RotationDegrees = new Vector3(-90f, 0f, 0f),
                });
                arrow.LookAt(to, Vector3.Up);
            }
        }
    }

    public void ClearArrows()
    {
        foreach (Node child in _arrows.GetChildren())
            child.QueueFree();
    }
}
