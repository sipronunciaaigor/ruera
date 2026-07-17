namespace Ruera.Sim.Rng;

/// <summary>
/// Seeded RNG handed to sim systems. Implementations must be reproducible
/// bit-for-bit across platforms and .NET versions (DESIGN.md §2, rule 3);
/// System.Random and Guid.NewGuid are banned in this assembly.
/// </summary>
public interface IDeterministicRng
{
    /// <summary>Next 64 uniformly distributed bits.</summary>
    ulong NextUInt64();

    /// <summary>Uniform value in [minInclusive, maxExclusive), unbiased via rejection sampling.</summary>
    long NextInt64(long minInclusive, long maxExclusive);
}
