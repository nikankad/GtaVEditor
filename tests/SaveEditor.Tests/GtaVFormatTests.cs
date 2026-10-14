using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SaveEditor.Core;
using SaveEditor.Games.GtaV;

namespace SaveEditor.Tests;

public class GtaHashTests
{
    [Fact]
    public void Joaat_MatchesKnownModelHash()
    {
        // "adder" is the well-known vehicle model hash 0xB779A091.
        Assert.Equal(0xB779A091u, GtaHash.Joaat("adder"));
        Assert.Equal(0xB779A091u, GtaHash.Joaat("ADDER"));
    }

    [Fact]
    public void Checksum_SignExtendsHighBytes()
    {
        // For ASCII input the checksum is a seeded JOAAT; bytes >= 0x80 must differ (sign extension).
        var ascii = "CHKS"u8.ToArray();
        Assert.Equal(SeededJoaat(ascii, GtaHash.ChecksumSeed), GtaHash.Checksum(ascii));
        byte[] high = [0x80, 0xFF];
        Assert.NotEqual(SeededJoaat(high, GtaHash.ChecksumSeed), GtaHash.Checksum(high));
    }

    private static uint SeededJoaat(byte[] data, uint h)
    {
        foreach (var b in data) { h += b; h += h << 10; h ^= h >> 6; }
        h += h << 3; h ^= h >> 11; h += h << 15;
        return h;
    }
}

public class GtaVSaveTests
{
    internal static byte[] BuildBody(params (string Id, byte[] Data)[] blocks)
    {
        var all = blocks.Append(("CHKS", new byte[12])).ToArray();
        var ms = new MemoryStream();
        foreach (var (id, data) in all)
        {
            ms.Write(Encoding.ASCII.GetBytes(id));
            var size = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(size, data.Length + 8);
            ms.Write(size);
            ms.Write(data);
        }
        // Fill in a correct checksum via the container itself.
        return BlockContainer.Parse(ms.ToArray()).Build();
    }

    internal static byte[] BuildSaveFile(byte[] key, string title, byte[] body)
    {
        var file = new SgtaFile { Version = 1, Title = title, Unknown104 = [1, 2, 3, 4], EncryptedBody = SaveCrypto.Encrypt(body, key) };
        return file.Write();
    }

    [Fact]
    public void Container_RoundTripsAndVerifies()
    {
        var body = BuildBody(("SAVE", RandomNumberGenerator.GetBytes(1000)), ("PSIN", [1, 2, 3]));
        var c = BlockContainer.Parse(body);
        Assert.Equal(["SAVE", "PSIN", "CHKS"], c.Blocks.Select(b => b.Id));
        Assert.True(c.Verify().IsValid);
        Assert.Equal(body, c.Build());
    }

    [Fact]
    public void Container_DetectsTamperingAndRepairsOnBuild()
    {
        var c = BlockContainer.Parse(BuildBody(("SAVE", new byte[64])));
        c.Blocks[0].Data[10] = 0x42;
        Assert.False(c.Verify().IsValid);
        var rebuilt = BlockContainer.Parse(c.Build());
        Assert.True(rebuilt.Verify().IsValid);
        Assert.Equal(0x42, rebuilt.Blocks[0].Data[10]);
    }

    [Fact]
    public void Container_RejectsGarbage()
    {
        Assert.Throws<SaveFormatException>(() => BlockContainer.Parse(RandomNumberGenerator.GetBytes(256)));
    }

    [Fact]
    public void SaveFile_EncryptDecryptRoundTrip()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "SGTA50000");
        File.WriteAllBytes(path, BuildSaveFile(key, "Mission 1", BuildBody(("SAVE", RandomNumberGenerator.GetBytes(333)))));

        var doc = GtaVSaveDocument.Load(path, key);
        Assert.Equal("Mission 1", doc.Title);
        Assert.True(doc.Integrity.IsValid);

        // Unedited save must round-trip byte-for-byte.
        Assert.Equal(File.ReadAllBytes(path), doc.ToBytes());

        doc.Title = "Edited";
        doc.Body.Blocks[0].Data[0] ^= 0xFF;
        doc.Save(path);
        var reloaded = GtaVSaveDocument.Load(path, key);
        Assert.Equal("Edited", reloaded.Title);
        Assert.True(reloaded.Integrity.IsValid);
        Assert.Equal([1, 2, 3, 4], File.ReadAllBytes(path)[0x104..0x108]);
    }

    [Fact]
    public void KeyStore_RejectsWrongKey()
    {
        var dir = Directory.CreateTempSubdirectory();
        var save = Path.Combine(dir.FullName, "SGTA50000");
        File.WriteAllBytes(save, BuildSaveFile(RandomNumberGenerator.GetBytes(32), "x", BuildBody(("SAVE", new byte[32]))));
        var store = new KeyStore(Path.Combine(dir.FullName, "k.key"));

        Assert.False(store.Import(RandomNumberGenerator.GetBytes(32), save).IsValid);
        Assert.False(store.HasKey);
    }

    [Fact]
    public void KeyStore_AcceptsCorrectKey()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var dir = Directory.CreateTempSubdirectory();
        var save = Path.Combine(dir.FullName, "SGTA50000");
        File.WriteAllBytes(save, BuildSaveFile(key, "x", BuildBody(("SAVE", new byte[32]))));
        var store = new KeyStore(Path.Combine(dir.FullName, "k.key"));

        Assert.True(store.Import(key, save).IsValid);
        Assert.Equal(key, store.Load());
    }

    /// <summary>Header check against a real PC save; set GTAV_SAMPLE_SAVE to its path to enable.</summary>
    [Fact]
    public void RealSave_HeaderParses()
    {
        var path = Environment.GetEnvironmentVariable("GTAV_SAMPLE_SAVE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

        var bytes = File.ReadAllBytes(path);
        var f = SgtaFile.Read(bytes);
        Assert.Equal(1u, f.Version);
        Assert.False(string.IsNullOrWhiteSpace(f.Title));
        Assert.Equal(bytes, f.Write());
        Assert.True(SgtaFile.LooksLikeSgta(path));
    }

    /// <summary>Full decrypt/verify/round-trip of a real save; set GTAV_SAMPLE_SAVE and GTAV_KEY_FILE to enable.</summary>
    [Fact]
    public void RealSave_DecryptsVerifiesAndRoundTrips()
    {
        var path = Environment.GetEnvironmentVariable("GTAV_SAMPLE_SAVE");
        var keyFile = Environment.GetEnvironmentVariable("GTAV_KEY_FILE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile)) return;

        var doc = GtaVSaveDocument.Load(path, File.ReadAllBytes(keyFile));

        Assert.True(doc.Integrity.IsValid, doc.Integrity.Summary);
        Assert.Equal("CHKS", doc.Body.Blocks[^1].Id);
        Assert.Equal(File.ReadAllBytes(path), doc.ToBytes());
    }
}

public class BackupServiceTests
{
    [Fact]
    public void Backup_CopiesFile()
    {
        var dir = Directory.CreateTempSubdirectory();
        var save = Path.Combine(dir.FullName, "SGTA50000");
        File.WriteAllText(save, "data");
        var svc = new BackupService(Path.Combine(dir.FullName, "backups"));

        var backup = svc.Backup("gtav-pc", save);

        Assert.NotNull(backup);
        Assert.Equal("data", File.ReadAllText(backup));
        Assert.Single(svc.ListBackups("gtav-pc"));
    }
}
