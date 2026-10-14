using System.IO;
using SaveEditor.Core;
using SaveEditor.Games.GtaV;

namespace SaveEditor.App;

/// <summary>One entry in the save list: which in-game slot the file is, its title and where it lives.</summary>
public sealed class SaveListItem(string path)
{
    public string Path { get; } = path;
    public string Slot { get; } = SaveSlots.Describe(path) + (path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ? " (game backup)" : "");
    public string Title { get; } = SgtaFile.ReadTitle(path) ?? "(unreadable)";
    public DateTime Modified { get; } = File.GetLastWriteTime(path);
    public string When => Modified.ToString("g");
    public string Folder => System.IO.Path.GetDirectoryName(Path) ?? "";
}

/// <summary>Finds a game's saves: its default folders plus folders the user added (remembered between runs).</summary>
public static class SaveLibrary
{
    private static readonly string FoldersFile = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SaveEditor", "folders.txt");

    private static readonly List<string> ExtraFolders = LoadExtraFolders();

    private static List<string> LoadExtraFolders()
    {
        try { return File.Exists(FoldersFile) ? File.ReadAllLines(FoldersFile).Where(Directory.Exists).ToList() : []; }
        catch (IOException) { return []; }
    }

    public static void AddFolder(string folder)
    {
        if (ExtraFolders.Contains(folder, StringComparer.OrdinalIgnoreCase)) return;
        ExtraFolders.Add(folder);
        try
        {
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(FoldersFile)!);
            File.WriteAllLines(FoldersFile, ExtraFolders);
        }
        catch (IOException) { /* still listed for this session */ }
    }

    /// <summary>The game's saves, newest first.</summary>
    public static List<SaveListItem> Find(IGameModule game)
    {
        var items = new List<SaveListItem>();
        foreach (var folder in game.DefaultSaveFolders().Concat(ExtraFolders).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try { items.AddRange(Directory.EnumerateFiles(folder).Where(game.CanOpen).Select(f => new SaveListItem(f))); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* skip unreadable folder */ }
        }
        return items.OrderByDescending(i => i.Modified).ToList();
    }
}
