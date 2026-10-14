using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using SaveEditor.Games.GtaV;

namespace SaveEditor.Tests;

public class ChecklistTests
{
    private const uint RootType = 0xAAAA0101, TableType = 0xAAAA0102, EntryType = 0xAAAA0103;
    private const uint TableField = 0x1A341F71, LabelField = 0x2D4B9B3A, WeightField = 0x8EF72701,
        CompletedField = 0x11782F19, CategoryField = 0xEB5686E6;
    private const int EntryLength = 56;

    private sealed record Entry(string Label, float Weight, bool Done, int Category);

    /// <summary>A save whose script data holds only a 100% checklist with the given entries (no stats).</summary>
    private static GtaVSaveDocument Build(params Entry[] entries)
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
        var path = Path.Combine(Directory.CreateTempSubdirectory().FullName, "SGTA50000");
        File.WriteAllBytes(path, GtaVSaveTests.BuildSaveFile(key, "t",
            GtaVSaveTests.BuildBody(("PSIN", psin), ("PMAP", pmap), ("PSCH", psch.ToArray()))));
        return GtaVSaveDocument.Load(path, key);
    }

    [Fact]
    public void PercentageCountsOnlyRequiredEntriesAndAtMost14RandomEvents()
    {
        var entries = new List<Entry>
        {
            new("M_ARM1", 27.5f, true, 1), new("M_ARM2", 27.5f, false, 1), // missions: 55% in total
            new("SC_MG_DART", 10, true, 3), new("MGSP", 5, true, 4),        // hobby; special-edition one doesn't count
            new("SC_RC_OME1", 10, true, 7), new("SC_RC_BA1", 5, true, 8),  // S&F; optional one doesn't count
            new("SC_MSC_CIN", 15, false, 11), new("SC_FR_BAR", 5, true, 13), new("SC_MSC_YOG", 5, true, 12),
            new("OJDA1", 0, true, 6), new("", 9, true, 1),                   // odd job (weight 0); unused entry
        };
        entries.AddRange(Enumerable.Range(0, 20).Select(i => new Entry($"RE{i}", 0.25f, i < 7, 9)));
        var checklist = Checklist.Load(Build([.. entries]).Pso, null);

        Assert.Equal(entries.Count - 1, checklist.Count); // the entry without a label is skipped
        Assert.Equal("Franklin and Lamar", checklist[0].Name);
        Assert.Equal("RE3", checklist.Single(i => i.Label == "RE3").Name); // unknown labels show as themselves
        // 27.5 + 10 + 10 + 5 (friend activity) + 7 of 14 random events = 2.5
        Assert.Equal(55f, checklist.Percentage, 3);
        var events = checklist.Progress.Single(p => p.Group == ChecklistGroup.RandomEvents);
        Assert.Equal((7, 14), (events.Completed, events.Needed));

        checklist.SetCompleted(checklist.Where(i => i.Group == ChecklistGroup.RandomEvents), true);
        Assert.Equal(57.5f, checklist.Percentage, 3); // 20 done, only 14 count: the full 5%
        Assert.Equal(14, checklist.Figures["NUM_RNDEVENTS_COMPLETED"]);

        checklist.SetCompleted(checklist.Where(i => i.Required), true);
        Assert.Equal(100f, checklist.Percentage, 3);
        Assert.Equal(100, checklist.Figures["PERCENT_STORY_MISSIONS"]);

        checklist.SetCompleted(checklist.Single(i => i.Label == "M_ARM2"), false);
        Assert.False(checklist.Single(i => i.Label == "M_ARM2").Completed);
        Assert.Equal(72.5f, checklist.Percentage, 3);
        Assert.Equal(50, checklist.Figures["PERCENT_STORY_MISSIONS"]);
    }

    /// <summary>Real save checks; set GTAV_SAMPLE_SAVE (a 100% save) and GTAV_KEY_FILE to enable.</summary>
    [Fact]
    public void RealSave_FiguresMatchTheGameAndEditsRoundTrip()
    {
        var path = Environment.GetEnvironmentVariable("GTAV_SAMPLE_SAVE");
        var keyFile = Environment.GetEnvironmentVariable("GTAV_KEY_FILE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile)) return;
        var key = File.ReadAllBytes(keyFile);

        var doc = GtaVSaveDocument.Load(path, key);
        foreach (var (name, value) in doc.Checklist.Figures)
            Assert.Equal(value, doc.Stats.Has(StatKind.Float, name) ? doc.Stats.GetFloat(name) : doc.Stats.GetInt(name), 3);

        // Edit a copy, never the original.
        var repossession = doc.Checklist.Single(i => i.Label == "M_ARM2");
        doc.Checklist.SetCompleted(repossession, false);
        Assert.Equal(100 - repossession.Weight, doc.Stats.GetFloat("TOTAL_PROGRESS_MADE"), 3);
        Assert.Equal(68, doc.Stats.GetInt("NUM_MISSIONS_COMPLETED"));
        Assert.Equal(98, doc.Stats.GetInt("PERCENT_STORY_MISSIONS"));
        var copy = Path.Combine(Directory.CreateTempSubdirectory().FullName, "SGTA50000");
        doc.Save(copy);

        var reloaded = GtaVSaveDocument.Load(copy, key);
        Assert.True(reloaded.Integrity.IsValid);
        Assert.False(reloaded.Checklist.Single(i => i.Label == "M_ARM2").Completed);
        Assert.Equal(68, reloaded.Stats.GetInt("NUM_MISSIONS_COMPLETED"));
        reloaded.Checklist.SetCompleted(reloaded.Checklist.Single(i => i.Label == "M_ARM2"), true);
        Assert.Equal(100f, reloaded.Stats.GetFloat("TOTAL_PROGRESS_MADE"), 3);
    }
}
