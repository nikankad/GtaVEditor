using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>First page: the GTA V saves on this PC; opening one goes to the editor.</summary>
public partial class SavePickerPage : UserControl
{
    private readonly MainWindow _shell;

    public SavePickerPage(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        Refresh();
    }

    private void Refresh()
    {
        bool needsKey = !App.Game.Keys.HasKey;
        KeyBanner.Visibility = needsKey ? Visibility.Visible : Visibility.Collapsed;
        KeyButton.Visibility = !needsKey ? Visibility.Visible : Visibility.Collapsed;

        var items = SaveLibrary.Find(App.Game);
        SaveList.ItemsSource = items;
        EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        CountText.Text = items.Count switch { 0 => "", 1 => "1 save", _ => $"{items.Count} saves, newest first" };
        EditButton.IsEnabled = false;
    }

    private string? SelectedPath => (SaveList.SelectedItem as SaveListItem)?.Path;

    private void SaveList_SelectionChanged(object sender, SelectionChangedEventArgs e) => EditButton.IsEnabled = SelectedPath != null;

    private void SaveList_DoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SelectedPath is { } path) Open(path);
    }

    private void Edit_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPath is { } path) Open(path);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Open a GTA V save", Filter = "All files|*.*" };
        var folder = App.Game.DefaultSaveFolders().FirstOrDefault();
        if (folder != null) dlg.InitialDirectory = folder;
        if (dlg.ShowDialog(_shell) == true) Open(dlg.FileName);
    }

    private void Open(string path)
    {
        GtaVSaveDocument doc;
        try
        {
            doc = App.Game.Open(path);
        }
        catch (Exception ex) when (ex is SaveFormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(_shell, ex.Message, "Cannot open save", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        _shell.Navigate(new EditorPage(_shell, doc));
    }

    private void AddFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Add a folder that contains saves" };
        if (dlg.ShowDialog(_shell) != true) return;
        SaveLibrary.AddFolder(dlg.FolderName);
        Refresh();
    }

    private void Backups_Click(object sender, RoutedEventArgs e)
    {
        var dir = BackupService.Default.BackupRoot;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPath is { } path) Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    private void ImportKey_Click(object sender, RoutedEventArgs e)
    {
        var gta = App.Game;
        MessageBox.Show(_shell,
            "GTA V encrypts PC saves with a 32-byte AES key that belongs to Rockstar and is not included with this app.\n\n" +
            "Select a 32-byte key file, then one of your own GTA V saves. The key is only stored if it correctly decrypts that save.",
            "Import key", MessageBoxButton.OK, MessageBoxImage.Information);

        var keyDlg = new OpenFileDialog { Title = "Select 32-byte key file", Filter = "Key files|*.key;*.bin|All files|*.*" };
        if (keyDlg.ShowDialog(_shell) != true) return;
        var saveDlg = new OpenFileDialog { Title = "Select one of your GTA V saves to verify the key" };
        var folder = gta.DefaultSaveFolders().FirstOrDefault();
        if (folder != null) saveDlg.InitialDirectory = folder;
        if (saveDlg.ShowDialog(_shell) != true) return;

        var result = gta.Keys.Import(File.ReadAllBytes(keyDlg.FileName), saveDlg.FileName);
        MessageBox.Show(_shell, result.Summary, "Import key", MessageBoxButton.OK,
            result.IsValid ? MessageBoxImage.Information : MessageBoxImage.Warning);
        Refresh();
    }
}
