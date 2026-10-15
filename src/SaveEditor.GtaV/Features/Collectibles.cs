using System.Collections.ObjectModel;
using System.Globalization;
using System.Reflection;

namespace SaveEditor.GtaV;

public enum CollectibleKind
{
    LetterScraps, SpaceshipParts, EpsilonTracts, NuclearWaste, SubmarinePieces, UnderTheBridge, KnifeFlights,
    ForSaleSigns, MonkeyMosaics, Peyote, ParachuteJumps, StuntJumps,
}

/// <summary>One collectible (a letter scrap, a bridge, ...). Ticking it marks it found in the save.</summary>
public sealed class CollectibleItem
{
    private readonly CollectibleSet _set;
    private readonly int _bit;

    internal CollectibleItem(CollectibleSet set, int number, int bit, (float X, float Y, float Z)? location)
    {
        _set = set;
        Number = number;
        _bit = bit;
        Location = location;
    }

    /// <summary>1-based, in the game's own order.</summary>
    public int Number { get; }

    /// <summary>Where it is in the world (game metres), when known.</summary>
    public (float X, float Y, float Z)? Location { get; }

    public bool Collected
    {
        get => _set.Bits.Get(_bit);
        set => _set.SetCollected([this], value);
    }

    internal void Write(bool on) => _set.Bits.Set(_bit, on);
}

/// <summary>
/// One kind of collectible with its found/not-found state per item. Changing items also updates the kind's count stat
/// and checklist entry, as the game does when you pick one up. Stunt jumps only have a count (<see cref="CountOnly"/>).
/// </summary>
public sealed class CollectibleSet
{
    private readonly StatStore _stats;
    private readonly Checklist? _checklist;
    private readonly PsoNode? _countNode;

    internal CollectibleSet(CollectibleKind kind, string name, IBits bits, IReadOnlyList<int> itemBits, StatStore stats,
        Checklist? checklist, Info info, PsoNode? countNode = null, int countOnlyTotal = 0)
    {
        Kind = kind;
        Name = name;
        Bits = bits;
        _stats = stats;
        _checklist = checklist;
        _countNode = countNode;
        CountStat = info.CountStat;
        ChecklistLabel = info.Checklist;
        ChecklistNeeded = info.Needed;
        var locations = CollectibleLocations.Of(kind);
        Items = itemBits.Select((b, i) => new CollectibleItem(this, i + 1, b, locations.TryGetValue(b, out var l) ? l : null)).ToList();
        Total = countNode != null ? countOnlyTotal : Items.Count;
    }

    internal sealed record Info(string? CountStat = null, string? Checklist = null, int Needed = 0);

    public CollectibleKind Kind { get; }
    public string Name { get; }
    public IReadOnlyList<CollectibleItem> Items { get; }
    public int Total { get; }

    /// <summary>True when the save only keeps how many were done, not which ones.</summary>
    public bool CountOnly => _countNode != null;

    public int Collected => CountOnly ? _countNode!.GetInt32() : Items.Count(i => i.Collected);

    public string? CountStat { get; }
    public string? ChecklistLabel { get; }
    public int ChecklistNeeded { get; }

    internal IBits Bits { get; }

    public void SetCollected(IEnumerable<CollectibleItem> items, bool collected)
    {
        foreach (var item in items) item.Write(collected);
        Sync();
    }

    public void SetAll(bool collected)
    {
        if (CountOnly) SetCount(collected ? Total : 0);
        else SetCollected(Items, collected);
    }

    /// <summary>For <see cref="CountOnly"/> kinds: how many are done.</summary>
    public void SetCount(int count)
    {
        if (!CountOnly) throw new InvalidOperationException($"{Name} are set one by one.");
        if (count < 0 || count > Total) throw new ArgumentOutOfRangeException(nameof(count), $"{Name}: between 0 and {Total}.");
        _countNode!.SetInt32(count);
        Sync();
    }

    private void Sync()
    {
        int n = Collected;
        if (CountStat != null && _stats.Has(StatKind.Int32, CountStat)) _stats.SetInt(CountStat, n);
        if (_checklist?.FirstOrDefault(i => i.Label == ChecklistLabel) is { } entry && entry.Completed != n >= ChecklistNeeded)
            _checklist.SetCompleted(entry, n >= ChecklistNeeded);
    }
}

internal interface IBits
{
    bool Get(int bit);
    void Set(int bit, bool on);
}

/// <summary>Bits spread over consecutive 32-bit save fields, bit 0 = lowest bit of the first field.</summary>
internal sealed class NodeBits(IReadOnlyList<PsoNode> ints) : IBits
{
    public bool Get(int bit) => (ints[bit / 32].GetInt32() >> (bit % 32) & 1) != 0;

    public void Set(int bit, bool on)
    {
        var n = ints[bit / 32];
        int mask = 1 << (bit % 32), v = n.GetInt32();
        n.SetInt32(on ? v | mask : v & ~mask);
    }
}

/// <summary>
/// The game's single-player "packed stat" bools: index i is bit (i - 705) of the 64-bit stats SP_PSTAT_BOOL0..8,
/// lowest bit first (docs/FINDINGS.md section 19).
/// </summary>
internal sealed class PackedStatBools(StatStore stats) : IBits
{
    public const int First = 705;

    private static string Stat(int index) => $"SP_PSTAT_BOOL{(index - First) / 64}";

    public static bool Available(StatStore stats, int index) => stats.Has(StatKind.UInt64, Stat(index));

    public bool Get(int index) => (stats.GetUInt64(Stat(index)) >> ((index - First) % 64) & 1) != 0;

    public void Set(int index, bool on)
    {
        ulong mask = 1UL << ((index - First) % 64), v = stats.GetUInt64(Stat(index));
        stats.SetUInt64(Stat(index), on ? v | mask : v & ~mask);
    }
}

/// <summary>
/// Every collectible kind the save tracks. Where each is stored was worked out from the game's collectible scripts and
/// checked against real saves (docs/FINDINGS.md section 19). Kinds whose data is missing from a save are left out.
/// </summary>
public sealed class Collectibles : ReadOnlyCollection<CollectibleSet>
{
    // Bits of BRIDGES_FLOWN_UNDER_FLAGS* the flyunderbridges script marks as knife flights; 2, 8, 37, 65 and 66 are unused.
    private static readonly int[] KnifeBits = [33, 34, 35, 36, 50, 51, 52, 53, 54, 55, 56, 57, 58, 59, 60];
    private static readonly int[] UnusedBridgeBits = [2, 8, 37, 65, 66];
    private const int BridgeBits = 70;

    private const uint FirstBridgeField = 0xF144B45F, BaseJumpsDoneField = 0x6EC087BF;

    // First packed-stat index of each kind stored as packed stat bools (from the collectible scripts).
    internal static readonly Dictionary<CollectibleKind, int> PackedFirst = new()
    {
        [CollectibleKind.LetterScraps] = 705, [CollectibleKind.SpaceshipParts] = 755, [CollectibleKind.EpsilonTracts] = 805,
        [CollectibleKind.NuclearWaste] = 815, [CollectibleKind.SubmarinePieces] = 845,
    };

    private Collectibles(IList<CollectibleSet> sets) : base(sets) { }

    public static Collectibles Load(PsoDocument pso, StatStore stats, Checklist? checklist)
    {
        var data = CompletionOrder.ScriptData(pso);
        PsoNode? Child(PsoNode? parent, string name) => parent?.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash(name));
        PsoNode? Int(PsoNode? n) => n?.Type == PsoType.SInt ? n : null;
        var sets = new List<CollectibleSet>();

        void Packed(CollectibleKind kind, string name, int count, CollectibleSet.Info info)
        {
            int first = PackedFirst[kind];
            if (!PackedStatBools.Available(stats, first) || !PackedStatBools.Available(stats, first + count - 1)) return;
            sets.Add(new CollectibleSet(kind, name, new PackedStatBools(stats), Enumerable.Range(first, count).ToList(), stats, checklist, info));
        }

        void Flags(CollectibleKind kind, string name, PsoNode?[] fields, IEnumerable<int> bits, CollectibleSet.Info info)
        {
            if (fields.Any(f => f == null)) return;
            sets.Add(new CollectibleSet(kind, name, new NodeBits(fields!), bits.ToList(), stats, checklist, info));
        }

        Packed(CollectibleKind.LetterScraps, "Letter Scraps", 50, new("NUM_HIDDEN_PACKAGES_0", "SC_CO_DLS1", 50));
        Packed(CollectibleKind.SpaceshipParts, "Spaceship Parts", 50, new("NUM_HIDDEN_PACKAGES_1", "SC_CO_UFOP1", 50));
        Packed(CollectibleKind.EpsilonTracts, "Epsilon Tracts", 10, new("NUM_HIDDEN_PACKAGES_2"));
        Packed(CollectibleKind.NuclearWaste, "Nuclear Waste", 30, new("NUM_HIDDEN_PACKAGES_3", "SC_MSC_SON", 30));
        Packed(CollectibleKind.SubmarinePieces, "Submarine Pieces", 30, new("NUM_HIDDEN_PACKAGES_4", "SC_MSC_DIV", 30));

        // The game recounts bridges and knife flights from these bits whenever the save loads.
        var firstBridge = data.Children.FirstOrDefault(n => n.NameHash == FirstBridgeField && n.Type == PsoType.SInt);
        PsoNode?[] bridges = [firstBridge, Int(Child(data, "BRIDGES_FLOWN_UNDER_FLAGS_2")), Int(Child(data, "BRIDGES_FLOWN_UNDER_FLAGS_3"))];
        Flags(CollectibleKind.UnderTheBridge, "Under the Bridge", bridges,
            Enumerable.Range(0, BridgeBits).Except(KnifeBits).Except(UnusedBridgeBits), new("SP_UNDER_THE_BRIDGE_COUNT", "SC_MSC_BRG", 25));
        Flags(CollectibleKind.KnifeFlights, "Knife Flights", bridges, KnifeBits, new("SP_KNIFE_FLIGHTS_COUNT", "SC_MSC_KFT", 8));

        Flags(CollectibleKind.ForSaleSigns, "For Sale Signs", [Int(Child(data, "FOR_SALE_SIGN_DESTROYED_FLAGS"))],
            Enumerable.Range(0, 15), new());
        var monkeys = Child(data, "PHOTOGRAPHY_MONKEY_FLAGS");
        Flags(CollectibleKind.MonkeyMosaics, "Monkey Mosaics",
            [Int(Child(monkeys, "PHOTOGRAPHY_MONKEY_FLAGS_1")), Int(Child(monkeys, "PHOTOGRAPHY_MONKEY_FLAGS_2"))],
            Enumerable.Range(0, 50), new("NUM_HIDDEN_PACKAGES_6"));

        // Peyote plants: found bits per plant type (land 15, then 6 and 6; the fourth type has none in story mode).
        var peyote = Child(data, "PEYOTE_PICKUP_OF_TYPE_FOUND")?.Children.Where(n => n.Type == PsoType.SInt).ToList();
        if (peyote is { Count: >= 3 })
            Flags(CollectibleKind.Peyote, "Peyote Plants", [.. peyote],
                Enumerable.Range(0, 15).Concat(Enumerable.Range(32, 6)).Concat(Enumerable.Range(64, 6)), new("NUM_HIDDEN_PACKAGES_5"));

        var baseJumps = Child(data, "BASEJUMP_SAVED_STRUCT")?.Children.FirstOrDefault(n => n.NameHash == BaseJumpsDoneField && n.Type == PsoType.SInt);
        Flags(CollectibleKind.ParachuteJumps, "Parachute Jumps", [baseJumps], Enumerable.Range(0, 13), new(null, "SC_MG_BJUM", 13));

        if (Int(Child(Child(data, "STUNT_JUMPS_SAVED_STRUCT"), "STUNT_JUMPS_iStuntJumpsCompleted")) is { } stunts)
            sets.Add(new CollectibleSet(CollectibleKind.StuntJumps, "Stunt Jumps", new NodeBits([stunts]), [], stats, checklist,
                new(null, "SC_MSC_STJ", 25), stunts, 50));

        if (sets.Count == 0) throw new SaveFormatException("This save has no collectible data.");
        return new Collectibles(sets);
    }
}

/// <summary>Positions from the embedded CollectibleLocations.txt, keyed by kind and storage bit.</summary>
internal static class CollectibleLocations
{
    private static readonly Dictionary<CollectibleKind, Dictionary<int, (float, float, float)>> All = Load();

    public static IReadOnlyDictionary<int, (float X, float Y, float Z)> Of(CollectibleKind kind) =>
        All.TryGetValue(kind, out var d) ? d : new Dictionary<int, (float, float, float)>();

    private static Dictionary<CollectibleKind, Dictionary<int, (float, float, float)>> Load()
    {
        var result = new Dictionary<CollectibleKind, Dictionary<int, (float, float, float)>>();
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SaveEditor.GtaV.CollectibleLocations.txt");
        if (stream == null) return result;
        using var reader = new StreamReader(stream);
        var inv = CultureInfo.InvariantCulture;
        while (reader.ReadLine() is { } line)
        {
            var p = line.Split(',');
            if (line.StartsWith('#') || p.Length != 5 || !Enum.TryParse<CollectibleKind>(p[0], out var kind)) continue;
            int index = int.Parse(p[1], inv);
            // Packed-stat kinds are keyed by stat index; their file indexes are 0-based item numbers.
            int key = Collectibles.PackedFirst.GetValueOrDefault(kind) + index;
            var at = (X: float.Parse(p[2], inv), Y: float.Parse(p[3], inv), Z: float.Parse(p[4], inv));
            if (at is { X: 0, Y: 0, Z: 0 }) continue; // the script's placeholder: position not known
            if (!result.TryGetValue(kind, out var d)) result[kind] = d = [];
            d[key] = at;
        }
        return result;
    }
}
