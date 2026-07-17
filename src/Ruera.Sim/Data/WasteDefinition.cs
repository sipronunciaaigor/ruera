namespace Ruera.Sim.Data;

/// <summary>
/// A waste fraction as declared in waste.json (DESIGN.md §3). The 1880 slice
/// has a single undifferentiated stream; separate fractions arrive with the
/// historical progression (differenziata 1980).
/// </summary>
public sealed class WasteDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    /// <summary>Base sale price; purity-dependent pricing (DESIGN.md §8) modulates it later.</summary>
    public required long BaseSaleCentsPerKg { get; init; }
}
