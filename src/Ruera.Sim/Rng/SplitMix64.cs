namespace Ruera.Sim.Rng;

/// <summary>
/// SplitMix64 (Steele, Lea, Flood). Used only to expand a seed into generator
/// state words and to derive per-system stream seeds (DESIGN.md §2, rule 3) —
/// never as the game RNG itself.
/// </summary>
internal struct SplitMix64(ulong state)
{
    private ulong _state = state;

    public ulong Next()
    {
        var z = _state += 0x9E3779B97F4A7C15UL;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return z ^ (z >> 31);
    }

    /// <summary>Stateless avalanche mix of a single value (the SplitMix64 finalizer).</summary>
    public static ulong Mix(ulong value)
    {
        value = (value ^ (value >> 30)) * 0xBF58476D1CE4E5B9UL;
        value = (value ^ (value >> 27)) * 0x94D049BB133111EBUL;
        return value ^ (value >> 31);
    }
}
