using System.Globalization;

using Ruera.Sim.Systems;

namespace Ruera.Sim.Commands;

/// <summary>
/// Buys a carrier: cash out at the order tick, the carrier materializes at a
/// scheduled future delivery tick (RUE-6 «Ritardi realistici»). Era-gated on
/// the calendar year (DESIGN.md §7). Insufficient cash at application skips
/// the command deterministically.
/// </summary>
public sealed record BuyCarrierCommand(string CarrierTypeId) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.BuyCarrier;

    internal override CommandValidation Validate(SimState state)
    {
        if (state.Definitions is null)
            return CommandValidation.Invalid("No world loaded: carrier types are undefined.");
        if (!state.Definitions.TryGetCarrier(CarrierTypeId, out var definition))
            return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown carrier type '{CarrierTypeId}'."));
        if (!definition.IsAvailable(state.Calendar.DateAt(state.Tick).Year))
            return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture,
                $"'{CarrierTypeId}' is not available before {definition.AvailableFromYear}."));
        return state.CashCents >= definition.PurchaseCents
            ? CommandValidation.Valid
            : CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture,
                $"Insufficient cash for '{CarrierTypeId}' ({definition.PurchaseCents} cents needed)."));
    }

    internal override void Apply(SimState state)
    {
        var definition = state.Definitions!.Carrier(CarrierTypeId);
        state.CashCents = checked(state.CashCents - definition.PurchaseCents);
        state.ScheduleDelivery(state.Tick + EconomySystem.DeliveryDelayTicks, CarrierTypeId);
    }

    internal override void WritePayload(BinaryWriter writer) => writer.Write(CarrierTypeId);

    internal static BuyCarrierCommand ReadPayload(BinaryReader reader) => new(reader.ReadString());
}
