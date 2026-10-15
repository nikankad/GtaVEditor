# Changelog

## Unreleased

- Move the projects to .NET 10 LTS and pin the build SDK and dependency locks.
- Keep the Windows download self-contained: one EXE with the .NET runtime built in, nothing to install.
- Resize the oversized app logo and package the EXE with setup instructions,
  Apache-2.0 license, third-party notices, and a SHA-256 checksum.
- Build and test pull requests without release secrets. Create draft releases
  from version tags, with an optional maintainer-supplied save key.
- Reject truncated checksum blocks and overflowing block lengths with readable
  format errors. Handle file-access failures during key import.
- Show private-save integration tests as skipped when local fixtures are absent.
- Add contribution, support, security-reporting, and release documentation.

Retained artwork permission details and Windows release validation are tracked
in [the release checklist](docs/RELEASE.md).
