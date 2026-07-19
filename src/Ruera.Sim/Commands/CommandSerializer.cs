using System.Globalization;

namespace Ruera.Sim.Commands;

/// <summary>
/// Binary round-trip for command logs: little-endian primitives, no strings,
/// no culture. This is only the wire encoding of commands — the on-disk
/// save/replay file layout (container, snapshots, versioning policy across
/// patches) is RUE-8's decision and will wrap this.
/// </summary>
public static class CommandSerializer
{
    private const ushort FormatVersion = 1;

    public static byte[] Serialize(IReadOnlyList<CommandLogEntry> entries)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(FormatVersion);
        writer.Write(entries.Count);
        foreach (var (day, command) in entries)
        {
            writer.Write(day);
            writer.Write((ushort)command.TypeId);
            command.WritePayload(writer);
        }

        writer.Flush();
        return stream.ToArray();
    }

    public static CommandLogEntry[] Deserialize(byte[] data)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream);

        var version = reader.ReadUInt16();
        if (version != FormatVersion)
            throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                $"Unsupported command log version {version}."));

        var count = reader.ReadInt32();
        if (count < 0)
            throw new InvalidDataException("Negative command count.");

        var entries = new CommandLogEntry[count];
        for (var i = 0; i < count; i++)
        {
            var day = reader.ReadInt64();
            var typeId = (CommandTypeId)reader.ReadUInt16();
            SimCommand command = typeId switch
            {
                CommandTypeId.NoOp => NoOpCommand.ReadPayload(reader),
                CommandTypeId.DrawRandom => DrawRandomCommand.ReadPayload(reader),
                CommandTypeId.AddVehicle => AddVehicleCommand.ReadPayload(reader),
                CommandTypeId.SetCoverage => SetCoverageCommand.ReadPayload(reader),
                CommandTypeId.BuyVehicle => BuyVehicleCommand.ReadPayload(reader),
                CommandTypeId.HireWorker => HireWorkerCommand.ReadPayload(reader),
                CommandTypeId.SignContract => SignContractCommand.ReadPayload(reader),
                CommandTypeId.CreateRouteTemplate => CreateRouteTemplateCommand.ReadPayload(reader),
                CommandTypeId.UpdateRouteTemplate => UpdateRouteTemplateCommand.ReadPayload(reader),
                CommandTypeId.DeleteRouteTemplate => DeleteRouteTemplateCommand.ReadPayload(reader),
                CommandTypeId.SetTemplateVehicles => SetTemplateVehiclesCommand.ReadPayload(reader),
                CommandTypeId.FireWorker => FireWorkerCommand.ReadPayload(reader),
                _ => throw new InvalidDataException(string.Create(CultureInfo.InvariantCulture,
                    $"Unknown command type id {(ushort)typeId}.")),
            };
            entries[i] = new CommandLogEntry(day, command);
        }

        if (stream.Position != stream.Length)
            throw new InvalidDataException("Trailing bytes after command log.");

        return entries;
    }
}
