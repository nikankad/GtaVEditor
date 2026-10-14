using SaveEditor.Core;

namespace SaveEditor.Games.GtaV;

public sealed class GtaVModule(KeyStore keys) : IGameModule
{
    public GtaVModule() : this(KeyStore.Default) { }

    public string Id => "gtav-pc";
    public string DisplayName => "Grand Theft Auto V (PC)";

    public KeyStore Keys { get; } = keys;

    public IEnumerable<string> DefaultSaveFolders()
    {
        var profiles = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rockstar Games", "GTA V", "Profiles");
        return Directory.Exists(profiles) ? Directory.GetDirectories(profiles) : [];
    }

    public bool CanOpen(string path) =>
        Path.GetFileName(path).StartsWith("SGTA", StringComparison.OrdinalIgnoreCase) && SgtaFile.LooksLikeSgta(path);

    public ISaveDocument Open(string path) => GtaVSaveDocument.Load(path, Keys.Load());
}

public sealed class GtaVSaveDocument : ISaveDocument
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
