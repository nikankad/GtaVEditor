using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace SaveEditor.App;

/// <summary>What the app is, who made it, and where to find the source.</summary>
public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        var shown = version is { Length: > 0 } ? version.Split('+')[0] : "";
        VersionText.Text = shown.Length == 0 ? "" : "Version " + shown + (shown.StartsWith("0.") ? " (beta)" : "");
    }

    private void Link_Navigate(object sender, RequestNavigateEventArgs e)
    {
        // Only ever hand web links to the shell, never a file or custom-protocol URI.
        e.Handled = true;
        if (!e.Uri.IsAbsoluteUri || e.Uri.Scheme != Uri.UriSchemeHttps) return;
        try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
        catch (System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, "Couldn't open your browser. You can visit " + e.Uri.AbsoluteUri,
                "Open link", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
