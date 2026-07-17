using System.Globalization;

namespace Ruera.Sim;

// All sim-state quantities are 64-bit integers wrapped in unit structs
// (DESIGN.md §2, rule 1). float/double/decimal do not exist in Ruera.Sim.
// Arithmetic is checked: silent wraparound in game state is always a bug.

/// <summary>Time budget quantity. One tick is one day; within a tick, plans are budgeted in minutes.</summary>
public readonly record struct Minutes(long Value) : IComparable<Minutes>
{
    public static readonly Minutes Zero = new(0);

    public static Minutes operator +(Minutes a, Minutes b) => new(checked(a.Value + b.Value));
    public static Minutes operator -(Minutes a, Minutes b) => new(checked(a.Value - b.Value));
    public static Minutes operator -(Minutes a) => new(checked(-a.Value));
    public static Minutes operator *(Minutes a, long scalar) => new(checked(a.Value * scalar));
    public static Minutes operator *(long scalar, Minutes a) => a * scalar;
    public static bool operator <(Minutes a, Minutes b) => a.Value < b.Value;
    public static bool operator >(Minutes a, Minutes b) => a.Value > b.Value;
    public static bool operator <=(Minutes a, Minutes b) => a.Value <= b.Value;
    public static bool operator >=(Minutes a, Minutes b) => a.Value >= b.Value;

    public int CompareTo(Minutes other) => Value.CompareTo(other.Value);
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Value} min");
}

/// <summary>Distance quantity in meters.</summary>
public readonly record struct Meters(long Value) : IComparable<Meters>
{
    public static readonly Meters Zero = new(0);

    public static Meters operator +(Meters a, Meters b) => new(checked(a.Value + b.Value));
    public static Meters operator -(Meters a, Meters b) => new(checked(a.Value - b.Value));
    public static Meters operator -(Meters a) => new(checked(-a.Value));
    public static Meters operator *(Meters a, long scalar) => new(checked(a.Value * scalar));
    public static Meters operator *(long scalar, Meters a) => a * scalar;
    public static bool operator <(Meters a, Meters b) => a.Value < b.Value;
    public static bool operator >(Meters a, Meters b) => a.Value > b.Value;
    public static bool operator <=(Meters a, Meters b) => a.Value <= b.Value;
    public static bool operator >=(Meters a, Meters b) => a.Value >= b.Value;

    public int CompareTo(Meters other) => Value.CompareTo(other.Value);
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Value} m");
}

/// <summary>Mass quantity in grams (waste volumes, vehicle capacities).</summary>
public readonly record struct Grams(long Value) : IComparable<Grams>
{
    public static readonly Grams Zero = new(0);

    public static Grams operator +(Grams a, Grams b) => new(checked(a.Value + b.Value));
    public static Grams operator -(Grams a, Grams b) => new(checked(a.Value - b.Value));
    public static Grams operator -(Grams a) => new(checked(-a.Value));
    public static Grams operator *(Grams a, long scalar) => new(checked(a.Value * scalar));
    public static Grams operator *(long scalar, Grams a) => a * scalar;
    public static bool operator <(Grams a, Grams b) => a.Value < b.Value;
    public static bool operator >(Grams a, Grams b) => a.Value > b.Value;
    public static bool operator <=(Grams a, Grams b) => a.Value <= b.Value;
    public static bool operator >=(Grams a, Grams b) => a.Value >= b.Value;

    public int CompareTo(Grams other) => Value.CompareTo(other.Value);
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Value} g");
}

/// <summary>Money quantity in cents. All accounting is integer cents; rates apply via basis points.</summary>
public readonly record struct Cents(long Value) : IComparable<Cents>
{
    public static readonly Cents Zero = new(0);

    public static Cents operator +(Cents a, Cents b) => new(checked(a.Value + b.Value));
    public static Cents operator -(Cents a, Cents b) => new(checked(a.Value - b.Value));
    public static Cents operator -(Cents a) => new(checked(-a.Value));
    public static Cents operator *(Cents a, long scalar) => new(checked(a.Value * scalar));
    public static Cents operator *(long scalar, Cents a) => a * scalar;
    public static bool operator <(Cents a, Cents b) => a.Value < b.Value;
    public static bool operator >(Cents a, Cents b) => a.Value > b.Value;
    public static bool operator <=(Cents a, Cents b) => a.Value <= b.Value;
    public static bool operator >=(Cents a, Cents b) => a.Value >= b.Value;

    public int CompareTo(Cents other) => Value.CompareTo(other.Value);
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Value} cents");
}
