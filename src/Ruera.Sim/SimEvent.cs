namespace Ruera.Sim;

public enum SimEventType
{
    /// <summary>Producer's accumulation exceeded its buffer (DESIGN.md §3).</summary>
    BufferOverflow = 1,

    /// <summary>Producer went uncollected past its max sanitary interval (DESIGN.md §3).</summary>
    SanitaryViolation = 2,
}

/// <summary>
/// A fact emitted while resolving a tick. Emitted every tick the condition
/// holds; re-derivable from state, so events are a query surface, not hashed
/// state.
/// </summary>
public readonly record struct SimEvent(long Tick, SimEventType Type, int ProducerId);
