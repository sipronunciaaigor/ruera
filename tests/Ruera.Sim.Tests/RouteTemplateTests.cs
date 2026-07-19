using Ruera.Sim.Calendar;
using Ruera.Sim.Commands;
using Ruera.Sim.Data;
using Ruera.Sim.World;

namespace Ruera.Sim.Tests;

/// <summary>
/// Calendar facts: tick 0 = Thu 1880-01-01 (holiday), tick 1 = Fri,
/// tick 2 = Sat, tick 3 = Sun. Producer 1 (base:condo-small, 12 000 g/tick)
/// sits on edge 2; the navazza tour on edge 2 costs 41 minutes.
/// </summary>
public class RouteTemplateTests
{
    private static readonly StreetGraph Graph =
        MapLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "data", "maps", "toy.map.json"));

    private static readonly DefinitionRegistry Definitions =
        DefinitionLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "definitions"));

    private static Simulation LineSim(byte mask, int carriers = 1)
    {
        var sim = new Simulation(1, Graph, Definitions);
        for (var i = 0; i < carriers; i++)
            sim.Submit(new AddCarrierCommand("base:navazza"));
        sim.Submit(new CreateRouteTemplateCommand("Giro Nord", [2], mask));
        sim.Advance(1); // carrier(s) + template exist from tick 0 (holiday: no collection)
        sim.Submit(new SetTemplateCarriersCommand(1, [.. Enumerable.Range(1, carriers)]));
        return sim;
    }

    [Fact]
    public void WeekdaySchedule_CollectsOnlyOnScheduledDays()
    {
        var sim = LineSim(RouteTemplate.Mask(Weekday.Friday));

        sim.Advance(1); // Friday: line active
        var report = Assert.Single(sim.State.LastDayReports);
        Assert.Equal(41, report.MinutesUsed);
        Assert.Equal(24_000, report.CollectedGrams);
        var line = Assert.Single(sim.State.LastLineReports);
        Assert.Equal(1, line.TemplateId);
        Assert.Equal(1, line.CarriersDispatched);
        Assert.Equal(24_000, line.CollectedGrams);
        Assert.Equal([1], line.ServedProducerIds);

        sim.Advance(1); // Saturday: not scheduled — no tour, waste waits
        Assert.Empty(sim.State.LastDayReports);
        Assert.Empty(sim.State.LastLineReports);
        Assert.Equal(12_000, sim.State.Producer(1).BufferGrams);
    }

    [Fact]
    public void LineTotals_AggregateAllDispatchedCarriers()
    {
        var sim = LineSim(RouteTemplate.Mask(Weekday.Friday), carriers: 2); // "camion 1 e 2, ven"

        sim.Advance(1); // Friday: first empties, second passes through

        Assert.Equal(2, sim.State.LastDayReports.Count);
        var line = Assert.Single(sim.State.LastLineReports);
        Assert.Equal(2, line.CarriersDispatched);
        Assert.Equal(24_000, line.CollectedGrams);
        Assert.Equal([1], line.ServedProducerIds);
    }

    [Fact]
    public void DirectCoverage_OverridesAssignedLines()
    {
        var sim = LineSim(RouteTemplate.Mask(Weekday.Friday));
        sim.Submit(new SetCoverageCommand(1, [5])); // painted override (DESIGN.md §4)

        sim.Advance(1); // Friday

        var report = Assert.Single(sim.State.LastDayReports);
        Assert.Equal([2], report.ServedProducerIds); // producer on edge 5, not the line's
        var line = Assert.Single(sim.State.LastLineReports);
        Assert.Equal(0, line.CarriersDispatched); // line-driven work only
        Assert.Equal(0, line.CollectedGrams);
    }

    [Fact]
    public void UpdateAndDelete_ChangeTheScheduleDeterministically()
    {
        var sim = LineSim(RouteTemplate.Mask(Weekday.Friday));
        sim.Submit(new UpdateRouteTemplateCommand(1, "Giro Nord", [2], RouteTemplate.Mask(Weekday.Saturday)));

        sim.Advance(1); // Friday: rescheduled to Saturday at tick open
        Assert.Empty(sim.State.LastDayReports);

        sim.Advance(1); // Saturday: collects three days of accumulation
        Assert.Equal(36_000, Assert.Single(sim.State.LastDayReports).CollectedGrams);

        sim.Submit(new DeleteRouteTemplateCommand(1));
        sim.Advance(7); // through next Saturday: nothing runs anymore
        Assert.Empty(sim.State.Templates);
        Assert.Equal(84_000, sim.State.Producer(1).BufferGrams); // 7 uncollected days
    }

    [Fact]
    public void TemplateCommands_RoundTripAndReplayIdentically()
    {
        CommandLogEntry[] entries =
        [
            new(0, new CreateRouteTemplateCommand("Giro Navigli", [2, 5], RouteTemplate.Mask(Weekday.Monday, Weekday.Thursday))),
            new(1, new UpdateRouteTemplateCommand(1, "Giro Navigli 2", [2, 8], RouteTemplate.Mask(Weekday.Friday))),
            new(1, new SetTemplateCarriersCommand(1, [1])),
            new(2, new DeleteRouteTemplateCommand(1)),
        ];
        Assert.Equal(entries, CommandSerializer.Deserialize(CommandSerializer.Serialize(entries)));

        static Simulation Script(ulong seed)
        {
            var sim = new Simulation(seed, Graph, Definitions);
            sim.Submit(new AddCarrierCommand("base:navazza"));
            sim.Submit(new CreateRouteTemplateCommand("Giro A", [2, 5], RouteTemplate.Mask(Weekday.Friday, Weekday.Monday)));
            sim.Advance(1);
            sim.Submit(new SetTemplateCarriersCommand(1, [1]));
            sim.Submit(new CreateRouteTemplateCommand("Giro B", [8], RouteTemplate.Mask(Weekday.Tuesday)));
            sim.Advance(5);
            sim.Submit(new DeleteRouteTemplateCommand(2));
            sim.Advance(20);
            return sim;
        }

        var original = Script(2026);
        var replayed = Simulation.Replay(2026,
            CommandSerializer.Deserialize(CommandSerializer.Serialize(original.CommandLog)), 26, Graph, Definitions);
        Assert.Equal(original.StateHash(), replayed.StateHash());
    }

    [Fact]
    public void InvalidTemplateCommands_AreRejectedAtSubmission()
    {
        var sim = new Simulation(1, Graph, Definitions);
        sim.Submit(new AddCarrierCommand("base:navazza"));
        sim.Advance(1);
        sim.Submit(new CreateRouteTemplateCommand("Giro", [2], RouteTemplate.Mask(Weekday.Friday)));

        Assert.Throws<ArgumentException>(() => sim.Submit(new CreateRouteTemplateCommand("Giro", [99], 1)));
        Assert.Throws<ArgumentException>(() => sim.Submit(new CreateRouteTemplateCommand("Giro", [2], 0)));
        Assert.Throws<ArgumentException>(() => sim.Submit(new CreateRouteTemplateCommand("Giro", [2], 255)));
        Assert.Throws<ArgumentException>(() => sim.Submit(new SetTemplateCarriersCommand(9, [1])));
        Assert.Throws<ArgumentException>(() => sim.Submit(new UpdateRouteTemplateCommand(9, "X", [2], 1)));

        sim.Advance(1); // template 1 applied
        Assert.Throws<ArgumentException>(() => sim.Submit(new SetTemplateCarriersCommand(1, [42]))); // unknown carrier
    }
}
