namespace Ruera.Sim.Rng;

/// <summary>
/// Stable identifiers for per-system RNG streams (DESIGN.md §2, rule 3).
/// Each system draws from its own stream, so adding a draw in one system
/// never shifts the sequences of the others. Values are part of the
/// save/replay contract: never renumber, only append.
/// </summary>
public enum RngStreamId : ulong
{
    WasteProduction = 1,
    Collection = 2,
    Economy = 3,
    Events = 4,
}
