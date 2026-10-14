using System.Windows;
using System.Windows.Controls;
using SaveEditor.Core;

namespace SaveEditor.App;

/// <summary>First page: one card per supported game.</summary>
public partial class GamePickerPage : UserControl
{
    private sealed record GameCard(IGameModule Module, string Name, string Summary)
    {
        public override string ToString() => Name; // what screen readers announce for the card
    }

    private readonly MainWindow _shell;

    public GamePickerPage(MainWindow shell)
    {
        InitializeComponent();
        _shell = shell;
        GameList.ItemsSource = App.Games.Modules.Select(m =>
        {
            int saves = SaveLibrary.Find(m).Count;
            return new GameCard(m, m.DisplayName, saves switch
            {
                0 => "No saves found on this PC yet",
                1 => "1 save found",
                _ => $"{saves} saves found",
            });
        }).ToList();
    }

    private void Game_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is GameCard card)
            _shell.Navigate(new SavePickerPage(_shell, card.Module));
    }
}
