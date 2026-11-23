# ADR-0005: Save format and integrity

- **Status:** Accepted (implemented in `Core/Persistence/`; one open item: tech references, see Follow-ups)
- **Date:** 2026-11-19
- **Deciders:** lead, persistence owner, security team
- **Related:** [ENGINEERING_CONTRACT §6](../ENGINEERING_CONTRACT.md), [ARCHITECTURE §11](../ARCHITECTURE.md#11-persistence-and-integrity), [SAVE_FORMAT.md](../SAVE_FORMAT.md), [SECURITY.md](../SECURITY.md), `Core/Persistence/Format/SaveSerializer.cs`, `Core/Persistence/Storage/SaveFileStore.cs`

## Context

Worlds run for hundreds of years with tens of thousands of entities, and players share save files.
Save files are **untrusted input**: they may be corrupted, truncated, edited by hand or crafted.
Constraints:

- netstandard2.1 only. `System.Text.Json` is not allowed. `System.IO`, `System.IO.Compression` and
  `System.Security.Cryptography` are.
- Loading must never crash the game, allocate unbounded memory, or produce a half-valid world (NaN
  fields, dangling ids).
- A loaded world must continue exactly as if it had never been saved.
- Core never reads the clock.
- The format must evolve. Today `WorldState.SchemaVersion = 1`.

## Decision

We will use a **custom, versioned binary format**: `header | 32-byte tag | payload`.

1. **Header** (`SaveHeader`, fixed and uncompressed): magic, format and schema version, flags
   (`SaveFormatFlags`), `GameVersion`, and `CreatedUnixSeconds`. The caller supplies the timestamp
   because Core never reads the clock. `SaveSerializer.ReadHeader` lets load menus show slot info
   without parsing the payload, but every header value is untrusted until `Deserialize` succeeds.
2. **Payload** (`PayloadCodec`) holds the whole `WorldState` graph:
   - map arrays, written linearly;
   - entity stores, in store order;
   - `IdAllocator` and `Rng` state (`State`, `Increment`);
   - diplomacy (relations, wars, armies), `GodState`, `WeatherState`, `Chronicle`, `WorldStats`;
   - all three `Memory` blackboards.

   It is GZip-compressed by default (`SaveOptions.Compress`).
3. **Tag:** HMAC-SHA256 over `header || payload` when `SaveOptions.HmacKey` is set (signed).
   Otherwise SHA-256 (unsigned checksum). `RequireSignature` rejects unsigned files. Keys come from
   an `IKeyProvider`: `StaticKeyProvider`, `DerivedKeyProvider` (PBKDF2-HMAC-SHA256), or
   `InstallKeyFile` (a random 32-byte per-installation secret, created atomically).
4. **Hardened load** (`SaveSerializer.Deserialize(bytes, db, options)`):
   1. Size caps are checked before any work: `MaxCompressedBytes` 64 MiB, `MaxUncompressedBytes`
      512 MiB.
   2. The tag is verified in constant time **before** decompression.
   3. Decompression stops at the uncompressed cap.
   4. Every count, enum and float is validated (`BoundedReader`).
   5. `StateValidator` checks referential integrity. Fatal problems reject the file. Repairable
      ones, such as an unknown research target, are fixed and reported.
   6. `SaveMigrator` upgrades the state one schema step at a time.

   **Only `SaveCorruptException` escapes.**
5. **Storage** (`SaveFileStore`): one file per slot, `<Root>/<slot>.gsav`. Slot names match
   `^[A-Za-z0-9_-]{1,48}$`, Windows reserved names excluded. Writes are atomic: a temp file, then
   replace.
6. **Not saved:** the `GameDatabase` (rebuilt from code), `Settlement.AiLog`, pending removals,
   profiles, and `WorldMap.TerrainVersion`.
7. **Stable identity:** enums are stored as numbers and are append-only.
8. **State hash:** `StateHasher.Compute` applies FNV-1a 64 to a canonical encoding of the same
   content, in 14 sections, with `Memory` sorted by ordinal key. Save, then load, then hash must
   equal the hash before saving.
9. **Replays:** `ReplayLog` records accepted commands (via `CommandQueue.OnAccepted`), a
   `ReplayHeader` and optional state-hash checkpoints.

## Consequences

### Positive
- Compact and fast, with no reflection and no dependence on Unity serialization.
- Robust against corrupted and malicious files: bounded memory, one exception type, no partial
  worlds.
- `tests/GodSim.Tests/Integration/SaveLoadContinuityTests.cs` covers the main properties: a mid-run
  save/load matches a straight run, signed round trips work, tampering and wrong keys are rejected,
  and garbage or truncated data is rejected.

### Negative / costs
- A hand-written codec must be updated for every contract field. The round-trip hash test is the
  safety net.
- **Unsigned saves only detect corruption.** A per-install HMAC key detects edits and files copied
  between installations. It does not stop a determined local user who reads `install.key`. That is
  acceptable for a single-player game and must not be relied on for competitive features.
- `Counts.*` are part of the format. Appending a `GodPowerType` changes the length of
  `GodState.CooldownUntil`, so the codec must write lengths and tolerate shorter arrays from older
  files.
- **State the contract hides cannot be saved.** `Stockpile`'s private per-period flow counters are
  not reachable, yet they are non-zero between ticks. See ARCHITECTURE §8.

### Follow-ups
- **Tech references.** `PayloadCodec` stores `KnownTechs` and `CurrentResearchTechId` as
  **registration indices**. Inserting a tech anywhere except the end of `TechCatalog` silently
  remaps techs in existing saves. Either store `TechDefinition.Key`, or enforce append-only tech
  registration with a test.
- Stockpile period counters: add contract accessors, or roll statistics last in the tick.
- A fuzz test: random byte flips and truncations must always raise `SaveCorruptException`.

## Alternatives considered

### JSON (`JsonUtility` / Newtonsoft)
`System.Text.Json` is not allowed, `JsonUtility` is Unity-only, and Newtonsoft is an extra
dependency. All of them are slow and large for map arrays. JSON stays for the **headless
snapshot** consumed by `tools/WebViewer`, which is not a save format.

### `BinaryFormatter`
Insecure by design (arbitrary type instantiation) and obsolete. Rejected.

### Protobuf / MessagePack
Extra dependencies and code generation that do not fit the netstandard2.1 whitelist.
