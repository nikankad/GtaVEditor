namespace SaveEditor.Core;

/// <summary>
/// One supported game. Each game ships its own module because save formats
/// (encryption, checksums, layout) differ completely between titles.
/// </summary>
public interface IGameModule
{
    /// <summary>Stable identifier, e.g. "gtav-pc".</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>Default folders where the game keeps its saves on this PC.</summary>
    IEnumerable<string> DefaultSaveFolders();

    /// <summary>Cheap check (name / header only) whether a file looks like this game's save.</summary>
    bool CanOpen(string path);

    /// <summary>Loads and decodes a save. Throws <see cref="SaveFormatException"/> on bad data.</summary>
    ISaveDocument Open(string path);
}

/// <summary>A decoded, editable save file.</summary>
public interface ISaveDocument
{
    string FilePath { get; }

    /// <summary>Human-readable save name shown in the game's load menu.</summary>
    string Title { get; set; }

    /// <summary>Integrity status of the file as loaded (checksums etc.).</summary>
    IntegrityReport Integrity { get; }

    /// <summary>Re-encodes the save (recomputing checksums) and writes it to <paramref name="path"/>.</summary>
    void Save(string path);
}

public sealed record IntegrityReport(bool IsValid, string Summary);

public sealed class SaveFormatException(string message, Exception? inner = null) : Exception(message, inner);
