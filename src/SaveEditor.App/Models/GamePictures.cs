using System.Collections;
using System.Resources;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>
/// The game's own artwork bundled under Assets/Game: character portraits, weapon pictures, the pause menu's mission and
/// checklist thumbnails, and collectible map blips. Every lookup returns null when there is no picture, so the UI just
/// shows the text.
/// </summary>
public static class GamePictures
{
    private const string Root = "assets/game/";

    // Resource names as WPF stores them: lower case, relative to the project.
    private static readonly HashSet<string> Available = LoadIndex();
    private static readonly Dictionary<string, ImageSource?> Cache = [];

    public static ImageSource? Character(string name) => Get($"characters/{name}.png");

    /// <summary>The character's money banner from the game's stats screen.</summary>
    public static ImageSource? Banner(string name) => Get($"banners/{name}.png");

    public static ImageSource? Weapon(string id) => Get($"weapons/{id}.png");

    public static ImageSource? Mission(string code) => Get($"missions/{code}.jpg");

    public static ImageSource? Stranger(int index) => Get($"strangers/{index}.jpg");

    /// <summary>A stranger's contact picture, e.g. "Nigel and Mrs. Thornhill".</summary>
    public static ImageSource? Person(string name) => Get($"people/{new string(name.Where(char.IsLetterOrDigit).ToArray())}.png");

    public static ImageSource? Collectible(CollectibleKind kind, bool found) => Get($"collectibles/{kind}{(found ? "-found" : "")}.png");

    /// <summary>
    /// The pause menu thumbnail for a checklist entry. Entries the game shows with a sibling's picture (a heist with
    /// approach variants, the later trips of an odd job) fall back to the nearest label: one or two characters shorter.
    /// </summary>
    public static ImageSource? Checklist(string label)
    {
        var key = label.ToLowerInvariant();
        if (Available.Contains($"{Root}checklist/{key}.jpg")) return Get($"checklist/{key}.jpg");
        for (int trim = 1; trim <= 2 && key.Length - trim >= 4; trim++)
        {
            var prefix = $"{Root}checklist/{key[..^trim]}";
            var sibling = Available.Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).Order(StringComparer.Ordinal).FirstOrDefault();
            if (sibling != null) return Get(sibling[Root.Length..]);
        }
        return null;
    }

    // The colours the game gives each character on the HUD and character wheel.
    private static readonly Dictionary<string, Brush> Colours = new()
    {
        ["Michael"] = Frozen(0x65, 0xB4, 0xD4), ["Franklin"] = Frozen(0xAB, 0xED, 0xAB), ["Trevor"] = Frozen(0xFF, 0xA3, 0x57),
    };

    public static Brush CharacterColour(string name) => Colours.GetValueOrDefault(name) ?? Brushes.Transparent;

    private static Brush Frozen(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    /// <summary>A picture beside a label, for buttons built in code.</summary>
    public static object WithLabel(ImageSource? picture, string text, double size = 22) =>
        WithLabel(picture, new TextBlock { Text = text }, size);

    public static object WithLabel(ImageSource? picture, TextBlock label, double size = 22)
    {
        if (picture == null) return label;
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new Image { Source = picture, Width = size, Height = size, Margin = new Thickness(-8, -2, 7, -2) });
        label.VerticalAlignment = VerticalAlignment.Center;
        panel.Children.Add(label);
        return panel;
    }

    private static ImageSource? Get(string path)
    {
        path = path.ToLowerInvariant();
        if (Cache.TryGetValue(path, out var cached)) return cached;
        ImageSource? image = null;
        if (Available.Contains(Root + path))
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri($"pack://application:,,,/{Root}{path}");
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.EndInit();
            bitmap.Freeze();
            image = bitmap;
        }
        return Cache[path] = image;
    }

    private static HashSet<string> LoadIndex()
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var assembly = typeof(GamePictures).Assembly;
        using var stream = assembly.GetManifestResourceStream(assembly.GetName().Name + ".g.resources");
        if (stream == null) return names;
        using var reader = new ResourceReader(stream);
        foreach (DictionaryEntry entry in reader)
            if (entry.Key is string name && name.StartsWith(Root, StringComparison.Ordinal)) names.Add(name);
        return names;
    }
}
