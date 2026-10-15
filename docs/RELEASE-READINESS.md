# Public release preparation

Prepared on 2026-10-15 on branch `chore/public-release` in a separate worktree.
The GitHub repository's visibility and existing releases have not been changed.

## Implemented

- Apache-2.0 for original code, copyright notice, and separate third-party notices.
  Existing artwork and map retained at the maintainer's request.
- .NET 10 LTS, exact SDK pin, locked dependencies, vulnerability warnings as errors,
  and monthly dependency/Actions update configuration.
- Runtime-dependent Windows x64 publishing, resized logo, and a ZIP containing
  the EXE, setup instructions, license, copyright and third-party notices.
- PR CI builds/tests and a preview package without release secrets. Tagged
  releases create drafts after testing, packaging, and a Windows startup check.
- Optional release-only key embedding with length validation and input cleanup.
  Source and keyless releases keep the import flow.
- Focused malformed-save regression fixes/tests and readable key-import I/O errors.
- Installation, backup/restore, compatibility scope, contribution and release docs.

## Verification performed

Checks run locally with .NET SDK 10.0.402 on Linux:

| Check | Result |
| --- | --- |
| Locked solution restore | Passed |
| Release solution cross-build | Passed; 0 warnings, 0 errors |
| Core tests | 48 passed, 2 skipped, 0 failed |
| KeyRecovery and StoryResearch builds | Passed; 0 warnings, 0 errors |
| Public packaging script in PowerShell 7 | Passed |
| Resulting EXE / complete release ZIP | About 4.27 MB / 3.21 MB |
| ZIP contents, CRC, and SHA-256 manifest | Passed |
| NuGet audit, direct and transitive packages | No known vulnerable packages reported |
| GitHub workflows (actionlint) | Passed |
| PowerShell scripts and workflow run-block syntax | Passed |
| Local documentation links and YAML parsing | Passed |
| Gitleaks, all existing Git refs (14 commits at audit time) | No leaks detected |

The two skipped tests require a private local save and, for decryption, a key.
They are not counted as passing real-save checks. The history scan is evidence of
no detected leaks, not a guarantee that every historical asset is publishable.

The previous self-contained release is 76.82 MB. The new runtime-dependent EXE
is about 94% smaller, and its complete ZIP download about 96% smaller. Users
install the .NET 10 Desktop Runtime separately; it is shared on their machine.

## Before public launch

1. Fill in the retained visuals' source links, permission terms, and required
   credit text in `THIRD_PARTY_NOTICES.md`. The maintainer has offered to supply
   these; the missing terms have not been invented.
2. Run the new Windows CI and complete the Windows/in-game checks in
   `docs/RELEASE.md`. Linux cross-building verifies artifacts, not GUI execution
   or save compatibility with every game build. Enhanced remains unverified.
3. Review historical releases/artifacts and repository settings before making
   the repository public, and review the generated draft before publishing it.

No code-signing certificate, private game fixture, or Windows runtime was
available for this local audit. The workflow includes an automated Windows
startup test; its execution remains a Windows CI check.
