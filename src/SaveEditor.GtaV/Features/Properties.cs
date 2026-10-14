using System.Globalization;

namespace SaveEditor.GtaV;

/// <summary>
/// A hangar, helipad or boat dock vehicle slot (script data vehicle generators, 0x943A0A56/0xC3630453). The game
/// spawns the vehicle stored here at that character's hangar, helipad or dock. See docs/FINDINGS.md section 18.
/// </summary>
public sealed class PropertyVehicleSlot
{
    // Fields of the vehicle setup (meanings from game-made entries in the user's saves).
    private const uint Model = 0x102CF7E5, Primary = 0xC47A9511, Secondary = 0xF2CB71B2, Pearl = 0x5C531690, Wheels = 0x8EBD7B64,
        SmokeR = 0x5953387A, SmokeG = 0x419A88F1, SmokeB = 0xB04B6651, Livery = 0x2D1E0949, Flags = 0x58DC0971;

    private readonly PsoNode _entry;

    internal PropertyVehicleSlot(string location, string owner, string vehicleClass, PsoNode entry) =>
        (Location, Owner, VehicleClass, _entry) = (location, owner, vehicleClass, entry);

    public string Location { get; }
    public string Owner { get; }

    /// <summary>Planes, Helicopters or Boats (see <see cref="VehicleCatalog"/>).</summary>
    public string VehicleClass { get; }

    public uint ModelHash => unchecked((uint)long.Parse(Node(Model).ValueText!, CultureInfo.InvariantCulture));
    public bool IsEmpty => ModelHash == 0;
    public string VehicleName => IsEmpty ? "Empty" : VehicleCatalog.NameOf(ModelHash);

    /// <summary>Stores a new vehicle here, as the game does when you buy one for this hangar, helipad or dock.</summary>
    public void Place(VehicleModel vehicle, VehicleColour colour)
    {
        Clear();
        foreach (var (h, v) in new (uint, long)[]
                 {
                     (Model, unchecked((int)vehicle.Hash)), (Primary, colour.Index), (Secondary, colour.Index), (Pearl, colour.Index),
                     (Wheels, 156), (SmokeR, 255), (SmokeG, 255), (SmokeB, 255), (Livery, -1), (Flags, 4194304),
                 })
            Node(h).SetValue(v.ToString(CultureInfo.InvariantCulture));
    }

    public void Clear()
    {
        void Zero(PsoNode n)
        {
            foreach (var c in n.Children)
                if (c.IsValue) c.SetValue(c.Field!.Type == PsoType.Bool ? "false" : "0");
                else Zero(c);
        }
        Zero(_entry);
    }

    private PsoNode Node(uint hash) => _entry.Children.First(c => c.NameHash == hash);

    internal static bool HasLayout(PsoNode entry) =>
        new[] { Model, Primary, Secondary, Pearl, Wheels, SmokeR, SmokeG, SmokeB, Livery, Flags }
            .All(h => entry.Children.Any(c => c.NameHash == h && c.IsValue));

    private const uint VehicleData = 0x943A0A56, Slots = 0xC3630453;

    // The first nine slots: hangar, boat dock and helipad for Michael, Franklin and Trevor. Slot 2 holds Trevor's
    // Cuban 800 and slot 8 the TPI Frogger in the story saves; the rest follow the same per-character order.
    private static readonly (string Place, string Owner, string Class)[] Known =
    [
        ("Hangar, Los Santos International", "Michael", VehicleCatalog.Planes),
        ("Hangar, Los Santos International", "Franklin", VehicleCatalog.Planes),
        ("Hangar, Sandy Shores airfield", "Trevor", VehicleCatalog.Planes),
        ("Boat dock, Puerto Del Sol", "Michael", VehicleCatalog.Boats),
        ("Boat dock, Puerto Del Sol", "Franklin", VehicleCatalog.Boats),
        ("Boat dock, Puerto Del Sol", "Trevor", VehicleCatalog.Boats),
        ("Helipad, Vespucci heliport", "Michael", VehicleCatalog.Helicopters),
        ("Helipad, Vespucci heliport", "Franklin", VehicleCatalog.Helicopters),
        ("Helipad, Sandy Shores", "Trevor", VehicleCatalog.Helicopters),
    ];

    public static IReadOnlyList<PropertyVehicleSlot> Load(PsoDocument pso)
    {
        var slots = CompletionOrder.ScriptData(pso).Children.FirstOrDefault(n => n.NameHash == VehicleData)
                        ?.Children.FirstOrDefault(n => n.NameHash == Slots)
                    ?? throw new SaveFormatException("This save has no hangar, helipad or dock data.");
        if (slots.Children.Count < Known.Length || !slots.Children.Take(Known.Length).All(HasLayout))
            throw new SaveFormatException("The hangar, helipad and dock data in this save is not in the expected layout.");
        return Known.Select((k, i) => new PropertyVehicleSlot(k.Place, k.Owner, k.Class, slots.Children[i])).ToList();
    }
}

/// <summary>A business the characters can buy, and who owns it.</summary>
public sealed class Property
{
    public const int NotOwned = 145;

    private const uint Owner = 0xBE27B286, State = 0xAD21F043, Time = 0x9EECB4A5;
    private readonly PsoNode _entry, _now;

    internal Property(string name, string kind, int usual, PsoNode entry, PsoNode now) =>
        (Name, Kind, UsualOwner, _entry, _now) = (name, kind, usual, entry, now);

    /// <summary>A character who can buy this business in the game (0 Michael, 1 Franklin, 2 Trevor).</summary>
    public int UsualOwner { get; }

    public string Name { get; }

    /// <summary>What kind of business it is, e.g. "Bar" or "Cinema".</summary>
    public string Kind { get; }

    /// <summary>0 Michael, 1 Franklin, 2 Trevor, or null when nobody owns it.</summary>
    public int? OwnerIndex
    {
        get => Node(Owner).GetInt32() is var o and >= 0 and <= 2 ? o : null;
        set
        {
            if (value is { } v && v is < 0 or > 2) throw new ArgumentOutOfRangeException(nameof(value));
            Node(Owner).SetInt32(value ?? NotOwned);
            // Bought properties in the user's saves have state 5 and a purchase time; unowned ones have 0.
            Node(State).SetInt32(value == null ? 0 : Node(State).GetInt32() is var s && s != 0 ? s : 5);
            Node(Time).SetInt32(value == null ? 0 : _now.GetInt32());
        }
    }

    private PsoNode Node(uint hash) => _entry.Children.First(c => c.NameHash == hash);

    private const uint PropertyData = 0x38301D26, List = 0xD6B3AB33;

    // Usual owner: who owns it in the user's 100% save (every one a real purchase, so the game accepts that character).
    // Usual owner: who owns it in the user's 100% save (each a real in-game purchase, so the game accepts that character).
    private static readonly (string Field, string Name, string Kind, int Usual)[] Known =
    [
        ("PROPERTY_TOWING_IMPOUND", "Towing Impound", "Towing", 1),
        ("PROPERTY_TAXI_LOT", "Downtown Cab Co.", "Taxi company", 1),
        ("PROPERTY_ARMS_TRAFFICKING", "McKenzie Field Hangar", "Arms trafficking", 2),
        ("PROPERTY_SONAR_COLLECTIONS", "Sonar Collections Dock", "Salvage", 1),
        ("PROPERTY_CAR_MOD_SHOP", "Car mod shop", "Car mod shop", 1),
        ("PROPERTY_CINEMA_VINEWOOD", "Cinema, Vinewood", "Cinema", 0),
        ("PROPERTY_CINEMA_DOWNTOWN", "Cinema, Downtown", "Cinema", 0),
        ("PROPERTY_CINEMA_MORNINGWOOD", "Cinema, Morningwood", "Cinema", 0),
        ("PROPERTY_GOLF_CLUB", "Los Santos Golf Club", "Golf club", 1),
        ("PROPERTY_CAR_SCRAP_YARD", "Car Scrapyard", "Scrapyard", 1),
        ("PROPERTY_WEED_SHOP", "Smoke on the Water", "Weed shop", 1),
        ("PROPERTY_BAR_TEQUILALA", "Tequi-la-la", "Bar", 1),
        ("PROPERTY_BAR_PITCHERS", "Pitchers", "Bar", 1),
        ("PROPERTY_BAR_HEN_HOUSE", "The Hen House", "Bar", 1),
        ("PROPERTY_BAR_HOOKIES", "Hookies", "Bar", 1),
    ];

    public static IReadOnlyList<Property> Load(PsoDocument pso)
    {
        var data = CompletionOrder.ScriptData(pso).Children.FirstOrDefault(n => n.NameHash == PropertyData)
                   ?? throw new SaveFormatException("This save has no property data.");
        var list = data.Children.FirstOrDefault(n => n.NameHash == List);
        var now = data.Children.FirstOrDefault(n => n.NameHash == Time && n.IsValue);
        if (list == null || now == null) throw new SaveFormatException("The property data in this save is not in the expected layout.");
        var result = new List<Property>();
        foreach (var (field, name, kind, usual) in Known)
        {
            var entry = list.Children.FirstOrDefault(n => n.NameHash == PsoNames.Hash(field));
            if (entry == null || !new[] { Owner, State, Time }.All(h => entry.Children.Any(c => c.NameHash == h && c.IsValue)))
                throw new SaveFormatException("The property data in this save is not in the expected layout.");
            result.Add(new Property(name, kind, usual, entry, now));
        }
        return result;
    }
}
