using Ruera.Sim.Commands;
using Ruera.Sim.Data;
using Ruera.Sim.World;

namespace Ruera.Sim.Tests;

/// <summary>
/// Calendar facts used below: tick 0 = Thu 1880-01-01 (Capodanno, holiday),
/// tick 1 = Fri, tick 2 = Sat (payday), tick 3 = Sun, tick 31 = Feb 1.
/// Scenario start: 500 000 cents, 4 trained workers. First fines land on
/// tick 3 (condo-large: overflow + sanitary = 1 000 cents).
/// </summary>
public class EconomyTests
{
    private static readonly StreetGraph Graph =
        MapLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "data", "maps", "toy.map.json"));

    private static readonly DefinitionRegistry Definitions =
        DefinitionLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "definitions"));

    private static Simulation NewSim(ulong seed = 1) => new(seed, Graph, Definitions);

    [Fact]
    public void Wages_AccruePerWorkedDay_AndPayAtSaturdayClose()
    {
        var sim = NewSim();

        sim.Advance(2); // holiday + Friday: accrued, not yet paid
        Assert.Equal(500_000, sim.State.CashCents);
        Assert.Equal(1_200, sim.State.WageAccruedCents); // Friday only: 4 workers x 300

        sim.Advance(1); // Saturday: second working day accrues, then payday
        Assert.Equal(497_600, sim.State.CashCents);
        Assert.Equal(0, sim.State.WageAccruedCents);
    }

    [Fact]
    public void Fines_PostAtTheAssessmentTick()
    {
        var sim = NewSim();

        sim.Advance(3);
        var cashBeforeSunday = sim.State.CashCents;

        sim.Advance(1); // Sunday: no wages, no work — only condo-large's two violations
        Assert.Equal(cashBeforeSunday - 2 * 500, sim.State.CashCents);
    }

    [Fact]
    public void Contracts_PayPreviousMonthOnTheFirstTickOfTheMonth()
    {
        var with = NewSim();
        var without = NewSim();
        with.Submit(new SignContractCommand(1)); // condo-small: 3 000/month

        with.Advance(31); // Jan 1 .. Jan 31: no month boundary crossed yet
        without.Advance(31);
        Assert.Equal(without.State.CashCents, with.State.CashCents);

        with.Advance(1); // tick 31 = Feb 1: previous month's fee posts
        without.Advance(1);
        Assert.Equal(without.State.CashCents + 3_000, with.State.CashCents);
    }

    [Fact]
    public void PurchasedCarrier_PaysAtOrder_ArrivesAtDeliveryTick()
    {
        var sim = NewSim();
        sim.Submit(new BuyCarrierCommand("base:navazza")); // 60 000 cents, 5-tick delivery

        sim.Advance(1);
        Assert.Equal(440_000, sim.State.CashCents); // paid at the order tick
        Assert.Empty(sim.State.Carriers);
        Assert.Single(sim.State.PendingDeliveries);

        sim.Advance(4); // ticks 1-4: still in transit
        Assert.Empty(sim.State.Carriers);

        sim.Advance(1); // tick 5: delivered
        var carrier = Assert.Single(sim.State.Carriers);
        Assert.Equal("base:navazza", carrier.TypeId);
        Assert.Empty(sim.State.PendingDeliveries);
    }

    [Fact]
    public void EraGating_BlocksCarriersFromTheFuture()
    {
        var sim = NewSim();

        Assert.Throws<ArgumentException>(() => sim.Submit(new BuyCarrierCommand("base:camion"))); // 1925 tech in 1880
    }

    [Fact]
    public void HiredWorkers_AreCostOnlyDuringTraining()
    {
        var sim = NewSim();
        for (var i = 0; i < 3; i++)
            sim.Submit(new AddCarrierCommand("base:navazza")); // 3 carriers x crew 2 = 6 needed
        sim.Submit(new HireWorkerCommand());
        sim.Submit(new HireWorkerCommand()); // workers 5-6, in training until tick 10
        sim.Advance(1);
        sim.Submit(new SetCoverageCommand(1, [2]));
        sim.Submit(new SetCoverageCommand(2, [5]));
        sim.Submit(new SetCoverageCommand(3, [8]));

        sim.Advance(1); // Friday: only 4 productive workers -> carriers 1 and 2 operate
        Assert.Equal(2, sim.State.LastDayReports.Count);

        sim.Advance(10); // through tick 11 (Monday): trainees productive from tick 10
        Assert.Equal(3, sim.State.LastDayReports.Count);
    }

    [Fact]
    public void Bankruptcy_LatchesWhenCashClosesNegative()
    {
        var sim = NewSim();
        for (var i = 0; i < 20; i++)
            sim.Submit(new HireWorkerCommand()); // payroll far beyond revenue

        sim.Advance(60);

        Assert.True(sim.State.Bankrupt);
        Assert.True(sim.State.CashCents < 0);
    }

    [Fact]
    public void EconomyCommands_RoundTripAndReplayIdentically()
    {
        CommandLogEntry[] entries =
        [
            new(0, new BuyCarrierCommand("base:navazza")),
            new(0, new HireWorkerCommand()),
            new(4, new SignContractCommand(1)),
        ];
        Assert.Equal(entries, CommandSerializer.Deserialize(CommandSerializer.Serialize(entries)));

        static Simulation Script(ulong seed)
        {
            var sim = new Simulation(seed, Graph, Definitions);
            sim.Submit(new BuyCarrierCommand("base:navazza"));
            sim.Submit(new HireWorkerCommand());
            sim.Submit(new SignContractCommand(1));
            sim.Advance(6); // delivery lands at tick 5
            sim.Submit(new SetCoverageCommand(1, [2]));
            sim.Advance(30);
            return sim;
        }

        var original = Script(2026);
        var wire = CommandSerializer.Serialize(original.CommandLog);
        var replayed = Simulation.Replay(2026, CommandSerializer.Deserialize(wire), 36, Graph, Definitions);
        Assert.Equal(original.StateHash(), replayed.StateHash());
    }
}
