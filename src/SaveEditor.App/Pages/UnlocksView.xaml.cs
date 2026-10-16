using System.Windows;
using System.Windows.Controls;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>The Unlocks tab: Ammu-Nation stock, clothes, Director Mode actors and Flight School medals.</summary>
public partial class UnlocksView : UserControl
{
    private readonly GtaVSaveDocument _doc;
    private readonly Action _onChanged;
    private readonly Dictionary<string, List<WeaponUnlockGroup>> _weapons = [];
    private string _weaponCharacter = StatCatalog.Characters[0];
    private List<WardrobeModel> _wardrobes = [];
    private List<FlightLessonModel> _lessons = [];
    private DirectorModeUnlocks? _director;

    public UnlocksView(GtaVSaveDocument doc, Action onChanged)
    {
        InitializeComponent();
        _doc = doc;
        _onChanged = onChanged;
        LoadWeapons();
        Try(ClothingError, "Clothing", () =>
        {
            // A save whose outfit data can't be read still shows the wardrobe, without the "Wearing now" lists.
            IReadOnlyList<Outfit> outfits = [];
            try { outfits = doc.Outfits; } catch (SaveFormatException) { }
            Wardrobes.ItemsSource = _wardrobes = doc.Wardrobes
                .Select(w => new WardrobeModel(w, outfits.FirstOrDefault(o => o.Character == w.Character), onChanged)).ToList();
        });
        if (!Try(DirectorError, "Director Mode", () => _director = doc.DirectorMode)) DirectorCard.Visibility = Visibility.Collapsed;
        if (!Try(FlightError, "Flight School", () => Lessons.ItemsSource = _lessons = doc.FlightSchool.Select(l => new FlightLessonModel(l, onChanged)).ToList()))
            GoldButton.Visibility = Visibility.Collapsed;
        ShowDirector();
    }

    private static bool Try(TextBlock error, string what, Action load)
    {
        try
        {
            load();
            return true;
        }
        catch (SaveFormatException ex)
        {
            error.Text = $"{what} couldn't be read from this save: {ex.Message}";
            return false;
        }
    }

    private void LoadWeapons()
    {
        IReadOnlyList<WeaponUnlock> all = [];
        if (!Try(WeaponError, "Weapon unlocks", () => all = _doc.WeaponUnlocks))
        {
            WeaponPanel.Visibility = Visibility.Collapsed;
            return;
        }
        foreach (var character in StatCatalog.Characters)
        {
            _weapons[character] = all.Where(u => u.Character == character).GroupBy(u => u.Weapon.Category)
                .Select(g => new WeaponUnlockGroup(g.Key, g.Select(u => new WeaponUnlockModel(u, _onChanged)).ToList())).ToList();
            var button = new RadioButton
            {
                Content = GamePictures.WithLabel(GamePictures.Character(character), character), GroupName = "UnlockCharacter", Style = (Style)FindResource("Segment"),
                IsChecked = character == _weaponCharacter, Tag = character,
            };
            button.Checked += (_, _) => ShowWeapons(character);
            WeaponCharacters.Children.Add(button);
        }
        ShowWeapons(_weaponCharacter);
    }

    private void ShowWeapons(string character)
    {
        _weaponCharacter = character;
        WeaponGroups.ItemsSource = _weapons.GetValueOrDefault(character);
    }

    private void UnlockWeapons(IEnumerable<WeaponUnlockGroup> groups)
    {
        foreach (var w in groups.SelectMany(g => g.Weapons)) w.Unlocked = true;
    }

    private void UnlockWeapons_Click(object sender, RoutedEventArgs e) => UnlockWeapons(_weapons.GetValueOrDefault(_weaponCharacter) ?? []);

    private void UnlockWeaponsEveryone_Click(object sender, RoutedEventArgs e) => UnlockWeapons(_weapons.Values.SelectMany(g => g));

    private void OwnClothes_Click(object sender, RoutedEventArgs e) => ((sender as FrameworkElement)?.DataContext as WardrobeModel)?.OwnAll();

    private void OwnClothingKind_Click(object sender, RoutedEventArgs e) => ((sender as FrameworkElement)?.DataContext as ClothingCategoryModel)?.OwnAll();

    private void ShowDirector()
    {
        if (_director == null) return;
        DirectorRows.ItemsSource = _director.Progress.Select(p => new DirectorRow(p.Title, $"{p.Unlocked} / {p.Total}")).ToList();
        DirectorButton.IsEnabled = !_director.AllUnlocked;
    }

    private void UnlockDirector_Click(object sender, RoutedEventArgs e)
    {
        _director?.UnlockAll();
        _onChanged();
        ShowDirector();
    }

    private void GoldAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var l in _lessons) l.Medal = FlightLesson.Medals[^1];
    }

    public void ShowItem(string label)
    {
        var split = label.IndexOf(": ", StringComparison.Ordinal);
        var who = split < 0 ? "" : label[..split];
        var name = split < 0 ? label : label[(split + 2)..];
        foreach (var button in WeaponCharacters.Children.OfType<RadioButton>())
            if (button.Tag as string == who) button.IsChecked = true;
        SearchNavigation.Reveal(this, e => e.DataContext switch
        {
            WeaponUnlockModel weapon => name == weapon.Label + " at Ammu-Nation",
            WardrobeModel wardrobe => wardrobe.Name == who && wardrobe.Categories.Any(c => c.Label.ToLowerInvariant() == name),
            FlightLessonModel lesson => who == "Flight School" && lesson.Name == name,
            _ => label == "Director Mode actors" && e == DirectorCard,
        });
    }

    /// <summary>Re-reads everything (after another view changed the same save bytes).</summary>
    public void Refresh()
    {
        foreach (var w in _weapons.Values.SelectMany(g => g).SelectMany(g => g.Weapons)) w.Refresh();
        _wardrobes.ForEach(w => { w.Refresh(); foreach (var k in w.Categories) k.Refresh(); });
        _lessons.ForEach(l => l.Refresh());
        ShowDirector();
    }
}
