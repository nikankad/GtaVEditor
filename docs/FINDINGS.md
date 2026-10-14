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
checksum is recomputed on save. The app flow is game picker → save picker → editor with friendly sections (Characters:
cash + skills; Weapons: give/remove/ammo per character; Story: read-only progress). The raw **Save data**, **Compare**
and **File** views live in an *Advanced* window. None of the edits are verified in-game yet.

## 8. Architecture decision

Saves are not interchangeable between games, so the app is **one module per game** (`IGameModule` in `SaveEditor.Core`).
The flow is identical for every game — read → decrypt → parse → edit → recompute checksums → encrypt → back up → write —
only the module internals differ.

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
Changing the mission records alone would not move the story: the strands' positions drive it, and a position only works
with its matching hash, which can't be computed without the game's flow command list. These four saves give known-good
strand states at four points.

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
  `…_MissionGolds`, …). These match the strands (with hash), flags, ints, bitsets and mission records found above, so
  the editor writes per-mission strand positions and hashes taken from its database. They are not computed from the save.
