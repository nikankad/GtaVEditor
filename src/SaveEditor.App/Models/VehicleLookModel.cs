using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>A choice with a colour swatch, for the paint and light colour lists.</summary>
public sealed record SwatchChoice(object Value, string Name, Brush Swatch)
{
    public static Brush Brush(string hex)
    {
        var brush = new SolidColorBrush(Color.FromRgb(Convert.ToByte(hex[..2], 16), Convert.ToByte(hex[2..4], 16), Convert.ToByte(hex[4..6], 16)));
        brush.Freeze();
        return brush;
    }
}

/// <summary>A choice with a picture from the game, for plates and rims.</summary>
public sealed record PictureChoice(int Value, string Name, ImageSource? Picture);

/// <summary>One mod-shop part (spoiler, bumper…) with its option names; the first option is always "Stock".</summary>
public sealed class PartRowModel(VehicleLook look, PartOptions part, Action onChanged) : Observable
{
    public string Label => part.Label;
    public IReadOnlyList<string> Options { get; } = ["Stock", .. part.Names];

    public int Selected
    {
        get => look.PartOption(part) + 1;
        set
        {
            if (value < 0 || value == Selected) return;
            look.SetPart(part, value - 1);
            onChanged();
            Refresh();
        }
    }
}

/// <summary>The customise window for a parked car: every choice writes straight into the save.</summary>
public sealed class VehicleLookModel : Observable, IDataErrorInfo
{
    private readonly GarageSlot _slot;
    private readonly VehicleLook _look;
    private readonly Action _onChanged;
    private string? _plateError, _platePending;
    private (int Type, IReadOnlyList<PictureChoice> List)? _designs;

    public VehicleLookModel(GarageSlot slot, Action onChanged)
    {
        _slot = slot;
        _look = slot.CarLook;
        _onChanged = onChanged;
        Paints = VehicleColour.Full.Concat(new[] { _look.PrimaryColour, _look.SecondaryColour, _look.PearlescentColour, _look.WheelColour,
                _look.InteriorColour, _look.DashboardColour }.Where(i => i >= VehicleColour.Full.Count).Distinct()
            .Select(i => new VehicleColour(i, VehicleColour.NameOf(i))))
            .Select(c => new SwatchChoice(c.Index, c.Name, SwatchChoice.Brush(c.Hex))).ToList();
        NeonColours = Lights(LightColour.Neon, _look.NeonColour);
        SmokeColours = Lights(LightColour.TyreSmoke, _look.TyreSmokeColour);
        PlateStyles = GarageSlot.PlateStyles.Concat(GarageSlot.PlateStyles.Any(s => s.Value == slot.PlateStyle) ? []
                : new[] { new LookOption(slot.PlateStyle, $"Plate style {slot.PlateStyle}") })
            .Select(s => new PictureChoice(s.Value, s.Name, GamePictures.Plate(s.Value))).ToList();
        PartRows = _look.BodyParts.Select(p => new PartRowModel(_look, p, onChanged)).ToList();
        LiveryNames = ["None", .. _look.Liveries];
    }

    public VehicleLook Look => _look;
    public string VehicleName => _slot.VehicleName;
    public string Where => $"{_slot.Garage}, space {_slot.Number}";
    public ImageSource? Picture => GamePictures.Vehicle(_slot.ModelHash);

    // ---- Paint ----

    public IReadOnlyList<SwatchChoice> Paints { get; }

    public int Primary { get => _look.PrimaryColour; set => Set(() => _look.PrimaryColour = value); }
    public int Secondary { get => _look.SecondaryColour; set => Set(() => _look.SecondaryColour = value); }
    public int Pearlescent { get => _look.PearlescentColour; set => Set(() => _look.PearlescentColour = value); }
    public int WheelColour { get => _look.WheelColour; set => Set(() => _look.WheelColour = value); }
    public int Interior { get => _look.InteriorColour; set => Set(() => _look.InteriorColour = value); }
    public int Dashboard { get => _look.DashboardColour; set => Set(() => _look.DashboardColour = value); }

    // ---- Windows and lights ----

    public IReadOnlyList<LookOption> WindowTints { get; } = VehicleLook.WindowTints;

    public int WindowTint { get => _look.WindowTint; set => Set(() => _look.WindowTint = value); }

    public bool NeonLeft { get => _look.NeonLeft; set => Set(() => _look.NeonLeft = value); }
    public bool NeonRight { get => _look.NeonRight; set => Set(() => _look.NeonRight = value); }
    public bool NeonFront { get => _look.NeonFront; set => Set(() => _look.NeonFront = value); }
    public bool NeonBack { get => _look.NeonBack; set => Set(() => _look.NeonBack = value); }

    public IReadOnlyList<SwatchChoice> NeonColours { get; }

    public SwatchChoice? NeonColour
    {
        get => NeonColours.FirstOrDefault(c => Same((LightColour)c.Value, _look.NeonColour));
        set { if (value != null) Set(() => _look.NeonColour = ToTuple((LightColour)value.Value)); }
    }

    public bool TyreSmoke { get => _look.TyreSmoke; set => Set(() => _look.TyreSmoke = value); }

    public IReadOnlyList<SwatchChoice> SmokeColours { get; }

    public SwatchChoice? SmokeColour
    {
        get => SmokeColours.FirstOrDefault(c => Same((LightColour)c.Value, _look.TyreSmokeColour));
        set { if (value != null) Set(() => _look.TyreSmokeColour = ToTuple((LightColour)value.Value)); }
    }

    public bool XenonLights { get => _look.XenonLights; set => Set(() => _look.XenonLights = value); }

    // ---- Number plate ----

    public IReadOnlyList<PictureChoice> PlateStyles { get; }
    public int PlateStyle { get => _slot.PlateStyle; set => Set(() => _slot.PlateStyle = value); }

    /// <summary>The plate's text; a wrong entry stays in the box with a message until it's fixed.</summary>
    public string PlateText
    {
        get => _platePending ?? _slot.PlateText;
        set
        {
            _plateError = _platePending = null;
            try
            {
                if (!string.Equals(value.Trim(), _slot.PlateText, StringComparison.OrdinalIgnoreCase))
                {
                    _slot.PlateText = value.Trim();
                    _onChanged();
                }
            }
            catch (ArgumentException ex)
            {
                (_plateError, _platePending) = (ex.Message, value);
            }
            Refresh();
        }
    }

    public string PlateError => _plateError ?? "";
    public string Error => _plateError ?? "";
    public string this[string column] => column == nameof(PlateText) ? _plateError ?? "" : "";

    // ---- Wheels ----

    public IReadOnlyList<LookOption> WheelTypes => _look.AllowedWheelTypes
        .Concat(_look.AllowedWheelTypes.Any(t => t.Value == _look.WheelType) ? [] : new[] { new LookOption(_look.WheelType, $"Wheel type no. {_look.WheelType}") })
        .ToList();

    /// <summary>Changing the wheel type puts the car's own rims back, since the design list is a different one.</summary>
    public int WheelType
    {
        get => _look.WheelType;
        set
        {
            if (value == _look.WheelType) return;
            _look.WheelType = value;
            if (_look.HasModKit) _look.WheelDesign = 255;
            _onChanged();
            Refresh();
        }
    }

    /// <summary>The rims for the current wheel type, with the game's picture where its order is known.</summary>
    public IReadOnlyList<PictureChoice> WheelDesigns
    {
        get
        {
            if (_designs is { } d && d.Type == _look.WheelType) return d.List;
            var type = _look.WheelType;
            var list = VehicleLook.WheelDesigns(type).Select(o =>
                new PictureChoice(o.Value, o.Name, VehicleLook.NamedWheel(type, o.Value) is { } n ? GamePictures.Rim(type, n) : null)).ToList();
            if (!list.Any(o => o.Value == _look.WheelDesign))
                list.Add(new PictureChoice(_look.WheelDesign, $"Design no. {_look.WheelDesign + 1}", null));
            _designs = (type, list);
            return list;
        }
    }

    public int WheelDesign { get => _look.WheelDesign; set => Set(() => _look.WheelDesign = value); }
    public bool CustomTyres { get => _look.CustomTyres; set => Set(() => _look.CustomTyres = value); }

    // ---- Mod shop parts ----

    public bool HasModKit => _look.HasModKit;
    public bool CanUpgrade => _look.CanUpgrade;
    public Visibility NoKitVisibility => Show(!_look.HasModKit);
    public Visibility NoUpgradeVisibility => Show(!_look.CanUpgrade);
    public Visibility UpgradeVisibility => Show(_look.CanUpgrade);

    public string NoUpgradeText => _look.HasModKit
        ? "This car's mod shop stores its upgrades in its own order, so the editor leaves them alone. Change them at a Los Santos Customs in game."
        : "Upgrades need a visit to a mod shop first (see above).";

    public Visibility EngineVisibility => Show(_look.HasEngine);
    public Visibility BrakesVisibility => Show(_look.HasBrakes);
    public Visibility TransmissionVisibility => Show(_look.HasTransmission);
    public Visibility ArmourVisibility => Show(_look.HasArmour);
    public Visibility SuspensionVisibility => Show(_look.HasSuspension);

    // Only as many levels as the car's kit has room for.
    public IReadOnlyList<LookOption> EngineLevels => VehicleLook.EngineLevels.Take(_look.EngineLevelCount + 1).ToList();
    public IReadOnlyList<LookOption> BrakeLevels => VehicleLook.BrakeLevels.Take(_look.BrakeLevelCount + 1).ToList();
    public IReadOnlyList<LookOption> TransmissionLevels => VehicleLook.TransmissionLevels.Take(_look.TransmissionLevelCount + 1).ToList();
    public IReadOnlyList<LookOption> ArmourLevels => VehicleLook.ArmourLevels.Take(_look.ArmourLevelCount + 1).ToList();
    public IReadOnlyList<LookOption> SuspensionLevels => VehicleLook.SuspensionLevels.Take(_look.SuspensionLevelCount + 1).ToList();

    public int Engine { get => _look.Engine; set => Set(() => _look.Engine = value, _look.HasEngine); }
    public int Brakes { get => _look.Brakes; set => Set(() => _look.Brakes = value, _look.HasBrakes); }
    public int Transmission { get => _look.Transmission; set => Set(() => _look.Transmission = value, _look.HasTransmission); }
    public int Armour { get => _look.Armour; set => Set(() => _look.Armour = value, _look.HasArmour); }
    public int Suspension { get => _look.Suspension; set => Set(() => _look.Suspension = value, _look.HasSuspension); }
    public bool Turbo { get => _look.Turbo; set => Set(() => _look.Turbo = value, _look.HasModKit); }

    public void MaxPerformance()
    {
        if (!_look.CanUpgrade) return;
        _look.MaxPerformance();
        _onChanged();
        Refresh();
    }

    // ---- Body parts and liveries ----

    public IReadOnlyList<PartRowModel> PartRows { get; }
    public Visibility PartsVisibility => Show(_look.HasModKit);

    public IReadOnlyList<string> LiveryNames { get; }

    /// <summary>The chosen livery as the game pictures it, where the game has the picture.</summary>
    public ImageSource? LiveryPicture => _look.Livery >= 0 ? GamePictures.Livery(_slot.ModelHash, _look.Livery) : null;
    public Visibility LiveryPictureVisibility => Show(LiveryPicture != null);
    public Visibility LiveryVisibility => Show(_look.Liveries.Count > 0);

    /// <summary>0 for none, then the liveries in the game's order.</summary>
    public int Livery
    {
        get => _look.Livery + 1;
        set
        {
            if (value < 0 || _look.Liveries.Count == 0 || value == Livery) return;
            _look.Livery = value - 1;
            _onChanged();
            Refresh();
        }
    }

    public string BodyNote => PartRows.Count > 0 ? "Pick an option for each part; Stock leaves the car's own."
        : "The editor doesn't have a parts list for this car, so it can only put its parts back to stock.";

    public void ResetBodyParts()
    {
        _look.ResetBodyParts();
        _onChanged();
        Refresh();
        foreach (var row in PartRows) row.Refresh();
    }

    private void Set(Action write, bool allowed = true)
    {
        if (!allowed) return;
        var before = _slot.SnapshotLook();
        write();
        if (!before.AsSpan().SequenceEqual(_slot.SnapshotLook())) _onChanged();
        Refresh();
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    private static (byte R, byte G, byte B) ToTuple(LightColour c) => (c.R, c.G, c.B);
    private static bool Same(LightColour l, (byte R, byte G, byte B) c) => l.R == c.R && l.G == c.G && l.B == c.B;

    private static IReadOnlyList<SwatchChoice> Lights(IReadOnlyList<LightColour> presets, (byte R, byte G, byte B) current)
    {
        var list = presets.ToList();
        if (!list.Any(l => Same(l, current))) list.Add(new LightColour($"Custom ({current.R}, {current.G}, {current.B})", current.R, current.G, current.B));
        return list.Select(l => new SwatchChoice(l, l.Name, SwatchChoice.Brush(l.Hex))).ToList();
    }
}
