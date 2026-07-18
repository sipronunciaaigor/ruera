using System.Globalization;

namespace Ruera.Sim.Commands;

/// <summary>
/// Signs a condo contract with a producer: its archetype's monthly fee is paid
/// on the first tick of each month (DESIGN.md §8, cadence per RUE-6).
/// </summary>
public sealed record SignContractCommand(int ProducerId) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.SignContract;

    internal override CommandValidation Validate(SimState state)
    {
        if (state.Graph is null)
            return CommandValidation.Invalid("No world loaded: there are no producers.");
        foreach (var producer in state.Producers)
        {
            if (producer.Id == ProducerId)
            {
                return producer.HasContract
                    ? CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Producer {ProducerId} is already under contract."))
                    : CommandValidation.Valid;
            }
        }

        return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown producer {ProducerId}."));
    }

    internal override void Apply(SimState state) => state.Producer(ProducerId).HasContract = true;

    internal override void WritePayload(BinaryWriter writer) => writer.Write(ProducerId);

    internal static SignContractCommand ReadPayload(BinaryReader reader) => new(reader.ReadInt32());
}
