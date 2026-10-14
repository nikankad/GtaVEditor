using System.ComponentModel;
using System.Windows;
using SaveEditor.Games.GtaV;

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

/// <summary>A section of the checklist summary: "Missions 32 / 69", worth up to 55%.</summary>
public sealed record ChecklistSummaryRow(string Title, string Count, double Fraction, string Percent);
