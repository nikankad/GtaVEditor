using System.Windows;
using System.Windows.Media;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>A collectible kind in the Collectibles picker: "Letter Scraps 12/50".</summary>
public sealed class CollectibleSetModel(CollectibleSet set) : Observable
{
    public CollectibleSet Set => set;
    public ImageSource? Blip => GamePictures.Collectible(set.Kind, found: true);
    public string Header => $"{set.Name}  {set.Collected}/{set.Total}";
    public string CountText => $"{set.Collected} of {set.Total} found";
}

/// <summary>One collectible: a tick box in the list and a dot on the map.</summary>
public sealed class CollectibleItemModel(CollectibleSet set, CollectibleItem item, Action onChanged) : Observable
{
    public const double Dot = 14;

    public string Label => $"No. {item.Number}";
    public ImageSource? Blip => GamePictures.Collectible(set.Kind, item.Collected);
    public bool HasLocation => item.Location != null;

    public string Tip => item.Location is { } l
        ? $"{set.Name} {item.Number}\n{l.X:0}, {l.Y:0}, {l.Z:0}" + (item.Collected ? "\nFound" : "\nNot found")
        : $"{set.Name} {item.Number}";

    // The details card shown when the item is picked on the map or in the list.
    public string Title => $"{set.Name} No. {item.Number}";
    public string Area => item.Location is { } l ? "Near " + MapLabel.Nearest(l.X, l.Y).Text : "Position not known";
    public string Coordinates => item.Location is { } l ? $"X {l.X:0}   Y {l.Y:0}   Z {l.Z:0}" : "";
    public string Status => item.Collected ? "Found" : "Not found yet";

    public double MapLeft => item.Location is { } l ? MapView.ToMap(l.X, l.Y).X - Dot / 2 : -100;
    public double MapTop => item.Location is { } l ? MapView.ToMap(l.X, l.Y).Y - Dot / 2 : -100;
    public int MapLayer => _selected ? 10 : 0;

    /// <summary>The middle of the dot, in map pixels.</summary>
    public Point MapCentre => new(MapLeft + Dot / 2, MapTop + Dot / 2);

    private bool _selected;

    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (value == _selected) return;
            _selected = value;
            Raise(nameof(IsSelected), nameof(MapLayer));
        }
    }

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

    // Finer district names (rough centres) used only to say where an item is; too many to draw on the map.
    private static readonly IReadOnlyList<MapLabel> Areas =
    [
        .. All,
        new("Downtown", -100, -800), new("Little Seoul", -700, -900), new("Vespucci Beach", -1350, -1350),
        new("Del Perro", -1500, -900), new("Rockford Hills", -800, -150), new("Richman", -1700, 300),
        new("Pacific Bluffs", -2000, -350), new("Strawberry", 150, -1450), new("Davis", 100, -1700),
        new("Rancho", 450, -1750), new("La Mesa", 800, -1000), new("Mirror Park", 1100, -600),
        new("El Burro Heights", 1350, -2000), new("Elysian Island", 200, -2700), new("Terminal", 950, -3100),
        new("Tataviam Mountains", 1800, -200), new("Palomino Highlands", 2500, -700), new("Banham Canyon", -2700, 1400),
        new("Tongva Hills", -1700, 2300), new("Great Chaparral", -400, 2700), new("Harmony", 500, 2700),
        new("Grand Senora Desert", 1500, 2900), new("Stab City", 60, 3700), new("Raton Canyon", -900, 4300),
        new("North Chumash", -2200, 4300), new("Paleto Forest", -500, 5200), new("Mount Gordo", 2900, 5500),
        new("Humane Labs", 3600, 3700),
    ];

    /// <summary>The place name closest to a world position, to say roughly where an item is.</summary>
    public static MapLabel Nearest(double x, double y) => Areas.MinBy(l => (l.X - x) * (l.X - x) + (l.Y - y) * (l.Y - y))!;

    public double Left => MapView.ToMap(X, Y).X - 40;
    public double Top => MapView.ToMap(X, Y).Y - 8;
}

/// <summary>One weapon's Ammu-Nation unlock for one character.</summary>
public sealed class WeaponUnlockModel(WeaponUnlock unlock, Action onChanged) : Observable
{
    public string Label => unlock.Weapon.Label;
    public ImageSource? Picture => GamePictures.Weapon(unlock.Weapon.Id);

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

/// <summary>One character's clothes on the Unlocks tab: what they own and what they're wearing now.</summary>
public sealed class WardrobeModel(Wardrobe wardrobe, Outfit? outfit, Action onChanged) : Observable
{
    public string Name => wardrobe.Character;
    public ImageSource? Portrait => GamePictures.Character(wardrobe.Character);
    public Brush Colour => GamePictures.CharacterColour(wardrobe.Character);
    public string OwnedText => $"{wardrobe.Owned:N0} of {wardrobe.Total:N0} clothing items owned";

    public IReadOnlyList<ClothingCategoryModel> Categories { get; } =
        wardrobe.Categories.Select(c => new ClothingCategoryModel(c, onChanged)).ToList();

    /// <summary>One drop-down per clothing slot; wearing an item also adds it to the wardrobe, so the totals update.</summary>
    public IReadOnlyList<OutfitSlotModel> Wearing => _wearing ??= outfit?.Slots.Select(s => new OutfitSlotModel(s, () =>
    {
        onChanged();
        Refresh();
        foreach (var c in Categories) c.Refresh();
    })).ToList() ?? [];

    private IReadOnlyList<OutfitSlotModel>? _wearing;

    public Visibility WearingVisibility => Wearing.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

    public void OwnAll()
    {
        wardrobe.OwnAll();
        onChanged();
        Refresh();
        foreach (var c in Categories) c.Refresh();
    }

    public override string ToString() => Name;
}

/// <summary>One slot of what a character is wearing, as a list of the shop items for it.</summary>
public sealed class OutfitSlotModel : Observable
{
    private const string Nothing = "Nothing", NotSold = "Something the shops don't sell";
    private readonly OutfitSlot _slot;
    private readonly Action _onChanged;

    public OutfitSlotModel(OutfitSlot slot, Action onChanged)
    {
        (_slot, _onChanged) = (slot, onChanged);
        var names = new List<string>();
        if (slot.IsProp) names.Add(Nothing);
        if (slot.Current == null && !slot.IsEmpty) names.Add(NotSold);
        names.AddRange(slot.Options.Select(o => o.Name));
        Options = names;
    }

    public string Label => _slot.Label;
    public IReadOnlyList<string> Options { get; }

    public string Selected
    {
        get => _slot.Current?.Name ?? (_slot.IsEmpty ? Nothing : NotSold);
        set
        {
            if (value == Selected) return;
            if (value == Nothing) _slot.Wear(null);
            else if (_slot.Options.FirstOrDefault(o => o.Name == value) is { } item) _slot.Wear(item);
            else return;
            _onChanged();
            Raise(nameof(Selected));
        }
    }
}

/// <summary>One kind of clothing (shoes, hatsâ€¦) with how much of it a character owns.</summary>
public sealed class ClothingCategoryModel(ClothingCategory category, Action onChanged) : Observable
{
    public string Label => category.Label;
    public string OwnedText => $"{category.Owned} of {category.Total}";
    public double Owned => category.Owned;
    public double Total => category.Total;
    public bool CanOwnAll => category.Owned < category.Total;

    public void OwnAll()
    {
        category.OwnAll();
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
    public string Group => flag.Group;
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
