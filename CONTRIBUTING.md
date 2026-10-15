# Contributing

Open an issue for a bug or proposed feature. For a change, open a pull request with
the problem, the resulting behavior, and how you tested it. Keep each PR focused.

## Development

Use Windows and the .NET 10 SDK version listed in `global.json`. From the repository root:

```powershell
dotnet restore SaveEditor.sln --locked-mode
dotnet build SaveEditor.sln -c Release --no-restore
dotnet test tests/SaveEditor.Tests -c Release --no-restore
dotnet run --project src/SaveEditor.App
```

Core tests also run on Linux/macOS. Cross-building WPF on Linux requires
`-p:EnableWindowsTargeting=true`; running the interface requires Windows.

The app, core library, and tests live under `src/` and `tests/`. Local research
tools live under `tools/`; they are built in CI but not included in the release
ZIP.

Commit `packages.lock.json` updates when intentionally changing dependencies.
Run `dotnet restore SaveEditor.sln --force-evaluate` and restore each affected
research tool, then rerun the locked restore and tests. Dependency vulnerability
warnings are treated as errors.
Update `global.json` and dependency locks together when upgrading the SDK, since
the SDK also chooses the single-file linker task package. Keep SDK patches current.

## Save-format changes

Use synthetic fixtures in automated tests. Add regression tests for parsing,
encryption, checksums, or edits that could change save bytes. Preserve unknown
fields and require unchanged saves to round-trip byte-for-byte. Do not infer
in-game correctness from a checksum alone.

Two optional integration checks use your own local save and key:

```powershell
$env:GTAV_SAMPLE_SAVE = 'C:\local-only\SGTA50000'
$env:GTAV_KEY_FILE = 'C:\local-only\gtav-pc.key'
dotnet test tests/SaveEditor.Tests -c Release
```

These tests show as skipped when fixtures are unavailable. Never commit real
player saves, game binaries, decompiled scripts, encryption keys, or
credentials. Do not paste those inputs into issues or CI logs.

## Interface and release changes

Test UI changes on Windows, including keyboard navigation and display scaling.
Build the actual distribution with `./scripts/Publish-App.ps1 -LockedMode` and
follow `docs/RELEASE.md` before shipping it. CI builds PRs without release secrets.

New contributions must be your own work or have compatible distribution terms.
By submitting original code, you agree to distribute it under this project's
Apache-2.0 license. Third-party images need their source and permission recorded
in `THIRD_PARTY_NOTICES.md`.
