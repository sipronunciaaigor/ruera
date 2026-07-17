namespace Ruera.Sim;

/// <summary>
/// Minimal simulation stub: seed + tick counter + stable state hash.
/// The real engine lands with RUE-11; this exists so the solution builds
/// end-to-end and the CLI/tests have something deterministic to drive.
/// </summary>
public sealed class Simulation
{
    public ulong Seed { get; }

    /// <summary>Elapsed ticks. One tick is one in-game day (DESIGN.md §2).</summary>
    public long Tick { get; private set; }

    public Simulation(ulong seed) => Seed = seed;

    public void Advance(int ticks)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ticks);
        Tick += ticks;
    }

    /// <summary>FNV-1a over the observable state. Placeholder until RUE-11.</summary>
    public ulong StateHash()
    {
        const ulong fnvOffset = 14695981039346656037UL;
        const ulong fnvPrime = 1099511628211UL;

        var hash = fnvOffset;
        Span<ulong> state = [Seed, (ulong)Tick];
        foreach (var value in state)
        {
            for (var shift = 0; shift < 64; shift += 8)
            {
                hash ^= (byte)(value >> shift);
                hash *= fnvPrime;
            }
        }

        return hash;
    }
}
