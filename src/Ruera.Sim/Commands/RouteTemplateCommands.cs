using System.Globalization;

namespace Ruera.Sim.Commands;

/// <summary>Creates a service line (DESIGN.md §4); the template id is assigned deterministically at application.</summary>
public sealed record CreateRouteTemplateCommand(string Name, int[] EdgeIds, byte WeekdayMask) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.CreateRouteTemplate;

    public bool Equals(CreateRouteTemplateCommand? other) =>
        other is not null && Name == other.Name && WeekdayMask == other.WeekdayMask
        && EdgeIds.AsSpan().SequenceEqual(other.EdgeIds);

    // Never used in sim logic (rule 7); required override for the Equals above.
    public override int GetHashCode() => (int)WeekdayMask;

    internal override CommandValidation Validate(SimState state) =>
        RouteTemplateValidation.ValidateShape(state, Name, EdgeIds, WeekdayMask);

    internal override void Apply(SimState state) => state.AddTemplate(Name, EdgeIds, WeekdayMask);

    internal override void WritePayload(BinaryWriter writer)
    {
        writer.Write(Name);
        writer.Write(EdgeIds.Length);
        foreach (var edgeId in EdgeIds)
            writer.Write(edgeId);
        writer.Write(WeekdayMask);
    }

    internal static CreateRouteTemplateCommand ReadPayload(BinaryReader reader)
    {
        var name = reader.ReadString();
        var edgeIds = new int[reader.ReadInt32()];
        for (var i = 0; i < edgeIds.Length; i++)
            edgeIds[i] = reader.ReadInt32();
        return new CreateRouteTemplateCommand(name, edgeIds, reader.ReadByte());
    }
}

/// <summary>Repaints/renames/reschedules an existing service line.</summary>
public sealed record UpdateRouteTemplateCommand(int TemplateId, string Name, int[] EdgeIds, byte WeekdayMask) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.UpdateRouteTemplate;

    public bool Equals(UpdateRouteTemplateCommand? other) =>
        other is not null && TemplateId == other.TemplateId && Name == other.Name
        && WeekdayMask == other.WeekdayMask && EdgeIds.AsSpan().SequenceEqual(other.EdgeIds);

    public override int GetHashCode() => TemplateId;

    internal override CommandValidation Validate(SimState state)
    {
        if (!state.TryGetTemplate(TemplateId, out _))
            return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown route template {TemplateId}."));
        return RouteTemplateValidation.ValidateShape(state, Name, EdgeIds, WeekdayMask);
    }

    internal override void Apply(SimState state) =>
        state.Template(TemplateId).Update(Name, [.. EdgeIds.Distinct().OrderBy(id => id)], WeekdayMask);

    internal override void WritePayload(BinaryWriter writer)
    {
        writer.Write(TemplateId);
        writer.Write(Name);
        writer.Write(EdgeIds.Length);
        foreach (var edgeId in EdgeIds)
            writer.Write(edgeId);
        writer.Write(WeekdayMask);
    }

    internal static UpdateRouteTemplateCommand ReadPayload(BinaryReader reader)
    {
        var templateId = reader.ReadInt32();
        var name = reader.ReadString();
        var edgeIds = new int[reader.ReadInt32()];
        for (var i = 0; i < edgeIds.Length; i++)
            edgeIds[i] = reader.ReadInt32();
        return new UpdateRouteTemplateCommand(templateId, name, edgeIds, reader.ReadByte());
    }
}

/// <summary>Deletes a service line; assigned vehicles simply lose it.</summary>
public sealed record DeleteRouteTemplateCommand(int TemplateId) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.DeleteRouteTemplate;

    internal override CommandValidation Validate(SimState state) =>
        state.TryGetTemplate(TemplateId, out _)
            ? CommandValidation.Valid
            : CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown route template {TemplateId}."));

    internal override void Apply(SimState state) => state.RemoveTemplate(TemplateId);

    internal override void WritePayload(BinaryWriter writer) => writer.Write(TemplateId);

    internal static DeleteRouteTemplateCommand ReadPayload(BinaryReader reader) => new(reader.ReadInt32());
}

/// <summary>Replaces the set of vehicles running a service line ("camion 3 e 7").</summary>
public sealed record SetTemplateVehiclesCommand(int TemplateId, int[] VehicleIds) : SimCommand
{
    public override CommandTypeId TypeId => CommandTypeId.SetTemplateVehicles;

    public bool Equals(SetTemplateVehiclesCommand? other) =>
        other is not null && TemplateId == other.TemplateId && VehicleIds.AsSpan().SequenceEqual(other.VehicleIds);

    public override int GetHashCode() => TemplateId;

    internal override CommandValidation Validate(SimState state)
    {
        if (!state.TryGetTemplate(TemplateId, out _))
            return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown route template {TemplateId}."));
        foreach (var vehicleId in VehicleIds)
        {
            if (!state.TryGetVehicle(vehicleId, out _))
                return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown vehicle {vehicleId}."));
        }

        return CommandValidation.Valid;
    }

    internal override void Apply(SimState state) =>
        state.Template(TemplateId).SetAssignedVehicles([.. VehicleIds.Distinct().OrderBy(id => id)]);

    internal override void WritePayload(BinaryWriter writer)
    {
        writer.Write(TemplateId);
        writer.Write(VehicleIds.Length);
        foreach (var vehicleId in VehicleIds)
            writer.Write(vehicleId);
    }

    internal static SetTemplateVehiclesCommand ReadPayload(BinaryReader reader)
    {
        var templateId = reader.ReadInt32();
        var vehicleIds = new int[reader.ReadInt32()];
        for (var i = 0; i < vehicleIds.Length; i++)
            vehicleIds[i] = reader.ReadInt32();
        return new SetTemplateVehiclesCommand(templateId, vehicleIds);
    }
}

internal static class RouteTemplateValidation
{
    public static CommandValidation ValidateShape(SimState state, string name, int[] edgeIds, byte weekdayMask)
    {
        if (state.Graph is null)
            return CommandValidation.Invalid("No world loaded: there are no streets to cover.");
        if (string.IsNullOrWhiteSpace(name))
            return CommandValidation.Invalid("Route template name must not be empty.");
        if (edgeIds.Length == 0)
            return CommandValidation.Invalid("Route template must cover at least one edge.");
        if (weekdayMask == 0 || weekdayMask >= 128)
            return CommandValidation.Invalid("Weekday mask must set at least one of the seven weekday bits.");
        foreach (var edgeId in edgeIds)
        {
            if (!state.Graph.HasEdge(edgeId))
                return CommandValidation.Invalid(string.Create(CultureInfo.InvariantCulture, $"Unknown edge {edgeId}."));
        }

        return CommandValidation.Valid;
    }
}
