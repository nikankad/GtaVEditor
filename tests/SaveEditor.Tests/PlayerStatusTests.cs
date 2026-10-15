using SaveEditor.GtaV;

namespace SaveEditor.Tests;

/// <summary>Real-save checks (GTAV_KEY_FILE + GTAV_SAMPLE_SAVE, a 100% save); they pass trivially without them.</summary>
public class PlayerStatusTests
{
    [Fact]
    public void ReadsAndEditsPlayerStatusAndClock()
    {
        var keyFile = Environment.GetEnvironmentVariable("GTAV_KEY_FILE");
        var path = Environment.GetEnvironmentVariable("GTAV_SAMPLE_SAVE");
        if (string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile) || string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var key = File.ReadAllBytes(keyFile);

        var doc = GtaVSaveDocument.Load(path, key);
        var player = doc.Player;
        Assert.Equal(["Michael", "Franklin", "Trevor"], player.Characters.Select(c => c.Name));
        Assert.All(player.Characters, c => Assert.Equal(100, c.Health));
        Assert.All(player.Characters, c => Assert.InRange(c.WantedLevel, 0, 5));
        Assert.InRange(player.CurrentCharacter, 0, 2);
        Assert.NotNull(player.Clock);
        Assert.InRange(player.Clock!.Hour, 0, 23);
        Assert.InRange(player.Clock.Month, 0, 11);

        var trevor = player.Characters[2];
        trevor.Armor = 50;
        trevor.WantedLevel = 3;
        trevor.SpecialAbility = 100;
        trevor.Position = (1985.5f, 3815.25f, 32.5f);
        player.Clock.SetTime(13, 37);
        player.Clock.SetDate(2013, 8, 17);
        Assert.Throws<ArgumentOutOfRangeException>(() => trevor.WantedLevel = 6);
        Assert.Throws<ArgumentOutOfRangeException>(() => player.Clock.SetDate(2013, 1, 30));

        var copy = Path.Combine(Directory.CreateTempSubdirectory().FullName, "SGTA50000");
        doc.Save(copy);
        var reloaded = GtaVSaveDocument.Load(copy, key);
        Assert.True(reloaded.Integrity.IsValid);
        var t = reloaded.Player.Characters[2];
        Assert.Equal((50, 3, 100), (t.Armor, t.WantedLevel, t.SpecialAbility));
        Assert.Equal((1985.5f, 3815.25f, 32.5f), t.Position);
        var clock = reloaded.Player.Clock!;
        Assert.Equal((13, 37, 2013, 8, 17), (clock.Hour, clock.Minute, clock.Year, clock.Month, clock.Day));
    }
}
