namespace SaveEditor.Games.GtaV;

/// <summary>
/// How the game maps files to its Load Game menu: SGTA50000..SGTA50014 are slots 1-15, SGTA50015 is the autosave.
/// The game ignores files with any other name.
/// </summary>
public static class SaveSlots
{
    public const int ManualSlots = 15;
    public const int AutosaveIndex = 15;

    public static string FileName(int index) => $"SGTA5{index:D4}";

    /// <summary>0-based slot index for a save file name, or null if the game would not list it.</summary>
    public static int? IndexOf(string path)
    {
        var name = Path.GetFileName(path);
        return name.Length == 9 && name.StartsWith("SGTA5", StringComparison.OrdinalIgnoreCase)
               && int.TryParse(name.AsSpan(5), out var i) && i is >= 0 and <= AutosaveIndex
            ? i : null;
    }

    public static string Describe(string path) => IndexOf(path) switch
    {
        AutosaveIndex => "Autosave",
        int i => $"Slot {i + 1}",
        null => Path.GetFileName(path),
    };
}
