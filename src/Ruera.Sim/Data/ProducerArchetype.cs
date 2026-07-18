namespace Ruera.Sim.Data;

/// <summary>One waste flow emitted by a producer archetype.</summary>
public sealed class WasteProduction
{
    /// <summary>Id of a <see cref="WasteDefinition"/>; validated at load.</summary>
    public required string Waste { get; init; }

    public required long GramsPerTick { get; init; }

    public Grams PerTick => new(GramsPerTick);
}

/// <summary>
/// A producer archetype as declared in producers.json (DESIGN.md §3):
/// urban-aggregate producers with an accumulation buffer and a maximum
/// sanitary collection interval. Map producers reference these by id (RUE-9).
/// </summary>
public sealed class ProducerArchetype
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Accumulation buffer: exceeding it is a violation regardless of the interval.</summary>
    public required long BufferGrams { get; init; }

    /// <summary>Maximum ticks between collections before a sanitary violation (DESIGN.md §3).</summary>
    public required int MaxSanitaryIntervalTicks { get; init; }

    /// <summary>Monthly condo-contract fee when under contract (DESIGN.md §8; cadence per RUE-6).</summary>
    public required long ContractCentsPerMonth { get; init; }

    public required List<WasteProduction> Production { get; init; }

    public Grams Buffer => new(BufferGrams);
}
