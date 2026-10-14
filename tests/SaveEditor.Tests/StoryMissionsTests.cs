using SaveEditor.Games.GtaV;

namespace SaveEditor.Tests;

/// <summary>
/// Real-save checks. GTAV_KEY_FILE is the key; GTAV_SAMPLE_SAVE a 100% save; GTAV_STORY_SAVE an early save that has
/// finished Franklin and Lamar but not Repossession (the "1.6%" save). Tests without their inputs pass trivially.
/// </summary>
public class StoryMissionsTests
{
    private static byte[]? Key() =>
        Environment.GetEnvironmentVariable("GTAV_KEY_FILE") is { Length: > 0 } k && File.Exists(k) ? File.ReadAllBytes(k) : null;

    private static string? Save(string variable) =>
        Environment.GetEnvironmentVariable(variable) is { Length: > 0 } p && File.Exists(p) ? p : null;

    [Fact]
    public void CatalogCoversEachRecordOnceAndEveryChoiceHasMissions()
    {
        var all = typeof(StoryMissions).Assembly.GetType("SaveEditor.Games.GtaV.MissionInfo")!
            .GetProperty("All")!.GetValue(null) as System.Collections.IEnumerable;
        var indices = all!.Cast<object>().Select(o => (int)o.GetType().GetProperty("Index")!.GetValue(o)!).ToList();
        Assert.Equal(indices.Count, indices.Distinct().Count());
        Assert.All(indices, i => Assert.InRange(i, 0, 93));
        Assert.Equal(5, StoryMissions.Choices.Count);
    }

    [Fact]
    public void FinishedSave_HasNothingLeftOnItsOwnPath()
    {
        var key = Key();
        var path = Save("GTAV_SAMPLE_SAVE");
        if (key == null || path == null) return;

        var missions = GtaVSaveDocument.Load(path, key).Missions;
        // The choices default to what the save took, so its own path is fully completed.
        Assert.Null(missions.Next);
        Assert.All(missions.Path, m => Assert.True(m.Completed, m.Name));
    }

    [Fact]
    public void CompletingTheNextMission_WritesWhatTheGameWrites()
    {
        var key = Key();
        var path = Save("GTAV_STORY_SAVE");
        if (key == null || path == null) return;

        var doc = GtaVSaveDocument.Load(path, key);
        var missions = doc.Missions;
        var next = missions.Next!;
        Assert.Equal("ARM2", next.Code);
        Assert.Equal(-1, next.Order);

        missions.SetCompleted(next, true);
        // The game wrote these same values for Repossession in the later saves of this playthrough.
        Assert.True(next.Completed);
        Assert.Equal(2, next.Order);
        Assert.Equal(3, doc.Stats.GetInt("FL_CO_ARM2"));
        Assert.Equal(3, doc.Stats.GetInt("NUM_MISSIONS_COMPLETED"));
        Assert.True(doc.Checklist.Single(i => i.Label == "M_ARM2").Completed);
        Assert.Equal("FRA0", missions.Next!.Code);

        // Save to a copy (never the original) and read it back.
        var copy = Path.Combine(Directory.CreateTempSubdirectory().FullName, Path.GetFileName(path));
        doc.Save(copy);
        var reloaded = GtaVSaveDocument.Load(copy, key);
        Assert.True(reloaded.Integrity.IsValid);
        var again = reloaded.Missions.Single(m => m.Code == "ARM2");
        Assert.True(again.Completed);
        Assert.Equal(2, again.Order);

        // Undoing puts the record back the way an unplayed mission looks.
        reloaded.Missions.SetCompleted(again, false);
        Assert.False(again.Completed);
        Assert.Equal(-1, again.Order);
        Assert.Equal(0, reloaded.Stats.GetInt("FL_CO_ARM2"));
        Assert.False(reloaded.Checklist.Single(i => i.Label == "M_ARM2").Completed);
        Assert.Equal(2, reloaded.Stats.GetInt("NUM_MISSIONS_COMPLETED"));
    }

    [Fact]
    public void CompleteUpTo_FollowsTheChosenApproach()
    {
        var key = Key();
        var path = Save("GTAV_STORY_SAVE");
        if (key == null || path == null) return;

        var missions = GtaVSaveDocument.Load(path, key).Missions;
        missions.Select(StoryChoice.JewelStore, 'B');
        missions.CompleteUpTo(missions.Single(m => m.Code == "JH2"));

        Assert.True(missions.Single(m => m.Code == "JHP1B").Completed);
        Assert.False(missions.Single(m => m.Code == "JHP1A").Completed);
        Assert.False(missions.Single(m => m.Code == "JHP2A").Completed);
        Assert.Equal("TRV1", missions.Next!.Code);
        // Completion order keeps counting up in story order.
        var orders = missions.Path.TakeWhile(m => m.Code != "TRV1").Select(m => m.Order).ToList();
        Assert.All(orders.Zip(orders.Skip(1)), p => Assert.True(p.First < p.Second));
        Assert.Throws<ArgumentException>(() => missions.CompleteUpTo(missions.Single(m => m.Code == "JHP1A")));
    }
}
