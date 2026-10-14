namespace SaveEditor.GtaV;

/// <summary>Where GTA V keeps its PC saves, how to recognise one, and how to open it with the imported key.</summary>
public sealed class GtaVGame(KeyStore keys)
{
    public GtaVGame() : this(KeyStore.Default) { }

    public const string DisplayName = "Grand Theft Auto V (PC)";

    public KeyStore Keys { get; } = keys;

    /// <summary>The game's profile folders on this PC (Documents\Rockstar Games\GTA V\Profiles\*).</summary>
    public IEnumerable<string> DefaultSaveFolders()
    {
        var profiles = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Rockstar Games", "GTA V", "Profiles");
        return Directory.Exists(profiles) ? Directory.GetDirectories(profiles) : [];
    }

    /// <summary>Cheap check (name and header only) whether a file looks like a GTA V save.</summary>
    public bool CanOpen(string path) =>
        Path.GetFileName(path).StartsWith("SGTA", StringComparison.OrdinalIgnoreCase) && SgtaFile.LooksLikeSgta(path);

    /// <summary>Loads and decrypts a save. Throws <see cref="SaveFormatException"/> on bad data.</summary>
    public GtaVSaveDocument Open(string path) => GtaVSaveDocument.Load(path, Keys.Load());
}
