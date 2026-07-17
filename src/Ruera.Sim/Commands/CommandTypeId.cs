namespace Ruera.Sim.Commands;

/// <summary>
/// Stable wire identifiers for command types (serialization contract, like
/// <see cref="Rng.RngStreamId"/>): never renumber, only append.
/// </summary>
public enum CommandTypeId : ushort
{
    NoOp = 1,
    DrawRandom = 2,
}
