using System.Buffers.Binary;

namespace SaveEditor.GtaV;

/// <summary>A choice in one of the customisation lists: the number the save stores and its name.</summary>
public sealed record LookOption(int Value, string Name)
{
    public override string ToString() => Name;
}

/// <summary>A light colour (tyre smoke or neon) with the name the mod shop gives it.</summary>
public sealed record LightColour(string Name, byte R, byte G, byte B)
{
    public static IReadOnlyList<LightColour> TyreSmoke { get; } =
    [
        new("White", 254, 254, 254), new("Black", 1, 1, 1), new("Blue", 0, 174, 239), new("Yellow", 252, 238, 0),
        new("Purple", 132, 102, 226), new("Orange", 255, 127, 0), new("Green", 114, 204, 114), new("Red", 226, 6, 6),
        new("Pink", 203, 54, 148), new("Brown", 180, 130, 97),
    ];

    public static IReadOnlyList<LightColour> Neon { get; } =
    [
        new("White", 222, 222, 255), new("Blue", 2, 21, 255), new("Electric blue", 3, 83, 255), new("Mint green", 0, 255, 140),
        new("Lime green", 94, 255, 1), new("Yellow", 255, 255, 0), new("Golden shower", 255, 150, 0), new("Orange", 255, 62, 0),
        new("Red", 255, 1, 1), new("Pony pink", 255, 50, 100), new("Hot pink", 255, 5, 190), new("Purple", 35, 1, 255),
        new("Blacklight", 15, 3, 255),
    ];

    /// <summary>The colour as a #RRGGBB string, for a swatch.</summary>
    public string Hex => $"{R:X2}{G:X2}{B:X2}";

    public override string ToString() => Name;
}

/// <summary>
/// The look and upgrades of a car parked in a safehouse garage: paint, window tint, lights, wheels and the
/// performance parts. Everything is stored in the vehicle's look struct; mod parts live in a map of
/// "vmt_&lt;part&gt;" → value (255 = stock) that the game fills with all 50 parts.
/// </summary>
public sealed class VehicleLook
{
    // Byte offsets inside the look struct.
    private const int ModsPointer = 8, ModsCount = 16, TyreVariation = 24, PaintAt = 27, SmokeAt = 33, NeonAt = 36,
        NeonSidesAt = 39, TintAt = 40, WheelTypeAt = 41, KitAt = 42;

    private const byte Stock = 255;
    private const ushort NoKit = 0xFFFF;

    // Parts that aren't picked from a list of options: the four upgrades, the on/off extras, wheels and horns.
    private static readonly HashSet<string> NotBodyParts =
    [
        "engine", "brakes", "gearbox", "armour", "horn", "suspension", "nitrous", "turbo", "subwoofer", "tyre_smoke", "hydraulics",
        "xenon_lights", "wheels", "wheels_rear", "livery_mod", "lightbar",
    ];

    private readonly byte[] _data;
    private readonly int _at;
    private readonly Func<uint> _model;
    private readonly Dictionary<string, int> _mods = [];

    internal VehicleLook(PsoNode look, Func<uint> model)
    {
        _data = look.Document.Data;
        _at = look.Offset;
        _model = model;
        if (look.Document.ResolvePointer(_at + ModsPointer) is not { } start) return;
        int count = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_at + ModsCount));
        var byKey = Parts.ToDictionary(p => GtaHash.Joaat("vmt_" + p), p => p);
        for (int i = 0; i < count && start + 8 * i + 5 <= _data.Length; i++)
            if (byKey.TryGetValue(BinaryPrimitives.ReadUInt32BigEndian(_data.AsSpan(start + 8 * i)), out var part))
                _mods[part] = start + 8 * i + 4;
    }

    /// <summary>The 50 mod parts the game saves, as named in its scripts (lower case, without "vmt_").</summary>
    public static IReadOnlyList<string> Parts { get; } =
    [
        "spoiler", "bumper_f", "bumper_r", "skirt", "exhaust", "chassis", "grill", "bonnet", "wing_l", "wing_r", "roof",
        "engine", "brakes", "gearbox", "horn", "suspension", "armour", "nitrous", "turbo", "subwoofer", "tyre_smoke",
        "hydraulics", "xenon_lights", "wheels", "wheels_rear", "pltholder", "pltvanity", "interior1", "interior2",
        "interior3", "interior4", "interior5", "seats", "steering", "knob", "plaque", "ice", "trunk", "hydro",
        "enginebay1", "enginebay2", "enginebay3", "chassis2", "chassis3", "chassis4", "chassis5", "door_l", "door_r",
        "livery_mod", "lightbar",
    ];

    /// <summary>What's known about the car's model, or null for a model the editor has no data for.</summary>
    public VehicleInfo? Info => VehicleData.Find(_model());

    // ---- Paint ----

    public int PrimaryColour { get => Byte(PaintAt); set => SetByte(PaintAt, value); }
    public int SecondaryColour { get => Byte(PaintAt + 1); set => SetByte(PaintAt + 1, value); }
    public int PearlescentColour { get => Byte(PaintAt + 2); set => SetByte(PaintAt + 2, value); }
    public int WheelColour { get => Byte(PaintAt + 3); set => SetByte(PaintAt + 3, value); }
    public int InteriorColour { get => Byte(PaintAt + 4); set => SetByte(PaintAt + 4, value); }
    public int DashboardColour { get => Byte(PaintAt + 5); set => SetByte(PaintAt + 5, value); }

    // ---- Windows and lights ----

    public static IReadOnlyList<LookOption> WindowTints { get; } =
    [
        new(Stock, "Factory windows"), new(0, "No tint"), new(3, "Light smoke"), new(2, "Dark smoke"), new(5, "Limo"),
        new(1, "Pure black"), new(6, "Green"), new(4, "Stock tint"),
    ];

    public int WindowTint { get => Byte(TintAt); set => SetByte(TintAt, value); }

    public (byte R, byte G, byte B) TyreSmokeColour
    {
        get => (_data[_at + SmokeAt], _data[_at + SmokeAt + 1], _data[_at + SmokeAt + 2]);
        set => (_data[_at + SmokeAt], _data[_at + SmokeAt + 1], _data[_at + SmokeAt + 2]) = value;
    }

    public (byte R, byte G, byte B) NeonColour
    {
        get => (_data[_at + NeonAt], _data[_at + NeonAt + 1], _data[_at + NeonAt + 2]);
        set => (_data[_at + NeonAt], _data[_at + NeonAt + 1], _data[_at + NeonAt + 2]) = value;
    }

    public bool NeonLeft { get => NeonSide(0); set => SetNeonSide(0, value); }
    public bool NeonRight { get => NeonSide(1); set => SetNeonSide(1, value); }
    public bool NeonFront { get => NeonSide(2); set => SetNeonSide(2, value); }
    public bool NeonBack { get => NeonSide(3); set => SetNeonSide(3, value); }

    // ---- Wheels ----

    public static IReadOnlyList<LookOption> WheelTypes { get; } =
    [
        new(0, "Sport"), new(1, "Muscle"), new(2, "Lowrider"), new(3, "SUV"), new(4, "Off-road"), new(5, "Tuner"),
        new(6, "Bike"), new(7, "High end"), new(8, "Benny's originals"), new(9, "Benny's bespoke"), new(10, "Open wheel"),
        new(11, "Street"), new(12, "Track"),
    ];

    // The rims the game lists first for each wheel type, in its order; later designs are newer and aren't named here.
    private static readonly string[][] WheelNames =
    [
        ["Inferno", "Deep Five", "Lozspeed Mk.V", "Diamond Cut", "Chrono", "Feroci RR", "FiftyNine", "Mercie", "Synthetic Z",
            "Organic Type 0", "Endo v.1", "GT One", "Duper 7", "Uzer", "GroundRide", "S Racer", "Venum", "Cosmo", "Dash VIP",
            "Ice Kid", "Ruff Weld", "Wangan Master", "Super Five", "Endo v.2", "Split Six"],
        ["Classic Five", "Dukes", "Muscle Freak", "Kracka", "Azreal", "Mecha", "Black Top", "Drag SPL", "Revolver",
            "Classic Rod", "Fairlie", "Spooner", "Five Star", "Old School", "El Jefe", "Dodman", "Six Gun", "Mercenary"],
        ["Flare", "Wired", "Triple Golds", "Big Worm", "Seven Fives", "Split Six", "Fresh Mesh", "Lead Sled", "Turbine",
            "Super Fin", "Classic Rod", "Dollar", "Dukes", "Low Five", "Gooch"],
        ["VIP", "Benefactor", "Cosmo", "Bippu", "Royal Six", "Fagorme", "Deluxe", "Iced Out", "Cognoscenti", "LozSpeed Ten",
            "Supernova", "Obey RS", "LozSpeed Baller", "Extravaganzo", "Split Six", "Empowered", "Sunrise", "Dash VIP", "Cutter"],
        ["Raider", "Mudslinger", "Nevis", "Cairngorm", "Amazon", "Challenger", "Dune Basher", "Five Star", "Rock Crawler",
            "Mil Spec Steelie"],
        ["Cosmo", "Super Mesh", "Outsider", "Rollas", "Driftmeister", "Slicer", "El Quatro", "Dubbed", "Five Star",
            "Slideways", "Apex", "Stanced EG", "Countersteer", "Endo v.1", "Endo v.2 Dish", "Gruppe Z", "Choku-Dori", "Chicane",
            "Saisoku", "Dished Eight", "Fujiwara", "Zokusha", "Battle VIII", "Rally Master"],
        ["Speedway", "Street Special", "Racer", "Track Star", "Overlord", "Trident", "Triple Threat", "Stilleto", "Wires",
            "Bobber", "Solidus", "Ice Shield", "Loops"],
        ["Shadow", "Hypher", "Blade", "Diamond", "Supa Gee", "Chromatic Z", "Mercie Ch.Lip", "Obey RS", "GT Chrome",
            "Cheetah RR", "Solar", "Split Ten", "Dash VIP", "LozSpeed Ten", "Carbon Inferno", "Carbon Shadow", "Carbon Z",
            "Carbon Solar", "Cheetah Carbon R", "Carbon S Racer"],
    ];

    // How many designs each wheel type has in the game's artwork, for the types whose newer designs aren't named.
    private static readonly Dictionary<int, int> WheelCounts = new()
    {
        [4] = 35, [6] = 48, [8] = 31, [9] = 31, [10] = 20, [11] = 30, [12] = 30,
    };

    /// <summary>The rim designs for a wheel type, starting with the car's own wheels.</summary>
    public static IReadOnlyList<LookOption> WheelDesigns(int wheelType)
    {
        var named = wheelType is >= 0 and < 8 ? WheelNames[wheelType] : [];
        int total = Math.Max(named.Length, WheelCounts.GetValueOrDefault(wheelType));
        return [new(Stock, "Stock wheels"), .. Enumerable.Range(0, total).Select(i => new LookOption(i, i < named.Length ? named[i] : $"Design {i + 1}"))];
    }

    /// <summary>The design's name when the game's order for it is known, so a picture can be shown; otherwise null.</summary>
    public static string? NamedWheel(int wheelType, int design) =>
        wheelType is >= 0 and < 8 && design >= 0 && design < WheelNames[wheelType].Length ? WheelNames[wheelType][design] : null;

    public bool IsBike => Info?.IsBike == true || WheelType == 6;

    /// <summary>The wheel types this vehicle can take: bike wheels for bikes, the rest for cars.</summary>
    public IReadOnlyList<LookOption> AllowedWheelTypes => WheelTypes.Where(t => (t.Value == 6) == IsBike).ToList();

    public int WheelType { get => Byte(WheelTypeAt); set => SetByte(WheelTypeAt, value); }

    /// <summary>The rim design (255 = the car's own); bikes get the same design front and back.</summary>
    public int WheelDesign
    {
        get => Mod("wheels");
        set
        {
            SetMod("wheels", value);
            if (IsBike) SetMod("wheels_rear", value);
        }
    }

    /// <summary>Custom (whitewall/racing) tyres, front and back.</summary>
    public bool CustomTyres
    {
        get => _data[_at + TyreVariation] != 0;
        set => _data[_at + TyreVariation] = _data[_at + TyreVariation + 1] = (byte)(value ? 1 : 0);
    }

    // ---- Mod shop parts ----

    /// <summary>The car's mod kit; cars that never visited a mod shop have none and keep their stock parts.</summary>
    public int ModKit => BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(_at + KitAt));

    /// <summary>True when wheels, lights and other mod shop parts can be changed.</summary>
    public bool HasModKit => ModKit != NoKit && _mods.Count == Parts.Count;

    // The four upgrades: how many levels each has and where the first level sits in the car's kit. Where the car's own
    // starts are known (from its model) they are used; otherwise the layout most kits share. The stat parts (engine, brakes,
    // transmission, armour, suspension, horns) share one list, so an upgrade never has more levels than fit before the part
    // that starts next.
    private (string Part, int Levels, int Start)[] Upgrades()
    {
        (string Part, int Levels, int Start)[] usual =
        [
            ("engine", 4, Info?.EngineStart ?? 0), ("brakes", 3, Info?.BrakesStart ?? 4),
            ("gearbox", 3, Info?.TransmissionStart ?? 7), ("armour", 5, Info?.ArmourStart ?? 10),
        ];
        return usual.Select(u => (u.Part, Math.Clamp(RoomAfter(u.Start), 0, u.Levels), u.Start)).ToArray();
    }

    // How many values fit from a stat part's first value to the start of the next stat part.
    private int RoomAfter(int start) =>
        Info is not { } i ? int.MaxValue
            : new[] { i.EngineStart, i.BrakesStart, i.TransmissionStart, i.ArmourStart, i.SuspensionStart, i.HornStart }
                .Where(s => s > start).DefaultIfEmpty(int.MaxValue).Min() is var next && next == int.MaxValue ? int.MaxValue : next - start;

    /// <summary>How many levels (after stock) the car has of engine, brakes, transmission, armour and suspension.</summary>
    public int EngineLevelCount => Upgrades()[0].Levels;
    public int BrakeLevelCount => Upgrades()[1].Levels;
    public int TransmissionLevelCount => Upgrades()[2].Levels;
    public int ArmourLevelCount => Upgrades()[3].Levels;
    public int SuspensionLevelCount => SuspensionUpgrade?.Levels ?? 0;

    private bool Offers((string Part, int Levels, int Start) u) =>
        HasModKit && u.Start >= 0 && u.Levels > 0 && Mod(u.Part) is var v && (v == Stock || (v >= u.Start && v < u.Start + u.Levels));

    /// <summary>
    /// True when at least one of engine, brakes, transmission and armour can be changed. They are stored as positions in
    /// the car's own kit, so each one is only offered where its saved value fits the kit's layout for this model.
    /// </summary>
    public bool CanUpgrade => Upgrades().Any(Offers);

    public bool HasEngine => Offers(Upgrades()[0]);
    public bool HasBrakes => Offers(Upgrades()[1]);
    public bool HasTransmission => Offers(Upgrades()[2]);
    public bool HasArmour => Offers(Upgrades()[3]);

    public static IReadOnlyList<LookOption> EngineLevels { get; } =
        [new(0, "Stock"), new(1, "EMS upgrade, level 1"), new(2, "EMS upgrade, level 2"), new(3, "EMS upgrade, level 3"), new(4, "EMS upgrade, level 4")];
    public static IReadOnlyList<LookOption> BrakeLevels { get; } = [new(0, "Stock"), new(1, "Street"), new(2, "Sport"), new(3, "Race")];
    public static IReadOnlyList<LookOption> TransmissionLevels { get; } = [new(0, "Stock"), new(1, "Street"), new(2, "Sports"), new(3, "Race")];
    public static IReadOnlyList<LookOption> ArmourLevels { get; } =
        [new(0, "None"), new(1, "20%"), new(2, "40%"), new(3, "60%"), new(4, "80%"), new(5, "100%")];
    public static IReadOnlyList<LookOption> SuspensionLevels { get; } =
        [new(0, "Stock"), new(1, "Lowered"), new(2, "Street"), new(3, "Sport"), new(4, "Competition")];

    public int Engine { get => Level(0); set => SetLevel(0, value); }
    public int Brakes { get => Level(1); set => SetLevel(1, value); }
    public int Transmission { get => Level(2); set => SetLevel(2, value); }
    public int Armour { get => Level(3); set => SetLevel(3, value); }
    public bool Turbo { get => Mod("turbo") != Stock; set => SetMod("turbo", value ? 1 : Stock); }
    public bool XenonLights { get => Mod("xenon_lights") != Stock; set => SetMod("xenon_lights", value ? 1 : Stock); }

    /// <summary>Coloured tyre smoke; the colour is <see cref="TyreSmokeColour"/>.</summary>
    public bool TyreSmoke { get => Mod("tyre_smoke") != Stock; set => SetMod("tyre_smoke", value ? 1 : Stock); }

    /// <summary>Best engine, brakes, transmission and armour the car offers, plus turbo.</summary>
    public void MaxPerformance()
    {
        if (!CanUpgrade) throw new InvalidOperationException("This car's upgrades can't be changed by the editor.");
        var all = Upgrades();
        for (int i = 0; i < all.Length; i++)
            if (Offers(all[i])) SetLevel(i, all[i].Levels);
        Turbo = true;
    }

    // Suspension has four levels after stock. A kit that squeezes it in before the next part has fewer, and the names of
    // those aren't known, so only the usual four-level suspension is offered.
    private (string Part, int Levels, int Start)? SuspensionUpgrade =>
        Info is { SuspensionStart: >= 0 } i && RoomAfter(i.SuspensionStart) >= 4 ? ("suspension", 4, i.SuspensionStart) : null;

    public bool HasSuspension => SuspensionUpgrade is { } s && Offers(s);

    public int Suspension
    {
        get => SuspensionUpgrade is { } s && Mod("suspension") is var v && v != Stock ? v - s.Start + 1 : 0;
        set
        {
            if (!HasSuspension) throw new InvalidOperationException("This car's suspension can't be changed by the editor.");
            SetMod("suspension", Math.Clamp(value, 0, 4) == 0 ? Stock : SuspensionUpgrade!.Value.Start + Math.Clamp(value, 0, 4) - 1);
        }
    }

    // ---- Body parts and liveries (lists known per model) ----

    /// <summary>The parts this car has named options for (spoilers, bumpers and so on) that the editor can offer.</summary>
    public IReadOnlyList<PartOptions> BodyParts =>
        HasModKit ? Info?.Parts.Where(p => p.Type < Parts.Count && !NotBodyParts.Contains(Parts[p.Type]) && PartFits(p)).ToList() ?? [] : [];

    private bool PartFits(PartOptions p) => Mod(Parts[p.Type]) is var v && (v == Stock || p.IndexOf(v) >= 0);

    /// <summary>The option fitted for a part: its position in <see cref="PartOptions.Names"/>, or -1 for stock.</summary>
    public int PartOption(PartOptions part) => part.IndexOf(Mod(Parts[part.Type]));

    public void SetPart(PartOptions part, int option)
    {
        if (!PartFits(part)) throw new InvalidOperationException($"The {part.Label} of this car can't be changed by the editor.");
        SetMod(Parts[part.Type], option < 0 || option >= part.Names.Count ? Stock : part.First + option);
    }

    /// <summary>The liveries this car can wear (the game's own list, without "none").</summary>
    public IReadOnlyList<string> Liveries => HasModKit ? Info?.Liveries ?? [] : [];

    /// <summary>The livery's position in <see cref="Liveries"/>, or -1 for none.</summary>
    public int Livery
    {
        get => Mod("livery_mod") is var v && v != Stock && Info is { } i && v >= i.LiveryStart && v < i.LiveryStart + Liveries.Count ? v - i.LiveryStart : -1;
        set
        {
            if (Liveries.Count == 0 || Info is not { } i) throw new InvalidOperationException("This car has no liveries the editor knows.");
            SetMod("livery_mod", value < 0 || value >= Liveries.Count ? Stock : i.LiveryStart + value);
        }
    }

    /// <summary>Puts every body part, the livery and the wheels back to the car's own.</summary>
    public void ResetBodyParts()
    {
        foreach (var part in Parts.Where(p => !NotBodyParts.Contains(p))) if (_mods.TryGetValue(part, out var at)) _data[at] = Stock;
        foreach (var part in new[] { "livery_mod", "lightbar", "wheels", "wheels_rear" }) if (_mods.TryGetValue(part, out var at)) _data[at] = Stock;
    }

    /// <summary>Raw value of a mod part (255 = stock).</summary>
    public int Mod(string part) => _mods.TryGetValue(part, out var at) ? _data[at] : Stock;

    // ---- Snapshot (change review) ----

    /// <summary>The look's bytes and its parts, for putting the car's look back as it was.</summary>
    public byte[] Snapshot() =>
        [.. _data.AsSpan(_at, KitAt + 2).ToArray(), .. Parts.Select(p => (byte)Mod(p))];

    public void Restore(byte[] snapshot)
    {
        // The mods pointer and count stay as they are; only the values are put back.
        snapshot.AsSpan(TyreVariation, KitAt + 2 - TyreVariation).CopyTo(_data.AsSpan(_at + TyreVariation));
        for (int i = 0; i < Parts.Count; i++)
            if (_mods.TryGetValue(Parts[i], out var at)) _data[at] = snapshot[KitAt + 2 + i];
    }

    /// <summary>A short description for the change review.</summary>
    public string Describe()
    {
        var parts = new List<string> { VehicleColour.NameOf(PrimaryColour) };
        if (SecondaryColour != PrimaryColour) parts[0] += " and " + VehicleColour.NameOf(SecondaryColour);
        if (WindowTint is not (Stock or 0 or 4)) parts.Add(WindowTints.FirstOrDefault(t => t.Value == WindowTint)?.Name ?? "tinted windows");
        if (NeonLeft || NeonRight || NeonFront || NeonBack) parts.Add("neon");
        if (HasModKit && WheelDesign != Stock) parts.Add(WheelDesigns(WheelType).FirstOrDefault(w => w.Value == WheelDesign)?.Name + " wheels");
        if (Livery >= 0) parts.Add(Liveries[Livery] + " livery");
        if (FullyUpgraded) parts.Add("fully upgraded");
        return string.Join(", ", parts);
    }

    private bool FullyUpgraded
    {
        get
        {
            var all = Upgrades();
            bool any = false;
            for (int i = 0; i < all.Length; i++)
            {
                if (!Offers(all[i])) continue;
                any = true;
                if (Level(i) != all[i].Levels) return false;
            }
            return any && Turbo;
        }
    }
    // ---- Garage helpers ----

    /// <summary>Puts every part back to stock and removes custom tyres.</summary>
    internal void ResetParts()
    {
        foreach (var at in _mods.Values) _data[at] = Stock;
        CustomTyres = false;
    }

    internal void SetModKit(int kit) => BinaryPrimitives.WriteUInt16BigEndian(_data.AsSpan(_at + KitAt), (ushort)kit);

    private void SetMod(string part, int value)
    {
        if (!_mods.TryGetValue(part, out var at)) throw new InvalidOperationException("This car's parts can't be changed by the editor.");
        _data[at] = (byte)value;
    }

    private int Level(int upgrade) => Upgrades()[upgrade] is var u && Mod(u.Part) is var v && v != Stock && v >= u.Start ? v - u.Start + 1 : 0;

    private void SetLevel(int upgrade, int level)
    {
        var u = Upgrades()[upgrade];
        if (!Offers(u)) throw new InvalidOperationException("This car's upgrades can't be changed by the editor.");
        level = Math.Clamp(level, 0, u.Levels);
        SetMod(u.Part, level == 0 ? Stock : u.Start + level - 1);
    }

    private bool NeonSide(int bit) => (_data[_at + NeonSidesAt] & (1 << bit)) != 0;

    private void SetNeonSide(int bit, bool on) =>
        _data[_at + NeonSidesAt] = (byte)(on ? _data[_at + NeonSidesAt] | (1 << bit) : _data[_at + NeonSidesAt] & ~(1 << bit));

    private int Byte(int offset) => _data[_at + offset];
    private void SetByte(int offset, int value) => _data[_at + offset] = (byte)Math.Clamp(value, 0, 255);
}
