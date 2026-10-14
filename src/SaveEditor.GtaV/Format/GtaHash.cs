namespace SaveEditor.GtaV;

/// <summary>
/// Jenkins one-at-a-time variants used by GTA V.
/// Ported from the reference editor's "GTAV Class" assembly (see docs/FINDINGS.md).
/// </summary>
public static class GtaHash
{
    /// <summary>Seed of the CHKS save checksum (GTAV_Class::GetHash, token 0x06000016).</summary>
    public const uint ChecksumSeed = 0x3FAC7125;

    /// <summary>
    /// Save checksum stored in the CHKS block. Bytes are sign-extended (IL: ldelem.u1; conv.i1)
    /// before being added, which differs from the plain JOAAT for bytes >= 0x80.
    /// </summary>
    public static uint Checksum(ReadOnlySpan<byte> data)
    {
        uint h = ChecksumSeed;
        foreach (var b in data)
        {
            h += (uint)(sbyte)b;
            h += h << 10;
            h ^= h >> 6;
        }
        h += h << 3;
        h ^= h >> 11;
        h += h << 15;
        return h;
    }

    /// <summary>Standard JOAAT over unsigned bytes, seed 0 (GTAV_Class::GetNativeHash, token 0x0600000D).</summary>
    public static uint Joaat(ReadOnlySpan<byte> data)
    {
        uint h = 0;
        foreach (var b in data)
        {
            h += b;
            h += h << 10;
            h ^= h >> 6;
        }
        h += h << 3;
        h ^= h >> 11;
        h += h << 15;
        return h;
    }

    /// <summary>Game-style name hash (stats, models): JOAAT of the lower-cased ASCII name.</summary>
    public static uint Joaat(string name) => Joaat(System.Text.Encoding.ASCII.GetBytes(name.ToLowerInvariant()));
}
