namespace Ruera.Sim.Data;

/// <summary>
/// A carrier type as declared in carriers.json (DESIGN.md §2 «Dati, non
/// codice», §8). Raw properties bind to JSON in integer units; unit-typed
/// accessors keep sim code in unit structs.
/// </summary>
public sealed class CarrierDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required long CapacityGrams { get; init; }

    /// <summary>Minutes spent loading at one collection stop.</summary>
    public required int FillMinutes { get; init; }

    /// <summary>Minutes spent unloading at the depot.</summary>
    public required int EmptyMinutes { get; init; }

    public required int MetersPerMinute { get; init; }

    public required long PurchaseCents { get; init; }

    public required long MaintenanceCentsPerDay { get; init; }

    /// <summary>Employees needed to operate it (DESIGN.md §7: carts need ≥2 per collection point).</summary>
    public required int Crew { get; init; }

    /// <summary>First calendar year the type can be bought (DESIGN.md §7 historical progression).</summary>
    public required int AvailableFromYear { get; init; }

    public Grams Capacity => new(CapacityGrams);

    public Minutes FillTime => new(FillMinutes);

    public Minutes EmptyTime => new(EmptyMinutes);

    public Cents PurchaseCost => new(PurchaseCents);

    public Cents DailyMaintenance => new(MaintenanceCentsPerDay);

    /// <summary>
    /// Era gating with the R&amp;D hook (DESIGN.md §7): research can anticipate
    /// availability by <paramref name="anticipatedYears"/>.
    /// </summary>
    public bool IsAvailable(int year, int anticipatedYears = 0) => year + anticipatedYears >= AvailableFromYear;
}
