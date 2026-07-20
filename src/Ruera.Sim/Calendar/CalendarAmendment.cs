namespace Ruera.Sim.Calendar;

/// <summary>The kind of change a <see cref="CalendarAmendment"/> makes to the calendar.</summary>
public enum CalendarAmendmentKind
{
    /// <summary>Adds a fixed-date holiday (non-working) from the effective tick onward.</summary>
    AddHoliday,

    /// <summary>Adds a weekly rest day (non-working) from the effective tick onward — e.g. «sabato festivo».</summary>
    AddRestDay,
}

/// <summary>
/// A dated change to the calendar, compiled from a scenario's <c>SetCalendar</c>
/// timeline effects (DESIGN.md §2 «Scenario e timeline storica», RUE-20/RUE-38).
/// The timeline is deterministic scenario data, not stochastic events: every
/// amendment is known at load time and folded into an immutable
/// <see cref="SimCalendar"/>, so replay and load reconstruct it identically —
/// the calendar never becomes mutable game state.
/// </summary>
/// <param name="EffectiveTick">First tick (inclusive) on which the change applies.</param>
/// <param name="Kind">What the amendment changes.</param>
/// <param name="Value">
/// Encoded payload: for <see cref="CalendarAmendmentKind.AddHoliday"/> it is
/// <c>Month * 100 + Day</c>; for <see cref="CalendarAmendmentKind.AddRestDay"/>
/// it is the <see cref="Weekday"/> integer value.
/// </param>
public readonly record struct CalendarAmendment(long EffectiveTick, CalendarAmendmentKind Kind, int Value)
{
    /// <summary>A holiday added from <paramref name="effectiveTick"/> onward.</summary>
    public static CalendarAmendment Holiday(long effectiveTick, int month, int day) =>
        new(effectiveTick, CalendarAmendmentKind.AddHoliday, month * 100 + day);

    /// <summary>A weekly rest day added from <paramref name="effectiveTick"/> onward.</summary>
    public static CalendarAmendment RestDay(long effectiveTick, Weekday weekday) =>
        new(effectiveTick, CalendarAmendmentKind.AddRestDay, (int)weekday);
}
