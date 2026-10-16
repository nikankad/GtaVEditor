using System.Numerics;

namespace SaveEditor.GtaV;

/// <summary>
/// Whether a weapon is for sale at Ammu-Nation for one character. The gun shop script reads bit (n % 32) of the stat
/// SPx_WEAP_UNLOCK_(n / 32), where n is the weapon's unlock number.
/// </summary>
public sealed class WeaponUnlock(StatStore stats, string character, string stat, int bit, WeaponInfo weapon)
{
    public string Character => character;
    public WeaponInfo Weapon => weapon;

    public bool Unlocked
    {
        get => (stats.GetInt(stat) >> bit & 1) != 0;
        set
        {
            int v = stats.GetInt(stat), mask = 1 << bit;
            stats.SetInt(stat, value ? v | mask : v & ~mask);
        }
    }
}

public static class WeaponUnlocks
{
    // Unlock numbers from the gun shop script's weapon table (gunclub_shop, game build 2628).
    private static readonly Dictionary<string, int> Numbers = new()
    {
        ["WEAPON_PISTOL"] = 1, ["WEAPON_COMBATPISTOL"] = 2, ["WEAPON_APPISTOL"] = 3, ["WEAPON_PISTOL50"] = 4,
        ["WEAPON_SMG"] = 5, ["WEAPON_ASSAULTSMG"] = 6, ["WEAPON_MICROSMG"] = 7, ["WEAPON_ASSAULTRIFLE"] = 8,
        ["WEAPON_CARBINERIFLE"] = 9, ["WEAPON_ADVANCEDRIFLE"] = 11, ["WEAPON_MG"] = 12, ["WEAPON_COMBATMG"] = 13,
        ["WEAPON_STICKYBOMB"] = 15, ["WEAPON_GRENADE"] = 16, ["WEAPON_SMOKEGRENADE"] = 17, ["WEAPON_SNIPERRIFLE"] = 20,
        ["WEAPON_HEAVYSNIPER"] = 21, ["WEAPON_PUMPSHOTGUN"] = 22, ["WEAPON_BULLPUPSHOTGUN"] = 23, ["WEAPON_ASSAULTSHOTGUN"] = 24,
        ["WEAPON_SAWNOFFSHOTGUN"] = 25, ["WEAPON_GRENADELAUNCHER"] = 26, ["WEAPON_RPG"] = 27, ["WEAPON_MINIGUN"] = 28,
        ["WEAPON_STUNGUN"] = 31, ["GADGET_PARACHUTE"] = 33, ["WEAPON_KNIFE"] = 34, ["WEAPON_NIGHTSTICK"] = 35,
        ["WEAPON_HAMMER"] = 36, ["WEAPON_BAT"] = 37, ["WEAPON_CROWBAR"] = 38, ["WEAPON_GOLFCLUB"] = 39,
        ["WEAPON_MOLOTOV"] = 41, ["WEAPON_PETROLCAN"] = 43, ["WEAPON_BOTTLE"] = 45, ["WEAPON_SPECIALCARBINE"] = 46,
        ["WEAPON_SNSPISTOL"] = 47, ["WEAPON_BULLPUPRIFLE"] = 48, ["WEAPON_HEAVYPISTOL"] = 49, ["WEAPON_GUSENBERG"] = 50,
        ["WEAPON_DAGGER"] = 51, ["WEAPON_VINTAGEPISTOL"] = 52, ["WEAPON_MUSKET"] = 53, ["WEAPON_FIREWORK"] = 54,
        ["WEAPON_HEAVYSHOTGUN"] = 55, ["WEAPON_MARKSMANRIFLE"] = 56, ["WEAPON_HATCHET"] = 58, ["WEAPON_RAILGUN"] = 59,
        ["WEAPON_PROXMINE"] = 60, ["WEAPON_HOMINGLAUNCHER"] = 61, ["WEAPON_KNUCKLE"] = 62, ["WEAPON_MARKSMANPISTOL"] = 63,
        ["WEAPON_COMBATPDW"] = 64, ["WEAPON_MACHETE"] = 65, ["WEAPON_MACHINEPISTOL"] = 68,
    };

    /// <summary>Every catalogued weapon for every character whose unlock stat is in the save.</summary>
    public static IReadOnlyList<WeaponUnlock> Load(StatStore stats)
    {
        var list = new List<WeaponUnlock>();
        for (int c = 0; c < StatCatalog.Characters.Length; c++)
            foreach (var w in WeaponCatalog.All)
            {
                if (!Numbers.TryGetValue(w.Id, out var n)) continue;
                var stat = $"SP{c}_WEAP_UNLOCK_{n / 32}";
                if (stats.Has(StatKind.Int32, stat)) list.Add(new WeaponUnlock(stats, StatCatalog.Characters[c], stat, n % 32, w));
            }
        if (list.Count == 0) throw new SaveFormatException("This save has no weapon unlock stats.");
        return list;
    }
}

/// <summary>
/// One kind of clothing for one character (hairstyles, shoesâ€¦): the save keeps, for each group of 32 items, a bitset of
/// the items the shops offer (AVAILABLE) and of those the character owns (ACQUIRED). The items' names aren't in the
/// save, so they are handled by kind.
/// </summary>
public sealed class ClothingCategory
{
    private readonly IReadOnlyList<(PsoNode Available, PsoNode Acquired)> _groups;

    internal ClothingCategory(string label, IReadOnlyList<(PsoNode, PsoNode)> groups) => (Label, _groups) = (label, groups);

    public string Label { get; }
    public int Owned => _groups.Sum(g => BitOperations.PopCount((uint)(g.Acquired.GetInt32() & g.Available.GetInt32())));
    public int Total => _groups.Sum(g => BitOperations.PopCount((uint)g.Available.GetInt32()));

    /// <summary>Puts every item the shops offer in this kind into the wardrobe.</summary>
    public void OwnAll()
    {
        foreach (var (available, acquired) in _groups) acquired.SetInt32(acquired.GetInt32() | available.GetInt32());
    }

    /// <summary>The owned bitsets, for putting the wardrobe back as it was.</summary>
    public int[] Snapshot() => _groups.Select(g => g.Acquired.GetInt32()).ToArray();

    /// <summary>How many items a snapshot of the owned bitsets holds.</summary>
    public int OwnedIn(int[] snapshot) =>
        _groups.Select((g, i) => BitOperations.PopCount((uint)(snapshot[i] & g.Available.GetInt32()))).Sum();

    public void Restore(int[] snapshot)
    {
        for (int i = 0; i < _groups.Count && i < snapshot.Length; i++) _groups[i].Acquired.SetInt32(snapshot[i]);
    }
}

/// <summary>
/// One character's clothes. COMPONENTS_ARRAY holds, per clothing group, bitsets of items the shops offer (AVAILABLE)
/// and items the character owns (ACQUIRED); owned items appear in the wardrobe.
/// </summary>
public sealed class Wardrobe
{
    private readonly IReadOnlyList<(PsoNode Available, PsoNode Acquired)> _groups;

    // The save's group names ("TORSO3", "PROP1"â€¦) belong to the game's clothing slots; groups of one slot are one kind here.
    // Slots whose purpose isn't certain are gathered under "Other accessories".
    private static readonly (string Label, Func<string, bool> Match)[] Kinds =
    [
        ("Hairstyles", n => n.StartsWith("HAIR")), ("Beards and stubble", n => n.StartsWith("BEARD")), ("Heads", n => n.StartsWith("HEAD")),
        ("Shirts and torsos", n => n.StartsWith("TORSO")), ("Jackets and tops", n => n.StartsWith("JBIB")),
        ("Trousers and shorts", n => n.StartsWith("LEGS")), ("Shoes", n => n.StartsWith("FEET")),
        ("Hats and helmets", n => n == "PROP0"), ("Glasses", n => n == "PROP1"), ("Ear pieces", n => n == "PROP2"),
        ("Outfits", n => n.StartsWith("OUTF")),
    ];

    internal Wardrobe(string character, IReadOnlyList<(string Name, PsoNode Available, PsoNode Acquired)> groups)
    {
        Character = character;
        _groups = groups.Select(g => (g.Available, g.Acquired)).ToList();
        var categories = new List<ClothingCategory>();
        var used = new HashSet<string>();
        foreach (var (label, match) in Kinds)
        {
            var members = groups.Where(g => match(g.Name)).ToList();
            members.ForEach(m => used.Add(m.Name));
            if (members.Sum(m => BitOperations.PopCount((uint)m.Available.GetInt32())) > 0)
                categories.Add(new ClothingCategory(label, members.Select(m => (m.Available, m.Acquired)).ToList()));
        }
        var rest = groups.Where(g => !used.Contains(g.Name)).ToList();
        if (rest.Sum(m => BitOperations.PopCount((uint)m.Available.GetInt32())) > 0)
            categories.Add(new ClothingCategory("Other accessories", rest.Select(m => (m.Available, m.Acquired)).ToList()));
        Categories = categories;
    }

    public string Character { get; }

    /// <summary>The kinds of clothing the character has, each with how many items they own.</summary>
    public IReadOnlyList<ClothingCategory> Categories { get; }

    public int Owned => _groups.Sum(g => BitOperations.PopCount((uint)(g.Acquired.GetInt32() & g.Available.GetInt32())));
    public int Total => _groups.Sum(g => BitOperations.PopCount((uint)g.Available.GetInt32()));

    /// <summary>Puts every item the shops offer into the character's wardrobe.</summary>
    public void OwnAll()
    {
        foreach (var (available, acquired) in _groups) acquired.SetInt32(acquired.GetInt32() | available.GetInt32());
    }

    public static IReadOnlyList<Wardrobe> Load(PsoDocument pso)
    {
        var root = CompletionOrder.ScriptData(pso).Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash("COMPONENTS_ARRAY"))
                   ?? throw new SaveFormatException("This save has no clothing data.");
        var list = new List<Wardrobe>();
        for (int c = 0; c < StatCatalog.Characters.Length; c++)
        {
            var character = root.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash($"SP{c}"));
            if (character == null) continue;
            var groups = new List<(string, PsoNode, PsoNode)>();
            foreach (var g in character.Children.Where(n => n.IsStruct))
            {
                var available = g.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash("AVAILABLE") && n.Type == PsoType.SInt);
                var acquired = g.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash("ACQUIRED") && n.Type == PsoType.SInt);
                if (available != null && acquired != null) groups.Add((g.Name, available, acquired));
            }
            if (groups.Count > 0) list.Add(new Wardrobe(StatCatalog.Characters[c], groups));
        }
        if (list.Count == 0) throw new SaveFormatException("The clothing data in this save is not in the expected layout.");
        return list;
    }
}
/// <summary>
/// The Director Mode actor unlock bitsets. The "all unlocked" values are the ones in a real 100% save, where every
/// story, heist, special and animal actor is available.
/// </summary>
public sealed class DirectorModeUnlocks
{
    private static readonly (string Field, string Title, int All)[] Groups =
    [
        ("BitsetCharacterStoryUnlock", "Story characters", 268402687),
        ("BitsetCharacterHeistUnlock", "Heist crew", 8191),
        ("BitsetCharacterSpecialUnlock", "Special characters", 268402687),
        ("BitsetCharacterAnimalUnlock", "Animals", 2097151),
    ];

    private readonly IReadOnlyList<(PsoNode Node, string Title, int All)> _groups;

    private DirectorModeUnlocks(IReadOnlyList<(PsoNode, string, int)> groups) => _groups = groups;

    public IReadOnlyList<(string Title, int Unlocked, int Total)> Progress =>
        _groups.Select(g => (g.Title, BitOperations.PopCount((uint)(g.Node.GetInt32() & g.All)), BitOperations.PopCount((uint)g.All))).ToList();

    public bool AllUnlocked => _groups.All(g => (g.Node.GetInt32() & g.All) == g.All);

    public void UnlockAll()
    {
        foreach (var (node, _, all) in _groups) node.SetInt32(node.GetInt32() | all);
    }

    public static DirectorModeUnlocks Load(PsoDocument pso)
    {
        var dm = CompletionOrder.ScriptData(pso).Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash("DIRECTOR_SAVED_STRUCT"))
                 ?? throw new SaveFormatException("This save has no Director Mode data.");
        var groups = new List<(PsoNode, string, int)>();
        foreach (var (field, title, all) in Groups)
            groups.Add((dm.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash(field) && n.Type == PsoType.SInt)
                        ?? throw new SaveFormatException("The Director Mode data in this save is not in the expected layout."), title, all));
        return new DirectorModeUnlocks(groups);
    }
}

/// <summary>One Flight School lesson (Franklin's): the medal he has, 0 none to 3 gold.</summary>
public sealed class FlightLesson
{
    public static readonly string[] Medals = ["None", "Bronze", "Silver", "Gold"];

    private readonly PsoNode _medal, _passed;
    private readonly Checklist? _checklist;

    internal FlightLesson(string label, PsoNode medal, PsoNode passed, Checklist? checklist)
    {
        ChecklistLabel = label;
        Name = ChecklistNames.Of(label).Replace("Flight School: ", "").Replace(" (win a medal)", "");
        (_medal, _passed, _checklist) = (medal, passed, checklist);
    }

    public string Name { get; }
    public string ChecklistLabel { get; }

    public int Medal
    {
        get => Math.Clamp(_medal.GetInt32(), 0, 3);
        set
        {
            if (value is < 0 or > 3) throw new ArgumentOutOfRangeException(nameof(value), "Medal is 0 (none) to 3 (gold).");
            _medal.SetInt32(value);
            _passed.SetInt32(value > 0 ? 1 : 0);
            if (_checklist?.FirstOrDefault(i => i.Label == ChecklistLabel) is { } entry) _checklist.SetCompleted(entry, value > 0);
        }
    }
}

public static class FlightSchool
{
    private const uint PilotSchoolArray = 0x93E8FC1E;

    // Lesson field → checklist entry; the checklist's names are the in-game lesson names.
    private static readonly (string Field, string Label)[] Lessons =
    [
        ("PSC_Takeoff", "SC_MGPS1"), ("PSC_Landing", "SC_MGPS2"), ("PSC_Inverted", "SC_MGPS3"), ("PSC_Knifing", "SC_MGPS4"),
        ("PSC_loopTheLoop", "SC_MGPS5"), ("PSC_FlyLow", "SC_MGPS6"), ("PSC_DaringLanding", "SC_MGPS7"),
        ("PSC_planeCourse", "SC_MGPS8"), ("PSC_heliCourse", "SC_MGPS9"), ("PSC_heliSpeedRun", "SC_MGPS10"),
        ("PSC_parachuteOntoTarget", "SC_MGPS11"), ("PSC_chuteOntoMovingTarg", "SC_MGPS12"),
    ];

    /// <summary>Franklin's lessons (the only character who takes Flight School; his record is the second of three).</summary>
    public static IReadOnlyList<FlightLesson> Load(PsoDocument pso, Checklist? checklist)
    {
        var franklin = CompletionOrder.ScriptData(pso).Children.FirstOrDefault(n => n.NameHash == PilotSchoolArray)
                           ?.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash("structPilotSchool_p1"))
                       ?? throw new SaveFormatException("This save has no Flight School data.");
        var list = new List<FlightLesson>();
        foreach (var (field, label) in Lessons)
        {
            var lesson = franklin.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash(field));
            var medal = lesson?.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash("eMedal") && n.Type is PsoType.SInt or PsoType.Enum);
            var passed = lesson?.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash("HasPassedLesson") && n.Type == PsoType.SInt);
            if (medal == null || passed == null) throw new SaveFormatException("The Flight School data in this save is not in the expected layout.");
            list.Add(new FlightLesson(label, medal, passed, checklist));
        }
        return list;
    }
}
