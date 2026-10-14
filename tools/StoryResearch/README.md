# Story flow research

Read-only verifier for deriving GTA V story command hashes without playing missions or capturing new saves.
It reads the public build-2628 `initial.c` decompilation and existing saves using this project's parser.
It never runs downloaded scripts, writes a save, or connects to a game process.

Source: <https://raw.githubusercontent.com/YimMenu/GTA-V-Decompiled-Scripts/master/decompiled_scripts/initial.c>

The extractor accepts only source SHA-256
`c151d005fa9cdbb3789d82d645f86bf19c2b29fb6e0a97dabf6cdad74b6aea8f`.
Downloaded game scripts are local research inputs, not included in this repository.

```powershell
dotnet build tools/StoryResearch/StoryResearch.csproj -m:1 -p:BuildInParallel=false
dotnet tools/StoryResearch/bin/Debug/net8.0/StoryResearch.dll <initial.c> <key-file> <save> [save ...]
```

It reconstructs the 32 strand command ranges (1,882 commands), including suppression of duplicate labels.
Hash calculation is implemented for 730 commands: simple integer commands, packed integer commands from
helpers 101/104, no-argument commands, labels, and mission commands from helpers 127/200.
Other command hashes remain explicitly unsupported. This is an extractor for one inspected source revision,
not a general C interpreter. The numbers identifying functions are specific to that revision.

On 2026-10-14 all 224 stored strand position/hash pairs across seven local save files matched:
the four `%1.6`, `%66.3`, `%98.8`, `%100` saves plus `100%/SGTA50000`, `SGTA50003`, and `SGTA50015`.
These are seven files, not seven independent playthroughs. Verification checked the original save checksums
before reading story fields. No generated save was loaded in-game.

Exit 0 means all supplied strand hashes matched; exit 3 means some were unresolved (which does not establish
save corruption); exit 2 means incorrect arguments. Unsupported source or malformed data raises an error.

See `docs/FINDINGS.md`, section 11, for the mission-skip path and remaining limitations. This tool does not
implement mission skipping; a matching hash alone does not establish a valid story transition.
