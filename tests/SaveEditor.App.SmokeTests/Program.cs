using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using SaveEditor.App;
using SaveEditor.GtaV;

internal static class Program
{
    private static readonly string _folder = Directory.CreateTempSubdirectory().FullName;

    [STAThread]
    private static int Main()
    {
        var app = new App();
        app.InitializeComponent();
        app.StartupUri = null;
        app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        MainWindow? shell = null;
        try
        {
            shell = new MainWindow();
            shell.Show();
            Pump();
            var doc = Build(new("M_ARM1", 27.5f, true, 1), new("M_ARM2", 27.5f, false, 1));
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

    private static byte[] BuildBody(params (string Id, byte[] Data)[] blocks)
    {
        using var stream = new MemoryStream();
        foreach (var (id, data) in blocks.Append(("CHKS", new byte[12])))
        {
            stream.Write(Encoding.ASCII.GetBytes(id));
            var size = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(size, data.Length + 8);
            stream.Write(size);
            stream.Write(data);
        }
        return BlockContainer.Parse(stream.ToArray()).Build();
    }

    private static byte[] BuildSaveFile(byte[] key, string title, byte[] body) =>
        new SgtaFile { Version = 1, Title = title, EncryptedBody = SaveCrypto.Encrypt(body, key) }.Write();

    private const uint RootType = 0xAAAA0101, TableType = 0xAAAA0102, EntryType = 0xAAAA0103;
    private const uint TableField = 0x1A341F71, LabelField = 0x2D4B9B3A, WeightField = 0x8EF72701,
        CompletedField = 0x11782F19, CategoryField = 0xEB5686E6;
    private const int EntryLength = 56;

    private sealed record Entry(string Label, float Weight, bool Done, int Category);

    /// <summary>A save whose script data holds only a 100% checklist with the given entries (no stats).</summary>
    private static GtaVSaveDocument Build(params Entry[] entries)
    {
        static byte[] Struct(int length, params (uint Name, PsoType Type, int Offset, uint Ref)[] fields)
        {
            var b = new byte[12 + fields.Length * 12];
            BinaryPrimitives.WriteUInt32BigEndian(b, (uint)fields.Length);
            BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), length);
            for (int i = 0; i < fields.Length; i++)
            {
                var e = b.AsSpan(12 + i * 12);
                BinaryPrimitives.WriteUInt32BigEndian(e, fields[i].Name);
                e[4] = (byte)fields[i].Type;
                BinaryPrimitives.WriteUInt16BigEndian(e[6..], (ushort)fields[i].Offset);
                BinaryPrimitives.WriteUInt32BigEndian(e[8..], fields[i].Ref);
            }
            return b;
        }

        int tableLength = entries.Length * EntryLength;
        var schema = new (uint Hash, byte[] Data)[]
        {
            (RootType, Struct(tableLength, (TableField, PsoType.Structure, 0, TableType))),
            (TableType, Struct(tableLength, entries.Select((_, i) =>
                (0xEE000000 + (uint)i, PsoType.Structure, i * EntryLength, EntryType)).ToArray())),
            (EntryType, Struct(EntryLength, (LabelField, PsoType.Float4, 0, 0u), (WeightField, PsoType.Float, 32, 0u),
                (CompletedField, PsoType.SInt, 40, 0u), (CategoryField, PsoType.SInt, 48, 0u))),
        };
        var psch = new MemoryStream();
        var index = new byte[4 + schema.Length * 8];
        BinaryPrimitives.WriteInt32BigEndian(index, schema.Length);
        int at = 8 + index.Length;
        for (int i = 0; i < schema.Length; i++)
        {
            BinaryPrimitives.WriteUInt32BigEndian(index.AsSpan(4 + i * 8), schema[i].Hash);
            BinaryPrimitives.WriteInt32BigEndian(index.AsSpan(8 + i * 8), at);
            at += schema[i].Data.Length;
        }
        psch.Write(index);
        foreach (var s in schema) psch.Write(s.Data);

        var psin = new byte[8 + tableLength];
        for (int i = 0; i < entries.Length; i++)
        {
            var e = psin.AsSpan(8 + i * EntryLength);
            Encoding.ASCII.GetBytes(entries[i].Label).CopyTo(e);
            BinaryPrimitives.WriteSingleBigEndian(e[32..], entries[i].Weight);
            BinaryPrimitives.WriteInt32BigEndian(e[40..], entries[i].Done ? 1 : 0);
            BinaryPrimitives.WriteInt32BigEndian(e[48..], entries[i].Category);
        }
        var pmap = new byte[8 + 16];
        BinaryPrimitives.WriteInt32BigEndian(pmap, 1);
        BinaryPrimitives.WriteUInt16BigEndian(pmap.AsSpan(4), 1);
        BinaryPrimitives.WriteUInt32BigEndian(pmap.AsSpan(8), RootType);
        BinaryPrimitives.WriteInt32BigEndian(pmap.AsSpan(12), 8 + 8);
        BinaryPrimitives.WriteInt32BigEndian(pmap.AsSpan(20), tableLength);

        var key = RandomNumberGenerator.GetBytes(32);
        var path = Path.Combine(_folder, "SGTA50000");
        File.WriteAllBytes(path, BuildSaveFile(key, "t",
            BuildBody(("PSIN", psin), ("PMAP", pmap), ("PSCH", psch.ToArray()))));
        return GtaVSaveDocument.Load(path, key);
    }

}
