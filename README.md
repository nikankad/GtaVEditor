# GTA V Save Editor

A free save editor for **Grand Theft Auto V story mode on PC**. Open a save, change it through plain pages, and save.

![GTA V Save Editor](docs/screenshot.png)

## Download

Get **GtaVSaveEditor.exe** from the [latest release](https://github.com/nikankad/GtaVEditor/releases/latest) and run it.
Nothing to install (Windows 10/11, 64-bit).

## What it can do

- **Checklist:** tick off 100% completion items
- **Missions:** complete story missions, pick heist approaches, unlock and revive heist crew
- **Strangers & Freaks:** complete their missions
- **Collectibles:** mark letter scraps, spaceship parts, bridges and more as found, on a map you can zoom, drag and click
  for each item's number and location
- **Player:** money, skills, health, armor, wanted level, game clock, and where each character is, picked on a map
- **Vehicles:** park cars, planes, helicopters and boats in your garages, hangars and docks; own businesses
- **Weapons and Unlocks:** give weapons and ammo, unlock Ammu-Nation stock, clothes, Director Mode actors, Flight School medals
- **Stats and Game flags:** every stat and story switch, with plain-English names
- **Search everything:** type in the box at the top to find any mission, collectible, flag or stat, tick it right there or jump to its tab
- **Check before saving:** every save first lists what you changed, with an Undo for each one

## Back up your saves

Editing a save can corrupt it or break story progress. The editor copies each save to a backups folder before
writing it, but keep your own copy of any save you care about. Use at your own risk.

Saves are in `Documents\Rockstar Games\GTA V\Profiles\<id>\SGTA500xx`. If cloud saves are on, the game may replace
an edited save with the cloud copy.

## Build it yourself

Needs Windows and the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
git clone https://github.com/nikankad/GtaVEditor.git
cd GtaVEditor
dotnet run --project src/SaveEditor.App
```

Builds from source don't include the save encryption key, so the app asks you to import a 32-byte key file once.

```bash
dotnet test tests/SaveEditor.Tests
```
