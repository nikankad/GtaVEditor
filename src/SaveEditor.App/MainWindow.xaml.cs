using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;

namespace SaveEditor.App;

/// <summary>A page that may hold unsaved edits; asked before the user leaves it or closes the app.</summary>
public interface IGuardedPage
{
    /// <summary>Returns false to stay on the page.</summary>
    bool ConfirmLeave();
}

/// <summary>The app shell: shows one page at a time (game picker → save picker → editor).</summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Navigate(new GamePickerPage(this));
    }

    /// <summary>Shows <paramref name="page"/> unless the current page wants to stay (unsaved changes).</summary>
    public bool Navigate(UserControl page)
    {
        if (PageHost.Content is IGuardedPage current && !current.ConfirmLeave()) return false;
        PageHost.Content = page;
        SetStatus("");
        return true;
    }

    public void SetStatus(string text) => StatusText.Text = text;

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (PageHost.Content is IGuardedPage page && !page.ConfirmLeave()) e.Cancel = true;
    }
}
