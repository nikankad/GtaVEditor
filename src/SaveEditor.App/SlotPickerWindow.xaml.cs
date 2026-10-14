using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using SaveEditor.Games.GtaV;

namespace SaveEditor.App;

/// <summary>Lets the user pick one of the game's 15 manual slots; the autosave slot is left alone.</summary>
public partial class SlotPickerWindow : Window
{
    private sealed record SlotEntry(int Index, string Path, string? ExistingTitle)
    {
        public override string ToString() =>
            $"Slot {Index + 1}    " + (ExistingTitle == null ? "(empty)" : $"{ExistingTitle}  — will be replaced (backed up first)");
    }

    public SlotPickerWindow(IReadOnlyList<string> profileFolders)
    {
        InitializeComponent();
        FolderBox.ItemsSource = profileFolders;
        FolderBox.SelectedIndex = 0;
    }

    /// <summary>The full path of the chosen slot file.</summary>
    public string? ChosenPath { get; private set; }

    private void FolderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FolderBox.SelectedItem is not string folder) return;
        SlotList.ItemsSource = Enumerable.Range(0, SaveSlots.ManualSlots).Select(i =>
        {
            var path = Path.Combine(folder, SaveSlots.FileName(i));
            return new SlotEntry(i, path, File.Exists(path) ? SgtaFile.ReadTitle(path) ?? "(unreadable file)" : null);
        }).ToList();
        // Default to the first empty slot.
        SlotList.SelectedItem = ((List<SlotEntry>)SlotList.ItemsSource).FirstOrDefault(s => s.ExistingTitle == null);
    }

    private void SlotList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var slot = SlotList.SelectedItem as SlotEntry;
        OkButton.IsEnabled = slot != null;
        WarningText.Text = slot?.ExistingTitle != null ? "This slot is in use." : "";
    }

    private void SlotList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SlotList.SelectedItem != null) Ok_Click(sender, e);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        if (SlotList.SelectedItem is not SlotEntry slot) return;
        if (slot.ExistingTitle != null && MessageBox.Show(this,
                $"Slot {slot.Index + 1} already holds \"{slot.ExistingTitle}\".\n\nReplace it? The current file is backed up first.",
                "Replace save", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            return;
        ChosenPath = slot.Path;
        DialogResult = true;
    }
}
