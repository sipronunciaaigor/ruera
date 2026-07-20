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
}
