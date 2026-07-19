namespace Ruera.Sim;

/// <summary>
/// An employee. Hiring costs wages from the first payday but yields no crew
/// capacity for ~10 training ticks (DESIGN.md §2 «Ritardi realistici»):
/// planning is rewarded, hire-and-fire is not.
/// </summary>
public sealed class WorkerState
{
    internal WorkerState(int id, long hiredTick)
    {
        Id = id;
        HiredTick = hiredTick;
    }

    public int Id { get; }

    public long HiredTick { get; }

    // Room for a role field (raccolta/cernita/vendita/R&D — DESIGN.md §8) when
    // processing and sales land: it slots into the canonical writer after
    // HiredTick with a StateSchemaVersion bump, and defaults keep old saves
    // loadable-forward. Not added now — no system reads a role yet (RUE-33).
}
