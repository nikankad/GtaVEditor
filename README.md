# GTA V Save Editor

A free save editor for **Grand Theft Auto V story mode on PC**. Open a save, change it through plain pages, and save.

> **Beta (version 0.1.0).** It works, but edits have not been tested in-game for every case. Back up your saves.

![GTA V Save Editor](docs/screenshot.png)

## Download

Get **GtaVSaveEditor.exe** from the [latest release](https://github.com/nikankad/GtaVEditor/releases/latest) and run it.
Nothing to install: the .NET runtime is built in. It targets Windows 10/11, 64-bit.

The release EXE has the PC save encryption key built in, so your saves open straight away. Builds from source don't include
it: the start page asks for a key once, so choose **Import key…**, select a 32-byte key file, then select one of your own
saves to verify it.

## Supported saves

This editor handles the PC story-mode save layout.
**GTA Online and console saves are unsupported. GTA V Enhanced compatibility has not been verified.**
Completing a mission entry changes saved completion data; it does not play the mission or simulate all of its
rewards, cutscenes, and story transitions. Keep a separate copy before changing story progress.

## What it can do

- **Checklist:** tick off 100% completion items
- **Missions:** complete story missions, pick heist approaches, unlock and revive heist crew
- **Strangers & Freaks:** complete their missions
- **Collectibles:** mark letter scraps, spaceship parts, bridges and more as found, on a map you can zoom, drag and click
  for each item's number and location
- **Player:** money, skills, health, armor, wanted level, game clock, and where each character is, picked on a map
- **Vehicles:** park cars, planes, helicopters and boats in your garages, hangars and docks, with the game's pictures; customise garage cars (paint, number plate, window tint, neon, rims, tyre smoke, upgrades, body parts, liveries); own businesses
- **Weapons and Unlocks:** give weapons and ammo, fit attachments and tints, unlock Ammu-Nation stock, clothes by kind, Director Mode actors, Flight School medals
- **Stats and Game flags:** every stat and story switch, with plain-English names
- **Open your saves quickly:** the start page lists detected saves with their slot, title, profile and modification date
- **Focus on what remains:** show only incomplete checklist entries, missions and missing collectibles
- **Restore backups:** browse dated backups, inspect a copy, or restore one while preserving the save being replaced
- **Keyboard shortcuts:** Ctrl+S saves the current file, Ctrl+Shift+S saves a copy, and Ctrl+F focuses search
- **Search everything:** type in the box at the top to find any mission, collectible, flag or stat, tick it right there or jump to the matching item, which is briefly highlighted
- **Check before saving:** every save first lists what you changed, with an Undo for each one

## Back up your saves

Editing a save can corrupt it or break story progress. The editor copies each save to a backups folder before
writing it. Use **Browse backups…** on the start page to inspect or restore a copy; saving an opened backup asks for a working-copy location. Keep your own copy of any save you care about. Use at your own risk.

Close GTA V before editing. Automatic backups are in `%LocalAppData%\SaveEditor\Backups\gtav-pc`;
use **Open backups folder** on the start page. To restore one, close the game, copy the backup to your profile
folder under its original `SGTA500xx` filename, and check cloud-save synchronization before starting the game.
Imported keys are stored locally at `%AppData%\SaveEditor\keys\gtav-pc.key`.

Saves are in `Documents\Rockstar Games\GTA V\Profiles\<id>\SGTA500xx`. If cloud saves are on, the game may replace
an edited save with the cloud copy.

## Disclaimer

**For educational and personal use only.** This is an independent, non-commercial hobby project that shows how GTA V
single-player save files are laid out. It is meant for learning and for editing your own saves offline.

- **Not affiliated with or endorsed by** Rockstar Games, Take-Two Interactive or any of their subsidiaries.
  *Grand Theft Auto* and *GTA V* are trademarks of their respective owners and are used here only to name the game
  the tool works with.
- **Story mode only.** It does not touch GTA Online, and it is not meant to be used to cheat, gain an unfair advantage
  or harm other players. Don't use it with online services; doing so can get an account banned.
- **You need your own copy of the game.** It does not include, distribute or help you obtain the game.
- **No warranty.** The software is provided "as is", without warranty of any kind. The author is not liable for
  corrupted or lost saves, broken progress, account actions, or any other damage or loss from using it. Back up your
  saves and use it at your own risk.
- **You are responsible** for making sure your use complies with the game's terms of service and the laws where you live.


## Build it yourself

Needs Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) (the version in `global.json`).

```bash
git clone https://github.com/nikankad/GtaVEditor.git
cd GtaVEditor
dotnet run --project src/SaveEditor.App
```

Builds from source don't include the save encryption key, so the app asks you to import a 32-byte key file once.

To make the self-contained single-file EXE:

```bash
dotnet publish src/SaveEditor.App -c Release -p:PublishProfile=Public -o publish
```

## Help and issues

[Report a bug](https://github.com/nikankad/GtaVEditor/issues/new) with the app version, Windows version,
game edition, and steps to reproduce it. Do not post saves, encryption keys, or personal profile paths.

## License

Licensed under [Apache-2.0](LICENSE); see [NOTICE](NOTICE).

This project is not affiliated with Rockstar Games or Take-Two Interactive.
