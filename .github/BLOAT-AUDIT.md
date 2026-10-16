# Bloat audit

Baseline: `259fd13` (`feature/car-customisation`), including the latest horns,
body-part and outfit features. Cleanup branch: `codex/bloat-cleanup`.

## Completed

- [x] Audit runtime dependencies, embedded catalogs, artwork, resource lookup,
      caching and the standalone publish profile.
- [x] Replace 134 byte-identical artwork copies with aliases to retained images.
      This removes 307,285 bytes of duplicate source artwork. All 1,902 original
      lookup paths remain available and return byte-identical image data.
- [x] Remove the unreferenced `assets/icons/gta5.png` (183,171 bytes). The actual
      application icon and logo remain in use.
- [x] Read resource keys without deserializing image values while indexing art.
- [x] Share cached images across aliases, retain image references weakly, and
      avoid retaining failed image lookups.
- [x] Build the sorted checklist thumbnail index once instead of scanning and
      sorting the entire resource index on each fallback lookup.
- [x] Add Windows artwork checks to CI and the release workflow.
- [x] Verify save regression checks, the existing unit suite, Windows editor UI
      smoke checks, every artwork lookup and the standalone EXE build.

## Measurements

Identical `Release`, `Public` profile, .NET SDK 10.0.402, win-x64 publish settings:

| Item | Before | After |
| --- | ---: | ---: |
| Unique bundled artwork files | 1,902 | 1,768 |
| Available artwork lookup paths | 1,902 | 1,902 |
| Standalone EXE, bytes | 83,642,265 | 83,331,993 |

The compressed EXE is 310,272 bytes smaller (0.37%). The additional runtime
improvements reduce image retention and repeated lookup work rather than
removing editor capabilities.

## Validation

- 20 maintained synthetic checks passed: save parsing, encryption, byte-for-byte
  round trips, editing, checksums, backups, malformed-input and allocation limits.
- 70 existing unit test cases passed through a temporary .NET 10 test harness.
  The ignored local test projects still target .NET 8, so they were not changed.
  Real-save tests that require external sample/key environment variables were
  not supplied with those inputs; these do not establish in-game compatibility.
- Existing Windows UI smoke checks passed: editor construction, completion
  filtering, edit reversion, search reveal/highlighting, backup browser, start page.
- All 1,768 retained images decode; all 134 aliases share their canonical image.
- Unused cached images can be garbage-collected and reloaded when needed.
- The one-time migration check compares SHA-256 hashes of all 1,902 original
  logical image paths against the baseline manifest in local audit artifacts.

Repeat the tracked checks on Windows:

```powershell
dotnet run --project .github/checks -c Release -p:RestoreLockedMode=true
dotnet run --project .github/artwork-checks -c Release -p:RestoreLockedMode=true
dotnet publish src/SaveEditor.App -c Release -p:PublishProfile=Public -p:RestoreLockedMode=true -o publish
./.github/checks/Startup.ps1 -Executable publish/GtaVSaveEditor.exe
```

## Retained functionality

The application already has no third-party runtime package dependencies and
already compresses its single-file distribution. The bundled .NET/WPF runtime,
localization resources, vector map, save-format protections, backup support,
vehicle/customisation catalogs, outfit data and distinct game artwork remain.
They support the existing standalone app and its editor features.

Original deleted files are recoverable from the baseline Git commit. Local
before/after EXEs, migration hashes and temporary test harnesses are under the
ignored `artifacts/bloat-audit/` directory and are not shipped.
