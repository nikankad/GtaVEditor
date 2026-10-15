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
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}
