using System.Windows;
using System.Windows.Media;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>One choice in a mod shop menu: a name, an optional picture or colour, and whether the car has it fitted.</summary>
public sealed class ModOption(string name, Func<bool> installed, Action apply, ImageSource? picture = null, Brush? swatch = null) : Observable
{
    public string Name => name;
    public ImageSource? Picture => picture;
    public Brush? Swatch => swatch;
    public bool IsInstalled => installed();
    public Visibility InstalledVisibility => IsInstalled ? Visibility.Visible : Visibility.Hidden;
    public Visibility PictureVisibility => picture != null ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SwatchVisibility => swatch != null ? Visibility.Visible : Visibility.Collapsed;
    internal void Apply() => apply();
}

/// <summary>
/// One entry of the mod shop menu (Respray, Wheels, Engine…). It either opens a list of options, runs an action
/// (Max performance), or edits the plate text.
/// </summary>
public sealed class ModCategory : Observable
{
    private readonly Func<IReadOnlyList<ModOption>>? _options;
    private readonly Func<string>? _current;

    public ModCategory(string group, string title, Func<IReadOnlyList<ModOption>> options, string? note = null)
        => (Group, Title, _options, Note) = (group, title, options, note);

    private ModCategory(string group, string title, Func<string>? current, Action? action, bool plate, string? note)
        => (Group, Title, _current, Action, IsPlateText, Note) = (group, title, current, action, plate, note);

    public static ModCategory ForAction(string group, string title, Action action, string note) => new(group, title, null, action, false, note);
    public static ModCategory ForPlateText(string group, Func<string> current) => new(group, "Plate text", current, null, true, null);

    public string Group { get; }
    public string Title { get; }
    public string? Note { get; }
    public Action? Action { get; }
    public bool IsPlateText { get; }

    public IReadOnlyList<ModOption> Options() => _options?.Invoke() ?? [];

    /// <summary>What's fitted now, shown on the right of the menu row.</summary>
    public string Current => _current?.Invoke() ?? (Action != null ? "" : Options().FirstOrDefault(o => o.IsInstalled)?.Name ?? "");
}

/// <summary>
/// The customise window laid out like the game's mod shop: a menu of categories on the left, each opening a list of
/// options with the fitted one ticked, and the car on the right. Every choice writes straight into the save.
/// </summary>
public sealed class ModShopModel : Observable
{
    private readonly VehicleLookModel _m;
    private readonly GarageSlot _slot;
    private ModCategory? _open;
    private IReadOnlyList<ModOption> _options = [];
    private ModOption? _preview;

    public ModShopModel(GarageSlot slot, Action onChanged)
    {
        _slot = slot;
        _m = new VehicleLookModel(slot, onChanged);
        Categories = Build();
        Stats = BuildStats();
    }

    public VehicleLookModel Look => _m;
    public string VehicleName => _m.VehicleName;
    public string Where => _m.Where;
    public ImageSource? Picture => _m.Picture;

    public IReadOnlyList<ModCategory> Categories { get; }

    /// <summary>The category whose options are showing, or null on the main menu.</summary>
    public ModCategory? Open => _open;

    public bool IsMenu => _open == null;
    public Visibility MenuVisibility => Show(_open == null);
    public Visibility BackVisibility => Show(_open != null);
    public Visibility OptionsVisibility => Show(_open is { IsPlateText: false });
    public Visibility PlateVisibility => Show(_open is { IsPlateText: true });

    public string Heading => _open?.Title ?? "Categories";
    public string Counter => _open == null ? $"{Categories.Count(c => c.Action == null)} categories"
        : _options.Count > 0 ? $"{_options.Count} options" : "";
    public string? Note => _open != null ? _open.Note
        : !_m.HasModKit ? "This car has never been to a mod shop, so only its paint, windows, neon and plate can be changed. Drive it into a Los Santos Customs once and save, and the rest opens up."
        : !_m.CanUpgrade ? _m.NoUpgradeText : null;
    public Visibility NoteVisibility => Show(!string.IsNullOrEmpty(Note));

    public IReadOnlyList<ModOption> Options => _options;

    /// <summary>A larger look at the option under the mouse (or the fitted one), when it has a picture or colour.</summary>
    public ModOption? Preview
    {
        get => _preview;
        set { _preview = value; Raise(nameof(Preview)); Raise(nameof(PreviewPicture), nameof(PreviewSwatch), nameof(PreviewVisibility), nameof(PreviewName)); }
    }

    public ImageSource? PreviewPicture => _preview?.Picture;
    public Brush? PreviewSwatch => _preview?.Swatch;
    public string PreviewName => _preview?.Name ?? "";
    public Visibility PreviewVisibility => Show(_preview?.Picture != null || _preview?.Swatch != null);

    public string Summary => _m.Look.Describe();

    /// <summary>Bars for the fitted upgrades, like the game shows beside the car.</summary>
    public IReadOnlyList<StatBar> Stats { get; }
    public Visibility StatsVisibility => Show(Stats.Count > 0);

    public void OpenCategory(ModCategory category)
    {
        if (category.Action != null)
        {
            category.Action();
            RefreshAll();
            return;
        }
        _open = category;
        _options = category.Options();
        Preview = _options.FirstOrDefault(o => o.IsInstalled && o.Picture != null);
        Refresh();
    }

    public void Back()
    {
        _open = null;
        _options = [];
        Preview = null;
        RefreshAll();
    }

    public void Choose(ModOption option)
    {
        option.Apply();
        Preview = option;
        RefreshAll();
    }

    public string PlateText
    {
        get => _m.PlateText;
        set { _m.PlateText = value; Raise(nameof(PlateText)); Raise(nameof(PlateError)); Raise(nameof(Summary)); }
    }

    public string PlateError => _m.PlateError;

    private void RefreshAll()
    {
        foreach (var c in Categories) c.Refresh();
        foreach (var o in _options) o.Refresh();
        foreach (var s in Stats) s.Refresh();
        Refresh();
    }

    // ---- The menu ----

    private List<ModCategory> Build()
    {
        var list = new List<ModCategory>();
        var m = _m;

        // Performance first, like the game.
        if (m.CanUpgrade)
        {
            const string g = "Performance";
            list.Add(ModCategory.ForAction(g, "Max performance", m.MaxPerformance, "Fits the best engine, brakes, transmission and armour, plus turbo."));
            if (m.ArmourVisibility == Visibility.Visible) list.Add(Levels(g, "Armour", m.ArmourLevels, () => m.Armour, v => m.Armour = v));
            if (m.BrakesVisibility == Visibility.Visible) list.Add(Levels(g, "Brakes", m.BrakeLevels, () => m.Brakes, v => m.Brakes = v));
            if (m.EngineVisibility == Visibility.Visible) list.Add(Levels(g, "Engine", m.EngineLevels, () => m.Engine, v => m.Engine = v));
            if (m.SuspensionVisibility == Visibility.Visible) list.Add(Levels(g, "Suspension", m.SuspensionLevels, () => m.Suspension, v => m.Suspension = v));
            if (m.TransmissionVisibility == Visibility.Visible) list.Add(Levels(g, "Transmission", m.TransmissionLevels, () => m.Transmission, v => m.Transmission = v));
            list.Add(Toggle(g, "Turbo", "None", "Turbo tuning", () => m.Turbo, v => m.Turbo = v));
        }
        if (m.HornVisibility == Visibility.Visible)
            list.Add(new ModCategory("Performance", "Horn", () => m.HornNames.Select((n, i) => Option(n, () => m.Horn == i, () => m.Horn = i)).ToList(), m.HornNote));

        // Body parts and livery.
        if (m.HasModKit)
        {
            const string g = "Body";
            foreach (var row in m.PartRows)
                list.Add(new ModCategory(g, row.Label, () => row.Options.Select((n, i) => Option(n, () => row.Selected == i, () => row.Selected = i)).ToList(),
                    m.Look.Info?.PartsNumbered == true ? "The game's names for this car's parts aren't known, so they're numbered in the mod shop's order." : null));
            if (m.LiveryVisibility == Visibility.Visible)
                list.Add(new ModCategory(g, "Livery", () => m.LiveryNames.Select((n, i) => Option(n, () => m.Livery == i, () => m.Livery = i,
                    i > 0 ? GamePictures.Livery(_slot.ModelHash, i - 1) : null)).ToList()));
            list.Add(ModCategory.ForAction(g, "Reset body to stock", m.ResetBodyParts, "Puts every body part, the livery and the rims back to the car's own."));
        }

        // Respray.
        {
            const string g = "Respray";
            list.Add(Paint(g, "Primary colour", () => m.Primary, v => m.Primary = v));
            list.Add(Paint(g, "Secondary colour", () => m.Secondary, v => m.Secondary = v));
            list.Add(Paint(g, "Pearlescent", () => m.Pearlescent, v => m.Pearlescent = v, "The shimmer over the primary paint."));
            list.Add(Paint(g, "Wheel colour", () => m.WheelColour, v => m.WheelColour = v));
            list.Add(Paint(g, "Interior colour", () => m.Interior, v => m.Interior = v, "Only some cars show it."));
            list.Add(Paint(g, "Dashboard colour", () => m.Dashboard, v => m.Dashboard = v, "Only some cars show it."));
        }

        // Wheels.
        if (m.HasModKit)
        {
            const string g = "Wheels";
            list.Add(new ModCategory(g, "Wheel type", () => m.WheelTypes.Select(t => Option(t.Name, () => m.WheelType == t.Value, () => m.WheelType = t.Value)).ToList(),
                "Changing the type puts the car's own rims back, since each type has its own designs."));
            list.Add(new ModCategory(g, "Rims", () => m.WheelDesigns.Select(d => Option(d.Name, () => m.WheelDesign == d.Value, () => m.WheelDesign = d.Value, d.Picture)).ToList()));
            list.Add(Toggle(g, "Tyres", "Stock tyres", "Custom tyres", () => m.CustomTyres, v => m.CustomTyres = v));
            list.Add(new ModCategory(g, "Tyre smoke", () =>
                [Option("No custom smoke", () => !m.TyreSmoke, () => m.TyreSmoke = false),
                 .. m.SmokeColours.Select(c => Option(c.Name, () => m.TyreSmoke && m.SmokeColour == c, () => { m.TyreSmoke = true; m.SmokeColour = c; }, swatch: c.Swatch))]));
        }

        // Lights and windows.
        {
            const string g = "Lights and windows";
            if (m.HasModKit) list.Add(Toggle(g, "Headlights", "Stock lights", "Xenon lights", () => m.XenonLights, v => m.XenonLights = v));
            list.Add(new ModCategory(g, "Neon layout", () => NeonLayouts().Select(l =>
                Option(l.Name, () => (m.NeonFront, m.NeonBack, m.NeonLeft, m.NeonRight) == (l.F, l.B, l.S, l.S), () =>
                {
                    m.NeonFront = l.F;
                    m.NeonBack = l.B;
                    m.NeonLeft = m.NeonRight = l.S;
                })).ToList()));
            list.Add(new ModCategory(g, "Neon colour", () => m.NeonColours.Select(c => Option(c.Name, () => m.NeonColour == c, () => m.NeonColour = c, swatch: c.Swatch)).ToList()));
            list.Add(new ModCategory(g, "Window tint", () => m.WindowTints.Select(t => Option(t.Name, () => m.WindowTint == t.Value, () => m.WindowTint = t.Value)).ToList()));
        }

        // Plate.
        {
            const string g = "Plate";
            list.Add(new ModCategory(g, "Plate style", () => m.PlateStyles.Select(s => Option(s.Name, () => m.PlateStyle == s.Value, () => m.PlateStyle = s.Value, s.Picture)).ToList()));
            list.Add(ModCategory.ForPlateText(g, () => m.PlateText));
        }
        return list;
    }

    private List<StatBar> BuildStats()
    {
        var m = _m;
        var list = new List<StatBar>();
        if (!m.CanUpgrade) return list;
        void Add(string name, bool has, IReadOnlyList<LookOption> levels, Func<int> get)
        {
            if (has && levels.Count > 1) list.Add(new StatBar(name, () => Math.Max(0, levels.ToList().FindIndex(l => l.Value == get())) / (double)(levels.Count - 1)));
        }
        Add("Engine", m.EngineVisibility == Visibility.Visible, m.EngineLevels, () => m.Engine);
        Add("Brakes", m.BrakesVisibility == Visibility.Visible, m.BrakeLevels, () => m.Brakes);
        Add("Transmission", m.TransmissionVisibility == Visibility.Visible, m.TransmissionLevels, () => m.Transmission);
        Add("Suspension", m.SuspensionVisibility == Visibility.Visible, m.SuspensionLevels, () => m.Suspension);
        Add("Armour", m.ArmourVisibility == Visibility.Visible, m.ArmourLevels, () => m.Armour);
        return list;
    }

    private static IEnumerable<(string Name, bool F, bool B, bool S)> NeonLayouts() =>
    [
        ("None", false, false, false), ("Front", true, false, false), ("Back", false, true, false), ("Sides", false, false, true),
        ("Front and back", true, true, false), ("Front and sides", true, false, true), ("Back and sides", false, true, true),
        ("Front, back and sides", true, true, true),
    ];

    private static ModOption Option(string name, Func<bool> installed, Action apply, ImageSource? picture = null, Brush? swatch = null) =>
        new(name, installed, apply, picture, swatch);

    private ModCategory Levels(string group, string title, IReadOnlyList<LookOption> levels, Func<int> get, Action<int> set) =>
        new(group, title, () => levels.Select(l => Option(l.Name, () => get() == l.Value, () => set(l.Value))).ToList());

    private static ModCategory Toggle(string group, string title, string off, string on, Func<bool> get, Action<bool> set) =>
        new(group, title, () => [Option(off, () => !get(), () => set(false)), Option(on, get, () => set(true))]);

    private ModCategory Paint(string group, string title, Func<int> get, Action<int> set, string? note = null) =>
        new(group, title, () => _m.Paints.Select(p => Option(p.Name, () => get() == (int)p.Value, () => set((int)p.Value), swatch: p.Swatch)).ToList(), note);

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;
}

/// <summary>How far one upgrade is from stock (0) to the best the car takes (1).</summary>
public sealed class StatBar(string name, Func<double> value) : Observable
{
    public string Name => name;
    public double Value => value();
}
