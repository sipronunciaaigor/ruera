using System.Globalization;

namespace Ruera.Sim.Commands;

/// <summary>
/// Adds a vehicle of a data-defined type to the fleet, parked at the depot.
/// Purchase cost, delivery delay and era gating enforcement arrive with the
/// economy loop (RUE-14); until then this is the fleet-setup command.
/// </summary>
public sealed record AddVehicleCommand(string VehicleTypeId) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.AddVehicle;

    internal override CommandValidation Validate(SimState state)
    {
        if (state.Definitions is null)
            return CommandValidation.Invalid("No world loaded: vehicle types are undefined.");
        return state.Definitions.TryGetVehicle(VehicleTypeId, out _)
            ? CommandValidation.Valid
            : CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown vehicle type '{VehicleTypeId}'."));
    }

    internal override void Apply(SimState state) => state.AddVehicle(VehicleTypeId);

    internal override void WritePayload(BinaryWriter writer) => writer.Write(VehicleTypeId);

    internal static AddVehicleCommand ReadPayload(BinaryReader reader) => new(reader.ReadString());
}
