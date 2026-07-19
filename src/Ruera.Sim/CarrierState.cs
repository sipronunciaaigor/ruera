using Ruera.Sim.Data;

namespace Ruera.Sim;

/// <summary>
/// Mutable per-carrier state: type (definition from RUE-12 data) and the
/// painted coverage set — which streets to cover, not in which order
/// (DESIGN.md §4). Carriers start and end every day at the depot.
/// </summary>
public sealed class CarrierState
{
    private int[] _coverageEdges = [];

    internal CarrierState(int id, CarrierDefinition definition)
    {
        Id = id;
        Definition = definition;
    }

    public int Id { get; }

    public CarrierDefinition Definition { get; }

    public string TypeId => Definition.Id;

    /// <summary>Broken down until this tick (exclusive): the day plan skips it (RUE-32). 0 = in service.</summary>
    public long OutOfServiceUntilTick { get; internal set; }

    /// <summary>Covered street edges, sorted ascending (canonical form).</summary>
    public IReadOnlyList<int> CoverageEdges => _coverageEdges;

    internal int[] CoverageArray => _coverageEdges;

    internal void SetCoverage(IEnumerable<int> edgeIds)
    {
        _coverageEdges = [.. edgeIds.Distinct().OrderBy(id => id)];
    }
}

/// <summary>What one carrier did in the last resolved day (inspectability, DESIGN.md §2).</summary>
public sealed record DayPlanReport(int CarrierId, long MinutesUsed, long CollectedGrams, IReadOnlyList<int> ServedProducerIds);
