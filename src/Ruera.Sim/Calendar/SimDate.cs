using System.Globalization;

namespace Ruera.Sim.Calendar;

/// <summary>Day of week, Monday = 0 through Sunday = 6.</summary>
public enum Weekday
{
    Monday = 0,
    Tuesday = 1,
    Wednesday = 2,
    Thursday = 3,
    Friday = 4,
    Saturday = 5,
    Sunday = 6,
}

/// <summary>A proleptic-Gregorian calendar date, produced by <see cref="SimCalendar"/> from a tick.</summary>
public readonly record struct SimDate(int Year, int Month, int Day, Weekday Weekday)
{
    public override string ToString() =>
        string.Create(CultureInfo.InvariantCulture, $"{Year:D4}-{Month:D2}-{Day:D2}");
}
