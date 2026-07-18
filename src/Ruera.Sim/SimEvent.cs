namespace Ruera.Sim;

public enum SimEventType
{
    /// <summary>Producer's accumulation exceeded its buffer (DESIGN.md §3).</summary>
    BufferOverflow = 1,

    /// <summary>Producer went uncollected past its max sanitary interval (DESIGN.md §3).</summary>
    SanitaryViolation = 2,

    /// <summary>Cash closed below zero for the first time (DESIGN.md §12: finire in rosso).</summary>
    Bankruptcy = 3,
}

/// <summary>
/// A fact emitted while resolving a tick. EntityId is the producer for
/// violations, 0 otherwise. Emitted every tick the condition holds;
/// re-derivable from state, so events are a query surface, not hashed state.
/// </summary>
public readonly record struct SimEvent(long Tick, SimEventType Type, int EntityId);
