using Ruera.Sim.Calendar;
using Ruera.Sim.Commands;
using Ruera.Sim.Hashing;

namespace Ruera.Sim;

/// <summary>
/// Deterministic tick engine (RUE-11). One tick = one in-game day; the sim
/// advances only through <see cref="Advance"/>. Same seed + same inputs =
/// identical state, bit for bit (DESIGN.md §2).
/// </summary>
public sealed class Simulation
{
    private readonly List<CommandLogEntry> _pending = [];
    private readonly List<CommandLogEntry> _log = [];

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

    /// <summary>
    /// Every command ever scheduled, in submission order: the replay input.
    /// Game = initial state (seed) + this log (DESIGN.md §2).
    /// </summary>
    public IReadOnlyList<CommandLogEntry> CommandLog => _log;

    /// <summary>Submits a command for the opening of the current day (the tick that resolves <see cref="Today"/>).</summary>
    public void Submit(SimCommand command) => Schedule(State.Tick, command);

    /// <summary>
    /// Schedules a command for the opening of a not-yet-resolved day. Throws if
    /// the day is already resolved or the command fails validation against the
    /// current state; it is re-validated authoritatively at application.
    /// </summary>
    public void Schedule(long day, SimCommand command)
    {
        ArgumentNullException.ThrowIfNull(command);
        var validation = command.Validate(State);
        if (!validation.IsValid)
            throw new ArgumentException($"Invalid command: {validation.Reason}", nameof(command));
        ScheduleCore(day, command);
    }

    /// <summary>
    /// Reconstructs a run from its inputs: fresh state from the seed, the full
    /// command log scheduled, then advanced. Skips submission-time validation —
    /// recorded commands were validated against states a fresh sim does not
    /// have yet; application-time validation remains the deterministic authority.
    /// </summary>
    public static Simulation Replay(ulong seed, IEnumerable<CommandLogEntry> log, int ticks)
    {
        var sim = new Simulation(seed);
        foreach (var entry in log)
            sim.ScheduleCore(entry.Day, entry.Command);
        sim.Advance(ticks);
        return sim;
    }

    public void Advance(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        for (var i = 0; i < ticks; i++)
            AdvanceOneTick();
    }

    private void ScheduleCore(long day, SimCommand command)
    {
        if (day < State.Tick)
            throw new ArgumentOutOfRangeException(nameof(day), day, "Cannot schedule a command for an already-resolved day.");
        var entry = new CommandLogEntry(day, command);
        _pending.Add(entry);
        _log.Add(entry);
    }

    private void AdvanceOneTick()
    {
        // Fixed in-tick order (DESIGN.md §2 «Risoluzione al tick»): commands at
        // tick open, then systems in fixed declaration order as they land
        // (calendar/events, waste production, day plan, processing, sales,
        // closing accounting). Single-threaded inside the tick by rule.
        ApplyDueCommands();
        State.Tick++; // tick close: effects are materialized, the day is resolved
    }

    private void ApplyDueCommands()
    {
        var day = State.Tick;
        var index = 0;
        while (index < _pending.Count)
        {
            var entry = _pending[index];
            if (entry.Day == day)
            {
                // Re-validate against the state the command actually meets; a
                // command that turned invalid is skipped deterministically (the
                // log still records the attempt, so replays skip it identically).
                if (entry.Command.Validate(State).IsValid)
                    entry.Command.Apply(State);
                _pending.RemoveAt(index);
            }
            else
            {
                index++;
            }
        }
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
