using Ruera.Sim.Commands;
using Ruera.Sim.Data;
using Ruera.Sim.World;

namespace Ruera.Sim.Tests;

/// <summary>
/// Toy-map facts used below (data/maps + data/definitions):
/// depot at node 1; edge 2 = nodes 2–3 (300 m) with producer 1 (condo-small,
/// 12 000 g/tick); edge 5 = nodes 6–7 (300 m) with producer 2 (condo-large,
/// 80 000 g/tick, buffer 300 000, interval 2). Calendar: tick 0 = Thu
/// 1880-01-01 (Capodanno, holiday), tick 1 = Fri (working), tick 3 = Sunday.
/// </summary>
public class DayPlanTests
{
    private static readonly StreetGraph Graph =
        MapLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "data", "packages", "base", "maps", "toy.map.json"));

    private static readonly DefinitionRegistry Definitions =
        DefinitionLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "packages", "base", "definitions"));

    private static Simulation WithCarrier(string type, int[] coverage, ulong seed = 1)
    {
        var sim = new Simulation(seed, Graph, Definitions);
        sim.Submit(new AddCarrierCommand(type));
        sim.Advance(1); // resolves the Jan 1 holiday: production only, carrier materializes
        sim.Submit(new SetCoverageCommand(1, coverage));
        return sim;
    }

    [Fact]
    public void ExecutedTour_MatchesHandComputedPlan()
    {
        var sim = WithCarrier("base:navazza", [2]);

        sim.Advance(1); // Fri Jan 2: first working day

        // travel ceil(600/90)=7 + 1 stop (12) + return ceil(600/90)=7 + empty 15 = 41
        var report = Assert.Single(sim.State.LastDayReports);
        Assert.Equal(41, report.MinutesUsed);
        Assert.Equal(1, report.Trips); // single trip: capacity (800 000 g) never runs out (RUE-44)
        Assert.Equal(24_000, report.CollectedGrams); // two days of accumulation
        Assert.Equal([1], report.ServedProducerIds);
        Assert.Equal(0, sim.State.StockpileGrams); // sorted and sold the same tick (RUE-45): idle crew capacity dwarfs 24 000 g
        Assert.Equal(0, sim.State.Producer(1).BufferGrams);
        Assert.Equal(1, sim.State.Producer(1).LastCollectedTick);
    }

    [Fact]
    public void Preview_IsPessimistic_OverlapBenefitOnlyInExecution()
    {
        var sim = new Simulation(1, Graph, Definitions);
        sim.Submit(new AddCarrierCommand("base:navazza"));
        sim.Submit(new AddCarrierCommand("base:navazza"));
        sim.Advance(1);
        sim.Submit(new SetCoverageCommand(1, [2]));
        sim.Submit(new SetCoverageCommand(2, [2]));

        // Tentative preview while painting ignores overlaps: full cost for both.
        Assert.Equal(41, sim.PreviewTour(1, [2]).Value);
        Assert.Equal(41, sim.PreviewTour(2, [2]).Value);

        sim.Advance(1);

        var first = sim.State.LastDayReports[0];
        var second = sim.State.LastDayReports[1];
        Assert.Equal(41, first.MinutesUsed);      // empties the producer
        Assert.Equal(24_000, first.CollectedGrams);
        Assert.Equal(14, second.MinutesUsed);     // passes through: travel only, no stop, no unload
        Assert.Equal(0, second.CollectedGrams);
        Assert.Equal(41, sim.PreviewTour(2).Value); // preview never shows the overlap saving
    }

    [Fact]
    public void UnservedProducers_AccumulateAndViolate()
    {
        var sim = new Simulation(1, Graph, Definitions);

        sim.Advance(4); // no fleet: nothing is ever collected

        var condoLarge = sim.State.Producer(2);
        Assert.Equal(320_000, condoLarge.BufferGrams); // 4 x 80 000 > buffer 300 000
        Assert.Equal(0, condoLarge.LastCollectedTick);
        Assert.Equal(2, condoLarge.ViolationCount); // overflow + sanitary, both on tick 3
        Assert.Contains(new SimEvent(3, SimEventType.BufferOverflow, 2), sim.State.LastTickEvents);
        Assert.Contains(new SimEvent(3, SimEventType.SanitaryViolation, 2), sim.State.LastTickEvents);
    }

    [Fact]
    public void RestDays_ProduceButDoNotCollect()
    {
        var sim = WithCarrier("base:navazza", [2]);

        sim.Advance(3); // Fri (collect), Sat (collect), Sun (rest)

        Assert.Equal(0, sim.State.StockpileGrams); // each working day's collection is sorted and sold same-tick (RUE-45)
        Assert.Equal(12_000, sim.State.Producer(1).BufferGrams); // Sunday's production waits
        Assert.Equal(2, sim.State.Producer(1).LastCollectedTick); // Saturday
        Assert.Empty(sim.State.LastDayReports); // no tours on rest days
    }

    [Fact]
    public void MultiTrip_GerlaDrainsCondoLargeAcrossSevenTrips()
    {
        // RUE-44: the gerla (25 000 g) no longer stops at one partial visit —
        // it shuttles back to the depot and out again until either the edge is
        // drained or the shift budget runs out.
        //
        // Depot is node 1; edge 5 (nodes 6-7, 300 m) carries producer 2
        // (condo-large). Nearest entry to edge 5 from the depot is node 6, at
        // distance 600 m (e.g. 1-2-6 or 1-5-6, both 300+300); the far end
        // (node 7, the exit) is 900 m from the depot by the same routes. So
        // each round trip costs: travel out ceil((600+300)/55)=17, fill 6,
        // travel back ceil(900/55)=17, unload 4 -> 44 min/trip.
        //
        // condo-large accumulated 2 working days' production before this tour
        // (2 x 80 000 = 160 000 g). The gerla's 25 000 g basket needs
        // ceil(160 000 / 25 000) = 7 trips (six full loads of 25 000 g plus a
        // final 10 000 g) to empty it: 7 x 44 = 308 min, well inside the
        // 480-minute shift.
        var sim = WithCarrier("base:gerla", [5]);

        sim.Advance(1);

        var report = Assert.Single(sim.State.LastDayReports);
        Assert.Equal(308, report.MinutesUsed);
        Assert.Equal(7, report.Trips);
        Assert.Equal(160_000, report.CollectedGrams);
        Assert.Equal([2], report.ServedProducerIds);
        Assert.Equal(0, sim.State.StockpileGrams); // sorted and sold the same tick (RUE-45)
        Assert.Equal(0, sim.State.Producer(2).BufferGrams); // fully drained, unlike the old single-trip partial pickup
        Assert.Equal(1, sim.State.Producer(2).LastCollectedTick);
    }

    [Fact]
    public void Preview_MultiTrip_UsesArchetypeCapAsPessimisticBufferAndExceedsBudget()
    {
        // RUE-44 preview rule: each producer is assumed to carry
        // max(archetype buffer, current buffer) — here the condo-large's
        // 300 000 g cap, worse than the ~80-160 000 g actually on the ground.
        // 300 000 / 25 000 = exactly 12 trips of the gerla's basket, each
        // 44 min (see MultiTrip_GerlaDrainsCondoLargeAcrossSevenTrips for the
        // per-trip arithmetic) = 528 min — over the 480-minute shift, which a
        // pessimistic preview is allowed to show (DESIGN.md §4: routes can
        // read past 100% of budget).
        var sim = WithCarrier("base:gerla", [5]);

        var preview = sim.PreviewTour(1, [5]);
        Assert.Equal(528, preview.Value);
        Assert.True(preview.Value > 480);

        sim.Advance(1);

        var report = Assert.Single(sim.State.LastDayReports);
        Assert.True(preview.Value > report.MinutesUsed); // preview is always >= reality
    }

    [Fact]
    public void FleetAndCoverage_AreDeterministicAndReplayable()
    {
        static Simulation Script(ulong seed)
        {
            var sim = new Simulation(seed, Graph, Definitions);
            sim.Submit(new AddCarrierCommand("base:navazza"));
            sim.Advance(1);
            sim.Submit(new SetCoverageCommand(1, [8, 2, 5])); // unsorted on purpose: canonicalized in state
            sim.Advance(29);
            return sim;
        }

        var a = Script(2026);
        var b = Script(2026);
        Assert.Equal(a.StateHash(), b.StateHash());

        var wire = CommandSerializer.Serialize(a.CommandLog);
        var replayed = Simulation.Replay(2026, CommandSerializer.Deserialize(wire), 30, Graph, Definitions);
        Assert.Equal(a.StateHash(), replayed.StateHash());
    }

    [Fact]
    public void WorldCommands_RoundTripThroughSerialization()
    {
        CommandLogEntry[] entries =
        [
            new(0, new AddCarrierCommand("base:gerla")),
            new(2, new SetCoverageCommand(1, [5, 2])),
        ];

        Assert.Equal(entries, CommandSerializer.Deserialize(CommandSerializer.Serialize(entries)));
    }

    [Fact]
    public void InvalidWorldCommands_AreRejectedAtSubmission()
    {
        var sim = new Simulation(1, Graph, Definitions);

        Assert.Throws<ArgumentException>(() => sim.Submit(new AddCarrierCommand("base:zeppelin")));
        Assert.Throws<ArgumentException>(() => sim.Submit(new SetCoverageCommand(1, [2]))); // no carrier yet

        var worldless = new Simulation(1);
        Assert.Throws<ArgumentException>(() => worldless.Submit(new AddCarrierCommand("base:gerla")));
    }
}
