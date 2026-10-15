# Security reports

Security fixes target the latest release and `main`. Install a supported .NET 10
Desktop Runtime and keep its patches current.

Please report exploitable crashes, unsafe file writes, credential exposure, or
other security issues through GitHub's private vulnerability reporting under the
repository's **Security** tab when available. If private reporting is unavailable,
open an issue asking the maintainer for a private reporting channel, without
including exploit details or sensitive files. No response deadline is promised.

Include the affected app version, Windows/runtime version, impact, and a minimal
synthetic reproducer. Do not upload your player save, encryption key, or private
profile paths. Coordinate any private fixture transfer with the maintainer first.

The editor reads local save files and writes files you select. Save checksums
detect format inconsistencies; they do not establish that a file is trustworthy
or that an edit is safe to load in-game. Keep independent backups.
