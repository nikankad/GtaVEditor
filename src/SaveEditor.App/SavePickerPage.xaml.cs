using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using SaveEditor.Core;
using SaveEditor.Games.GtaV;

namespace SaveEditor.App;

/// <summary>Second page: the chosen game's saves; opening one goes to that game's editor.</summary>
public partial class SavePickerPage : UserControl
{
    private readonly MainWindow _shell;
    private readonly IGameModule _game;

    public SavePickerPage(MainWindow shell, IGameModule game)
    {
        InitializeComponent();
        _shell = shell;
        _game = game;
        GameTitle.Text = game.DisplayName;
        Refresh();
    }

    private void Refresh()
    {
        bool needsKey = _game is GtaVModule gta && !gta.Keys.HasKey;
        KeyBanner.Visibility = needsKey ? Visibility.Visible : Visibility.Collapsed;
        KeyButton.Visibility = _game is GtaVModule && !needsKey ? Visibility.Visible : Visibility.Collapsed;

        var items = SaveLibrary.Find(_game);
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
        var dlg = new OpenFileDialog { Title = $"Open a {_game.DisplayName} save", Filter = "All files|*.*" };
        var folder = _game.DefaultSaveFolders().FirstOrDefault();
        if (folder != null) dlg.InitialDirectory = folder;
        if (dlg.ShowDialog(_shell) == true) Open(dlg.FileName);
    }

    private void Open(string path)
    {
        ISaveDocument doc;
        try
        {
            doc = _game.Open(path);
        }
        catch (Exception ex) when (ex is SaveFormatException or IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(_shell, ex.Message, "Cannot open save", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (doc is GtaVSaveDocument gta)
            _shell.Navigate(new GtaVEditorPage(_shell, _game, gta));
        else
            MessageBox.Show(_shell, $"There is no editor for {_game.DisplayName} saves yet.", "Cannot edit",
                MessageBoxButton.OK, MessageBoxImage.Information);
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
        var dir = Path.Combine(BackupService.Default.BackupRoot, _game.Id);
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    private void ShowInFolder_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedPath is { } path) Process.Start("explorer.exe", $"/select,\"{path}\"");
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _shell.Navigate(new GamePickerPage(_shell));

    private void ImportKey_Click(object sender, RoutedEventArgs e)
    {
        if (_game is not GtaVModule gta) return;
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
