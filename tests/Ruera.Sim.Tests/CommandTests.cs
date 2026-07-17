using Ruera.Sim.Commands;
using Ruera.Sim.Rng;

namespace Ruera.Sim.Tests;

public class CommandTests
{
    [Fact]
    public void SubmittedCommand_AppliesAtTickOpen_NotAtSubmission()
    {
        var withCommand = new Simulation(11);
        var plain = new Simulation(11);

        withCommand.Submit(new DrawRandomCommand(RngStreamId.Economy));
        Assert.Equal(plain.StateHash(), withCommand.StateHash()); // nothing happened yet

        withCommand.Advance(1);
        plain.Advance(1);
        Assert.NotEqual(plain.StateHash(), withCommand.StateHash());
    }

    [Fact]
    public void NoOpCommand_DoesNotPerturbState()
    {
        var withNoOp = new Simulation(12);
        var plain = new Simulation(12);

        withNoOp.Submit(new NoOpCommand());
        withNoOp.Advance(10);
        plain.Advance(10);

        Assert.Equal(plain.StateHash(), withNoOp.StateHash());
    }

    [Fact]
    public void ScheduledCommand_AppliesOnItsDayOnly()
    {
        var scheduled = new Simulation(13);
        var plain = new Simulation(13);

        scheduled.Schedule(5, new DrawRandomCommand(RngStreamId.Events));

        // Days 0..4 resolved: day 5 has not opened yet.
        scheduled.Advance(5);
        plain.Advance(5);
        Assert.Equal(plain.StateHash(), scheduled.StateHash());

        // Day 5 resolves: the command fires at its open.
        scheduled.Advance(1);
        plain.Advance(1);
        Assert.NotEqual(plain.StateHash(), scheduled.StateHash());
    }

    [Fact]
    public void Schedule_RejectsResolvedDays()
    {
        var sim = new Simulation(0);
        sim.Advance(3);

        Assert.Throws<ArgumentOutOfRangeException>(() => sim.Schedule(2, new NoOpCommand()));
    }

    [Fact]
    public void Submit_RejectsMalformedCommand()
    {
        var sim = new Simulation(0);

        Assert.Throws<ArgumentException>(() => sim.Submit(new DrawRandomCommand((RngStreamId)999)));
    }

    [Fact]
    public void CommandLog_RecordsSubmissionOrder()
    {
        var sim = new Simulation(1);
        var first = new NoOpCommand();
        var second = new DrawRandomCommand(RngStreamId.Collection);
        var third = new DrawRandomCommand(RngStreamId.Economy);

        sim.Submit(first);
        sim.Schedule(7, second); // scheduled ahead, still logged in submission order
        sim.Advance(2);
        sim.Submit(third);

        Assert.Equal(
        [
            new CommandLogEntry(0, first),
            new CommandLogEntry(7, second),
            new CommandLogEntry(2, third),
        ], sim.CommandLog);
    }

    [Fact]
    public void Commands_RoundTripThroughSerialization()
    {
        CommandLogEntry[] entries =
        [
            new(0, new NoOpCommand()),
            new(3, new DrawRandomCommand(RngStreamId.Economy)),
            new(400, new DrawRandomCommand(RngStreamId.Events)),
        ];

        var roundTripped = CommandSerializer.Deserialize(CommandSerializer.Serialize(entries));

        Assert.Equal(entries, roundTripped);
    }

    [Fact]
    public void Deserialize_RejectsUnknownTypeId()
    {
        // version 1, one entry, day 0, type id 999 — hand-crafted little-endian.
        var bytes = CommandSerializer.Serialize([new CommandLogEntry(0, new NoOpCommand())]);
        bytes[^2] = 0xE7; // 999 = 0x03E7 overwrites the ushort type id
        bytes[^1] = 0x03;

        Assert.Throws<InvalidDataException>(() => CommandSerializer.Deserialize(bytes));
    }

    [Fact]
    public void Deserialize_RejectsTrailingBytes()
    {
        var bytes = CommandSerializer.Serialize([new CommandLogEntry(0, new NoOpCommand())]);

        Assert.Throws<InvalidDataException>(() => CommandSerializer.Deserialize([.. bytes, 0x00]));
    }

    [Fact]
    public void RecordAndReplay_ProduceIdenticalHashes()
    {
        // AC RUE-15: original run and replayed run (seed + serialized command
        // log) must end bit-identical.
        var original = new Simulation(2077);
        original.Advance(10);
        original.Submit(new DrawRandomCommand(RngStreamId.Economy));
        original.Submit(new NoOpCommand());
        original.Advance(20);
        original.Submit(new DrawRandomCommand(RngStreamId.Collection));
        original.Schedule(original.Tick + 15, new DrawRandomCommand(RngStreamId.Events));
        original.Advance(50);

        var wire = CommandSerializer.Serialize(original.CommandLog);
        var replayed = Simulation.Replay(2077, CommandSerializer.Deserialize(wire), ticks: 80);

        Assert.Equal(original.StateHash(), replayed.StateHash());
        Assert.Equal(original.Tick, replayed.Tick);
    }
}
