using System.Windows;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>
/// Paint, lights, plate, wheels, upgrades, body parts and livery for a car parked in a safehouse garage. Choices write
/// straight into the save; Cancel puts the car back the way it was when the window opened.
/// </summary>
public partial class VehicleCustomiseWindow : Window
{
    private readonly GarageSlot _slot;
    private readonly byte[] _before;
    private readonly Action _changed;

    public VehicleCustomiseWindow(GarageSlot slot, Action changed)
    {
        InitializeComponent();
        _slot = slot;
        _before = slot.SnapshotLook();
        _changed = changed;
        DataContext = new VehicleLookModel(slot, changed);
    }

    private void Done_Click(object sender, RoutedEventArgs e) => Close();

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        _slot.RestoreLook(_before);
        _changed();
        Close();
    }

    private void MaxPerformance_Click(object sender, RoutedEventArgs e) => (DataContext as VehicleLookModel)?.MaxPerformance();

    private void ResetParts_Click(object sender, RoutedEventArgs e) => (DataContext as VehicleLookModel)?.ResetBodyParts();
}
