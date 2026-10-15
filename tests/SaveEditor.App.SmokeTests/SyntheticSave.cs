using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using SaveEditor.GtaV;

namespace SaveEditor.TestSupport;

// Shared with core regression tests so the generated UI fixture can be validated on Linux too.
internal static class SyntheticSave
{
    private static byte[] BuildStatTables()
    {
        using var stream = new MemoryStream();
        void Table(int stride, string anchor, ulong value)
        {
            stream.Write(Enumerable.Repeat((byte)0x70, 8).ToArray());
            var entries = Enumerable.Range(0, 20).Select(i => ((uint)(i * 0x0800_0000 + 7), 0UL)).ToList();
            entries.Add((GtaHash.Joaat(anchor), value));
            foreach (var (hash, number) in entries.OrderBy(e => e.Item1))
            {
                var bytes = new byte[stride];
                BinaryPrimitives.WriteUInt32BigEndian(bytes, hash);
                if (stride == 12) BinaryPrimitives.WriteUInt64BigEndian(bytes.AsSpan(4), number);
                else BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), (uint)number);
                stream.Write(bytes);
            }
        }
        Table(8, "SP0_TOTAL_CASH", 1234);
        Table(8, "TOTAL_PROGRESS_MADE", BitConverter.SingleToUInt32Bits(27.5f));
        Table(12, "SP0_TOTAL_PLAYING_TIME", 99000);
        return stream.ToArray();
    }

    private static byte[] BuildBody(params (string Id, byte[] Data)[] blocks)
    {
        using var stream = new MemoryStream();
        foreach (var (id, data) in blocks.Append(("CHKS", new byte[12])))
        {
            stream.Write(Encoding.ASCII.GetBytes(id));
            var size = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(size, data.Length + 8);
            stream.Write(size);
            stream.Write(data);
        }
        return BlockContainer.Parse(stream.ToArray()).Build();
    }

    private static byte[] BuildSaveFile(byte[] key, string title, byte[] body) =>
        new SgtaFile { Version = 1, Title = title, EncryptedBody = SaveCrypto.Encrypt(body, key) }.Write();

    private const uint RootType = 0xAAAA0101, TableType = 0xAAAA0102, EntryType = 0xAAAA0103;
    private const uint TableField = 0x1A341F71, LabelField = 0x2D4B9B3A, WeightField = 0x8EF72701,
        CompletedField = 0x11782F19, CategoryField = 0xEB5686E6;
    private const int EntryLength = 56;

    internal sealed record Entry(string Label, float Weight, bool Done, int Category);

    /// <summary>A save whose script data holds only a 100% checklist with the given entries (no stats).</summary>
    public static GtaVSaveDocument Build(string folder, params Entry[] entries)
    {
        static byte[] Struct(int length, params (uint Name, PsoType Type, int Offset, uint Ref)[] fields)
        {
            var b = new byte[12 + fields.Length * 12];
            BinaryPrimitives.WriteUInt32BigEndian(b, (uint)fields.Length);
            BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), length);
            for (int i = 0; i < fields.Length; i++)
            {
                var e = b.AsSpan(12 + i * 12);
                BinaryPrimitives.WriteUInt32BigEndian(e, fields[i].Name);
                e[4] = (byte)fields[i].Type;
                BinaryPrimitives.WriteUInt16BigEndian(e[6..], (ushort)fields[i].Offset);
                BinaryPrimitives.WriteUInt32BigEndian(e[8..], fields[i].Ref);
            }
            return b;
        }

        int tableLength = entries.Length * EntryLength;
        var schema = new (uint Hash, byte[] Data)[]
        {
            (RootType, Struct(tableLength, (TableField, PsoType.Structure, 0, TableType))),
            (TableType, Struct(tableLength, entries.Select((_, i) =>
                (0xEE000000 + (uint)i, PsoType.Structure, i * EntryLength, EntryType)).ToArray())),
            (EntryType, Struct(EntryLength, (LabelField, PsoType.Float4, 0, 0u), (WeightField, PsoType.Float, 32, 0u),
                (CompletedField, PsoType.SInt, 40, 0u), (CategoryField, PsoType.SInt, 48, 0u))),
        };
        var psch = new MemoryStream();
        var index = new byte[4 + schema.Length * 8];
        BinaryPrimitives.WriteInt32BigEndian(index, schema.Length);
        int at = 8 + index.Length;
        for (int i = 0; i < schema.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(index.AsSpan(4 + i * 8), schema[i].Hash);
            BinaryPrimitives.WriteInt32BigEndian(index.AsSpan(8 + i * 8), at);
            at += schema[i].Data.Length;
        }
        psch.Write(index);
        foreach (var s in schema) psch.Write(s.Data);

        var psin = new byte[8 + tableLength];
        for (int i = 0; i < entries.Length; i++)
        {
            var e = psin.AsSpan(8 + i * EntryLength);
            Encoding.ASCII.GetBytes(entries[i].Label).CopyTo(e);
            BinaryPrimitives.WriteSingleBigEndian(e[32..], entries[i].Weight);
            BinaryPrimitives.WriteInt32BigEndian(e[40..], entries[i].Done ? 1 : 0);
            BinaryPrimitives.WriteInt32BigEndian(e[48..], entries[i].Category);
        }
        var pmap = new byte[8 + 16];
        BinaryPrimitives.WriteInt32BigEndian(pmap, 1);
        BinaryPrimitives.WriteUInt16BigEndian(pmap.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(pmap.AsSpan(8), RootType);
        BinaryPrimitives.WriteInt32BigEndian(pmap.AsSpan(12), 8 + 8);
        BinaryPrimitives.WriteInt32BigEndian(pmap.AsSpan(20), tableLength);

        var key = RandomNumberGenerator.GetBytes(32);
        var path = Path.Combine(folder, "SGTA50000");
        File.WriteAllBytes(path, BuildSaveFile(key, "t",
            BuildBody(("PSIN", [.. psin, .. BuildStatTables()]), ("PMAP", pmap), ("PSCH", psch.ToArray()))));
        return GtaVSaveDocument.Load(path, key);
    }

}
