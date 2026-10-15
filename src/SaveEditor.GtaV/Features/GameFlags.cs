namespace SaveEditor.GtaV;

/// <summary>One story flow flag, e.g. FLAG_PLAYER_VEH_F_UNLOCK_BIKE (Franklin's bike unlocked).</summary>
public sealed class GameFlag(PsoNode node)
{
    /// <summary>The game's own name, e.g. "FLAG_LETTER_SCRAPS_DONE".</summary>
    public string Id => node.Name;

    /// <summary>The name in plain words, e.g. "Letter scraps done".</summary>
    public string Name { get; } = Humanize(node.Name);

    public bool On
    {
        get => node.GetInt32() != 0;
        set => node.SetInt32(value ? 1 : 0);
    }

    private static string Humanize(string id)
    {
        var words = id["FLAG_".Length..].ToLowerInvariant().Replace('_', ' ');
        return words.Length == 0 ? id : char.ToUpperInvariant(words[0]) + words[1..];
    }
}

/// <summary>
/// The story flow's flags (FLOW_STRUCT → MF_CONTROLS_STRUCT → MF_CONTROL_FLAGIDS): one int per flag the mission flow
/// sets as the story moves on (vehicles unlocked, collectibles done, help shown, ...). Only flags whose name is known
/// are listed.
/// </summary>
public static class GameFlags
{
    public static IReadOnlyList<GameFlag> Load(PsoDocument pso)
    {
        PsoNode? Child(PsoNode? n, string name) => n?.Children.FirstOrDefault(c => c.NameHash == PsoNames.Hash(name));
        var flags = Child(Child(Child(CompletionOrder.ScriptData(pso), "FLOW_STRUCT"), "MF_CONTROLS_STRUCT"), "MF_CONTROL_FLAGIDS")
                    ?? throw new SaveFormatException("This save has no story flow flags.");
        var list = flags.Children.Where(n => n.Type == PsoType.SInt && n.Name.StartsWith("FLAG_", StringComparison.Ordinal))
            .Select(n => new GameFlag(n)).OrderBy(f => f.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) throw new SaveFormatException("The story flow flags in this save have no known names.");
        return list;
    }
}
