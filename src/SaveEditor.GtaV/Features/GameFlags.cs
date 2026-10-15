using System.Reflection;

namespace SaveEditor.GtaV;

/// <summary>One story flow flag, e.g. FLAG_PLAYER_VEH_F_UNLOCK_BIKE (Franklin's bike unlocked).</summary>
public sealed class GameFlag(PsoNode node, string group, string name)
{
    /// <summary>The game's own name, e.g. "FLAG_LETTER_SCRAPS_DONE".</summary>
    public string Id => node.Name;

    /// <summary>Unlocks, Story, Heists, Collectibles, Epsilon Program or Messages.</summary>
    public string Group { get; } = group;

    /// <summary>The flag in plain words, e.g. "All letter scraps found".</summary>
    public string Name { get; } = name;

    public bool On
    {
        get => node.GetInt32() != 0;
        set => node.SetInt32(value ? 1 : 0);
    }
}

/// <summary>
/// The story flow's flags (FLOW_STRUCT → MF_CONTROLS_STRUCT → MF_CONTROL_FLAGIDS): one int per flag the mission flow
/// sets as the story moves on (vehicles unlocked, collectibles done, texts sent, ...). Only flags with a label in
/// Data/FlagLabels.txt are listed; the rest are heist-board bookkeeping or have no known name.
/// </summary>
public static class GameFlags
{
    public static readonly string[] Groups = ["Unlocks", "Story", "Heists", "Collectibles", "Epsilon Program", "Messages"];

    private static readonly Lazy<Dictionary<string, (string Group, string Label)>> Labels = new(LoadLabels);

    /// <summary>Every labelled flag name, for tests.</summary>
    public static IReadOnlyDictionary<string, (string Group, string Label)> AllLabels => Labels.Value;

    public static IReadOnlyList<GameFlag> Load(PsoDocument pso)
    {
        PsoNode? Child(PsoNode? n, string name) => n?.Children.FirstOrDefault(c => c.NameHash == PsoNames.Hash(name));
        var flags = Child(Child(Child(CompletionOrder.ScriptData(pso), "FLOW_STRUCT"), "MF_CONTROLS_STRUCT"), "MF_CONTROL_FLAGIDS")
                    ?? throw new SaveFormatException("This save has no story flow flags.");
        var list = flags.Children
            .Where(n => n.Type == PsoType.SInt && Labels.Value.ContainsKey(n.Name))
            .Select(n => new GameFlag(n, Labels.Value[n.Name].Group, Labels.Value[n.Name].Label))
            .OrderBy(f => Array.IndexOf(Groups, f.Group)).ThenBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) throw new SaveFormatException("The story flow flags in this save have no known names.");
        return list;
    }

    private static Dictionary<string, (string, string)> LoadLabels()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SaveEditor.GtaV.FlagLabels.txt")
            ?? throw new InvalidOperationException("FlagLabels.txt resource missing.");
        using var reader = new StreamReader(stream);
        var labels = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        for (string? line; (line = reader.ReadLine()) != null;)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var p = line.Split('|');
            if (p.Length == 3) labels[p[0]] = (p[1], p[2]);
        }
        return labels;
    }
}
