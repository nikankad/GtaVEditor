using System.Windows;
using System.Windows.Controls;

namespace SaveEditor.App;

/// <summary>
/// Cards in as many equal columns as fit, each card dropped into the shortest column. Unlike a WrapPanel of fixed-width
/// cards, there is no empty band on the right and no hole under a short card.
/// </summary>
public sealed class MasonryPanel : Panel
{
    public static readonly DependencyProperty ColumnWidthProperty = DependencyProperty.Register(nameof(ColumnWidth), typeof(double),
        typeof(MasonryPanel), new FrameworkPropertyMetadata(300.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty SpacingProperty = DependencyProperty.Register(nameof(Spacing), typeof(double),
        typeof(MasonryPanel), new FrameworkPropertyMetadata(12.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    /// <summary>The narrowest a column may get; columns widen to share out the leftover space.</summary>
    public double ColumnWidth
    {
        get => (double)GetValue(ColumnWidthProperty);
        set => SetValue(ColumnWidthProperty, value);
    }

    /// <summary>The gap between cards, across and down.</summary>
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    protected override Size MeasureOverride(Size available) => Layout(available.Width, arrange: false);

    protected override Size ArrangeOverride(Size final)
    {
        Layout(final.Width, arrange: true);
        return final;
    }

    private Size Layout(double width, bool arrange)
    {
        if (double.IsInfinity(width)) width = ColumnWidth;
        int columns = Math.Max(1, (int)((width + Spacing) / (ColumnWidth + Spacing)));
        double columnWidth = Math.Max(0, (width - Spacing * (columns - 1)) / columns);
        var heights = new double[columns];
        foreach (UIElement child in InternalChildren)
        {
            if (!arrange) child.Measure(new Size(columnWidth, double.PositiveInfinity));
            if (child.Visibility == Visibility.Collapsed) continue;
            int column = Array.IndexOf(heights, heights.Min());
            double height = child.DesiredSize.Height;
            if (arrange) child.Arrange(new Rect(column * (columnWidth + Spacing), heights[column], columnWidth, height));
            heights[column] += height + Spacing;
        }
        return new Size(width, Math.Max(0, heights.Max() - Spacing));
    }
}
