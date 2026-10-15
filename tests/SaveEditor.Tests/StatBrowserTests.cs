using SaveEditor.GtaV;

namespace SaveEditor.Tests;

public class StatBrowserTests
{
    [Fact]
    public void EveryStatHasItsOwnLabel()
    {
        var names = StatBrowser.AllNames.ToList();
        Assert.True(names.Count > 600, $"only {names.Count} labelled stats");
        Assert.All(names, n => Assert.DoesNotContain('_', n.Label));
        Assert.Equal(names.Count, names.Select(n => n.Label).Distinct().Count());
        var labels = names.ToDictionary(n => n.Name, n => n.Label);
        Assert.Equal("Shooting range high score: pistols, challenge 1", labels["MG_RANGE_PIST1_HISCORE"]);
        Assert.Equal("Letter scraps found", labels["NUM_HIDDEN_PACKAGES_0"]);
        Assert.Equal("Pistol .50 headshot kills", labels["PISTOL50_HEADSHOTS"]);
    }

    /// <summary>Real-save check (GTAV_KEY_FILE + GTAV_SAMPLE_SAVE); passes trivially without them.</summary>
    [Fact]
    public void ListsAndEditsStats()
    {
        var keyFile = Environment.GetEnvironmentVariable("GTAV_KEY_FILE");
        var path = Environment.GetEnvironmentVariable("GTAV_SAMPLE_SAVE");
        if (string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile) || string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var key = File.ReadAllBytes(keyFile);

        var doc = GtaVSaveDocument.Load(path, key);
        var stats = doc.StatList;
        Assert.True(stats.Count > 1000, $"only {stats.Count} stats");
        Assert.Equal(stats.Count, stats.Select(s => s.Name).Distinct().Count());

        var range = stats.Single(s => s.Name == "SP1_MG_RANGE_PIST1_HISCORE");
        Assert.Equal(("Franklin", "Shooting range", StatValueType.Int), (range.Character, range.Category, range.Type));
        var stamina = stats.Single(s => s.Name == "SP0_STAMINA");
        Assert.Equal((0L, 100L), (stamina.Min!.Value, stamina.Max!.Value));
        Assert.Throws<ArgumentException>(() => stamina.SetText("101"));
        Assert.Throws<ArgumentException>(() => range.SetText("lots"));
        Assert.True(stats.Single(s => s.Name == "TOTAL_PROGRESS_MADE").IsReadOnly);
        Assert.Contains(stats, s => s.Type == StatValueType.Float);
        Assert.Contains(stats, s => s.Type == StatValueType.UInt64);

        range.SetText("1,234");
        stamina.SetText("42");
        var copy = Path.Combine(Directory.CreateTempSubdirectory().FullName, "SGTA50000");
        doc.Save(copy);
        var reloaded = GtaVSaveDocument.Load(copy, key);
        Assert.True(reloaded.Integrity.IsValid);
        Assert.Equal(1234, reloaded.Stats.GetInt("SP1_MG_RANGE_PIST1_HISCORE"));
        Assert.Equal(42, reloaded.Stats.GetInt("SP0_STAMINA"));
    }
}

public class HeistCrewTests
{
    /// <summary>Real-save check on the 1.6% save (GTAV_STORY_SAVE): few crew unlocked, none dead.</summary>
    [Fact]
    public void UnlocksAndRevivesCrew()
    {
        var keyFile = Environment.GetEnvironmentVariable("GTAV_KEY_FILE");
        var path = Environment.GetEnvironmentVariable("GTAV_STORY_SAVE");
        if (string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile) || string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var key = File.ReadAllBytes(keyFile);

        var doc = GtaVSaveDocument.Load(path, key);
        Assert.Equal((6, 0), (doc.Crew.Unlocked, doc.Crew.Dead)); // Crew_Unlocked_Bitset 978 = bits 1,4,6,7,8,9
        doc.Crew.UnlockAll();
        doc.Crew.ReviveAll();
        var copy = Path.Combine(Directory.CreateTempSubdirectory().FullName, "SGTA50001");
        doc.Save(copy);
        var reloaded = GtaVSaveDocument.Load(copy, key);
        Assert.True(reloaded.Integrity.IsValid);
        Assert.Equal((HeistCrew.Members, 0), (reloaded.Crew.Unlocked, reloaded.Crew.Dead));
    }
}

public class GarageTests
{
    [Fact]
    public void CatalogHasUniqueModels() =>
        Assert.Equal(VehicleCatalog.All.Count, VehicleCatalog.All.Select(v => v.Hash).Distinct().Count());

    /// <summary>Real-save check: the 100% sample's parked cars, then parking a car in the empty 1.6% save.</summary>
    [Fact]
    public void ReadsAndParksVehicles()
    {
        var keyFile = Environment.GetEnvironmentVariable("GTAV_KEY_FILE");
        var sample = Environment.GetEnvironmentVariable("GTAV_SAMPLE_SAVE");
        var story = Environment.GetEnvironmentVariable("GTAV_STORY_SAVE");
        if (string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile) || !File.Exists(sample) || !File.Exists(story)) return;
        var key = File.ReadAllBytes(keyFile);

        var full = GtaVSaveDocument.Load(sample!, key).Garages;
        Assert.Equal(12, full.Count);
        Assert.Equal(["Truffade Z-Type", "Invetero Coquette Classic"], full.Where(s => s.Owner == "Michael").Select(s => s.VehicleName));

        var doc = GtaVSaveDocument.Load(story!, key);
        var adder = VehicleCatalog.All.Single(v => v.Model == "adder");
        var spot = doc.Garages.First(s => s.Garage.StartsWith("Franklin's aunt"));
        spot.Place(adder, VehicleColour.All[0]);
        var copy = Path.Combine(Directory.CreateTempSubdirectory().FullName, "SGTA50001");
        doc.Save(copy);
        var reloaded = GtaVSaveDocument.Load(copy, key);
        Assert.True(reloaded.Integrity.IsValid);
        var parked = reloaded.Garages.First(s => s.Garage.StartsWith("Franklin's aunt"));
        Assert.Equal("Truffade Adder", parked.VehicleName);
        parked.Clear();
        Assert.True(parked.IsEmpty);
    }
}

public class PropertyTests
{
    /// <summary>Real-save check on the 98.8% story point (GTAV_STORY_SAVE's sibling %98.8/SGTA50003 is not required; uses GTAV_STORY_SAVE).</summary>
    [Fact]
    public void GivesAircraftAndBusinesses()
    {
        var keyFile = Environment.GetEnvironmentVariable("GTAV_KEY_FILE");
        var story = Environment.GetEnvironmentVariable("GTAV_STORY_SAVE");
        var sample = Environment.GetEnvironmentVariable("GTAV_SAMPLE_SAVE");
        if (string.IsNullOrEmpty(keyFile) || !File.Exists(keyFile) || !File.Exists(story) || !File.Exists(sample)) return;
        var key = File.ReadAllBytes(keyFile);

        var full = GtaVSaveDocument.Load(sample!, key);
        Assert.Equal(["Buckingham Luxor Deluxe", "P-996 Lazer"], full.PropertyVehicles.Take(2).Select(s => s.VehicleName));
        Assert.All(full.Properties, p => Assert.NotNull(p.OwnerIndex));
        Assert.All(full.Properties, p => Assert.Equal(p.UsualOwner, p.OwnerIndex));

        var doc = GtaVSaveDocument.Load(story!, key);
        Assert.All(doc.Properties, p => Assert.Null(p.OwnerIndex));
        var trevorHangar = doc.PropertyVehicles[2];
        trevorHangar.Place(VehicleCatalog.All.Single(v => v.Model == "hydra"), VehicleColour.All[0]);
        doc.Properties[0].OwnerIndex = 1;
        var copy = Path.Combine(Directory.CreateTempSubdirectory().FullName, "SGTA50001");
        doc.Save(copy);
        var reloaded = GtaVSaveDocument.Load(copy, key);
        Assert.True(reloaded.Integrity.IsValid);
        Assert.Equal("Mammoth Hydra", reloaded.PropertyVehicles[2].VehicleName);
        Assert.Equal(1, reloaded.Properties[0].OwnerIndex);
        reloaded.Properties[0].OwnerIndex = null;
        Assert.Null(reloaded.Properties[0].OwnerIndex);
    }
}
