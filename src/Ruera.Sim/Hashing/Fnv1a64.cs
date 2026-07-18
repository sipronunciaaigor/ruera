namespace Ruera.Sim.Hashing;

/// <summary>
/// Incremental FNV-1a 64-bit hash over a canonical byte order (little-endian).
/// In-repo because object/string GetHashCode is randomized per process and
/// must never feed state hashes (DESIGN.md §2, rule 7).
/// </summary>
public struct Fnv1a64
{
    public const ulong OffsetBasis = 14695981039346656037UL;
    private const ulong Prime = 1099511628211UL;

    private ulong _hash;

    public static Fnv1a64 Create() => new() { _hash = OffsetBasis };

    public readonly ulong Hash => _hash;

    public void Add(byte value) => _hash = (_hash ^ value) * Prime;

    public void Add(ulong value)
    {
        for (var shift = 0; shift < 64; shift += 8)
            Add((byte)(value >> shift));
    }

    public void Add(long value) => Add(unchecked((ulong)value));

    public void Add(int value) => Add((long)value);

    public void Add(bool value) => Add(value ? (byte)1 : (byte)0);

    /// <summary>Length-prefixed UTF-8 bytes: canonical and culture-free.</summary>
    public void Add(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Add(bytes.Length);
        foreach (var b in bytes)
            Add(b);
    }
}
