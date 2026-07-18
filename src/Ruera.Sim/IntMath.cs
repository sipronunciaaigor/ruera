namespace Ruera.Sim;

/// <summary>
/// Integer arithmetic with explicit rounding (DESIGN.md §2, rule 2).
/// Every division in sim logic goes through these helpers so rounding is
/// always a stated choice, never an accident.
/// </summary>
public static class IntMath
{
    /// <summary>Rates and percentages are expressed in basis points: 10 000 = 100%.</summary>
    public const long BasisPointScale = 10_000;

    /// <summary>
    /// value * numerator / denominator with an Int128 intermediate (no overflow),
    /// floor rounding. Throws <see cref="OverflowException"/> if the result does not fit in a long.
    /// </summary>
    public static long MulDiv(long value, long numerator, long denominator)
    {
        if (denominator == 0)
            throw new DivideByZeroException();

        var product = (Int128)value * numerator;
        return checked((long)FloorDivInt128(product, denominator));
    }

    /// <summary>Division rounding toward negative infinity: DivFloor(-7, 2) = -4.</summary>
    public static long DivFloor(long dividend, long divisor)
    {
        var quotient = dividend / divisor;
        if (dividend % divisor != 0 && (dividend < 0) != (divisor < 0))
            quotient--;
        return quotient;
    }

    /// <summary>
    /// Division rounding toward positive infinity: DivCeil(7, 2) = 4. The
    /// rounding of cost estimates: estimates are pessimistic (DESIGN.md §4).
    /// </summary>
    public static long DivCeil(long dividend, long divisor) => -DivFloor(-dividend, divisor);

    /// <summary>Division rounding half away from zero: DivRound(5, 2) = 3, DivRound(-5, 2) = -3.</summary>
    public static long DivRound(long dividend, long divisor)
    {
        var quotient = dividend / divisor;
        var remainder = dividend % divisor;
        if (remainder == 0)
            return quotient;

        // Compare |2 * remainder| against |divisor| in Int128: 2 * remainder can overflow long.
        var twiceRemainder = (Int128)remainder * 2;
        if (twiceRemainder < 0)
            twiceRemainder = -twiceRemainder;
        Int128 absDivisor = divisor;
        if (absDivisor < 0)
            absDivisor = -absDivisor;

        if (twiceRemainder >= absDivisor)
            quotient += (dividend < 0) == (divisor < 0) ? 1 : -1;
        return quotient;
    }

    /// <summary>Applies a rate in basis points: ApplyBps(200_000, 250) = 2.5% of 200 000 = 5 000.</summary>
    public static long ApplyBps(long value, long basisPoints) => MulDiv(value, basisPoints, BasisPointScale);

    private static Int128 FloorDivInt128(Int128 dividend, Int128 divisor)
    {
        var quotient = dividend / divisor;
        if (dividend % divisor != 0 && (dividend < 0) != (divisor < 0))
            quotient--;
        return quotient;
    }
}
