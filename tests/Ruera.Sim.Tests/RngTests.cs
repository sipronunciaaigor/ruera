using Ruera.Sim.Rng;

namespace Ruera.Sim.Tests;

public class RngTests
{
    [Fact]
    public void KnownState_ProducesReferenceSequence()
    {
        // xoshiro256** with raw state {1, 2, 3, 4}; expected values derived
        // from the reference algorithm by hand, independent of this codebase.
        var rng = new Xoshiro256StarStar(1, 2, 3, 4);

        Assert.Equal(11520UL, rng.NextUInt64());
        Assert.Equal(0UL, rng.NextUInt64());
        Assert.Equal(1509978240UL, rng.NextUInt64());
    }

    [Fact]
    public void SameSeed_ProducesSameSequence()
    {
        var a = new Xoshiro256StarStar(123456789UL);
        var b = new Xoshiro256StarStar(123456789UL);

        for (var i = 0; i < 100; i++)
            Assert.Equal(a.NextUInt64(), b.NextUInt64());
    }

    [Fact]
    public void Streams_AreIndependent()
    {
        // Draining one system's stream must not shift another system's sequence.
        var drained = new SimState(7);
        var untouched = new SimState(7);

        for (var i = 0; i < 100; i++)
            drained.Rng(RngStreamId.Economy).NextUInt64();

        for (var i = 0; i < 20; i++)
            Assert.Equal(untouched.Rng(RngStreamId.Collection).NextUInt64(),
                drained.Rng(RngStreamId.Collection).NextUInt64());
    }

    [Fact]
    public void NextInt64_StaysInRange()
    {
        var rng = new Xoshiro256StarStar(0UL);

        for (var i = 0; i < 1000; i++)
        {
            var value = rng.NextInt64(-5, 5);
            Assert.InRange(value, -5, 4);
        }
    }

    [Fact]
    public void NextInt64_SingleValueRange_ReturnsMin()
    {
        var rng = new Xoshiro256StarStar(1UL);

        Assert.Equal(9, rng.NextInt64(9, 10));
    }

    [Fact]
    public void NextInt64_RejectsEmptyRange()
    {
        var rng = new Xoshiro256StarStar(1UL);

        Assert.Throws<ArgumentOutOfRangeException>(() => rng.NextInt64(5, 5));
    }

    [Fact]
    public void AllZeroRawState_IsRejected()
    {
        Assert.Throws<ArgumentException>(() => new Xoshiro256StarStar(0, 0, 0, 0));
    }
}
