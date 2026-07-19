using System.Globalization;

namespace Ruera.Sim.Commands;

/// <summary>
/// Adds a carrier of a data-defined type to the fleet, parked at the depot.
/// Purchase cost, delivery delay and era gating enforcement arrive with the
/// economy loop (RUE-14); until then this is the fleet-setup command.
/// </summary>
public sealed record AddCarrierCommand(string CarrierTypeId) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.AddCarrier;

    internal override CommandValidation Validate(SimState state)
    {
        if (state.Definitions is null)
            return CommandValidation.Invalid("No world loaded: carrier types are undefined.");
        return state.Definitions.TryGetCarrier(CarrierTypeId, out _)
            ? CommandValidation.Valid
            : CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown carrier type '{CarrierTypeId}'."));
    }

    internal override void Apply(SimState state) => state.AddCarrier(CarrierTypeId);

    internal override void WritePayload(BinaryWriter writer) => writer.Write(CarrierTypeId);

    internal static AddCarrierCommand ReadPayload(BinaryReader reader) => new(reader.ReadString());
}
