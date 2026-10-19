# Contributing

Thanks for helping improve GTA V Save Editor.

## Before you start

- Read the [security policy](.github/SECURITY.md) before reporting security issues.
- Search existing [issues](https://github.com/nikankad/GtaVEditor/issues) and [pull requests](https://github.com/nikankad/GtaVEditor/pulls) before opening a new one.

## Development setup

Use Windows with the SDK pinned in `global.json`:

```powershell
dotnet --version
dotnet restore SaveEditor.sln --locked-mode
```

Run local checks before opening a pull request:

```powershell
dotnet run --project .github/checks -c Release -p:RestoreLockedMode=true
dotnet publish src/SaveEditor.App -c Release -p:PublishProfile=Public -p:RestoreLockedMode=true -o publish
```

## Pull request guidance

- Keep changes focused and explain why they are needed.
- Include validation details (commands run and outcomes).
- If UI or save behavior changes, include clear reproduction and verification steps.
- Do not commit saves, encryption keys, or personal filesystem paths.

## Issue reports

When reporting bugs, include:

- App version
- Game edition
- Exact steps to reproduce
- Expected behavior vs. actual behavior

This helps triage issues quickly and safely.
