# Editor quick wins

- [x] Save as and Save to game slot update the active destination after a successful write; failed writes keep the previous destination.
- [x] Ctrl+S, Ctrl+Shift+S and Ctrl+F shortcuts; text bindings commit before saving with a shortcut.
- [x] Incomplete filters for the checklist, story missions and Strangers & Freaks, plus a missing filter for collectible lists and map markers.
- [x] Unsaved state reflects actual save data and clears after an edit is fully reverted.
- [x] Start page lists detected manual saves and autosaves across profiles with titles and modification dates.
- [x] Search reveals and briefly highlights matching entries, clearing conflicting filters and showing alternative mission approaches where needed.
- [x] Backup browser supports inspection and restore, keeps legacy backups visible, and backs up any replaced destination.

Regression checks cover destination changes and failures, dirty-state rebasing and reversion, profile discovery, backup metadata and legacy timestamps, restore preservation, invalid restores and empty slots. Windows smoke checks exercise UI construction, live completion filtering, dirty-state reversion, search navigation/highlighting and the backup browser.

Manual checks on Windows:

- [ ] Try all three shortcuts while editing cash, ammo and stat text; verify the focused input is included in the save review.
- [ ] Open a real save with missions, collectibles, vehicles and unlocks; search for an item on each tab and check its highlight.
- [ ] Filter missing collectibles, tick a map item, and verify the marker and list update together without changing the completion total incorrectly.
- [ ] Save as a different slot, edit again, press Ctrl+S and verify only the new slot changes.
- [ ] Inspect an older backup, save it as a working copy, then restore a backup over an occupied slot and confirm the replaced save remains in the browser.
