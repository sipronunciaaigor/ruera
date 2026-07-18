using Ruera.Sim.Data;

namespace Ruera.Sim;

/// <summary>
/// Mutable per-producer state: accumulation buffer, last collection, violation
/// tally. Identity and archetype come from the map (RUE-9/13); production
/// rates and limits from the archetype definition (RUE-12).
/// </summary>
public sealed class ProducerState
{
    internal ProducerState(int id, int edgeId, ProducerArchetype archetype)
    {
        Id = id;
        EdgeId = edgeId;
        Archetype = archetype;
    }

    public int Id { get; }

    public int EdgeId { get; }

    public ProducerArchetype Archetype { get; }

    /// <summary>Waste currently waiting at the producer.</summary>
    public long BufferGrams { get; internal set; }

    /// <summary>Tick of the last collection (0 = game start counts as freshly served).</summary>
    public long LastCollectedTick { get; internal set; }

    /// <summary>Ticks spent in violation so far (fines hook for RUE-14).</summary>
    public long ViolationCount { get; internal set; }

    /// <summary>Under a condo contract: pays the archetype's monthly fee (DESIGN.md §8).</summary>
    public bool HasContract { get; internal set; }
}
