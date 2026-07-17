using Ruera.Sim.Hashing;

namespace Ruera.Sim.Tests;

public class HashingTests
{
    [Fact]
    public void EmptyHash_IsOffsetBasis()
    {
        Assert.Equal(0xcbf29ce484222325UL, Fnv1a64.Create().Hash);
    }

    [Fact]
    public void MatchesPublishedReferenceVector()
    {
        // FNV-1a 64 of the single byte 'a' (0x61) — published test vector.
        var hasher = Fnv1a64.Create();
        hasher.Add((byte)0x61);

        Assert.Equal(0xaf63dc4c8601ec8cUL, hasher.Hash);
    }

    [Fact]
    public void CanonicalHash_IsIndependentOfDictionaryInsertionOrder()
    {
        // AC RUE-11: state hashing must not depend on undefined iteration
        // order. Build the same logical map with different insertion history
        // (including add/remove churn to scramble bucket layout).
        var ascending = new Dictionary<long, long>();
        for (long key = 0; key < 100; key++)
            ascending[key] = key * 31;

        var scrambled = new Dictionary<long, long>();
        for (long key = 5000; key < 5100; key++)
            scrambled[key] = 1; // churn: forces a different internal layout
        for (long key = 5000; key < 5100; key++)
            scrambled.Remove(key);
        for (long key = 99; key >= 0; key--)
            scrambled[key] = key * 31;

        var hashA = Fnv1a64.Create();
        CanonicalHash.AddEntries(ref hashA, ascending);
        var hashB = Fnv1a64.Create();
        CanonicalHash.AddEntries(ref hashB, scrambled);

        Assert.Equal(hashA.Hash, hashB.Hash);
    }

    [Fact]
    public void CanonicalHash_IsSensitiveToContent()
    {
        var a = new Dictionary<long, long> { [1] = 10, [2] = 20 };
        var b = new Dictionary<long, long> { [1] = 10, [2] = 21 };

        var hashA = Fnv1a64.Create();
        CanonicalHash.AddEntries(ref hashA, a);
        var hashB = Fnv1a64.Create();
        CanonicalHash.AddEntries(ref hashB, b);

        Assert.NotEqual(hashA.Hash, hashB.Hash);
    }
}
