using Ruera.Sim.Calendar;

namespace Ruera.Sim.Scenario;

/// <summary>
/// Base of the closed timeline effect vocabulary (DESIGN.md §2 «Scenario e
/// timeline storica», RUE-20). The historical timeline is a list of typed,
/// engine-owned effects — data, not code — the same principle as commands and
/// moddability. A scenario selects and parameterizes applicators the engine
/// owns; it never ships behaviour.
///
/// RUE-38 wires <see cref="SetCalendarEffect"/> end to end. The other vocabulary
/// members (<c>setCarrierAvailability</c>, <c>setProducerParam</c>,
/// <c>scaleParam</c>, <c>requireNorm</c>, <c>growWorld</c>) are recognized names
/// but reserved: the loader rejects them with a clear message until the slice
/// event that needs each one lands (DESIGN.md §2 «Non blocca la slice» — «resta
/// aperto il *cosa*, non il *come*»).
/// </summary>
public abstract record TimelineEffect
{
    /// <summary>The wire discriminator used in scenario JSON (camelCase).</summary>
    public abstract string TypeName { get; }

    /// <summary>Feeds this effect's payload into the scenario content hash, after the type name.</summary>
    internal abstract void AddToHash(ref Hashing.Fnv1a64 hasher);
}

/// <summary>
/// Amends the calendar from a timeline date: adds a fixed holiday and/or a
/// weekly rest day (e.g. «sabato festivo»). Compiled into immutable
/// <see cref="CalendarAmendment"/>s at scenario build time — deterministic and
/// replay/save-stable, the calendar never becomes mutable state.
/// </summary>
public sealed record SetCalendarEffect : TimelineEffect
{
    public override string TypeName => "setCalendar";

    /// <summary>A fixed-date holiday to add (month, day, descriptive name), if any.</summary>
    public (int Month, int Day, string Name)? AddHoliday { get; init; }

    /// <summary>A weekly rest day to add, if any.</summary>
    public Weekday? AddRestDay { get; init; }

    internal override void AddToHash(ref Hashing.Fnv1a64 hasher)
    {
        if (AddHoliday is { } holiday)
        {
            hasher.Add(holiday.Month);
            hasher.Add(holiday.Day);
            hasher.Add(holiday.Name);
        }
        else
        {
            hasher.Add(-1); // "no holiday" sentinel, distinct from any month value
        }

        hasher.Add(AddRestDay is { } restDay ? (int)restDay : -1);
    }
}
