using System.Globalization;

namespace Ruera.Sim.Commands;

/// <summary>
/// Paints a carrier's coverage: the set of streets to cover, not their order
/// (DESIGN.md §4). Empty set clears the coverage. Stored canonically on the
/// carrier (sorted, distinct); the command keeps what was submitted.
/// </summary>
public sealed record SetCoverageCommand(int CarrierId, int[] EdgeIds) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.SetCoverage;

    public bool Equals(SetCoverageCommand? other) =>
        other is not null && CarrierId == other.CarrierId && EdgeIds.AsSpan().SequenceEqual(other.EdgeIds);

    // Never used in sim logic (rule 7); required override for the Equals above.
    public override int GetHashCode() => CarrierId;

    internal override CommandValidation Validate(SimState state)
    {
        if (state.Graph is null)
            return CommandValidation.Invalid("No world loaded: there are no streets to cover.");
        if (!state.TryGetCarrier(CarrierId, out _))
            return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown carrier {CarrierId}."));
        foreach (var edgeId in EdgeIds)
        {
            if (!state.Graph.HasEdge(edgeId))
                return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown edge {edgeId}."));
        }

        return CommandValidation.Valid;
    }

    internal override void Apply(SimState state) => state.Carrier(CarrierId).SetCoverage(EdgeIds);

    internal override void WritePayload(BinaryWriter writer)
    {
        writer.Write(CarrierId);
        writer.Write(EdgeIds.Length);
        foreach (var edgeId in EdgeIds)
            writer.Write(edgeId);
    }

    internal static SetCoverageCommand ReadPayload(BinaryReader reader)
    {
        var carrierId = reader.ReadInt32();
        var count = reader.ReadInt32();
        var edgeIds = new int[count];
        for (var i = 0; i < count; i++)
            edgeIds[i] = reader.ReadInt32();
        return new SetCoverageCommand(carrierId, edgeIds);
    }
}
