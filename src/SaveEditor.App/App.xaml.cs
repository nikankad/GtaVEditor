using System.Windows;
using SaveEditor.GtaV;

namespace SaveEditor.App;

public partial class App : Application
{
    /// <summary>GTA V on this PC: its save folders and the imported decryption key.</summary>
    public static GtaVGame Game { get; } = new();
}
