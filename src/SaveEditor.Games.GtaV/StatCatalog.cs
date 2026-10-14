namespace SaveEditor.Games.GtaV;

/// <summary>A named stat the editor knows how to show. Every entry was confirmed present in a real 100% PC save.</summary>
public sealed record StatDefinition(
    string Name, string Character, string Label, StatKind Kind, long Min, long Max, bool Editable, string Unit = "",
    string Note = "");

public static class StatCatalog
{
    public static readonly string[] Characters = ["Michael", "Franklin", "Trevor"];

    // Haircut test 2026-10-14: the game's own save moved these by exactly the $180 price.
    private const string HaircutConfirmed = "Confirmed in-game: a $180 haircut changed this by exactly $180.";

    public static IReadOnlyList<StatDefinition> All { get; } = Build();

    private static List<StatDefinition> Build()
    {
        var list = new List<StatDefinition>();
        for (int c = 0; c < Characters.Length; c++)
        {
            string p = $"SP{c}_", who = Characters[c];
            list.Add(new(p + "TOTAL_CASH", who, "Cash", StatKind.Int32, 0, int.MaxValue, true, "$", HaircutConfirmed));
            list.Add(new(p + "STAMINA", who, "Stamina", StatKind.Int32, 0, 100, true, "%"));
            list.Add(new(p + "SHOOTING_ABILITY", who, "Shooting", StatKind.Int32, 0, 100, true, "%"));
            list.Add(new(p + "STRENGTH", who, "Strength", StatKind.Int32, 0, 100, true, "%"));
            list.Add(new(p + "STEALTH_ABILITY", who, "Stealth", StatKind.Int32, 0, 100, true, "%"));
            list.Add(new(p + "FLYING_ABILITY", who, "Flying", StatKind.Int32, 0, 100, true, "%"));
            list.Add(new(p + "WHEELIE_ABILITY", who, "Driving", StatKind.Int32, 0, 100, true, "%"));
            list.Add(new(p + "LUNG_CAPACITY", who, "Lung capacity", StatKind.Int32, 0, 100, true, "%"));
            list.Add(new(p + "SPECIAL_ABILITY", who, "Special ability meter", StatKind.Int32, 0, int.MaxValue, false, "",
                "Changes as the meter fills and drains; range not mapped yet."));
            list.Add(new(p + "KILLS", who, "Kills", StatKind.Int32, 0, int.MaxValue, false));
            list.Add(new(p + "DEATHS", who, "Deaths", StatKind.Int32, 0, int.MaxValue, false));
            list.Add(new(p + "BUSTED", who, "Times busted", StatKind.Int32, 0, int.MaxValue, false));
            list.Add(new(p + "NUMBER_STOLEN_CARS", who, "Cars stolen", StatKind.Int32, 0, int.MaxValue, false));
            list.Add(new(p + "TOTAL_CASH_EARNED", who, "Total cash earned", StatKind.Int32, 0, int.MaxValue, false, "$"));
            list.Add(new(p + "MONEY_TOTAL_SPENT", who, "Total money spent", StatKind.Int32, 0, int.MaxValue, false, "$", HaircutConfirmed));
            list.Add(new(p + "MONEY_SPENT_ON_HAIRDOS", who, "Spent on haircuts", StatKind.Int32, 0, int.MaxValue, false, "$", HaircutConfirmed));
            list.Add(new(p + "MONEY_SPENT_IN_CLOTHES", who, "Spent on clothes", StatKind.Int32, 0, int.MaxValue, false, "$"));
            list.Add(new(p + "MONEY_SPENT_ON_TATTOOS", who, "Spent on tattoos", StatKind.Int32, 0, int.MaxValue, false, "$"));
            list.Add(new(p + "MONEY_SPENT_IN_BUYING_GUNS", who, "Spent on guns", StatKind.Int32, 0, int.MaxValue, false, "$"));
            list.Add(new(p + "MONEY_SPENT_CAR_MODS", who, "Spent on car mods", StatKind.Int32, 0, int.MaxValue, false, "$"));
            list.Add(new(p + "MONEY_SPENT_PROPERTY", who, "Spent on property", StatKind.Int32, 0, int.MaxValue, false, "$"));
            list.Add(new(p + "MONEY_SPENT_ON_HEALTHCARE", who, "Spent on healthcare", StatKind.Int32, 0, int.MaxValue, false, "$"));
            list.Add(new(p + "MONEY_SPENT_ON_TAXIS", who, "Spent on taxis", StatKind.Int32, 0, int.MaxValue, false, "$"));
            list.Add(new(p + "TOTAL_PLAYING_TIME", who, "Playing time", StatKind.UInt64, 0, long.MaxValue, false, "ms"));
        }
        // Story progress: shown read-only until mission data is mapped (changing counters alone won't complete missions).
        list.Add(new("NUM_MISSIONS_COMPLETED", "Story", "Missions completed", StatKind.Int32, 0, 1000, false));
        list.Add(new("NUM_MISSIONS_AVAILABLE", "Story", "Missions available", StatKind.Int32, 0, 1000, false));
        list.Add(new("NUM_RNDPEOPLE_COMPLETED", "Story", "Random characters completed", StatKind.Int32, 0, 1000, false));
        list.Add(new("NUM_RNDEVENTS_COMPLETED", "Story", "Random events completed", StatKind.Int32, 0, 1000, false));
        list.Add(new("NUM_MISC_COMPLETED", "Story", "Misc. completed", StatKind.Int32, 0, 1000, false));
        list.Add(new("NUM_HIDDEN_PACKAGES_0", "Story", "Hidden packages (set 0)", StatKind.Int32, 0, 1000, false));
        list.Add(new("TOTAL_PROGRESS_MADE", "Story", "Completion", StatKind.Float, 0, 100, false, "%"));
        return list;
    }

    // Names that exist in real saves but are not worth a row in the Stats tab; used to label comparisons.
    private static readonly (string Stem, string Label)[] ExtraNames =
    [
        ("NUMBER_NEAR_MISS", "Near misses"), ("DIST_DRIVING_CAR", "Distance driven (car)"), ("DIST_RUNNING", "Distance run"),
        ("DIST_WALKING", "Distance walked"), ("DIST_SWIMMING", "Distance swum"), ("TIME_DRIVING_CAR", "Time driving (ms)"),
        ("SPECIAL_ABILITY_UNLOCKED", "Special ability unlocked"), ("MONEY_SPENT_IN_STRIP_CLUBS", "Spent in strip clubs"),
        ("MONEY_PICKED_UP_ON_STREET", "Money picked up"),
    ];

    private static readonly Dictionary<uint, (string Name, string Label)> ByHash = BuildNames();

    private static Dictionary<uint, (string, string)> BuildNames()
    {
        var names = new Dictionary<uint, (string, string)>();
        foreach (var d in All) names[GtaHash.Joaat(d.Name)] = (d.Name, d.Label);
        foreach (var (stem, label) in ExtraNames)
            for (int c = 0; c < Characters.Length; c++)
                names.TryAdd(GtaHash.Joaat($"SP{c}_{stem}"), ($"SP{c}_{stem}", label));
        return names;
    }

    public static StatDefinition? Find(uint hash) =>
        ByHash.TryGetValue(hash, out var n) ? All.FirstOrDefault(d => d.Name == n.Name) : null;

    /// <summary>The stat's name if known, otherwise null (saves store only name hashes).</summary>
    public static string? NameOf(uint hash) => ByHash.TryGetValue(hash, out var n) ? n.Name : null;

    public static string? LabelOf(uint hash) => ByHash.TryGetValue(hash, out var n) ? n.Label : null;
}
