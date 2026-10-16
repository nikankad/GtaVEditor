using System.Globalization;
using System.Text.Json;

namespace SaveEditor.GtaV;

public sealed record SaveBackup(string FilePath, string Title, DateTime Created, string? OriginalPath)
{
    public string Slot => SaveSlots.Describe(OriginalPath ?? Path.GetFileName(FilePath).Split('.')[0]);
    public string Source => OriginalPath ?? "Older backup — choose a destination when restoring";
}

/// <summary>Preserves the old file before writes and restores, with optional source-path metadata.</summary>
public sealed class BackupService(string backupRoot)
{
    public static BackupService Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaveEditor", "Backups", "gtav-pc"));

    public string BackupRoot { get; } = backupRoot;

    public bool Contains(string path) => Path.GetFullPath(path).StartsWith(
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(BackupRoot)) + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    public string? Backup(string savePath)
    {
        if (!File.Exists(savePath)) return null;
        Directory.CreateDirectory(BackupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture);
        var dest = Path.Combine(BackupRoot, $"{Path.GetFileName(savePath)}.{stamp}.{Guid.NewGuid():N}.bak");
        File.Copy(savePath, dest, overwrite: false);
        File.WriteAllText(dest + ".json", JsonSerializer.Serialize(Path.GetFullPath(savePath)));
        return dest;
    }

    public IEnumerable<string> ListBackups() =>
        Directory.Exists(BackupRoot) ? Directory.GetFiles(BackupRoot, "*.bak").OrderByDescending(f => f) : [];

    public IReadOnlyList<SaveBackup> Entries() => ListBackups().Select(path =>
    {
        string? original = null;
        try
        {
            if (File.Exists(path + ".json")) original = JsonSerializer.Deserialize<string>(SgtaFile.ReadAllBytesLimited(path + ".json", 16 * 1024));
            if (original != null && !Path.IsPathFullyQualified(original)) original = null;
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        catch (SaveFormatException) { }
        catch (ArgumentException) { original = null; }
        var parts = Path.GetFileName(path).Split('.');
        var created = parts.Length > 1 && DateTime.TryParseExact(parts[1], "yyyyMMdd-HHmmss-fff", CultureInfo.InvariantCulture,
            DateTimeStyles.None, out var stamp) ? stamp : File.GetLastWriteTime(path);
        return new SaveBackup(path, SgtaFile.ReadTitle(path) ?? "Unreadable backup", created, original);
    }).OrderByDescending(b => b.Created).ToList();

    /// <summary>Restores to an explicitly chosen destination, backing up an existing destination first.</summary>
    public string? Restore(string backupPath, string destination)
    {
        if (!Contains(backupPath) || !backupPath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
            throw new SaveFormatException("Choose a file from the backup list.");
        if (Contains(destination)) throw new SaveFormatException("Restore outside the backups folder to keep the backup intact.");
        if (!SgtaFile.LooksLikeSgta(backupPath)) throw new SaveFormatException("This backup isn't a readable GTA V save.");
        var tmp = destination + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.Copy(backupPath, tmp);
            var previous = Backup(destination);
            File.Move(tmp, destination, overwrite: true);
            return previous;
        }
        finally
        {
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}
