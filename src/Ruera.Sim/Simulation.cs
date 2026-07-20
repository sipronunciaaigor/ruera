using Ruera.Sim.Calendar;
using Ruera.Sim.Commands;
using Ruera.Sim.Data;
using Ruera.Sim.Hashing;
using Ruera.Sim.Systems;
using Ruera.Sim.World;

using ScenarioPackage = Ruera.Sim.Scenario.Scenario;

namespace Ruera.Sim;

/// <summary>
/// Deterministic tick engine (RUE-11). One tick = one in-game day; the sim
/// advances only through <see cref="Advance"/>. Same seed + same inputs =
/// identical state, bit for bit (DESIGN.md §2).
/// </summary>
public sealed class Simulation
{
    // The fixed in-tick system order (DESIGN.md §2 «Risoluzione al tick», RUE-6):
    // waste production -> day plan -> closing checks. Calendar/events, processing
    // and sales slot into this array as they land. Plain array, run in order.
    private static readonly ISimSystem[] SystemPipeline =
    [
        new EventsSystem(),
        new WasteProductionSystem(),
        new DayPlanSystem(),
        new ViolationSystem(),
        new EconomySystem(),
    ];

    private readonly List<CommandLogEntry> _pending = [];
    private readonly List<CommandLogEntry> _log = [];

    public SimState State { get; }

    public SimCalendar Calendar => State.Calendar;

    /// <summary>Worldless engine with the vertical-slice default calendar (tick 0 = 1880-01-01).</summary>
    public Simulation(ulong seed) : this(seed, SimCalendar.Milano1880(), null, null)
    {
    }

    public Simulation(ulong seed, SimCalendar calendar) : this(seed, calendar, null, null)
    {
    }

    /// <summary>Engine over a loaded world (map + entity definitions), default calendar.</summary>
    public Simulation(ulong seed, StreetGraph graph, DefinitionRegistry definitions, EventSettings? events = null)
        : this(seed, SimCalendar.Milano1880(), graph, definitions, events)
    {
    }

    public Simulation(ulong seed, SimCalendar calendar, StreetGraph? graph, DefinitionRegistry? definitions,
        EventSettings? events = null)
    {
        State = new SimState(seed, calendar, graph, definitions, events);
    }

    /// <summary>
    /// Builds an engine from a loaded scenario package (RUE-38): the calendar is
    /// compiled from the scenario's data (epoch, holidays, rest days, and any
    /// <c>SetCalendar</c> timeline effects) and events come from the scenario.
    /// The scenario is retained so the save header carries the whole-bundle hash.
    /// </summary>
    public static Simulation FromScenario(ulong seed, ScenarioPackage scenario, StreetGraph graph,
        DefinitionRegistry definitions, Packaging.PackageSetIdentity? packages = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        return new Simulation(seed, scenario, graph, definitions, packages);
    }

    private Simulation(ulong seed, ScenarioPackage scenario, StreetGraph graph, DefinitionRegistry definitions,
        Packaging.PackageSetIdentity? packages)
    {
        State = new SimState(seed, scenario.BuildCalendar(), graph, definitions, scenario.Events, scenario, packages);
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
    public static Simulation Replay(ulong seed, IEnumerable<CommandLogEntry> log, int ticks,
        StreetGraph? graph = null, DefinitionRegistry? definitions = null, EventSettings? events = null)
    {
        var sim = new Simulation(seed, SimCalendar.Milano1880(), graph, definitions, events);
        foreach (var entry in log)
            sim.ScheduleCore(entry.Day, entry.Command);
        sim.Advance(ticks);
        return sim;
    }

    /// <summary>Pessimistic tour estimate for a carrier's painted coverage (DESIGN.md §4).</summary>
    public Minutes PreviewTour(int carrierId) => DayPlanSystem.Preview(State, State.Carrier(carrierId));

    /// <summary>Estimate for a tentative coverage still being painted in the UI.</summary>
    public Minutes PreviewTour(int carrierId, IReadOnlyList<int> tentativeCoverage) =>
        DayPlanSystem.Preview(State, State.Carrier(carrierId).Definition, tentativeCoverage);

    public void Advance(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        for (var i = 0; i < ticks; i++)
            AdvanceOneTick();
    }

    /// <summary>Reinstalls a loaded command log: full history kept, not-yet-resolved days re-queued.</summary>
    internal void RestoreLog(IReadOnlyList<CommandLogEntry> entries)
    {
        _log.Clear();
        _pending.Clear();
        foreach (var entry in entries)
        {
            _log.Add(entry);
            if (entry.Day >= State.Tick)
                _pending.Add(entry);
        }
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
        // Hard time cap (RUE-39): refuse to simulate a day past 12345-12-31. A
        // scenario end is otherwise optional (endless is first-class, DESIGN.md
        // §2/§12); this only guards the structural int32-year ceiling.
        if (!Calendar.IsWithinCap(State.Tick))
            throw new InvalidOperationException(
                $"Simulation reached the hard time cap ({SimCalendar.MaxYear}-12-31); it cannot advance beyond it (RUE-39).");

        State.BeginTick();
        ApplyDueCommands(); // commands at tick open (RUE-6)
        foreach (var system in SystemPipeline)
            system.Run(State, Calendar);
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
