using System.Buffers.Binary;
using System.Text;
using SaveEditor.Core;

namespace SaveEditor.Games.GtaV;

/// <summary>One tagged chunk of the decrypted save body: 4-byte ASCII id, big-endian size (includes the 8-byte header), data.</summary>
public sealed class SaveBlock(string id, byte[] data)
{
    public const int HeaderSize = 8;

    public string Id { get; } = id;
    public byte[] Data { get; set; } = data;
    public int TotalSize => HeaderSize + Data.Length;

    public override string ToString() => $"{Id} ({TotalSize:N0} bytes)";
}

/// <summary>
/// The decrypted save body: a sequence of blocks ending with a "CHKS" block that holds the
/// body length and checksum (GTAV_Class::GetBlocks / VerifyHash / ReplaceBlocks, tokens 0x06000018/0x06000014/0x06000019).
/// </summary>
public sealed class BlockContainer
{
    public const string ChecksumId = "CHKS";

    public List<SaveBlock> Blocks { get; }

    /// <summary>Bytes after the last block (AES zero padding); kept so unedited saves round-trip exactly.</summary>
    public byte[] Trailer { get; }

    private readonly int _originalLength;

    private BlockContainer(List<SaveBlock> blocks, byte[] trailer)
    {
        Blocks = blocks;
        Trailer = trailer;
        _originalLength = blocks.Sum(b => b.TotalSize);
    }

    public SaveBlock Checksum => Blocks.FirstOrDefault(b => b.Id == ChecksumId)
        ?? throw new SaveFormatException("Save has no CHKS block.");

    public static BlockContainer Parse(byte[] body)
    {
        var blocks = new List<SaveBlock>();
        int pos = 0;
        while (pos + SaveBlock.HeaderSize <= body.Length)
        {
            var id = Encoding.ASCII.GetString(body, pos, 4);
            int size = BinaryPrimitives.ReadInt32BigEndian(body.AsSpan(pos + 4));
            if (!IsPrintableId(id) || size < SaveBlock.HeaderSize || pos + size > body.Length)
                throw new SaveFormatException(
                    $"Invalid block at 0x{pos:X} (id '{Sanitize(id)}', size {size}). Wrong key or not a GTA V PC save?");
            blocks.Add(new SaveBlock(id, body[(pos + SaveBlock.HeaderSize)..(pos + size)]));
            pos += size;
            if (id == ChecksumId) break;
        }
        if (blocks.Count == 0 || blocks[^1].Id != ChecksumId)
            throw new SaveFormatException("Save body ended without a CHKS block.");
        return new BlockContainer(blocks, body[pos..]);
    }

    /// <summary>Serialises all blocks exactly as stored (no trailer).</summary>
    public byte[] Serialize()
    {
        var output = new byte[Blocks.Sum(b => b.TotalSize)];
        int pos = 0;
        foreach (var b in Blocks)
        {
            Encoding.ASCII.GetBytes(b.Id, output.AsSpan(pos, 4));
            BinaryPrimitives.WriteInt32BigEndian(output.AsSpan(pos + 4), b.TotalSize);
            b.Data.CopyTo(output, pos + SaveBlock.HeaderSize);
            pos += b.TotalSize;
        }
        return output;
    }

    /// <summary>Length and hash as the game computes them: over all blocks with CHKS data[0..8] zeroed.</summary>
    public (int Length, uint Hash) ComputeChecksum()
    {
        var chks = Checksum;
        if (chks.Data.Length < 8) throw new SaveFormatException("CHKS block is too short.");
        var saved = chks.Data[..8];
        Array.Clear(chks.Data, 0, 8);
        try
        {
            var bytes = Serialize();
            return (bytes.Length, GtaHash.Checksum(bytes));
        }
        finally
        {
            saved.CopyTo(chks.Data, 0);
        }
    }

    public (int Length, uint Hash) StoredChecksum
    {
        get
        {
            var d = Checksum.Data;
            return (BinaryPrimitives.ReadInt32BigEndian(d), BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(4)));
        }
    }

    public IntegrityReport Verify()
    {
        var stored = StoredChecksum;
        var actual = ComputeChecksum();
        if (stored.Length != actual.Length)
            return new(false, $"Length mismatch: stored {stored.Length:N0}, actual {actual.Length:N0}.");
        if (stored.Hash != actual.Hash)
            return new(false, $"Checksum mismatch: stored 0x{stored.Hash:X8}, actual 0x{actual.Hash:X8}.");
        return new(true, $"Checksum OK (0x{actual.Hash:X8}, {actual.Length:N0} bytes).");
    }

    /// <summary>Recomputes CHKS and returns the body ready to encrypt.</summary>
    public byte[] Build()
    {
        var (length, hash) = ComputeChecksum();
        var d = Checksum.Data;
        BinaryPrimitives.WriteInt32BigEndian(d, length);
        BinaryPrimitives.WriteUInt32BigEndian(d.AsSpan(4), hash);
        var blocks = Serialize();
        // Unchanged size: keep the original trailer so the file round-trips byte-for-byte.
        // Otherwise SaveCrypto.Encrypt zero-pads to the AES block size.
        return blocks.Length == _originalLength ? [.. blocks, .. Trailer] : blocks;
    }

    private static bool IsPrintableId(string id) => id.All(c => c is >= ' ' and <= '~');

    private static string Sanitize(string id) => new(id.Select(c => c is >= ' ' and <= '~' ? c : '?').ToArray());
}
