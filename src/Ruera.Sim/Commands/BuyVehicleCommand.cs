using System.Globalization;

using Ruera.Sim.Systems;

namespace Ruera.Sim.Commands;

/// <summary>
/// Buys a vehicle: cash out at the order tick, the vehicle materializes at a
/// scheduled future delivery tick (RUE-6 «Ritardi realistici»). Era-gated on
/// the calendar year (DESIGN.md §7). Insufficient cash at application skips
/// the command deterministically.
/// </summary>
public sealed record BuyVehicleCommand(string VehicleTypeId) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.BuyVehicle;

    internal override CommandValidation Validate(SimState state)
    {
        if (state.Definitions is null)
            return CommandValidation.Invalid("No world loaded: vehicle types are undefined.");
        if (!state.Definitions.TryGetVehicle(VehicleTypeId, out var definition))
            return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown vehicle type '{VehicleTypeId}'."));
        if (!definition.IsAvailable(state.Calendar.DateAt(state.Tick).Year))
            return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture,
                $"'{VehicleTypeId}' is not available before {definition.AvailableFromYear}."));
        return state.CashCents >= definition.PurchaseCents
            ? CommandValidation.Valid
            : CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture,
                $"Insufficient cash for '{VehicleTypeId}' ({definition.PurchaseCents} cents needed)."));
    }

    internal override void Apply(SimState state)
    {
        var definition = state.Definitions!.Vehicle(VehicleTypeId);
        state.CashCents = checked(state.CashCents - definition.PurchaseCents);
        state.ScheduleDelivery(state.Tick + EconomySystem.DeliveryDelayTicks, VehicleTypeId);
    }

    internal override void WritePayload(BinaryWriter writer) => writer.Write(VehicleTypeId);

    internal static BuyVehicleCommand ReadPayload(BinaryReader reader) => new(reader.ReadString());
}
