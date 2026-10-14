namespace SaveEditor.GtaV;

/// <summary>A decrypted, editable GTA V save; every feature page reads and writes through it.</summary>
public sealed class GtaVSaveDocument
{
    private readonly SgtaFile _file;
    private readonly byte[] _key;

    private GtaVSaveDocument(string path, SgtaFile file, BlockContainer body, byte[] key)
    {
        FilePath = path;
        _file = file;
        Body = body;
        _key = key;
        Integrity = body.Verify();
    }

    public string FilePath { get; }
    public BlockContainer Body { get; }
    public IntegrityReport Integrity { get; }

    private StatStore? _stats;

    /// <summary>Stat tables in PSIN; edits write straight into the block and are checksummed on save.</summary>
    public StatStore Stats => _stats ??= StatStore.Load(Body);

    private PsoDocument? _pso;

    /// <summary>The whole save as a PSO tree (script save data, stats, etc.); shares PSIN with <see cref="Stats"/>.</summary>
    public PsoDocument Pso => _pso ??= PsoDocument.Load(Body);

    private WeaponInventory? _weapons;

    /// <summary>Every catalogued weapon for every character, owned or not.</summary>
    public WeaponInventory Weapons => _weapons ??= WeaponStore.Load(Pso);

    private Checklist? _checklist;

    /// <summary>The 100% checklist; ticking entries also updates the completion stats.</summary>
    public Checklist Checklist => _checklist ??= Checklist.Load(Pso, Stats);

    private StoryMissions? _missions;

    /// <summary>The story missions; completing one also updates its stat and checklist entry (when the save has one).</summary>
    public StoryMissions Missions => _missions ??= StoryMissions.Load(Pso, Stats, ChecklistOrNull());

    private StrangersAndFreaks? _strangers;

    /// <summary>The Strangers and Freaks missions; completing one also ticks its checklist entry.</summary>
    public StrangersAndFreaks Strangers => _strangers ??= StrangersAndFreaks.Load(Pso, ChecklistOrNull());

    private PlayerStatus? _player;

    /// <summary>Health, armor, special ability, wanted level and position per character, plus the game clock.</summary>
    public PlayerStatus Player => _player ??= PlayerStatus.Load(Pso);

    private IReadOnlyList<GarageSlot>? _garages;

    /// <summary>The parking spaces in the six safehouse garages.</summary>
    public IReadOnlyList<GarageSlot> Garages => _garages ??= GtaV.Garages.Load(Pso);

    private IReadOnlyList<PropertyVehicleSlot>? _propertyVehicles;

    /// <summary>The vehicles kept at each character's hangar, helipad and boat dock.</summary>
    public IReadOnlyList<PropertyVehicleSlot> PropertyVehicles => _propertyVehicles ??= PropertyVehicleSlot.Load(Pso);

    private IReadOnlyList<Property>? _properties;

    /// <summary>The businesses the characters can buy, and who owns each one.</summary>
    public IReadOnlyList<Property> Properties => _properties ??= Property.Load(Pso);

    private HeistCrew? _crew;

    /// <summary>Which heist crew members are unlocked or dead.</summary>
    public HeistCrew Crew => _crew ??= HeistCrew.Load(Pso);

    private IReadOnlyList<StatEntry>? _statList;

    /// <summary>Every named stat this save has, by category, for the Stats page.</summary>
    public IReadOnlyList<StatEntry> StatList => _statList ??= StatBrowser.Load(Stats);

    private Checklist? ChecklistOrNull()
    {
        try { return Checklist; }
        catch (SaveFormatException) { return null; }
    }

    public long GetStat(StatDefinition d) => d.Kind switch
    {
        StatKind.Int32 => Stats.GetInt(d.Name),
        StatKind.Float => (long)Math.Round(Stats.GetFloat(d.Name)),
        _ => (long)Stats.GetUInt64(d.Name),
    };

    public void SetStat(StatDefinition d, long value)
    {
        if (!d.Editable) throw new InvalidOperationException($"{d.Label} is read-only.");
        if (value < d.Min || value > d.Max)
            throw new ArgumentOutOfRangeException(nameof(value), $"{d.Label} must be between {d.Min:N0} and {d.Max:N0}.");
        switch (d.Kind)
        {
            case StatKind.Int32: Stats.SetInt(d.Name, (int)value); break;
            case StatKind.Float: Stats.SetFloat(d.Name, value); break;
            default: Stats.SetUInt64(d.Name, (ulong)value); break;
        }
    }

    public string Title
    {
        get => _file.Title;
        set => _file.Title = value;
    }

    public static GtaVSaveDocument Load(string path, byte[] key)
    {
        var file = SgtaFile.Read(File.ReadAllBytes(path));
        var body = BlockContainer.Parse(SaveCrypto.Decrypt(file.EncryptedBody, key));
        return new GtaVSaveDocument(path, file, body, key);
    }

    public byte[] ToBytes()
    {
        _file.EncryptedBody = SaveCrypto.Encrypt(Body.Build(), _key);
        return _file.Write();
    }

    public void Save(string path)
    {
        var bytes = ToBytes();
        // Write to a temp file first so a crash can never leave a half-written save.
        var tmp = path + ".tmp";
        File.WriteAllBytes(tmp, bytes);
        File.Move(tmp, path, overwrite: true);
    }
}
