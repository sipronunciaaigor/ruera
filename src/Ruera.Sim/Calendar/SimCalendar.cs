namespace Ruera.Sim.Calendar;

/// <summary>
/// Calendar layer on top of the tick counter: one tick is one in-game day
/// (DESIGN.md §2, §3). Pure integer Gregorian arithmetic — DateTime is banned
/// in the sim. Rest days (Sunday by default: six-day working week,
/// period-accurate for 1880–1930) and fixed-date holidays are non-working.
/// Movable feasts (Easter) are out of scope for now.
///
/// The calendar is immutable scenario config, built from data (RUE-38). A
/// scenario's <c>SetCalendar</c> timeline effects are compiled into
/// <see cref="CalendarAmendment"/>s at load time, so date-driven changes
/// (a new holiday, «sabato festivo») are deterministic and replay-stable
/// without the calendar ever becoming mutable game state.
/// </summary>
public sealed class SimCalendar
{
    /// <summary>
    /// Hard cap on simulated time (RUE-39): the last representable day is
    /// 12345-12-31 (a deliberately silly number). The engine is year-agnostic
    /// and only structurally limited by <see cref="SimDate.Year"/> being an
    /// int32 (~2.1 billion); the cap makes that limit explicit rather than
    /// accidental and keeps cumulative counters (DESIGN.md §15.10 → Int128)
    /// safe. A scenario's end is otherwise optional — endless is first-class.
    /// </summary>
    public const int MaxYear = 12345;

    private readonly long _epochDays;
    private readonly int[] _holidays;                 // base fixed holidays, Month*100+Day, sorted
    private readonly bool[] _restDays;                // indexed by Weekday (0..6)
    private readonly CalendarAmendment[] _amendments; // sorted ascending by EffectiveTick

    public SimCalendar(int epochYear, int epochMonth, int epochDay, IEnumerable<(int Month, int Day)> fixedHolidays)
        : this(epochYear, epochMonth, epochDay, [Weekday.Sunday], fixedHolidays, [])
    {
    }

    public SimCalendar(
        int epochYear,
        int epochMonth,
        int epochDay,
        IEnumerable<Weekday> restDays,
        IEnumerable<(int Month, int Day)> fixedHolidays,
        IEnumerable<CalendarAmendment> amendments)
    {
        _epochDays = DaysFromCivil(epochYear, epochMonth, epochDay);

        _restDays = new bool[7];
        foreach (var weekday in restDays)
            _restDays[(int)weekday] = true;

        var encoded = new List<int>();
        foreach (var (month, day) in fixedHolidays)
        {
            ValidateMonthDay(month, day);
            encoded.Add(month * 100 + day);
        }

        _holidays = [.. encoded.Distinct().Order()];
        _amendments = [.. amendments.OrderBy(a => a.EffectiveTick)];
    }

    /// <summary>
    /// Default calendar for the vertical slice: tick 0 = 1880-01-01, Italian
    /// fixed holidays including Sant'Ambrogio (Milano's patron saint). This is
    /// the reference the loaded <c>base:milano-1880</c> scenario reproduces.
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

    /// <summary>The tick on which a given civil date falls (inverse of <see cref="DateAt"/>).</summary>
    public long TickOf(int year, int month, int day) => DaysFromCivil(year, month, day) - _epochDays;

    /// <summary>Whether a tick is at or before the hard time cap (RUE-39).</summary>
    public bool IsWithinCap(long tick) => DateAt(tick).Year <= MaxYear;

    /// <summary>Base-calendar holiday test (ignores timeline amendments; use the tick overload for the live rule).</summary>
    public bool IsHoliday(SimDate date) => Array.BinarySearch(_holidays, date.Month * 100 + date.Day) >= 0;

    /// <summary>Base-calendar working-day test (ignores timeline amendments).</summary>
    public bool IsWorkingDay(SimDate date) => !_restDays[(int)date.Weekday] && !IsHoliday(date);

    /// <summary>
    /// Working day = not a rest day and not a holiday, with any timeline
    /// amendments effective by <paramref name="tick"/> applied. This is the
    /// authoritative rule the systems use (six-day working week by default).
    /// </summary>
    public bool IsWorkingDay(long tick)
    {
        var date = DateAt(tick);
        var rest = _restDays[(int)date.Weekday];
        var holiday = Array.BinarySearch(_holidays, date.Month * 100 + date.Day) >= 0;

        if (_amendments.Length > 0 && !(rest && holiday))
        {
            var code = date.Month * 100 + date.Day;
            foreach (var amendment in _amendments) // sorted ascending by EffectiveTick
            {
                if (amendment.EffectiveTick > tick)
                    break;
                if (amendment.Kind == CalendarAmendmentKind.AddHoliday && amendment.Value == code)
                    holiday = true;
                else if (amendment.Kind == CalendarAmendmentKind.AddRestDay && amendment.Value == (int)date.Weekday)
                    rest = true;
            }
        }

        return !rest && !holiday;
    }

    private static void ValidateMonthDay(int month, int day)
    {
        if (month is < 1 or > 12)
            throw new ArgumentOutOfRangeException(nameof(month), month, "Holiday month out of range.");
        if (day is < 1 or > 31)
            throw new ArgumentOutOfRangeException(nameof(day), day, "Holiday day out of range.");
    }

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
