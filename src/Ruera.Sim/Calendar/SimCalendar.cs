namespace Ruera.Sim.Calendar;

/// <summary>
/// Calendar layer on top of the tick counter: one tick is one in-game day
/// (DESIGN.md §2, §3). Pure integer Gregorian arithmetic — DateTime is banned
/// in the sim. Sunday is the rest day (six-day working week, period-accurate
/// for 1880–1930); fixed-date holidays are non-working. Movable feasts
/// (Easter) are out of scope for now.
/// </summary>
public sealed class SimCalendar
{
    private readonly long _epochDays;
    private readonly int[] _holidays; // fixed-date holidays encoded Month * 100 + Day, sorted

    public SimCalendar(int epochYear, int epochMonth, int epochDay, IEnumerable<(int Month, int Day)> fixedHolidays)
    {
        _epochDays = DaysFromCivil(epochYear, epochMonth, epochDay);

        var encoded = new List<int>();
        foreach (var (month, day) in fixedHolidays)
        {
            if (month is < 1 or > 12)
                throw new ArgumentOutOfRangeException(nameof(fixedHolidays), month, "Holiday month out of range.");
            if (day is < 1 or > 31)
                throw new ArgumentOutOfRangeException(nameof(fixedHolidays), day, "Holiday day out of range.");
            encoded.Add(month * 100 + day);
        }

        _holidays = encoded.Distinct().Order().ToArray();
    }

    /// <summary>
    /// Default calendar for the vertical slice: tick 0 = 1880-01-01, Italian
    /// fixed holidays including Sant'Ambrogio (Milano's patron saint).
    /// </summary>
    public static SimCalendar Milano1880() => new(1880, 1, 1,
    [
        (1, 1),   // Capodanno
        (1, 6),   // Epifania
        (8, 15),  // Assunzione / Ferragosto
        (11, 1),  // Ognissanti
        (12, 7),  // Sant'Ambrogio
        (12, 8),  // Immacolata
        (12, 25), // Natale
        (12, 26), // Santo Stefano
    ]);

    /// <summary>The calendar date of a tick. Every tick knows its date.</summary>
    public SimDate DateAt(long tick)
    {
        var days = _epochDays + tick;
        var (year, month, day) = CivilFromDays(days);
        var weekday = (Weekday)(((days + 3) % 7 + 7) % 7); // 1970-01-01 (day 0) was a Thursday
        return new SimDate(year, month, day, weekday);
    }

    public bool IsHoliday(SimDate date) => Array.BinarySearch(_holidays, date.Month * 100 + date.Day) >= 0;

    /// <summary>Working day = not Sunday and not a holiday (six-day working week).</summary>
    public bool IsWorkingDay(SimDate date) => date.Weekday != Weekday.Sunday && !IsHoliday(date);

    public bool IsWorkingDay(long tick) => IsWorkingDay(DateAt(tick));

    // Howard Hinnant's "days from civil" / "civil from days" algorithms:
    // exact proleptic-Gregorian <-> days since 1970-01-01, integer-only.

    private static long DaysFromCivil(int year, int month, int day)
    {
        year -= month <= 2 ? 1 : 0;
        var era = (year >= 0 ? year : year - 399) / 400;
        var yoe = year - era * 400;                                       // [0, 399]
        var doy = (153 * (month + (month > 2 ? -3 : 9)) + 2) / 5 + day - 1; // [0, 365]
        var doe = yoe * 365 + yoe / 4 - yoe / 100 + doy;                  // [0, 146096]
        return era * 146097L + doe - 719468L;
    }

    private static (int Year, int Month, int Day) CivilFromDays(long days)
    {
        days += 719468L;
        var era = (days >= 0 ? days : days - 146096) / 146097;
        var doe = days - era * 146097;                                    // [0, 146096]
        var yoe = (doe - doe / 1460 + doe / 36524 - doe / 146096) / 365;  // [0, 399]
        var year = yoe + era * 400;
        var doy = doe - (365 * yoe + yoe / 4 - yoe / 100);                // [0, 365]
        var mp = (5 * doy + 2) / 153;                                     // [0, 11]
        var day = doy - (153 * mp + 2) / 5 + 1;                           // [1, 31]
        var month = mp < 10 ? mp + 3 : mp - 9;                            // [1, 12]
        return ((int)(year + (month <= 2 ? 1 : 0)), (int)month, (int)day);
    }
}
