using System.Collections.ObjectModel;
using System.Globalization;
using SaveEditor.Core;

namespace SaveEditor.Games.GtaV;

/// <summary>The story's either/or decisions; each mission tied to one belongs to one of its options.</summary>
public enum StoryChoice { JewelStore, Merryweather, BureauRaid, BigScore, Ending }

/// <summary>One option of a <see cref="StoryChoice"/>, e.g. the Jewel Store Job's "Smart" approach (letter A).</summary>
public sealed record StoryChoiceOption(char Letter, string Name);

/// <summary>One story mission and its saved mission record.</summary>
public sealed class StoryMission
{
    private readonly PsoNode _done, _order, _time, _score;

    internal StoryMission(MissionInfo info, PsoNode record)
    {
        Info = info;
        var f = record.Children;
        (_done, _order, _time, _score) = (f[0], f[3], f[4], f[5]);
    }

    internal MissionInfo Info { get; }

    /// <summary>The mission's number in the game's mission list (the record index).</summary>
    public int Index => Info.Index;

    /// <summary>The game's short id, e.g. "ARM1".</summary>
    public string Code => Info.Code;

    public string Name => Info.Name;

    /// <summary>The decision this mission depends on, or null if every playthrough has it.</summary>
    public StoryChoice? Choice => Info.Choice;

    /// <summary>The option of <see cref="Choice"/> this mission belongs to.</summary>
    public char Letter => Info.Letter;

    /// <summary>A background event rather than a mission the story waits for.</summary>
    public bool Optional => Info.Optional;

    public bool Completed => _done.GetInt32() != 0;

    /// <summary>When it was completed relative to the other missions (0 = first); -1 if not completed.</summary>
    public int Order => _order.GetInt32();

    internal void Write(bool completed, int order)
    {
        _done.SetInt32(completed ? 1 : 0);
        _order.SetInt32(completed ? order : -1);
        if (!completed)
        {
            // Back to the state of a mission that was never played.
            _time.SetInt32(0);
            _score.SetValue("0");
        }
    }
}

/// <summary>
/// The story missions in the order a typical playthrough meets them, with their saved mission records.
/// <para>
/// Marking a mission completed writes what the game itself writes when one is passed (docs/FINDINGS.md section 12):
/// the record's completed flag and completion order, the missions-completed counter, the mission's FL_CO_ stat and its
/// 100% checklist entry. The story itself is not moved here: when the game's flow controller reaches a mission whose
/// record is already completed, it skips the mission and carries on with the story after it.
/// </para>
/// </summary>
public sealed class StoryMissions : ReadOnlyCollection<StoryMission>
{
    // Script save data → mission flow struct → mission records, and the missions-completed counter.
    private static readonly uint[] ScriptData = [0x4B041FD7, 0x77714C97, 0x412DDFE6];
    private const uint FlowField = 0x2FC0B451, RecordsField = 0x739B7568, CounterStruct = 0x83B9CA30, CounterField = 0xFAA57378;
    private const int RecordCount = 100;

    public static IReadOnlyDictionary<StoryChoice, (string Title, IReadOnlyList<StoryChoiceOption> Options)> Choices { get; } =
        new Dictionary<StoryChoice, (string, IReadOnlyList<StoryChoiceOption>)>
        {
            [StoryChoice.JewelStore] = ("The Jewel Store Job", [new('A', "Smart"), new('B', "Loud")]),
            [StoryChoice.Merryweather] = ("The Merryweather Heist", [new('A', "Offshore"), new('B', "Freighter")]),
            [StoryChoice.BureauRaid] = ("The Bureau Raid", [new('A', "Fire Crew"), new('B', "Roof Entry")]),
            [StoryChoice.BigScore] = ("The Big Score", [new('A', "Subtle"), new('B', "Obvious")]),
            [StoryChoice.Ending] = ("Ending", [new('A', "Kill Trevor"), new('B', "Kill Michael"), new('C', "Deathwish")]),
        };

    private readonly PsoNode _counter;
    private readonly StatStore? _stats;
    private readonly Checklist? _checklist;
    private readonly Dictionary<StoryChoice, char> _selected = [];

    private StoryMissions(IList<StoryMission> missions, PsoNode counter, StatStore? stats, Checklist? checklist) : base(missions)
    {
        _counter = counter;
        _stats = stats;
        _checklist = checklist;
        foreach (var (choice, (_, options)) in Choices)
        {
            // Keep whichever option the save already went for; otherwise the first (the third ending for the finale).
            var taken = missions.FirstOrDefault(m => m.Choice == choice && m.Completed);
            _selected[choice] = taken?.Letter ?? (choice == StoryChoice.Ending ? 'C' : options[0].Letter);
        }
    }

    /// <param name="stats">Kept in step (FL_CO_ stats); null to leave stats alone.</param>
    /// <param name="checklist">Kept in step (mission entries and completion figures); null to leave it alone.</param>
    public static StoryMissions Load(PsoDocument pso, StatStore? stats, Checklist? checklist)
    {
        var data = pso.Root;
        foreach (var hash in ScriptData)
            data = data.Children.FirstOrDefault(n => n.NameHash == hash)
                   ?? throw new SaveFormatException("This save has no story script data.");
        var records = data.Children.FirstOrDefault(n => n.NameHash == FlowField)?.Children.FirstOrDefault(n => n.NameHash == RecordsField);
        var counter = data.Children.FirstOrDefault(n => n.NameHash == CounterStruct)?.Children.FirstOrDefault(n => n.NameHash == CounterField);
        if (records == null || counter == null || counter.Type != PsoType.SInt || records.Children.Count != RecordCount
            || records.Children.Any(r => r.Children.Count != 6 || r.Children.Take(5).Any(f => f.Type != PsoType.SInt)
                                         || r.Children[5].Type != PsoType.Float))
            throw new SaveFormatException("The mission records in this save are not in the expected layout.");
        return new StoryMissions(MissionInfo.All.Select(i => new StoryMission(i, records.Children[i.Index])).ToList(), counter, stats, checklist);
    }

    /// <summary>The option currently chosen for a decision; missions of the other options are left out of <see cref="Path"/>.</summary>
    public char Selected(StoryChoice choice) => _selected[choice];

    public void Select(StoryChoice choice, char letter)
    {
        if (Choices[choice].Options.All(o => o.Letter != letter)) throw new ArgumentOutOfRangeException(nameof(letter));
        _selected[choice] = letter;
    }

    /// <summary>The missions of the chosen options, in story order.</summary>
    public IEnumerable<StoryMission> Path => this.Where(m => m.Choice is not { } c || _selected[c] == m.Letter);

    /// <summary>The first mission on <see cref="Path"/> that is not completed (background events aside), or null once the
    /// story is done.</summary>
    public StoryMission? Next => Path.FirstOrDefault(m => !m.Completed && !m.Optional);

    /// <summary>Marks every mission on <see cref="Path"/> up to and including <paramref name="last"/> completed.</summary>
    public void CompleteUpTo(StoryMission last)
    {
        var path = Path.ToList();
        int end = path.IndexOf(last);
        if (end < 0) throw new ArgumentException($"{last.Name} is not on the chosen story path.", nameof(last));
        SetCompleted(path.Take(end + 1), true);
    }

    public void SetCompleted(StoryMission mission, bool completed) => SetCompleted([mission], completed);

    public void SetCompleted(IEnumerable<StoryMission> missions, bool completed)
    {
        var changed = new List<StoryMission>();
        foreach (var m in missions)
        {
            if (!Contains(m)) throw new ArgumentException($"{m.Name} is not part of this save.", nameof(missions));
            if (m.Completed == completed) continue;
            int count = _counter.GetInt32();
            if (completed)
            {
                // The game numbers completions with a running order and counts them in FL_CO_<code>.
                m.Write(true, this.Where(x => x.Completed).Select(x => x.Order).DefaultIfEmpty(-1).Max() + 1);
                _counter.SetInt32(count + 1);
                SetStat(m, count + 1);
            }
            else
            {
                m.Write(false, -1);
                _counter.SetInt32(Math.Max(0, count - 1));
                SetStat(m, 0);
            }
            changed.Add(m);
        }
        if (changed.Count > 0) UpdateChecklist(changed);
    }

    private void SetStat(StoryMission m, int value)
    {
        if (_stats == null || m.Info.Stat == null) return;
        // The Jewel Store Job's stat name ends in the approach letter.
        string name = m.Info.Stat.Replace("*", _selected[StoryChoice.JewelStore].ToString(CultureInfo.InvariantCulture));
        if (_stats.Has(StatKind.Int32, name)) _stats.SetInt(name, value);
    }

    private void UpdateChecklist(IEnumerable<StoryMission> changed)
    {
        if (_checklist == null) return;
        foreach (int index in changed.Select(m => m.Info.ChecklistIndex).OfType<int>().Distinct())
        {
            var item = _checklist.FirstOrDefault(i => i.Index == index);
            if (item == null) continue;
            // Missions of different approaches share an entry; it is done while any of them is.
            _checklist.SetCompleted(item, this.Any(m => m.Info.ChecklistIndex == index && m.Completed));
        }
    }
}

/// <summary>Fixed facts about a story mission: record index, id, name, checklist entry, completion stat, decision.</summary>
internal sealed record MissionInfo(int Index, string Code, string Name, int? ChecklistIndex, string? Stat,
    StoryChoice? Choice = null, char Letter = ' ')
{
    /// <summary>Michael's family events run in the background and not every playthrough gets them all
    /// (the user's 98.8% save never had Amanda's), so they are never "the next mission".</summary>
    public bool Optional => Code is "MEA1" or "MEJ1" or "MET1";

    // Story order follows two real playthroughs (the user's 100% saves); see docs/FINDINGS.md section 12.
    // Record indices, ids, checklist entries and stats come from the game's mission data (build 2628 scripts).
    public static IReadOnlyList<MissionInfo> All { get; } =
    [
        M(53, "PRO1", "Prologue", 38),
        M(0, "ARM1", "Franklin and Lamar", 0),
        M(1, "ARM2", "Repossession", 1),
        M(40, "FRA0", "Chop", 28),
        M(2, "ARM3", "Complications", 2),
        M(17, "FAM1", "Father/Son", 12),
        M(43, "LM1", "The Long Stretch", 31),
        M(19, "FAM3", "Marriage Counseling", 14),
        M(18, "FAM2", "Daddy's Little Girl", 13),
        M(54, "DRF1", "Dr. Friedlander, session 1", null),
        M(44, "LS1", "Friend Request", 32),
        M(50, "MEA1", "Michael's family: Amanda", null),
        M(86, "JH1", "Casing the Jewel Store", 53),
        M(87, "JHP1A", "Bugstars Equipment", 54, StoryChoice.JewelStore, 'A'),
        M(88, "JHP2A", "BZ Gas Grenades", null, StoryChoice.JewelStore, 'A'),
        M(89, "JHP1B", "Carbine Rifles", 54, StoryChoice.JewelStore, 'B'),
        new(90, "JH2", "The Jewel Store Job", 55, "FL_CO_JH2*"),
        M(62, "TRV1", "Mr. Philips", 43),
        M(12, "CHN1", "Trevor Philips Industries", 7),
        M(63, "TRV2", "Nervous Ron", 44),
        M(13, "CHN2", "Crystal Maze", 8),
        M(64, "TRV3", "Friends Reunited", 45),
        M(20, "FAM4", "Fame or Shame", 15),
        M(29, "FIB1", "Dead Man Walking", 18),
        M(30, "FIB2", "Three's Company", 19),
        M(3, "ASS1", "The Hotel Assassination", 144),
        M(41, "FRA1", "Hood Safari", 29),
        M(31, "FIB3", "By the Book", 20),
        M(21, "FAM5", "Did Somebody Say Yoga?", 16),
        M(71, "DH1", "Scouting the Port", 60),
        M(72, "DHP1", "Minisub", 59),
        M(73, "DHP2B", "Merryweather Heist preparation (Freighter)", null, StoryChoice.Merryweather, 'B'),
        M(74, "DH2A", "The Merryweather Heist (Offshore)", 61, StoryChoice.Merryweather, 'A'),
        M(75, "DH2B", "The Merryweather Heist (Freighter)", 61, StoryChoice.Merryweather, 'B'),
        new(32, "FIB4I", "Blitz Play setup", null, null),
        M(34, "FB4P2", "Tow Truck", 22),
        M(33, "FB4P1", "Trash Truck", 21),
        M(37, "FB4P5", "Boiler Suits", 25),
        M(36, "FB4P4", "Masks", 24),
        M(38, "FIB4", "Blitz Play", 26),
        M(8, "CAR1", "I Fought the Law...", 3),
        M(59, "SOL1", "Mr. Richards", 40),
        M(55, "DRF2", "Dr. Friedlander, session 2", null),
        M(9, "CAR2", "Eye in the Sky", 4),
        M(56, "DRF3", "Dr. Friedlander, session 3", null),
        M(45, "MAR1", "Caida Libre", 39),
        M(10, "CAR3", "Deep Inside", 5),
        M(14, "EXL1", "Minor Turbulence", 9),
        M(91, "RH1", "Paleto Score Setup", 57),
        M(15, "EXL2", "Predator", 10),
        M(57, "DRF4", "Dr. Friedlander, session 4", null),
        M(92, "RHP1", "Military Hardware", 56),
        M(93, "RH2", "The Paleto Score", 58),
        M(16, "EXL3", "Derailed", 11),
        M(39, "FIB5", "Monkey Business", 27),
        M(65, "TRV4", "Hang Ten", 46),
        M(76, "FH1", "Surveying the Score", 51),
        M(46, "MIC1", "Bury the Hatchet", 34),
        M(11, "CAR4", "Pack Man", 6),
        M(47, "MIC2", "Fresh Meat", 35),
        M(60, "SOL2", "The Ballad of Rocco", 41),
        M(66, "AH1", "Cleaning out the Bureau", 47),
        M(22, "FAM6", "Reuniting the Family", 17),
        M(67, "AH2", "Architect's Plans", 48),
        M(68, "AHP1", "Fire Truck", null, StoryChoice.BureauRaid, 'A'),
        M(69, "AH3a", "The Bureau Raid (Fire Crew)", 49, StoryChoice.BureauRaid, 'A'),
        M(70, "AH3b", "The Bureau Raid (Roof Entry)", 49, StoryChoice.BureauRaid, 'B'),
        M(52, "MET1", "Michael's family: Tracey", null),
        M(48, "MIC3", "The Wrap Up", 36),
        M(61, "SOL3", "Legal Trouble", 42),
        M(42, "FRA2", "Lamar Down", 30),
        M(49, "MIC4", "Meltdown", 37),
        M(77, "FH2I", "Planning the Score", 50),
        M(78, "FHPRA", "Big Score preparation (Subtle)", 62, StoryChoice.BigScore, 'A'),
        M(80, "FHPC1", "Gauntlet 1", 63, StoryChoice.BigScore, 'A'),
        M(81, "FHPC2", "Gauntlet 2", 63, StoryChoice.BigScore, 'A'),
        M(82, "FHPC3", "Gauntlet 3", 63, StoryChoice.BigScore, 'A'),
        M(79, "FHPRB", "Big Score preparation 1 (Obvious)", 62, StoryChoice.BigScore, 'B'),
        M(83, "FHPD", "Big Score preparation 2 (Obvious)", 63, StoryChoice.BigScore, 'B'),
        M(51, "MEJ1", "Michael's family: Jimmy", null),
        M(84, "FH2A", "The Big Score (Subtle)", 52, StoryChoice.BigScore, 'A'),
        M(85, "FH2B", "The Big Score (Obvious)", 52, StoryChoice.BigScore, 'B'),
        new(23, "FINI", "The finale: the choice", null, null),
        M(24, "FINA", "Something Sensible", 65, StoryChoice.Ending, 'A'),
        M(25, "FINB", "The Time's Come", 65, StoryChoice.Ending, 'B'),
        new(26, "FINC1", "The Third Way, part 1", null, null, StoryChoice.Ending, 'C'),
        M(27, "FINC2", "The Third Way", 65, StoryChoice.Ending, 'C'),
        new(28, "FINC", "End credits", null, null),
        M(4, "ASS2", "The Multi Target Assassination", 145),
        M(5, "ASS3", "The Vice Assassination", 146),
        M(6, "ASS4", "The Bus Assassination", 147),
        M(7, "ASS5", "The Construction Assassination", 148),
        M(58, "DRF5", "Dr. Friedlander, session 5", null),
    ];

    private static MissionInfo M(int index, string code, string name, int? checklist, StoryChoice? choice = null, char letter = ' ') =>
        new(index, code, name, checklist, "FL_CO_" + code, choice, letter);
}
