# Public release preparation

Prepared on 2026-10-15.
The GitHub repository's visibility and existing releases have not been changed.

## Implemented

- Apache-2.0 for original code, copyright notice, and separate third-party notices.
- .NET 10 LTS, exact SDK pin, locked dependencies, vulnerability warnings as errors,
  and monthly dependency/Actions update configuration.
- Self-contained single-file Windows x64 publishing (the .NET runtime is built in,
  nothing to install), resized logo, and a ZIP containing the EXE, instructions,
  license, copyright and third-party notices.
- PR CI builds/tests and a preview package without release secrets. Tagged
  releases create drafts after testing, packaging, and a Windows startup check.
- Optional release-only key embedding with length validation and input cleanup.
  Source and keyless releases keep the import flow.
- Focused malformed-save regression fixes/tests and readable key-import I/O errors.
- Installation, backup/restore, compatibility scope, contribution and release docs.

## Verification performed

These checks were run with .NET SDK 10.0.402 on Linux against an earlier, runtime-dependent version of this branch,
before it was made self-contained and `main` was merged in. Re-run them (CI does) before releasing:

| Check | Result |
| --- | --- |
| Locked solution restore | Passed |
| Release solution cross-build | Passed; 0 warnings, 0 errors |
| Core tests | 48 passed, 2 skipped, 0 failed |
| StoryResearch tool build | Passed; 0 warnings, 0 errors |
| Public packaging script in PowerShell 7 | Passed |
| ZIP contents, CRC, and SHA-256 manifest | Passed |
| NuGet audit, direct and transitive packages | No known vulnerable packages reported |
| GitHub workflows (actionlint) | Passed |
| PowerShell scripts and workflow run-block syntax | Passed |
| Local documentation links and YAML parsing | Passed |

The two skipped tests require a private local save and, for decryption, a key.
They are not counted as passing real-save checks.

The download stays self-contained (previously about 77 MB with .NET 8). A smaller
runtime-dependent package was considered and rejected so that players have nothing
to install.

## Before public launch

1. Run the new Windows CI and complete the Windows/in-game checks in
   `docs/RELEASE.md`. Linux cross-building verifies artifacts, not GUI execution
   or save compatibility with every game build. Enhanced remains unverified.
2. Review historical releases/artifacts and repository settings before making
   the repository public, and review the generated draft before publishing it.

No code-signing certificate, private game fixture, or Windows runtime was
available for this local audit. The workflow includes an automated Windows
startup test; its execution remains a Windows CI check.
