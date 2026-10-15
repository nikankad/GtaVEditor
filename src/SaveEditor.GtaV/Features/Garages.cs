using System.Globalization;

namespace SaveEditor.GtaV;

/// <summary>A paint colour the editor offers, with the game's colour number.</summary>
public sealed record VehicleColour(int Index, string Name)
{
    public static IReadOnlyList<VehicleColour> All { get; } =
    [
        new(0, "Black"), new(4, "Silver"), new(111, "White"), new(1, "Graphite"), new(27, "Red"), new(28, "Torino red"),
        new(38, "Orange"), new(89, "Yellow"), new(92, "Lime green"), new(49, "Dark green"), new(64, "Blue"), new(70, "Bright blue"),
        new(71, "Purple"), new(135, "Hot pink"),
    ];

    public override string ToString() => Name;
}

/// <summary>
/// One parking space in a safehouse garage. The game's own garage save (root struct 0x72194FCC) keeps the vehicle
/// parked in each space and puts it back there when the save loads.
/// </summary>
public sealed class GarageSlot
{
    // Fields of a saved vehicle (names unknown; meanings from the user's saves).
    private const uint PosX = 0xDC34682B, PosY = 0x5D62EA92, PosZ = 0x48A1C110, Model = 0x55F7497A, DirX = 0x084DB6A8,
        DirY = 0xE86E76CE, Health1 = 0x4C2AA15F, Health2 = 0xA8F45122, InUse = 0x6531DDC5, InUse2 = 0x23D3A43F, Count = 0x168E3FEC,
        Unknown2EFC = 0x2EFCB9E1, UnknownFB04 = 0xFB0454C0, UnknownB62A = 0xB62A9CB2, Unknown2DF3 = 0x2DF31E34, UnknownB611 = 0xB6119215,
        Look = 0xCFF74A31;

    // Fields of the vehicle's look (paint and extras).
    private const uint Primary = 0x143A8221, Secondary = 0x88466A37, Pearl = 0x7EEF5789, Wheels = 0x6BE0316B,
        SmokeR = 0xE3114EBD, SmokeG = 0xF822F8D8, SmokeB = 0x90BCAA11, LookE2E8 = 0xE2E82803, LookA2E9 = 0xA2E90C68, Look87A3 = 0x87A334CC;

    private readonly PsoNode _entry;
    private readonly (float X, float Y, float Z, int DirX, int DirY)? _spot;

    internal GarageSlot(string garage, string owner, int number, PsoNode entry, (float, float, float, int, int)? spot)
    {
        Garage = garage;
        Owner = owner;
        Number = number;
        _entry = entry;
        _spot = spot;
    }

    public string Garage { get; }

    /// <summary>The character whose safehouse this is.</summary>
    public string Owner { get; }

    /// <summary>1 or 2.</summary>
    public int Number { get; }

    public uint ModelHash => unchecked((uint)long.Parse(Node(Model).ValueText!, CultureInfo.InvariantCulture));
    public bool IsEmpty => ModelHash == 0;
    public string VehicleName => IsEmpty ? "Empty" : VehicleCatalog.NameOf(ModelHash);

    /// <summary>False for a space whose parking position isn't known yet; it can only be emptied.</summary>
    public bool CanPlace => _spot != null;

    /// <summary>Parks a new vehicle here (replacing what's there), undamaged, in one colour.</summary>
    public void Place(VehicleModel vehicle, VehicleColour colour)
    {
        if (_spot is not { } s) throw new InvalidOperationException("This parking space can't be filled yet.");
        Clear();
        Set(PosX, s.X); Set(PosY, s.Y); Set(PosZ, s.Z);
        Set(DirX, s.DirX); Set(DirY, s.DirY);
        Set(Model, vehicle.Hash);
        Set(Health1, 1000); Set(Health2, 1000);
        Set(Unknown2EFC, -1);
        Set(InUse, true); Set(InUse2, true);
        Set(Count, 1);
        var look = Node(Look);
        foreach (var (h, v) in new (uint, long)[]
                 {
                     (Primary, colour.Index), (Secondary, colour.Index), (Pearl, colour.Index), (Wheels, 0),
                     (SmokeR, 20), (SmokeG, 20), (SmokeB, 20), (LookE2E8, 1), (LookA2E9, 7), (Look87A3, 20),
                 })
            SetIn(look, h, v);
    }

    /// <summary>Empties the space, as in a save where nothing was ever parked here.</summary>
    public void Clear()
    {
        foreach (var f in _entry.Children.Where(c => c.IsValue))
            f.SetValue(f.Field!.Type == PsoType.Bool ? "false" : "0");
        Set(UnknownFB04, -1);
        foreach (var f in Node(Look).Children.Where(c => c.IsValue)) f.SetValue("0");
        SetIn(Node(Look), 0x8EA846D0, 255);
        SetIn(Node(Look), Look87A3, 65535);
    }

    private PsoNode Node(uint hash) => _entry.Children.First(c => c.NameHash == hash);
    private void Set(uint hash, object value) => SetIn(_entry, hash, value);

    private static void SetIn(PsoNode parent, uint hash, object value)
    {
        var node = parent.Children.First(c => c.NameHash == hash);
        node.SetValue(value switch
        {
            bool b => b ? "true" : "false",
            float f => f.ToString("R", CultureInfo.InvariantCulture),
            uint u when node.Field!.Type is PsoType.SInt or PsoType.Enum => unchecked((int)u).ToString(CultureInfo.InvariantCulture),
            IFormattable n => n.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString()!,
        });
    }

    internal static bool HasLayout(PsoNode entry) =>
        new[] { PosX, PosY, PosZ, Model, DirX, DirY, Health1, Health2, InUse, InUse2, Count, Unknown2EFC, UnknownFB04, Look }
            .All(h => entry.Children.Any(c => c.NameHash == h))
        && entry.Children.First(c => c.NameHash == Look) is var look
        && new[] { Primary, Secondary, Pearl, Wheels, SmokeR, SmokeG, SmokeB, LookE2E8, LookA2E9, Look87A3, 0x8EA846D0u }
            .All(h => look.Children.Any(c => c.NameHash == h && c.IsValue));
}

/// <summary>The six safehouse garages and their parking spaces.</summary>
public static class Garages
{
    private const uint GarageRoot = 0x72194FCC, GarageList = 0x58DAF7C3, Slots = 0x26533C8A;

    // Garage index → (name, owner, known parking spots). Spots are where the game itself parked cars in the user's saves.
    private static readonly (uint Id, string Name, string Owner, (float, float, float, int, int)?[] Spots)[] Known =
    [
        (360562957, "Michael's house, Rockford Hills", "Michael", [(-811.9601f, 188.97633f, 71.89587f, -92, -38), (-812.112f, 185.49031f, 71.71477f, -91, -40)]),
        (2175093583, "Trevor's trailer, Sandy Shores", "Trevor", [(1968.3551f, 3820.8054f, 31.965816f, 82, 56), (1970.3564f, 3824.8516f, 31.847128f, 96, 25)]),
        (3774828611, "Trevor's apartment, Vespucci Beach", "Trevor", [(-1151.1544f, -1530.3187f, 3.8096159f, 56, -82), null]),
        (1066626361, "Vanilla Unicorn", "Trevor", [(140.88826f, -1279.8905f, 29.040127f, 84, 53), (143.6936f, -1285.0824f, 29.035297f, 85, 52)]),
        (4019785634, "Franklin's aunt's house, Strawberry", "Franklin", [(-26.190565f, -1427.0371f, 30.254105f, -6, -99), (-23.381763f, -1428.9832f, 30.328018f, -10, -99)]),
        (2393745202, "Franklin's house, Vinewood Hills", "Franklin", [(22.773432f, 545.47797f, 175.60344f, -95, 29), (20.689054f, 542.10986f, 175.54683f, -99, -8)]),
    ];

    public static IReadOnlyList<GarageSlot> Load(PsoDocument pso)
    {
        var list = pso.Root.Children.FirstOrDefault(n => n.NameHash == GarageRoot)?.Children.FirstOrDefault(n => n.NameHash == GarageList)
                   ?? throw new SaveFormatException("This save has no garage data.");
        var slots = new List<GarageSlot>();
        for (int g = 0; g < Known.Length; g++)
        {
            // Each garage is a struct of (id, parked vehicles); find it by id.
            var garage = list.Children.FirstOrDefault(n => n.Children.Count == 2 && n.Children[0].IsValue
                && long.TryParse(n.Children[0].ValueText, out var id) && unchecked((uint)id) == Known[g].Id);
            var entries = garage?.Children.FirstOrDefault(n => n.NameHash == Slots);
            if (entries == null || entries.Children.Count != 2 || !entries.Children.All(GarageSlot.HasLayout))
                throw new SaveFormatException("The garage data in this save is not in the expected layout.");
            for (int s = 0; s < 2; s++)
                slots.Add(new GarageSlot(Known[g].Name, Known[g].Owner, s + 1, entries.Children[s], Known[g].Spots[s]));
        }
        return slots;
    }
}
