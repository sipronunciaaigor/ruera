using System.Globalization;

namespace Ruera.Sim.World;

/// <summary>
/// The city as a validated street graph (DESIGN.md §4, §11): nodes joined by
/// undirected edges whose lengths are the travel costs, with depots and
/// producers as addressable entities. Queries are deterministic by
/// construction: adjacency and scans run in sorted-id order, ties in shortest
/// paths resolve to the lowest node id.
/// </summary>
public sealed class StreetGraph
{
    private readonly MapNode[] _nodes;
    private readonly MapEdge[] _edges;
    private readonly MapDepot[] _depots;
    private readonly MapProducer[] _producers;
    private readonly int[] _nodeIds;
    private readonly int[] _edgeIds;
    private readonly int[] _depotIds;
    private readonly int[] _producerIds;
    private readonly (int Neighbor, long Length)[][] _adjacency; // by node index, sorted (neighbor, length)
    private readonly long[][] _distanceTable; // [sourceIndex][targetIndex], precomputed once at load (RUE-42)

    internal StreetGraph(MapFile map)
    {
        MapId = map.Id;
        Name = map.Name;
        _nodes = [.. map.Nodes.OrderBy(n => n.Id)];
        _edges = [.. map.Edges.OrderBy(e => e.Id)];
        _depots = [.. map.Depots.OrderBy(d => d.Id)];
        _producers = [.. map.Producers.OrderBy(p => p.Id)];
        _nodeIds = [.. _nodes.Select(n => n.Id)];
        _edgeIds = [.. _edges.Select(e => e.Id)];
        _depotIds = [.. _depots.Select(d => d.Id)];
        _producerIds = [.. _producers.Select(p => p.Id)];

        var lists = new List<(int Neighbor, long Length)>[_nodes.Length];
        for (var i = 0; i < lists.Length; i++)
            lists[i] = [];
        foreach (var edge in _edges)
        {
            var from = IndexOfNode(edge.From);
            var to = IndexOfNode(edge.To);
            lists[from].Add((to, edge.LengthMeters));
            lists[to].Add((from, edge.LengthMeters));
        }

        _adjacency = new (int, long)[_nodes.Length][];
        for (var i = 0; i < lists.Length; i++)
        {
            lists[i].Sort();
            _adjacency[i] = [.. lists[i]];
        }

        // All-pairs shortest paths, once per source node (RUE-42): shortest-path
        // lengths are unique, so this changes nothing about what Distance
        // returns — only how fast. Distance(a, b) becomes an O(1) lookup;
        // ShortestPath (cold path) still runs its own single-source Dijkstra.
        _distanceTable = new long[_nodes.Length][];
        var tableDistance = new long[_nodes.Length];
        var tablePrevious = new int[_nodes.Length];
        var tableVisited = new bool[_nodes.Length];
        for (var source = 0; source < _nodes.Length; source++)
        {
            RunDijkstra(source, target: null, tableDistance, tablePrevious, tableVisited);
            _distanceTable[source] = [.. tableDistance];
        }
    }

    public string MapId { get; }

    public string Name { get; }

    public IReadOnlyList<MapNode> Nodes => _nodes;

    public IReadOnlyList<MapEdge> Edges => _edges;

    public IReadOnlyList<MapDepot> Depots => _depots;

    public IReadOnlyList<MapProducer> Producers => _producers;

    public bool HasEdge(int id) => Array.BinarySearch(_edgeIds, id) >= 0;

    public MapNode Node(int id) => _nodes[Find(_nodeIds, id, "node")];

    public MapEdge Edge(int id) => _edges[Find(_edgeIds, id, "edge")];

    public MapDepot Depot(int id) => _depots[Find(_depotIds, id, "depot")];

    public MapProducer Producer(int id) => _producers[Find(_producerIds, id, "producer")];

    /// <summary>
    /// Length of the shortest route between two nodes. On the per-tick hot
    /// path (the day-plan tour): an O(1) lookup into the all-pairs table
    /// precomputed once at load (RUE-42), superseding the per-call Dijkstra
    /// this used to run (RUE-37).
    /// </summary>
    public Meters Distance(int fromNodeId, int toNodeId)
    {
        var source = Find(_nodeIds, fromNodeId, "node");
        var target = Find(_nodeIds, toNodeId, "node");
        return new Meters(_distanceTable[source][target]);
    }

    /// <summary>Node ids of the shortest route, endpoints included. Ties resolve to the lowest node id.</summary>
    public IReadOnlyList<int> ShortestPath(int fromNodeId, int toNodeId)
    {
        var source = Find(_nodeIds, fromNodeId, "node");
        var target = Find(_nodeIds, toNodeId, "node");

        // Cold path (routing preview / debug, not per-tick): plain allocation.
        var distance = new long[_nodes.Length];
        var previous = new int[_nodes.Length];
        var visited = new bool[_nodes.Length];

        RunDijkstra(source, target, distance, previous, visited);

        var path = new List<int>();
        for (var index = target; index >= 0; index = previous[index])
            path.Add(_nodeIds[index]);
        path.Reverse();
        return path;
    }

    /// <summary>
    /// Deterministic Dijkstra into caller-provided buffers (length >= node
    /// count). Linear scan for the min: no heap, no unspecified tie order.
    /// A null <paramref name="target"/> runs to completion (every reachable
    /// node), used to fill one row of the all-pairs table; a given target
    /// stops as soon as it is settled, used by the cold-path <see cref="ShortestPath"/>.
    /// </summary>
    private void RunDijkstra(int source, int? target, long[] distance, int[] previous, bool[] visited)
    {
        var count = _nodes.Length;
        Array.Fill(distance, long.MaxValue, 0, count);
        Array.Fill(previous, -1, 0, count);
        Array.Clear(visited, 0, count);
        distance[source] = 0;

        while (true)
        {
            var current = -1;
            for (var i = 0; i < count; i++)
            {
                if (!visited[i] && distance[i] < long.MaxValue && (current < 0 || distance[i] < distance[current]))
                    current = i;
            }

            if (current < 0)
            {
                if (target.HasValue)
                    throw new InvalidOperationException("Target unreachable — validated maps are connected.");
                return; // full run: every reachable node is settled
            }
            if (target.HasValue && current == target.Value)
                return;

            visited[current] = true;
            foreach (var (neighbor, length) in _adjacency[current])
            {
                var candidate = checked(distance[current] + length);
                // Strict improvement only: with neighbors scanned in sorted
                // order, equal-length routes keep the lowest-id predecessor.
                if (candidate < distance[neighbor])
                {
                    distance[neighbor] = candidate;
                    previous[neighbor] = current;
                }
            }
        }
    }

    private int IndexOfNode(int id) => Find(_nodeIds, id, "node");

    private static int Find(int[] ids, int id, string kind)
    {
        var index = Array.BinarySearch(ids, id);
        return index >= 0
            ? index
            : throw new KeyNotFoundException(string.Create(CultureInfo.InvariantCulture,
                $"Unknown {kind} id {id}."));
    }
}
