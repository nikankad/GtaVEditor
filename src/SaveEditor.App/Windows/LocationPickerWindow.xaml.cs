using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>
/// Picks where a character is when you switch to them, on the map. The save needs a height as well as a map position,
/// so a click moves to the nearest place known to be standable (a named place, a walk-up collectible, or where a
/// character already is) rather than to the exact point clicked.
/// </summary>
public partial class LocationPickerWindow : Window
{
    private readonly List<LocationSpot> _spots;
    private LocationSpot? _chosen;

    public LocationPickerWindow(StatusModel character, IEnumerable<StatusModel> everyone)
    {
        InitializeComponent();
        Title = $"Choose where {character.Name} is";
        Heading.Text = $"Where should {character.Name} be?";
        Intro.Text = $"Click anywhere on the map. {character.Name} goes to the nearest spot you can stand on, since the save " +
                     "needs a height the map can't show. Scroll to zoom, drag to move.";
        OkButton.Content = $"Move {character.Name} here";

        var people = everyone.Select(s => new LocationSpot(s == character ? $"Where {s.Name} is now" : $"Where {s.Name} is",
            s.Position.X, s.Position.Y, s.Position.Z, LocationSpot.Kinds.Character, s.Name)).ToList();
        var places = LocationPreset.All.Select(p => new LocationSpot(p.Name, p.X, p.Y, p.Z, LocationSpot.Kinds.Place)).ToList();
        var pickups = GroundSpots.Pickups.Select(p =>
            new LocationSpot("Spot near " + MapLabel.Nearest(p.X, p.Y).Text, p.X, p.Y, p.Z, LocationSpot.Kinds.Pickup)).ToList();
        _spots = [.. pickups, .. places, .. people];

        Map.Markers = _spots;
        PlaceList.ItemsSource = people.Concat(places).ToList();
        Choose(people[everyone.ToList().IndexOf(character)], note: "");
        Loaded += (_, _) => Map.ZoomTo(_chosen!.MapCentre);
    }

    /// <summary>The spot picked, once the window closes with OK.</summary>
    public LocationSpot? Chosen => _chosen;

    private void Choose(LocationSpot spot, string note)
    {
        if (_chosen != null) _chosen.IsSelected = false;
        _chosen = spot;
        spot.IsSelected = true;
        ChosenCard.DataContext = spot;
        SnapNote.Text = note;
        SnapNote.Visibility = note.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (PlaceList.SelectedItem != spot) PlaceList.SelectedItem = PlaceList.Items.Contains(spot) ? spot : null;
    }

    private void Map_Click(object? sender, Point at)
    {
        var nearest = _spots.MinBy(s => (s.MapCentre - at).LengthSquared)!;
        var (x, y) = MapView.ToWorld(at);
        double metres = Math.Sqrt((nearest.X - x) * (nearest.X - x) + (nearest.Y - y) * (nearest.Y - y));
        Choose(nearest, metres < 25 ? "" : $"The nearest spot you can stand on, about {metres:N0} m from where you clicked.");
    }

    private void Spot_Click(object sender, MouseButtonEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is LocationSpot spot) Choose(spot, "");
        e.Handled = true;
    }

    private void PlaceList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PlaceList.SelectedItem is not LocationSpot spot || spot == _chosen) return;
        Choose(spot, "");
        Map.ZoomTo(spot.MapCentre);
    }

    private void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
