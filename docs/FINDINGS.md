# How "GTA V Save Editor.exe" edits saves — evidence

Static analysis only (reflection-only metadata loads + IL disassembly; the target was never executed).
Target: `GTA V Save Editor.exe`, SHA-256 `76e67e346e0ac968b6c42e94bf65ee9ab78620d31fa548c51c22f2a98637f1d1`, v5.9.0.0.

## 1. Packaging

| Observation | Evidence |
|---|---|
| VB.NET WinForms app, .NET Framework 4.8, DevComponents DotNetBar UI | `TargetFrameworkAttribute`, references `Microsoft.VisualBasic`, `DevComponents.DotNetBar2` |
| Obfuscated (SmartAssembly-style): renamed types, ~6,400 proxy-call delegates, encrypted strings (824 `ldstr ""` sites), `SuppressIldasmAttribute` | type dump, string dump |
| Dependencies embedded as 8 manifest resources | `GetManifestResourceNames()` |

Resource format, from `ActivityTable::ReduceDockingPane` (token `0x060001BB`):
bytes 0–2 ignored; `flags = ~byte[3]`; `flags & 2` → DES-CBC with IV = next 8 bytes, key = following 8 bytes;
`flags & 8` → raw deflate. Unpacked:

| Resource | Flags | Assembly |
|---|---|---|
| res0 | enc+deflate | `GTA V Save Editor&` (UI resources, embedded template saves) |
| res1 | deflate | DevComponents.DotNetBar2 12.4.0.2 |
| **res2** | **enc+deflate** | **`GTAV Class` 3.0.0.0 — the save-format library** |
| res3 | deflate | ICSharpCode.SharpZipLib |
| res4 | deflate | MultipartForm |
| res5 | enc+deflate | X3T_INFINITY |
| res6 | deflate | X360Lib (Xbox 360 STFS/FATX) |
| res7 | deflate | Compression (7-Zip LZMA SDK) |

## 2. The GTA V PC save format

### File layout (observed in the embedded real PC save `SGTA50009`, 547,768 bytes)

```
0x000  uint32 BE   1
0x004  UTF-16LE    title, zero padded to 0x104  ("XB36Hazard's Save Converter 2020 (100.0%) - 06/26/17 22:41:42")
0x104  4 bytes     unknown (preserved)
0x108  ...         encrypted body; (fileLength - 0x108) % 16 == 0
```

The body is **ECB**-encrypted: identical 16-byte ciphertext blocks repeat every 16 bytes from 0x128 onwards
(encrypted runs of zeros), aligned to 0x108.

### Encryption — `GTAV_Class::Decrypt/Encrypt` (`0x0600000F` / `0x06000011`)

`AesCryptoServiceProvider`, `KeySize = 256`, `Mode = ECB (2)`, `Padding = Zeros (3)`, `TransformFinalBlock` over the whole body.
The overloads without a key pick a hard-coded static field per platform (`X360_Key`, `PS3_Key`, `PC_Key`, 32 bytes each,
initialised in `GTAV_Class..cctor`, token `0x0600004A`). **The `PC_Key` initializer does not decrypt the supplied PC save.**
The main editor instead constructs another key and passes it to the explicit-key overload; see section 4.
Key bytes are not included in source. Our app accepts an imported key only after a real save passes its checksum.

### Decrypted body — `GetBlocks` (`0x06000018`)

A sequence of blocks: `ID` (4 ASCII bytes) + `size` (int32 **big-endian**, includes the 8-byte header) + data.
The reference reads exactly 5 blocks; the 5th is `CHKS`.

### Checksum — `VerifyHash` (`0x06000014`) / `ReplaceBlocks` (`0x06000019`) / `GetHash` (`0x06000016`)

* `CHKS.data[0..4]` = int32 BE total length of all blocks; `CHKS.data[4..8]` = uint32 BE hash.
* Hash input: all blocks re-serialised, with `CHKS.data[0..8]` zeroed.
* Hash: Jenkins one-at-a-time, seed `0x3FAC7125`, bytes **sign-extended** (`ldelem.u1; conv.i1`).
* Valid ⇔ stored length == serialised length **and** stored hash == computed hash.

Other hashes in the library: `GetNativeHash` = standard JOAAT (seed 0) used for stat/model names;
`encode_date/decode_date` pack timestamps; `GET/SET_COMPONENT_DRAWABLE_INDEX` decode outfit flags.

## 3. What the editor can change (enum names that survived obfuscation)

`Mission_Target_IDs` (343), `StatType`, `Characters`, `Skills`, `VehicleTypes`, vehicle mods (`Armors`, `Brakes`, `Engines`,
`Horns`, `Lights`, `Plates`, `Suspensions`, `Transmissons`, `Turbos`, `Windows`, rim sets), `Michael/Franklin/TrevorTatooIDs`,
phone `Backgrounds/Ringtones/Themes`. It also embeds whole template saves (`SGTA50009`, `SGTA50000_Xbox360`, `DefaultPC`),
which is how "jump to a point in the story" works: ship a known-good save rather than synthesise one.

## 4. Working PC key recovered and verified (2026-10-13)

The earlier scan of contiguous 32-byte windows failed because the working key is assembled from two separate
16-byte slices. The supplied `GtaStuff/gtasaves/SGTA50000` now decrypts and validates with the recovered key.
No additional AES passes or changes to the app's encryption/checksum implementations were needed.

### Static recovery path

* `MenuItemAttribute.TogglePath` (`0x060055B2`) is a delegate proxy for the explicit-key `GTAV_Class.Decrypt`
  MemberRef `0x0A000715`. Its `.cctor` supplies that MemberRef token to `AspectInfo.ReduceDockingPane`
  (`0x06000475`), which resolves the method and creates the delegate.
* `Config.ClearReference` (`0x06000F67`, IL `0x0172`) passes the result of `Config.ReduceDockingPane`
  (`0x06000FCF`) as the fourth argument to that proxy. The latter method constructs the working key.
* `BookmarkFactory.get_X` (`0x060000B4`) obtains byte-array resource `X` from
  `GTAV_Save_Editor.Resources.resources` in the unpacked `GTA V Save Editor&` assembly. `X` is 8,192 bytes.
* `SolutionScope.TogglePath` (`0x0600013B`) reads little-endian int32 constants from the unpacked
  `GTA V Save Editor%` resource. The key builder initializes seven ints from FieldRVA token `0x04000A01`
  (RVA `0x2DBC6C`) and computes source positions using those constants.
* The final 32 bytes are `X[5776..5792]` followed by `X[2816..2832]`. These are offsets in the actual byte-array
  payload, excluding the four-byte `.resources` ByteArray length prefix. Key bytes stay in local files only.

The reproducible metadata-only extractor is `tools/KeyRecovery`. It accepts only the exact v5.9.0.0 reference
binary SHA-256 recorded at the top of this document and never executes the reference editor. It validates the
checksum and an identical decrypt/encrypt round trip before writing a key. `recover` writes a chosen local file;
`install` uses the app's `%AppData%/SaveEditor/keys/gtav-pc.key` store. See its README for commands.

### Validation against the supplied save

| Observation | Result |
|---|---|
| Title | `The Last One (100.0%) - 02/28/25 12:22:18` |
| Save size | 567,128 bytes |
| Save SHA-256 | `f1d19cb507d7fbdb64d77752001e60fae78abb80c6dbb51e8dd6e69370797453` |
| Parsed blocks, including eight-byte headers | `PSIN` 432,184; `PMAP` 672; `PSCH` 132,232; `PSIG` 1,744; `CHKS` 20 bytes |
| Stored and computed checksum | `0xF2988783`, both over 566,852 bytes |
| AES trailer | 12 bytes preserved |
| Unedited decrypt/encrypt round trip | Byte-for-byte identical to the original save |
| Original `.bak` | Identical to the supplied save; not an independent second sample |

The input save and its backup were not modified. Game/editor binaries, player saves, generated artifacts, and
`*.key` files are excluded by `.gitignore`. Recovery is verified for this binary and sample; other editor builds
and GTA V save variants have not been tested.

### Evidence and limitations

* REA original assembly inventory: `ev_8510eb8a52d1567ae4fe50776351520b07007f1561d6b5315fbf8c1368c458ab`.
* REA embedded `GTAV Class` member inspection: `ev_5863b2e30ec689e9e8a290e6bddffc8f76371fad98a7b41d460f3ab7146f503e`,
  artifact SHA-256 `0c62e1ec058710a38d0e30286e29adcad86647195bde5c729bb4b908b7d27e01`.
* REA main-assembly member inspection exceeded its 64 MiB metadata representation budget:
  `ev_e462af738150d90d85845173ae2dd85931dc72a8217c335607106cdb08f37167`. Focused CIL/resource inspection used
  `System.Reflection.Metadata` instead. The local `artifacts/key-recovery/pc-key-builder.il.txt` and
  `custom-key-decrypt-call.il.txt` retain decoded instructions, proxy destinations, and numeric constants.
* Static observations establish the reference's construction and call path. Verification executed this
  project's own decrypt/checksum/encrypt code against the sample; the reference editor and GTA5.exe were not run.

## 5. Stats (mapped from the decrypted sample save)

`PSIN` holds sorted stat tables. Entries are big-endian `(uint32 JOAAT(lower-case stat name), value)`, strictly
ascending by hash, so lookups are a binary search. A directory at `PSIN+0x9000` lists sections as
`8-byte ref, uint16 BE count, uint16 BE capacity, 4 bytes`; its counts match the tables found:

| Table | Entry | Count (directory) | Sample offset | Identified by |
|---|---|---|---|---|
| Int32 | 8 bytes (hash, int32) | 2,599 (`0x9000`) | `0x060A48` | `SP0_TOTAL_CASH` |
| Float | 8 bytes (hash, float32) | 131 (`0x9010`) | `0x0603A8` | `TOTAL_PROGRESS_MADE` = 100.0 |
| UInt64 | 12 bytes (hash, uint64) | 106 (`0x9070`) | `0x069338` | `SP0_TOTAL_PLAYING_TIME` (ms) |

The editor does not hard-code offsets: `StatStore` finds each table by growing a sorted run around its anchor stat.
Values in the 100% sample are consistent: `NUM_MISSIONS_COMPLETED` = 69 (number of story missions), all seven
skills = 100 for each character, `TOTAL_PROGRESS_MADE` = 100.0. Cash: Michael $767,345,781, Franklin
$1,589,772,362, Trevor $810,836,951. Editing a value in place keeps the size and the checksum is recomputed on save;
a test edits a copy of the real save, reloads it and verifies the checksum.

**Confirmed in-game (2026-10-14, haircut test):** in saves the game itself wrote before and after Michael bought a
$180 haircut, `SP0_TOTAL_CASH` dropped by exactly 180 while `SP0_MONEY_SPENT_ON_HAIRDOS` and `SP0_MONEY_TOTAL_SPENT`
rose by 180. So these are the values the game really uses. The hairstyle itself is not a stat.

**Not yet verified in-game:** that the game *accepts* a save edited by this app (the test save was never loaded).

**Slots:** the Load Game menu lists only `SGTA50000`..`SGTA50014` (slots 1–15) and `SGTA50015` (autosave), and the
game reads the list only at startup. A `cloudsavedata.dat` index sits next to the saves when cloud saves are on.

**Compare tab:** `SaveComparer` lists every stat whose value differs between two saves, named where the name is known,
plus changed bytes per block. Saves loaded far apart differ in thousands of places (world state), so the useful pair is two
saves made back to back with one change in between.

## 6. Not yet known

* Mission completion flags (story-skip). The mission *counters* are stats, but completing missions also involves
  script data — changing counters alone will not complete missions, so they are read-only for now. The editor's
  mission-flow names (`Mission_FlowCompleted`, `Mission_FlagIDs`, …) do not occur as hashes in the save; they are
  probably resolved through its mission list `Main\MissionsInfo\MissionsInfo.txt` (columns `MissionID`, `Description`,
  `StoryDescription`; the path is base64 in the string table). That file is not installed with the editor; it is
  apparently fetched from `x3t-infinity.com/Files/` behind the editor's sign-in/API. (`Content_Update\…` holds vehicle and
  weapon art, not missions.)
* The exact meaning of the weapon slot fields after hash + ammo (attachments bitfield and tint, by the values seen).
* Which of the three `WEAPONS` records is which character (assumed game order Michael, Franklin, Trevor).
* Whether give/remove/ammo edits behave in-game as expected (not yet tested in the game).
* The 4 bytes at 0x104 (preserved verbatim; edits so far round-trip correctly in tests).

## 7. The save body is a PSO document — how the reference editor edits it (2026-10-14)

Analysed with REA (managed inventory of the installed `C:\Program Files (x86)\XB36Hazard\GTA V Save Editor\GTA V
Save Editor.exe`, same SHA-256 as above, evidence `ev_5385769075bfd2f907fb480ac6896ce352dd25aba60eea44009e7eab5f72f33c`;
embedded `GTA V Save Editor&` assembly, evidence `ev_88734cbf6e9348e28f58d5a685ecd7203ba371a4d1188951f80af045a49eda61`)
plus metadata-only IL reads. Nothing was executed.

**Strings.** The 824 `ldstr ""` sites are followed by `ldc.i4 N; call FileLayout::TogglePath` (`0x06000173`), which reads a
length-prefixed (1/2/4-byte big-endian prefix, top bits = prefix size) UTF-16 string at offset N of a table loaded by
`FileLayout..cctor` from resource `GTA V Save Editor$` (unpacked with the same DES/deflate scheme as section 1).
Decoding it statically gives 7,780 strings: UI labels, 27 panel names (`Weapon_Panel`, `Garage_Panel`, `Missions_Panel`,
`Collectibles_Panel`, `Tattoos_Panel`, …) and the game's **script save variable names** (`WEAPONS`, `SHOP_PROPERTIES`,
`iRCMissionsCompleted`, `VEH_POS`, …).

**Format.** The blocks are Rockstar's PSO format (as in `.ymt` files), all big-endian:

| Block | Content |
|---|---|
| `PSIN` | All data. Pointers are 8 bytes: PMAP block id (1-based) in the low 12 bits, offset in the block in the next 20 |
| `PMAP` | `int32 root block id, uint16 count, 0x7070`, then 16-byte entries `(type hash, PSIN offset, 0, length)` |
| `PSCH` | `int32 count`, `(name hash, offset)` index, then structs `[0][-][uint16 fields] length 0` + 12-byte fields, and enums |
| `PSIG`, `CHKS` | signature / checksum (section 2) |

PMAP/PSCH offsets count from the start of the section *including* its 8-byte header. A field is
`(name hash, type, subtype, uint16 offset, reference)`; types `0 bool, 1/2 int8/uint8, 3/4 int16/uint16, 5 int32,
6 uint32, 7 float, 8/10/11 vectors, 9 string, 12 struct, 13 array, 14 enum, 15 flags`. **Bits 6–7 of the subtype are
bits 16–17 of the offset** (the script-globals struct is 0x4B5E8 bytes; without this every value past 64 KB is wrong).
Struct subtype 0 = inline, otherwise a pointer; array subtype 0 = `atArray` (pointer + uint16 count), 1 = inline with the
count in the reference's high 16 bits; the reference's low 16 bits index the element descriptor (a field named `0x100`).

**Names.** Field names are JOAAT hashes of the **exact-case** script variable name (`JOAAT("WEAPONS")`, not lower-case).
242 of the editor's strings match fields in the sample save; they ship in `PsoNames.txt`. The rest of the tree is unnamed.
The script save data (`g_savedGlobals`) is block 17, 309 KB, reached via root → `0x4B041FD7` → `0x77714C97`.

**Weapons.** `WEAPONS` holds 3 character records; each has two slot tables (base game: 44 slots, DLC: 51, or 50 in older
saves) of ints at 8-byte spacing. Slot 0 is unarmed. Every weapon has a **fixed slot**; a slot the character doesn't
own is all zeros. Slot formats:

| Saves | Slot | Unmodified owned weapon |
|---|---|---|
| current (100% sample, `DefaultPC_41917`, `SGTA50009`) | 5 ints `[hash, ammo, attachments, tint, -1]` | `[hash, ammo, 0, 0, -1]` |
| oldest (`DefaultPC`) | 3 ints `[hash, ammo, (1 << (20 + tint)) \| attachments]` | `[hash, ammo, 0x100000]` |

The slot size is read from the field names: slot 0's weapon field is `0x6DBFBC23` and slot 1's is `0x7866D171` in every
version. The weapon hash is JOAAT of the lower-case id (`weapon_pistol`). The slot map (`WeaponCatalog`) comes from the
100% sample and matches `DefaultPC_41917` and `SGTA50009`. Base table: 1 knife … 7 stun gun, 8–10 pistols, 11–17
SMGs/rifles/MGs, 18–20 shotguns, 21–22 snipers, 23–25 heavy, 26–29 throwables, 31 jerry can, 33 parachute, 36–38 assault
SMG / bullpup shotgun / pistol .50 (35 holds an uncatalogued hash `0x39958261` and is left alone). DLC table: 0 bottle …
20 railgun. **Exception:** the oldest template's DLC table is ordered differently (homing launcher in slot 6, where current
saves keep the dagger), so a table that doesn't match the map is handled conservatively: weapons found in it stay editable
in the slot they were found in, missing ones are not offered. Full ammo seen: guns 9,999, grenade launcher / RPG /
firework / railgun 20, throwables 25, proximity mine 5, homing launcher 10, jerry can 4,500, parachute 1; melee none.
`SGTA50009` is a 100% save with almost no weapons (Franklin 8, others none), handy for testing "give".

**Templates are not story checkpoints.** All three embedded templates report 69/69 missions and 100% completion, so they
cannot be diffed to find mission flow data.

**App.** `PsoDocument`/`PsoNode` parse this generically; edits write into PSIN in place (sizes never change) and the
checksum is recomputed on save. The app flow is save picker → editor with friendly sections (Characters:
cash + skills; Weapons: give/remove/ammo per character; Story: read-only progress). The raw **Save data**, **Compare**
and **File** views live in an *Advanced* window. None of the edits are verified in-game yet.

## 8. Architecture decision

The app was first planned as a multi-game editor (one module per game). On 2026-10-14 it became a **GTA V-only** editor:
the module layer was removed and everything lives in `SaveEditor.GtaV`. The flow is read → decrypt → parse → edit →
recompute checksums → encrypt → back up → write.

## 9. Story progress lives in the flow struct (2026-10-14)

From four saves of one series (`GtaStuff\gtasaves\%1.6`, `%66.3`, `%98.8`, `%100`: 2, 32, 69, 69 of 69 missions),
found by counting non-zero entries per struct table across the saves. Under the script save data
(`0x4B041FD7/0x77714C97/0x412DDFE6`) the struct field `0x2FC0B451` (type `0x77714D98`) holds the game's mission flow:

| Field | Shape | Meaning (inferred) |
|---|---|---|
| `0xD207EA69`, `0xFB17542D` | int | flow on / finale done? (`0xFB17542D` 0→1 once all story missions are done) |
| `0xDEE6F89E` | 32 structs `[flags, position, hash]` | **story strands**. `position` is an index into one global flow command list (each strand owns a contiguous range: strand 1 183–237, strand 2 238–271, …); `hash` changes with it (likely a check value for the command there); `flags` 0 → 2 (running) → 6 (finished) |
| `0xA277E18D` | flags (~146 bools), ints `[13]`, bitsets `[11]` | flow flags / counters / bitsets |
| `0x739B7568` | 100 structs `[done, ?, skipped?, order, ?, score]` | **per-mission records**: `order` = completion order (-1 = not done; 0 = Prologue at index 53, 1 = index 0 Franklin and Lamar, 2 = index 1 Repossession, 4 = index 2 Complications), `score` = medal %. Order looks like the game's alphabetical `SP_MISSION` enum |

Also `0xA2A52C6E` (63 entries) tracks random-character missions and `0x866CE621/0x1A341F71` (321) is the 100% checklist.
The strands' positions drive story execution, and a position must match its command hash. These four saves give
known-good strand states at four points. **Updated by section 11:** the game's command list and hash algorithm are now
available from decompiled scripts, and the controller has a conditional shortcut for already-completed mission records.

### How the reference editor edits story progress (static IL reading, 2026-10-14)

* It needs two data packages in `%LOCALAPPDATA%\XB36Hazard\GTA V Save Editor\`: `Main.db` and `Content_Update.db`.
  `Config::CheckWindow` (`0x06000F4E`) checks both ("Verifying Databases", with a SHA-256 constant). If either is missing
  it shows "… is required to run editor. Download at X3T-Infinity.com/GTAV_SE" and kills its own process. On this PC
  the folder exists but is empty.
* `Main.db` is a package (`PluginSerializer`, opened by `Config::GenerateAddin` `0x06000FE1`; entries are found by
  `\`-separated paths). It holds fonts, ringtones, icons, `Main\Missions` and `Main\MissionsInfo\MissionsInfo.txt`
  (read by `Config::TogglePath` `0x06001043`; columns `MissionID`, `Name`, `Description`, `StoryDescription`).
* `BuilderHelper::TogglePath` (`0x06000846`, about 52 KB of IL) is a hard-coded layout map of the save's script data that
  gives its own names to the flow-struct parts: `Mission_IsGameFlowActive`, `Mission_FlowCompleted`, `Mission_SyncIDs`,
  `Mission_FlagIDs`, `Mission_IntIDs`, `Mission_BitSetIDs`, `Mission_List`, `Custom_Flow_Order_Missions_STRANDS`,
  `…_OVERRIDE`, `…_HASH_IDs`, `…_MissionsToUnComplete`, and counters (`Flow_Order_Mission_MissionsCompleted`,
  `…_MissionGolds`, …). These match the strands (with hash), flags, ints, bitsets and mission records found above.
  No other method references these name strings, so how the mission UI uses them is still open.

### What `Main.db` contains (user-downloaded copy, `GtaStuff\Main.db`, 20,628,832 bytes; 2026-10-14)

* **Package format** (from `PluginSerializer` / `Config::GenerateAddin` / `Seven_Zip::Decompress`):
  * Header: `"FD"`, a big-endian int32 table length (×2), then `"XB36Hazard"`.
  * The entry table starts at offset 16, then the file data.
  * Encryption is AES-256-CBC. The key comes from the editor's string table; like the Rockstar key, it is not
    reproduced here.
  * Compression is 7-Zip LZMA: 5 property bytes, an 8-byte little-endian size, then the stream.
  * The table is a big-endian folder tree:
    * file count, then each file as (name length, name, int64 offset, int64 size);
    * subfolder count, then each subfolder as (name length, name, folder).
  * 2,341 entries in total. Main folders: Checklist, Checklist_Icons, Missions, MissionsIcons, MissionsInfo,
    SFMissions, Heist, Stats, Weapons.
* `Main\Missions\0..100` are 32 bytes each, with the same field shape as one save mission record.
  * Layout: `[done=1, 0, 0, order, time-like int, score as float (100.0), int, int]`.
  * In other words, they are "completed" record templates (all `done = 1`, with the completion order of one full
    playthrough).
  * **They contain no strand positions or hashes.** `Main\MissionsInfo\MissionNames.txt` is `HASH=Name~`.
    `MissionsInfo.txt` is a list of 706 internal bug-tracker categories ("AMB - …", "CnC - …", "RC - …"), not
    flow data.
* Conclusion: nothing in the editor's database lets it compute a strand position and hash for an arbitrary mission. Its
  story editing most likely changes mission records (completed / medal / time / order: `FunctionDictionary` has a
  medal combo, an int, a bool and a time input; `SelectionTree` has "Mission_ChangeOrder") and flags. That has not been
  confirmed in-game.

### The mission screen (`DeploymentQueue` form), static reading 2026-10-14

* `ToolboxSite` (constructor `0x0600169E`) models the flow struct as 61 named parts. It loads `Main\MT` from `Main.db`.
  * `MT` layout: count 100, then per mission `n` followed by `n` pairs of `[stat hash, target value]`.
  * These are the gold-medal targets (`MissionTargetEdit`). `SFMT` holds the same for Strangers & Freaks.
* Story choice handlers write a stat plus a flow flag by index:
  * `0x0600038B`: `SP_KILLED_AL` / `SP_KILLED_DR_FRIEDLANDER` / `SP_KILLED_ORTEGA` with flags 101/80/132.
  * `0x0600038D`: final choice, flags 134–136.
  * `0x0600038E`: heist approach `SP_HEIST_CHOSE_*`.
* Other screen features:
  * a per-mission value editor (`FunctionDictionary`: completed switch, medal, int, time);
  * "Reorder Missions" ("Change the order of completed missions", `SelectionTree`).
* No method found here writes strand positions or hashes. A search of the exe, its unpacked resources, the decoded
  constant table and every `Main.db` file for all 67 strand hash values seen in the four saves found only
  coincidental hits. The editor carries no table of strand states.

### Field names are JOAAT hashes of the game's own script names

* All 146 flag bools in `0xA277E18D/0x29036862` match `joaat(name)` for the editor's flag list.
  * Examples: `FLAG_H_BOARD_UNDO`, `FLAG_M_T_EXILE_FINISHED`, `FLAG_FINAL_CHOICE_MADE`, `FLAG_GAME_100_PERC_COMP`.
  * The editor stores the list as a base64 `~` list in its string table.
  * Across the four saves the values make sense, e.g. `FLAG_GAME_100_PERC_COMP` is set only in the 100% save.
* 19 of the 32 strands match `joaat("STRAND_" + name)`:
  * ARMENIAN, ASSASSINATIONS, CAR_STEAL, CHINESE, EXILE, FAMILY, FBI_OFFICERS, FINALE, FINALE_HEIST, FRANKLIN;
  * LAMAR, LESTER, MARTIN, MICHAEL, MICHAEL_EVENTS, PROLOGUE, SHRINK, SOLOMON, TREVOR.
  * The other 13 are probably the other heists and their variants (not yet guessed).
* The strand `hash` values are not JOAAT of any mission or script name tried. **Updated by section 11:** they hash
  command data, including its owning strand, and can be derived without a real save at every checkpoint.

## 10. 100% checklist (2026-10-14)

* Location: struct field `0x1A341F71` under `0x866CE621`. It holds 321 entries of struct type `0x77714EA5` (56 bytes each).
  Entries with an empty label are unused.
* Entry fields:

  | Field | Offset | Type | Meaning |
  |---|---|---|---|
  | `0x2D4B9B3A` | 0 | Float4 (16 bytes) | label, zero-padded ASCII, e.g. `M_ARM1`, `SC_RC_OME1`, `RE8A` |
  | `0x8EF72701` | 32 | Float | weight: the share of the 100% the entry is worth |
  | `0x11782F19` | 40 | SInt | completed (0/1) |
  | `0xEB5686E6` | 48 | SInt | category |
  | `0xF9050E9A`, `0xB6A063B0`, `0xEFC7B9B3`, `0x166F30BC`, `0x202DC439` | 56–88 | | not needed; `0xB6A063B0` looks like an id |

* Categories:

  | Category | Pause-menu section | Counts toward 100% |
  |---|---|---|
  | 1 | Missions | yes, 55% |
  | 3 | Hobbies & Pastimes | yes, 10% |
  | 7 | Strangers & Freaks | yes, 10% |
  | 9 | Random Events | yes, 5%, any 14 |
  | 11, 13 | Miscellaneous | yes, 20% |
  | 4, 8, 12, 14 | optional entries in the same sections | no |
  | 0, 6 | unused / odd jobs (weight 0) | no |

* Formula: completion = sum of completed weights in categories 1, 3, 7, 11 and 13, plus `5 * min(cat 9 done, 14) / 14`.
* The game keeps the pause-menu figures in stats derived from the same entries:

  | Stat | Value |
  |---|---|
  | `TOTAL_PROGRESS_MADE` | the completion % above |
  | `NUM_MISSIONS_COMPLETED` | cat 1 done |
  | `NUM_MINIGAMES_COMPLETED` | cat 3 done |
  | `NUM_RNDPEOPLE_COMPLETED` | cat 7 done |
  | `NUM_RNDEVENTS_COMPLETED` | `min(cat 9 done, 14)` |
  | `NUM_MISC_COMPLETED` | cat 11 + cat 13 done |
  | `PERCENT_STORY_MISSIONS` | `floor(100 × missions / total cat 1)` |
  | `PERCENT_ODDJOBS` | `floor(100 × minigames / total cat 3)` |
  | `PERCENT_AMBIENT_MISSIONS` | `floor(100 × (people + events) / (total cat 7 + 14))` |

* Verified exactly against the stored stats in 11 saves: the four story saves, the sample 100% save, the embedded
  template `SGTA50009`, and the user's profile saves (read only).
* Display names come from `Main.db`'s `Main\Checklist_Icons\*.txt` lists. They were cross-checked and cleaned into
  `src/SaveEditor.GtaV/Data/ChecklistNames.txt`. Check X3T's terms before shipping this list.
* Our editor (`Checklist.cs`) flips the completed flag and rewrites the stats above. This changes the checklist and
  the completion figures only. It does not play a mission, unlock what it unlocks, or advance the story strands
  (see section 9).

## 11. Deriving story transitions without manual gameplay (2026-10-14)

Constraint: mission skipping must not depend on the user playing through missions and capturing save pairs.

### Sources and evidence boundary

Public build-2628 decompilations:

* [initial.c](https://github.com/YimMenu/GTA-V-Decompiled-Scripts/blob/master/decompiled_scripts/initial.c),
  SHA-256 `c151d005fa9cdbb3789d82d645f86bf19c2b29fb6e0a97dabf6cdad74b6aea8f`.
* [flow_controller.c](https://github.com/YimMenu/GTA-V-Decompiled-Scripts/blob/master/decompiled_scripts/flow_controller.c),
  SHA-256 `1a76faa65a3d3e19832dccadd7c723d53d4ffce69fe28226354634f597ef723b`.
* [flow_autoplay.c](https://github.com/YimMenu/GTA-V-Decompiled-Scripts/blob/master/decompiled_scripts/flow_autoplay.c)
  only terminates its thread in this build; it is not a usable automatic playthrough facility.

These are third-party decompilations of shipped scripts, not original source or a decompilation of this PC's current
installation. Function numbers and global indices are build-specific. REA session inspection found no active analysis
target and no configured native decompiler; this pass used static source reads and the project's own save parser.
No game process, reference editor, or downloaded game script was executed. The source digests above and the repeatable
verifier identify this evidence; there is no new REA binary-analysis Evidence ID for these source-derived conclusions.

### Command hashes are reproducible

`initial::func_85` dispatches 32 strand builders. Together they emit 1,882 commands into
`Global_78127.f_6825`, with ranges in `f_12526`. `func_128` suppresses duplicate labels across the command list.
`flow_controller::func_10` and helpers 11–19 hash decimal concatenations of opcode, owning strand number, and command
arguments. Complex argument groups are separately hashed, then appended as signed decimal integers.

The read-only `tools/StoryResearch` utility reconstructs those ranges and supports hashes for 730 commands, leaving
other command types explicitly unsupported. **All 224 stored position/hash pairs across seven existing save files
match exactly**: the four story saves in section 9 and `100%/SGTA50000`, `SGTA50003`, `SGTA50015`. These files include
related saves, not seven independent playthroughs. Every input passed checksum verification. No save was modified.
The actual strand 1 range is **133–237**, correcting the earlier lower-bound observation of 183.

`initial::func_55` checks the position/hash pair; `func_44` searches the owning strand for the saved hash if the position
does not match. This explains why hashes, rather than bare command indices, matter across script revisions.

This verifies the hash algorithm for the observed commands; it does not validate every one of the 1,882 commands,
all current game versions, or a synthesized story transition.

### The controller has an already-completed mission path

`flow_controller::func_314` handles opcode `889972124`. When waiting for a mission trigger, `func_722` consumes a
runtime trigger latch. If no trigger occurred but saved mission record `Global_112922.f_9085.f_330[mission /*6*/]`
is true, the handler sets runtime started/success bits. On a following evaluation, with no mission script running,
it enters common success handler `func_363`. That handler returns the command's success label; `func_3`/`func_4`
resolve the label in the owning strand and move the story position. This is distinct from the weighted checklist
edited by the app today.

Another handler, `func_1018` (opcode `-2093459088`), has a similar path with an additional runtime bit 6 that suppresses
some checkpoint/last-mission processing. `func_1015` (opcode `-868169264`) also checks completed records, but its control
flow can still reach script startup in that invocation; do not assume all mission types behave identically.

**Inference:** selected missions could be skipped by updating their real mission records and letting the game run its
own following story commands, without manually playing them. This is a candidate implementation, not yet verified
in-game. It is not equivalent to setting the overall completion percentage.

### Remaining implementation questions

* Premarking a mission done bypasses first-time bookkeeping in `func_418`: checklist mapping (`func_243`), completion
  order/counter/stat writes (`func_419`), and certain profile hooks. A skip implementation must account for those.
* Preceding conditions, dialogs, timers, character-state gates, heist choices, and branch dependencies still apply.
  Arbitrary jumps need a transition planner, not just a bit toggle or a new strand position.
* Common success handling does not establish that all persistent effects normally written by each mission script
  occurred. Rewards, unlocks, and branch-specific state need separate coverage.
* The current installed game script version has not been compared with the public build-2628 source. Existing-save
  hash matches are useful compatibility evidence, not proof that all present-day transitions are identical.
* Runtime validation can use generated copies of the existing saves. Manual mission playthroughs are not required
  to derive the command table; loading a generated save and observing the next available mission remains necessary
  to establish actual skip behavior.

Status: command-list recovery and sample hash verification completed; mission success shortcut statically traced;
complete skip/unlock behavior and game acceptance remain unresolved. The production app is unchanged by this research.

## 12. Mission records and the Missions screen (2026-10-14)

Implemented in `src/SaveEditor.GtaV/Features/StoryMissions.cs`, using the shortcut from section 11.

* **Mission records.** `0x739B7568` under the flow struct (`Global_112922.f_9085.f_330`): 100 records of
  `[done, ?, ?, order, time, score(float)]`, indexed by the game's mission enum (`mission-metadata.json`; 0 = `armenian1`,
  53 = `prologue1`, 94–99 unused). An unplayed record is `[0, 0, 0, -1, 0, 0]`.
* **What a normal completion writes** (`flow_controller` `func_418` → `func_419`, `func_243`), when the record was not
  already done:
  * the record's `done` = 1 and `order` = the running completion order;
  * the missions-completed counter `Global_112922.f_10016.f_21` is incremented. In the save it is
    `0x412DDFE6/0x83B9CA30/0xFAA57378` (2, 35, 86 in the 1.6/66.3/98.8% saves);
  * stat `FL_CO_<code>` = the new counter value (not for missions with flag bit 7: `FIB4I`, `FINI`, `FINC1`, `FINC`).
    `JH2` uses `FL_CO_JH2A` or `FL_CO_JH2B` by approach;
  * the mission's 100% checklist entry (raw table index from `func_423`) is ticked and the percentage recomputed;
  * `func_421` also stores a "last mission" id for the UI; the editor does not write it.
* **Check against real data.** On the 1.6% save, completing Repossession with the editor gives order 2,
  `FL_CO_ARM2 = 3`, `NUM_MISSIONS_COMPLETED = 3`. These are the exact values the game wrote for Repossession in the
  66.3% and 98.8% saves of the same playthrough (`tests/StoryMissionsTests.cs`).
* **Order.** The editor's story order follows the user's two 100% playthroughs (`100%/SGTA50000` and `%98.8`).
  They took different approaches, so between them they cover every heist option except Merryweather B (`DHP2B`,
  `DH2B`), which neither save played.
* **Approach mapping.**
  * Observed in the saves: Jewel Store A = `JHP1A` + `JHP2A`, B = `JHP1B`; Bureau Raid A = `AHP1` + `AH3a`,
    B = `AH3b`; Big Score A = `FHPRA` + `FHPC1..3` + `FH2A`, B = `FHPRB` + `FHPD` + `FH2B`; Ending C = `FINC1` + `FINC2`.
  * Assumed: the option names shown in the UI (Smart/Loud, Offshore/Freighter, Fire Crew/Roof Entry, Subtle/Obvious)
    for the A/B letters.
* **Background events.** Michael's family events (`MEA1`, `MEJ1`, `MET1`) are never "the next mission": the 98.8%
  playthrough never got `MEA1`.
* **Unused.** `FB4P3` is not listed; neither playthrough has it.
* **Not verified in-game yet.** A test file is in `GtaStuff/skip-test/SGTA50001`: the 1.6% save with Repossession
  completed. Expected in game: Repossession is skipped and the story moves to the next mission(s).

## 13. Strangers and Freaks (2026-10-14)

* **Records.** Script data `0x412DDFE6/0xA2A52C6E`: 63 records of 6 ints `[stateBits, done, ?, order, ?, score]`.
  Passed looks like `stateBits = 63, done = 1`; unplayed keeps only bits `0b010010`, with `done = 0, order = -1`.
* **Damaged Goods (index 36)** is passed with `stateBits = 56` and `done = 0`. Completed is therefore
  `done != 0 || (stateBits & 32) != 0`. The editor writes Damaged Goods the same way.
* **Counter.** `0x91D94BB8` (`Global_112922.f_18574.f_380`); also the stat `NUM_RNDPEOPLE_COMPLETED`.
* **Shared order.** Story missions and Strangers & Freaks share one completion order. The next order is
  max(order over completed records in both tables) + 1 (`CompletionOrder.Next`).
* **Checklist.** Each S&F mission ticks its `SC_RC_*` checklist entry by label. Abigail2, Barry3A/3C and
  Paparazzo3A/3B have none. The mapping agrees across all 5 saves (scratch tool `rcmap`).

## 14. Player status and game clock (2026-10-14)

* **Player ped struct** `0x412DDFE6/0x622852C8`. Every field holds three values (Michael, Franklin, Trevor):
  * Health `0xCC13AAD0` (float, 100 = full);
  * Armor `0x9CDB82CB` (int);
  * `SPECIAL_ABILITY` (int 0/1 = unlocked);
  * `SPEC_AB_UNLOCK` (int, bar %);
  * `LAST_KNOWN_WANTED` (stars);
  * `LAST_KNOWN_XCOORD` (9 floats = xyz × 3);
  * `LAST_KNOWN_HEADING` (3 floats).
* **Current character.** `PP_CURRENT_PED` is the character played when the save loads.
* **Clock.** The root struct `SimpleVariables` (`0x765FB4E3`) holds the in-game clock as uint fields:
  * hour `0xC8893D05`, minute `0xFB8C5018`, second `0xAE65D6CB`;
  * day `0xDBCB068B`, month `0xBB64A995` (0 = January), year `0x9EBBBABF`.
  * Their names are unknown. They were identified by their values across saves, and the 0-based month is an
    assumption.
* **Not verified in-game yet.**

## 15. Stats page (2026-10-14)

* **Name source.** `src/SaveEditor.GtaV/Data/StatNames.txt` lists 1,761 single-player stat names. They are the
  game's own names.
  * Names and min/max ranges were taken from the reference editor's stat lists (`Main.db` → `Main\Stats\*.txt`).
  * The categories are ours; the reference editor's descriptions are not used.
  * Check X3T's terms before shipping (same caveat as `ChecklistNames.txt`).
* **Per-character names** are stored as `SP0_` (Michael), `SP1_` (Franklin) and `SP2_` (Trevor) + name.
* **Coverage.** 2,691 of the expanded names exist in the 100% sample save. The table holding each one fixes its type:
  * the Int32 table holds int, bool and label values;
  * the Float table holds floats;
  * the UInt64 table holds u64 and also u32 values.
* **Not shown:**
  * packed stats (`*_PSTAT_*`), labels and profile settings;
  * names the save doesn't have.
* **Read-only on the Stats page**, because other tabs keep them consistent:
  * `FL_CO_*`, `NUM_MISSIONS_COMPLETED` and `NUM_RNDPEOPLE_COMPLETED` (Missions and Strangers & Freaks tabs);
  * `PERCENT_*` and `TOTAL_PROGRESS_MADE` (Checklist tab).
* **Heist crew stats.** `HCS_<heist>_CREW<n>_CHOICE` hold the crew picked for each heist. The 66.3% save has
  Jewel crew 1 = 8 and crew 2 = 10, which matches the hacker and driver of a Jewel Store run in the usual crew order
  (unverified).

## 16. Heist crew (2026-10-14)

* **Location.** Script data struct `0x412DDFE6/0xA3382ECE` holds `Crew_Unlocked_Bitset`, `Crew_Used_Bitset`,
  `Crew_Dead_Bitset` and `Crew_Dialogue_Bitset` (names from the reference editor's layout map). Each is one bit per
  crew member, using bits 1–13.
* **Values across the saves:**

  | Save | Unlocked | Used | Dead |
  |---|---|---|---|
  | 1.6% | 978 | 0 | 0 |
  | 66.3% | 14290 | 50 | 0 |
  | 98.8% | 16382 | 6450 | 32 |
  | 100% (2020) | 16382 | 6450 | 32 |
  | 100% sample | 16382 | 1212746 | 0 |

  16382 = bits 1–13, which every finished save has. 1212746 sets bits above 13 too (unknown meaning).
* **Editor actions** (Missions tab, Heist crew card):
  * "Unlock every crew member" sets bits 1–13 of Unlocked;
  * "Bring dead crew back" clears bits 1–13 of Dead.
* **Unresolved:** which bit is which crew member. The `HCS_*_CREW<n>_CHOICE` stat values (0–13) don't match the Used
  bits one-to-one, so the editor shows counts, not names.
* **Not verified in-game yet.**

## 17. Safehouse garages and giving vehicles (2026-10-14)

* **Location.** The game's own garage save is root struct `0x72194FCC`. Inside it, `0x58DAF7C3` is 15 garages, each
  a struct of (id `0x0F4710A0`, array `0x26533C8A` of 2 parked vehicles). Six garages are used:

  | Garage id | Safehouse |
  |---|---|
  | 360562957 | Michael's house |
  | 2175093583 | Trevor's trailer |
  | 3774828611 | Trevor's Vespucci flat |
  | 1066626361 | Vanilla Unicorn |
  | 4019785634 | Franklin's aunt's house |
  | 2393745202 | Franklin's Vinewood Hills house |

  The parking positions come from the user's saves. The second space of the Vespucci flat is unknown, so it can't be
  filled yet.
* **A parked vehicle** has these fields:
  * position (`0xDC34682B`, `0x5D62EA92`, `0x48A1C110`);
  * direction as two signed bytes ×100 (`0x084DB6A8`, `0xE86E76CE`);
  * model hash (`0x55F7497A`);
  * two health values of 1000 (`0x4C2AA15F`, `0xA8F45122`);
  * in-use flags (`0x6531DDC5`, `0x23D3A43F`, count `0x168E3FEC` = 1);
  * the look struct `0xCFF74A31`: primary `0x143A8221`, secondary `0x88466A37`, pearl `0x7EEF5789`, wheel colour
    `0x6BE0316B`, a `mods` map, and other values copied from a stock Z-Type entry.
* **Empty space:** all zero / false, except `0xFB0454C0` = -1, look `0x8EA846D0` = 255 and `0x87A334CC` = 65535.
* **Hangars, helipads and marinas** (script data `0x943A0A56/0xC3630453`, 23 vehicle-generator slots) aren't
  edited yet.
* **In-game test file.** `GtaStuff/garage-test/SGTA50003` is the 98.8% save with a red Adder in Michael's garage.
  Not verified in-game.

## 18. Hangars, helipads, boat docks and businesses (2026-10-14)

* **Vehicle generators.** Script data `0x943A0A56/0xC3630453` has 23 vehicle-setup slots (the reference editor calls
  them "VehicleList1").
  * Slots 0–8 are hangar, boat dock and helipad for Michael, Franklin and Trevor, in that order.
  * Evidence: slot 2 holds the Cuban 800 and slot 8 the TPI Frogger in the story saves. In the 100% save the slots
    hold a Luxor Deluxe, Lazer, Rhino, Toro, Dinghy, Kraken, Swift Deluxe, Annihilator and Buzzard.
  * Slot fields: model `0x102CF7E5` (int), primary `0xC47A9511`, secondary `0xF2CB71B2`, pearl `0x5C531690`, wheel
    `0x8EBD7B64`, tyre smoke RGB (`0x5953387A`, `0x419A88F1`, `0xB04B6651`), livery `0x2D1E0949` (-1), flags
    `0x58DC0971`.
  * A new vehicle copies the game-made Cuban 800 entry: wheel 156, smoke 255, livery -1, flags 4194304.
  * Slots 9–22 are not edited yet. They look like 4-car garages plus two "last vehicle" slots.
* **Businesses.** Script data `0x38301D26/0xD6B3AB33` holds 16 structs, named `PROPERTY_<NAME>` (JOAAT, verified for
  15 of them; the 16th is the Vanilla Unicorn, which the story gives Trevor). Each struct has:
  * owner `0xBE27B286`: 0 Michael, 1 Franklin, 2 Trevor, 145 not owned;
  * state `0xAD21F043`: 5 or 7 when owned, 0 when not;
  * purchase time `0x9EECB4A5`, which the editor sets to the struct's top-level `0x9EECB4A5`;
  * `0xFA8EFCF2`, which looks like uncollected income.
* **"Usual owner"** comes from the user's 100% save, where every business was bought in game.
* **In-game test file.** `GtaStuff/property-test/SGTA50003` is the 98.8% save with a Hydra in Trevor's hangar, a
  Buzzard on his helipad, and all 15 businesses owned. Not verified in-game.

## 19. Field names from decompiled scripts; collectibles, unlocks and flags (2026-10-14)

* **Names.** About 6,200 save field names were recovered from publicly available decompiled game scripts
  (YimMenu/GTA-V-Decompiled-Scripts, build 2628). Most come from the name literals of `REGISTER_*_TO_SAVE`,
  `START_SAVE_STRUCT_WITH_SIZE` and `START_SAVE_ARRAY_WITH_SIZE`, plus indexed names built with `StringIntConCat`.
  Only names whose exact-case JOAAT matched a field in a real save were kept. Script save data (`g_savedGlobals`)
  now reads by name; about two thirds of its fields are named.
* **Packed stat bools.** `_GET_PACKED_STAT_BOOL(i)` for story mode reads bit `(i - 705) % 64` of `SP_PSTAT_BOOL<(i - 705) / 64>`,
  lowest bit first. This was the only layout that matched the collectible counters in all five sample saves.
* **Collectibles.** Item indexes come from the collectible scripts; every count was checked against the game's own
  counter stat and checklist entry in the 1.6%, 66.3%, 98.8% and 100% saves.

  | Kind | Stored as | Counter stat | Checklist |
  | --- | --- | --- | --- |
  | Letter scraps | packed bools 705–754 | `NUM_HIDDEN_PACKAGES_0` | `SC_CO_DLS1` (50) |
  | Spaceship parts | packed bools 755–804 | `NUM_HIDDEN_PACKAGES_1` | `SC_CO_UFOP1` (50) |
  | Epsilon tracts | packed bools 805–814 | `NUM_HIDDEN_PACKAGES_2` | – |
  | Nuclear waste | packed bools 815–844 | `NUM_HIDDEN_PACKAGES_3` | `SC_MSC_SON` (30) |
  | Submarine pieces | packed bools 845–874 | `NUM_HIDDEN_PACKAGES_4` | `SC_MSC_DIV` (30) |
  | Bridges / knife flights | bits 0–69 of `0xF144B45F`, `BRIDGES_FLOWN_UNDER_FLAGS_2`, `_3` | `SP_UNDER_THE_BRIDGE_COUNT` / `SP_KNIFE_FLIGHTS_COUNT` | `SC_MSC_BRG` (25) / `SC_MSC_KFT` (8) |
  | For-sale signs | bits 0–14 of `FOR_SALE_SIGN_DESTROYED_FLAGS` | – | – |
  | Monkey mosaics | 50 bits of `PHOTOGRAPHY_MONKEY_FLAGS_1`, `_2` | `NUM_HIDDEN_PACKAGES_6` | – |
  | Peyote | `PEYOTE_PICKUP_OF_TYPE_FOUND` (15 + 6 + 6 bits) | `NUM_HIDDEN_PACKAGES_5` | – |
  | Parachute jumps | bits 0–12 of `BASEJUMP_SAVED_STRUCT/0x6EC087BF` | – | `SC_MG_BJUM` (13) |
  | Stunt jumps | count only, `STUNT_JUMPS_iStuntJumpsCompleted` | – | `SC_MSC_STJ` (25) |

  * Knife flights are bits 33–36 and 50–60 (`flyunderbridges`); bits 2, 8, 37, 65 and 66 are unused, which leaves
    50 bridges. The game recounts both counters from these bits when a save loads (`standard_global_reg`).
  * The letter-scrap and spaceship script bitsets (`GLOBAL_SCRAP_DATA_*`) are -1 already in a 66% save, so they are
    not the found state and are left alone.
  * Positions (`Data/CollectibleLocations.txt`) are the pickup coordinates in `letterscraps`, `spaceshipparts`,
    `epsilontract`, `ambient_sonar`, `ambient_diving` and `flyunderbridges`.
* **Weapon unlocks.** The gun shop checks bit `n % 32` of `SP<c>_WEAP_UNLOCK_<n / 32>`, where `n` is the weapon's number
  in `gunclub_shop`'s weapon table (`func_57`). A 100% save still has a few weapons locked (hammer, bat, crowbar, golf club,
  Pistol .50, Assault SMG, Bullpup Shotgun, firework and homing launchers).
* **Clothing.** `COMPONENTS_ARRAY/SP<c>/<group>` has `AVAILABLE` (what the shops offer, nearly all set from the start),
  `ACQUIRED` (owned) and `NEW`. "Own everything" sets `ACQUIRED |= AVAILABLE`.
* **Tattoos.** `TATTOOS_ARRAY/.../UNLOCKED` is the same in the 1.6% and 100% saves, so it is not a progress unlock;
  not edited.
* **Director Mode.** The four `BitsetCharacter*Unlock` values in a 100% save are 268402687, 8191, 268402687 and
  2097151 (27, 13, 27 and 21 actors).
* **Flight School.** `0x93E8FC1E/structPilotSchool_p1` is Franklin's (the other two are zero in every save). Each
  `PSC_*` lesson has `eMedal` (0 none, 1 bronze, 2 silver, 3 gold) and `HasPassedLesson`. The lessons map onto
  checklist `SC_MGPS1`–`SC_MGPS12`.
* **Game flags.** `FLOW_STRUCT/MF_CONTROLS_STRUCT/MF_CONTROL_FLAGIDS` holds 137 named `FLAG_*` ints.
* **Not done.** Wildlife photography (`WILDLIFE_PHOTOGRAPHY_FLAGS` has more bits than the 20 animals; the mapping is unknown),
  vehicle upgrade unlocks, heist breakdowns, stocks, phone, booty calls and activity records.
* None of these edits has been verified in-game.
