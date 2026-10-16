using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SaveEditor.GtaV;

// Synthetic inputs only: these checks never need a game installation, player save, or production key.
var work = Directory.CreateTempSubdirectory("gtav-checks-").FullName;
int passed = 0, failed = 0;
try
{
    Check("Unedited encrypted saves round-trip byte-for-byte", () =>
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var bytes = Save(key);
        var path = Path.Combine(work, "roundtrip");
        File.WriteAllBytes(path, bytes);
        var doc = GtaVSaveDocument.Load(path, key);
        Assert(doc.Integrity.IsValid && !doc.HasChanges);
        Assert(bytes.SequenceEqual(doc.ToBytes()));
    });
    Check("Saving rebases changes and reopens with a valid checksum", () =>
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var path = Path.Combine(work, "edited");
        File.WriteAllBytes(path, Save(key));
        var doc = GtaVSaveDocument.Load(path, key);
        doc.Title = "Edited";
        Assert(doc.HasChanges);
        doc.Save(path);
        Assert(!doc.HasChanges && doc.FilePath == path);
        var result = GtaVSaveDocument.Load(path, key);
        Assert(result.Title == "Edited" && result.Integrity.IsValid);
    });
    Check("Saving preserves a pre-existing .tmp file", () =>
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var path = Path.Combine(work, "temp-collision");
        File.WriteAllBytes(path, Save(key));
        File.WriteAllText(path + ".tmp", "Keep me");
        var doc = GtaVSaveDocument.Load(path, key);
        doc.Title = "Another title";
        doc.Save(path);
        Assert(File.ReadAllText(path + ".tmp") == "Keep me");
    });
    Check("Failed writes clean up owned temporary files and retain changes", () =>
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var path = Path.Combine(work, "failed-save");
        File.WriteAllBytes(path, Save(key));
        var doc = GtaVSaveDocument.Load(path, key);
        doc.Title = "Unsaved";
        var target = Directory.CreateDirectory(Path.Combine(work, "target-directory")).FullName;
        bool denied = false;
        try { doc.Save(target); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { denied = true; }
        Assert(denied);
        Assert(doc.HasChanges && doc.FilePath == path);
        Assert(!Directory.EnumerateFiles(work, "target-directory.*.tmp").Any());
        Assert(GtaVSaveDocument.Load(path, key).Title == "Original");
    });
    Check("Read limits reject oversized keys and files", () =>
    {
        var path = Path.Combine(work, "too-large");
        File.WriteAllBytes(path, new byte[1025]);
        Throws<SaveFormatException>(() => SgtaFile.ReadAllBytesLimited(path, 1024));
        Throws<SaveFormatException>(() => new KeyStore(path).Load());
        Assert(SgtaFile.ReadAllBytesLimited(path, 1025).Length == 1025);
    });
    Check("Unknown save versions are rejected", () =>
    {
        var bytes = Save(RandomNumberGenerator.GetBytes(32));
        Put(bytes, 0, 2);
        Throws<SaveFormatException>(() => SgtaFile.Read(bytes));
    });
    Check("Oversized saves are rejected before allocation or decryption", () =>
    {
        var path = Path.Combine(work, "oversized-save");
        using (var stream = File.Create(path)) stream.SetLength(SgtaFile.MaxFileBytes + 1);
        Throws<SaveFormatException>(() => GtaVSaveDocument.Load(path, RandomNumberGenerator.GetBytes(32)));
    });
    Check("Wrong keys are not installed; valid keys are verified and stored", () =>
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var path = Path.Combine(work, "key-sample");
        File.WriteAllBytes(path, Save(key));
        var store = new KeyStore(Path.Combine(work, "keys", "test.key"));
        Assert(!store.Import(RandomNumberGenerator.GetBytes(32), path).IsValid && !store.HasKey);
        Assert(store.Import(key, path).IsValid && store.Load().SequenceEqual(key));
    });
    Check("Truncated and overflowing blocks fail with format errors", () =>
    {
        foreach (int length in new[] { 0, 4, 7 })
        {
            var bytes = new byte[8 + length];
            "CHKS"u8.CopyTo(bytes);
            Put(bytes, 4, bytes.Length);
            Throws<SaveFormatException>(() => BlockContainer.Parse(bytes));
        }
        var overflow = new byte[24];
        "PSIN"u8.CopyTo(overflow);
        Put(overflow, 4, 16);
        "CHKS"u8.CopyTo(overflow.AsSpan(16));
        Put(overflow, 20, int.MaxValue);
        Throws<SaveFormatException>(() => BlockContainer.Parse(overflow));
    });
    Check("Backup restoration preserves both the backup and replaced file", () =>
    {
        var original = Save(RandomNumberGenerator.GetBytes(32));
        var replacement = Save(RandomNumberGenerator.GetBytes(32));
        var path = Path.Combine(work, "SGTA50000");
        File.WriteAllBytes(path, original);
        var backups = new BackupService(Path.Combine(work, "backups"));
        var backup = backups.Backup(path)!;
        File.WriteAllBytes(path, replacement);
        var previous = backups.Restore(backup, path)!;
        Assert(File.ReadAllBytes(backup).SequenceEqual(original));
        Assert(File.ReadAllBytes(path).SequenceEqual(original));
        Assert(File.ReadAllBytes(previous).SequenceEqual(replacement));
        Throws<SaveFormatException>(() => backups.Restore(backup, backup));
    });
    Check("Oversized backup metadata does not hide the backup", () =>
    {
        var path = Path.Combine(work, "metadata-source");
        File.WriteAllBytes(path, Save(RandomNumberGenerator.GetBytes(32)));
        var backups = new BackupService(Path.Combine(work, "metadata-backups"));
        var backup = backups.Backup(path)!;
        File.WriteAllText(backup + ".json", new string('x', 20_000));
        Assert(backups.Entries().Single().OriginalPath == null);
    });
    Check("PMAP integer overflows cannot create an out-of-bounds region", () =>
    {
        var (data, map, schema) = Schema();
        Put(map, 12, int.MaxValue);
        Put(map, 20, int.MaxValue);
        Throws<SaveFormatException>(() => PsoDocument.Load(Body(("PSIN", data), ("PMAP", map), ("PSCH", schema))));
    });
    Check("Truncated PSO headers and invalid enum counts are rejected", () =>
    {
        var (data, map, schema) = Schema();
        Throws<SaveFormatException>(() => PsoDocument.Load(Body(("PSIN", data), ("PMAP", new byte[1]), ("PSCH", schema))));
        Put(schema, 12, 0x01FFFFFF);
        Throws<SaveFormatException>(() => PsoDocument.Load(Body(("PSIN", data), ("PMAP", map), ("PSCH", schema))));
    });
    Check("Duplicate field hashes are rejected before feature dictionaries are built", () =>
    {
        var (data, map, schema) = Schema((PsoType.SInt, 0, 0u), (PsoType.SInt, 4, 0u));
        Put(schema, 36, 10); // Give the second field the first field's name hash.
        Throws<SaveFormatException>(() => PsoDocument.Load(Body(("PSIN", data), ("PMAP", map), ("PSCH", schema))));
    });
    Check("Truncated PSO values are skipped; out-of-bounds pointers are null", () =>
    {
        var (data, map, schema) = Schema((PsoType.SInt, 15, 0u));
        var pso = PsoDocument.Load(Body(("PSIN", data), ("PMAP", map), ("PSCH", schema)));
        Assert(pso.Root.Children.Count == 0);
        Assert(pso.ResolvePointer(-1) == null && pso.ResolvePointer(data.Length - 1) == null);
    });
    Check("Cyclic structures terminate without expanding an endless tree", () =>
    {
        var (data, map, schema) = Schema((PsoType.Structure, 0, 1u));
        var pso = PsoDocument.Load(Body(("PSIN", data), ("PMAP", map), ("PSCH", schema)));
        Assert(pso.Walk().Count() == 2);
    });
    Check("Valid PSO values stay editable and preserve neighboring bytes", () =>
    {
        var (data, map, schema) = Schema((PsoType.SInt, 0, 0u));
        data[8] = 99;
        var pso = PsoDocument.Load(Body(("PSIN", data), ("PMAP", map), ("PSCH", schema)));
        pso.Root.Children.Single().SetInt32(42);
        Assert(pso.Root.Children.Single().GetInt32() == 42 && pso.Data[8] == 99);
    });
    Check("Corrupted-checksum detection and repair preserve payload data", () =>
    {
        var body = Body(("PSIN", new byte[32]));
        body.Blocks[0].Data[4] = 123;
        Assert(!body.Verify().IsValid);
        var repaired = BlockContainer.Parse(body.Build());
        Assert(repaired.Verify().IsValid && repaired.Blocks[0].Data[4] == 123);
    });
    Check("Repeated schema records cannot bypass the allocation budget", () =>
    {
        var (data, map, source) = Schema(Enumerable.Repeat((PsoType.UByte, 0, 0u), 100).ToArray());
        const int count = 1001;
        int record = 4 + count * 8;
        var schema = new byte[record + source.Length - 12];
        Put(schema, 0, count);
        for (int i = 0; i < count; i++)
        {
            Put(schema, 4 + i * 8, i + 1);
            Put(schema, 8 + i * 8, record + 8);
        }
        source.AsSpan(12).CopyTo(schema.AsSpan(record));
        Throws<SaveFormatException>(() => PsoDocument.Load(Body(("PSIN", data), ("PMAP", map), ("PSCH", schema))));
    });
    Check("Malformed PSO mutations produce format errors instead of runtime exceptions", () =>
    {
        var random = new Random(20261016);
        for (int i = 0; i < 1000; i++)
        {
            var (data, map, schema) = Schema((PsoType.SInt, 0, 0u), (PsoType.Array, 4, 0u));
            var target = i % 2 == 0 ? map : schema;
            target[random.Next(target.Length)] = (byte)random.Next(256);
            try
            {
                var pso = PsoDocument.Load(Body(("PSIN", data), ("PMAP", map), ("PSCH", schema)));
                foreach (var node in pso.Walk()) _ = node.ValueText;
            }
            catch (SaveFormatException) { }
        }
    });
}
finally { Directory.Delete(work, recursive: true); }
Console.WriteLine($"{passed} passed, {failed} failed.");
return failed == 0 ? 0 : 1;

void Check(string name, Action action)
{
    try { action(); passed++; Console.WriteLine("PASS " + name); }
    catch (Exception ex) { failed++; Console.Error.WriteLine($"FAIL {name}: {ex}"); }
}
static void Assert(bool condition) { if (!condition) throw new Exception("Assertion failed."); }
static void Throws<T>(Action action) where T : Exception
{
    try { action(); } catch (T) { return; }
    throw new Exception($"Expected {typeof(T).Name}.");
}
static void Put(byte[] bytes, int offset, int value) => BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(offset), value);
static BlockContainer Body(params (string Id, byte[] Data)[] blocks)
{
    using var stream = new MemoryStream();
    foreach (var (id, data) in blocks.Append(("CHKS", new byte[12])))
    {
        stream.Write(Encoding.ASCII.GetBytes(id));
        var size = new byte[4];
        Put(size, 0, data.Length + 8);
        stream.Write(size);
        stream.Write(data);
    }
    var body = BlockContainer.Parse(stream.ToArray());
    return BlockContainer.Parse(body.Build());
}
static byte[] Save(byte[] key) => new SgtaFile
{
    Version = 1, Title = "Original", EncryptedBody = SaveCrypto.Encrypt(Body(("PSIN", new byte[64])).Build(), key)
}.Write();
static (byte[] Data, byte[] Map, byte[] Schema) Schema(params (PsoType Type, int Offset, uint Reference)[] fields)
{
    var map = new byte[24];
    Put(map, 0, 1);
    BinaryPrimitives.WriteUInt16BigEndian(map.AsSpan(4), 1);
    Put(map, 8, 1);
    Put(map, 12, 8);
    Put(map, 20, 16);
    var schema = new byte[24 + fields.Length * 12];
    Put(schema, 0, 1);
    Put(schema, 4, 1);
    Put(schema, 8, 20); // Offsets include the section header.
    Put(schema, 12, fields.Length);
    Put(schema, 16, 16);
    for (int i = 0; i < fields.Length; i++)
    {
        int at = 24 + i * 12;
        Put(schema, at, 10 + i);
        schema[at + 4] = (byte)fields[i].Type;
        BinaryPrimitives.WriteUInt16BigEndian(schema.AsSpan(at + 6), (ushort)fields[i].Offset);
        BinaryPrimitives.WriteUInt32BigEndian(schema.AsSpan(at + 8), fields[i].Reference);
    }
    return (new byte[16], map, schema);
}
