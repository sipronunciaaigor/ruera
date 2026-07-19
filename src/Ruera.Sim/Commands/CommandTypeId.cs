namespace Ruera.Sim.Commands;

/// <summary>
/// Stable wire identifiers for command types (serialization contract, like
/// <see cref="Rng.RngStreamId"/>): never renumber, only append.
/// </summary>
public enum CommandTypeId : ushort
{
    NoOp = 1,
    DrawRandom = 2,
    AddCarrier = 3,
    SetCoverage = 4,
    BuyCarrier = 5,
    HireWorker = 6,
    SignContract = 7,
    CreateRouteTemplate = 8,
    UpdateRouteTemplate = 9,
    DeleteRouteTemplate = 10,
    SetTemplateCarriers = 11,
    FireWorker = 12,
}
