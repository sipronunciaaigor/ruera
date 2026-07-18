using Ruera.Sim.Hashing;

namespace Ruera.Sim.Persistence;

/// <summary>
/// The single canonical state encoding (RUE-8 «writer unico»): one field
/// order, defined by <see cref="SimState.WriteTo"/>, feeds both the state
/// hash and snapshot bytes — never two orderings to keep aligned by hand.
/// Encoding: little-endian; int promoted to long; bool as one byte; string
/// as int length + UTF-8 bytes (matching <see cref="Fnv1a64"/> semantics).
/// </summary>
internal interface IStateWriter
{
    void Add(ulong value);

    void Add(long value);

    void Add(int value);

    void Add(bool value);

    void Add(string value);
}

/// <summary>Feeds the canonical field stream into the FNV-1a state hash.</summary>
internal sealed class HashStateWriter : IStateWriter
{
    private Fnv1a64 _hasher;

    public HashStateWriter(Fnv1a64 hasher) => _hasher = hasher;

    public Fnv1a64 Hasher => _hasher;

    public void Add(ulong value) => _hasher.Add(value);

    public void Add(long value) => _hasher.Add(value);

    public void Add(int value) => _hasher.Add(value);

    public void Add(bool value) => _hasher.Add(value);

    public void Add(string value) => _hasher.Add(value);
}

/// <summary>Feeds the canonical field stream into snapshot bytes.</summary>
internal sealed class BinaryStateWriter(BinaryWriter writer) : IStateWriter
{
    public void Add(ulong value) => writer.Write(value);

    public void Add(long value) => writer.Write(value);

    public void Add(int value) => writer.Write((long)value);

    public void Add(bool value) => writer.Write(value ? (byte)1 : (byte)0);

    public void Add(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        Add(bytes.Length);
        writer.Write(bytes);
    }
}

/// <summary>Mirror of <see cref="BinaryStateWriter"/> for snapshot restore.</summary>
internal sealed class BinaryStateReader(BinaryReader reader)
{
    public ulong ReadUInt64() => reader.ReadUInt64();

    public long ReadInt64() => reader.ReadInt64();

    public int ReadInt32() => checked((int)reader.ReadInt64());

    public bool ReadBoolean() => reader.ReadByte() != 0;

    public string ReadString()
    {
        var length = ReadInt32();
        return System.Text.Encoding.UTF8.GetString(reader.ReadBytes(length));
    }
}
