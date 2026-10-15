using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;

namespace SaveEditor.GtaV;

/// <summary>The sections of the game's 100% checklist, as the pause menu groups them.</summary>
public enum ChecklistGroup { Missions, HobbiesAndPastimes, StrangersAndFreaks, RandomEvents, Miscellaneous, Other }

/// <summary>One entry of the 100% checklist.</summary>
public sealed class ChecklistItem
{
    private readonly PsoNode _completed;

    internal ChecklistItem(int index, string label, int category, float weight, PsoNode completed)
    {
        Index = index;
        Label = label;
        Category = category;
        Weight = weight;
        _completed = completed;
        Name = ChecklistNames.Of(label);
    }

    /// <summary>The entry's position in the save's checklist table (unused entries included), as the game numbers it.</summary>
    public int Index { get; }

    /// <summary>The label the save stores for the entry, e.g. "M_ARM1".</summary>
    public string Label { get; }

    public string Name { get; }

    /// <summary>The entry's category number in the save; see <see cref="Checklist"/>.</summary>
    public int Category { get; }

    /// <summary>The share of the completion percentage this entry is worth (if it counts).</summary>
    public float Weight { get; }

    public ChecklistGroup Group => Category switch
    {
        1 => ChecklistGroup.Missions,
        3 or 4 => ChecklistGroup.HobbiesAndPastimes,
        7 or 8 => ChecklistGroup.StrangersAndFreaks,
        9 => ChecklistGroup.RandomEvents,
        11 or 12 or 13 or 14 => ChecklistGroup.Miscellaneous,
        _ => ChecklistGroup.Other,
    };

    /// <summary>True when the entry is needed for 100%; the others are optional or unused.</summary>
    public bool Required => Category is 1 or 3 or 7 or 9 or 11 or 13;

    public bool Completed => _completed.GetInt32() != 0;

    internal void Write(bool completed) => _completed.SetInt32(completed ? 1 : 0);
}

/// <summary>Completed and needed counts for one checklist section.</summary>
public sealed record ChecklistProgress(ChecklistGroup Group, int Completed, int Needed, float Percent, float MaxPercent);

/// <summary>
/// The game's 100% completion checklist. The script save data holds one entry per item (label, weight, completed flag,
/// category). The completion percentage and the pause-menu counters are worked out from the entries the same way the
/// game's own figures come out in real saves:
/// Missions 55% (category 1), Hobbies and Pastimes 10% (3), Strangers and Freaks 10% (7), Random Events 5% (9; any 14),
/// Miscellaneous 20% (11 and 13). Categories 0, 4, 6, 8, 12 and 14 hold optional or unused entries that do not count.
/// Ticking an entry changes the checklist and these figures only; it does not play the mission or move the story.
/// </summary>
public sealed class Checklist : ReadOnlyCollection<ChecklistItem>
{
    /// <summary>Random events needed for their 5%; only this many count.</summary>
    public const int RandomEventsNeeded = 14;

    private const float RandomEventsPercent = 5;

    // Field names in each entry struct (the 100% checklist entries all share one struct type).
    private const uint TableField = 0x1A341F71, LabelField = 0x2D4B9B3A, WeightField = 0x8EF72701,
        CompletedField = 0x11782F19, CategoryField = 0xEB5686E6;

    private readonly StatStore? _stats;

    private Checklist(IList<ChecklistItem> items, StatStore? stats) : base(items) => _stats = stats;

    /// <param name="stats">The save's stats, kept in step with the checklist; null to leave them alone.</param>
    public static Checklist Load(PsoDocument pso, StatStore? stats)
    {
        var table = pso.Walk().FirstOrDefault(n => n.NameHash == TableField && n.IsStruct && n.Field != null)
                    ?? throw new SaveFormatException("This save has no 100% checklist data.");
        var items = new List<ChecklistItem>();
        int index = -1;
        foreach (var entry in table.Children.Where(c => c.IsStruct))
        {
            index++;
            var fields = entry.Children.ToDictionary(c => c.NameHash);
            if (!fields.TryGetValue(LabelField, out var label) || !fields.TryGetValue(WeightField, out var weight)
                || !fields.TryGetValue(CompletedField, out var completed) || !fields.TryGetValue(CategoryField, out var category)
                || weight.Type != PsoType.Float || completed.Type != PsoType.SInt || category.Type != PsoType.SInt)
                throw new SaveFormatException("The 100% checklist layout in this save is not recognised.");
            var text = ReadLabel(pso.Data, label.Offset);
            if (text.Length == 0) continue; // unused entry
            items.Add(new ChecklistItem(index, text, category.GetInt32(),
                float.Parse(weight.ValueText!, System.Globalization.CultureInfo.InvariantCulture), completed));
        }
        if (items.Count == 0) throw new SaveFormatException("The 100% checklist in this save is empty.");
        return new Checklist(items, stats);
    }

    // The label is a 16-byte, zero-padded ASCII text field.
    private static string ReadLabel(byte[] data, int offset)
    {
        int end = offset;
        while (end < offset + 16 && end < data.Length && data[end] != 0) end++;
        return Encoding.ASCII.GetString(data, offset, end - offset);
    }

    /// <summary>Marks entries done or not done and updates the completion percentage and counters to match.</summary>
    public void SetCompleted(IEnumerable<ChecklistItem> items, bool completed)
    {
        bool any = false;
        foreach (var item in items)
        {
            if (!Contains(item)) throw new ArgumentException($"{item.Label} is not part of this checklist.", nameof(items));
            if (item.Completed == completed) continue;
            item.Write(completed);
            any = true;
        }
        if (any) WriteStats();
    }

    public void SetCompleted(ChecklistItem item, bool completed) => SetCompleted([item], completed);

    private int Done(int category) => this.Count(i => i.Category == category && i.Completed);
    private int Total(int category) => this.Count(i => i.Category == category);
    private float Sum(params int[] categories) => this.Where(i => categories.Contains(i.Category) && i.Completed).Sum(i => i.Weight);
    private float Max(params int[] categories) => this.Where(i => categories.Contains(i.Category)).Sum(i => i.Weight);

    private float RandomEvents => RandomEventsPercent * Math.Min(Done(9), RandomEventsNeeded) / RandomEventsNeeded;

    /// <summary>The overall completion percentage (0–100).</summary>
    public float Percentage => Math.Clamp(Sum(1) + Sum(3) + Sum(7) + RandomEvents + Sum(11, 13), 0, 100);

    /// <summary>Progress per section, counting only entries needed for 100%.</summary>
    public IReadOnlyList<ChecklistProgress> Progress =>
    [
        new(ChecklistGroup.Missions, Done(1), Total(1), Sum(1), Max(1)),
        new(ChecklistGroup.HobbiesAndPastimes, Done(3), Total(3), Sum(3), Max(3)),
        new(ChecklistGroup.StrangersAndFreaks, Done(7), Total(7), Sum(7), Max(7)),
        new(ChecklistGroup.RandomEvents, Math.Min(Done(9), RandomEventsNeeded), RandomEventsNeeded, RandomEvents, RandomEventsPercent),
        new(ChecklistGroup.Miscellaneous, Done(11) + Done(13), Total(11) + Total(13), Sum(11, 13), Max(11, 13)),
    ];

    /// <summary>The stats the game keeps for the pause menu, worked out from the checklist.</summary>
    public IReadOnlyDictionary<string, float> Figures
    {
        get
        {
            int missions = Done(1), minigames = Done(3), people = Done(7), events = Math.Min(Done(9), RandomEventsNeeded),
                misc = Done(11) + Done(13);
            static float Floor(int done, int total) => total == 0 ? 0 : (float)Math.Floor(100.0 * done / total);
            return new Dictionary<string, float>
            {
                ["TOTAL_PROGRESS_MADE"] = Percentage,
                ["NUM_MISSIONS_COMPLETED"] = missions,
                ["NUM_MINIGAMES_COMPLETED"] = minigames,
                ["NUM_RNDPEOPLE_COMPLETED"] = people,
                ["NUM_RNDEVENTS_COMPLETED"] = events,
                ["NUM_MISC_COMPLETED"] = misc,
                ["PERCENT_STORY_MISSIONS"] = Floor(missions, Total(1)),
                ["PERCENT_ODDJOBS"] = Floor(minigames, Total(3)),
                ["PERCENT_AMBIENT_MISSIONS"] = Floor(people + events, Total(7) + RandomEventsNeeded),
            };
        }
    }

    private void WriteStats()
    {
        if (_stats == null) return;
        foreach (var (name, value) in Figures)
        {
            if (_stats.Has(StatKind.Float, name)) _stats.SetFloat(name, value);
            else if (_stats.Has(StatKind.Int32, name)) _stats.SetInt(name, (int)value);
        }
    }
}

/// <summary>Display names for checklist labels (embedded ChecklistNames.txt); unknown labels show as themselves.</summary>
public static class ChecklistNames
{
    private static readonly Dictionary<string, string> Names = Load();

    public static string Of(string label) => Names.TryGetValue(label, out var n) ? n : label;

    private static Dictionary<string, string> Load()
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SaveEditor.GtaV.ChecklistNames.txt");
        if (stream == null) return names;
        using var reader = new StreamReader(stream);
        while (reader.ReadLine() is { } line)
        {
            var parts = line.Split('=', 2);
            if (parts.Length == 2 && !line.StartsWith('#')) names.TryAdd(parts[0].Trim(), parts[1].Trim());
        }
        return names;
    }
}
