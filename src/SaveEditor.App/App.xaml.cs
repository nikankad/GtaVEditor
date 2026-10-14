using System.Windows;
using SaveEditor.Core;
using SaveEditor.Games.GtaV;

namespace SaveEditor.App;

public partial class App : Application
{
    /// <summary>All supported games. Add new game modules here (e.g. RDR 2).</summary>
    public static GameRegistry Games { get; } = new GameRegistry()
        .Register(new GtaVModule());
}
