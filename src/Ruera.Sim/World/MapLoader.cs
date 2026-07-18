using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

using Ruera.Sim.Data;

namespace Ruera.Sim.World;

/// <summary>Raised when a map file is malformed, incomplete, or inconsistent.</summary>
public sealed class MapLoadException(string message) : Exception(message);

/// <summary>
/// Strict loader for *.map.json (RUE-9, DESIGN.md §11). Unknown fields,
/// missing required fields, dangling references, non-positive lengths and a
/// disconnected graph all fail the load with an error naming the offender.
/// </summary>
public static class MapLoader
{
    private const int SupportedFormatVersion = 1;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static StreetGraph LoadFromFile(string path, DefinitionRegistry? definitions = null)
    {
        if (!File.Exists(path))
            throw new MapLoadException(Invariant($"Map file not found: '{path}'."));
        return Load(File.ReadAllText(path), definitions);
    }

    /// <summary>
    /// Parses and validates a map. When <paramref name="definitions"/> is
    /// given, producer archetype ids are checked against it too.
    /// </summary>
    public static StreetGraph Load(string json, DefinitionRegistry? definitions = null)
    {
        MapFile? map;
        try
        {
            map = JsonSerializer.Deserialize<MapFile>(json, Options);
        }
        catch (JsonException exception)
        {
            throw new MapLoadException(Invariant($"map: {exception.Message}"));
        }

        if (map is null)
            throw new MapLoadException("map: file is empty.");
        Validate(map, definitions);
        return new StreetGraph(map);
    }

    private static void Validate(MapFile map, DefinitionRegistry? definitions)
    {
        Require(map.FormatVersion == SupportedFormatVersion,
            Invariant($"unsupported formatVersion {map.FormatVersion} (supported: {SupportedFormatVersion})"));
        Require(!string.IsNullOrWhiteSpace(map.Id), "map id must not be empty");
        var colon = map.Id.IndexOf(':');
        Require(colon > 0 && colon == map.Id.LastIndexOf(':') && colon < map.Id.Length - 1,
            Invariant($"map id '{map.Id}' must be namespaced as 'package:name' (DESIGN.md §2 «Moddabilità»)"));
        Require(!string.IsNullOrWhiteSpace(map.Name), "map name must not be empty");
        Require(map.Nodes.Count > 0, "map must have at least one node");
        Require(map.Depots.Count > 0, "map must have at least one depot");

        RequireUniqueIds(map.Nodes.Select(n => n.Id), "node");
        RequireUniqueIds(map.Edges.Select(e => e.Id), "edge");
        RequireUniqueIds(map.Depots.Select(d => d.Id), "depot");
        RequireUniqueIds(map.Producers.Select(p => p.Id), "producer");

        var nodeIds = map.Nodes.Select(n => n.Id).ToList();
        var edgeIds = map.Edges.Select(e => e.Id).ToList();
        foreach (var edge in map.Edges)
        {
            Require(nodeIds.Contains(edge.From), Invariant($"edge {edge.Id}: unknown node {edge.From}"));
            Require(nodeIds.Contains(edge.To), Invariant($"edge {edge.Id}: unknown node {edge.To}"));
            Require(edge.From != edge.To, Invariant($"edge {edge.Id}: from and to are the same node"));
            Require(edge.LengthMeters > 0, Invariant($"edge {edge.Id}: lengthMeters must be > 0 (was {edge.LengthMeters})"));
        }

        foreach (var depot in map.Depots)
            Require(nodeIds.Contains(depot.Node), Invariant($"depot {depot.Id}: unknown node {depot.Node}"));

        foreach (var producer in map.Producers)
        {
            Require(edgeIds.Contains(producer.Edge), Invariant($"producer {producer.Id}: unknown edge {producer.Edge}"));
            Require(!string.IsNullOrWhiteSpace(producer.Archetype), Invariant($"producer {producer.Id}: archetype must not be empty"));
            if (definitions is not null)
                Require(definitions.TryGetProducerArchetype(producer.Archetype, out _),
                    Invariant($"producer {producer.Id}: unknown archetype '{producer.Archetype}'"));
        }

        RequireConnected(map, nodeIds);
    }

    private static void RequireConnected(MapFile map, List<int> nodeIds)
    {
        var reached = new List<int> { nodeIds[0] };
        var frontier = new Queue<int>();
        frontier.Enqueue(nodeIds[0]);
        while (frontier.Count > 0)
        {
            var current = frontier.Dequeue();
            foreach (var edge in map.Edges) // file order: deterministic
            {
                var neighbor = edge.From == current ? edge.To : edge.To == current ? edge.From : -1;
                if (neighbor >= 0 && !reached.Contains(neighbor))
                {
                    reached.Add(neighbor);
                    frontier.Enqueue(neighbor);
                }
            }
        }

        Require(reached.Count == nodeIds.Count, "graph is not connected");
    }

    private static void RequireUniqueIds(IEnumerable<int> ids, string kind)
    {
        var seen = new List<int>();
        foreach (var id in ids)
        {
            Require(!seen.Contains(id), Invariant($"duplicate {kind} id {id}"));
            seen.Add(id);
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
            throw new MapLoadException(Invariant($"map: {message}."));
    }

    private static string Invariant(FormattableString message) => FormattableString.Invariant(message);
}
