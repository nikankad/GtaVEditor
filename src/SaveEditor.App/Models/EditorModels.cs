using System.ComponentModel;
using System.Windows;
using SaveEditor.GtaV;

namespace SaveEditor.App;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected void Raise(params string[] names)
    {
        foreach (var n in names) PropertyChanged?.Invoke(this, new(n));
    }

    /// <summary>Re-reads every property (after another view changed the same save bytes).</summary>
    public void Refresh() => PropertyChanged?.Invoke(this, new(""));
}

/// <summary>A 0–100 skill; the slider writes straight into the save.</summary>
public sealed class SkillModel(GtaVSaveDocument doc, StatDefinition def, Action onChanged) : Observable
{
    public string Label => def.Label;

    public double Value
    {
        get => doc.GetStat(def);
        set
        {
            long v = Math.Clamp((long)Math.Round(value), def.Min, def.Max);
            if (v == doc.GetStat(def)) return;
            doc.SetStat(def, v);
            onChanged();
            Raise(nameof(Value));
        }
    }

    public void Max() => Value = def.Max;
}

/// <summary>One character's money and skills.</summary>
public sealed class CharacterModel : Observable, IDataErrorInfo
{
    private readonly GtaVSaveDocument _doc;
    private readonly StatDefinition? _cash;
    private readonly Action _onChanged;
    private string? _cashError, _pendingCash;

    public CharacterModel(GtaVSaveDocument doc, string name, Action onChanged)
    {
        _doc = doc;
        _onChanged = onChanged;
        Name = name;
        var mine = StatCatalog.All.Where(d => d.Character == name && d.Editable && doc.Stats.Has(d.Kind, d.Name)).ToList();
        _cash = mine.FirstOrDefault(d => d.Name.EndsWith("_TOTAL_CASH"));
        Skills = mine.Where(d => d.Unit == "%").Select(d => new SkillModel(doc, d, onChanged)).ToList();
    }

    public string Name { get; }
    public IReadOnlyList<SkillModel> Skills { get; }
    public bool HasCash => _cash != null;

    public string Cash
    {
        get => _pendingCash ?? (_cash == null ? "" : $"${_doc.GetStat(_cash):N0}");
        set
        {
            _cashError = null;
            _pendingCash = null;
            if (_cash == null) return;
            var text = value.Replace(",", "").Replace("$", "").Trim();
            if (!long.TryParse(text, out var n)) (_cashError, _pendingCash) = ("Enter an amount in dollars, e.g. 1,000,000.", value);
            else if (n < _cash.Min || n > _cash.Max) (_cashError, _pendingCash) = ($"Cash must be between $0 and ${_cash.Max:N0}.", value);
            else if (n != _doc.GetStat(_cash))
            {
                _doc.SetStat(_cash, n);
                _onChanged();
            }
            Raise(nameof(Cash), nameof(CashError));
        }
    }

    public string CashError => _cashError ?? "";

    public void MaxSkills()
    {
        foreach (var s in Skills) s.Max();
    }

    public string Error => _cashError ?? "";
    public string this[string column] => column == nameof(Cash) ? _cashError ?? "" : "";
}

/// <summary>One weapon card: ticking gives the weapon, unticking takes it away.</summary>
public sealed class WeaponModel(CharacterWeapon weapon, Action onChanged) : Observable, IDataErrorInfo
{
    private string? _error, _pending;

    public CharacterWeapon Weapon => weapon;
    public string Label => weapon.Weapon.Label;
    public bool UsesAmmo => weapon.Weapon.UsesAmmo;
    public Visibility AmmoVisibility => UsesAmmo ? Visibility.Visible : Visibility.Collapsed;
    public string MaxText => $"of {weapon.Weapon.MaxAmmo:N0}";

    public bool Owned
    {
        get => weapon.Owned;
        set
        {
            if (value == weapon.Owned) return;
            if (value) weapon.Give(); else weapon.Remove();
            _error = _pending = null;
            onChanged();
            Refresh();
        }
    }

    public string Ammo
    {
        get => _pending ?? (weapon.Owned ? weapon.Ammo.ToString("N0") : "");
        set
        {
            _error = _pending = null;
            if (!weapon.Owned) { Refresh(); return; }
            if (!int.TryParse(value.Replace(",", "").Trim(), out var n)) (_error, _pending) = ("Enter a whole number.", value);
            else if (n < 0 || n > weapon.Weapon.MaxAmmo) (_error, _pending) = ($"Between 0 and {weapon.Weapon.MaxAmmo:N0}.", value);
            else if (n != weapon.Ammo)
            {
                weapon.Ammo = n;
                onChanged();
            }
            Refresh();
        }
    }

    public string AmmoError => _error ?? "";

    public void Give() => Owned = true;
    public void Remove() => Owned = false;

    public void FillAmmo()
    {
        if (!weapon.Owned || !UsesAmmo || weapon.Ammo == weapon.Weapon.MaxAmmo) return;
        weapon.Ammo = weapon.Weapon.MaxAmmo;
        _error = _pending = null;
        onChanged();
        Refresh();
    }

    public string Error => _error ?? "";
    public string this[string column] => column == nameof(Ammo) ? _error ?? "" : "";
}

public sealed record WeaponGroup(string Category, IReadOnlyList<WeaponModel> Weapons);

/// <summary>One checklist entry; ticking marks it done in the save and updates the completion figures.</summary>
public sealed class ChecklistItemModel(Checklist checklist, ChecklistItem item, Action onChanged) : Observable
{
    public ChecklistItem Item => item;
    public string Name => item.Name;
    public string Label => item.Label;

    /// <summary>Shown under the name for entries that don't count toward 100%.</summary>
    public string Note => item.Required ? "" : item.Weight > 0 ? "Optional" : "Doesn't count toward 100%";

    public Visibility NoteVisibility => Note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    public bool Done
    {
        get => item.Completed;
        set
        {
            if (value == item.Completed) return;
            checklist.SetCompleted(item, value);
            onChanged();
            Raise(nameof(Done));
        }
    }
}

/// <summary>One story mission; ticking marks its mission record completed so the game skips it when the story gets there.</summary>
public sealed class MissionModel(StoryMissions missions, StoryMission mission, Action onChanged) : Observable
{
    public StoryMission Mission => mission;
    public string Name => mission.Name;
    public string Code => mission.Code;

    /// <summary>"Smart approach", "Background event", or nothing.</summary>
    public string Note => mission.Choice is { } c
        ? StoryMissions.Choices[c].Options.Single(o => o.Letter == mission.Letter).Name +
          (c == StoryChoice.Ending ? " ending" : " approach")
        : mission.Optional ? "Background event" : "";

    public Visibility NoteVisibility => Note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>True for the first mission the story still waits on.</summary>
    public bool IsNext => missions.Next == mission;

    public bool Done
    {
        get => mission.Completed;
        set
        {
            if (value == mission.Completed) return;
            missions.SetCompleted(mission, value);
            onChanged();
        }
    }
}

/// <summary>A named place a character can be moved to.</summary>
public sealed record LocationPreset(string Name, float X, float Y, float Z)
{
    // Commonly used ground-level spots near each place.
    public static IReadOnlyList<LocationPreset> All { get; } =
    [
        new("Michael's house (Rockford Hills)", -813.6f, 179.5f, 72.2f),
        new("Franklin's house (Strawberry)", -14.4f, -1438.9f, 31.1f),
        new("Franklin's house (Vinewood Hills)", 7.9f, 548.5f, 176.0f),
        new("Trevor's trailer (Sandy Shores)", 1974.7f, 3819.5f, 33.4f),
        new("Floyd's apartment (Vespucci Beach)", -1150.7f, -1520.7f, 10.6f),
        new("Los Santos International Airport", -1037.0f, -2737.0f, 20.2f),
        new("Mount Chiliad summit", 501.8f, 5604.4f, 797.9f),
        new("Paleto Bay", -130.0f, 6300.0f, 31.3f),
        new("Sandy Shores airfield", 1747.0f, 3273.0f, 41.1f),
        new("Vinewood sign", 711.0f, 1198.0f, 348.5f),
    ];

    public override string ToString() => Name;
}

/// <summary>One character's health, armor, special ability, wanted level and position.</summary>
public sealed class StatusModel(CharacterStatus status, bool current, Action onChanged) : Observable, IDataErrorInfo
{
    private readonly Dictionary<string, string> _errors = [];
    private readonly Dictionary<string, string> _pending = [];

    public string Name => status.Name;
    public string CurrentNote => current ? "You play as this character when the save loads" : "";
    public Visibility CurrentVisibility => current ? Visibility.Visible : Visibility.Collapsed;

    public double Health { get => status.Health; set => Set(() => status.Health = (int)Math.Round(value), nameof(Health)); }
    public double Armor { get => status.Armor; set => Set(() => status.Armor = (int)Math.Round(value), nameof(Armor)); }
    public bool AbilityUnlocked { get => status.SpecialAbilityUnlocked; set => Set(() => status.SpecialAbilityUnlocked = value, nameof(AbilityUnlocked)); }
    public double Ability { get => status.SpecialAbility; set => Set(() => status.SpecialAbility = (int)Math.Round(value), nameof(Ability)); }
    public double Wanted { get => status.WantedLevel; set => Set(() => status.WantedLevel = (int)Math.Round(value), nameof(Wanted)); }
    public string WantedText => status.WantedLevel == 0 ? "None" : new string('★', status.WantedLevel);

    public string X { get => Coord(nameof(X), status.Position.X); set => SetCoord(nameof(X), value, (p, v) => p with { X = v }); }
    public string Y { get => Coord(nameof(Y), status.Position.Y); set => SetCoord(nameof(Y), value, (p, v) => p with { Y = v }); }
    public string Z { get => Coord(nameof(Z), status.Position.Z); set => SetCoord(nameof(Z), value, (p, v) => p with { Z = v }); }

    public IReadOnlyList<LocationPreset> Presets => LocationPreset.All;

    /// <summary>Picking a preset moves the character there.</summary>
    public LocationPreset? Preset
    {
        get => null;
        set
        {
            if (value == null) return;
            _errors.Clear();
            _pending.Clear();
            Set(() => status.Position = (value.X, value.Y, value.Z), "");
        }
    }

    public void MaxAll()
    {
        Health = 100;
        Armor = 100;
        if (AbilityUnlocked) Ability = 100;
    }

    private void Set(Action write, string property)
    {
        write();
        onChanged();
        if (property.Length > 0) Raise(property, nameof(WantedText)); else Refresh();
    }

    private string Coord(string name, float v) =>
        _pending.TryGetValue(name, out var p) ? p : v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    private void SetCoord(string name, string text, Func<(float X, float Y, float Z), float, (float X, float Y, float Z)> with)
    {
        _errors.Remove(name);
        _pending.Remove(name);
        if (!float.TryParse(text.Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v)
            || v is < -10000 or > 10000)
            (_errors[name], _pending[name]) = ("Enter a number between -10,000 and 10,000.", text);
        else if (v != (name == nameof(X) ? status.Position.X : name == nameof(Y) ? status.Position.Y : status.Position.Z))
        {
            status.Position = with(status.Position, v);
            onChanged();
        }
        Raise(name);
    }

    public string Error => string.Join(" ", _errors.Values);
    public string this[string column] => _errors.GetValueOrDefault(column, "");
}

/// <summary>The in-game clock as text and a date.</summary>
public sealed class ClockModel(GameClock clock, Action onChanged) : Observable, IDataErrorInfo
{
    private string? _timeError, _pendingTime;

    public string Time
    {
        get => _pendingTime ?? $"{clock.Hour:00}:{clock.Minute:00}";
        set
        {
            _timeError = _pendingTime = null;
            var parts = value.Trim().Split(':');
            if (parts.Length != 2 || !int.TryParse(parts[0], out var h) || !int.TryParse(parts[1], out var m) || h is < 0 or > 23 || m is < 0 or > 59)
                (_timeError, _pendingTime) = ("Enter a time like 13:30.", value);
            else if (h != clock.Hour || m != clock.Minute)
            {
                clock.SetTime(h, m);
                onChanged();
            }
            Raise(nameof(Time));
        }
    }

    public DateTime? Date
    {
        get => clock.Year is >= 1900 and <= 2100 && clock.Month is >= 0 and <= 11 && clock.Day >= 1
               && clock.Day <= DateTime.DaysInMonth(clock.Year, clock.Month + 1)
            ? new DateTime(clock.Year, clock.Month + 1, clock.Day) : null;
        set
        {
            if (value is not { } d || d == Date) return;
            clock.SetDate(d.Year, d.Month - 1, d.Day);
            onChanged();
            Raise(nameof(Date));
        }
    }

    public string Error => _timeError ?? "";
    public string this[string column] => column == nameof(Time) ? _timeError ?? "" : "";
}

/// <summary>One garage parking space on the Vehicles tab.</summary>
public sealed class GarageSlotModel(GarageSlot slot, Action onChanged) : Observable
{
    public GarageSlot Slot => slot;
    public string Space => $"Space {slot.Number}";
    public string VehicleName => slot.VehicleName;
    public bool CanPlace => slot.CanPlace;
    public bool CanEmpty => !slot.IsEmpty;
    public IReadOnlyList<VehicleModel> Vehicles => VehicleCatalog.RoadVehicles;
    public IReadOnlyList<VehicleColour> Colours => VehicleColour.All;
    public VehicleModel? Vehicle { get; set; } = VehicleCatalog.Find(slot.ModelHash) ?? VehicleCatalog.RoadVehicles[0];
    public VehicleColour Colour { get; set; } = VehicleColour.All[0];

    public void Park()
    {
        if (Vehicle == null) return;
        slot.Place(Vehicle, Colour);
        onChanged();
        Refresh();
    }

    public void Empty()
    {
        slot.Clear();
        onChanged();
        Refresh();
    }
}

public sealed record GarageGroup(string Name, string Owner, IReadOnlyList<GarageSlotModel> Slots);

/// <summary>A hangar, helipad or boat dock slot on the Vehicles tab.</summary>
public sealed class PropertyVehicleModel(PropertyVehicleSlot slot, Action onChanged) : Observable
{
    public string Owner => slot.Owner;
    public string Place => slot.Location;
    public string VehicleName => slot.VehicleName;
    public bool CanEmpty => !slot.IsEmpty;
    public IReadOnlyList<VehicleModel> Vehicles { get; } = VehicleCatalog.OfClass(slot.VehicleClass);
    public IReadOnlyList<VehicleColour> Colours => VehicleColour.All;
    public VehicleModel? Vehicle { get; set; }
    public VehicleColour Colour { get; set; } = VehicleColour.All[2];

    public PropertyVehicleModel Init()
    {
        Vehicle = VehicleCatalog.Find(slot.ModelHash) is { } v && Vehicles.Contains(v) ? v : Vehicles[0];
        return this;
    }

    public void Park()
    {
        if (Vehicle == null) return;
        slot.Place(Vehicle, Colour);
        onChanged();
        Refresh();
    }

    public void Empty()
    {
        slot.Clear();
        onChanged();
        Refresh();
    }
}

public sealed record PropertyVehicleGroup(string Title, IReadOnlyList<PropertyVehicleModel> Slots);

/// <summary>A business on the Properties section, with an owner picker.</summary>
public sealed class PropertyModel(Property property, Action onChanged) : Observable
{
    public static readonly string[] Owners = ["Nobody", .. StatCatalog.Characters];

    public string Name => property.Name;
    public string Kind => property.Kind;
    public IReadOnlyList<string> OwnerChoices => Owners;

    public string Owner
    {
        get => property.OwnerIndex is { } i ? StatCatalog.Characters[i] : Owners[0];
        set
        {
            int? index = Array.IndexOf(StatCatalog.Characters, value) is var i and >= 0 ? i : null;
            if (index == property.OwnerIndex) return;
            property.OwnerIndex = index;
            onChanged();
            Raise(nameof(Owner));
        }
    }
}

/// <summary>A category in the Stats page's list.</summary>
public sealed record StatCategory(string Name, int Count);

/// <summary>One row on the Stats page.</summary>
public sealed class StatRowModel(StatEntry entry, Action onChanged) : Observable, IDataErrorInfo
{
    private string? _error, _pending;

    public StatEntry Entry => entry;
    public string Label => entry.Label;
    public string Character => entry.Character == StatBrowser.Everyone ? "" : entry.Character;

    public string Detail => string.Join(" · ", new[]
    {
        entry.Min != null ? $"{entry.Min:N0} to {entry.Max:N0}" : null,
        entry.ReadOnlyNote,
    }.Where(s => s != null));

    public bool IsReadOnly => entry.IsReadOnly;
    public string? ReadOnlyNote => entry.ReadOnlyNote;
    public Visibility TextVisibility => entry.Type == StatValueType.Bool ? Visibility.Collapsed : Visibility.Visible;
    public Visibility BoolVisibility => entry.Type == StatValueType.Bool ? Visibility.Visible : Visibility.Collapsed;

    public string Text
    {
        get => _pending ?? entry.Text;
        set
        {
            _error = _pending = null;
            if (value.Trim() != entry.Text)
            {
                try
                {
                    entry.SetText(value);
                    onChanged();
                }
                catch (ArgumentException ex)
                {
                    (_error, _pending) = (ex.Message, value);
                }
            }
            Raise(nameof(Text), nameof(Error));
        }
    }

    public bool BoolValue
    {
        get => entry.Type == StatValueType.Bool && entry.BoolValue;
        set
        {
            if (entry.IsReadOnly || value == entry.BoolValue) return;
            entry.BoolValue = value;
            onChanged();
            Raise(nameof(BoolValue));
        }
    }

    public bool Matches(string search) =>
        entry.Label.Contains(search, StringComparison.OrdinalIgnoreCase) || entry.Name.Contains(search, StringComparison.OrdinalIgnoreCase);

    public string Error => _error ?? "";
    public string this[string column] => column == nameof(Text) ? _error ?? "" : "";
}

/// <summary>One Strangers and Freaks mission.</summary>
public sealed class StrangerModel(StrangersAndFreaks all, StrangerMission mission, Action onChanged) : Observable
{
    public StrangerMission Mission => mission;
    public string Name => mission.Name;
    public string Script => mission.Script;

    public bool Done
    {
        get => mission.Completed;
        set
        {
            if (value == mission.Completed) return;
            all.SetCompleted(mission, value);
            onChanged();
        }
    }
}

public sealed record StrangerGroup(string Character, IReadOnlyList<StrangerModel> Missions);

/// <summary>A section of the checklist summary: "Missions 32 / 69", worth up to 55%.</summary>
public sealed record ChecklistSummaryRow(string Title, string Count, double Fraction, string Percent);
