using System.Windows;
using System.Windows.Controls;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>The Game Flags tab: the story flow's named on/off flags, with search.</summary>
public partial class GameFlagsView : UserControl
{
    private readonly List<GameFlagModel> _flags = [];

    public GameFlagsView(GtaVSaveDocument doc, Action onChanged)
    {
        InitializeComponent();
        try
        {
            _flags = doc.Flags.Select(f => new GameFlagModel(f, onChanged)).ToList();
        }
        catch (SaveFormatException ex)
        {
            Error.Text = "The game flags couldn't be read from this save: " + ex.Message;
            return;
        }
        Filter();
    }

    private void Filter()
    {
        var search = Search.Text.Trim();
        var shown = _flags.Where(f => search.Length == 0 || f.Matches(search)).ToList();
        Flags.ItemsSource = shown;
        CountText.Text = $"{shown.Count} of {_flags.Count} flags · {_flags.Count(f => f.On)} on";
    }

    private void Search_Changed(object sender, RoutedEventArgs e) => Filter();

    public void Refresh()
    {
        _flags.ForEach(f => f.Refresh());
        Filter();
    }
}
