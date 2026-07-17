namespace Ruera.Sim.Hashing;

/// <summary>
/// Order-independent hashing for unordered collections: entries are sorted by
/// key before hashing, so bucket layout and insertion order never leak into
/// the state hash (DESIGN.md §2, rule 5).
/// </summary>
public static class CanonicalHash
{
    /// <summary>Hashes key/value entries in ascending key order. Keys must be distinct.</summary>
    public static void AddEntries(ref Fnv1a64 hasher, IEnumerable<KeyValuePair<long, long>> entries)
    {
        var sorted = entries.ToArray();
        Array.Sort(sorted, static (a, b) => a.Key.CompareTo(b.Key));

        hasher.Add(sorted.Length);
        foreach (var (key, value) in sorted)
        {
            hasher.Add(key);
            hasher.Add(value);
        }
    }
}
