namespace Ruera.Sim.Tests;

public class IntMathTests
{
    [Theory]
    [InlineData(7, 2, 3)]
    [InlineData(-7, 2, -4)]
    [InlineData(7, -2, -4)]
    [InlineData(-7, -2, 3)]
    [InlineData(6, 3, 2)]
    [InlineData(-6, 3, -2)]
    public void DivFloor_RoundsTowardNegativeInfinity(long dividend, long divisor, long expected)
    {
        Assert.Equal(expected, IntMath.DivFloor(dividend, divisor));
    }

    [Theory]
    [InlineData(7, 2, 4)]   // 3.5 rounds away from zero
    [InlineData(-7, 2, -4)]
    [InlineData(5, 2, 3)]   // 2.5 rounds away from zero
    [InlineData(-5, 2, -3)]
    [InlineData(1, 3, 0)]
    [InlineData(2, 3, 1)]
    [InlineData(-1, 3, 0)]
    [InlineData(-2, 3, -1)]
    [InlineData(7, 3, 2)]
    public void DivRound_RoundsHalfAwayFromZero(long dividend, long divisor, long expected)
    {
        Assert.Equal(expected, IntMath.DivRound(dividend, divisor));
    }

    [Fact]
    public void MulDiv_SurvivesIntermediateOverflow()
    {
        // long.MaxValue * 2 overflows a long; the Int128 intermediate must not.
        Assert.Equal(long.MaxValue / 2, IntMath.MulDiv(long.MaxValue, 2, 4));
    }

    [Fact]
    public void MulDiv_FloorsNegativeResults()
    {
        Assert.Equal(-1, IntMath.MulDiv(-1, 1, 3));  // floor(-1/3)
        Assert.Equal(-3, IntMath.MulDiv(-10, 1, 4)); // floor(-2.5)
    }

    [Fact]
    public void MulDiv_ThrowsWhenResultDoesNotFit()
    {
        Assert.Throws<OverflowException>(() => IntMath.MulDiv(long.MaxValue, long.MaxValue, 1));
    }

    [Fact]
    public void MulDiv_ThrowsOnZeroDenominator()
    {
        Assert.Throws<DivideByZeroException>(() => IntMath.MulDiv(1, 1, 0));
    }

    [Fact]
    public void ApplyBps_AppliesBasisPoints()
    {
        Assert.Equal(5_000, IntMath.ApplyBps(200_000, 250)); // 2.5%
        Assert.Equal(200_000, IntMath.ApplyBps(200_000, IntMath.BasisPointScale));
    }

    [Fact]
    public void Units_ArithmeticIsCheckedAndComparable()
    {
        var a = new Minutes(480);
        var b = new Minutes(120);

        Assert.Equal(new Minutes(600), a + b);
        Assert.Equal(new Minutes(360), a - b);
        Assert.Equal(new Minutes(960), a * 2);
        Assert.True(b < a);
        Assert.Equal("480 min", a.ToString());
        Assert.Throws<OverflowException>(() => new Cents(long.MaxValue) + new Cents(1));
    }
}
