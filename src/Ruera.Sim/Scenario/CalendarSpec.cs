using Ruera.Sim.Calendar;

namespace Ruera.Sim.Scenario;

/// <summary>
/// The data-driven calendar definition of a scenario (DESIGN.md §2, RUE-38):
/// epoch (tick 0), the weekly rest days, and the fixed-date holidays. Replaces
/// the hardcoded <see cref="SimCalendar.Milano1880"/> factory. Timeline
/// <c>SetCalendar</c> effects amend a calendar built from this spec.
/// </summary>
public sealed record CalendarSpec(
    int EpochYear,
    int EpochMonth,
    int EpochDay,
    IReadOnlyList<Weekday> RestDays,
    IReadOnlyList<(int Month, int Day, string Name)> Holidays);
