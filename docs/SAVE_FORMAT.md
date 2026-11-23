# GodSim save format (format version 1, schema version 2)

Owner: Persistence/Format (`Assets/GodSim/Core/Persistence/Format/`). API: `GodSim.Core.Persistence.SaveSerializer`
(`Serialize`, `Deserialize`, `ReadHeader`), `SaveOptions`, `SaveHeader`, `SaveCorruptException`,
`StateValidator`, `SaveMigrator`.

A save file is `header | tag | payload`. All integers are **little-endian**. Floats are IEEE-754 binary32 (bit-exact).

## 1. Container layout

`L` = byte length of the game version string (0..64).

| Offset | Size | Type | Field | Rules on load |
|---:|---:|---|---|---|
| 0 | 4 | bytes | magic `G S A V` | must match |
| 4 | 2 | u16 | format version = 1 | must equal 1 (container layout) |
| 6 | 2 | u16 | flags: bit0 = Compressed (GZip), bit1 = Signed (HMAC) | unknown bits rejected |
| 8 | 4 | i32 | schema version (`WorldState.SchemaVersion`) | must be supported by `SaveMigrator` |
| 12 | 8 | i64 | created (Unix seconds, supplied by the caller) | informational |
| 20 | 1 | u8 | `L` | <= 64 |
| 21 | L | UTF-8 | game version | strict UTF-8 |
| 21+L | 8 | u64 | seed | must equal payload seed |
| 29+L | 8 | i64 | tick | >= 0, must equal payload tick |
| 37+L | 4 | i32 | living population | >= 0, must equal the parsed state |
| 41+L | 4 | i32 | payload length P (stored bytes) | `45 + L + 32 + P == file length` |
| 45+L | 32 | bytes | **tag** | see section 2 |
| 77+L | P | bytes | payload (raw, or one GZip member when Compressed) | section 3 |

`ReadHeader` parses only this table, with no hashing and no decompression, so load menus can call it. Treat its values as
untrusted until `Deserialize` succeeds.

## 2. Integrity model

* **Tag** = `HMAC-SHA256(key, header || payload)` when Signed, otherwise `SHA-256(header || payload)`. It covers the
  stored (compressed) payload, so it is verified **before** decompression. The comparison runs in constant time
  (it XOR-accumulates over all 32 bytes, with `NoInlining|NoOptimization`).
* A Signed file needs a key: without one it is rejected. With `RequireSignature`, unsigned files are rejected. Without
  that option, a loader that holds a key still accepts unsigned files. Anyone can then strip the Signed flag and
  recompute a plain SHA-256 (a *downgrade*), so set `RequireSignature` wherever signatures matter.
* **What this buys:** reliable detection of disk/transfer corruption and truncation (SHA-256). With a key, it is
  tamper-evident against anyone who does not have the key, for example a save shared online, synced through the cloud
  or uploaded to a server that holds the key.
* **What it is not: anti-cheat.** In a single-player build the HMAC key has to live on the player's machine, inside
  the binary or a local key file. Whoever controls that machine can extract the key with a debugger or disassembler,
  edit the save and re-sign it. They can also skip the file and edit memory directly. Local HMAC only raises the
  effort from a hex editor to reverse engineering. Real anti-cheat needs a key the player never holds: server-side
  signing or validation, or server-authoritative simulation. Whatever the integrity model, the loader assumes the file
  is hostile. Every limit below applies to signed files too.

## 3. Payload

The payload is a fixed sequence of sections. Each starts with a 4-byte ASCII tag, which turns misalignment into a
clear error. Encodings:

* `str`: i32 byte length (`-1` = null, otherwise 0..1536), then strict UTF-8. The decoded string is at most 512 UTF-16 chars.
* `bool`: one byte, 0 or 1 (anything else is rejected). Enums: one byte, validated against the enum's defined values.
  Flags enums: u16, validated against the mask of defined bits.
* `list<T>`: i32 count, then the elements. `arr<T>` (enum-indexed): i32 length, then the elements. A length **greater**
  than the current `Counts.*` is rejected. A shorter one is read as-is and the remaining slots keep their constructor
  defaults (so enums can grow without a schema bump).
* `mem` (blackboard): i32 count (<= 10,000), then `(str key, f32 value)` pairs, sorted by ordinal key. Keys must be
  strictly increasing, which makes the encoding canonical and rules out duplicates.
* `vec`: i32 x, i32 y.

| Tag | Content (in order) |
|---|---|
| `WRLD` | u64 Seed, i64 Tick |
| `MAP_` | i32 W, i32 H, i32 tiles (= W*H, 16 <= W,H <= 1024); then tiles x f32 Elevation, Moisture, Temperature, Fertility; tiles x u8 Biome; tiles x u16 TileFlags; tiles x f32 Resource; tiles x i32 Owner; tiles x i32 BuildingAt; f32 SeaLevel; i32 TerrainVersion |
| `RNG_` | u64 State, u64 Increment (must be odd; restored with `Rng.FromState`) |
| `IDS_` | i32 NextPersonId, NextBuildingId, NextSettlementId, NextCivilizationId, NextArmyId, NextWarId |
| `PEOP` | list (<= 2,000,000) of Person: Id, str Name, Race, Sex, i64 BirthTick, f32 AgeYears, Stage, bool IsAlive, i64 DeathTick, DeathCause, f32 Health, Happiness, Satiation, Faith, u16 Traits, f32 Skill, Experience, Job, i32 WorkplaceId, HomeId, SettlementId, CivilizationId, ArmyId, MotherId, FatherId, SpouseId, ChildrenCount, Generation, vec Position |
| `BLDG` | list (<= 2,000,000) of Building: Id, Type, SettlementId, CivilizationId, vec Origin, i32 Width, Height, u8 Rotation, i32 Variant, bool IsComplete, IsDestroyed, f32 ConstructionProgress, bool CostPaid, f32 Condition, i32 Level, bool IsOnFire, i32 HousingCapacity, list<i32> Residents, list<i32> Workers, f32 Efficiency, StyleRace, StyleAge, i64 PlacedTick, CompletedTick |
| `SETL` | list (<= 2,000,000) of Settlement: Id, str Name, CivilizationId, Race, vec Center, i64 FoundedTick, Tier, bool IsCapital, IsAlive, i32 TownCenterId, ParentSettlementId, TerritoryRadius, list BuildingIds, list ResidentIds, Stockpile {f32 Capacity, arr amounts (Get/SetRaw), arr AvgIncome, arr AvgExpense}, list BuildOrder {Type, f32 Priority, i64 RequestedTick, str Reason, i32 Attempts, bool HasPreferredSite, vec PreferredSite}, arr ServiceOutput, arr ServiceCoverage, arr JobPriority, i32 DesiredBuilders, Homeless, Unemployed, Soldiers, f32 Happiness, Health, Unrest, Faith, DefenseStrength, i64 LastStarvationTick, mem Memory, schema >= 2: i32 n then n x (f32 Stockpile.PeriodProduced, f32 Stockpile.PeriodConsumed) |
| `CIVS` | list of Civilization: Id, str Name, Race, u32 Color, Age, i64 AgeEnteredTick, FoundedTick, bool IsAlive, i64 CollapsedTick, i32 CapitalId, list SettlementIds, i32 LeaderId, f32 ResearchPool, TotalResearch, i32 CurrentResearchTechId, list KnownTechs (strictly increasing), arr<bool> UnlockedBuildings, Modifiers {arr Production, arr Service, f32 Research, Growth, BuildSpeed, Faith, Military, Storage, Trade, MoveSpeed, HealthBonus, HappinessBonus, LifespanBonusYears}, f32 Devotion, GodFavor, i32 Population, f32 MilitaryPower, mem Memory |
| `DIPL` | list Relation (ascending `DiplomacyState.Key`, CivA < CivB): CivA, CivB, f32 Opinion, Stance, bool HasContact, TradeAgreement, i64 TruceUntilTick, LastContactTick, i32 WarId; list War: Id, AttackerId, DefenderId, i64 StartTick, EndTick, bool IsActive, f32 WarScore, str CasusBelli, i32 Battles, AttackerLosses, DefenderLosses, SettlementsCaptured; list Army: Id, CivilizationId, HomeSettlementId, TargetSettlementId, WarId, State, vec Position, Destination, f32 MoveProgress, Strength, Morale, bool IsAlive, list SoldierIds, list<vec> Path, i32 PathIndex, i64 CreatedTick |
| `GOD_` | f32 Mana, MaxMana, ManaPerTick, Alignment, arr<i64> CooldownUntil, i32 PowersCast, i64 LastCastTick |
| `WTHR` | Season, f32 Rainfall, TemperatureOffset, FoodYieldMultiplier, i32 DroughtTicksRemaining, RainTicksRemaining, f32 StormIntensity |
| `CHRN` | list (<= `Chronicle.MaxEntries` = 2000) of entry: i64 Tick, Category, str Text, i32 CivilizationId, SettlementId, vec Position, i32 Importance |
| `STAT` | i64 TotalBirths, TotalDeaths, BuildingsCompleted, BuildingsDestroyed, SettlementsFounded, WarsDeclared, BattlesFought, GodPowersCast, i32 PeakPopulation, arr<i64> DeathsByCause |
| `MEMW` | mem `WorldState.Memory` |
| `END_` | end marker; trailing bytes are rejected |

Lists without a stated limit allow at most 1,000,000 entries. Entity stores are written in `Items` order and re-added in
that order, so iteration order (and therefore determinism) survives a reload. When `Compressed`, the payload is exactly
one GZip member. Its trailer (CRC-32 and ISIZE of the plaintext) is verified explicitly against the decompressed bytes,
independent of how the platform inflater behaves.

**Not persisted (transient by contract):** `Settlement.AiLog`, `WorldState.Pending*Removals` (`Serialize` throws
`InvalidOperationException` unless they are empty, so save between ticks). Since schema 2 the `Stockpile`'s
current-period flow counters (`PeriodProduced`/`PeriodConsumed`, not yet folded by `RollStatistics`) are persisted, so the
next roll after a load matches an uninterrupted run; schema-1 files load them as 0. The averages (`AvgIncome`,
`AvgExpense`) are persisted too.

**Writer guarantees:** every file `Serialize` produces is accepted by `Deserialize` with the same options. Strings over
512 chars, counts over the limits and an output over `MaxCompressedBytes` or `MaxUncompressedBytes` all throw
`InvalidOperationException`. A non-finite float (forbidden in state by contract rule 5) is written as `0` so a save can
never become unloadable. Only the newest `Chronicle.MaxEntries` entries are written. Output is canonical: re-saving a
loaded world produces identical bytes.

## 4. Load pipeline (`Deserialize`)

1. `data.Length > MaxCompressedBytes` → reject (before any other work).
2. Parse the header: magic, format version, flags, `L`, and the exact total length.
3. Check the schema version with `SaveMigrator.IsSupported`.
4. Apply the signature policy (Signed needs a key; `RequireSignature` rejects unsigned files), then verify the tag in
   constant time.
5. Uncompressed: `P <= MaxUncompressedBytes`. Compressed: inflate through a fuse stream that throws once the output
   passes `MaxUncompressedBytes` (protects against decompression bombs).
6. Parse with `BoundedReader`. Every count is checked before anything is allocated (nothing is pre-sized from a declared
   count except the map, which is bounded by 1024x1024). Every enum, flag mask, bool and float is validated (NaN/Inf
   are rejected), and every string is checked for length and strict UTF-8. Duplicate ids are rejected.
7. Cross-check the header against the payload (seed, tick, population). Verify the GZip trailer.
8. `SaveMigrator.Upgrade(state, db, fileSchema)`.
9. `StateValidator.ValidateAndRepair`. Fatal problems → reject. Benign problems → repaired.

Every failure surfaces as `SaveCorruptException`. Any other exception (EndOfStream, InvalidData, Decoder, Overflow,
OutOfMemory, ...) is wrapped in one. `db` may be null, which skips the content checks (unknown tech ids).

## 5. Post-load validation (`StateValidator`)

**Fatal:** non-finite floats; IdAllocator counters below 1; entity ids outside `[1, Next*Id)`; duplicate war ids;
building footprints outside 1..16 per side or out of bounds; overlapping live footprints; a settlement, war or army whose
civilization does not exist; relations that are not normalized or name an unknown civ; a living person whose settlement
is missing or does not list them, or who is listed by the wrong settlement.

**Repaired (the same way WorldOps would clean up):** dangling or non-reciprocal `HomeId`/`WorkplaceId` (cleared; the
job is reset to None) and `ArmyId`/`SpouseId`; stale or duplicate entries in `Residents`, `Workers`, `ResidentIds`,
`BuildingIds`, `SettlementIds` and `SoldierIds`; unlisted live buildings and settlements are added to their owner's list;
`map.BuildingAt` is rebuilt from footprints; tiles owned by missing settlements are released; invalid capital, leader,
town center, research target, parent, war, home and target ids are cleared; unknown techs are forgotten (only when `db`
is given); out-of-bounds positions are clamped; an army path that leaves the map is cleared; rotation is masked to 0..3;
chronicle importance is clamped to 0..3.

`Validate` reports the same findings without changing anything ("fatal: ..." / "repairable: ..."; empty = consistent).
Work is linear in the state's size. Messages are capped at 100 per kind, and the overlap scan stops at the first conflict.

## 6. Versioning and migration policy

* **Format version** (container layout, section 1): changes only when the header, tag or compression framing changes.
  A reader rejects any format version it does not know.
* **Schema version** (`WorldState.SchemaVersion`, the payload content): bump it whenever a persistent field is added,
  removed or reinterpreted. To add a field in schema N+1: (1) write it in the payload codec, appended at the end of its
  entity record; (2) read it only when `schema >= N+1`, so older files keep the constructor default; (3) add exactly one
  step to `SaveMigrator.Steps` that derives or fixes data for N → N+1 (it may be a no-op); (4) add a round-trip fixture
  for the old version. `SaveMigrator.StepCount` must equal `Current - MinSupported`, and a test enforces this.
* Old schemas stay readable until `MinSupportedSchemaVersion` is raised on purpose. Newer schemas are always rejected
  ("made with a newer version").
* Enum growth (appending values, as the contract requires) does not need a schema bump. Enum-indexed arrays are
  length-prefixed and padded with defaults.

## 7. Limits (defaults)

| Limit | Value |
|---|---|
| File size (`MaxCompressedBytes`) | 64 MiB |
| Decompressed payload (`MaxUncompressedBytes`) | 512 MiB |
| Map | 16..1024 per side, <= 1,048,576 tiles |
| People / buildings / settlements | <= 2,000,000 each |
| Other lists (civs, wars, armies, relations, member lists, paths, techs, build queue) | <= 1,000,000 |
| Blackboard entries | <= 10,000 per dictionary |
| Strings | <= 512 UTF-16 chars (<= 1536 UTF-8 bytes) |
| Game version | <= 64 UTF-8 bytes |
| Chronicle | <= 2000 entries |
| Building footprint (validator) | 1..16 per side |

Memory note: in the worst case a hostile file within these limits makes the loader allocate about 2-3x
`MaxUncompressedBytes` (object overhead plus list growth). On memory-constrained platforms, lower
`MaxUncompressedBytes`. A real 1024x1024 world with 40k people decompresses to roughly 40 MB.
