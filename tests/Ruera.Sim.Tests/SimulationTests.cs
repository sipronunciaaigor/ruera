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

        Assert.Equal(0x5b2db317bd31b1dbUL, sim.StateHash());
    }
}
