using System.Globalization;

namespace SaveEditor.GtaV;

/// <summary>
/// One character's status in the script save data's player-ped struct (field names from the game's script variable
/// names). Every field holds one value per character in the order Michael, Franklin, Trevor.
/// </summary>
public sealed class CharacterStatus
{
    private readonly PsoNode _health, _armor, _abilityUnlocked, _ability, _wanted, _heading;
    private readonly PsoNode[] _position;

    internal CharacterStatus(string name, int index, PsoNode health, PsoNode armor, PsoNode abilityUnlocked, PsoNode ability,
        PsoNode wanted, PsoNode[] position, PsoNode heading)
    {
        Name = name;
        Index = index;
        (_health, _armor, _abilityUnlocked, _ability, _wanted, _position, _heading) =
            (health, armor, abilityUnlocked, ability, wanted, position, heading);
    }

    public string Name { get; }

    /// <summary>0 Michael, 1 Franklin, 2 Trevor (the game's player-character number).</summary>
    public int Index { get; }

    /// <summary>Health in percent (100 = full).</summary>
    public int Health
    {
        get => (int)Math.Round(F(_health));
        set => _health.SetValue(Check(value, 0, 100).ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Body armor in percent.</summary>
    public int Armor { get => _armor.GetInt32(); set => _armor.SetInt32(Check(value, 0, 100)); }

    public bool SpecialAbilityUnlocked { get => _abilityUnlocked.GetInt32() != 0; set => _abilityUnlocked.SetInt32(value ? 1 : 0); }

    /// <summary>How full the special ability bar is, in percent.</summary>
    public int SpecialAbility { get => _ability.GetInt32(); set => _ability.SetInt32(Check(value, 0, 100)); }

    /// <summary>Wanted level in stars (0–5) the character had when last played.</summary>
    public int WantedLevel { get => _wanted.GetInt32(); set => _wanted.SetInt32(Check(value, 0, 5)); }

    /// <summary>Where the character is when you next switch to them (x, y, z in world units).</summary>
    public (float X, float Y, float Z) Position
    {
        get => (F(_position[0]), F(_position[1]), F(_position[2]));
        set
        {
            foreach (var (node, v) in _position.Zip(new[] { value.X, value.Y, value.Z }))
                node.SetValue(Check(v, -10000, 10000).ToString("R", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Which way the character faces, in degrees.</summary>
    public float Heading
    {
        get => F(_heading);
        set => _heading.SetValue(((value % 360 + 360) % 360).ToString("R", CultureInfo.InvariantCulture));
    }

    private static float F(PsoNode n) => float.Parse(n.ValueText!, CultureInfo.InvariantCulture);

    private static T Check<T>(T value, T min, T max) where T : IComparable<T> =>
        value.CompareTo(min) < 0 || value.CompareTo(max) > 0
            ? throw new ArgumentOutOfRangeException(nameof(value), $"Must be between {min} and {max}.")
            : value;
}

/// <summary>The in-game date and time, from the game's own save variables (SimpleVariables).</summary>
public sealed class GameClock
{
    private readonly PsoNode _hour, _minute, _second, _day, _month, _year;

    internal GameClock(PsoNode hour, PsoNode minute, PsoNode second, PsoNode day, PsoNode month, PsoNode year) =>
        (_hour, _minute, _second, _day, _month, _year) = (hour, minute, second, day, month, year);

    public int Hour => _hour.GetInt32();
    public int Minute => _minute.GetInt32();
    public int Second => _second.GetInt32();
    public int Day => _day.GetInt32();

    /// <summary>The month as the game counts it: 0 = January.</summary>
    public int Month => _month.GetInt32();

    public int Year => _year.GetInt32();

    public void SetTime(int hour, int minute, int second = 0)
    {
        if (hour is < 0 or > 23 || minute is < 0 or > 59 || second is < 0 or > 59)
            throw new ArgumentOutOfRangeException(nameof(hour), "Enter a time between 00:00 and 23:59.");
        _hour.SetInt32(hour);
        _minute.SetInt32(minute);
        _second.SetInt32(second);
    }

    /// <param name="month">0 = January.</param>
    public void SetDate(int year, int month, int day)
    {
        if (year is < 1900 or > 2100 || month is < 0 or > 11 || day < 1 || day > DateTime.DaysInMonth(year, month + 1))
            throw new ArgumentOutOfRangeException(nameof(day), "Enter a real calendar date.");
        _year.SetInt32(year);
        _month.SetInt32(month);
        _day.SetInt32(day);
    }
}

/// <summary>Player status for the three characters, the current character and the clock.</summary>
public sealed class PlayerStatus
{
    private const uint PlayerPed = 0x622852C8, Health = 0xCC13AAD0, Armor = 0x9CDB82CB;
    private const uint SimpleVariables = 0x765FB4E3;
    // SimpleVariables clock fields (names unknown; identified by their values across saves).
    private const uint ClockHour = 0xC8893D05, ClockMinute = 0xFB8C5018, ClockSecond = 0xAE65D6CB,
        ClockDay = 0xDBCB068B, ClockMonth = 0xBB64A995, ClockYear = 0x9EBBBABF;

    private readonly PsoNode _current;

    private PlayerStatus(IReadOnlyList<CharacterStatus> characters, PsoNode current, GameClock? clock)
    {
        Characters = characters;
        _current = current;
        Clock = clock;
    }

    public IReadOnlyList<CharacterStatus> Characters { get; }

    /// <summary>Null when the save has no clock in the expected place.</summary>
    public GameClock? Clock { get; }

    /// <summary>The character you play when the save loads (0 Michael, 1 Franklin, 2 Trevor).</summary>
    public int CurrentCharacter => _current.GetInt32();

    public static PlayerStatus Load(PsoDocument pso)
    {
        var ped = CompletionOrder.ScriptData(pso).Children.FirstOrDefault(n => n.NameHash == PlayerPed)
                  ?? throw new SaveFormatException("This save has no player data.");
        PsoNode Field(uint hash, int count, PsoType type)
        {
            var f = ped.Children.FirstOrDefault(n => n.NameHash == hash);
            if (f == null || f.Children.Count != count || f.Children.Any(c => c.Type != type))
                throw new SaveFormatException("The player data in this save is not in the expected layout.");
            return f;
        }
        var health = Field(Health, 3, PsoType.Float);
        var armor = Field(Armor, 3, PsoType.SInt);
        var unlocked = Field(PsoNames.Hash("SPECIAL_ABILITY"), 3, PsoType.SInt);
        var ability = Field(PsoNames.Hash("SPEC_AB_UNLOCK"), 3, PsoType.SInt);
        var wanted = Field(PsoNames.Hash("LAST_KNOWN_WANTED"), 3, PsoType.SInt);
        var position = Field(PsoNames.Hash("LAST_KNOWN_XCOORD"), 9, PsoType.Float);
        var heading = Field(PsoNames.Hash("LAST_KNOWN_HEADING"), 3, PsoType.Float);
        var current = ped.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash("PP_CURRENT_PED") && n.Type == PsoType.SInt)
                      ?? throw new SaveFormatException("The player data in this save is not in the expected layout.");

        var characters = StatCatalog.Characters.Select((name, i) => new CharacterStatus(name, i, health.Children[i], armor.Children[i],
            unlocked.Children[i], ability.Children[i], wanted.Children[i], position.Children.Skip(i * 3).Take(3).ToArray(),
            heading.Children[i])).ToList();

        GameClock? clock = null;
        var simple = pso.Root.Children.FirstOrDefault(n => n.NameHash == SimpleVariables);
        var fields = new[] { ClockHour, ClockMinute, ClockSecond, ClockDay, ClockMonth, ClockYear }
            .Select(h => simple?.Children.FirstOrDefault(n => n.NameHash == h && n.Type is PsoType.UInt or PsoType.SInt)).ToArray();
        if (fields.All(f => f != null)) clock = new GameClock(fields[0]!, fields[1]!, fields[2]!, fields[3]!, fields[4]!, fields[5]!);
        return new PlayerStatus(characters, current, clock);
    }
}
