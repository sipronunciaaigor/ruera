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
}
