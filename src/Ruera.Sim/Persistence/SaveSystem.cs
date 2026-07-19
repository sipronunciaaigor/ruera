using System.Globalization;
using System.Text;

using Ruera.Sim.Calendar;
using Ruera.Sim.Commands;
using Ruera.Sim.Data;
using Ruera.Sim.Hashing;
using Ruera.Sim.World;

namespace Ruera.Sim.Persistence;

/// <summary>Raised when a save file is corrupt, incompatible, or from a different scenario.</summary>
public sealed class SaveLoadException(string message) : Exception(message);

/// <summary>
/// The save container decided in RUE-8 (DESIGN.md §2 «Save e replay»): the
/// command log is the source of truth, snapshots are an acceleration cache.
/// One binary file: header (versions, scenario identity, seed, tick, state
/// hash) + command log + snapshot index + canonical end-of-tick snapshots,
/// FNV-1a checksums per section. V1 writes one snapshot (at save time); the
/// index already supports the yearly cadence when scrubbing needs it.
/// </summary>
public static class SaveSystem
{
    private const string Magic = "RUERA";
    private const ushort ContainerVersion = 1;

    public static byte[] Save(Simulation sim) =>
        Save(sim, EngineVersion.SimVersion, EngineVersion.StateSchemaVersion);

    internal static byte[] Save(Simulation sim, int simVersion, int stateSchemaVersion)
    {
        var state = sim.State;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write(Encoding.ASCII.GetBytes(Magic));
        writer.Write(ContainerVersion);
        writer.Write(simVersion);
        writer.Write(stateSchemaVersion);
        writer.Write(state.Graph?.MapId ?? "");
        writer.Write(state.Graph is not null ? ScenarioHash.Compute(state.Graph, state.Definitions!) : 0UL);
        writer.Write(state.Seed);
        writer.Write(state.Tick);
        writer.Write(sim.StateHash());

        var log = CommandSerializer.Serialize(sim.CommandLog);
        writer.Write(log.Length);
        writer.Write(log);
        writer.Write(HashBytes(log));

        var snapshot = TakeSnapshot(state);
        writer.Write(1); // snapshot count; yearly cadence slots in here later
        writer.Write(state.Tick);
        writer.Write(snapshot.Length);
        writer.Write(HashBytes(snapshot));
        writer.Write(snapshot);

        writer.Flush();
        return stream.ToArray();
    }

    public static Simulation Load(byte[] data, StreetGraph? graph = null, DefinitionRegistry? definitions = null,
        SimCalendar? calendar = null, EventSettings? events = null)
    {
        using var stream = new MemoryStream(data, writable: false);
        using var reader = new BinaryReader(stream);

        if (Encoding.ASCII.GetString(reader.ReadBytes(Magic.Length)) != Magic)
            throw new SaveLoadException("Not a Ruera save file.");
        var containerVersion = reader.ReadUInt16();
        if (containerVersion != ContainerVersion)
            throw new SaveLoadException(Invariant($"Unsupported container version {containerVersion} (supported: {ContainerVersion})."));

        var simVersion = reader.ReadInt32();
        var schemaVersion = reader.ReadInt32();
        if (schemaVersion != EngineVersion.StateSchemaVersion)
            throw new SaveLoadException(Invariant(
                $"Save uses state schema {schemaVersion}, this engine uses {EngineVersion.StateSchemaVersion}: incompatible (no migrations pre-1.0)."));
        // Different SimVersion with the same schema: loading from the snapshot is
        // exact; only the recorded history is no longer replayable on this engine (RUE-8).

        var scenarioId = reader.ReadString();
        var scenarioHash = reader.ReadUInt64();
        if (scenarioId.Length > 0)
        {
            if (graph is null || definitions is null)
                throw new SaveLoadException(Invariant($"Save requires scenario '{scenarioId}' (map + definitions)."));
            if (graph.MapId != scenarioId || ScenarioHash.Compute(graph, definitions) != scenarioHash)
                throw new SaveLoadException(Invariant(
                    $"Save was created with different scenario data ('{scenarioId}'): map or definitions do not match."));
        }
        else if (graph is not null || definitions is not null)
        {
            throw new SaveLoadException("Save is worldless but a scenario was provided.");
        }

        var seed = reader.ReadUInt64();
        var savedTick = reader.ReadInt64();
        var stateHash = reader.ReadUInt64();

        var log = reader.ReadBytes(reader.ReadInt32());
        if (reader.ReadUInt64() != HashBytes(log))
            throw new SaveLoadException("Command log section is corrupt (checksum mismatch).");
        var entries = CommandSerializer.Deserialize(log);

        var snapshotCount = reader.ReadInt32();
        byte[]? snapshot = null;
        for (var i = 0; i < snapshotCount; i++)
        {
            var tick = reader.ReadInt64();
            var bytes = ReadSnapshot(reader);
            if (tick == savedTick)
                snapshot = bytes;
        }

        if (snapshot is null)
            throw new SaveLoadException("No snapshot for the saved tick.");

        var sim = new Simulation(seed, calendar ?? SimCalendar.Milano1880(), graph, definitions, events);
        using var snapshotStream = new MemoryStream(snapshot, writable: false);
        using var snapshotReader = new BinaryReader(snapshotStream);
        try
        {
            sim.State.ReadFrom(new BinaryStateReader(snapshotReader));
        }
        catch (Exception exception) when (exception is InvalidDataException or KeyNotFoundException or EndOfStreamException)
        {
            throw new SaveLoadException(Invariant($"Snapshot restore failed: {exception.Message}"));
        }

        // Determinism self-check (RUE-8): the restored state must land exactly
        // on the hash recorded at save time.
        if (sim.StateHash() != stateHash)
            throw new SaveLoadException("Restored state does not match the recorded state hash.");

        sim.RestoreLog(entries);
        return sim;
    }

    private static byte[] ReadSnapshot(BinaryReader reader)
    {
        var length = reader.ReadInt32();
        var hash = reader.ReadUInt64();
        var bytes = reader.ReadBytes(length);
        if (HashBytes(bytes) != hash)
            throw new SaveLoadException("Snapshot section is corrupt (checksum mismatch).");
        return bytes;
    }

    private static byte[] TakeSnapshot(SimState state)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        state.WriteTo(new BinaryStateWriter(writer));
        writer.Flush();
        return stream.ToArray();
    }

    private static ulong HashBytes(byte[] bytes)
    {
        var hasher = Fnv1a64.Create();
        foreach (var b in bytes)
            hasher.Add(b);
        return hasher.Hash;
    }

    private static string Invariant(FormattableString message) => FormattableString.Invariant(message);
}
