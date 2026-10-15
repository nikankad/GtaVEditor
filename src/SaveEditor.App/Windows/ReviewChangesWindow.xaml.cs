using System.Windows;

namespace SaveEditor.App;

/// <summary>
/// Lists everything that differs from the save as opened (or last saved), by tab, with an Undo for each value that
/// can be put back. Shown before every save; the confirm button then writes the file.
/// </summary>
public partial class ReviewChangesWindow : Window
{
    // Stats move along with most other edits (completion %, counters), so they're folded away and explained.
    private const string StatsSection = "Stats";

    private sealed record Section(string Name, IReadOnlyList<Change> Changes, bool Expanded, string Note)
    {
        public string CountText => Changes.Count == 1 ? "1 change" : $"{Changes.Count} changes";
    }

    private readonly ChangeTracker _tracker;
    private readonly Action _undone;

    /// <param name="confirm">The save button's text, e.g. "Save"; null to only review.</param>
    /// <param name="undone">Called after values are put back, so the tabs can show them.</param>
    public ReviewChangesWindow(ChangeTracker tracker, string? confirm, Action undone)
    {
        InitializeComponent();
        _tracker = tracker;
        _undone = undone;
        if (confirm == null)
        {
            ConfirmButton.Visibility = Visibility.Collapsed;
            CloseButton.Content = "Close";
            Heading.Text = "Changes so far";
        }
        else
        {
            ConfirmButton.Content = confirm;
            Heading.Text = "Check your changes before saving";
        }
        Fill();
    }

    private void Fill()
    {
        var changes = _tracker.Changes();
        Sections.ItemsSource = changes.GroupBy(c => c.Section)
            .OrderBy(g => g.Key == StatsSection)
            .Select(g => g.Key == StatsSection
                ? new Section(g.Key, g.ToList(), false, "Many stats change along with your other edits (completion, counters). Undo those edits above first.")
                : new Section(g.Key, g.ToList(), true, ""))
            .ToList();
        int main = changes.Count(c => c.Section != StatsSection), stats = changes.Count - main;
        Intro.Text = changes.Count == 0
            ? "Nothing to save."
            : $"{main} change{(main == 1 ? "" : "s")}" + (stats > 0 ? $", plus {stats} stat{(stats == 1 ? "" : "s")} that moved with them" : "") +
              ". Undo puts a value back as it was when the save was opened.";
        Empty.Visibility = changes.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UndoAllButton.IsEnabled = changes.Any(c => c.CanUndo);
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Change { Undo: { } undo }) return;
        undo();
        _undone();
        Fill();
    }

    private void UndoAll_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Put back every change that can be undone?", "Undo everything", MessageBoxButton.YesNo,
                MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        // Stats last: undoing missions and collectibles fixes most of their counters on its own.
        foreach (var c in _tracker.Changes().OrderBy(c => c.Section == StatsSection))
            if (c.IsChanged()) c.Undo?.Invoke();
        _undone();
        Fill();
    }

    private void Confirm_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
