using System.Buffers.Binary;
using System.Security.Cryptography;
using SaveEditor.Games.GtaV;

namespace SaveEditor.Tests;

public class StatStoreTests
{
    /// <summary>Builds a PSIN-like block holding the three stat tables, each padded with 20 filler stats.</summary>
    private static byte[] BuildPsin()
    {
        var ms = new MemoryStream();
        void Table(int stride, string anchor, ulong anchorValue)
        {
            ms.Write(Enumerable.Repeat((byte)0x70, 8).ToArray());
            var entries = Enumerable.Range(0, 20).Select(i => ((uint)(i * 0x0800_0000 + 7), 0UL)).ToList();
            entries.Add((GtaHash.Joaat(anchor), anchorValue));
            foreach (var (hash, value) in entries.OrderBy(e => e.Item1))
            {
                var e = new byte[stride];
                BinaryPrimitives.WriteUInt32BigEndian(e, hash);
                if (stride == 12) BinaryPrimitives.WriteUInt64BigEndian(e.AsSpan(4), value);
                else BinaryPrimitives.WriteUInt32BigEndian(e.AsSpan(4), (uint)value);
                ms.Write(e);
            }
        }
        Table(8, "SP0_TOTAL_CASH", 1234);
        Table(8, "TOTAL_PROGRESS_MADE", BitConverter.SingleToUInt32Bits(55f));
        Table(12, "SP0_TOTAL_PLAYING_TIME", 99_000);
        return ms.ToArray();
    }

    [Fact]
    public void FindsTablesAndEditsValues()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "SGTA50000");
        File.WriteAllBytes(path, GtaVSaveTests.BuildSaveFile(key, "t", GtaVSaveTests.BuildBody(("PSIN", BuildPsin()))));

        var doc = GtaVSaveDocument.Load(path, key);
        Assert.Equal(1234, doc.Stats.GetInt("SP0_TOTAL_CASH"));
        Assert.Equal(55f, doc.Stats.GetFloat("TOTAL_PROGRESS_MADE"));
        Assert.Equal(99_000UL, doc.Stats.GetUInt64("SP0_TOTAL_PLAYING_TIME"));

        var cash = StatCatalog.All.First(d => d.Name == "SP0_TOTAL_CASH");
        doc.SetStat(cash, 5_000_000);
        doc.Save(path);

        var reloaded = GtaVSaveDocument.Load(path, key);
        Assert.True(reloaded.Integrity.IsValid);
        Assert.Equal(5_000_000, reloaded.GetStat(cash));
    }

    [Fact]
    public void RejectsOutOfRangeAndReadOnlyEdits()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var dir = Directory.CreateTempSubdirectory();
        var path = Path.Combine(dir.FullName, "SGTA50000");
        File.WriteAllBytes(path, GtaVSaveTests.BuildSaveFile(key, "t", GtaVSaveTests.BuildBody(("PSIN", BuildPsin()))));
        var doc = GtaVSaveDocument.Load(path, key);

        var cash = StatCatalog.All.First(d => d.Name == "SP0_TOTAL_CASH");
        Assert.Throws<ArgumentOutOfRangeException>(() => doc.SetStat(cash, -1));
        var progress = StatCatalog.All.First(d => d.Name == "TOTAL_PROGRESS_MADE");
        Assert.Throws<InvalidOperationException>(() => doc.SetStat(progress, 50));
    }

    [Fact]
    public void ComparerReportsChangedStatsByName()
    {
        var key = RandomNumberGenerator.GetBytes(32);
        var dir = Directory.CreateTempSubdirectory();
        string a = Path.Combine(dir.FullName, "SGTA50000"), b = Path.Combine(dir.FullName, "SGTA50001");
        File.WriteAllBytes(a, GtaVSaveTests.BuildSaveFile(key, "t", GtaVSaveTests.BuildBody(("PSIN", BuildPsin()))));
        var doc = GtaVSaveDocument.Load(a, key);
        doc.SetStat(StatCatalog.All.First(d => d.Name == "SP0_TOTAL_CASH"), 1054);
        doc.Save(b);

        var result = SaveComparer.Compare(GtaVSaveDocument.Load(a, key), GtaVSaveDocument.Load(b, key));
        var change = Assert.Single(result.Stats);
        Assert.Equal("SP0_TOTAL_CASH", change.Name);
        Assert.Equal("Michael", change.Character);
        Assert.Equal("$1,234", change.Before);
        Assert.Equal("$1,054", change.After);
        Assert.Equal("-$180", change.Change);
    }

    [Theory]
    [InlineData("SGTA50000", 0, "Slot 1")]
    [InlineData("SGTA50014", 14, "Slot 15")]
    [InlineData("SGTA50015", 15, "Autosave")]
    public void SlotNamesMatchTheGame(string file, int index, string label)
    {
        Assert.Equal(index, SaveSlots.IndexOf(file));
        Assert.Equal(file, SaveSlots.FileName(index));
        Assert.Equal(label, SaveSlots.Describe(file));
    }

    [Theory]
    [InlineData("SGTA50016")]
    [InlineData("SGTA50000.bak")]
    [InlineData("mysave")]
    public void NonSlotNamesAreNotListedByTheGame(string file) => Assert.Null(SaveSlots.IndexOf(file));

    /// <summary>Real save checks; set GTAV_SAMPLE_SAVE and GTAV_KEY_FILE to enable.</summary>
    [Fact]
    public void RealSave_StatsReadAndEditRoundTrip()
    {
        var path = Environment.GetEnvironmentVariable("GTAV_SAMPLE_SAVE");
        var keyFile = Environment.GetEnvironmentVariable("GTAV_KEY_FILE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile)) return;
        var key = File.ReadAllBytes(keyFile);

        var doc = GtaVSaveDocument.Load(path, key);
        Assert.Equal(2599, doc.Stats.Tables[StatKind.Int32].Count);
        Assert.Equal(131, doc.Stats.Tables[StatKind.Float].Count);
        Assert.Equal(106, doc.Stats.Tables[StatKind.UInt64].Count);
        Assert.Equal(69, doc.Stats.GetInt("NUM_MISSIONS_COMPLETED"));
        Assert.Equal(100f, doc.Stats.GetFloat("TOTAL_PROGRESS_MADE"));
        foreach (var d in StatCatalog.All) doc.GetStat(d); // every catalog stat must exist

        // Edit a copy, never the original.
        var copy = Path.Combine(Directory.CreateTempSubdirectory().FullName, "SGTA50000");
        var cash = StatCatalog.All.First(d => d.Name == "SP1_TOTAL_CASH");
        doc.SetStat(cash, 123_456_789);
        doc.Save(copy);

        var reloaded = GtaVSaveDocument.Load(copy, key);
        Assert.True(reloaded.Integrity.IsValid);
        Assert.Equal(123_456_789, reloaded.GetStat(cash));
        Assert.Equal(new FileInfo(path).Length, new FileInfo(copy).Length);
    }
}
