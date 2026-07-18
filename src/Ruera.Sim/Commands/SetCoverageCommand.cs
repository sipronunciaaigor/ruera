using System.Globalization;

namespace Ruera.Sim.Commands;

/// <summary>
/// Paints a vehicle's coverage: the set of streets to cover, not their order
/// (DESIGN.md §4). Empty set clears the coverage. Stored canonically on the
/// vehicle (sorted, distinct); the command keeps what was submitted.
/// </summary>
public sealed record SetCoverageCommand(int VehicleId, int[] EdgeIds) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.SetCoverage;

    public bool Equals(SetCoverageCommand? other) =>
        other is not null && VehicleId == other.VehicleId && EdgeIds.AsSpan().SequenceEqual(other.EdgeIds);

    // Never used in sim logic (rule 7); required override for the Equals above.
    public override int GetHashCode() => VehicleId;

    internal override CommandValidation Validate(SimState state)
    {
        if (state.Graph is null)
            return CommandValidation.Invalid("No world loaded: there are no streets to cover.");
        if (!state.TryGetVehicle(VehicleId, out _))
            return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown vehicle {VehicleId}."));
        foreach (var edgeId in EdgeIds)
        {
            if (!state.Graph.HasEdge(edgeId))
                return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown edge {edgeId}."));
        }

        return CommandValidation.Valid;
    }

    internal override void Apply(SimState state) => state.Vehicle(VehicleId).SetCoverage(EdgeIds);

    internal override void WritePayload(BinaryWriter writer)
    {
        writer.Write(VehicleId);
        writer.Write(EdgeIds.Length);
        foreach (var edgeId in EdgeIds)
            writer.Write(edgeId);
    }

    internal static SetCoverageCommand ReadPayload(BinaryReader reader)
    {
        var vehicleId = reader.ReadInt32();
        var count = reader.ReadInt32();
        var edgeIds = new int[count];
        for (var i = 0; i < count; i++)
            edgeIds[i] = reader.ReadInt32();
        return new SetCoverageCommand(vehicleId, edgeIds);
    }
}
