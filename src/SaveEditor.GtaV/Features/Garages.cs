using System.Globalization;

namespace SaveEditor.GtaV;

/// <summary>A paint colour the editor offers, with the game's colour number and its colour for a swatch.</summary>
public sealed record VehicleColour(int Index, string Name, string Hex = "808080")
{
    /// <summary>Every paint the game has, by colour number.</summary>
    public static IReadOnlyList<VehicleColour> Full { get; } = Load();

    /// <summary>A short list of common paints for parking a new car.</summary>
    public static IReadOnlyList<VehicleColour> All { get; } =
        new[] { 0, 4, 111, 1, 27, 28, 38, 89, 92, 49, 64, 70, 71, 135 }.Select(i => Full[i]).ToList();

    /// <summary>The paint's name, or its number for paints the list doesn't have.</summary>
    public static string NameOf(int index) => index >= 0 && index < Full.Count ? Full[index].Name : $"Paint no. {index}";

    public static string HexOf(int index) => index >= 0 && index < Full.Count ? Full[index].Hex : "808080";

    private static List<VehicleColour> Load()
    {
        using var stream = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("SaveEditor.GtaV.Paints.txt")
                           ?? throw new InvalidOperationException("The paint list is missing.");
        using var reader = new StreamReader(stream);
        var list = new List<VehicleColour>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var f = line.Split('|');
            // Everyday paints say how they finish ("Black (Metallic)"); the rest have their own names.
            var name = f[1] is "Metallic" or "Matte" or "Util" or "Worn" ? $"{f[2]} ({f[1]})" : f[2];
            list.Add(new VehicleColour(int.Parse(f[0], CultureInfo.InvariantCulture), name, f[3]));
        }
        return list;
    }

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
        PlateText = RandomPlate();
        // Stock parts, the model's own mod kit (cars the game gives no kit keep their factory parts), and the wheel
        // type that fits the vehicle; the game's own saves store 255 for "factory windows" and 156 for alloy wheels.
        var look = Node(Look);
        foreach (var (h, v) in new (uint, long)[]
                 {
                     (Primary, colour.Index), (Secondary, colour.Index), (Pearl, colour.Index), (Wheels, 156),
                     (SmokeR, 255), (SmokeG, 255), (SmokeB, 255), (LookE2E8, 255), (LookA2E9, DefaultWheelType(vehicle)),
                     (Look87A3, VehicleData.Find(vehicle.Hash)?.Kit ?? 65535),
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
        CarLook.ResetParts();
        _entry.Document.Data.AsSpan(_entry.Offset + PlateAt, PlateLength).Clear();
    }

    private VehicleLook? _look;

    /// <summary>Paint, lights, wheels and upgrades of the vehicle parked here.</summary>
    public VehicleLook CarLook => _look ??= new VehicleLook(Node(Look), () => ModelHash);

    private static int DefaultWheelType(VehicleModel vehicle) => (VehicleData.Find(vehicle.Hash)?.Class ?? vehicle.Class) switch
    {
        "Motorcycles" => 6,
        "Super" => 7,
        "Muscle" => 1,
        "SUVs" or "SUVs and off-road" => 3,
        "Off-Road" => 4,
        "Open Wheel" => 10,
        _ => 0,
    };

    // ---- Number plate ----

    // The plate is 8 characters inside the vehicle entry, padded with spaces; the style is the entry's small count field.
    private const int PlateAt = 98, PlateLength = 8;

    /// <summary>The plate styles the game has, by the number the save stores.</summary>
    public static IReadOnlyList<LookOption> PlateStyles { get; } =
    [
        new(0, "Blue on white 1"), new(1, "Yellow on black"), new(2, "Yellow on blue"), new(3, "Blue on white 2"),
        new(4, "Blue on white 3"), new(5, "North Yankton"), new(6, "eCola"), new(7, "Las Venturas"), new(8, "Liberty City"),
        new(9, "Los Santos Car Meet"), new(10, "Panic"), new(11, "Pounders"), new(12, "Sprunk"),
    ];

    public const string PlateCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 ";

    /// <summary>The plate's text without its padding.</summary>
    public string PlateText
    {
        get => System.Text.Encoding.ASCII.GetString(_entry.Document.Data, _entry.Offset + PlateAt, PlateLength).Replace("\0", "").Trim();
        set
        {
            var text = (value ?? "").ToUpperInvariant();
            if (text.Length > PlateLength || text.Any(c => !PlateCharacters.Contains(c)))
                throw new ArgumentException("A plate has up to 8 letters, digits or spaces.");
            var bytes = System.Text.Encoding.ASCII.GetBytes(text.PadRight(PlateLength));
            bytes.CopyTo(_entry.Document.Data.AsSpan(_entry.Offset + PlateAt, PlateLength));
        }
    }

    /// <summary>The plate style (see <see cref="PlateStyles"/>).</summary>
    public int PlateStyle
    {
        get => int.Parse(Node(Count).ValueText!, CultureInfo.InvariantCulture);
        set => Set(Count, Math.Clamp(value, 0, sbyte.MaxValue));
    }

    /// <summary>A plate in the shape the game gives its own cars: two digits, three letters, three digits.</summary>
    public static string RandomPlate()
    {
        var r = Random.Shared;
        return $"{r.Next(10)}{r.Next(10)}{(char)('A' + r.Next(26))}{(char)('A' + r.Next(26))}{(char)('A' + r.Next(26))}{r.Next(10)}{r.Next(10)}{r.Next(10)}";
    }

    // ---- Snapshot (change review) ----

    /// <summary>Everything about the car's look and plate, for putting it back as it was.</summary>
    public byte[] SnapshotLook() =>
        [.. CarLook.Snapshot(), .. _entry.Document.Data.AsSpan(_entry.Offset + PlateAt, PlateLength).ToArray(), (byte)PlateStyle];

    public void RestoreLook(byte[] snapshot)
    {
        int n = snapshot.Length - PlateLength - 1;
        CarLook.Restore(snapshot[..n]);
        snapshot.AsSpan(n, PlateLength).CopyTo(_entry.Document.Data.AsSpan(_entry.Offset + PlateAt, PlateLength));
        PlateStyle = snapshot[^1];
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
