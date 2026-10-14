using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>Raw tools for one open save: every stored value, save comparison, and file details.</summary>
public partial class AdvancedToolsWindow : Window
{
    private readonly GtaVSaveDocument _doc;
    private readonly Action _onChanged;
    private readonly bool _loading;

    // Every value node in the save's PSO tree, built when the tab is first shown.
    private List<PsoValueRow>? _dataRows;

    // Current comparison, kept so "Swap" and "Include unnamed stats" can redraw it.
    private (GtaVSaveDocument Doc, string Name)? _compareBefore, _compareAfter;

    public AdvancedToolsWindow(GtaVSaveDocument doc, Action onChanged)
    {
        _loading = true;
        InitializeComponent();
        _doc = doc;
        _onChanged = onChanged;

        FileText.Text = doc.FilePath;
        SlotText.Text = SaveSlots.IndexOf(doc.FilePath) == null
            ? "Not a game slot file name; the game would not list it. Use 'Save to game slot…' to put it in one."
            : $"{SaveSlots.Describe(doc.FilePath)} in the game's Load Game menu (if this is the game's profile folder).";
        TitleBox.Text = doc.Title;
        IntegrityText.Text = doc.Integrity.Summary;
        IntegrityText.Foreground = doc.Integrity.IsValid ? System.Windows.Media.Brushes.DarkGreen : System.Windows.Media.Brushes.DarkRed;
        BlockList.ItemsSource = doc.Body.Blocks;
        _loading = false;
        ShowDataRows();
    }

    private void ShowDataRows()
    {
        try
        {
            _dataRows ??= _doc.Pso.Walk().Where(n => n.IsValue).Select(n => new PsoValueRow(n, _onChanged)).ToList();
        }
        catch (SaveFormatException ex)
        {
            DataCountText.Text = "Save data unavailable: " + ex.Message;
            SaveDataGrid.ItemsSource = null;
            return;
        }
        var find = DataSearchBox.Text.Trim();
        bool unnamed = DataUnnamedBox.IsChecked == true;
        var rows = _dataRows.Where(r => (unnamed || r.Node.IsNamed) &&
                                        (find.Length == 0 || r.Path.Contains(find, StringComparison.OrdinalIgnoreCase))).ToList();
        foreach (var r in rows) r.Refresh();
        SaveDataGrid.ItemsSource = rows;
        DataCountText.Text = $"{rows.Count:N0} of {_dataRows.Count:N0} values";
    }

    private void DataSearch_Changed(object sender, RoutedEventArgs e)
    {
        if (!_loading) ShowDataRows();
    }

    private void Tabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // SelectionChanged bubbles up from the grids inside the tabs; react only to tab switches.
        if (e.Source == Tabs && !_loading && Tabs.SelectedItem == SaveDataTab) ShowDataRows();
    }

    private void TitleBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || TitleBox.Text == _doc.Title) return;
        _doc.Title = TitleBox.Text;
        _onChanged();
    }

    private void CompareWith_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Pick the save to compare with", Filter = "All files|*.*", InitialDirectory = Path.GetDirectoryName(_doc.FilePath),
        };
        if (dlg.ShowDialog(this) != true) return;

        GtaVSaveDocument other;
        try
        {
            other = App.Game.Open(dlg.FileName);
        }
        catch (Exception ex) when (ex is SaveFormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, ex.Message, "Cannot open save", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        (GtaVSaveDocument, string) Named(GtaVSaveDocument d) => (d, $"{SaveSlots.Describe(d.FilePath)} \"{d.Title}\"");
        // The older file is "before" by default; Swap reverses it.
        bool currentIsOlder = File.GetLastWriteTimeUtc(_doc.FilePath) <= File.GetLastWriteTimeUtc(other.FilePath);
        _compareBefore = Named(currentIsOlder ? _doc : other);
        _compareAfter = Named(currentIsOlder ? other : _doc);
        ShowComparison();
    }

    private void SwapCompare_Click(object sender, RoutedEventArgs e)
    {
        if (_compareBefore == null) return;
        (_compareBefore, _compareAfter) = (_compareAfter, _compareBefore);
        ShowComparison();
    }

    private void ShowUnnamed_Changed(object sender, RoutedEventArgs e) => ShowComparison();

    private void ShowComparison()
    {
        if (_compareBefore is not { } before || _compareAfter is not { } after) return;
        SaveComparison result;
        try
        {
            result = SaveComparer.Compare(before.Doc, after.Doc);
        }
        catch (SaveFormatException ex)
        {
            CompareText.Text = "Cannot compare: " + ex.Message;
            return;
        }

        int unnamed = result.Stats.Count(s => !s.IsNamed);
        CompareGrid.ItemsSource = ShowUnnamedBox.IsChecked == true ? result.Stats : result.Stats.Where(s => s.IsNamed).ToList();
        CompareText.Text = $"Before: {before.Name}\nAfter:  {after.Name}\n" +
                           $"{result.Stats.Count} stat(s) changed ({result.Stats.Count - unnamed} with known names, {unnamed} unnamed).";
        CompareBlocksText.Text = "Bytes changed per data block: " + string.Join(",  ", result.Blocks.Select(b =>
            $"{b.Id} {b.ChangedBytes:N0}" + (b.SizeBefore != b.SizeAfter ? $" (size {b.SizeBefore:N0} → {b.SizeAfter:N0})" : ""))) +
            ". Changes outside the stats (appearance, missions, world state) are only counted here for now.";
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
