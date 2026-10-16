using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>First page: pick a save file with a button, then go to the editor.</summary>
public partial class StartPage : UserControl
{
    private readonly MainWindow _shell;

    public StartPage(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        ShowKeyState();
        DetectSaves();
    }

    private void ShowKeyState()
    {
        bool needsKey = !App.Game.Keys.HasKey;
        KeyBanner.Visibility = needsKey ? Visibility.Visible : Visibility.Collapsed;
        SelectButton.IsEnabled = !needsKey;
        DetectedList.IsEnabled = !needsKey;
        OpenDetectedButton.IsEnabled = !needsKey && DetectedList.SelectedItem != null;
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Select a GTA V save", Filter = "GTA V saves|SGTA*|All files|*.*" };
        var folder = App.Game.DefaultSaveFolders().FirstOrDefault();
        if (folder != null) dlg.InitialDirectory = folder;
        if (dlg.ShowDialog(_shell) == true) Open(dlg.FileName);
    }

    private void Open(string path, bool backup = false)
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
        _shell.Navigate(new EditorPage(_shell, doc, backup));
    }

    private void DetectSaves()
    {
        try
        {
            var saves = DetectedSave.Find(App.Game.DefaultSaveFolders());
            DetectedList.ItemsSource = saves;
            DetectedList.SelectedIndex = saves.Count > 0 ? 0 : -1;
            DetectionNote.Text = saves.Count == 0 ? "No saves found. You can still select a save file above." : $"{saves.Count} saves, newest first";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            DetectionNote.Text = "Couldn't scan the game folders. You can still select a save file above.";
        }
    }

    private void RefreshSaves_Click(object sender, RoutedEventArgs e) => DetectSaves();
    private void Detected_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (OpenDetectedButton != null) OpenDetectedButton.IsEnabled = App.Game.Keys.HasKey && DetectedList.SelectedItem != null;
    }
    private void OpenDetected_Click(object sender, RoutedEventArgs e)
    {
        if (App.Game.Keys.HasKey && DetectedList.SelectedItem is DetectedSave save) Open(save.FilePath);
    }
    private void Detected_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e) => OpenDetected_Click(sender, e);

    private void Backups_Click(object sender, RoutedEventArgs e)
    {
        var browser = new BackupsWindow { Owner = _shell };
        if (browser.ShowDialog() == true && browser.ChosenPath is { } path) Open(path, browser.OpenedBackup);
        else DetectSaves();
    }

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = _shell }.ShowDialog();

    private void ImportKey_Click(object sender, RoutedEventArgs e)
    {
        var gta = App.Game;
        MessageBox.Show(_shell,
            "GTA V encrypts PC saves with a 32-byte AES key. This build needs a key before it can open saves.\n\n" +
            "Select a 32-byte key file, then one of your own GTA V saves. The key is only stored if it correctly decrypts that save.",
            "Import key", MessageBoxButton.OK, MessageBoxImage.Information);

        var keyDlg = new OpenFileDialog { Title = "Select 32-byte key file", Filter = "Key files|*.key;*.bin|All files|*.*" };
        if (keyDlg.ShowDialog(_shell) != true) return;
        var saveDlg = new OpenFileDialog { Title = "Select one of your GTA V saves to verify the key" };
        var folder = gta.DefaultSaveFolders().FirstOrDefault();
        if (folder != null) saveDlg.InitialDirectory = folder;
        if (saveDlg.ShowDialog(_shell) != true) return;

        try
        {
            var result = gta.Keys.Import(SgtaFile.ReadAllBytesLimited(keyDlg.FileName, SaveCrypto.KeyLength), saveDlg.FileName);
            MessageBox.Show(_shell, result.Summary, "Import key", MessageBoxButton.OK,
                result.IsValid ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveFormatException)
        {
            MessageBox.Show(_shell, "The key could not be imported. " + ex.Message,
                "Import key", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        ShowKeyState();
        DetectSaves();
    }
}
