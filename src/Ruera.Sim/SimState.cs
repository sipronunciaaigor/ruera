using Ruera.Sim.Calendar;
using Ruera.Sim.Data;
using Ruera.Sim.Hashing;
using Ruera.Sim.Rng;
using Ruera.Sim.World;

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
    private readonly List<VehicleState> _vehicles = []; // ids assigned densely: always sorted
    private readonly List<WorkerState> _workers = [];
    private readonly List<(long DeliveryTick, string VehicleTypeId)> _pendingDeliveries = [];
    private readonly List<SimEvent> _events = [];
    private readonly List<DayPlanReport> _reports = [];

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

    internal int NextVehicleId { get; private set; } = 1;

    internal int NextWorkerId { get; private set; } = 1;

    public SimCalendar Calendar { get; }

    public StreetGraph? Graph { get; }

    public DefinitionRegistry? Definitions { get; }

    public SimState(ulong seed) : this(seed, SimCalendar.Milano1880(), null, null)
    {
    }

    public SimState(ulong seed, SimCalendar calendar, StreetGraph? graph, DefinitionRegistry? definitions)
    {
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
    public IReadOnlyList<VehicleState> Vehicles => _vehicles;

    /// <summary>Staff in id order, trainees included.</summary>
    public IReadOnlyList<WorkerState> Workers => _workers;

    /// <summary>Ordered vehicles bought but not yet delivered (RUE-6: scheduled future-tick effects).</summary>
    public IReadOnlyList<(long DeliveryTick, string VehicleTypeId)> PendingDeliveries => _pendingDeliveries;

    /// <summary>Events emitted while resolving the last tick.</summary>
    public IReadOnlyList<SimEvent> LastTickEvents => _events;

    /// <summary>Per-vehicle outcome of the last resolved day.</summary>
    public IReadOnlyList<DayPlanReport> LastDayReports => _reports;

    public ProducerState Producer(int id)
    {
        var index = Array.BinarySearch(_producerIds, id);
        return index >= 0 ? _producers[index] : throw new KeyNotFoundException($"Unknown producer id {id}.");
    }

    public VehicleState Vehicle(int id) =>
        TryGetVehicle(id, out var vehicle) ? vehicle : throw new KeyNotFoundException($"Unknown vehicle id {id}.");

    public bool TryGetVehicle(int id, out VehicleState vehicle)
    {
        var index = id - 1; // dense ids starting at 1
        if (index >= 0 && index < _vehicles.Count)
        {
            vehicle = _vehicles[index];
            return true;
        }

        vehicle = null!;
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

    internal int AddVehicle(string typeId)
    {
        var vehicle = new VehicleState(NextVehicleId, Definitions!.Vehicle(typeId));
        NextVehicleId++;
        _vehicles.Add(vehicle);
        return vehicle.Id;
    }

    internal int AddWorker(long hiredTick)
    {
        var worker = new WorkerState(NextWorkerId, hiredTick);
        NextWorkerId++;
        _workers.Add(worker);
        return worker.Id;
    }

    internal void ScheduleDelivery(long deliveryTick, string vehicleTypeId) =>
        _pendingDeliveries.Add((deliveryTick, vehicleTypeId));

    internal void DeliverDue()
    {
        var index = 0;
        while (index < _pendingDeliveries.Count)
        {
            if (_pendingDeliveries[index].DeliveryTick == Tick)
            {
                AddVehicle(_pendingDeliveries[index].VehicleTypeId);
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
    }

    internal void Emit(SimEvent simEvent) => _events.Add(simEvent);

    internal void Report(DayPlanReport report) => _reports.Add(report);

    /// <summary>
    /// Feeds every piece of state to the hasher in a fixed, documented order.
    /// State format v3 (RUE-14): economy state included.
    /// </summary>
    public void AddToHash(ref Fnv1a64 hasher)
    {
        hasher.Add(Seed);
        hasher.Add(Tick);
        foreach (var stream in _streams) // StreamIds order: stable by construction
            stream.AddState(ref hasher);

        hasher.Add(StockpileGrams);
        hasher.Add(NextVehicleId);
        hasher.Add(_vehicles.Count);
        foreach (var vehicle in _vehicles) // id order
        {
            hasher.Add(vehicle.Id);
            hasher.Add(vehicle.TypeId);
            hasher.Add(vehicle.CoverageArray.Length);
            foreach (var edgeId in vehicle.CoverageArray) // sorted
                hasher.Add(edgeId);
        }

        hasher.Add(_producers.Length);
        foreach (var producer in _producers) // id order
        {
            hasher.Add(producer.Id);
            hasher.Add(producer.BufferGrams);
            hasher.Add(producer.LastCollectedTick);
            hasher.Add(producer.ViolationCount);
            hasher.Add(producer.HasContract);
        }

        hasher.Add(CashCents);
        hasher.Add(WageAccruedCents);
        hasher.Add(Bankrupt);
        hasher.Add(NextWorkerId);
        hasher.Add(_workers.Count);
        foreach (var worker in _workers) // id order
        {
            hasher.Add(worker.Id);
            hasher.Add(worker.HiredTick);
        }

        hasher.Add(_pendingDeliveries.Count);
        foreach (var (deliveryTick, typeId) in _pendingDeliveries) // scheduling order
        {
            hasher.Add(deliveryTick);
            hasher.Add(typeId);
        }
    }
}
