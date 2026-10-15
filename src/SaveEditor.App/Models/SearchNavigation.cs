using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace SaveEditor.App;

/// <summary>Reveals a rendered search target and briefly outlines it without replacing its styling.</summary>
internal static class SearchNavigation
{
    public static void Reveal(FrameworkElement root, Func<FrameworkElement, bool> matches)
    {
        root.Dispatcher.BeginInvoke(() =>
        {
            root.UpdateLayout();
            var target = Descendants(root).FirstOrDefault(matches);
            if (target == null) return;
            target.BringIntoView();
            var layer = AdornerLayer.GetAdornerLayer(target);
            if (layer == null) return;
            var highlight = new SearchHighlight(target);
            layer.Add(highlight);
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            timer.Tick += (_, _) => { timer.Stop(); layer.Remove(highlight); };
            timer.Start();
        }, DispatcherPriority.Loaded);
    }

    private static IEnumerable<FrameworkElement> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is FrameworkElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private sealed class SearchHighlight : Adorner
    {
        public SearchHighlight(UIElement target) : base(target) => IsHitTestVisible = false;
        protected override void OnRender(DrawingContext drawingContext) =>
            drawingContext.DrawRoundedRectangle(null, new Pen((Brush)FindResource("AccentBrush"), 2),
                new Rect(AdornedElement.RenderSize), 3, 3);
    }
}
