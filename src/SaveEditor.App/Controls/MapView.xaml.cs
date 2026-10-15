using System.Collections;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace SaveEditor.App;

/// <summary>
/// The San Andreas map with place names: scroll to zoom, drag to pan. The host supplies the markers (items with
/// <c>MapLeft</c>, <c>MapTop</c> and <c>MapLayer</c>) and how to draw them; a marker template that handles its own click
/// should mark the event handled, so <see cref="MapClick"/> only reports clicks on the map itself.
/// </summary>
public partial class MapView : UserControl
{
    // The map spans the playable area in game metres; north is up.
    public const double Width0 = 400, MinX = -4100, MaxX = 4500, MinY = -4300, MaxY = 8300;
    public const double Scale = Width0 / (MaxX - MinX), Height0 = (MaxY - MinY) * Scale;

    /// <summary>A world position (game metres) as map pixels.</summary>
    public static Point ToMap(double x, double y) => new((x - MinX) * Scale, (MaxY - y) * Scale);

    /// <summary>Map pixels as a world position (game metres).</summary>
    public static (double X, double Y) ToWorld(Point p) => (p.X / Scale + MinX, MaxY - p.Y / Scale);

    public static readonly DependencyProperty MarkersProperty =
        DependencyProperty.Register(nameof(Markers), typeof(IEnumerable), typeof(MapView));

    public static readonly DependencyProperty MarkerTemplateProperty =
        DependencyProperty.Register(nameof(MarkerTemplate), typeof(DataTemplate), typeof(MapView));

    public static readonly DependencyProperty MarkerScaleProperty =
        DependencyProperty.Register(nameof(MarkerScale), typeof(double), typeof(MapView), new PropertyMetadata(1.0));

    public IEnumerable? Markers
    {
        get => (IEnumerable?)GetValue(MarkersProperty);
        set => SetValue(MarkersProperty, value);
    }

    public DataTemplate? MarkerTemplate
    {
        get => (DataTemplate?)GetValue(MarkerTemplateProperty);
        set => SetValue(MarkerTemplateProperty, value);
    }

    /// <summary>How much markers should shrink to undo the zoom (they still grow a little). Bind a ScaleTransform to it.</summary>
    public double MarkerScale
    {
        get => (double)GetValue(MarkerScaleProperty);
        set => SetValue(MarkerScaleProperty, value);
    }

    /// <summary>A click (not a drag) on the map, in map pixels.</summary>
    public event EventHandler<Point>? MapClick;

    public MapView()
    {
        InitializeComponent();
        MapArea.Width = Width0;
        MapArea.Height = Height0;
        MapLabels.ItemsSource = MapLabel.All;
    }

    // ---- Zoom and pan ----

    private const double MinZoom = 1, MaxZoom = 12, StepZoom = 1.5, FocusZoom = 4;
    private Point _dragStart, _panStart;
    private bool _dragging, _panned;

    private double Zoom => MapZoom.ScaleX;

    /// <summary>Zooms by a factor, keeping the map point under <paramref name="anchor"/> (viewport pixels) in place.</summary>
    private void ZoomAt(Point anchor, double factor)
    {
        double zoom = Math.Clamp(Zoom * factor, MinZoom, MaxZoom);
        double f = zoom / Zoom;
        SetView(zoom, anchor.X - (anchor.X - MapPan.X) * f, anchor.Y - (anchor.Y - MapPan.Y) * f);
    }

    /// <summary>Centres the map on a point (map pixels), zooming in if the map is zoomed out.</summary>
    public void ZoomTo(Point mapPoint)
    {
        // Where the point sits with no zoom or pan, then put it in the middle at the new zoom.
        var unzoomed = MapArea.TranslatePoint(mapPoint, ZoomLayer);
        double zoom = Math.Max(Zoom, FocusZoom);
        SetView(zoom, MapViewport.ActualWidth / 2 - unzoomed.X * zoom, MapViewport.ActualHeight / 2 - unzoomed.Y * zoom);
    }

    public void ResetZoom() => SetView(1, 0, 0);

    private void SetView(double zoom, double x, double y)
    {
        // Keep the map filling the view.
        double w = MapViewport.ActualWidth, h = MapViewport.ActualHeight;
        if (zoom <= MinZoom) (x, y) = (0, 0);
        else (x, y) = (Math.Clamp(x, w - w * zoom, 0), Math.Clamp(y, h - h * zoom, 0));
        MapZoom.ScaleX = MapZoom.ScaleY = zoom;
        MapPan.X = x;
        MapPan.Y = y;
        MarkerScale = 1 / Math.Pow(zoom, 0.8);
    }

    private Point ViewportCentre => new(MapViewport.ActualWidth / 2, MapViewport.ActualHeight / 2);

    private void ZoomIn_Click(object sender, RoutedEventArgs e) => ZoomAt(ViewportCentre, StepZoom);

    private void ZoomOut_Click(object sender, RoutedEventArgs e) => ZoomAt(ViewportCentre, 1 / StepZoom);

    private void ZoomReset_Click(object sender, RoutedEventArgs e) => ResetZoom();

    private void Map_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        ZoomAt(e.GetPosition(MapViewport), e.Delta > 0 ? 1.25 : 0.8);
        e.Handled = true;
    }

    private void Map_MouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(MapViewport);
        _panStart = new Point(MapPan.X, MapPan.Y);
        _dragging = true;
        _panned = false;
    }

    private void Map_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || e.LeftButton != MouseButtonState.Pressed) return;
        var delta = e.GetPosition(MapViewport) - _dragStart;
        if (!_panned && Math.Abs(delta.X) + Math.Abs(delta.Y) < 4) return;
        if (!_panned)
        {
            _panned = true;
            MapViewport.CaptureMouse();
        }
        SetView(Zoom, _panStart.X + delta.X, _panStart.Y + delta.Y);
    }

    private void Map_MouseUp(object sender, MouseButtonEventArgs e)
    {
        bool panned = _panned;
        _dragging = false;
        _panned = false;
        if (MapViewport.IsMouseCaptured) MapViewport.ReleaseMouseCapture();
        if (!panned) MapClick?.Invoke(this, e.GetPosition(MapArea));
    }

    private void Map_LostCapture(object sender, MouseEventArgs e) => _dragging = false;
}
