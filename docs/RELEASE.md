# Public release checklist

The release workflow builds an existing version tag and creates a **draft**. A
push to `main` runs CI; it does not publish a release. Public visibility and draft
publication remain explicit maintainer actions.

## Repository readiness

- [x] Apache-2.0 license and copyright notice for original code.
- [x] Self-contained single-file Windows x64 publish profile and packaging script (no runtime install needed).
- [x] PR CI with tests, tool builds, packaged-app startup check, and a preview package; no release secrets.
- [x] Locked dependencies, vulnerability checks, pinned Action revisions, SHA-256 checksums.
- [x] Key setup, backup/restore, support scope, and contribution documentation.
- [ ] Add the maintainer's permission terms, source links, and required attribution for
  retained artwork, branding, and map to `THIRD_PARTY_NOTICES.md`.
- [ ] Review all existing Git history, old releases, screenshots, issues, and Actions
  artifacts before changing the repository from private to public. Ignoring a file
  now does not remove it from history or old downloads.
- [ ] Enable private vulnerability reporting if it will be offered, and set branch
  protection to require CI before merging.

## Build a candidate

1. Merge reviewed changes and confirm Windows CI is green.
   Review the pinned SDK for available servicing updates; update `global.json`
   and the dependency locks together before cutting a release when needed.
2. Choose a new tag, such as `v1.1.0` or `v1.1.0-rc.1`. Set any desired release
   version metadata in `Directory.Build.props` for local development builds.
3. Optional: configure `GTAV_SAVE_KEY` as a repository Actions secret containing
   the base64-encoded 32-byte PC save key. Release builds validate its length,
   embed it, and remove the temporary input in a `finally` block. Builds without
   the secret remain supported and use the one-time import flow. A key embedded
   in a public EXE is extractable; the secret only keeps it out of source/logs.
4. Create and push the chosen tag. Alternatively, run **Release** manually with
   an existing tag. Both paths build the tagged revision, not the current branch.
5. Download the draft's `GtaVSaveEditor-win-x64.zip` and `SHA256SUMS.txt`. Confirm
   that the checksum matches and that the ZIP includes only the EXE, README,
   LICENSE, NOTICE, and third-party notices.

For a local candidate:

```powershell
dotnet restore SaveEditor.sln --locked-mode
dotnet test tests/SaveEditor.Tests -c Release --no-restore
./scripts/Publish-App.ps1 -Version 1.1.0 -LockedMode
Get-FileHash artifacts/release/GtaVSaveEditor-win-x64.zip -Algorithm SHA256
```

The packaging script checks that the EXE is a bundled-runtime build (roughly 30-200 MB), so a
framework-dependent publish is caught. It packages files from a fresh temporary directory; debug symbols,
player saves, research binaries, and loose keys are excluded.

## Windows smoke test before publishing the draft

- [ ] On a clean Windows x64 machine with no .NET runtime or SDK installed, extract the ZIP
  and confirm the EXE starts and shows the start page.
- [ ] Check the version in About, branding, and the source/download links.
- [ ] Test bundled-key startup if the release includes one. For a build without a
  bundled key, test correct/incorrect imports and missing or unreadable files.
- [ ] Open a copy of a known-good supported save. Verify its checksum, inspect all
  tabs, maps and search, change one value, review/undo, and save to a separate file.
- [ ] Reopen the edited file and verify the intended value and checksum. Confirm
  that overwriting an existing file first creates an identical backup of that file.
- [ ] Test unsupported/malformed inputs and denied write locations; confirm that
  failure leaves the original save intact and produces a readable error.
- [ ] Test the resized logo, keyboard navigation, and 100%, 150%, and 200% scaling.
- [ ] For any claimed game edition, load a backed-up test edit in that edition and
  record the edition/build and result. Enhanced remains unverified until tested.
- [ ] Record whether real-save integration tests were run; two skipped tests are
  not evidence that a private fixture round trip passed.
- [ ] Verify that the public repository and every distributed asset have the
  intended permissions and attribution, then publish the reviewed draft.

Unsigned builds may show a Windows trust prompt. Do not instruct users to disable
Windows security features. Code signing requires a maintainer certificate and is
not configured in this repository.
