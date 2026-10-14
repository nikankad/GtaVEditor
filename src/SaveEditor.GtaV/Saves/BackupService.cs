namespace SaveEditor.GtaV;

/// <summary>
/// Copies a save to a timestamped backup before anything overwrites it,
/// so a bad edit can always be rolled back.
/// </summary>
public sealed class BackupService(string backupRoot)
{
    // Same folder earlier versions used, so existing backups stay listed.
    public static BackupService Default { get; } = new(Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SaveEditor", "Backups", "gtav-pc"));

    public string BackupRoot { get; } = backupRoot;

    /// <summary>Backs up <paramref name="savePath"/> (if it exists) and returns the backup path.</summary>
    public string? Backup(string savePath)
    {
        if (!File.Exists(savePath)) return null;
        Directory.CreateDirectory(BackupRoot);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var dest = Path.Combine(BackupRoot, $"{Path.GetFileName(savePath)}.{stamp}.bak");
        File.Copy(savePath, dest, overwrite: false);
        return dest;
    }

    public IEnumerable<string> ListBackups() =>
        Directory.Exists(BackupRoot) ? Directory.GetFiles(BackupRoot, "*.bak").OrderByDescending(f => f) : [];
}
