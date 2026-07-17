using Ruera.Sim.Calendar;
using Ruera.Sim.Hashing;

namespace Ruera.Sim;

/// <summary>
/// Deterministic tick engine (RUE-11). One tick = one in-game day; the sim
/// advances only through <see cref="Advance"/>. Same seed + same inputs =
/// identical state, bit for bit (DESIGN.md §2).
/// </summary>
public sealed class Simulation
{
    public SimState State { get; }

    public SimCalendar Calendar { get; }

    /// <summary>Engine with the vertical-slice default calendar (Milano, tick 0 = 1880-01-01).</summary>
    public Simulation(ulong seed) : this(seed, SimCalendar.Milano1880())
    {
    }

    public Simulation(ulong seed, SimCalendar calendar)
    {
        State = new SimState(seed);
        Calendar = calendar;
    }

    public ulong Seed => State.Seed;

    /// <summary>Elapsed ticks. One tick is one in-game day (DESIGN.md §2).</summary>
    public long Tick => State.Tick;

    /// <summary>The current tick's date: a tick always knows its calendar day.</summary>
    public SimDate Today => Calendar.DateAt(State.Tick);

    public bool IsWorkingDay => Calendar.IsWorkingDay(State.Tick);

    public void Advance(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        for (var i = 0; i < ticks; i++)
            AdvanceOneTick();
    }

    private void AdvanceOneTick()
    {
        // Systems will run here in fixed declaration order as they land
        // (waste production, collection, economy, ...). Single-threaded inside
        // the tick by rule (DESIGN.md §2, rule 6).
        State.Tick++;
    }

    /// <summary>
    /// Stable FNV-1a hash of the full state. Equal seeds + equal inputs must
    /// yield equal hashes at every tick — this is the determinism contract.
    /// </summary>
    public ulong StateHash()
    {
        var hasher = Fnv1a64.Create();
        State.AddToHash(ref hasher);
        return hasher.Hash;
    }
}
