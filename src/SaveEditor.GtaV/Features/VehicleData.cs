using System.Globalization;
using System.Reflection;

namespace SaveEditor.GtaV;

/// <summary>One mod-shop part with its own list of options, such as the spoilers of a car.</summary>
/// <param name="Type">The part's slot number (the position of its "vmt_" name in <see cref="VehicleLook.Parts"/>).</param>
/// <param name="First">The saved value of the first option; the rest follow one by one.</param>
public sealed record PartOptions(int Type, string Label, int First, IReadOnlyList<string> Names)
{
    /// <summary>The option a saved value stands for, or -1 for stock or a value outside this list.</summary>
    public int IndexOf(int value) => value >= First && value < First + Names.Count ? value - First : -1;
}

/// <summary>What is known about a vehicle model: its name, its mod kit and where its upgrades start in that kit.</summary>
public sealed class VehicleInfo
{
    internal VehicleInfo(uint hash, string name, string cls, int kit, int engine, int brakes, int transmission, int armour, int suspension, int horn)
        => (Hash, Name, Class, Kit, EngineStart, BrakesStart, TransmissionStart, ArmourStart, SuspensionStart, HornStart) =
            (hash, name, cls, kit, engine, brakes, transmission, armour, suspension, horn);

    public uint Hash { get; }
    public string Name { get; }
    public string Class { get; }

    /// <summary>The mod kit number the game saves for this model; 65535 means the model has no mod kit.</summary>
    public int Kit { get; }

    /// <summary>Where the first level of each upgrade sits in the car's kit, or -1 when the car has none.</summary>
    public int EngineStart { get; }
    public int BrakesStart { get; }
    public int TransmissionStart { get; }
    public int ArmourStart { get; }

    /// <summary>Where the first suspension level sits, or -1 when the car has no suspension upgrades.</summary>
    public int SuspensionStart { get; }

    /// <summary>Where the first horn sits; horns come after the armour, so this caps how many armour levels there are.</summary>
    public int HornStart { get; }

    public IReadOnlyList<PartOptions> Parts { get; internal set; } = [];

    /// <summary>The liveries the car can wear, in the order the game lists them (without "none").</summary>
    public IReadOnlyList<string> Liveries { get; internal set; } = [];

    /// <summary>The saved value of the first livery (the rest follow).</summary>
    public int LiveryStart { get; internal set; }

    public bool IsBike => Class == "Motorcycles";
}

/// <summary>Per-model vehicle facts, read from the editor's embedded data file.</summary>
public static class VehicleData
{
    private static readonly Dictionary<uint, VehicleInfo> ByHash = Load();

    public static int Count => ByHash.Count;

    public static VehicleInfo? Find(uint hash) => ByHash.GetValueOrDefault(hash);

    public static IEnumerable<VehicleInfo> All => ByHash.Values;

    private static Dictionary<uint, VehicleInfo> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("SaveEditor.GtaV.VehicleData.txt")
                           ?? throw new InvalidOperationException("The vehicle data is missing.");
        using var reader = new StreamReader(stream);
        var inv = CultureInfo.InvariantCulture;
        var vehicles = new Dictionary<uint, VehicleInfo>();
        var parts = new Dictionary<uint, List<PartOptions>>();
        var liveries = new Dictionary<uint, (int Start, string[] Names)>();
        while (reader.ReadLine() is { } line)
        {
            if (line.Length < 3 || line[0] == '#') continue;
            var f = line.Split('|');
            uint hash = uint.Parse(f[1], NumberStyles.HexNumber, inv);
            switch (f[0])
            {
                case "V":
                    vehicles[hash] = new VehicleInfo(hash, f[2], f[3], int.Parse(f[4], inv), int.Parse(f[5], inv), int.Parse(f[6], inv),
                        int.Parse(f[7], inv), int.Parse(f[8], inv), int.Parse(f[9], inv), int.Parse(f[10], inv));
                    break;
                case "P":
                    if (!parts.TryGetValue(hash, out var list)) parts[hash] = list = [];
                    list.Add(new PartOptions(int.Parse(f[2], inv), f[3], int.Parse(f[4], inv), f[5].Split(';')));
                    break;
                case "L":
                    liveries[hash] = (int.Parse(f[2], inv), f[3].Split(';'));
                    break;
            }
        }
        foreach (var (hash, list) in parts)
            if (vehicles.TryGetValue(hash, out var v)) v.Parts = list;
        foreach (var (hash, livery) in liveries)
            if (vehicles.TryGetValue(hash, out var v)) (v.LiveryStart, v.Liveries) = livery;
        return vehicles;
    }
}
