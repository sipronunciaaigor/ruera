namespace Ruera.Sim.World;

// JSON-bound map entities (*.map.json, format v1 — RUE-9, DESIGN.md §11).
// Integer units; node coordinates are presentation-only, edge lengthMeters is
// authoritative for travel costs.

public sealed class MapNode
{
    public required int Id { get; init; }

    /// <summary>Presentation-only (staging, painting UI): never feeds travel costs.</summary>
    public required long X { get; init; }

    /// <summary>Presentation-only: never feeds travel costs.</summary>
    public required long Y { get; init; }
}

public sealed class MapEdge
{
    public required int Id { get; init; }

    public required int From { get; init; }

    public required int To { get; init; }

    /// <summary>Authoritative travel cost of the street (DESIGN.md §11): undirected in V1.</summary>
    public required long LengthMeters { get; init; }

    public Meters Length => new(LengthMeters);
}

public sealed class MapDepot
{
    public required int Id { get; init; }

    public required int Node { get; init; }
}

public sealed class MapProducer
{
    public required int Id { get; init; }

    /// <summary>The street the producer sits on; service happens while covering it.</summary>
    public required int Edge { get; init; }

    /// <summary>Id of a producer archetype from the entity definitions (RUE-12).</summary>
    public required string Archetype { get; init; }
}

internal sealed class MapFile
{
    public required int FormatVersion { get; init; }

    public required string Id { get; init; }

    public required string Name { get; init; }

    public required List<MapNode> Nodes { get; init; }

    public required List<MapEdge> Edges { get; init; }

    public required List<MapDepot> Depots { get; init; }

    public required List<MapProducer> Producers { get; init; }
}
