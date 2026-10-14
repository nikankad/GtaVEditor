# GTA V Save Editor

A friendly save editor for **Grand Theft Auto V story mode on PC** (C# / .NET 8, WPF).

Pick a save, change things through plain pages (missions, checklist, player, vehicles, stats, weapons), and save.
A backup is made before every write.

## What it can edit

| Page | What you can do |
| --- | --- |
| Checklist | Tick or untick 100% checklist items; the completion figures update to match |
| Missions | Complete the next mission, up to a chosen mission, or the whole story (with heist approach choices); unlock and revive heist crew |
| Strangers & Freaks | Complete Strangers and Freaks missions |
| Player | Cash and skills, health, armor, special ability, wanted level, location, game clock |
| Vehicles | Park cars and bikes in safehouse garages; planes, helicopters and boats at hangars, helipads and docks; business ownership |
| Stats | Clearly labelled stats by category, with search and per-character filter |
| Weapons | Give or remove weapons and set ammo per character |

Raw tools (every stored value, save comparison, file details) are under **Advanced…**.

## Encryption key

GTA V PC saves are AES-encrypted with a Rockstar key. **The key is not included in this repository.**
Import it once in the app ("Import key…"); it is only stored if it correctly decrypts one of your saves.

## Project layout

```
SaveEditor.sln
src/
  SaveEditor.App/            WPF app (GtaVSaveEditor.exe)
    Pages/                   Save picker, editor page
    Windows/                 Advanced tools, game-slot picker
    Models/                  View models for the pages
    Themes/                  GTA-style dark theme
  SaveEditor.GtaV/           Save format and editing library
    Format/                  SGTA file, AES, block container + checksums, PSO tree, stat tables, hashes
    Saves/                   GtaVGame, GtaVSaveDocument, key store, slots, backups, comparer
    Features/                Checklist, missions, strangers & freaks, player, garages, properties, crew, weapons, stats
    Catalogs/                Vehicle catalogue
    Data/                    Embedded name lists (PSO names, checklist, stats)
tests/SaveEditor.Tests/      xUnit tests
tools/                       Research utilities (KeyRecovery, StoryResearch); not part of the app
docs/FINDINGS.md             Save format and reverse-engineering notes
assets/icons/                App icon
```

## Build and test

```bash
dotnet build SaveEditor.sln
```

```bash
dotnet test tests/SaveEditor.Tests
```

Tests that need a real save pass without checking anything unless these are set: `GTAV_KEY_FILE` (your imported key),
`GTAV_SAMPLE_SAVE` (a 100% save) and `GTAV_STORY_SAVE` (an early-story save).

## Notes

- None of the edits has been verified in-game across every case; keep the backups.
- Name lists in `src/SaveEditor.GtaV/Data` partly come from a third-party editor's data; check its terms before
  publishing a release.
