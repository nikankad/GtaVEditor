namespace SaveEditor.Core;

/// <summary>
/// Copies a save to a timestamped backup before anything overwrites it,
/// so a bad edit can always be rolled back.
/// </summary>
public sealed class BackupService(string backupRoot)
{
    public static BackupService Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaveEditor", "Backups"));

    public string BackupRoot { get; } = backupRoot;

    /// <summary>Backs up <paramref name="savePath"/> (if it exists) and returns the backup path.</summary>
    public string? Backup(string gameId, string savePath)
    {
        if (!File.Exists(savePath)) return null;
        var dir = Path.Combine(BackupRoot, gameId);
        Directory.CreateDirectory(dir);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var dest = Path.Combine(dir, $"{Path.GetFileName(savePath)}.{stamp}.bak");
        File.Copy(savePath, dest, overwrite: false);
        return dest;
    }

    public IEnumerable<string> ListBackups(string gameId)
    {
        var dir = Path.Combine(BackupRoot, gameId);
        return Directory.Exists(dir)
            ? Directory.GetFiles(dir, "*.bak").OrderByDescending(f => f)
            : [];
    }
}
