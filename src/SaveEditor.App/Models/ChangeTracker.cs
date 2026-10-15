using System.Windows;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>One value that differs from the save as it was opened (or last saved).</summary>
public sealed record Change(string Section, string Label, string Before, string After, Action? Undo, Func<bool> IsChanged)
{
    public bool CanUndo => Undo != null;
}

/// <summary>An editable value anywhere in the save, for the change review and the search box.</summary>
public sealed class TrackedValue
{
    private readonly Func<object?> _read;
    private readonly Action<object?>? _write;
    private readonly Func<object?, string> _show;
    private object? _original;

    internal TrackedValue(string section, string label, Func<object?> read, Action<object?>? write, Func<object?, string> show)
    {
        Section = section;
        Label = label;
        _read = read;
        _write = write;
        _show = show;
        _original = read();
    }

    /// <summary>The tab it lives on.</summary>
    public string Section { get; }
    public string Label { get; }
    public string Value => _show(_read());
    public bool Changed => !Equals(_read(), _original);

    /// <summary>For on/off values: the current state, so the search results can offer a tick box.</summary>
    public bool? Flag => _read() as bool?;
    public bool CanWrite => _write != null;

    public void Write(object? value) => _write?.Invoke(value);

    internal void Rebase() => _original = _read();

    internal Change ToChange() =>
        new(Section, Label, _show(_original), Value, _write == null ? null : () => _write(_original), () => Changed);
}

/// <summary>A search result: an editable value, with a tick box when it's on/off.</summary>
public sealed class SearchHit(TrackedValue tracked, Action changed) : Observable
{
    public TrackedValue Tracked => tracked;
    public string Section => tracked.Section;
    public string Label => tracked.Label;
    public string Value => tracked.Value;
    public Visibility ToggleVisibility => tracked.Flag != null && tracked.CanWrite ? Visibility.Visible : Visibility.Collapsed;

    public bool IsOn
    {
        get => tracked.Flag == true;
        set
        {
            if (value == IsOn || tracked.Flag == null || !tracked.CanWrite) return;
            tracked.Write(value);
            changed();
            Refresh();
        }
    }
}

/// <summary>
/// Remembers every editable value when the save is opened, so the editor can list what changed before saving and
/// put single values back. Values are read and written through the same feature classes the tabs use, so undoing one
/// also fixes up what it is linked to (a mission's checklist entry, a collectible's counter).
/// </summary>
public sealed class ChangeTracker
{
    private readonly List<TrackedValue> _values = [];

    public IReadOnlyList<TrackedValue> Values => _values;

    public void Track<T>(string section, string label, Func<T> read, Action<T>? write, Func<T, string> show) =>
        _values.Add(new TrackedValue(section, label, () => read(), write == null ? null : v => write((T)v!), v => show((T)v!)));

    /// <summary>Everything that differs from the save as opened, in tab order.</summary>
    public IReadOnlyList<Change> Changes() => _values.Where(v => v.Changed).Select(v => v.ToChange()).ToList();

    /// <summary>After saving: the saved values become the new starting point.</summary>
    public void Rebase() => _values.ForEach(v => v.Rebase());

    /// <summary>Tracks every editable value in <paramref name="doc"/>; parts the save can't read are skipped.</summary>
    public static ChangeTracker For(GtaVSaveDocument doc)
    {
        var t = new ChangeTracker();
        var skipStats = new HashSet<string>();
        static string DoneText(bool b) => b ? "Done" : "Not done";

        Try(() =>
        {
            var checklist = doc.Checklist;
            foreach (var i in checklist)
                t.Track("Checklist", i.Name, () => i.Completed, v => checklist.SetCompleted([i], v), DoneText);
        });
        Try(() =>
        {
            var missions = doc.Missions;
            foreach (var m in missions)
                t.Track("Missions", m.Name, () => m.Completed, v => missions.SetCompleted([m], v), DoneText);
        });
        Try(() =>
        {
            var strangers = doc.Strangers;
            foreach (var m in strangers)
                t.Track("Strangers & Freaks", $"{m.Character}: {m.Name}", () => m.Completed, v => strangers.SetCompleted([m], v), DoneText);
        });
        Try(() =>
        {
            foreach (var set in doc.Collectibles)
            {
                if (set.CountOnly)
                    t.Track("Collectibles", set.Name, () => set.Collected, set.SetCount, v => $"{v} of {set.Total}");
                else
                    foreach (var item in set.Items)
                        t.Track("Collectibles", $"{set.Name} No. {item.Number}", () => item.Collected, v => item.Collected = v,
                            v => v ? "Found" : "Not found");
            }
        });
        Try(() =>
        {
            foreach (var name in StatCatalog.Characters)
                foreach (var d in StatCatalog.All.Where(d => d.Character == name && d.Editable && doc.Stats.Has(d.Kind, d.Name)
                                                             && (d.Unit == "%" || d.Name.EndsWith("_TOTAL_CASH"))))
                {
                    skipStats.Add(d.Name);
                    bool cash = d.Unit == "$";
                    t.Track("Player", $"{name}: {(cash ? "Cash" : d.Label)}", () => doc.GetStat(d), v => doc.SetStat(d, v),
                        v => cash || d.Name.EndsWith("_TOTAL_CASH") ? $"${v:N0}" : $"{v}%");
                }
        });
        Try(() =>
        {
            var player = doc.Player;
            foreach (var c in player.Characters)
            {
                t.Track("Player", $"{c.Name}: Health", () => c.Health, v => c.Health = v, v => $"{v}%");
                t.Track("Player", $"{c.Name}: Armor", () => c.Armor, v => c.Armor = v, v => $"{v}%");
                t.Track("Player", $"{c.Name}: Special ability unlocked", () => c.SpecialAbilityUnlocked, v => c.SpecialAbilityUnlocked = v,
                    v => v ? "Yes" : "No");
                t.Track("Player", $"{c.Name}: Ability bar", () => c.SpecialAbility, v => c.SpecialAbility = v, v => $"{v}%");
                t.Track("Player", $"{c.Name}: Wanted level", () => c.WantedLevel, v => c.WantedLevel = v,
                    v => v == 0 ? "None" : new string('★', v));
                t.Track("Player", $"{c.Name}: Location", () => c.Position, v => c.Position = v,
                    v => $"Near {MapLabel.Nearest(v.X, v.Y).Text} ({v.X:0}, {v.Y:0}, {v.Z:0})");
            }
            if (player.Clock is { } clock)
                t.Track("Player", "Game date and time", () => (clock.Year, clock.Month, clock.Day, clock.Hour, clock.Minute),
                    v => { clock.SetDate(v.Year, v.Month, v.Day); clock.SetTime(v.Hour, v.Minute); },
                    v => $"{v.Day}/{v.Month}/{v.Year} {v.Hour:00}:{v.Minute:00}");
        });
        // Vehicles can only be put back when the space was empty; parking a vehicle needs its colour too.
        Try(() =>
        {
            foreach (var s in doc.Garages)
            {
                bool wasEmpty = s.IsEmpty;
                t.Track("Vehicles", $"{s.Garage}, space {s.Number}", () => s.VehicleName, wasEmpty ? _ => s.Clear() : null, v => v);
            }
        });
        Try(() =>
        {
            foreach (var s in doc.PropertyVehicles)
            {
                bool wasEmpty = s.IsEmpty;
                t.Track("Vehicles", $"{s.Location} ({s.Owner})", () => s.VehicleName, wasEmpty ? _ => s.Clear() : null, v => v);
            }
        });
        Try(() =>
        {
            foreach (var p in doc.Properties)
                t.Track("Vehicles", $"Business: {p.Name}", () => p.OwnerIndex, v => p.OwnerIndex = v,
                    v => v is { } i ? StatCatalog.Characters[i] : "Not owned");
        });
        Try(() =>
        {
            var crew = doc.Crew;
            t.Track("Missions", "Heist crew", () => (crew.Unlocked, crew.Dead), null,
                v => $"{v.Unlocked} unlocked, {v.Dead} dead");
        });
        Try(() =>
        {
            foreach (var w in doc.Weapons)
                t.Track("Weapons", $"{w.Character}: {w.Weapon.Label}", () => (w.Owned, Ammo: w.Owned ? w.Ammo : 0),
                    v =>
                    {
                        if (!v.Owned) { w.Remove(); return; }
                        w.Give();
                        if (w.Weapon.UsesAmmo) w.Ammo = v.Ammo;
                    },
                    v => !v.Owned ? "Not owned" : w.Weapon.UsesAmmo ? $"Owned, {v.Ammo:N0} ammo" : "Owned");
        });
        Try(() =>
        {
            foreach (var u in doc.WeaponUnlocks)
                t.Track("Unlocks", $"{u.Character}: {u.Weapon.Label} at Ammu-Nation", () => u.Unlocked, v => u.Unlocked = v,
                    v => v ? "For sale" : "Locked");
        });
        Try(() =>
        {
            foreach (var w in doc.Wardrobes)
                t.Track("Unlocks", $"{w.Character}: clothes", () => w.Owned, null, v => $"{v} of {w.Total} owned");
        });
        Try(() =>
        {
            var director = doc.DirectorMode;
            t.Track("Unlocks", "Director Mode actors", () => director.Progress.Sum(p => p.Unlocked), null,
                v => $"{v} of {director.Progress.Sum(p => p.Total)} unlocked");
        });
        Try(() =>
        {
            foreach (var l in doc.FlightSchool)
                t.Track("Unlocks", $"Flight School: {l.Name}", () => l.Medal, v => l.Medal = v, v => FlightLesson.Medals[v]);
        });
        Try(() =>
        {
            foreach (var f in doc.Flags)
                t.Track("Game flags", f.Name, () => f.On, v => f.On = v, v => v ? "On" : "Off");
        });
        Try(() =>
        {
            foreach (var s in doc.StatList.Where(s => !skipStats.Contains(s.Name)))
                t.Track("Stats", string.IsNullOrEmpty(s.Character) ? s.Label : $"{s.Character}: {s.Label}", () => s.Text,
                    s.IsReadOnly ? null : s.SetText, v => v);
        });
        return t;
    }

    private static void Try(Action track)
    {
        try { track(); }
        catch (SaveFormatException) { }
    }
}
