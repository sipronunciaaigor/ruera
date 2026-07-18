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
}
