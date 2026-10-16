# Releasing

Every push to `main`, merged pull requests included, publishes a release. Do
steps 1–3 on the pull request before merging.

1. Check Windows CI on the pull request. It runs synthetic save checks,
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
5. Merge to `main`. The release workflow tests, publishes with the key, launches
   the EXE, tags the commit and attaches it to the release. GitHub also provides
   the source-code ZIP automatically. The first release of the `<Version>` in `Directory.Build.props` uses it
   as is; later pushes bump the patch number (0.1.0, 0.1.1, …). Raise `<Version>`
   to start a new minor or major version. Versions below 1.0.0 are marked as
   prereleases. A manual run releases the current head of `main` if it has no
   release yet.

The pinned SDK and package locks must be updated together for SDK servicing.
Code signing is not configured. The public README describes the shipped app;
keep maintenance notes and audit results out of it.
