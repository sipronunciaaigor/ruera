using Ruera.Sim.Commands;
using Ruera.Sim.Data;
using Ruera.Sim.Persistence;
using Ruera.Sim.Rng;
using Ruera.Sim.World;

namespace Ruera.Sim.Tests;

public class SaveLoadTests
{
    private static readonly StreetGraph Graph =
        MapLoader.LoadFromFile(Path.Combine(AppContext.BaseDirectory, "data", "maps", "toy.map.json"));

    private static readonly DefinitionRegistry Definitions =
        DefinitionLoader.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data", "definitions"));

    private static Simulation ScriptedSim(ulong seed = 2026)
    {
        var sim = new Simulation(seed, Graph, Definitions);
        sim.Submit(new AddCarrierCommand("base:navazza"));
        sim.Submit(new BuyCarrierCommand("base:gerla"));
        sim.Submit(new HireWorkerCommand());
        sim.Submit(new SignContractCommand(1));
        sim.Advance(1);
        sim.Submit(new SetCoverageCommand(1, [2, 5]));
        sim.Advance(39);
        return sim;
    }

    [Fact]
    public void SaveLoadContinue_MatchesUninterruptedRun()
    {
        var interrupted = ScriptedSim();
        var reference = ScriptedSim();
        var savedHash = interrupted.StateHash();

        var loaded = SaveSystem.Load(SaveSystem.Save(interrupted), Graph, Definitions);
        Assert.Equal(savedHash, loaded.StateHash()); // restored exactly, self-check passed

        loaded.Advance(30);
        reference.Advance(30);
        Assert.Equal(reference.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void LoadedSim_KeepsTheFullCommandLog_AndPendingFutureCommands()
    {
        var sim = ScriptedSim();
        sim.Schedule(sim.Tick + 10, new DrawRandomCommand(RngStreamId.Events)); // still pending at save

        var loaded = SaveSystem.Load(SaveSystem.Save(sim), Graph, Definitions);
        Assert.Equal(sim.CommandLog, loaded.CommandLog);

        var reference = ScriptedSim();
        reference.Schedule(reference.Tick + 10, new DrawRandomCommand(RngStreamId.Events));
        loaded.Advance(20);
        reference.Advance(20);
        Assert.Equal(reference.StateHash(), loaded.StateHash()); // the pending command fired after load
    }

    [Fact]
    public void FinishedGame_ReplaysEndToEndFromItsLog()
    {
        var original = ScriptedSim();
        var loaded = SaveSystem.Load(SaveSystem.Save(original), Graph, Definitions);

        var replayed = Simulation.Replay(2026, loaded.CommandLog, ticks: 40, Graph, Definitions);

        Assert.Equal(original.StateHash(), replayed.StateHash());
    }

    [Fact]
    public void SchemaVersionMismatch_FailsGracefully()
    {
        var bytes = SaveSystem.Save(ScriptedSim(), EngineVersion.SimVersion, EngineVersion.StateSchemaVersion + 1);

        var exception = Assert.Throws<SaveLoadException>(() => SaveSystem.Load(bytes, Graph, Definitions));

        Assert.Contains("schema", exception.Message);
        Assert.Contains("incompatible", exception.Message);
    }

    [Fact]
    public void SimVersionMismatch_StillLoadsFromTheSnapshot()
    {
        // RUE-8 policy: same schema, different SimVersion -> continue from the
        // snapshot (exact); only the recorded history stops being replayable.
        var sim = ScriptedSim();
        var bytes = SaveSystem.Save(sim, EngineVersion.SimVersion + 1, EngineVersion.StateSchemaVersion);

        var loaded = SaveSystem.Load(bytes, Graph, Definitions);

        Assert.Equal(sim.StateHash(), loaded.StateHash());
    }

    [Fact]
    public void CorruptSnapshot_IsRejectedByChecksum()
    {
        var bytes = SaveSystem.Save(ScriptedSim());
        bytes[^1] ^= 0xFF; // flip a byte inside the snapshot section

        var exception = Assert.Throws<SaveLoadException>(() => SaveSystem.Load(bytes, Graph, Definitions));

        Assert.Contains("corrupt", exception.Message);
    }

    [Fact]
    public void DifferentScenarioData_IsRejected()
    {
        var bytes = SaveSystem.Save(ScriptedSim());
        var carriersJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "definitions", "carriers.json"))
            .Replace("\"capacityGrams\": 25000", "\"capacityGrams\": 26000");
        var tweaked = DefinitionLoader.Load(carriersJson,
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "definitions", "waste.json")),
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "definitions", "producers.json")));

        var exception = Assert.Throws<SaveLoadException>(() => SaveSystem.Load(bytes, Graph, tweaked));

        Assert.Contains("different scenario data", exception.Message);
    }

    [Fact]
    public void WorldlessSave_RoundTrips()
    {
        var sim = new Simulation(7);
        sim.Submit(new DrawRandomCommand(RngStreamId.Economy));
        sim.Advance(100);

        var loaded = SaveSystem.Load(SaveSystem.Save(sim));
        loaded.Advance(50);
        sim.Advance(50);

        Assert.Equal(sim.StateHash(), loaded.StateHash());
        Assert.Throws<SaveLoadException>(() => SaveSystem.Load(SaveSystem.Save(sim), Graph, Definitions));
    }

    [Fact]
    public void NotASaveFile_IsRejected()
    {
        Assert.Throws<SaveLoadException>(() => SaveSystem.Load([0x52, 0x55, 0x45, 0x52, 0x58, 0x00, 0x00]));
    }
}
