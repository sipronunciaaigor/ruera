using Ruera.Sim.Commands;
using Ruera.Sim.Data;
using Ruera.Sim.Persistence;
using Ruera.Sim.World;

namespace Ruera.Sim.Tests;

/// <summary>
/// RUE-45: processing (cernita) and sales, tick-order steps 5 and 6.
/// Toy-map facts (see DayPlanTests.cs header): depot at node 1; edge 2 =
/// producer 1 (condo-small, 12 000 g/tick); tick 0 = Thu 1880-01-01
/// (Capodanno, holiday), tick 1 = Fri, tick 2 = Sat, tick 3 = Sun (rest).
/// The scenario starts with 4 trained workers (RUE-43 default).
/// </summary>
public class ProcessingSalesTests
{
    private static readonly StreetGraph Graph =
        MapLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "data", "packages", "base", "maps", "toy.map.json"));

    private static readonly DefinitionRegistry Definitions =
        DefinitionLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "packages", "base", "definitions"));

    [Fact]
    public void Processing_CapsSortingByIdleWorkers_NotTotalWorkers()
    {
        // 4 trained workers; the navazza's crew (2) is busy driving, leaving 2
        // idle. Idle capacity = 2 x 3000 = 6000 g, well under the 24 000 g
        // collected, so exactly 6000 g gets sorted (and sold) -- not
        // 4 x 3000 = 12 000, which is what a crew-blind cap would give.
        var economy = EconomySettings.Default with { SortingGramsPerWorkerDay = 3000 };
        var sim = new Simulation(1, Graph, Definitions, economy: economy);
        sim.Submit(new AddCarrierCommand("base:navazza"));
        sim.Advance(1); // Jan 1 (Thu, holiday): production only, carrier materializes
        sim.Submit(new SetCoverageCommand(1, [2]));

        sim.Advance(1); // Jan 2 (Fri): collects 24 000 g (two days' production)

        Assert.Equal(24_000 - 6_000, sim.State.StockpileGrams);
        Assert.Equal(0, sim.State.SortedGrams); // sold the same tick it was sorted
        Assert.Contains(new SimEvent(1, SimEventType.MaterialSold, 0, 48), sim.State.LastTickEvents); // 6000 g @ 8 c/kg = 48 cents
    }

    [Fact]
    public void NoProcessing_OnRestDays_LeftoverStockpileCarriesOverUntouched()
    {
        // Deliberately tiny sorting rate so a day's collection can't be fully
        // processed in one day, leaving a leftover stockpile that proves
        // Sunday (a rest day) doesn't touch it -- same gate as DayPlanSystem.
        var economy = EconomySettings.Default with { SortingGramsPerWorkerDay = 5000 };
        var sim = new Simulation(1, Graph, Definitions, economy: economy);
        sim.Submit(new AddCarrierCommand("base:navazza")); // crew 2 -> 2 idle workers/day
        sim.Advance(1); // Jan 1 (Thu, holiday)
        sim.Submit(new SetCoverageCommand(1, [2]));

        sim.Advance(2); // Jan 2 (Fri) collects 24 000 g; Jan 3 (Sat) collects 12 000 g more
        // Fri: idle capacity 2 x 5000 = 10 000; sorts 10 000 of 24 000 -> stockpile 14 000.
        // Sat: +12 000 collected = 26 000; sorts another 10 000 -> stockpile 16 000.
        Assert.Equal(16_000, sim.State.StockpileGrams);
        Assert.Equal(0, sim.State.SortedGrams);

        sim.Advance(1); // Jan 4 (Sun): rest day -- no collection, no processing
        Assert.Equal(16_000, sim.State.StockpileGrams); // untouched
        Assert.Equal(0, sim.State.SortedGrams);
        Assert.DoesNotContain(sim.State.LastTickEvents, e => e.Type == SimEventType.MaterialSold);
    }

    [Fact]
    public void Sale_PricesExactlyAtBaseSaleCentsPerKg()
    {
        // Default economy: sortingGramsPerWorkerDay (400 000) easily covers
        // the toy map's daily volumes, so the whole 24 000 g is sorted and
        // sold the same tick it is collected. base:mixed sells at 8 c/kg
        // (tuned in RUE-46 so material sales meaningfully move the needle).
        var sim = new Simulation(1, Graph, Definitions);
        sim.Submit(new AddCarrierCommand("base:navazza"));
        sim.Advance(1);
        sim.Submit(new SetCoverageCommand(1, [2]));

        sim.Advance(1); // Jan 2 (Fri): collects, sorts and sells 24 000 g

        Assert.Equal(0, sim.State.StockpileGrams);
        Assert.Equal(0, sim.State.SortedGrams);
        Assert.Contains(new SimEvent(1, SimEventType.MaterialSold, 0, 192), sim.State.LastTickEvents); // 24 000 g x 8 c/kg / 1000 = 192 cents
    }

    [Fact]
    public void SortedGrams_RoundTripsThroughSaveAndLoad()
    {
        // In normal play SalesSystem always drains SortedGrams the same tick
        // it is filled (DESIGN.md §8 "vendita immediata"), so it is never
        // observably nonzero between ticks. Set it directly (internal setter,
        // same assembly via InternalsVisibleTo) to prove the writer/reader
        // pair still round-trips the field correctly regardless.
        var sim = new Simulation(1, Graph, Definitions);
        sim.Advance(1);
        sim.State.SortedGrams = 12_345;
        var expectedHash = sim.StateHash();

        var loaded = SaveSystem.Load(SaveSystem.Save(sim), Graph, Definitions);

        Assert.Equal(expectedHash, loaded.StateHash()); // load's own self-check against the registered hash passed
        Assert.Equal(12_345, loaded.State.SortedGrams);
    }
}
