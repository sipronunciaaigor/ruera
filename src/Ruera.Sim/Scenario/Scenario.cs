using Ruera.Sim.Calendar;
using Ruera.Sim.Hashing;

namespace Ruera.Sim.Scenario;

/// <summary>
/// A loaded scenario package (DESIGN.md §2, RUE-20/RUE-38): the top-level unit
/// of play — a map reference plus calendar, scripted timeline and event
/// settings. Turns the hardcoded <see cref="SimCalendar.Milano1880"/> into
/// data. The map and entity definitions are loaded by their own loaders
/// (RUE-9/RUE-12) and bound to this scenario by id; multi-package composition
/// and load order are RUE-36.
/// </summary>
public sealed class Scenario
{
    public Scenario(
        string id,
        string name,
        string mapRef,
        CalendarSpec calendar,
        IReadOnlyList<TimelineEntry> timeline,
        EventSettings? events,
        (int Year, int Month, int Day)? end)
    {
        Id = id;
        Name = name;
        MapRef = mapRef;
        Calendar = calendar;
        Timeline = timeline;
        Events = events;
        End = end;
    }

    /// <summary>Namespaced scenario id, e.g. <c>base:milano-1880</c>.</summary>
    public string Id { get; }

    /// <summary>Human-readable title.</summary>
    public string Name { get; }

    /// <summary>Id of the map this scenario runs on; bound to a loaded <c>StreetGraph</c> by id.</summary>
    public string MapRef { get; }

    public CalendarSpec Calendar { get; }

    /// <summary>Scripted, deterministic timeline in declared order (DESIGN.md §2).</summary>
    public IReadOnlyList<TimelineEntry> Timeline { get; }

    /// <summary>Essential-events configuration (RUE-32); null = events disabled.</summary>
    public EventSettings? Events { get; }

    /// <summary>
    /// Optional declared end date. This is an objective/UI bound (DESIGN.md §12),
    /// **not** an engine constraint: the engine enforces only the hard cap
    /// (<see cref="SimCalendar.MaxYear"/>-12-31, RUE-39). Absent = endless
    /// sandbox, which is first-class. Part of scenario identity so a declared
    /// end is replay/save-stable.
    /// </summary>
    public (int Year, int Month, int Day)? End { get; }

    /// <summary>
    /// Builds the runnable calendar from <see cref="Calendar"/>, compiling every
    /// <see cref="SetCalendarEffect"/> in the timeline into dated
    /// <see cref="CalendarAmendment"/>s. Deterministic: the same scenario always
    /// yields the same calendar, so load/replay reconstruct it identically.
    /// </summary>
    public SimCalendar BuildCalendar()
    {
        var baseHolidays = Calendar.Holidays.Select(h => (h.Month, h.Day)).ToArray();

        // A basis calendar (no amendments) resolves timeline trigger dates to ticks.
        var basis = new SimCalendar(Calendar.EpochYear, Calendar.EpochMonth, Calendar.EpochDay,
            Calendar.RestDays, baseHolidays, []);

        var amendments = new List<CalendarAmendment>();
        foreach (var entry in Timeline) // declared order
        {
            if (entry.Effect is not SetCalendarEffect setCalendar)
                continue;
            var effectiveTick = basis.TickOf(entry.Year, entry.Month, entry.Day);
            if (setCalendar.AddHoliday is { } holiday)
                amendments.Add(CalendarAmendment.Holiday(effectiveTick, holiday.Month, holiday.Day));
            if (setCalendar.AddRestDay is { } restDay)
                amendments.Add(CalendarAmendment.RestDay(effectiveTick, restDay));
        }

        return new SimCalendar(Calendar.EpochYear, Calendar.EpochMonth, Calendar.EpochDay,
            Calendar.RestDays, baseHolidays, amendments);
    }

    /// <summary>
    /// Content hash of the scenario config alone (calendar, timeline, events,
    /// end, ids). Modding any timeline entry changes it — the basis for the
    /// bundle scenario hash in the save header (RUE-8/RUE-38, extended by
    /// <see cref="Persistence.ScenarioHash"/> with the map and definitions).
    /// </summary>
    public ulong ContentHash()
    {
        var hasher = Fnv1a64.Create();
        AddToHash(ref hasher);
        return hasher.Hash;
    }

    /// <summary>Feeds the scenario config into a shared hasher (deterministic field order).</summary>
    internal void AddToHash(ref Fnv1a64 hasher)
    {
        hasher.Add(Id);
        hasher.Add(MapRef);

        hasher.Add(Calendar.EpochYear);
        hasher.Add(Calendar.EpochMonth);
        hasher.Add(Calendar.EpochDay);
        hasher.Add(Calendar.RestDays.Count);
        foreach (var restDay in Calendar.RestDays) // declared order
            hasher.Add((int)restDay);
        hasher.Add(Calendar.Holidays.Count);
        foreach (var (month, day, holidayName) in Calendar.Holidays) // declared order
        {
            hasher.Add(month);
            hasher.Add(day);
            hasher.Add(holidayName);
        }

        hasher.Add(Timeline.Count);
        foreach (var entry in Timeline) // declared order: the timeline is ordered data
        {
            hasher.Add(entry.Year);
            hasher.Add(entry.Month);
            hasher.Add(entry.Day);
            hasher.Add(entry.Effect.TypeName);
            entry.Effect.AddToHash(ref hasher);
        }

        if (Events is { } events)
        {
            hasher.Add(true);
            hasher.Add(events.BreakdownChanceBps);
            hasher.Add(events.RepairTicks);
            hasher.Add(events.RepairCostBpsOfPurchase);
            hasher.Add(events.InspectionChanceBps);
            hasher.Add(events.InspectionFineCents);
            hasher.Add(events.TenderChanceBps);
            hasher.Add(events.TenderDeadlineTicks);
        }
        else
        {
            hasher.Add(false);
        }

        if (End is { } end)
        {
            hasher.Add(true);
            hasher.Add(end.Year);
            hasher.Add(end.Month);
            hasher.Add(end.Day);
        }
        else
        {
            hasher.Add(false);
        }
    }
}
