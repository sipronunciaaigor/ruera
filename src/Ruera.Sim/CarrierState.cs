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

/// <summary>
/// What one carrier did in the last resolved day (inspectability, DESIGN.md §2).
/// <paramref name="Trips"/> is the number of depot departures (RUE-44 multi-trip:
/// ≥ 1 whenever the carrier left the depot). <paramref name="ExecutedLegs"/> is
/// the actual edge visit order (RUE-19), so the renderer can stage what really
/// happened, distinct from the pessimistic <see cref="TourPlan"/> it painted.
/// Not part of canonical state — a transient per-tick report, cleared every
/// <see cref="SimState.BeginTick"/>.
/// </summary>
public sealed record DayPlanReport(int CarrierId, long MinutesUsed, long CollectedGrams,
    IReadOnlyList<int> ServedProducerIds, int Trips, IReadOnlyList<ExecutedLeg> ExecutedLegs);

/// <summary>One edge actually visited during execution (RUE-19). <see cref="ReturnToDepot"/>
/// marks a leg immediately followed by a trip home (multi-trip refill or the tour's end).</summary>
public readonly record struct ExecutedLeg(int EdgeId, bool ReturnToDepot);
