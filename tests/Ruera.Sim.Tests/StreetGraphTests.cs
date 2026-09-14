using Ruera.Sim.Data;
using Ruera.Sim.World;

namespace Ruera.Sim.Tests;

public class StreetGraphTests
{
    private static string ToyMapPath =>
        Path.Combine(AppContext.BaseDirectory, "data", "packages", "base", "maps", "toy.map.json");

    private static DefinitionRegistry SliceDefinitions =>
        DefinitionLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "packages", "base", "definitions"));

    private const string MinimalMapJson = """
        {
          "formatVersion": 1, "id": "base:mini", "name": "Mini",
          "nodes": [ { "id": 1, "x": 0, "y": 0 }, { "id": 2, "x": 100, "y": 0 } ],
          "edges": [ { "id": 1, "from": 1, "to": 2, "lengthMeters": 100 } ],
          "depots": [ { "id": 1, "node": 1 } ],
          "producers": [ { "id": 1, "edge": 1, "archetype": "base:shop" } ]
        }
        """;

    private static string Mutate(string oldText, string newText) => MinimalMapJson.Replace(oldText, newText);

    [Fact]
    public void ToyMap_LoadsAndValidatesAgainstSliceDefinitions()
    {
        var graph = MapLoader.LoadFromFile(ToyMapPath, SliceDefinitions);

        Assert.Equal("base:toy", graph.MapId);
        Assert.Equal(12, graph.Nodes.Count);
        Assert.Equal(17, graph.Edges.Count);
        Assert.Equal(6, graph.Producers.Count);
        Assert.Single(graph.Depots);
    }

    [Fact]
    public void ProducersAndDepots_AreAddressable()
    {
        var graph = MapLoader.LoadFromFile(ToyMapPath);

        Assert.Equal(1, graph.Depot(1).Node);
        Assert.Equal(16, graph.Producer(5).Edge);
        Assert.Equal("base:factory", graph.Producer(5).Archetype);
        Assert.Equal(330, graph.Edge(3).Length.Value);
        Assert.Throws<KeyNotFoundException>(() => graph.Producer(99));
    }

    [Fact]
    public void Distances_AreShortestAndDeterministic()
    {
        var graph = MapLoader.LoadFromFile(ToyMapPath);

        Assert.Equal(0, graph.Distance(1, 1).Value);
        Assert.Equal(930, graph.Distance(1, 4).Value);   // 300 + 300 + 330 along the top row
        Assert.Equal(1500, graph.Distance(1, 12).Value); // best corner-to-corner route
        Assert.Equal(graph.Distance(1, 12).Value, graph.Distance(12, 1).Value); // undirected symmetry
    }

    [Fact]
    public void ShortestPath_IsStableAcrossInstances()
    {
        var a = MapLoader.LoadFromFile(ToyMapPath);
        var b = MapLoader.LoadFromFile(ToyMapPath);

        Assert.Equal([1, 2, 3, 4], a.ShortestPath(1, 4));
        Assert.Equal(a.ShortestPath(1, 12), b.ShortestPath(1, 12)); // ties resolve identically
        Assert.Equal(a.ShortestPath(9, 4), b.ShortestPath(9, 4));
    }

    [Theory]
    [InlineData("\"to\": 2,", "\"to\": 9,", "unknown node 9")]
    [InlineData("\"lengthMeters\": 100", "\"lengthMeters\": 0", "lengthMeters must be > 0")]
    [InlineData("\"node\": 1", "\"node\": 7", "unknown node 7")]
    [InlineData("\"edge\": 1,", "\"edge\": 5,", "unknown edge 5")]
    [InlineData("\"formatVersion\": 1,", "\"formatVersion\": 9,", "unsupported formatVersion 9")]
    [InlineData("\"id\": \"base:mini\"", "\"id\": \"mini\"", "package:name")]
    public void InvalidMaps_AreRejectedWithClearErrors(string oldText, string newText, string expectedError)
    {
        var exception = Assert.Throws<MapLoadException>(() => MapLoader.Load(Mutate(oldText, newText)));

        Assert.Contains(expectedError, exception.Message);
    }

    [Fact]
    public void DisconnectedGraph_IsRejected()
    {
        var disconnected = Mutate(
            "{ \"id\": 2, \"x\": 100, \"y\": 0 }",
            "{ \"id\": 2, \"x\": 100, \"y\": 0 }, { \"id\": 3, \"x\": 500, \"y\": 500 }");

        var exception = Assert.Throws<MapLoadException>(() => MapLoader.Load(disconnected));

        Assert.Contains("not connected", exception.Message);
    }

    [Fact]
    public void UnknownArchetype_IsRejectedWhenDefinitionsProvided()
    {
        var badArchetype = Mutate("\"archetype\": \"base:shop\"", "\"archetype\": \"base:palazzo\"");

        Assert.NotNull(MapLoader.Load(badArchetype)); // without definitions: structural check only
        var exception = Assert.Throws<MapLoadException>(() => MapLoader.Load(badArchetype, SliceDefinitions));
        Assert.Contains("base:palazzo", exception.Message);
    }

    // RUE-42: Distance() now reads the all-pairs table precomputed at load
    // instead of running Dijkstra per call. Both tests prove the table is
    // identical to an independent oracle (ShortestPath's node path, summed
    // against the edge lengths straight from the loaded map) for every
    // ordered pair — the precomputation must not change a single distance.

    [Fact]
    public void DistanceTable_MatchesShortestPath_ForEveryOrderedPair_OnToyMap()
    {
        var graph = MapLoader.LoadFromFile(ToyMapPath);

        AssertTableMatchesShortestPathForEveryPair(graph);
    }

    [Fact]
    public void DistanceTable_MatchesShortestPath_ForEveryOrderedPair_On10x10GridWithIrregularLengths()
    {
        var graph = MapLoader.Load(GenerateGridMapJson(width: 10, height: 10));

        Assert.Equal(100, graph.Nodes.Count);
        AssertTableMatchesShortestPathForEveryPair(graph);
    }

    private static void AssertTableMatchesShortestPathForEveryPair(StreetGraph graph)
    {
        var edgeLengths = new Dictionary<(int Low, int High), long>();
        foreach (var edge in graph.Edges)
            edgeLengths[(Math.Min(edge.From, edge.To), Math.Max(edge.From, edge.To))] = edge.LengthMeters;

        var nodeIds = graph.Nodes.Select(n => n.Id).ToArray();
        foreach (var from in nodeIds)
        {
            foreach (var to in nodeIds)
            {
                var expected = 0L;
                if (from != to)
                {
                    var path = graph.ShortestPath(from, to);
                    for (var i = 0; i < path.Count - 1; i++)
                        expected += edgeLengths[(Math.Min(path[i], path[i + 1]), Math.Max(path[i], path[i + 1]))];
                }

                Assert.Equal(expected, graph.Distance(from, to).Value);
            }
        }
    }

    /// <summary>Deterministic W×H grid, irregular (but reproducible) edge lengths, one depot at node 1, no producers.</summary>
    private static string GenerateGridMapJson(int width, int height)
    {
        int NodeId(int x, int y) => (y * width) + x + 1;

        var nodes = new List<string>();
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                nodes.Add(FormattableString.Invariant($$"""{ "id": {{NodeId(x, y)}}, "x": {{x * 100}}, "y": {{y * 100}} }"""));

        var edges = new List<string>();
        var edgeId = 1;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (x + 1 < width)
                {
                    var length = 80 + ((x * 7) + (y * 13)) % 41;
                    edges.Add(FormattableString.Invariant(
                        $$"""{ "id": {{edgeId++}}, "from": {{NodeId(x, y)}}, "to": {{NodeId(x + 1, y)}}, "lengthMeters": {{length}} }"""));
                }

                if (y + 1 < height)
                {
                    var length = 90 + ((x * 11) + (y * 5)) % 37;
                    edges.Add(FormattableString.Invariant(
                        $$"""{ "id": {{edgeId++}}, "from": {{NodeId(x, y)}}, "to": {{NodeId(x, y + 1)}}, "lengthMeters": {{length}} }"""));
                }
            }
        }

        return $$"""
            {
              "formatVersion": 1, "id": "base:grid", "name": "Grid",
              "nodes": [ {{string.Join(", ", nodes)}} ],
              "edges": [ {{string.Join(", ", edges)}} ],
              "depots": [ { "id": 1, "node": 1 } ],
              "producers": []
            }
            """;
    }
}
