using Ruera.Sim.Commands;
using Ruera.Sim.Data;
using Ruera.Sim.Persistence;
using Ruera.Sim.World;

namespace Ruera.Sim.Tests;

/// <summary>
/// Calendar facts: tick 0 = Thu 1880-01-01 (holiday), ticks 1-2 working,
/// tick 3 = Sunday. Navazza costs 60 000 cents; repair at 500 bps = 3 000.
/// Condo-large (producer 2) first violates at tick 3 close.
/// </summary>
public class EventsTests
{
    private static readonly StreetGraph Graph =
        MapLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "data", "maps", "toy.map.json"));

    private static readonly DefinitionRegistry Definitions =
        DefinitionLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "definitions"));

    private static EventSettings Only(int breakdown = 0, int inspection = 0, int tender = 0) => new(
        BreakdownChanceBps: breakdown, RepairTicks: 5, RepairCostBpsOfPurchase: 500,
        InspectionChanceBps: inspection, InspectionFineCents: 2_000,
        TenderChanceBps: tender, TenderDeadlineTicks: 30);

    [Fact]
    public void Breakdown_RemovesTheVehicleFromDayPlans()
    {
        var broken = new Simulation(7, Graph, Definitions, Only(breakdown: 10_000)); // breaks whenever eligible
        var quiet = new Simulation(7, Graph, Definitions);
        foreach (var sim in (Simulation[])[broken, quiet])
        {
            sim.Submit(new AddVehicleCommand("base:navazza"));
            sim.Advance(1);
            sim.Submit(new SetCoverageCommand(1, [2]));
        }

        var breakdowns = 0;
        for (var i = 0; i < 15; i++)
        {
            broken.Advance(1);
            quiet.Advance(1);
            Assert.Empty(broken.State.LastDayReports); // always in the workshop
            if (quiet.Calendar.IsWorkingDay(quiet.Tick - 1))
                Assert.Single(quiet.State.LastDayReports); // same fleet, no events: it works
            foreach (var simEvent in broken.State.LastTickEvents)
            {
                if (simEvent.Type == SimEventType.VehicleBreakdown)
                {
                    breakdowns++;
                    Assert.Equal(1, simEvent.EntityId);
                    Assert.Equal(simEvent.Tick + 5, simEvent.Data); // out until tick + RepairTicks
                }
            }
        }

        Assert.True(breakdowns >= 2);
    }

    [Fact]
    public void Breakdown_PostsRepairCost_AndRespectsTheRepairWindow()
    {
        // Vehicle without coverage: tours never differ, so the cash delta vs a
        // no-events twin is exactly repairs.
        var withEvents = new Simulation(11, Graph, Definitions, Only(breakdown: 5_000));
        var quiet = new Simulation(11, Graph, Definitions);
        withEvents.Submit(new AddVehicleCommand("base:navazza"));
        quiet.Submit(new AddVehicleCommand("base:navazza"));

        var breakdownTicks = new List<long>();
        for (var i = 0; i < 60; i++)
        {
            withEvents.Advance(1);
            quiet.Advance(1);
            foreach (var simEvent in withEvents.State.LastTickEvents)
            {
                if (simEvent.Type == SimEventType.VehicleBreakdown)
                    breakdownTicks.Add(simEvent.Tick);
            }
        }

        Assert.True(breakdownTicks.Count >= 1);
        for (var i = 1; i < breakdownTicks.Count; i++)
            Assert.True(breakdownTicks[i] >= breakdownTicks[i - 1] + 5); // no re-break while in the workshop
        Assert.Equal(quiet.State.CashCents - 3_000 * breakdownTicks.Count, withEvents.State.CashCents);
    }

    [Fact]
    public void Inspection_FinesStandingViolationsAtTheAssessmentTick()
    {
        var inspected = new Simulation(1, Graph, Definitions, Only(inspection: 10_000)); // inspects every working day
        var quiet = new Simulation(1, Graph, Definitions);

        inspected.Advance(5); // ticks 0-4: no fleet; at tick 4 two producers stand in violation
        quiet.Advance(5);

        // Tick 4 inspection: condo-large (over buffer) + factory (over its 3-tick
        // interval) = 2 x 2 000. Ticks 1-2 inspected clean, tick 3 is Sunday.
        Assert.Contains(new SimEvent(4, SimEventType.SanitaryInspection, 0, 4_000), inspected.State.LastTickEvents);
        Assert.Equal(quiet.State.CashCents - 4_000, inspected.State.CashCents);
    }

    [Fact]
    public void Tender_AnnouncesAnUnsignedProducerWithDeadline()
    {
        var sim = new Simulation(3, Graph, Definitions, Only(tender: 10_000));

        sim.Advance(2); // tick 1: first working day

        var tender = Assert.Single(sim.State.LastTickEvents);
        Assert.Equal(SimEventType.TenderAnnounced, tender.Type);
        Assert.InRange(tender.EntityId, 1, 6);
        Assert.Equal(31, tender.Data); // deadline = tick 1 + 30

        sim.Submit(new SignContractCommand(tender.EntityId));
        for (var i = 0; i < 20; i++)
        {
            sim.Advance(1);
            foreach (var simEvent in sim.State.LastTickEvents)
            {
                if (simEvent.Type == SimEventType.TenderAnnounced)
                    Assert.NotEqual(tender.EntityId, simEvent.EntityId); // signed producers are never re-announced
            }
        }
    }

    [Fact]
    public void EventsAreDeterministic_AcrossRunsReplayAndSaveLoad()
    {
        static Simulation Script(ulong seed)
        {
            var sim = new Simulation(seed, Graph, Definitions, EventSettings.Default);
            sim.Submit(new AddVehicleCommand("base:navazza"));
            sim.Submit(new HireWorkerCommand());
            sim.Advance(1);
            sim.Submit(new SetCoverageCommand(1, [2, 5]));
            sim.Advance(59);
            return sim;
        }

        var a = Script(2077);
        var b = Script(2077);
        Assert.Equal(a.StateHash(), b.StateHash());

        var replayed = Simulation.Replay(2077, a.CommandLog, 60, Graph, Definitions, EventSettings.Default);
        Assert.Equal(a.StateHash(), replayed.StateHash());

        var loaded = SaveSystem.Load(SaveSystem.Save(a), Graph, Definitions, events: EventSettings.Default);
        loaded.Advance(30);
        b.Advance(30);
        Assert.Equal(b.StateHash(), loaded.StateHash());
    }
}
