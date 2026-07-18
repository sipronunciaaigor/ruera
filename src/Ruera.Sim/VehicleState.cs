using Ruera.Sim.Data;

namespace Ruera.Sim;

/// <summary>
/// Mutable per-vehicle state: type (definition from RUE-12 data) and the
/// painted coverage set — which streets to cover, not in which order
/// (DESIGN.md §4). Vehicles start and end every day at the depot.
/// </summary>
public sealed class VehicleState
{
    private int[] _coverageEdges = [];

    internal VehicleState(int id, VehicleDefinition definition)
    {
        Id = id;
        Definition = definition;
    }

    public int Id { get; }

    public VehicleDefinition Definition { get; }

    public string TypeId => Definition.Id;

    /// <summary>Covered street edges, sorted ascending (canonical form).</summary>
    public IReadOnlyList<int> CoverageEdges => _coverageEdges;

    internal int[] CoverageArray => _coverageEdges;

    internal void SetCoverage(IEnumerable<int> edgeIds)
    {
        _coverageEdges = [.. edgeIds.Distinct().OrderBy(id => id)];
    }
}

/// <summary>What one vehicle did in the last resolved day (inspectability, DESIGN.md §2).</summary>
public sealed record DayPlanReport(int VehicleId, long MinutesUsed, long CollectedGrams, IReadOnlyList<int> ServedProducerIds);
