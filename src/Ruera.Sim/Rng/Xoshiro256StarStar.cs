using System.Numerics;

using Ruera.Sim.Hashing;

namespace Ruera.Sim.Rng;

/// <summary>
/// xoshiro256** 1.0 (Blackman &amp; Vigna), committed in-repo because the seeded
/// sequence of System.Random is not guaranteed stable across .NET versions
/// (DESIGN.md §2, rule 3). Pure 64-bit integer state: deterministic by construction.
/// </summary>
public sealed class Xoshiro256StarStar : IDeterministicRng
{
    private ulong _s0, _s1, _s2, _s3;

    /// <summary>Seeds the four state words by expanding <paramref name="seed"/> with SplitMix64.</summary>
    public Xoshiro256StarStar(ulong seed)
    {
        var mix = new SplitMix64(seed);
        _s0 = mix.Next();
        _s1 = mix.Next();
        _s2 = mix.Next();
        _s3 = mix.Next();
    }

    /// <summary>Restores a generator from raw state words (save/replay). Must not be all zero.</summary>
    public Xoshiro256StarStar(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        if ((s0 | s1 | s2 | s3) == 0)
            throw new ArgumentException("xoshiro256** state must not be all zero.");
        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
    }

    /// <summary>Raw state words, for saving and for state hashing.</summary>
    public (ulong S0, ulong S1, ulong S2, ulong S3) State => (_s0, _s1, _s2, _s3);

    public ulong NextUInt64()
    {
        var result = BitOperations.RotateLeft(_s1 * 5, 7) * 9;
        var t = _s1 << 17;

        _s2 ^= _s0;
        _s3 ^= _s1;
        _s1 ^= _s2;
        _s0 ^= _s3;
        _s2 ^= t;
        _s3 = BitOperations.RotateLeft(_s3, 45);

        return result;
    }

    public long NextInt64(long minInclusive, long maxExclusive)
    {
        if (minInclusive >= maxExclusive)
            throw new ArgumentOutOfRangeException(nameof(maxExclusive), maxExclusive,
                "maxExclusive must be greater than minInclusive.");

        // The mathematical range always fits in ulong even when the long subtraction wraps.
        var range = unchecked((ulong)(maxExclusive - minInclusive));

        // Rejection sampling: discard draws below 2^64 mod range so every offset is equally likely.
        var threshold = unchecked((0UL - range) % range);
        ulong value;
        do
        {
            value = NextUInt64();
        } while (value < threshold);

        return unchecked(minInclusive + (long)(value % range));
    }

    internal void WriteState(Persistence.IStateWriter writer)
    {
        writer.Add(_s0);
        writer.Add(_s1);
        writer.Add(_s2);
        writer.Add(_s3);
    }

    internal void SetState(ulong s0, ulong s1, ulong s2, ulong s3)
    {
        if ((s0 | s1 | s2 | s3) == 0)
            throw new ArgumentException("xoshiro256** state must not be all zero.");
        _s0 = s0;
        _s1 = s1;
        _s2 = s2;
        _s3 = s3;
    }
}
