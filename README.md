# GTA V Save Editor

A Windows app for editing GTA V story-mode saves. Change your money, missions, vehicles, weapons and collectibles, then review your changes before saving.

This is a beta. Keep a separate copy of your saves; not every edit has been tested in-game.

![GTA V Save Editor](docs/screenshot.png)

## Download

Download **GtaVSaveEditor.exe** from the [releases page](https://github.com/nikankad/GtaVEditor/releases) and run it on Windows 10/11, 64-bit. The official EXE includes the .NET runtime and save encryption key, so there is nothing else to install.

PC story-mode saves only. GTA Online and console saves are unsupported, and GTA V Enhanced compatibility has not been verified.

## What you can edit

- Money, skills, health, armor, wanted level, character locations and the game clock.
- Story missions, heist choices, crew and Strangers & Freaks missions.
- The 100% checklist and collectibles, with an interactive map.
- Garage vehicles, aircraft, boats and businesses.
- Weapons, ammo, clothing, shop stock, Director Mode actors and Flight School medals.
- Individual stats and story flags.

The editor finds local saves, lets you search across its pages, and shows each change before writing it. You can undo changes individually or browse and restore automatic backups.

## Using your saves

Close GTA V before editing. Saves are usually in `Documents\Rockstar Games\GTA V\Profiles\<id>`, named `SGTA500xx`.

The editor backs up existing files before replacing them. Use **Browse backups…** on the start page to inspect or restore a copy. Backups are kept in `%LocalAppData%\SaveEditor\Backups\gtav-pc`.

Check cloud-save synchronization before reopening the game: it may replace an edited file. Marking a mission complete changes saved progress; it does not play the mission or reproduce all of its rewards and story events.

Shortcuts: **Ctrl+S** to save, **Ctrl+Shift+S** to save a copy, and **Ctrl+F** to search.

## Build

Use Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/10.0) version listed in `global.json`.

```powershell
git clone https://github.com/nikankad/GtaVEditor.git
cd GtaVEditor
dotnet restore SaveEditor.sln --locked-mode
dotnet run --project src/SaveEditor.App
```

Source builds need a one-time key import. Choose **Import key…**, select your 32-byte key file, then one of your saves to verify it. The imported key stays in `%AppData%\SaveEditor\keys\gtav-pc.key`.

To publish a self-contained EXE:

```powershell
dotnet publish src/SaveEditor.App -c Release -p:PublishProfile=Public -p:RestoreLockedMode=true -o publish
```

[Report a bug](https://github.com/nikankad/GtaVEditor/issues/new) with your app version, game edition and steps to reproduce it. Leave out saves, encryption keys and personal paths. For security issues, see [the reporting policy](.github/SECURITY.md).

Original code is licensed under [Apache-2.0](LICENSE). See [NOTICE](NOTICE) for credits and third-party materials. This is an independent project, unaffiliated with Rockstar Games or Take-Two Interactive.
