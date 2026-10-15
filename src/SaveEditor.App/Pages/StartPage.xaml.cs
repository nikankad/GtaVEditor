using System.Diagnostics;
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
    }

    private void ShowKeyState()
    {
        bool needsKey = !App.Game.Keys.HasKey;
        KeyBanner.Visibility = needsKey ? Visibility.Visible : Visibility.Collapsed;
        SelectButton.IsEnabled = !needsKey;
    }

    private void Select_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Title = "Select a GTA V save", Filter = "GTA V saves|SGTA*|All files|*.*" };
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

    private void Backups_Click(object sender, RoutedEventArgs e)
    {
        var dir = BackupService.Default.BackupRoot;
        Directory.CreateDirectory(dir);
        Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
    }

    private void About_Click(object sender, RoutedEventArgs e) => new AboutWindow { Owner = _shell }.ShowDialog();

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
        ShowKeyState();
    }
}
