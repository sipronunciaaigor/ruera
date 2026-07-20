using System.Text.Json;
using System.Text.Json.Serialization;

using Ruera.Sim.Calendar;

namespace Ruera.Sim.Scenario;

/// <summary>Raised when a scenario file is malformed, incomplete, or inconsistent.</summary>
public sealed class ScenarioLoadException(string message) : Exception(message);

/// <summary>
/// Strict loader for the scenario package file (scenario.json — DESIGN.md §2
/// «Scenario e timeline storica», RUE-38). Same discipline as
/// <see cref="Data.DefinitionLoader"/>: JSON is culture-free, quantities are
/// integers, unknown fields / missing required fields / bad ranges fail the
/// load with an error that names the source. The closed timeline effect
/// vocabulary is enforced here: <c>setCalendar</c> is applied; the other
/// reserved names and any unknown name are rejected with a clear message.
/// </summary>
public static class ScenarioLoader
{
    private const int SupportedFormatVersion = 1;

    // The closed timeline vocabulary (DESIGN.md §2, RUE-20). Only setCalendar is
    // wired in RUE-38; the rest are reserved until a slice event needs each.
    private static readonly string[] ReservedEffectTypes =
        ["setCarrierAvailability", "setProducerParam", "scaleParam", "requireNorm", "growWorld"];

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static Scenario LoadFromFile(string path)
    {
        if (!File.Exists(path))
            throw new ScenarioLoadException(Invariant($"scenario file not found: '{path}'."));
        return Load(File.ReadAllText(path), Path.GetFileName(path));
    }

    public static Scenario Load(string json, string sourceName = "scenario.json")
    {
        var file = Parse(json, sourceName);

        if (file.FormatVersion != SupportedFormatVersion)
            throw Fail(sourceName, Invariant(
                $"unsupported formatVersion {file.FormatVersion} (supported: {SupportedFormatVersion})"));

        Require(!string.IsNullOrWhiteSpace(file.Id), sourceName, "id must not be empty");
        RequireNamespacedId(file.Id!, sourceName);
        Require(!string.IsNullOrWhiteSpace(file.Name), sourceName, "name must not be empty");
        Require(!string.IsNullOrWhiteSpace(file.Map), sourceName, "map must not be empty");

        var calendar = BuildCalendarSpec(file.Calendar, sourceName);
        var timeline = BuildTimeline(file.Timeline, sourceName);
        var events = BuildEvents(file.Events, sourceName);
        var end = BuildEnd(file.End, calendar, sourceName);

        return new Scenario(file.Id!, file.Name!, file.Map!, calendar, timeline, events, end);
    }

    private static ScenarioFile Parse(string json, string sourceName)
    {
        ScenarioFile? file;
        try
        {
            file = JsonSerializer.Deserialize<ScenarioFile>(json, Options);
        }
        catch (JsonException exception)
        {
            throw Fail(sourceName, exception.Message);
        }

        return file ?? throw Fail(sourceName, "file is empty");
    }

    private static CalendarSpec BuildCalendarSpec(CalendarDto? dto, string sourceName)
    {
        Require(dto is not null, sourceName, "calendar is required");
        var epochYear = dto!.EpochYear;
        RequireDate(epochYear, dto.EpochMonth, dto.EpochDay, sourceName, "calendar.epoch");

        var restDays = new List<Weekday>();
        foreach (var name in dto.RestDays ?? [])
        {
            var weekday = ParseWeekday(name, sourceName, "calendar.restDays");
            if (!restDays.Contains(weekday))
                restDays.Add(weekday);
        }

        Require(restDays.Count > 0, sourceName, "calendar.restDays must list at least one rest day");

        var holidays = new List<(int Month, int Day, string Name)>();
        foreach (var holiday in dto.Holidays ?? [])
        {
            RequireMonthDay(holiday.Month, holiday.Day, sourceName, "calendar.holidays");
            holidays.Add((holiday.Month, holiday.Day, holiday.Name ?? ""));
        }

        return new CalendarSpec(epochYear, dto.EpochMonth, dto.EpochDay, restDays, holidays);
    }

    private static IReadOnlyList<TimelineEntry> BuildTimeline(List<TimelineDto>? dtos, string sourceName)
    {
        var entries = new List<TimelineEntry>();
        foreach (var dto in dtos ?? [])
        {
            RequireDate(dto.OnYear, dto.OnMonth, dto.OnDay, sourceName, "timeline.on");
            Require(dto.Effect is not null, sourceName, "timeline entry is missing its effect");
            var effect = BuildEffect(dto.Effect!, sourceName);
            entries.Add(new TimelineEntry(dto.OnYear, dto.OnMonth, dto.OnDay, effect));
        }

        return entries;
    }

    private static TimelineEffect BuildEffect(EffectDto dto, string sourceName)
    {
        Require(!string.IsNullOrWhiteSpace(dto.Type), sourceName, "timeline effect is missing its type");
        var type = dto.Type!;

        if (string.Equals(type, "setCalendar", StringComparison.Ordinal))
        {
            (int Month, int Day, string Name)? addHoliday = null;
            if (dto.AddHoliday is { } holiday)
            {
                RequireMonthDay(holiday.Month, holiday.Day, sourceName, "timeline setCalendar.addHoliday");
                addHoliday = (holiday.Month, holiday.Day, holiday.Name ?? "");
            }

            Weekday? addRestDay = dto.AddRestDay is null
                ? null
                : ParseWeekday(dto.AddRestDay, sourceName, "timeline setCalendar.addRestDay");

            Require(addHoliday is not null || addRestDay is not null, sourceName,
                "setCalendar must set at least one of addHoliday / addRestDay");

            return new SetCalendarEffect { AddHoliday = addHoliday, AddRestDay = addRestDay };
        }

        if (Array.Exists(ReservedEffectTypes, t => string.Equals(t, type, StringComparison.Ordinal)))
            throw Fail(sourceName, Invariant(
                $"timeline effect type '{type}' is reserved but not yet implemented (RUE-38 wires 'setCalendar'; the rest land with the slice event that needs each)"));

        throw Fail(sourceName, Invariant($"unknown timeline effect type '{type}'"));
    }

    private static EventSettings? BuildEvents(EventSettingsDto? dto, string sourceName)
    {
        if (dto is null)
            return null;

        RequireNonNegative(dto.BreakdownChanceBps, sourceName, "events.breakdownChanceBps");
        RequireNonNegative(dto.RepairTicks, sourceName, "events.repairTicks");
        RequireNonNegative(dto.RepairCostBpsOfPurchase, sourceName, "events.repairCostBpsOfPurchase");
        RequireNonNegative(dto.InspectionChanceBps, sourceName, "events.inspectionChanceBps");
        RequireNonNegative(dto.InspectionFineCents, sourceName, "events.inspectionFineCents");
        RequireNonNegative(dto.TenderChanceBps, sourceName, "events.tenderChanceBps");
        RequireNonNegative(dto.TenderDeadlineTicks, sourceName, "events.tenderDeadlineTicks");

        return new EventSettings(
            dto.BreakdownChanceBps,
            dto.RepairTicks,
            dto.RepairCostBpsOfPurchase,
            dto.InspectionChanceBps,
            dto.InspectionFineCents,
            dto.TenderChanceBps,
            dto.TenderDeadlineTicks);
    }

    private static (int Year, int Month, int Day)? BuildEnd(EndDto? dto, CalendarSpec calendar, string sourceName)
    {
        if (dto is null)
            return null;

        RequireDate(dto.Year, dto.Month, dto.Day, sourceName, "end");

        // Ordering is a data sanity check; the engine enforces only the hard cap
        // (RUE-39) — the declared end is a §12 objective bound, not a constraint.
        // TickOf(epoch) is 0 by construction, so end after epoch means TickOf > 0.
        var basis = new SimCalendar(calendar.EpochYear, calendar.EpochMonth, calendar.EpochDay,
            calendar.RestDays, calendar.Holidays.Select(h => (h.Month, h.Day)), []);
        Require(basis.TickOf(dto.Year, dto.Month, dto.Day) > 0, sourceName,
            "end must be after the calendar epoch");

        return (dto.Year, dto.Month, dto.Day);
    }

    private static Weekday ParseWeekday(string value, string sourceName, string field) =>
        value.Trim().ToLowerInvariant() switch
        {
            "monday" => Weekday.Monday,
            "tuesday" => Weekday.Tuesday,
            "wednesday" => Weekday.Wednesday,
            "thursday" => Weekday.Thursday,
            "friday" => Weekday.Friday,
            "saturday" => Weekday.Saturday,
            "sunday" => Weekday.Sunday,
            _ => throw Fail(sourceName, Invariant($"{field}: unknown weekday '{value}'")),
        };

    /// <summary>Moddability day-one rule (DESIGN.md §2 «Moddabilità»): scenario id is package:name.</summary>
    private static void RequireNamespacedId(string id, string sourceName)
    {
        var colon = id.IndexOf(':');
        if (colon <= 0 || colon != id.LastIndexOf(':') || colon == id.Length - 1)
            throw Fail(sourceName, Invariant($"id '{id}' must be namespaced as 'package:name' (e.g. 'base:milano-1880')"));
    }

    private static void RequireDate(int year, int month, int day, string sourceName, string field)
    {
        Require(year is >= 1 and <= SimCalendar.MaxYear, sourceName,
            Invariant($"{field}: year must be within 1-{SimCalendar.MaxYear} (was {year})"));
        RequireMonthDay(month, day, sourceName, field);
    }

    private static void RequireMonthDay(int month, int day, string sourceName, string field)
    {
        Require(month is >= 1 and <= 12, sourceName, Invariant($"{field}: month must be within 1-12 (was {month})"));
        Require(day is >= 1 and <= 31, sourceName, Invariant($"{field}: day must be within 1-31 (was {day})"));
    }

    private static void RequireNonNegative(long value, string sourceName, string field) =>
        Require(value >= 0, sourceName, Invariant($"{field} must be >= 0 (was {value})"));

    private static void Require(bool condition, string sourceName, string message)
    {
        if (!condition)
            throw Fail(sourceName, message);
    }

    private static ScenarioLoadException Fail(string sourceName, string message) =>
        new(Invariant($"{sourceName}: {message}."));

    private static string Invariant(FormattableString message) => FormattableString.Invariant(message);

    private sealed class ScenarioFile
    {
        public required int FormatVersion { get; init; }

        public string? Id { get; init; }

        public string? Name { get; init; }

        public string? Map { get; init; }

        public CalendarDto? Calendar { get; init; }

        public List<TimelineDto>? Timeline { get; init; }

        public EventSettingsDto? Events { get; init; }

        public EndDto? End { get; init; }
    }

    private sealed class CalendarDto
    {
        public required int EpochYear { get; init; }

        public required int EpochMonth { get; init; }

        public required int EpochDay { get; init; }

        public List<string>? RestDays { get; init; }

        public List<HolidayDto>? Holidays { get; init; }
    }

    private sealed class HolidayDto
    {
        public required int Month { get; init; }

        public required int Day { get; init; }

        public string? Name { get; init; }
    }

    private sealed class TimelineDto
    {
        public required int OnYear { get; init; }

        public required int OnMonth { get; init; }

        public required int OnDay { get; init; }

        public EffectDto? Effect { get; init; }
    }

    private sealed class EffectDto
    {
        public string? Type { get; init; }

        public HolidayDto? AddHoliday { get; init; }

        public string? AddRestDay { get; init; }
    }

    private sealed class EventSettingsDto
    {
        public required int BreakdownChanceBps { get; init; }

        public required long RepairTicks { get; init; }

        public required int RepairCostBpsOfPurchase { get; init; }

        public required int InspectionChanceBps { get; init; }

        public required long InspectionFineCents { get; init; }

        public required int TenderChanceBps { get; init; }

        public required long TenderDeadlineTicks { get; init; }
    }

    private sealed class EndDto
    {
        public required int Year { get; init; }

        public required int Month { get; init; }

        public required int Day { get; init; }
    }
}
