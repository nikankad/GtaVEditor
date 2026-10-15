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
        MapArea.Width = CollectibleItemModel.MapWidth;
        MapArea.Height = CollectibleItemModel.MapHeight;
        MapLabels.ItemsSource = MapLabel.All;
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
                Content = label, GroupName = "CollectibleKind", Style = (Style)FindResource("Segment"),
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
        ItemList.ItemsSource = _items;
        bool mapped = _items.Any(i => i.MapLeft >= 0);
        MapDots.ItemsSource = mapped ? _items : null;
        MapBorder.Visibility = Visible(mapped);
        Legend.Visibility = Visible(mapped);
        CountPanel.Visibility = Visible(set.Set.CountOnly);
        CountBox.Text = set.Set.Collected.ToString();
        CountError.Text = "";
        Note.Text = Notes.GetValueOrDefault(set.Set.Kind, "");
        ShowCount();
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
    }

    /// <summary>Re-reads everything (after another view changed the same save bytes).</summary>
    public void Refresh()
    {
        _sets.ForEach(s => s.Refresh());
        _items.ForEach(i => i.Refresh());
        if (_current != null) CountBox.Text = _current.Set.Collected.ToString();
        ShowCount();
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

    private void Dot_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CollectibleItemModel item) item.Collected = !item.Collected;
    }
}
