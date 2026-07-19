namespace Ruera.Sim;

public enum SimEventType
{
    /// <summary>Producer's accumulation exceeded its buffer (DESIGN.md §3).</summary>
    BufferOverflow = 1,

    /// <summary>Producer went uncollected past its max sanitary interval (DESIGN.md §3).</summary>
    SanitaryViolation = 2,

    /// <summary>Cash closed below zero for the first time (DESIGN.md §12: finire in rosso).</summary>
    Bankruptcy = 3,

    /// <summary>Vehicle broke down: out of service until the tick in Data; repair cost posted (RUE-32).</summary>
    VehicleBreakdown = 4,

    /// <summary>Sanitary inspection: Data carries the total fines charged on standing violations (RUE-32).</summary>
    SanitaryInspection = 5,

    /// <summary>Public tender announced for the producer in EntityId; Data is the deadline tick (RUE-32).</summary>
    TenderAnnounced = 6,
}

/// <summary>
/// A fact emitted while resolving a tick. EntityId is the producer for
/// violations/tenders, the vehicle for breakdowns, 0 otherwise; Data carries
/// the event-specific number (see each type). Emitted every tick the
/// condition holds; re-derivable from state, so events are a query surface,
/// not hashed state.
/// </summary>
public readonly record struct SimEvent(long Tick, SimEventType Type, int EntityId, long Data = 0);
