using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SaveEditor.GtaV;

namespace SaveEditor.App;

/// <summary>Third page: friendly editing of one GTA V save (checklist, player, weapons).</summary>
public partial class EditorPage : UserControl, IGuardedPage
{
    private readonly MainWindow _shell;
    private readonly GtaVSaveDocument _doc;
    private bool _dirty;

    private List<CharacterModel> _characters = [];
    private readonly Dictionary<string, List<WeaponGroup>> _weapons = [];
    private string _weaponCharacter = StatCatalog.Characters[0];

    public EditorPage(MainWindow shell, GtaVSaveDocument doc)
    {
        InitializeComponent();
        _shell = shell;
        _doc = doc;

        ShowHeader();
        LoadChecklist();
        LoadMissions();
        LoadStrangers();
        LoadCharacters();
        LoadStatus();
        LoadStats();
        LoadCrew();
        LoadGarages();
        LoadPropertyVehiclesAndProperties();
        LoadWeapons();
        if (!doc.Integrity.IsValid)
            shell.SetStatus("Warning: this save's checksum was already wrong before editing. " + doc.Integrity.Summary);
    }

    private void ShowHeader()
    {
        TitleText.Text = _doc.Title;
        SubtitleText.Text = $"{SaveSlots.Describe(_doc.FilePath)} · {_doc.FilePath}" + (_dirty ? " · unsaved changes" : "");
    }

    private void Changed()
    {
        if (_dirty) return;
        _dirty = true;
        SaveButton.IsEnabled = true;
        ShowHeader();
    }

    // ---- Characters ----

    private void LoadCharacters()
    {
        try
        {
            _characters = StatCatalog.Characters.Select(c => new CharacterModel(_doc, c, Changed)).ToList();
            CharacterList.ItemsSource = _characters;
        }
        catch (SaveFormatException ex)
        {
            CharactersError.Text = "Character data couldn't be read from this save: " + ex.Message;
        }
    }

    private void MaxSkills_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is CharacterModel c) c.MaxSkills();
    }

    private void MaxEveryonesSkills_Click(object sender, RoutedEventArgs e) => _characters.ForEach(c => c.MaxSkills());

    // ---- Status and clock ----

    private List<StatusModel> _status = [];
    private ClockModel? _clock;

    private void LoadStatus()
    {
        PlayerStatus player;
        try
        {
            player = _doc.Player;
        }
        catch (SaveFormatException ex)
        {
            StatusError.Text = "Health, armor and the other status values couldn't be read from this save: " + ex.Message;
            ClockCard.Visibility = Visibility.Collapsed;
            return;
        }
        _status = player.Characters.Select(c => new StatusModel(c, c.Index == player.CurrentCharacter, Changed)).ToList();
        StatusList.ItemsSource = _status;
        if (player.Clock == null) ClockCard.Visibility = Visibility.Collapsed;
        else ClockCard.DataContext = _clock = new ClockModel(player.Clock, Changed);
    }

    private void MaxStatus_Click(object sender, RoutedEventArgs e) => _status.ForEach(s => s.MaxAll());

    // ---- Vehicles ----

    private List<GarageSlotModel> _garageSlots = [];

    private void LoadGarages()
    {
        try
        {
            _garageSlots = _doc.Garages.Select(s => new GarageSlotModel(s, Changed)).ToList();
        }
        catch (SaveFormatException ex)
        {
            GaragesError.Text = "The garages couldn't be read from this save: " + ex.Message;
            return;
        }
        GarageList.ItemsSource = _garageSlots.GroupBy(s => s.Slot.Garage)
            .Select(g => new GarageGroup(g.Key, g.First().Slot.Owner, g.ToList())).ToList();
    }

    private List<PropertyVehicleModel> _propertyVehicles = [];
    private List<PropertyModel> _properties = [];

    private void LoadPropertyVehiclesAndProperties()
    {
        try
        {
            _propertyVehicles = _doc.PropertyVehicles.Select(s => new PropertyVehicleModel(s, Changed).Init()).ToList();
            PropertyVehicleList.ItemsSource = new[]
            {
                new PropertyVehicleGroup("Hangars", _propertyVehicles.Where((_, i) => i is >= 0 and < 3).ToList()),
                new PropertyVehicleGroup("Helipads", _propertyVehicles.Where((_, i) => i is >= 6 and < 9).ToList()),
                new PropertyVehicleGroup("Boat docks", _propertyVehicles.Where((_, i) => i is >= 3 and < 6).ToList()),
            };
        }
        catch (SaveFormatException ex)
        {
            PropertyVehiclesError.Text = "The hangars, helipads and docks couldn't be read from this save: " + ex.Message;
        }
        try
        {
            _properties = _doc.Properties.Select(p => new PropertyModel(p, Changed)).ToList();
            PropertyList.ItemsSource = _properties;
        }
        catch (SaveFormatException ex)
        {
            PropertiesError.Text = "The businesses couldn't be read from this save: " + ex.Message;
        }
    }

    private void GivePropertyVehicle_Click(object sender, RoutedEventArgs e) =>
        ((sender as FrameworkElement)?.DataContext as PropertyVehicleModel)?.Park();

    private void EmptyPropertyVehicle_Click(object sender, RoutedEventArgs e) =>
        ((sender as FrameworkElement)?.DataContext as PropertyVehicleModel)?.Empty();

    private void BuyAllProperties_Click(object sender, RoutedEventArgs e)
    {
        foreach (var p in _doc.Properties.Where(p => p.OwnerIndex == null)) p.OwnerIndex = p.UsualOwner;
        Changed();
        _properties.ForEach(p => p.Refresh());
    }

    private void ParkHere_Click(object sender, RoutedEventArgs e) => ((sender as FrameworkElement)?.DataContext as GarageSlotModel)?.Park();

    private void EmptySpace_Click(object sender, RoutedEventArgs e) => ((sender as FrameworkElement)?.DataContext as GarageSlotModel)?.Empty();

    // ---- Heist crew ----

    private HeistCrew? _crew;

    private void LoadCrew()
    {
        try
        {
            _crew = _doc.Crew;
        }
        catch (SaveFormatException)
        {
            CrewCard.Visibility = Visibility.Collapsed;
            return;
        }
        ShowCrew();
    }

    private void ShowCrew()
    {
        if (_crew == null) return;
        CrewText.Text = $"{_crew.Unlocked} of {HeistCrew.Members} unlocked";
        CrewDeadText.Text = _crew.Dead == 0 ? "Nobody has died" : _crew.Dead == 1 ? "1 crew member is dead" : $"{_crew.Dead} crew members are dead";
        UnlockCrewButton.IsEnabled = _crew.Unlocked < HeistCrew.Members;
        ReviveCrewButton.IsEnabled = _crew.Dead > 0;
    }

    private void UnlockCrew_Click(object sender, RoutedEventArgs e)
    {
        _crew?.UnlockAll();
        Changed();
        ShowCrew();
    }

    private void ReviveCrew_Click(object sender, RoutedEventArgs e)
    {
        _crew?.ReviveAll();
        Changed();
        ShowCrew();
    }

    // ---- Stats ----

    private const string AllStats = "All stats", AnyCharacter = "Anyone";
    private List<StatRowModel> _statRows = [];

    private void LoadStats()
    {
        try
        {
            _statRows = _doc.StatList.Select(e => new StatRowModel(e, StatsChanged)).ToList();
        }
        catch (SaveFormatException ex)
        {
            StatsError.Text = "The stats couldn't be read from this save: " + ex.Message;
            return;
        }
        StatCategories.ItemsSource = new[] { new StatCategory(AllStats, _statRows.Count) }
            .Concat(_statRows.GroupBy(r => r.Entry.Category).OrderBy(g => g.Key).Select(g => new StatCategory(g.Key, g.Count())))
            .ToList();
        StatCharacter.ItemsSource = new[] { AnyCharacter, StatBrowser.Everyone }.Concat(StatCatalog.Characters).ToList();
        StatCharacter.SelectedIndex = 0;
        StatCategories.SelectedIndex = 0;
    }

    private void StatFilter_Changed(object sender, RoutedEventArgs e)
    {
        if (_statRows.Count == 0 || StatCategories.SelectedItem is not StatCategory category) return;
        var search = StatSearch.Text.Trim();
        var who = StatCharacter.SelectedItem as string ?? AnyCharacter;
        var rows = _statRows.Where(r =>
            (search.Length > 0 || category.Name == AllStats || r.Entry.Category == category.Name)
            && (who == AnyCharacter || r.Entry.Character == who)
            && (search.Length == 0 || r.Matches(search))).ToList();
        StatRows.ItemsSource = rows;
        StatCount.Text = search.Length > 0 ? $"{rows.Count:N0} matches in all categories" : $"{rows.Count:N0} stats";
    }

    private void StatsChanged()
    {
        Changed();
        // Cash and skills are also on the Player tab.
        _characters.ForEach(c =>
        {
            c.Refresh();
            foreach (var s in c.Skills) s.Refresh();
        });
    }

    // ---- Weapons ----

    private void LoadWeapons()
    {
        WeaponInventory all;
        try
        {
            all = _doc.Weapons;
            if (!all.Complete)
                WeaponsError.Text = "This save comes from an older game version that stores some weapons in a different order. " +
                                    "Weapons it already has can be edited; the others aren't listed, so they can't be given.";
        }
        catch (SaveFormatException ex)
        {
            WeaponsError.Text = "Weapons couldn't be read from this save, so they can't be edited: " + ex.Message;
            return;
        }
        foreach (var character in StatCatalog.Characters)
        {
            _weapons[character] = all.Where(w => w.Character == character)
                .GroupBy(w => w.Weapon.Category)
                .Select(g => new WeaponGroup(g.Key, g.Select(w => new WeaponModel(w, Changed)).ToList()))
                .ToList();
            var button = new RadioButton
            {
                Content = character, GroupName = "WeaponCharacter", Style = (Style)FindResource("Segment"),
                IsChecked = character == _weaponCharacter,
            };
            button.Checked += (_, _) => ShowWeapons(character);
            CharacterPicker.Children.Add(button);
        }
        ShowWeapons(_weaponCharacter);
    }

    private void ShowWeapons(string character)
    {
        _weaponCharacter = character;
        WeaponGroups.ItemsSource = _weapons.GetValueOrDefault(character);
    }

    /// <summary>The weapons the bulk buttons act on: the shown character, or everyone.</summary>
    private IEnumerable<WeaponModel> BulkTargets() =>
        (AllCharactersBox.IsChecked == true ? _weapons.Values.SelectMany(g => g) : _weapons.GetValueOrDefault(_weaponCharacter) ?? [])
        .SelectMany(g => g.Weapons);

    private void GiveAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var w in BulkTargets()) w.Give();
    }

    private void FillAmmo_Click(object sender, RoutedEventArgs e)
    {
        foreach (var w in BulkTargets()) w.FillAmmo();
    }

    private void RemoveAll_Click(object sender, RoutedEventArgs e)
    {
        string who = AllCharactersBox.IsChecked == true ? "all three characters" : _weaponCharacter;
        if (MessageBox.Show(_shell, $"Take every weapon away from {who}?", "Remove all weapons",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        foreach (var w in BulkTargets()) w.Remove();
    }

    private void WeaponMax_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is WeaponModel w) w.FillAmmo();
    }

    // ---- Checklist ----

    private static readonly Dictionary<ChecklistGroup, string> GroupTitles = new()
    {
        [ChecklistGroup.Missions] = "Missions",
        [ChecklistGroup.HobbiesAndPastimes] = "Hobbies & Pastimes",
        [ChecklistGroup.StrangersAndFreaks] = "Strangers & Freaks",
        [ChecklistGroup.RandomEvents] = "Random Events",
        [ChecklistGroup.Miscellaneous] = "Miscellaneous",
        [ChecklistGroup.Other] = "Other",
    };

    private List<ChecklistItemModel> _checklistItems = [];
    private ChecklistGroup _checklistGroup = ChecklistGroup.Missions;

    private void LoadChecklist()
    {
        Checklist checklist;
        try
        {
            checklist = _doc.Checklist;
        }
        catch (SaveFormatException ex)
        {
            ChecklistError.Text = "The checklist couldn't be read from this save, so it can't be edited: " + ex.Message;
            ChecklistSummary.Visibility = Visibility.Collapsed;
            return;
        }
        _checklistItems = checklist.Select(i => new ChecklistItemModel(checklist, i, () =>
        {
            Changed();
            ShowChecklistSummary();
        })).ToList();
        foreach (var group in GroupTitles.Keys.Where(g => _checklistItems.Any(i => i.Item.Group == g)))
        {
            var button = new RadioButton
            {
                Content = GroupTitles[group], GroupName = "ChecklistGroup", Style = (Style)FindResource("Segment"),
                IsChecked = group == _checklistGroup,
            };
            button.Checked += (_, _) => ShowChecklistGroup(group);
            ChecklistPicker.Children.Add(button);
        }
        ShowChecklistGroup(_checklistGroup);
        ShowChecklistSummary();
    }

    private void ShowChecklistGroup(ChecklistGroup group)
    {
        _checklistGroup = group;
        // Items that count come first; optional and unused ones after them.
        ChecklistItems.ItemsSource = _checklistItems.Where(i => i.Item.Group == group).OrderBy(i => !i.Item.Required).ToList();
    }

    private void ShowChecklistSummary()
    {
        var checklist = _doc.Checklist;
        CompletionText.Text = $"{checklist.Percentage:0.##}%";
        ChecklistRows.ItemsSource = checklist.Progress.Select(p => new ChecklistSummaryRow(GroupTitles[p.Group],
            $"{p.Completed} / {p.Needed}", p.Needed == 0 ? 0 : (double)p.Completed / p.Needed,
            $"{p.Percent:0.#}/{p.MaxPercent:0}%")).ToList();
    }

    private void SetChecklist(IEnumerable<ChecklistItemModel> items, bool done)
    {
        var list = items.Where(i => i.Done != done).ToList();
        if (list.Count == 0) return;
        _doc.Checklist.SetCompleted(list.Select(i => i.Item), done);
        foreach (var i in list) i.Refresh();
        Changed();
        ShowChecklistSummary();
    }

    private void ChecklistAllDone_Click(object sender, RoutedEventArgs e) =>
        SetChecklist(_checklistItems.Where(i => i.Item.Group == _checklistGroup), true);

    private void ChecklistNoneDone_Click(object sender, RoutedEventArgs e) =>
        SetChecklist(_checklistItems.Where(i => i.Item.Group == _checklistGroup), false);

    private void Checklist100_Click(object sender, RoutedEventArgs e) =>
        SetChecklist(_checklistItems.Where(i => i.Item.Required), true);

    // ---- Missions ----

    private StoryMissions? _missions;
    private List<MissionModel> _missionItems = [];

    private void LoadMissions()
    {
        try
        {
            _missions = _doc.Missions;
        }
        catch (SaveFormatException ex)
        {
            MissionsError.Text = "The story missions couldn't be read from this save, so they can't be edited: " + ex.Message;
            MissionsSummary.Visibility = Visibility.Collapsed;
            return;
        }
        foreach (var (choice, (title, options)) in StoryMissions.Choices)
        {
            var picker = new StackPanel { Margin = new Thickness(0, 0, 22, 8) };
            picker.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 3) });
            var buttons = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var option in options)
            {
                var button = new RadioButton
                {
                    Content = option.Name, GroupName = "Choice" + choice, Style = (Style)FindResource("Segment"),
                    IsChecked = _missions.Selected(choice) == option.Letter,
                };
                button.Checked += (_, _) =>
                {
                    _missions.Select(choice, option.Letter);
                    ShowMissions();
                };
                buttons.Children.Add(button);
            }
            picker.Children.Add(buttons);
            ChoicePickers.Children.Add(picker);
        }
        ShowMissions();
    }

    /// <summary>Lists the missions of the chosen approaches and refreshes the summary.</summary>
    private void ShowMissions()
    {
        if (_missions == null) return;
        _missionItems = _missions.Path.Select(m => new MissionModel(_missions, m, MissionsChanged)).ToList();
        MissionItems.ItemsSource = _missionItems;
        ShowMissionSummary();
    }

    private void ShowMissionSummary()
    {
        if (_missions == null) return;
        var next = _missions.Next;
        NextMissionText.Text = next?.Name ?? "Story complete";
        var story = _missions.Path.Where(m => !m.Optional).ToList();
        MissionCountText.Text = $"{story.Count(m => m.Completed)} of {story.Count} story missions completed";
        CompleteNextButton.IsEnabled = next != null;
    }

    private void MissionsChanged()
    {
        Changed();
        foreach (var m in _missionItems) m.Refresh();
        ShowMissionSummary();
        // Missions tick their checklist entries too.
        foreach (var i in _checklistItems) i.Refresh();
        if (_checklistItems.Count > 0) ShowChecklistSummary();
    }

    private void CompleteNextMission_Click(object sender, RoutedEventArgs e)
    {
        if (_missions?.Next is not { } next) return;
        _missions.SetCompleted(next, true);
        MissionsChanged();
    }

    private void CompleteStory_Click(object sender, RoutedEventArgs e)
    {
        if (_missions == null || _missions.Path.All(m => m.Completed)) return;
        if (MessageBox.Show(_shell, "Mark every story mission on the chosen approaches completed, up to the end credits?",
                "Complete the whole story", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        _missions.CompleteUpTo(_missions.Path.Last());
        MissionsChanged();
    }

    private void CompleteUpTo_Click(object sender, RoutedEventArgs e)
    {
        if (_missions == null || (sender as FrameworkElement)?.DataContext is not MissionModel m) return;
        _missions.CompleteUpTo(m.Mission);
        MissionsChanged();
    }

    // ---- Strangers and Freaks ----

    private StrangersAndFreaks? _strangers;
    private List<StrangerModel> _strangerItems = [];

    private void LoadStrangers()
    {
        try
        {
            _strangers = _doc.Strangers;
        }
        catch (SaveFormatException ex)
        {
            StrangersError.Text = "Strangers and Freaks missions couldn't be read from this save: " + ex.Message;
            StrangersButtons.Visibility = Visibility.Collapsed;
            return;
        }
        _strangerItems = _strangers.Select(m => new StrangerModel(_strangers, m, StrangersChanged)).ToList();
        StrangerGroups.ItemsSource = _strangerItems.GroupBy(m => m.Mission.Character)
            .Select(g => new StrangerGroup(g.Key, g.ToList())).ToList();
        ShowStrangersCount();
    }

    private void ShowStrangersCount()
    {
        if (_strangers != null) StrangersCountText.Text = $"{_strangers.Count(m => m.Completed)} of {_strangers.Count} completed";
    }

    private void StrangersChanged()
    {
        Changed();
        foreach (var m in _strangerItems) m.Refresh();
        ShowStrangersCount();
        foreach (var i in _checklistItems) i.Refresh();
        if (_checklistItems.Count > 0) ShowChecklistSummary();
    }

    private void SetStrangers(bool done)
    {
        if (_strangers == null || _strangers.All(m => m.Completed == done)) return;
        _strangers.SetCompleted(_strangers, done);
        StrangersChanged();
    }

    private void StrangersAll_Click(object sender, RoutedEventArgs e) => SetStrangers(true);

    private void StrangersNone_Click(object sender, RoutedEventArgs e) => SetStrangers(false);

    // ---- Saving and navigation ----

    private void Save_Click(object sender, RoutedEventArgs e) => WriteTo(_doc.FilePath);

    private void SaveAs_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            FileName = Path.GetFileName(_doc.FilePath), InitialDirectory = Path.GetDirectoryName(_doc.FilePath),
            Filter = "All files|*.*",
        };
        if (dlg.ShowDialog(_shell) == true) WriteTo(dlg.FileName);
    }

    private void SaveToSlot_Click(object sender, RoutedEventArgs e)
    {
        var profiles = App.Game.DefaultSaveFolders().ToList();
        if (profiles.Count == 0)
        {
            MessageBox.Show(_shell, "No game profile folder was found on this PC. Run the game once and save, then try again.",
                "Save to game slot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var picker = new SlotPickerWindow(profiles) { Owner = _shell };
        if (picker.ShowDialog() == true && picker.ChosenPath != null) WriteTo(picker.ChosenPath);
    }

    private bool WriteTo(string path)
    {
        try
        {
            var backup = BackupService.Default.Backup(path);
            _doc.Save(path);
            _dirty = false;
            SaveButton.IsEnabled = false;
            ShowHeader();
            _shell.SetStatus((backup == null ? $"Saved to {path}." : $"Saved to {path}. The previous file was backed up to {backup}.")
                             + (SaveSlots.IndexOf(path) != null ? " Restart the game to see it in Load Game." : ""));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SaveFormatException)
        {
            MessageBox.Show(_shell, ex.Message, "Save failed", MessageBoxButton.OK, MessageBoxImage.Error);
            return false;
        }
    }

    public bool ConfirmLeave()
    {
        if (!_dirty) return true;
        return MessageBox.Show(_shell, $"Save your changes to \"{_doc.Title}\" before leaving?", "Unsaved changes",
                MessageBoxButton.YesNoCancel, MessageBoxImage.Question) switch
        {
            MessageBoxResult.Yes => WriteTo(_doc.FilePath),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _shell.Navigate(new SavePickerPage(_shell));

    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        new AdvancedToolsWindow(_doc, Changed) { Owner = _shell }.ShowDialog();
        // The raw editor may have changed the same bytes the friendly views show.
        _characters.ForEach(c =>
        {
            c.Refresh();
            foreach (var s in c.Skills) s.Refresh();
        });
        foreach (var w in _weapons.Values.SelectMany(g => g).SelectMany(g => g.Weapons)) w.Refresh();
        foreach (var i in _checklistItems) i.Refresh();
        if (_checklistItems.Count > 0) ShowChecklistSummary();
        foreach (var m in _missionItems) m.Refresh();
        ShowMissionSummary();
        foreach (var m in _strangerItems) m.Refresh();
        ShowStrangersCount();
        _status.ForEach(s => s.Refresh());
        _clock?.Refresh();
        _statRows.ForEach(r => r.Refresh());
        _garageSlots.ForEach(s => s.Refresh());
        _propertyVehicles.ForEach(s => s.Refresh());
        _properties.ForEach(p => p.Refresh());
        ShowCrew();
        ShowHeader();
    }
}
