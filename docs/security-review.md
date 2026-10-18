# Security review

Reviewed `main` at commit `94e786b` (October 2026) by reading the source and the GitHub Actions workflows.
Nothing was built or run for this review.

## Summary

No critical or high-severity issues. No secrets are committed, the workflows take no untrusted input, and a save
file cannot run code or make the editor write anywhere other than the file the user chose.
Three issues are worth fixing.

## Findings

### Medium: releases publish automatically with no approval step

Every push to `main` builds and publishes a public release, and the EXE is not code-signed. A merged pull request
reaches users within minutes, and the secrets the release job uses are repository-level, so any workflow run by
someone with write access can read them.

Recommended, in the repository settings:

- Move the release job's secrets into a `release` environment that requires a reviewer, and add
  `environment: release` to the job in `.github/workflows/release.yml`.
- Protect `main` so changes need a pull request and passing CI.
- Consider code signing, so users can tell an official build from a modified one.

### Low: releases no longer include a checksum

CI writes `SHA256SUMS.txt`, but the release workflow does not attach it. Without code signing, a published checksum
is the only way users can check that their download matches the build.

Recommended: compute the SHA-256 in the release job and attach `SHA256SUMS.txt` next to the EXE.

### Low: a crafted save can crash the app

A modified save file can make the editor close when it is opened. This is a crash only: the code is managed .NET,
so an out-of-range read raises an exception rather than corrupting memory.

- `Features/VehicleLook.cs` reads fixed offsets of the vehicle look struct (`ModsCount`, `KitAt`) without checking
  them against the end of the save data.
- `Features/Garages.cs` (`GarageSlot`) does the same for the number plate (`PlateAt`).
- These throw `ArgumentOutOfRangeException`. `EditorPage` catches only `SaveFormatException`, and the app has no
  global exception handler.

Recommended: check those offsets in `GarageSlot.HasLayout` and the `VehicleLook` constructor, and add an
`Application.DispatcherUnhandledException` handler that reports the error instead of closing the app.

## Already in good shape

- **Workflows:** actions are pinned to commit SHAs, default permissions are read-only, checkout does not keep
  credentials, and NuGet restore runs in locked mode. Pull request CI runs without secrets.
- **Save parsing:** the PSO schema and tables are bounds-checked, the node tree is capped and pointer cycles are
  detected, and save files are size-limited before they are read.
- **Saving:** writes go to a uniquely named temporary file that replaces the save only once it is complete, and the
  previous file is backed up first.
- **Links:** the About window opens only `https` links.
