using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SaveEditor.App;
using SaveEditor.GtaV;
using SaveEditor.TestSupport;

internal static class Program
{
    private static readonly string _folder = Directory.CreateTempSubdirectory().FullName;

    [STAThread]
    private static int Main()
    {
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/GtaVSaveEditor;component/Themes/GtaTheme.xaml"),
        });
        MainWindow? shell = null;
        try
        {
            shell = new MainWindow();
            shell.Show();
            Pump();
            var doc = SyntheticSave.Build(_folder, new("M_ARM1", 27.5f, true, 1), new("M_ARM2", 27.5f, false, 1));
            var page = new EditorPage(shell, doc);
            Check(shell.Navigate(page), "Navigate to editor");
            Pump();
            var items = (ItemsControl)page.FindName("ChecklistItems");
            var filter = (CheckBox)page.FindName("ChecklistIncomplete");
            var save = (Button)page.FindName("SaveButton");
            Check(items.Items.Count == 2, "Both checklist entries shown");
            filter.IsChecked = true;
            Pump();
            Check(items.Items.Count == 1, "Incomplete filter hides completed entry");

            var missing = (ChecklistItemModel)items.Items[0];
            missing.Done = true;
            Pump();
            Check(items.Items.Count == 0, "Completing an entry updates the filtered list");
            Check(save.IsEnabled, "An edit enables Save");
            missing.Done = false;
            Pump();
            Check(items.Items.Count == 1, "Reverting an entry returns it to the filtered list");
            Check(!save.IsEnabled && !doc.HasChanges, "Reverting the edit clears unsaved state");

            var search = (TextBox)page.FindName("SearchBox");
            search.Text = doc.Checklist[0].Name;
            Pump();
            var results = (ListBox)page.FindName("SearchResults");
            results.SelectedItem = results.Items.Cast<SearchHit>().First(h => h.Section == "Checklist" && h.Label == doc.Checklist[0].Name);
            results.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(shell), 0, Key.Enter)
                { RoutedEvent = Keyboard.KeyDownEvent });
            Pump();
            Check(filter.IsChecked == false && items.Items.Count == 2, "Search reveals an entry hidden by the filter");
            Check(Descendants(page).Any(e => AdornerLayer.GetAdornerLayer(e)?.GetAdorners(e)?.Length > 0), "Search target is highlighted");

            var backupWindow = new BackupsWindow();
            backupWindow.Show();
            Pump();
            Check(backupWindow.FindName("BackupList") is ListBox, "Backup browser renders");
            backupWindow.Close();
            Check(shell.Navigate(new StartPage(shell)), "Return to detected saves");
            Pump();
            Console.WriteLine("PASS: Windows UI initialization, completion filtering, reverting edits, search reveal/highlight, backup browser and start page.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
        finally
        {
            // A failed assertion can leave the editor dirty; avoid a modal leave prompt during test cleanup.
            if (shell != null) { ((ContentControl)shell.FindName("PageHost")).Content = null; shell.Close(); }
            app.Shutdown();
            Directory.Delete(_folder, recursive: true);
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(() => frame.Continue = false, DispatcherPriority.ApplicationIdle);
        Dispatcher.PushFrame(frame);
    }

    private static IEnumerable<UIElement> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is UIElement element) yield return element;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

}
