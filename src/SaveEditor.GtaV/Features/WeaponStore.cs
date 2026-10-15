
namespace SaveEditor.GtaV;

/// <summary>A weapon the editor knows: the game's id (the save stores JOAAT of it, lower-cased) and where it lives.</summary>
public sealed record WeaponInfo(string Id, string Label, string Category, int Table, int Slot, int MaxAmmo)
{
    public uint Hash { get; } = GtaHash.Joaat(Id);
    public bool UsesAmmo => MaxAmmo > 0;
}

/// <summary>
/// One weapon for one character. Every weapon has a fixed slot in the character's slot tables; an all-zero slot
/// means the character does not have it (observed in real saves).
/// </summary>
public sealed class CharacterWeapon
{
    private readonly IReadOnlyList<PsoNode> _slot; // the slot's ints: weapon hash, ammo, then version-dependent fields

    internal CharacterWeapon(string character, WeaponInfo weapon, IReadOnlyList<PsoNode> slot)
    {
        Character = character;
        Weapon = weapon;
        _slot = slot;
    }

    public string Character { get; }
    public WeaponInfo Weapon { get; }

    public bool Owned => (uint)_slot[0].GetInt32() == Weapon.Hash;

    public int Ammo
    {
        get => Owned ? _slot[1].GetInt32() : 0;
        set
        {
            if (!Owned) throw new InvalidOperationException($"{Character} does not have the {Weapon.Label}.");
            if (value < 0 || value > Math.Max(Weapon.MaxAmmo, 0))
                throw new ArgumentOutOfRangeException(nameof(value), Weapon.UsesAmmo
                    ? $"{Weapon.Label} ammo must be between 0 and {Weapon.MaxAmmo:N0}."
                    : $"The {Weapon.Label} does not use ammo.");
            _slot[1].SetInt32(value);
        }
    }

    /// <summary>Gives the weapon (full ammo, no attachments, default tint). Keeps an already owned weapon as it is.</summary>
    public void Give()
    {
        if (Owned) return;
        _slot[0].SetInt32((int)Weapon.Hash);
        _slot[1].SetInt32(Weapon.MaxAmmo);
        switch (_slot.Count)
        {
            case 5: // current saves: attachments, tint, then -1 in every owned slot seen
                _slot[2].SetInt32(0);
                _slot[3].SetInt32(0);
                _slot[4].SetInt32(-1);
                break;
            case 3: // older saves pack tint and attachments; 0x100000 is the value of an unmodified weapon
                _slot[2].SetInt32(0x100000);
                break;
            default:
                for (int i = 2; i < _slot.Count; i++) _slot[i].SetInt32(0);
                break;
        }
    }

    /// <summary>Takes the weapon away (clears its slot, as in saves where the character never had it).</summary>
    public void Remove()
    {
        foreach (var n in _slot) n.SetInt32(0);
    }

    // Current saves keep attachments and tint in their own ints; older ones pack both into the third int, with the
    // tint as a single bit from bit 20 up.
    private const int TintShift = 20, AttachmentMask = (1 << TintShift) - 1;

    /// <summary>The attachments the game can put on this weapon (empty when the save's slot layout is unknown).</summary>
    public IReadOnlyList<WeaponAttachment> Attachments =>
        _slot.Count is 3 or 5 ? WeaponAttachments.For(Weapon) : [];

    public bool HasAttachment(WeaponAttachment attachment) => Owned && (AttachmentBits & (1 << attachment.Bit)) != 0;

    /// <summary>Fits or removes an attachment; fitting one takes off the others it replaces (another clip, scope or finish).</summary>
    public void SetAttachment(WeaponAttachment attachment, bool on)
    {
        if (!Owned) throw new InvalidOperationException($"{Character} does not have the {Weapon.Label}.");
        if (!Attachments.Contains(attachment)) throw new ArgumentException($"The {Weapon.Label} can't take a {attachment.Name}.");
        int bits = AttachmentBits;
        if (on && attachment.Group != null)
            foreach (var other in Attachments.Where(a => a.Group == attachment.Group)) bits &= ~(1 << other.Bit);
        bits = on ? bits | (1 << attachment.Bit) : bits & ~(1 << attachment.Bit);
        AttachmentBits = bits;
    }

    /// <summary>
    /// Fits every useful attachment: the biggest clip, the best scope, and the flashlight, suppressor and grip.
    /// Finishes, knuckle styles and the rail cover are left as they are.
    /// </summary>
    public void FitAllAttachments()
    {
        if (!Owned) return;
        foreach (var a in Attachments.Where(a => a.Group == null && a.Name != WeaponAttachments.RailCover))
            SetAttachment(a, true);
        foreach (var group in new[] { WeaponAttachments.Clip, WeaponAttachments.Scope })
            if (Attachments.LastOrDefault(a => a.Group == group) is { } best) SetAttachment(best, true);
    }

    /// <summary>True when the weapon can be painted a tint.</summary>
    public bool CanTint => _slot.Count is 3 or 5 && WeaponTints.Applies(Weapon);

    /// <summary>The weapon's tint, 0–7 (see <see cref="WeaponTints.Names"/>).</summary>
    public int Tint
    {
        get
        {
            if (!Owned) return 0;
            if (_slot.Count == 5) return Math.Clamp(_slot[3].GetInt32(), 0, WeaponTints.Names.Count - 1);
            int packed = _slot[2].GetInt32() >> TintShift;
            for (int t = 0; t < WeaponTints.Names.Count; t++)
                if ((packed & (1 << t)) != 0) return t;
            return 0;
        }
        set
        {
            if (!Owned) throw new InvalidOperationException($"{Character} does not have the {Weapon.Label}.");
            if (!CanTint) throw new InvalidOperationException($"The {Weapon.Label} can't be tinted.");
            if (value < 0 || value >= WeaponTints.Names.Count) throw new ArgumentOutOfRangeException(nameof(value), "Unknown tint.");
            if (_slot.Count == 5) _slot[3].SetInt32(value);
            else _slot[2].SetInt32((_slot[2].GetInt32() & AttachmentMask) | (1 << (TintShift + value)));
        }
    }

    private int AttachmentBits
    {
        get => _slot.Count switch { 5 => _slot[2].GetInt32(), 3 => _slot[2].GetInt32() & AttachmentMask, _ => 0 };
        set
        {
            if (_slot.Count == 5) _slot[2].SetInt32(value);
            else if (_slot.Count == 3) _slot[2].SetInt32((_slot[2].GetInt32() & ~AttachmentMask) | (value & AttachmentMask));
        }
    }
}

/// <summary>An attachment the game can fit to a weapon: its bit in the saved attachment field and its shop name.</summary>
/// <param name="Group">Attachments in the same group replace each other (clips, scopes, finishes).</param>
public sealed record WeaponAttachment(int Bit, string Name, string? Group = null)
{
    public override string ToString() => Name;
}

/// <summary>Weapon tints, in the order the save numbers them.</summary>
public static class WeaponTints
{
    public static IReadOnlyList<string> Names { get; } = ["Normal", "Green", "Gold", "Pink", "Army", "LSPD", "Orange", "Platinum"];

    private static readonly HashSet<string> NoTint = ["WEAPON_STUNGUN", "WEAPON_FIREWORK", "WEAPON_RAILGUN"];

    internal static bool Applies(WeaponInfo w) =>
        w.Category is WeaponCatalog.Pistols or WeaponCatalog.Smgs or WeaponCatalog.Rifles or WeaponCatalog.Shotguns
            or WeaponCatalog.Snipers or WeaponCatalog.Heavy
        && !NoTint.Contains(w.Id);
}

/// <summary>
/// Which attachment each bit of a weapon's saved attachment field stands for. The order is the one the game's
/// scripts use when they put a character's attachments back on after loading. Bits not listed (some weapons keep an
/// internal bit) are left as they are.
/// </summary>
public static class WeaponAttachments
{
    internal const string Clip = "clip", Scope = "scope", Finish = "finish", Style = "style", RailCover = "Rail cover";

    private static WeaponAttachment DefaultClip(int bit) => new(bit, "Default clip", Clip);
    private static WeaponAttachment ExtendedClip(int bit) => new(bit, "Extended clip", Clip);
    private static WeaponAttachment Drum(int bit) => new(bit, "Drum magazine", Clip);
    private static WeaponAttachment Light(int bit) => new(bit, "Flashlight");
    private static WeaponAttachment Suppressor(int bit) => new(bit, "Suppressor");
    private static WeaponAttachment Grip(int bit) => new(bit, "Grip");
    private static WeaponAttachment Sight(int bit, string name = "Scope") => new(bit, name, Scope);
    private static WeaponAttachment Paint(int bit, string name) => new(bit, name + " finish", Finish);

    private static readonly Dictionary<string, WeaponAttachment[]> ByWeapon = new()
    {
        ["WEAPON_PISTOL"] = [DefaultClip(1), ExtendedClip(2), Light(3), Suppressor(4), Paint(5, "Yusuf Amir Luxury")],
        ["WEAPON_COMBATPISTOL"] = [DefaultClip(0), ExtendedClip(1), Light(2), Suppressor(3), Paint(4, "Yusuf Amir Luxury")],
        ["WEAPON_APPISTOL"] = [DefaultClip(0), ExtendedClip(1), Light(2), Suppressor(3), Paint(4, "Gilded Gun Metal"), Paint(5, "Security")],
        ["WEAPON_PISTOL50"] = [DefaultClip(0), ExtendedClip(1), Light(2), Suppressor(3), Paint(4, "Platinum Pearl Deluxe")],
        ["WEAPON_SNSPISTOL"] = [DefaultClip(0), ExtendedClip(1), Paint(2, "Etched Wood Grip")],
        ["WEAPON_HEAVYPISTOL"] = [DefaultClip(0), ExtendedClip(1), Light(2), Suppressor(3), Paint(4, "Etched Wood Grip")],
        ["WEAPON_VINTAGEPISTOL"] = [DefaultClip(0), ExtendedClip(1), Suppressor(2)],

        ["WEAPON_MICROSMG"] = [DefaultClip(0), ExtendedClip(1), Light(2), Sight(3), Suppressor(4), Paint(5, "Yusuf Amir Luxury"), Paint(6, "Security")],
        ["WEAPON_SMG"] = [DefaultClip(1), ExtendedClip(2), Drum(3), Light(4), Suppressor(5), Sight(6), Grip(7), Paint(8, "Yusuf Amir Luxury")],
        ["WEAPON_ASSAULTSMG"] = [DefaultClip(0), ExtendedClip(1), Light(2), Sight(3), Suppressor(4), Paint(5, "Yusuf Amir Luxury")],
        ["WEAPON_COMBATPDW"] = [DefaultClip(0), ExtendedClip(1), Drum(2), Light(3), Sight(4), Grip(5)],
        ["WEAPON_MACHINEPISTOL"] = [DefaultClip(0), ExtendedClip(1), Drum(2), Suppressor(3)],
        ["WEAPON_MG"] = [DefaultClip(0), ExtendedClip(1), Sight(2), Grip(3), Paint(4, "Yusuf Amir Luxury")],
        ["WEAPON_COMBATMG"] = [DefaultClip(1), ExtendedClip(2), Grip(3), Sight(4), Paint(5, "Etched Gun Metal")],
        ["WEAPON_GUSENBERG"] = [DefaultClip(0), ExtendedClip(1)],

        ["WEAPON_ASSAULTRIFLE"] = [DefaultClip(1), ExtendedClip(2), Drum(3), Grip(4), Light(5), Sight(6), Suppressor(7), Paint(8, "Yusuf Amir Luxury")],
        ["WEAPON_CARBINERIFLE"] = [DefaultClip(1), ExtendedClip(2), new(3, "Box magazine", Clip), new(4, RailCover), Grip(5), Light(6), Sight(7), Suppressor(8), Paint(9, "Yusuf Amir Luxury")],
        ["WEAPON_ADVANCEDRIFLE"] = [DefaultClip(0), ExtendedClip(1), Light(2), Sight(3), Suppressor(4), Paint(5, "Gilded Gun Metal")],
        ["WEAPON_SPECIALCARBINE"] = [DefaultClip(0), ExtendedClip(1), Drum(2), Light(3), Sight(4), Suppressor(5), Grip(6), Paint(7, "Etched Gun Metal")],
        ["WEAPON_BULLPUPRIFLE"] = [DefaultClip(0), ExtendedClip(1), Light(2), Sight(3), Suppressor(4), Grip(5), Paint(6, "Gilded Gun Metal")],

        ["WEAPON_PUMPSHOTGUN"] = [Suppressor(0), Light(1), Paint(2, "Yusuf Amir Luxury"), Paint(3, "Security")],
        ["WEAPON_SAWNOFFSHOTGUN"] = [Paint(0, "Gilded Gun Metal")],
        ["WEAPON_ASSAULTSHOTGUN"] = [DefaultClip(0), ExtendedClip(1), Grip(2), Light(3), Suppressor(4)],
        ["WEAPON_BULLPUPSHOTGUN"] = [Grip(0), Light(1), Suppressor(2)],
        ["WEAPON_HEAVYSHOTGUN"] = [DefaultClip(0), ExtendedClip(1), Drum(2), Light(3), Suppressor(4), Grip(5)],

        ["WEAPON_SNIPERRIFLE"] = [Sight(1), Sight(2, "Advanced scope"), Suppressor(3), Paint(4, "Etched Wood Grip")],
        ["WEAPON_HEAVYSNIPER"] = [Sight(2), Sight(3, "Advanced scope")],
        ["WEAPON_MARKSMANRIFLE"] = [DefaultClip(0), ExtendedClip(1), Sight(2), Light(3), Suppressor(4), Grip(5), Paint(6, "Yusuf Amir Luxury")],

        ["WEAPON_GRENADELAUNCHER"] = [Grip(0), Light(1), Sight(2)],

        ["WEAPON_KNUCKLE"] =
        [
            new(0, "The Pimp", Style), new(1, "The Ballas", Style), new(2, "The Hustler", Style), new(3, "The Rock", Style),
            new(4, "The Hater", Style), new(5, "The Lover", Style), new(6, "The Player", Style), new(7, "The King", Style),
            new(8, "The Vagos", Style),
        ],
    };

    public static IReadOnlyList<WeaponAttachment> For(WeaponInfo weapon) => ByWeapon.GetValueOrDefault(weapon.Id) ?? [];
}

/// <summary>Every weapon the editor can show for a save.</summary>
/// <param name="Complete">False when some slot table isn't laid out as in current saves; weapons missing from such a
/// table are not listed, because where the game expects them is unknown.</param>
public sealed class WeaponInventory(IList<CharacterWeapon> weapons, bool complete)
    : System.Collections.ObjectModel.ReadOnlyCollection<CharacterWeapon>(weapons)
{
    public bool Complete { get; } = complete;
}

/// <summary>
/// Weapons the characters carry, stored in the script save data under "WEAPONS": three character records in game order
/// (Michael, Franklin, Trevor — assumed), each with two slot tables (base game, DLC) of fixed weapon slots.
/// A slot is 5 ints in current saves [weapon, ammo, attachments, tint, -1] and 3 in older ones [weapon, ammo, packed].
/// </summary>
public static class WeaponStore
{
    // Field names of the weapon field of slots 0 and 1; the same in every save version seen. The distance between
    // them is the slot size.
    private const uint Slot0WeaponField = 0x6DBFBC23, Slot1WeaponField = 0x7866D171;

    public static WeaponInventory Load(PsoDocument pso)
    {
        var root = pso.FindStruct("WEAPONS") ?? throw new SaveFormatException("This save has no WEAPONS data.");
        var characters = root.Children.Where(c => c.IsStruct).ToList();
        if (characters.Count != StatCatalog.Characters.Length)
            throw new SaveFormatException($"WEAPONS has {characters.Count} character records; expected 3.");

        var list = new List<CharacterWeapon>();
        bool complete = true;
        for (int c = 0; c < characters.Count; c++)
        {
            var tables = characters[c].Children.Where(t => t.IsStruct).Select(t => t.Children).ToList();
            for (int t = 0; t < tables.Count; t++)
            {
                int stride = SlotStride(tables[t]);
                var slots = Enumerable.Range(0, tables[t].Count / stride)
                    .Select(s => (IReadOnlyList<PsoNode>)tables[t].Skip(s * stride).Take(stride).ToList()).ToList();
                var weapons = WeaponCatalog.All.Where(w => w.Table == t && w.Slot < slots.Count).ToList();

                // The slot map was taken from current saves. A table laid out differently (the oldest saves order their
                // DLC weapons differently) keeps the weapons it holds editable, but missing ones can't be placed safely.
                bool standard = weapons.All(w => (uint)slots[w.Slot][0].GetInt32() is 0 || (uint)slots[w.Slot][0].GetInt32() == w.Hash);
                foreach (var w in weapons)
                {
                    var slot = standard ? slots[w.Slot] : slots.FirstOrDefault(s => (uint)s[0].GetInt32() == w.Hash);
                    if (slot != null) list.Add(new CharacterWeapon(StatCatalog.Characters[c], w, slot));
                }
                complete &= standard;
            }
        }
        return new WeaponInventory(list, complete);
    }

    private static int SlotStride(IReadOnlyList<PsoNode> ints)
    {
        int first = -1, second = -1;
        for (int i = 0; i < ints.Count; i++)
        {
            if (ints[i].NameHash == Slot0WeaponField) first = i;
            else if (ints[i].NameHash == Slot1WeaponField) second = i;
        }
        int stride = second - first;
        if (first != 0 || stride < 2 || ints.Count % stride != 0 || ints.Any(i => i.Type != PsoType.SInt))
            throw new SaveFormatException("WEAPONS slot table layout is not recognised.");
        return stride;
    }
}

/// <summary>
/// The weapons the editor can give, with their fixed slots and the full-ammo values seen in a real 100% save.
/// The game's own ids are used; the save stores JOAAT of the lower-case id.
/// </summary>
public static class WeaponCatalog
{
    public const string Melee = "Melee", Pistols = "Pistols", Smgs = "SMGs & MGs", Rifles = "Rifles", Shotguns = "Shotguns",
        Snipers = "Sniper Rifles", Heavy = "Heavy", Thrown = "Thrown", Other = "Other";

    private const int Base = 0, Dlc = 1, Full = 9999;

    public static IReadOnlyList<WeaponInfo> All { get; } =
    [
        new("WEAPON_KNIFE", "Knife", Melee, Base, 1, 0),
        new("WEAPON_NIGHTSTICK", "Nightstick", Melee, Base, 2, 0),
        new("WEAPON_HAMMER", "Hammer", Melee, Base, 3, 0),
        new("WEAPON_BAT", "Baseball Bat", Melee, Base, 4, 0),
        new("WEAPON_CROWBAR", "Crowbar", Melee, Base, 5, 0),
        new("WEAPON_GOLFCLUB", "Golf Club", Melee, Base, 6, 0),
        new("WEAPON_BOTTLE", "Broken Bottle", Melee, Dlc, 0, 0),
        new("WEAPON_DAGGER", "Antique Cavalry Dagger", Melee, Dlc, 6, 0),
        new("WEAPON_KNUCKLE", "Brass Knuckles", Melee, Dlc, 15, 0),
        new("WEAPON_MACHETE", "Machete", Melee, Dlc, 17, 0),
        new("WEAPON_HATCHET", "Hatchet", Melee, Dlc, 19, 0),

        new("WEAPON_PISTOL", "Pistol", Pistols, Base, 8, Full),
        new("WEAPON_COMBATPISTOL", "Combat Pistol", Pistols, Base, 9, Full),
        new("WEAPON_APPISTOL", "AP Pistol", Pistols, Base, 10, Full),
        new("WEAPON_PISTOL50", "Pistol .50", Pistols, Base, 38, Full),
        new("WEAPON_SNSPISTOL", "SNS Pistol", Pistols, Dlc, 1, Full),
        new("WEAPON_HEAVYPISTOL", "Heavy Pistol", Pistols, Dlc, 3, Full),
        new("WEAPON_VINTAGEPISTOL", "Vintage Pistol", Pistols, Dlc, 7, Full),
        new("WEAPON_MARKSMANPISTOL", "Marksman Pistol", Pistols, Dlc, 16, Full),
        new("WEAPON_STUNGUN", "Stun Gun", Pistols, Base, 7, Full),

        new("WEAPON_MICROSMG", "Micro SMG", Smgs, Base, 11, Full),
        new("WEAPON_SMG", "SMG", Smgs, Base, 12, Full),
        new("WEAPON_ASSAULTSMG", "Assault SMG", Smgs, Base, 36, Full),
        new("WEAPON_COMBATPDW", "Combat PDW", Smgs, Dlc, 14, Full),
        new("WEAPON_MACHINEPISTOL", "Machine Pistol", Smgs, Dlc, 18, Full),
        new("WEAPON_MG", "MG", Smgs, Base, 16, Full),
        new("WEAPON_COMBATMG", "Combat MG", Smgs, Base, 17, Full),
        new("WEAPON_GUSENBERG", "Gusenberg Sweeper", Smgs, Dlc, 2, Full),

        new("WEAPON_ASSAULTRIFLE", "Assault Rifle", Rifles, Base, 13, Full),
        new("WEAPON_CARBINERIFLE", "Carbine Rifle", Rifles, Base, 14, Full),
        new("WEAPON_ADVANCEDRIFLE", "Advanced Rifle", Rifles, Base, 15, Full),
        new("WEAPON_SPECIALCARBINE", "Special Carbine", Rifles, Dlc, 4, Full),
        new("WEAPON_BULLPUPRIFLE", "Bullpup Rifle", Rifles, Dlc, 5, Full),

        new("WEAPON_PUMPSHOTGUN", "Pump Shotgun", Shotguns, Base, 18, Full),
        new("WEAPON_SAWNOFFSHOTGUN", "Sawed-Off Shotgun", Shotguns, Base, 19, Full),
        new("WEAPON_ASSAULTSHOTGUN", "Assault Shotgun", Shotguns, Base, 20, Full),
        new("WEAPON_BULLPUPSHOTGUN", "Bullpup Shotgun", Shotguns, Base, 37, Full),
        new("WEAPON_HEAVYSHOTGUN", "Heavy Shotgun", Shotguns, Dlc, 10, Full),
        new("WEAPON_MUSKET", "Musket", Shotguns, Dlc, 9, Full),

        new("WEAPON_SNIPERRIFLE", "Sniper Rifle", Snipers, Base, 21, Full),
        new("WEAPON_HEAVYSNIPER", "Heavy Sniper", Snipers, Base, 22, Full),
        new("WEAPON_MARKSMANRIFLE", "Marksman Rifle", Snipers, Dlc, 11, Full),

        new("WEAPON_GRENADELAUNCHER", "Grenade Launcher", Heavy, Base, 23, 20),
        new("WEAPON_RPG", "RPG", Heavy, Base, 24, 20),
        new("WEAPON_MINIGUN", "Minigun", Heavy, Base, 25, Full),
        new("WEAPON_FIREWORK", "Firework Launcher", Heavy, Dlc, 8, 20),
        new("WEAPON_HOMINGLAUNCHER", "Homing Launcher", Heavy, Dlc, 13, 10),
        new("WEAPON_RAILGUN", "Railgun", Heavy, Dlc, 20, 20),

        new("WEAPON_GRENADE", "Grenade", Thrown, Base, 26, 25),
        new("WEAPON_STICKYBOMB", "Sticky Bomb", Thrown, Base, 27, 25),
        new("WEAPON_SMOKEGRENADE", "Tear Gas", Thrown, Base, 28, 25),
        new("WEAPON_MOLOTOV", "Molotov Cocktail", Thrown, Base, 29, 25),
        new("WEAPON_PROXMINE", "Proximity Mine", Thrown, Dlc, 12, 5),

        new("WEAPON_PETROLCAN", "Jerry Can", Other, Base, 31, 4500),
        new("GADGET_PARACHUTE", "Parachute", Other, Base, 33, 1),
    ];

    public static readonly uint Unarmed = GtaHash.Joaat("WEAPON_UNARMED");
}
