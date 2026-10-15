using SaveEditor.GtaV;

namespace SaveEditor.Tests;

/// <summary>Real save checks; set GTAV_KEY_FILE plus GTAV_SAMPLE_SAVE (a 100% save) and/or GTAV_STORY_SAVE (an early one).</summary>
public class CollectibleTests
{
    private static GtaVSaveDocument? Open(string variable)
    {
        var path = Environment.GetEnvironmentVariable(variable);
        var keyFile = Environment.GetEnvironmentVariable("GTAV_KEY_FILE");
        if (string.IsNullOrEmpty(path) || !File.Exists(path) || string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile)) return null;
        return GtaVSaveDocument.Load(path, File.ReadAllBytes(keyFile));
    }

    private static GtaVSaveDocument SaveAndReload(GtaVSaveDocument doc)
    {
        var copy = Path.Combine(Directory.CreateTempSubdirectory().FullName, "SGTA50000");
        doc.Save(copy);
        var reloaded = GtaVSaveDocument.Load(copy, File.ReadAllBytes(Environment.GetEnvironmentVariable("GTAV_KEY_FILE")!));
        Assert.True(reloaded.Integrity.IsValid);
        return reloaded;
    }

    [Fact]
    public void FinishedSave_HasEverythingFoundAndCountsMatchTheGame()
    {
        if (Open("GTAV_SAMPLE_SAVE") is not { } doc) return;
        foreach (var set in doc.Collectibles)
        {
            Assert.Equal(set.Total, set.Collected);
            if (set.CountStat != null && doc.Stats.Has(StatKind.Int32, set.CountStat)) Assert.Equal(set.Total, doc.Stats.GetInt(set.CountStat));
        }
        var expected = new Dictionary<CollectibleKind, int>
        {
            [CollectibleKind.LetterScraps] = 50, [CollectibleKind.SpaceshipParts] = 50, [CollectibleKind.EpsilonTracts] = 10,
            [CollectibleKind.NuclearWaste] = 30, [CollectibleKind.SubmarinePieces] = 30, [CollectibleKind.UnderTheBridge] = 50,
            [CollectibleKind.KnifeFlights] = 15, [CollectibleKind.MonkeyMosaics] = 50, [CollectibleKind.Peyote] = 27,
        };
        foreach (var (kind, total) in expected) Assert.Equal(total, doc.Collectibles.Single(s => s.Kind == kind).Total);
        // Each mapped kind has positions for every item but at most one (the scripts give the last item as 0,0,0).
        Assert.All(doc.Collectibles.Where(s => s.Kind <= CollectibleKind.KnifeFlights),
            s => Assert.True(s.Items.Count(i => i.Location == null) <= 1, $"{s.Kind} has too many unknown positions"));
        Assert.True(doc.DirectorMode.AllUnlocked);
        Assert.All(doc.FlightSchool, l => Assert.Equal(3, l.Medal));
        Assert.Contains(doc.Flags, f => f.Id == "FLAG_LETTER_SCRAPS_DONE" && f.On && f.Name == "All letter scraps found");
        // Every labelled flag is a real flag in the save, and only labelled flags are shown.
        Assert.Empty(GameFlags.AllLabels.Keys.Except(doc.Flags.Select(f => f.Id)));
        Assert.All(doc.Flags, f => Assert.Contains(f.Group, GameFlags.Groups));
    }

    [Fact]
    public void FinishedSave_UnfindingAnItemUpdatesTheCounterAndChecklist()
    {
        if (Open("GTAV_SAMPLE_SAVE") is not { } doc) return;
        var letters = doc.Collectibles.Single(s => s.Kind == CollectibleKind.LetterScraps);
        letters.Items[11].Collected = false;
        Assert.Equal(49, doc.Stats.GetInt("NUM_HIDDEN_PACKAGES_0"));
        Assert.False(doc.Checklist.Single(i => i.Label == "SC_CO_DLS1").Completed);

        var bridges = doc.Collectibles.Single(s => s.Kind == CollectibleKind.UnderTheBridge);
        bridges.Items[0].Collected = false;
        Assert.Equal(15, doc.Collectibles.Single(s => s.Kind == CollectibleKind.KnifeFlights).Collected); // shares the same fields

        var reloaded = SaveAndReload(doc);
        var reletters = reloaded.Collectibles.Single(s => s.Kind == CollectibleKind.LetterScraps);
        Assert.False(reletters.Items[11].Collected);
        Assert.Equal(49, reletters.Collected);
        Assert.Equal(49, reloaded.Collectibles.Single(s => s.Kind == CollectibleKind.UnderTheBridge).Collected);
    }

    [Fact]
    public void EarlySave_FindingEverythingAndUnlockingRoundTrips()
    {
        if (Open("GTAV_STORY_SAVE") is not { } doc) return;
        foreach (var set in doc.Collectibles) set.SetAll(true);
        Assert.Equal(50, doc.Stats.GetInt("NUM_HIDDEN_PACKAGES_1"));
        Assert.True(doc.Checklist.Single(i => i.Label == "SC_MSC_KFT").Completed);
        foreach (var u in doc.WeaponUnlocks) u.Unlocked = true;
        foreach (var w in doc.Wardrobes) w.OwnAll();
        doc.DirectorMode.UnlockAll();
        foreach (var l in doc.FlightSchool) l.Medal = 3;
        Assert.True(doc.Checklist.Single(i => i.Label == "SC_MGPS12").Completed);

        var reloaded = SaveAndReload(doc);
        Assert.All(reloaded.Collectibles, s => Assert.Equal(s.Total, s.Collected));
        Assert.All(reloaded.WeaponUnlocks, u => Assert.True(u.Unlocked));
        Assert.All(reloaded.Wardrobes, w => Assert.Equal(w.Total, w.Owned));
        Assert.True(reloaded.DirectorMode.AllUnlocked);
        Assert.All(reloaded.FlightSchool, l => Assert.Equal(3, l.Medal));
    }
}
