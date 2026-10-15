using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>The Collectibles tab: one kind at a time, as a map and a tick list.</summary>
public partial class CollectiblesView : UserControl
{
    private static readonly Dictionary<CollectibleKind, string> Notes = new()
    {
        [CollectibleKind.UnderTheBridge] = "The game recounts bridges and knife flights from these marks when the save loads.",
        [CollectibleKind.KnifeFlights] = "The game recounts bridges and knife flights from these marks when the save loads.",
        [CollectibleKind.StuntJumps] = "The save only keeps how many stunt jumps are done, not which ones, so they can't be shown one by one.",
        [CollectibleKind.ForSaleSigns] = "Positions aren't known for this kind, so it has no map.",
        [CollectibleKind.MonkeyMosaics] = "Positions aren't known for this kind, so it has no map.",
        [CollectibleKind.Peyote] = "Positions aren't known for this kind, so it has no map.",
        [CollectibleKind.ParachuteJumps] = "Positions aren't known for this kind, so it has no map.",
    };

    private readonly Action _onChanged;
    private readonly List<CollectibleSetModel> _sets = [];
    private CollectibleSetModel? _current;
    private List<CollectibleItemModel> _items = [];

    public CollectiblesView(GtaVSaveDocument doc, Action onChanged)
    {
        InitializeComponent();
        _onChanged = onChanged;
        try
        {
            _sets = doc.Collectibles.Select(s => new CollectibleSetModel(s)).ToList();
        }
        catch (SaveFormatException ex)
        {
            Error.Text = "Collectibles couldn't be read from this save: " + ex.Message;
            Actions.Visibility = Visibility.Collapsed;
            MapBorder.Visibility = Visibility.Collapsed;
            Legend.Visibility = Visibility.Collapsed;
            return;
        }
        foreach (var set in _sets)
        {
            var label = new TextBlock();
            label.SetBinding(TextBlock.TextProperty, new Binding(nameof(CollectibleSetModel.Header)) { Source = set });
            var button = new RadioButton
            {
                Content = GamePictures.WithLabel(set.Blip, label, 18), GroupName = "CollectibleKind", Style = (Style)FindResource("Segment"),
                IsChecked = set == _sets[0], Margin = new Thickness(0, 0, 0, 4),
            };
            button.Checked += (_, _) => Show(set);
            KindPicker.Children.Add(button);
        }
        Show(_sets[0]);
    }

    private void Show(CollectibleSetModel set)
    {
        _current = set;
        _items = set.Set.Items.Select(i => new CollectibleItemModel(set.Set, i, ItemChanged)).ToList();
        bool mapped = _items.Any(i => i.HasLocation);
        ApplyFilter();
        MapBorder.Visibility = Visible(mapped);
        MapColumn.Width = mapped ? new GridLength(3, GridUnitType.Star) : new GridLength(0);
        MapColumn.MinWidth = mapped ? 320 : 0;
        NextMissingButton.Visibility = Visible(mapped);
        Select(null);
        Map.ResetZoom();
        LegendFound.Source = GamePictures.Collectible(set.Set.Kind, found: true);
        LegendMissing.Source = GamePictures.Collectible(set.Set.Kind, found: false);
        CountPanel.Visibility = Visible(set.Set.CountOnly);
        CountBox.Text = set.Set.Collected.ToString();
        CountError.Text = "";
        Note.Text = Notes.GetValueOrDefault(set.Set.Kind, "");
        ShowCount();
    }

    private void Filter_Changed(object sender, RoutedEventArgs e) => ApplyFilter();

    private void ApplyFilter()
    {
        var shown = _items.Where(i => MissingOnly.IsChecked != true || !i.Collected).ToList();
        ItemList.ItemsSource = shown;
        Map.Markers = shown.Where(i => i.HasLocation).ToList();
        if (_selected != null && !shown.Contains(_selected)) Select(null);
        MissingOnly.IsEnabled = _current?.Set.CountOnly != true;
    }

    private static Visibility Visible(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private void ShowCount()
    {
        if (_current == null) return;
        _current.Refresh();
        CountText.Text = _current.CountText;
    }

    private void ItemChanged()
    {
        _onChanged();
        ShowCount();
        Dispatcher.BeginInvoke(ApplyFilter);
    }

    /// <summary>Re-reads everything (after another view changed the same save bytes).</summary>
    public void Refresh()
    {
        _sets.ForEach(s => s.Refresh());
        _items.ForEach(i => i.Refresh());
        if (_current != null) CountBox.Text = _current.Set.Collected.ToString();
        ShowCount();
        ApplyFilter();
    }

    private void SetAll(IEnumerable<CollectibleSetModel> sets, bool found)
    {
        foreach (var s in sets) s.Set.SetAll(found);
        _onChanged();
        Refresh();
    }

    private void All_Click(object sender, RoutedEventArgs e) => SetAll(_current == null ? [] : [_current], true);

    private void None_Click(object sender, RoutedEventArgs e) => SetAll(_current == null ? [] : [_current], false);

    private void Everything_Click(object sender, RoutedEventArgs e) => SetAll(_sets, true);

    private void SetCount_Click(object sender, RoutedEventArgs e)
    {
        if (_current is not { Set.CountOnly: true } set) return;
        if (!int.TryParse(CountBox.Text.Trim(), out var n) || n < 0 || n > set.Set.Total)
        {
            CountError.Text = $"Enter a number from 0 to {set.Set.Total}.";
            return;
        }
        CountError.Text = "";
        if (n == set.Set.Collected) return;
        set.Set.SetCount(n);
        _onChanged();
        Refresh();
    }

    /// <summary>Opens the set an item belongs to and, for a single item ("Letter Scraps No. 12"), selects it on the map.</summary>
    public void ShowItem(string title)
    {
        var set = _sets.FirstOrDefault(s => title == s.Set.Name || title.StartsWith(s.Set.Name + " No. ", StringComparison.Ordinal));
        if (set == null) return;
        if (KindPicker.Children[_sets.IndexOf(set)] is RadioButton button) button.IsChecked = true;
        if (_current != set) Show(set);
        var item = _items.FirstOrDefault(i => i.Title == title);
        if (item == null) return;
        MissingOnly.IsChecked = false;
        Select(item);
        SearchNavigation.Reveal(this, e => ReferenceEquals(e.DataContext, item));
        if (item.HasLocation) Dispatcher.BeginInvoke(() => Map.ZoomTo(item.MapCentre), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    // ---- Picking an item ----

    private CollectibleItemModel? _selected;

    /// <summary>Highlights an item on the map and in the list, and shows its details card.</summary>
    private void Select(CollectibleItemModel? item)
    {
        if (_selected != null) _selected.IsSelected = false;
        _selected = item;
        if (item != null) item.IsSelected = true;
        Details.DataContext = item;
        Details.Visibility = Visible(item != null);
    }

    private void Dot_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CollectibleItemModel item) Select(item);
        e.Handled = true;
    }

    /// <summary>A click on empty map closes the details card.</summary>
    private void Map_Click(object? sender, Point e) => Select(null);

    private void Card_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not CollectibleItemModel item) return;
        Select(item);
        if (item.HasLocation) Map.ZoomTo(item.MapCentre);
    }

    private void CloseDetails_Click(object sender, RoutedEventArgs e) => Select(null);

    private void ZoomToSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is { HasLocation: true }) Map.ZoomTo(_selected.MapCentre);
    }

    private void NextItem_Click(object sender, RoutedEventArgs e) => Step(+1, _ => true);

    private void PreviousItem_Click(object sender, RoutedEventArgs e) => Step(-1, _ => true);

    private void NextMissing_Click(object sender, RoutedEventArgs e) => Step(+1, i => !i.Collected);

    /// <summary>Moves to the next (or previous) mapped item that matches, wrapping round, and zooms to it.</summary>
    private void Step(int direction, Func<CollectibleItemModel, bool> wanted)
    {
        var mapped = _items.Where(i => i.HasLocation && (MissingOnly.IsChecked != true || !i.Collected)).ToList();
        if (mapped.Count == 0) return;
        int start = _selected == null ? (direction > 0 ? -1 : 0) : mapped.IndexOf(_selected);
        for (int n = 1; n <= mapped.Count; n++)
        {
            var item = mapped[((start + direction * n) % mapped.Count + mapped.Count) % mapped.Count];
            if (!wanted(item)) continue;
            Select(item);
            Map.ZoomTo(item.MapCentre);
            return;
        }
    }
}
