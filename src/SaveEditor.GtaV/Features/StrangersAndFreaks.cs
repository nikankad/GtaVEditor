using System.Collections.ObjectModel;

namespace SaveEditor.GtaV;

/// <summary>
/// Story missions and Strangers and Freaks missions share one running completion order (the game adds the two
/// completion counters together). The next number is one past the highest order of any completed record.
/// </summary>
internal static class CompletionOrder
{
    private static readonly uint[] ScriptDataPath = [0x4B041FD7, 0x77714C97, 0x412DDFE6];
    internal const uint FlowField = 0x2FC0B451, StoryRecordsField = 0x739B7568, StrangersRecordsField = 0xA2A52C6E;

    /// <summary>The script save data (g_savedGlobals).</summary>
    public static PsoNode ScriptData(PsoDocument pso)
    {
        var data = pso.Root;
        foreach (var hash in ScriptDataPath)
            data = data.Children.FirstOrDefault(n => n.NameHash == hash)
                   ?? throw new SaveFormatException("This save has no story script data.");
        return data;
    }

    public static int Next(PsoNode data)
    {
        int max = -1;
        // Story records: [done, ?, ?, order, …]; Strangers and Freaks: [state bits, done, ?, order, …].
        var story = data.Children.FirstOrDefault(n => n.NameHash == FlowField)?.Children.FirstOrDefault(n => n.NameHash == StoryRecordsField);
        foreach (var r in story?.Children ?? [])
            if (r.Children[0].GetInt32() != 0) max = Math.Max(max, r.Children[3].GetInt32());
        var strangers = data.Children.FirstOrDefault(n => n.NameHash == StrangersRecordsField);
        foreach (var r in strangers?.Children ?? [])
            if (StrangerMission.IsCompleted(r.Children[0].GetInt32(), r.Children[1].GetInt32())) max = Math.Max(max, r.Children[3].GetInt32());
        return max + 1;
    }
}

/// <summary>One Strangers and Freaks mission and its saved record.</summary>
public sealed class StrangerMission
{
    // Bits of the record's state: all six are set once a mission is passed. Unplayed missions keep only
    // bit 1 (activated by the story) and bit 4, as seen in every unplayed record in the saves.
    private const int PassedBits = 63, UnplayedBits = 0b010010;

    private readonly PsoNode _state, _done, _order, _score;

    internal StrangerMission(StrangerInfo info, PsoNode record)
    {
        Info = info;
        var f = record.Children;
        (_state, _done, _order, _score) = (f[0], f[1], f[3], f[5]);
    }

    internal StrangerInfo Info { get; }

    /// <summary>The record index (the game's random-character mission number).</summary>
    public int Index => Info.Index;

    /// <summary>The mission script, e.g. "Epsilon3".</summary>
    public string Script => Info.Script;

    public string Name => Info.Name;

    /// <summary>The stranger or freak the mission belongs to, e.g. "Epsilon Program".</summary>
    public string Character => Info.Character;

    public bool Completed => IsCompleted(_state.GetInt32(), _done.GetInt32());

    /// <summary>Passed missions have the completed flag, except Damaged Goods, which the game marks only with state
    /// bits 3–5 (56) in every finished save.</summary>
    internal static bool IsCompleted(int state, int done) => done != 0 || (state & PassedOnlyBit) != 0;

    private const int PassedOnlyBit = 32, DamagedGoodsBits = 56, DamagedGoods = 36;

    public int Order => _order.GetInt32();

    internal void Write(bool completed, int order)
    {
        bool flagless = Index == DamagedGoods;
        _state.SetInt32(completed ? _state.GetInt32() | (flagless ? DamagedGoodsBits : PassedBits) : _state.GetInt32() & UnplayedBits);
        _done.SetInt32(completed && !flagless ? 1 : 0);
        _order.SetInt32(completed ? order : -1);
        if (!completed) _score.SetValue("0");
    }
}

/// <summary>
/// The 63 Strangers and Freaks (random character) missions, grouped by character in the game's own order.
/// Marking one completed writes its record (state, completed flag, completion order), the Strangers and Freaks
/// counter and its 100% checklist entry.
/// </summary>
public sealed class StrangersAndFreaks : ReadOnlyCollection<StrangerMission>
{
    private const uint CounterField = 0x91D94BB8;
    private const int RecordCount = 63;

    private readonly PsoNode _data, _counter;
    private readonly Checklist? _checklist;

    private StrangersAndFreaks(IList<StrangerMission> missions, PsoNode data, PsoNode counter, Checklist? checklist) : base(missions)
    {
        _data = data;
        _counter = counter;
        _checklist = checklist;
    }

    public static StrangersAndFreaks Load(PsoDocument pso, Checklist? checklist)
    {
        var data = CompletionOrder.ScriptData(pso);
        var records = data.Children.FirstOrDefault(n => n.NameHash == CompletionOrder.StrangersRecordsField);
        var counter = data.Children.FirstOrDefault(n => n.NameHash == CounterField);
        if (records == null || counter == null || counter.Type != PsoType.SInt || records.Children.Count != RecordCount
            || records.Children.Any(r => r.Children.Count != 6 || r.Children.Take(5).Any(f => f.Type != PsoType.SInt)))
            throw new SaveFormatException("The Strangers and Freaks records in this save are not in the expected layout.");
        return new StrangersAndFreaks(StrangerInfo.All.Select(i => new StrangerMission(i, records.Children[i.Index])).ToList(),
            data, counter, checklist);
    }

    public void SetCompleted(StrangerMission mission, bool completed) => SetCompleted([mission], completed);

    public void SetCompleted(IEnumerable<StrangerMission> missions, bool completed)
    {
        var changed = new List<StrangerMission>();
        foreach (var m in missions)
        {
            if (!Contains(m)) throw new ArgumentException($"{m.Name} is not part of this save.", nameof(missions));
            if (m.Completed == completed) continue;
            m.Write(completed, completed ? CompletionOrder.Next(_data) : -1);
            _counter.SetInt32(Math.Max(0, _counter.GetInt32() + (completed ? 1 : -1)));
            changed.Add(m);
        }
        if (_checklist == null) return;
        foreach (var label in changed.Select(m => m.Info.ChecklistLabel).OfType<string>().Distinct())
            if (_checklist.FirstOrDefault(i => i.Label == label) is { } item)
                _checklist.SetCompleted(item, this.Any(m => m.Info.ChecklistLabel == label && m.Completed));
    }
}

/// <summary>Fixed facts about a Strangers and Freaks mission (record index and script from the game's
/// random-character table in build 2628's flow_controller; names from the 100% checklist).</summary>
internal sealed record StrangerInfo(int Index, string Script, string Character, string Name, string? ChecklistLabel)
{
    public static IReadOnlyList<StrangerInfo> All { get; } =
    [
        new(0, "Abigail1", "Abigail", "Death at Sea", "SC_RC_ABI1"),
        new(1, "Abigail2", "Abigail", "What Lies Beneath", null),
        new(2, "Barry1", "Barry", "Grass Roots - Michael", "SC_RC_BA1"),
        new(3, "Barry2", "Barry", "Grass Roots - Trevor", "SC_RC_BA2"),
        new(4, "Barry3", "Barry", "Grass Roots - Franklin", "SC_RC_BA3"),
        new(5, "Barry3A", "Barry", "Grass Roots - Franklin: stash car 1", null),
        new(6, "Barry3C", "Barry", "Grass Roots - Franklin: stash car 2", null),
        new(7, "Barry4", "Barry", "Grass Roots - The Smoke-in", "SC_RC_BA4"),
        new(8, "Dreyfuss1", "Peter Dreyfuss", "A Starlet in Vinewood", "SC_RC_DRE1"),
        new(9, "Epsilon1", "Epsilon Program", "Seeking the Truth", "SC_RC_EPS1"),
        new(10, "Epsilon2", "Epsilon Program", "Accepting the Truth", "SC_RC_EPS2"),
        new(11, "Epsilon3", "Epsilon Program", "Assuming the Truth", "SC_RC_EPS3"),
        new(12, "Epsilon4", "Epsilon Program", "Chasing the Truth", "SC_RC_EPS4"),
        new(13, "Epsilon5", "Epsilon Program", "Bearing the Truth", "SC_RC_EPS5"),
        new(14, "Epsilon6", "Epsilon Program", "Delivering the Truth", "SC_RC_EPS6"),
        new(15, "Epsilon7", "Epsilon Program", "Exercising the Truth", "SC_RC_EPS7"),
        new(16, "Epsilon8", "Epsilon Program", "Unknowing the Truth", "SC_RC_EPS8"),
        new(17, "Extreme1", "Dom", "Risk Assessment", "SC_RC_EXT1"),
        new(18, "Extreme2", "Dom", "Liquidity Risk", "SC_RC_EXT2"),
        new(19, "Extreme3", "Dom", "Targeted Risk", "SC_RC_EXT3"),
        new(20, "Extreme4", "Dom", "Uncalculated Risk", "SC_RC_EXT4"),
        new(21, "Fanatic1", "Mary-Ann", "Exercising Demons - Michael", "SC_RC_FAN1"),
        new(22, "Fanatic2", "Mary-Ann", "Exercising Demons - Trevor", "SC_RC_FAN2"),
        new(23, "Fanatic3", "Mary-Ann", "Exercising Demons - Franklin", "SC_RC_FAN3"),
        new(24, "Hao1", "Hao", "Shift Work", "SC_RC_HAO1"),
        new(25, "Hunting1", "Cletus", "Target Practice", "SC_RC_HUN1"),
        new(26, "Hunting2", "Cletus", "Fair Game", "SC_RC_HUN2"),
        new(27, "Josh1", "Josh", "Extra Commission", "SC_RC_JOS1"),
        new(28, "Josh2", "Josh", "Closing the Deal", "SC_RC_JOS2"),
        new(29, "Josh3", "Josh", "Surreal Estate", "SC_RC_JOS3"),
        new(30, "Josh4", "Josh", "Breach of Contract", "SC_RC_JOS4"),
        new(31, "Maude1", "Maude", "Bail Bonds", "SC_RC_MAU1"),
        new(32, "Minute1", "Joe and Josef", "The Civil Border Patrol", "SC_RC_MIN1"),
        new(33, "Minute2", "Joe and Josef", "An American Welcome", "SC_RC_MIN2"),
        new(34, "Minute3", "Joe and Josef", "Minute Man Blues", "SC_RC_MIN3"),
        new(35, "MrsPhilips1", "Mrs. Philips", "Mrs. Philips", "SC_RC_MRS1"),
        new(36, "MrsPhilips2", "Mrs. Philips", "Damaged Goods", "SC_RC_MRS2"),
        new(37, "Nigel1", "Nigel and Mrs. Thornhill", "Nigel and Mrs. Thornhill", "SC_RC_NI1"),
        new(38, "Nigel1A", "Nigel and Mrs. Thornhill", "Vinewood Souvenirs - Willie", "SC_RC_NI1A"),
        new(39, "Nigel1B", "Nigel and Mrs. Thornhill", "Vinewood Souvenirs - Tyler", "SC_RC_NI1B"),
        new(40, "Nigel1C", "Nigel and Mrs. Thornhill", "Vinewood Souvenirs - Kerry", "SC_RC_NI1C"),
        new(41, "Nigel1D", "Nigel and Mrs. Thornhill", "Vinewood Souvenirs - Mark", "SC_RC_NI1D"),
        new(42, "Nigel2", "Nigel and Mrs. Thornhill", "Vinewood Souvenirs - Al Di Napoli", "SC_RC_NI2"),
        new(43, "Nigel3", "Nigel and Mrs. Thornhill", "Vinewood Souvenirs - The Last Act", "SC_RC_NI3"),
        new(44, "Omega1", "Omega", "Far Out", "SC_RC_OME1"),
        new(45, "Omega2", "Omega", "The Final Frontier", "SC_RC_OME2"),
        new(46, "Paparazzo1", "Beverly", "Paparazzo", "SC_RC_PA1"),
        new(47, "Paparazzo2", "Beverly", "Paparazzo - The Sex Tape", "SC_RC_PA2"),
        new(48, "Paparazzo3", "Beverly", "Paparazzo - The Partnership", "SC_RC_PA3"),
        new(49, "Paparazzo3A", "Beverly", "Paparazzo - The Partnership: photo 1", null),
        new(50, "Paparazzo3B", "Beverly", "Paparazzo - The Partnership: photo 2", null),
        new(51, "Paparazzo4", "Beverly", "Paparazzo - Reality Check", "SC_RC_PA4"),
        new(52, "Rampage1", "Rampages", "Rampage: Rednecks", "SC_RC_RAM1"),
        new(53, "Rampage2", "Rampages", "Rampage: Hipsters", "SC_RC_RAM2"),
        new(54, "Rampage3", "Rampages", "Rampage: Vagos", "SC_RC_RAM3"),
        new(55, "Rampage4", "Rampages", "Rampage: Ballas", "SC_RC_RAM4"),
        new(56, "Rampage5", "Rampages", "Rampage: Military", "SC_RC_RAM5"),
        new(57, "TheLastOne", "The Last One", "The Last One", "SC_RC_SAS1"),
        new(58, "Tonya1", "Tonya", "Pulling Favors", "SC_RC_TON1"),
        new(59, "Tonya2", "Tonya", "Pulling Another Favor", "SC_RC_TON2"),
        new(60, "Tonya3", "Tonya", "Pulling Favors Again", "SC_RC_TON3"),
        new(61, "Tonya4", "Tonya", "Still Pulling Favors", "SC_RC_TON4"),
        new(62, "Tonya5", "Tonya", "Pulling One Last Favor", "SC_RC_TON5"),
    ];
}
