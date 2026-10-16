using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>
/// Picks a vehicle by its picture: a searchable grid of the game's vehicles, filtered by class, with the paint below.
/// The choice is in <see cref="Vehicle"/> and <see cref="Colour"/> after the window closes with OK.
/// </summary>
public partial class VehiclePickerWindow : Window
{
    /// <summary>A vehicle tile.</summary>
    public sealed record Tile(VehicleModel Model)
    {
        public string Name => Model.Name;
        public string Class => Model.Class;
        public ImageSource? Picture => GamePictures.VehicleThumb(Model.Hash);
    }

    /// <summary>A class filter; "All" has no class.</summary>
    public sealed class ClassFilter(string label, string? cls) : Observable
    {
        public string Label => label;
        public string? Class => cls;
        public bool IsChecked { get; set; }
    }

    private const string AllClasses = "All";
    private readonly IReadOnlyList<Tile> _all;
    private string? _class;

    public VehiclePickerWindow(IReadOnlyList<VehicleModel> choices, VehicleModel? current, string where, string okText)
    {
        InitializeComponent();
        _all = choices.Select(v => new Tile(v)).ToList();
        WhereText.Text = where;
        OkButton.Content = okText;

        // Start on the current vehicle's class, so the grid opens small and on something familiar.
        var classes = choices.Select(v => v.Class).Distinct().Order().ToList();
        _class = classes.Count > 1 && current != null && classes.Contains(current.Class) ? current.Class : null;
        var filters = new List<ClassFilter> { new(AllClasses, null) { IsChecked = _class == null } };
        if (classes.Count > 1) filters.AddRange(classes.Select(c => new ClassFilter(c, c) { IsChecked = c == _class }));
        ClassFilters.ItemsSource = filters;
        ClassFilters.Visibility = classes.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        Paints.ItemsSource = VehicleColour.All.Select(c => new SwatchChoice(c, c.Name, SwatchChoice.Brush(c.Hex))).ToList();
        Paints.SelectedIndex = 0;

        ShowTiles();
        Tiles.SelectedItem = Tiles.Items.Cast<Tile>().FirstOrDefault(t => t.Model == current) ?? Tiles.Items.Cast<Tile>().FirstOrDefault();
        Tiles.ScrollIntoView(Tiles.SelectedItem);
        Loaded += (_, _) => SearchBox.Focus();
    }

    public VehicleModel? Vehicle { get; private set; }
    public VehicleColour Colour { get; private set; } = VehicleColour.All[0];

    private void ShowTiles()
    {
        var search = SearchBox.Text.Trim();
        // A search looks through every class.
        var shown = _all.Where(t => search.Length > 0
            ? t.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || t.Class.Contains(search, StringComparison.OrdinalIgnoreCase)
            : _class == null || t.Class == _class).ToList();
        var keep = Tiles.SelectedItem;
        Tiles.ItemsSource = shown;
        if (keep is Tile t && shown.Contains(t)) Tiles.SelectedItem = t;
        NoMatches.Visibility = shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Search_Changed(object sender, TextChangedEventArgs e) => ShowTiles();

    private void Class_Checked(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not ClassFilter f) return;
        _class = f.Class;
        if (SearchBox.Text.Length > 0) SearchBox.Text = "";
        else ShowTiles();
    }

    private void Tile_Changed(object sender, SelectionChangedEventArgs e)
    {
        var tile = Tiles.SelectedItem as Tile;
        Vehicle = tile?.Model ?? Vehicle;
        if (tile == null) return;
        ChosenPicture.Source = GamePictures.Vehicle(tile.Model.Hash);
        ChosenName.Text = tile.Name;
        ChosenClass.Text = tile.Class;
    }

    private void Paint_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Paints.SelectedItem is not SwatchChoice { Value: VehicleColour c }) return;
        Colour = c;
        PaintName.Text = c.Name;
    }

    private void Tile_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is Tile) Ok_Click(sender, e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (Vehicle == null) return;
        DialogResult = true;
    }
}
