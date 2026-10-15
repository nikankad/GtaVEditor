using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>A collectible kind in the Collectibles picker: "Letter Scraps 12/50".</summary>
public sealed class CollectibleSetModel(CollectibleSet set) : Observable
{
    public CollectibleSet Set => set;
    public string Header => $"{set.Name}  {set.Collected}/{set.Total}";
    public string CountText => $"{set.Collected} of {set.Total} found";
}

/// <summary>One collectible: a tick box in the list and a dot on the map.</summary>
public sealed class CollectibleItemModel(CollectibleSet set, CollectibleItem item, Action onChanged) : Observable
{
    // The map spans the playable area in game metres; north is up.
    public const double MapWidth = 400, MinX = -4100, MaxX = 4500, MinY = -4300, MaxY = 8300;
    public const double Scale = MapWidth / (MaxX - MinX), MapHeight = (MaxY - MinY) * Scale, Dot = 10;

    public string Label => $"No. {item.Number}";

    public string Tip => item.Location is { } l
        ? $"{set.Name} {item.Number}\n{l.X:0}, {l.Y:0}, {l.Z:0}" + (item.Collected ? "\nFound" : "\nNot found")
        : $"{set.Name} {item.Number}";

    public double MapLeft => item.Location is { } l ? (l.X - MinX) * Scale - Dot / 2 : -100;
    public double MapTop => item.Location is { } l ? (MaxY - l.Y) * Scale - Dot / 2 : -100;

    public bool Collected
    {
        get => item.Collected;
        set
        {
            if (value == item.Collected) return;
            item.Collected = value;
            onChanged();
            Refresh();
        }
    }
}

/// <summary>A place name drawn on the collectibles map for orientation.</summary>
public sealed record MapLabel(string Text, double X, double Y)
{
    public static IReadOnlyList<MapLabel> All { get; } =
    [
        new("Los Santos", 0, -1200), new("LSIA", -1200, -2900), new("Vinewood", 300, 900), new("Chumash", -3100, 900),
        new("Sandy Shores", 1800, 3700), new("Grapeseed", 2200, 5000), new("Mount Chiliad", 500, 5600),
        new("Paleto Bay", -300, 6400), new("Fort Zancudo", -2300, 3100), new("Alamo Sea", 900, 4200),
    ];

    public double Left => (X - CollectibleItemModel.MinX) * CollectibleItemModel.Scale - 40;
    public double Top => (CollectibleItemModel.MaxY - Y) * CollectibleItemModel.Scale - 8;
}

/// <summary>One weapon's Ammu-Nation unlock for one character.</summary>
public sealed class WeaponUnlockModel(WeaponUnlock unlock, Action onChanged) : Observable
{
    public string Label => unlock.Weapon.Label;

    public bool Unlocked
    {
        get => unlock.Unlocked;
        set
        {
            if (value == unlock.Unlocked) return;
            unlock.Unlocked = value;
            onChanged();
            Raise(nameof(Unlocked));
        }
    }
}

public sealed record WeaponUnlockGroup(string Category, IReadOnlyList<WeaponUnlockModel> Weapons);

/// <summary>One character's clothes on the Unlocks tab.</summary>
public sealed class WardrobeModel(Wardrobe wardrobe, Action onChanged) : Observable
{
    public string Name => wardrobe.Character;
    public string OwnedText => $"{wardrobe.Owned:N0} clothing items owned";

    public void OwnAll()
    {
        wardrobe.OwnAll();
        onChanged();
        Refresh();
    }
}

/// <summary>One Flight School lesson with a medal picker.</summary>
public sealed class FlightLessonModel(FlightLesson lesson, Action onChanged) : Observable
{
    public string Name => lesson.Name;
    public IReadOnlyList<string> Medals => FlightLesson.Medals;

    public string Medal
    {
        get => FlightLesson.Medals[lesson.Medal];
        set
        {
            int m = Array.IndexOf(FlightLesson.Medals, value);
            if (m < 0 || m == lesson.Medal) return;
            lesson.Medal = m;
            onChanged();
            Raise(nameof(Medal));
        }
    }
}

/// <summary>One story flow flag on the Game Flags tab.</summary>
public sealed class GameFlagModel(GameFlag flag, Action onChanged) : Observable
{
    public string Name => flag.Name;
    public string Id => flag.Id;

    public bool On
    {
        get => flag.On;
        set
        {
            if (value == flag.On) return;
            flag.On = value;
            onChanged();
            Raise(nameof(On));
        }
    }

    public bool Matches(string search) =>
        Name.Contains(search, StringComparison.OrdinalIgnoreCase) || Id.Contains(search, StringComparison.OrdinalIgnoreCase);
}

public sealed record DirectorRow(string Title, string Count);
