using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SaveEditor.GtaV;

namespace SaveEditor.App;

public partial class BackupsWindow : Window
{
    public string? ChosenPath { get; private set; }
    public bool OpenedBackup { get; private set; }

    public BackupsWindow()
    {
        InitializeComponent();
        try
        {
            var backups = BackupService.Default.Entries();
            BackupList.ItemsSource = backups;
            Note.Text = !App.Game.Keys.HasKey ? "Import your encryption key before opening or restoring backups."
                : backups.Count == 0 ? "No backups yet. The editor creates one before replacing a save." : $"{backups.Count} backups, newest first";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Note.Text = "Couldn't read the backups: " + ex.Message;
        }
    }

    private void Selection_Changed(object sender, SelectionChangedEventArgs e) =>
        OpenButton.IsEnabled = RestoreButton.IsEnabled = App.Game.Keys.HasKey && BackupList.SelectedItem != null;

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Game.Keys.HasKey || BackupList.SelectedItem is not SaveBackup backup) return;
        ChosenPath = backup.FilePath;
        OpenedBackup = true;
        DialogResult = true;
    }

    private void Restore_Click(object sender, RoutedEventArgs e)
    {
        if (!App.Game.Keys.HasKey || BackupList.SelectedItem is not SaveBackup backup) return;
        try
        {
            // Check the key, body and checksum before offering to overwrite a game save.
            var doc = App.Game.Open(backup.FilePath);
            if (!doc.Integrity.IsValid) throw new SaveFormatException("This backup has a checksum error: " + doc.Integrity.Summary);
            var dialog = new SaveFileDialog
            {
                Title = "Restore backup to a save file", Filter = "GTA V saves|SGTA*|All files|*.*", AddExtension = false,
                FileName = backup.OriginalPath == null ? Path.GetFileName(backup.FilePath).Split('.')[0] : Path.GetFileName(backup.OriginalPath),
                InitialDirectory = backup.OriginalPath == null ? App.Game.DefaultSaveFolders().FirstOrDefault() : Path.GetDirectoryName(backup.OriginalPath),
            };
            if (dialog.ShowDialog(this) != true) return;
            var previous = BackupService.Default.Restore(backup.FilePath, dialog.FileName);
            MessageBox.Show(this, "Restored to " + dialog.FileName + (previous == null ? "." : ".\n\nThe previous file was backed up to " + previous)
                + "\n\nRestart the game to see the restored save.", "Backup restored", MessageBoxButton.OK, MessageBoxImage.Information);
            ChosenPath = dialog.FileName;
            DialogResult = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveFormatException)
        {
            MessageBox.Show(this, ex.Message, "Cannot restore backup", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void Folder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(BackupService.Default.BackupRoot);
            Process.Start(new ProcessStartInfo("explorer.exe", BackupService.Default.BackupRoot) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, ex.Message, "Cannot open folder", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }
}
