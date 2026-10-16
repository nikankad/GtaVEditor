using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>
/// The mod shop for a car parked in a safehouse garage: a menu of categories on the left, each opening its options,
/// and the car on the right. Choices write straight into the save; Cancel puts the car back the way it was when the
/// window opened.
/// </summary>
public partial class VehicleCustomiseWindow : Window
{
    private readonly GarageSlot _slot;
    private readonly byte[] _before;
    private readonly Action _changed;
    private readonly ModShopModel _model;

    public VehicleCustomiseWindow(GarageSlot slot, Action changed)
    {
        InitializeComponent();
        _slot = slot;
        _before = slot.SnapshotLook();
        _changed = changed;
        DataContext = _model = new ModShopModel(slot, changed);
        CollectionViewSource.GetDefaultView(_model.Categories).GroupDescriptions.Add(new PropertyGroupDescription(nameof(ModCategory.Group)));
        Loaded += (_, _) => ShowMenu(null);
    }

    private void ShowMenu(ModCategory? select)
    {
        MenuList.SelectedItem = select ?? _model.Categories.FirstOrDefault();
        if (MenuList.SelectedItem != null) MenuList.ScrollIntoView(MenuList.SelectedItem);
        FocusSelected(MenuList);
    }

    private void Open(ModCategory category)
    {
        _model.OpenCategory(category);
        if (_model.IsMenu)
        {
            // An action (Max performance, Reset) ran; stay on it.
            MenuList.SelectedItem = category;
            return;
        }
        if (category.IsPlateText)
        {
            PlateBox.Focus();
            PlateBox.SelectAll();
            return;
        }
        OptionList.SelectedItem = _model.Options.FirstOrDefault(o => o.IsInstalled) ?? _model.Options.FirstOrDefault();
        OptionList.UpdateLayout();
        if (OptionList.SelectedItem != null) OptionList.ScrollIntoView(OptionList.SelectedItem);
        FocusSelected(OptionList);
    }

    private void GoBack()
    {
        var was = _model.Open;
        _model.Back();
        ShowMenu(was);
    }

    /// <summary>Puts the keyboard on the picked row, so the arrow keys carry on from there.</summary>
    private static void FocusSelected(ListBox list)
    {
        list.UpdateLayout();
        if (list.ItemContainerGenerator.ContainerFromItem(list.SelectedItem) is ListBoxItem item) item.Focus();
        else list.Focus();
    }

    private void Row_Click(object sender, MouseButtonEventArgs e)
    {
        switch ((sender as FrameworkElement)?.DataContext)
        {
            case ModCategory c: Open(c); e.Handled = true; break;
            case ModOption o: _model.Choose(o); break;
        }
    }

    private void Row_MouseEnter(object sender, MouseEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ModOption o && (o.Picture != null || o.Swatch != null)) _model.Preview = o;
    }

    private void Option_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (OptionList.SelectedItem is ModOption o) _model.Preview = o;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var typing = Keyboard.FocusedElement is TextBox;
        switch (e.Key)
        {
            case Key.Escape when !_model.IsMenu:
            case Key.Back when !_model.IsMenu && !typing:
                GoBack();
                e.Handled = true;
                break;
            case Key.Enter when _model.IsMenu && MenuList.SelectedItem is ModCategory c:
                Open(c);
                e.Handled = true;
                break;
            case Key.Enter when !_model.IsMenu && !typing && OptionList.SelectedItem is ModOption o:
                _model.Choose(o);
                e.Handled = true;
                break;
            case Key.Enter when typing:
                GoBack();
                e.Handled = true;
                break;
        }
    }

    private void Back_Click(object sender, RoutedEventArgs e) => GoBack();

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _slot.RestoreLook(_before);
        _changed();
        Close();
    }
}
