using System.Buffers.Binary;

namespace SaveEditor.GtaV;

public enum StatKind { Int32, Float, UInt64 }

/// <summary>
/// One sorted stat table inside the PSIN block: entries of big-endian (JOAAT name hash, value),
/// strictly ascending by hash. Observed in a real PC save.
/// </summary>
public sealed class StatTable
{
    private readonly byte[] _data;

    internal StatTable(byte[] data, int offset, int count, StatKind kind)
    {
        _data = data;
        Offset = offset;
        Count = count;
        Kind = kind;
    }

    public int Offset { get; }
    public int Count { get; }
    public StatKind Kind { get; }
    public int Stride => Kind == StatKind.UInt64 ? 12 : 8;

    private uint HashAt(int i) => BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(Offset + i * Stride));

    /// <summary>Binary search (the table is sorted by hash). Returns the byte offset of the value, or -1.</summary>
    public int FindValueOffset(uint hash)
    {
        int lo = 0, hi = Count - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            uint h = HashAt(mid);
            if (h == hash) return Offset + mid * Stride + 4;
            if (h < hash) lo = mid + 1; else hi = mid - 1;
        }
        return -1;
    }

    public bool Contains(uint hash) => FindValueOffset(hash) >= 0;

    /// <summary>Every (hash, raw value) pair; 32-bit values are zero-extended.</summary>
    public IEnumerable<(uint Hash, ulong Raw)> Entries()
    {
        for (int i = 0; i < Count; i++)
        {
            int o = Offset + i * Stride;
            yield return (HashAt(i), Stride == 12
                ? BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(o + 4))
                : BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(o + 4)));
        }
    }
}

/// <summary>Locates the Int32 / Float / UInt64 stat tables in the PSIN block and reads/writes values in place.</summary>
public sealed class StatStore
{
    public const string BlockId = "PSIN";

    // A stat known to live in each table identifies which table is which.
    private static readonly (StatKind Kind, uint Anchor)[] Anchors =
    [
        (StatKind.Int32, GtaHash.Joaat("SP0_TOTAL_CASH")),
        (StatKind.Float, GtaHash.Joaat("TOTAL_PROGRESS_MADE")),
        (StatKind.UInt64, GtaHash.Joaat("SP0_TOTAL_PLAYING_TIME")),
    ];

    private readonly byte[] _data;
    private readonly Dictionary<StatKind, StatTable> _tables;

    private StatStore(byte[] data, Dictionary<StatKind, StatTable> tables)
    {
        _data = data;
        _tables = tables;
    }

    public IReadOnlyDictionary<StatKind, StatTable> Tables => _tables;

    public static StatStore Load(BlockContainer body)
    {
        var psin = body.Blocks.FirstOrDefault(b => b.Id == BlockId)
            ?? throw new SaveFormatException("Save has no PSIN block.");
        var data = psin.Data;
        var tables = new Dictionary<StatKind, StatTable>();

        Span<byte> pattern = stackalloc byte[4];
        foreach (var (kind, anchor) in Anchors)
        {
            int stride = kind == StatKind.UInt64 ? 12 : 8;
            BinaryPrimitives.WriteUInt32BigEndian(pattern, anchor);
            // Grow a strictly ascending run around each occurrence of the anchor hash; the table is the longest one.
            StatTable? best = null;
            for (int pos = 0, hit; (hit = data.AsSpan(pos).IndexOf(pattern)) >= 0; pos += hit + 1)
            {
                int at = pos + hit, start = at, end = at; // end = offset of last entry
                if (at > data.Length - stride) continue;
                // Hash 0 is not a real stat name; it is padding just before a table.
                while (start - stride >= 0 && Hash(data, start - stride) is var h && h != 0 && h < Hash(data, start)) start -= stride;
                while (end + 2 * stride <= data.Length && Hash(data, end + stride) > Hash(data, end)) end += stride;
                int count = (end - start) / stride + 1;
                if (count >= MinTableEntries && (best == null || count > best.Count))
                    best = new StatTable(data, start, count, kind);
            }
            tables[kind] = best ?? throw new SaveFormatException($"Could not locate the {kind} stat table in PSIN.");
        }
        return new StatStore(data, tables);
    }

    private const int MinTableEntries = 16;

    private static uint Hash(byte[] d, int offset) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(offset));

    private int Locate(StatKind kind, string name)
    {
        int off = _tables[kind].FindValueOffset(GtaHash.Joaat(name));
        if (off < 0) throw new KeyNotFoundException($"Stat '{name}' is not in this save.");
        return off;
    }

    public bool Has(StatKind kind, string name) => _tables[kind].Contains(GtaHash.Joaat(name));

    public int GetInt(string name) => BinaryPrimitives.ReadInt32BigEndian(_data.AsSpan(Locate(StatKind.Int32, name)));
    public void SetInt(string name, int value) => BinaryPrimitives.WriteInt32BigEndian(_data.AsSpan(Locate(StatKind.Int32, name)), value);

    public float GetFloat(string name) => BinaryPrimitives.ReadSingleBigEndian(_data.AsSpan(Locate(StatKind.Float, name)));
    public void SetFloat(string name, float value) => BinaryPrimitives.WriteSingleBigEndian(_data.AsSpan(Locate(StatKind.Float, name)), value);

    public ulong GetUInt64(string name) => BinaryPrimitives.ReadUInt64BigEndian(_data.AsSpan(Locate(StatKind.UInt64, name)));
    public void SetUInt64(string name, ulong value) => BinaryPrimitives.WriteUInt64BigEndian(_data.AsSpan(Locate(StatKind.UInt64, name)), value);
}
