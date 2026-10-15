using System.Globalization;
using System.Reflection;

namespace SaveEditor.GtaV;

public enum StatValueType { Int, Bool, Float, UInt64 }

/// <summary>One named stat present in the save, for the Stats page.</summary>
public sealed class StatEntry
{
    private readonly StatStore _stats;

    internal StatEntry(StatStore stats, string name, string category, string character, string label, StatValueType type,
        long? min, long? max, string? readOnlyNote)
    {
        _stats = stats;
        Name = name;
        Category = category;
        Character = character;
        Label = label;
        Type = type;
        Min = min;
        Max = max;
        ReadOnlyNote = readOnlyNote;
    }

    /// <summary>The stat's full name as the game stores it, e.g. SP0_KILLS.</summary>
    public string Name { get; }

    public string Category { get; }

    /// <summary>Michael, Franklin, Trevor, or "Everyone" for stats shared by all three.</summary>
    public string Character { get; }

    public string Label { get; }
    public StatValueType Type { get; }
    public long? Min { get; }
    public long? Max { get; }

    /// <summary>Why the stat can't be edited here, or null when it can.</summary>
    public string? ReadOnlyNote { get; }

    public bool IsReadOnly => ReadOnlyNote != null;

    public string Text => Type switch
    {
        StatValueType.Int or StatValueType.Bool => _stats.GetInt(Name).ToString(CultureInfo.InvariantCulture),
        StatValueType.Float => _stats.GetFloat(Name).ToString("0.###", CultureInfo.InvariantCulture),
        _ => _stats.GetUInt64(Name).ToString(CultureInfo.InvariantCulture),
    };

    public bool BoolValue
    {
        get => _stats.GetInt(Name) != 0;
        set => SetText(value ? "1" : "0");
    }

    /// <summary>Parses and writes a new value; throws <see cref="ArgumentException"/> with a readable message when it's invalid.</summary>
    public void SetText(string text)
    {
        if (IsReadOnly) throw new InvalidOperationException(ReadOnlyNote);
        text = text.Trim().Replace(",", "");
        switch (Type)
        {
            case StatValueType.Bool:
                _stats.SetInt(Name, text is "1" or "true" or "True" ? 1 : text is "0" or "false" or "False" ? 0
                    : throw new ArgumentException("Enter 0 or 1."));
                break;
            case StatValueType.Int:
                if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i)
                    || (Min is { } lo && i < lo) || (Max is { } hi && i > hi))
                    throw new ArgumentException(Min != null ? $"Enter a whole number from {Min:N0} to {Max:N0}." : "Enter a whole number.");
                _stats.SetInt(Name, i);
                break;
            case StatValueType.Float:
                if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) || !float.IsFinite(f))
                    throw new ArgumentException("Enter a number.");
                _stats.SetFloat(Name, f);
                break;
            default:
                if (!ulong.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var u))
                    throw new ArgumentException("Enter a whole number of 0 or more.");
                _stats.SetUInt64(Name, u);
                break;
        }
    }
}

/// <summary>
/// The stats the Stats page lists: every name in the embedded StatNames.txt that this save actually has, with each
/// per-character name expanded for Michael (SP0_), Franklin (SP1_) and Trevor (SP2_).
/// </summary>
public static class StatBrowser
{
    public const string Everyone = "Everyone";

    private static readonly Lazy<List<(string Category, string Name, string Type, bool PerCharacter, long? Min, long? Max, string Label)>> Names =
        new(LoadNames);

    public static IReadOnlyList<StatEntry> Load(StatStore stats)
    {
        var list = new List<StatEntry>();
        var seen = new HashSet<string>();
        foreach (var n in Names.Value)
        {
            var names = n.PerCharacter
                ? StatCatalog.Characters.Select((c, i) => (Full: $"SP{i}_{n.Name}", Who: c))
                : [(Full: n.Name, Who: Everyone)];
            foreach (var (full, who) in names)
            {
                if (!seen.Add(full)) continue;
                StatValueType? type =
                    stats.Has(StatKind.Int32, full) ? (n.Type == "bool" ? StatValueType.Bool : StatValueType.Int)
                    : stats.Has(StatKind.Float, full) ? StatValueType.Float
                    : stats.Has(StatKind.UInt64, full) ? StatValueType.UInt64
                    : null;
                if (type == null) continue;
                list.Add(new StatEntry(stats, full, n.Category, who, n.Label, type.Value, n.Min, n.Max, ReadOnlyNote(n.Name)));
            }
        }
        return list;
    }

    // Stats another page keeps consistent with the rest of the save; editing them alone would desync it.
    private static string? ReadOnlyNote(string name) =>
        name.StartsWith("FL_CO_") || name is "NUM_MISSIONS_COMPLETED" or "NUM_RNDPEOPLE_COMPLETED"
            ? "Set by the Missions and Strangers & Freaks tabs."
            : name.StartsWith("PERCENT_") || name is "TOTAL_PROGRESS_MADE"
                ? "Recomputed by the Checklist tab."
                : name.StartsWith("NUM_HIDDEN_PACKAGES_") && name != "NUM_HIDDEN_PACKAGES_7"
                  || name is "SP_UNDER_THE_BRIDGE_COUNT" or "SP_KNIFE_FLIGHTS_COUNT"
                    ? "Kept in step by the Collectibles tab."
                    : null;

    /// <summary>Every stat name the Stats page knows, for tests.</summary>
    public static IEnumerable<(string Name, string Label)> AllNames => Names.Value.Select(n => (n.Name, n.Label));

    private static List<(string, string, string, bool, long?, long?, string)> LoadNames()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SaveEditor.GtaV.StatNames.txt")
            ?? throw new InvalidOperationException("StatNames.txt resource missing.");
        using var reader = new StreamReader(stream);
        var list = new List<(string, string, string, bool, long?, long?, string)>();
        for (string? line; (line = reader.ReadLine()) != null;)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split('|');
            if (p.Length < 7 || p[6].Length == 0) continue;
            list.Add((p[0], p[1], p[2], p[3] == "1", long.TryParse(p[4], out var lo) ? lo : null,
                long.TryParse(p[5], out var hi) ? hi : null, p[6]));
        }
        return list;
    }
}
