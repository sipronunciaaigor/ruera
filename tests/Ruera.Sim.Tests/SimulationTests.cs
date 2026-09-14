using Ruera.Sim.Commands;
using Ruera.Sim.Packaging;
using Ruera.Sim.Rng;

namespace Ruera.Sim.Tests;

public class SimulationTests
{
    [Fact]
    public void SameSeedAndTicks_YieldIdenticalHash()
    {
        var a = new Simulation(42);
        var b = new Simulation(42);

        a.Advance(365);
        b.Advance(365);

        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void DifferentSeeds_YieldDifferentHash()
    {
        var a = new Simulation(1);
        var b = new Simulation(2);

        a.Advance(10);
        b.Advance(10);

        Assert.NotEqual(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void AdvancingChangesHash()
    {
        var sim = new Simulation(7);
        var before = sim.StateHash();

        sim.Advance(1);

        Assert.NotEqual(before, sim.StateHash());
    }

    [Fact]
    public void Advance_RejectsNegativeTicks()
    {
        var sim = new Simulation(0);

        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Advance(-1));
    }

    [Fact]
    public void AdvanceChunking_DoesNotAffectState()
    {
        // Real time / frame rate must never leak into the sim: advancing one
        // year in a single call or day by day must produce identical state.
        var oneCall = new Simulation(99);
        var dayByDay = new Simulation(99);

        oneCall.Advance(365);
        for (var i = 0; i < 365; i++)
            dayByDay.Advance(1);

        Assert.Equal(oneCall.StateHash(), dayByDay.StateHash());
    }

    [Fact]
    public void RngDraws_AreCapturedInStateHash()
    {
        var pristine = new Simulation(5);
        var drawn = new Simulation(5);

        drawn.State.Rng(RngStreamId.Economy).NextUInt64();

        Assert.NotEqual(pristine.StateHash(), drawn.StateHash());
    }

    [Fact]
    public void SameSeedSameInputs_IdenticalHashAtEveryCheckpoint()
    {
        // Same seed + same input sequence (ticks interleaved with rng draws)
        // must match at every checkpoint, not just at the end.
        var a = new Simulation(2026);
        var b = new Simulation(2026);

        static void Script(Simulation sim)
        {
            sim.Advance(100);
            for (var i = 0; i < 10; i++)
                sim.State.Rng(RngStreamId.Collection).NextInt64(0, 1000);
            sim.Advance(265);
        }

        a.Advance(100);
        b.Advance(100);
        Assert.Equal(a.StateHash(), b.StateHash());

        Script(a);
        Script(b);
        Assert.Equal(a.StateHash(), b.StateHash());
    }

    [Fact]
    public void GoldenHash_Seed42_365Ticks()
    {
        // Golden value (DESIGN.md §2 test strategy): pins the state layout and
        // hash across refactors, .NET versions and platforms. Update only
        // deliberately, when the state format intentionally changes.
        var sim = new Simulation(42);
        sim.Advance(365);

        // Updated 2026-09-14 (RUE-45): state format v6 adds SortedGrams to the
        // hash — a conscious SimVersion bump, like v4 (RUE-31) and v5 (RUE-32)
        // before it. The worldless run never touches SortedGrams (it stays 0:
        // no graph, so Processing/Sales never fire), but the byte stream still
        // shifts because a new field entered the canonical writer.
        Assert.Equal(0xd5e6c14cbc0c018eUL, sim.StateHash());
    }

    [Fact]
    public void WorldGoldenHash_TwoNavazze_Seed42_365Ticks()
    {
        // World-level golden (RUE-46, same discipline as GoldenHash above, but
        // exercising a full world: graph, day plan, processing/sales, events,
        // economy). Script matches the "two-navazze" strategy of
        // Ruera.Cli play (inlined here so this test never depends on the CLI).
        // From now on, any change that moves this world's trajectory must
        // update the pinned hash consciously.
        var packages = ContentLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "packages"));
        var sim = packages.NewSimulation(42UL, "base:milano-1880");
        var state = sim.State;

        foreach (var producer in state.Producers)
            sim.Submit(new SignContractCommand(producer.Id));
        sim.Submit(new BuyCarrierCommand("base:navazza"));
        sim.Submit(new BuyCarrierCommand("base:navazza"));
        sim.Advance(6); // purchase deliveries land

        int[] allProducerEdges = [.. state.Producers.Select(p => p.EdgeId).Distinct().OrderBy(e => e)];
        for (var carrierId = 1; carrierId <= state.Carriers.Count; carrierId++)
        {
            var coverage = allProducerEdges.Where((_, i) => i % state.Carriers.Count == carrierId - 1).ToArray();
            sim.Submit(new SetCoverageCommand(carrierId, coverage));
        }

        sim.Advance(365); // one full year of play

        // Tuning pass (RUE-46): base:mixed sale price 2 -> 8 c/kg, so the
        // two-navazze strategy clears the year with more cash than it
        // started with (verified by Ruera.Cli play, not re-asserted here —
        // this test's job is only to freeze the resulting trajectory).
        Assert.Equal(0xa0955b225d4f0b3aUL, sim.StateHash());
    }
}
