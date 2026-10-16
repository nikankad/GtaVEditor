# Releasing

1. Check Windows CI on the intended commit. It runs synthetic save checks,
   publishes the self-contained EXE and launches it. CI uses no production save key.
2. Review the changes in `NOTICE` and confirm permission and required credit for
   retained artwork, branding and map geometry. The source license does not grant
   rights to those materials. Review old branches, tags, releases and artifacts
   before changing repository visibility; cleaning `main` does not remove them.
3. On Windows, test key import, opening a copy of a real supported save, editing,
   review/undo, saving to a new file, and restoring a backup. Verify the original
   stays intact and the edited copy reloads. Record any in-game compatibility
   checks separately; synthetic checks do not prove story progress is safe.
4. Set `GTAV_SAVE_KEY` to the base64-encoded 32-byte save key. Releases require it;
   ordinary CI does not. Embedding it makes it extractable from the public EXE.
5. Push a version tag such as `v0.1.1`. The workflow tests, publishes with the key,
   launches the EXE and attaches its checksum, license and notices to the release.
   Tags below 1.0.0 and tags with a suffix are marked as prereleases. Manual runs
   require an existing tag and build that tag's commit.

The pinned SDK and package locks must be updated together for SDK servicing.
Code signing is not configured. The public README describes the shipped app;
keep maintenance notes and audit results out of it.
