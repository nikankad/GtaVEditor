using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using SaveEditor.Core;
using SaveEditor.Games.GtaV;

namespace SaveEditor.App;

/// <summary>Third page: friendly editing of one GTA V save (checklist, player, weapons).</summary>
public partial class GtaVEditorPage : UserControl, IGuardedPage
{
    private readonly MainWindow _shell;
    private readonly IGameModule _game;
    private readonly GtaVSaveDocument _doc;
    private bool _dirty;

    private List<CharacterModel> _characters = [];
    private readonly Dictionary<string, List<WeaponGroup>> _weapons = [];
    private string _weaponCharacter = StatCatalog.Characters[0];

    public GtaVEditorPage(MainWindow shell, IGameModule game, GtaVSaveDocument doc)
    {
        InitializeComponent();
        _shell = shell;
        _game = game;
        _doc = doc;

        ShowHeader();
        LoadChecklist();
        LoadMissions();
        LoadCharacters();
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
        var profiles = _game.DefaultSaveFolders().ToList();
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
            var backup = BackupService.Default.Backup(_game.Id, path);
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

    private void Back_Click(object sender, RoutedEventArgs e) => _shell.Navigate(new SavePickerPage(_shell, _game));

    private void Advanced_Click(object sender, RoutedEventArgs e)
    {
        new AdvancedToolsWindow(_game, _doc, Changed) { Owner = _shell }.ShowDialog();
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
        ShowHeader();
    }
}
