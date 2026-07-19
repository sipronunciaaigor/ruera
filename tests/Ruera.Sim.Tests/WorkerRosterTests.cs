using Ruera.Sim.Commands;
using Ruera.Sim.Data;
using Ruera.Sim.World;

namespace Ruera.Sim.Tests;

/// <summary>
/// Calendar facts: tick 0 = Thu 1880-01-01 (holiday), tick 1 = Fri, tick 2 =
/// Sat (payday), tick 3 = Sun. Scenario start: 4 trained workers, 500 000
/// cents; daily wage 300/worker.
/// </summary>
public class WorkerRosterTests
{
    private static readonly StreetGraph Graph =
        MapLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "data", "maps", "toy.map.json"));

    private static readonly DefinitionRegistry Definitions =
        DefinitionLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "definitions"));

    private static Simulation NewSim(ulong seed = 1) => new(seed, Graph, Definitions);

    [Fact]
    public void Fire_RemovesTheWorkerFromTheRoster()
    {
        var sim = NewSim();
        Assert.Equal(4, sim.State.Workers.Count);

        sim.Submit(new FireWorkerCommand(2));
        sim.Advance(1); // applied at tick 0 open

        Assert.Equal(3, sim.State.Workers.Count);
        Assert.DoesNotContain(sim.State.Workers, w => w.Id == 2);
        Assert.Equal([1, 3, 4], sim.State.Workers.Select(w => w.Id)); // remaining stay in id order
    }

    [Fact]
    public void AccruedWages_StillDue_ButFutureAccrualDrops()
    {
        var fired = NewSim();
        var kept = NewSim();

        fired.Advance(2); // holiday + Friday: both accrue 4 x 300 = 1 200
        kept.Advance(2);
        Assert.Equal(1_200, fired.State.WageAccruedCents);

        fired.Submit(new FireWorkerCommand(1)); // fired at Saturday open: 3 workers today
        fired.Advance(1); // Saturday: accrue 3 x 300 = 900 -> pool 2 100, then pay
        kept.Advance(1);  // Saturday: accrue 4 x 300 = 1 200 -> pool 2 400, then pay

        // Friday's 1 200 is paid in both (accrued-to-date honored); firing only
        // saved the fired worker's Saturday wage (300).
        Assert.Equal(500_000 - 2_100, fired.State.CashCents);
        Assert.Equal(500_000 - 2_400, kept.State.CashCents);
        Assert.Equal(300, fired.State.CashCents - kept.State.CashCents);
    }

    [Fact]
    public void CrewGating_ReflectsTheRosterTheSameTick()
    {
        // 2 navazze need crew 2 each = 4; firing one worker the same day the
        // tour would run drops capacity to 3 -> only one vehicle operates.
        var sim = NewSim();
        sim.Submit(new AddVehicleCommand("base:navazza"));
        sim.Submit(new AddVehicleCommand("base:navazza"));
        sim.Advance(1);
        sim.Submit(new SetCoverageCommand(1, [2]));
        sim.Submit(new SetCoverageCommand(2, [5]));

        sim.Advance(1); // Friday, full roster: both run
        Assert.Equal(2, sim.State.LastDayReports.Count);

        sim.Submit(new FireWorkerCommand(1));
        sim.Advance(1); // Saturday: fired at open, only 3 crew -> one vehicle
        Assert.Single(sim.State.LastDayReports);
    }

    [Fact]
    public void Fire_RejectsUnknownWorker()
    {
        var sim = NewSim();

        Assert.Throws<ArgumentException>(() => sim.Submit(new FireWorkerCommand(99)));

        sim.Submit(new FireWorkerCommand(3));
        sim.Advance(1);
        Assert.Throws<ArgumentException>(() => sim.Submit(new FireWorkerCommand(3))); // already gone
    }

    [Fact]
    public void FireCommand_RoundTripsAndReplaysIdentically()
    {
        CommandLogEntry[] entries = [new(0, new FireWorkerCommand(2)), new(3, new FireWorkerCommand(1))];
        Assert.Equal(entries, CommandSerializer.Deserialize(CommandSerializer.Serialize(entries)));

        static Simulation Script(ulong seed)
        {
            var sim = new Simulation(seed, Graph, Definitions);
            sim.Submit(new HireWorkerCommand());
            sim.Submit(new FireWorkerCommand(2));
            sim.Advance(3);
            sim.Submit(new FireWorkerCommand(5)); // the newly hired one
            sim.Advance(30);
            return sim;
        }

        var original = Script(2026);
        var replayed = Simulation.Replay(2026,
            CommandSerializer.Deserialize(CommandSerializer.Serialize(original.CommandLog)), 33, Graph, Definitions);
        Assert.Equal(original.StateHash(), replayed.StateHash());
    }
}
