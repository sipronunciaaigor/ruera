using Ruera.Sim.Calendar;
using Ruera.Sim.Data;
using Ruera.Sim.Hashing;
using Ruera.Sim.Persistence;
using Ruera.Sim.Rng;
using Ruera.Sim.World;

using ScenarioPackage = Ruera.Sim.Scenario.Scenario;

namespace Ruera.Sim;

/// <summary>
/// The complete mutable simulation state. A game is initial state (seed +
/// scenario) plus the player's command stream (DESIGN.md §2): everything the
/// engine mutates must live here and feed the state hash. Graph, definitions
/// and calendar are immutable scenario config, referenced but not hashed here
/// (they enter the scenario-data hash of the save header, RUE-8).
/// </summary>
public sealed class SimState
{
    // Scenario constants (starting endowment) — scenario data eventually (RUE-20).
    private const long StartingCashCents = 500_000; // 5 000 lire
    private const int StartingWorkers = 4;          // trained from day zero

    // Enum.GetValues returns values sorted by underlying value: a stable, documented order.
    private static readonly RngStreamId[] StreamIds = Enum.GetValues<RngStreamId>();

    private readonly Xoshiro256StarStar[] _streams;
    private readonly ProducerState[] _producers; // sorted by id
    private readonly int[] _producerIds;
    private readonly List<CarrierState> _carriers = []; // ids assigned densely: always sorted
    private readonly List<WorkerState> _workers = [];
    private readonly List<(long DeliveryTick, string CarrierTypeId)> _pendingDeliveries = [];
    private readonly List<RouteTemplate> _templates = []; // ids monotonic: always sorted
    private readonly List<SimEvent> _events = [];
    private readonly List<DayPlanReport> _reports = [];
    private readonly List<LineReport> _lineReports = [];

    public ulong Seed { get; }

    /// <summary>Elapsed ticks since the start of the game. One tick = one in-game day.</summary>
    public long Tick { get; internal set; }

    /// <summary>Collected waste sitting at the depot, awaiting processing (RUE-14/16 successors).</summary>
    public long StockpileGrams { get; internal set; }

    /// <summary>Company cash. All accounting is integer cents (DESIGN.md §2).</summary>
    public long CashCents { get; internal set; }

    /// <summary>Wages accrued since the last Saturday payday (RUE-6 cadence).</summary>
    public long WageAccruedCents { get; internal set; }

    /// <summary>Latched true the first time cash closes below zero (DESIGN.md §12).</summary>
    public bool Bankrupt { get; internal set; }

    internal int NextCarrierId { get; private set; } = 1;

    internal int NextWorkerId { get; private set; } = 1;

    internal int NextTemplateId { get; private set; } = 1;

    public SimCalendar Calendar { get; }

    public StreetGraph? Graph { get; }

    public DefinitionRegistry? Definitions { get; }

    /// <summary>Event configuration; null = events disabled (scenario config, like the calendar).</summary>
    public EventSettings? Events { get; }

    /// <summary>
    /// The scenario package this game was loaded from (RUE-38), when built via
    /// <see cref="Simulation.FromScenario"/>. Null for the worldless/legacy
    /// constructors. Used to compute the whole-bundle scenario hash in the save
    /// header; the calendar and events above are derived from it.
    /// </summary>
    public ScenarioPackage? Scenario { get; }

    /// <summary>
    /// Identity of the ordered package set this game was loaded from (RUE-40),
    /// when built through <see cref="Packaging.LoadedPackages.NewSimulation"/>.
    /// Null for single-scenario or worldless/legacy construction. Written to the
    /// save header so a load can verify the exact package set + versions.
    /// </summary>
    public Packaging.PackageSetIdentity? Packages { get; }

    public SimState(ulong seed) : this(seed, SimCalendar.Milano1880(), null, null, null)
    {
    }

    public SimState(ulong seed, SimCalendar calendar, StreetGraph? graph, DefinitionRegistry? definitions,
        EventSettings? events, ScenarioPackage? scenario = null, Packaging.PackageSetIdentity? packages = null)
    {
        Events = events;
        Scenario = scenario;
        Packages = packages;
        if (graph is not null && definitions is not null)
        {
            _producers = [.. graph.Producers.Select(p =>
                new ProducerState(p.Id, p.Edge, definitions.Archetype(p.Archetype)))];
            CashCents = StartingCashCents;
            for (var i = 0; i < StartingWorkers; i++)
                AddWorker(hiredTick: long.MinValue / 2); // trained long before day zero
        }
        else if (graph is null && definitions is null)
        {
            _producers = [];
        }
        else
        {
            throw new ArgumentException("Graph and definitions must be provided together.");
        }

        Seed = seed;
        Calendar = calendar;
        Graph = graph;
        Definitions = definitions;
        _producerIds = [.. _producers.Select(p => p.Id)];
        _streams = new Xoshiro256StarStar[StreamIds.Length];
        for (var i = 0; i < StreamIds.Length; i++)
        {
            // Stream seed = master seed x mixed stream id (SplitMix64), per RUE-7:
            // each system gets an independent, reproducible sequence.
            _streams[i] = new Xoshiro256StarStar(seed ^ SplitMix64.Mix((ulong)StreamIds[i]));
        }
    }

    /// <summary>Producers sorted by id; served/unserved state queryable per producer.</summary>
    public IReadOnlyList<ProducerState> Producers => _producers;

    /// <summary>Fleet in id order.</summary>
    public IReadOnlyList<CarrierState> Carriers => _carriers;

    /// <summary>Staff in id order, trainees included.</summary>
    public IReadOnlyList<WorkerState> Workers => _workers;

    internal bool TryGetWorker(int id, out WorkerState worker)
    {
        foreach (var candidate in _workers) // id order
        {
            if (candidate.Id == id)
            {
                worker = candidate;
                return true;
            }
        }

        worker = null!;
        return false;
    }

    /// <summary>Ordered carriers bought but not yet delivered (RUE-6: scheduled future-tick effects).</summary>
    public IReadOnlyList<(long DeliveryTick, string CarrierTypeId)> PendingDeliveries => _pendingDeliveries;

    /// <summary>Service lines in id order (DESIGN.md §4).</summary>
    public IReadOnlyList<RouteTemplate> Templates => _templates;

    /// <summary>Events emitted while resolving the last tick.</summary>
    public IReadOnlyList<SimEvent> LastTickEvents => _events;

    /// <summary>Per-carrier outcome of the last resolved day.</summary>
    public IReadOnlyList<DayPlanReport> LastDayReports => _reports;

    /// <summary>Per-line totals of the last resolved day (template-driven work only).</summary>
    public IReadOnlyList<LineReport> LastLineReports => _lineReports;

    public RouteTemplate Template(int id) =>
        TryGetTemplate(id, out var template) ? template : throw new KeyNotFoundException($"Unknown route template id {id}.");

    internal bool TryGetTemplate(int id, out RouteTemplate template)
    {
        foreach (var candidate in _templates) // id order
        {
            if (candidate.Id == id)
            {
                template = candidate;
                return true;
            }
        }

        template = null!;
        return false;
    }

    public ProducerState Producer(int id)
    {
        var index = Array.BinarySearch(_producerIds, id);
        return index >= 0 ? _producers[index] : throw new KeyNotFoundException($"Unknown producer id {id}.");
    }

    public CarrierState Carrier(int id) =>
        TryGetCarrier(id, out var carrier) ? carrier : throw new KeyNotFoundException($"Unknown carrier id {id}.");

    public bool TryGetCarrier(int id, out CarrierState carrier)
    {
        var index = id - 1; // dense ids starting at 1
        if (index >= 0 && index < _carriers.Count)
        {
            carrier = _carriers[index];
            return true;
        }

        carrier = null!;
        return false;
    }

    /// <summary>Per-system RNG stream: draws in one system never shift another system's sequence.</summary>
    internal IDeterministicRng Rng(RngStreamId id)
    {
        var index = Array.IndexOf(StreamIds, id);
        return index >= 0
            ? _streams[index]
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown RNG stream.");
    }

    internal int AddCarrier(string typeId)
    {
        var carrier = new CarrierState(NextCarrierId, Definitions!.Carrier(typeId));
        NextCarrierId++;
        _carriers.Add(carrier);
        return carrier.Id;
    }

    internal int AddWorker(long hiredTick)
    {
        var worker = new WorkerState(NextWorkerId, hiredTick);
        NextWorkerId++;
        _workers.Add(worker);
        return worker.Id;
    }

    internal void RemoveWorker(int id)
    {
        // Wages already accrued into WageAccruedCents stay payable at the next
        // Saturday (RUE-33); only future accrual drops with the smaller roster.
        for (var i = 0; i < _workers.Count; i++)
        {
            if (_workers[i].Id == id)
            {
                _workers.RemoveAt(i);
                return;
            }
        }
    }

    internal int AddTemplate(string name, int[] edgeIds, byte weekdayMask)
    {
        var template = new RouteTemplate(NextTemplateId, name, [.. edgeIds.Distinct().OrderBy(id => id)], weekdayMask);
        NextTemplateId++;
        _templates.Add(template);
        return template.Id;
    }

    internal void RemoveTemplate(int id)
    {
        for (var i = 0; i < _templates.Count; i++)
        {
            if (_templates[i].Id == id)
            {
                _templates.RemoveAt(i);
                return;
            }
        }
    }

    internal void ScheduleDelivery(long deliveryTick, string carrierTypeId) =>
        _pendingDeliveries.Add((deliveryTick, carrierTypeId));

    internal void DeliverDue()
    {
        var index = 0;
        while (index < _pendingDeliveries.Count)
        {
            if (_pendingDeliveries[index].DeliveryTick == Tick)
            {
                AddCarrier(_pendingDeliveries[index].CarrierTypeId);
                _pendingDeliveries.RemoveAt(index);
            }
            else
            {
                index++;
            }
        }
    }

    internal void BeginTick()
    {
        _events.Clear();
        _reports.Clear();
        _lineReports.Clear();
    }

    internal void Emit(SimEvent simEvent) => _events.Add(simEvent);

    internal void Report(DayPlanReport report) => _reports.Add(report);

    internal void ReportLine(LineReport report) => _lineReports.Add(report);

    /// <summary>Feeds the canonical field stream into the state hash (same order as snapshots).</summary>
    public void AddToHash(ref Fnv1a64 hasher)
    {
        var writer = new HashStateWriter(hasher);
        WriteTo(writer);
        hasher = writer.Hasher;
    }

    /// <summary>
    /// THE canonical state serialization, format v3 (RUE-14): the single field
    /// order feeding both the state hash and snapshot bytes (RUE-8 «writer
    /// unico»). <see cref="ReadFrom"/> must mirror it exactly.
    /// </summary>
    internal void WriteTo(IStateWriter writer)
    {
        writer.Add(Seed);
        writer.Add(Tick);
        foreach (var stream in _streams) // StreamIds order: stable by construction
            stream.WriteState(writer);

        writer.Add(StockpileGrams);
        writer.Add(NextCarrierId);
        writer.Add(_carriers.Count);
        foreach (var carrier in _carriers) // id order
        {
            writer.Add(carrier.Id);
            writer.Add(carrier.TypeId);
            writer.Add(carrier.CoverageArray.Length);
            foreach (var edgeId in carrier.CoverageArray) // sorted
                writer.Add(edgeId);
            writer.Add(carrier.OutOfServiceUntilTick);
        }

        writer.Add(_producers.Length);
        foreach (var producer in _producers) // id order
        {
            writer.Add(producer.Id);
            writer.Add(producer.BufferGrams);
            writer.Add(producer.LastCollectedTick);
            writer.Add(producer.ViolationCount);
            writer.Add(producer.HasContract);
        }

        writer.Add(CashCents);
        writer.Add(WageAccruedCents);
        writer.Add(Bankrupt);
        writer.Add(NextWorkerId);
        writer.Add(_workers.Count);
        foreach (var worker in _workers) // id order
        {
            writer.Add(worker.Id);
            writer.Add(worker.HiredTick);
        }

        writer.Add(_pendingDeliveries.Count);
        foreach (var (deliveryTick, typeId) in _pendingDeliveries) // scheduling order
        {
            writer.Add(deliveryTick);
            writer.Add(typeId);
        }

        writer.Add(NextTemplateId);
        writer.Add(_templates.Count);
        foreach (var template in _templates) // id order
        {
            writer.Add(template.Id);
            writer.Add(template.Name);
            writer.Add(template.WeekdayMask);
            writer.Add(template.EdgeArray.Length);
            foreach (var edgeId in template.EdgeArray) // sorted
                writer.Add(edgeId);
            writer.Add(template.AssignedArray.Length);
            foreach (var carrierId in template.AssignedArray) // sorted
                writer.Add(carrierId);
        }
    }

    /// <summary>Restores state from canonical snapshot bytes; mirrors <see cref="WriteTo"/>.</summary>
    internal void ReadFrom(BinaryStateReader reader)
    {
        var seed = reader.ReadUInt64();
        if (seed != Seed)
            throw new InvalidDataException("Snapshot seed does not match the save header.");
        Tick = reader.ReadInt64();
        foreach (var stream in _streams)
            stream.SetState(reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64(), reader.ReadUInt64());

        StockpileGrams = reader.ReadInt64();
        NextCarrierId = reader.ReadInt32();
        _carriers.Clear();
        var carrierCount = reader.ReadInt32();
        for (var i = 0; i < carrierCount; i++)
        {
            var id = reader.ReadInt32();
            var carrier = new CarrierState(id, Definitions!.Carrier(reader.ReadString()));
            var coverageCount = reader.ReadInt32();
            var coverage = new int[coverageCount];
            for (var c = 0; c < coverageCount; c++)
                coverage[c] = reader.ReadInt32();
            carrier.SetCoverage(coverage);
            carrier.OutOfServiceUntilTick = reader.ReadInt64();
            _carriers.Add(carrier);
        }

        var producerCount = reader.ReadInt32();
        if (producerCount != _producers.Length)
            throw new InvalidDataException("Snapshot producers do not match the loaded scenario.");
        for (var i = 0; i < producerCount; i++)
        {
            var producer = Producer(reader.ReadInt32());
            producer.BufferGrams = reader.ReadInt64();
            producer.LastCollectedTick = reader.ReadInt64();
            producer.ViolationCount = reader.ReadInt64();
            producer.HasContract = reader.ReadBoolean();
        }

        CashCents = reader.ReadInt64();
        WageAccruedCents = reader.ReadInt64();
        Bankrupt = reader.ReadBoolean();
        NextWorkerId = reader.ReadInt32();
        _workers.Clear();
        var workerCount = reader.ReadInt32();
        for (var i = 0; i < workerCount; i++)
            _workers.Add(new WorkerState(reader.ReadInt32(), reader.ReadInt64()));

        _pendingDeliveries.Clear();
        var deliveryCount = reader.ReadInt32();
        for (var i = 0; i < deliveryCount; i++)
            _pendingDeliveries.Add((reader.ReadInt64(), reader.ReadString()));

        NextTemplateId = reader.ReadInt32();
        _templates.Clear();
        var templateCount = reader.ReadInt32();
        for (var i = 0; i < templateCount; i++)
        {
            var id = reader.ReadInt32();
            var name = reader.ReadString();
            var mask = checked((byte)reader.ReadInt32());
            var edges = new int[reader.ReadInt32()];
            for (var e = 0; e < edges.Length; e++)
                edges[e] = reader.ReadInt32();
            var template = new RouteTemplate(id, name, edges, mask);
            var assigned = new int[reader.ReadInt32()];
            for (var v = 0; v < assigned.Length; v++)
                assigned[v] = reader.ReadInt32();
            template.SetAssignedCarriers(assigned);
            _templates.Add(template);
        }
    }
}
