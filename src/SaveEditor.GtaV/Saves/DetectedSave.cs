namespace SaveEditor.GtaV;

public sealed record DetectedSave(string FilePath, string Title, DateTime Modified)
{
    public string Slot => SaveSlots.Describe(FilePath);
    public string Profile => Path.GetFileName(Path.GetDirectoryName(FilePath)) ?? "";

    /// <summary>Reads headers only; a missing or inaccessible profile does not hide other profiles.</summary>
    public static IReadOnlyList<DetectedSave> Find(IEnumerable<string> profileFolders)
    {
        var saves = new List<DetectedSave>();
        foreach (var folder in profileFolders.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (var path in Directory.GetFiles(folder, "SGTA*"))
                {
                    if (SaveSlots.IndexOf(path) == null || SgtaFile.ReadTitle(path) is not { } title) continue;
                    try { saves.Add(new(path, title, File.GetLastWriteTime(path))); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
        return saves.OrderByDescending(s => s.Modified).ThenBy(s => s.FilePath).ToList();
    }
}
