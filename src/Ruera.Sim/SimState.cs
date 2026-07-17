using Ruera.Sim.Hashing;
using Ruera.Sim.Rng;

namespace Ruera.Sim;

/// <summary>
/// The complete mutable simulation state. A game is initial state (seed) plus
/// the player's command stream (DESIGN.md §2): everything the engine mutates
/// must live here and feed the state hash.
/// </summary>
public sealed class SimState
{
    // Enum.GetValues returns values sorted by underlying value: a stable, documented order.
    private static readonly RngStreamId[] StreamIds = Enum.GetValues<RngStreamId>();

    private readonly Xoshiro256StarStar[] _streams;

    public ulong Seed { get; }

    /// <summary>Elapsed ticks since the start of the game. One tick = one in-game day.</summary>
    public long Tick { get; internal set; }

    public SimState(ulong seed)
    {
        Seed = seed;
        _streams = new Xoshiro256StarStar[StreamIds.Length];
        for (var i = 0; i < StreamIds.Length; i++)
        {
            // Stream seed = master seed x mixed stream id (SplitMix64), per RUE-7:
            // each system gets an independent, reproducible sequence.
            _streams[i] = new Xoshiro256StarStar(seed ^ SplitMix64.Mix((ulong)StreamIds[i]));
        }
    }

    /// <summary>
    /// Per-system RNG stream: draws in one system never shift another system's
    /// sequence. Internal on purpose: outside the assembly, state may only be
    /// mutated through command application at tick boundaries (RUE-15).
    /// </summary>
    internal IDeterministicRng Rng(RngStreamId id)
    {
        var index = Array.IndexOf(StreamIds, id);
        return index >= 0
            ? _streams[index]
            : throw new ArgumentOutOfRangeException(nameof(id), id, "Unknown RNG stream.");
    }

    /// <summary>Feeds every piece of state to the hasher in a fixed, documented order.</summary>
    public void AddToHash(ref Fnv1a64 hasher)
    {
        hasher.Add(Seed);
        hasher.Add(Tick);
        foreach (var stream in _streams) // StreamIds order: stable by construction
            stream.AddState(ref hasher);
    }
}
