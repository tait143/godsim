# GodSim Coding Standards

These standards apply to all C# in `Assets/GodSim/`, `tests/` and `tools/`. They expand on the
hard rules in [`ENGINEERING_CONTRACT.md`](ENGINEERING_CONTRACT.md) §3, which take precedence.
Rules marked **MUST** block review. **SHOULD** rules need a reason in the PR when broken.

Contents: [Language and platform](#1-language-and-platform) · [Determinism](#2-determinism) ·
[Allocation and performance](#3-allocation-and-performance) · [Naming and layout](#4-naming-and-layout) ·
[XML documentation](#5-xml-documentation) · [Error handling](#6-error-handling-and-untrusted-data) ·
[State mutation](#7-state-mutation) · [Tests](#8-tests) · [Unity-side rules](#9-unity-side-rules) ·
[Formatting](#10-formatting)

## 1. Language and platform

Core targets **C# 9 on `netstandard2.1`** (what Unity 6 compiles).

| MUST NOT use (Core and tests) | Use instead |
|---|---|
| File-scoped namespaces (`namespace X;`) | Block namespaces `namespace X { ... }` |
| `record`, `record struct` | `sealed class` / `struct` with explicit members |
| `global using`, implicit usings | Explicit `using` per file |
| `init` accessors, `required` members | Constructors or public fields |
| Raw string literals (`"""`) | Regular or verbatim strings |
| `System.Text.Json`, Newtonsoft in Core | Hand-written binary (Persistence) or `StringBuilder` |
| Nullable reference annotations (`string?`); `Nullable` is **disabled** | Explicit null checks |
| `UnityEngine.*` in Core | `MathUtil`, `Vec2i`, `ISimLogger`, `Rng` |
| APIs newer than netstandard2.1: `ArgumentNullException.ThrowIfNull`, `Random.Shared`, `List.EnsureCapacity`, `CollectionsMarshal`, `Enumerable.Chunk`/`DistinctBy`/`MaxBy`, `string.ReplaceLineEndings`, `Math.Clamp` on newer overloads | `Guard.*`, `MathUtil.*`, hand-written loops |
| Pattern features from C# 10+ (extended property patterns `{ A.B: 1 }`, list patterns) | C# 9 patterns (`is`, `and`/`or`/`not`, relational) |

Allowed namespaces in Core: `System`, `System.Collections.Generic`, `System.Text`, `System.IO`,
`System.Security.Cryptography`, `System.IO.Compression`. (`SimulationEngine` uses
`System.Diagnostics.Stopwatch` for profiling. Modules MUST NOT.)

> **What catches what.**
>
> | Check | C# 10+ syntax | net5+ APIs | Determinism APIs |
> |---|---|---|---|
> | `build/checks/*` (net8.0, `LangVersion 9.0`) | caught | **not caught** | not caught |
> | `dotnet test` from the root (`build/GodSim.Core.csproj`, `netstandard2.1`) | caught | caught | analyzer CA5394 (`System.Random`) only |
> | `scripts/check-unity-compat.sh` | caught | not caught | caught |
>
> Run all three before merging (`scripts/run-tests.sh` does). A reviewed compat exception is written
> on its own line as `// godsim-compat: allow <RULE> <reason>`.

## 2. Determinism

All simulation code (anything reachable from `ISimSystem.Tick`/`Initialize`, `WorldOps`, catalogs,
world generation) MUST follow these rules. See [ADR-0002](adr/0002-deterministic-simulation.md).

1. **Randomness only from `ctx.Rng`** (or an `Rng` passed in, such as world generation's). MUST NOT
   use `System.Random`, `UnityEngine.Random`, `Guid.NewGuid`.
2. **No clocks:** MUST NOT read `DateTime.Now/UtcNow`, `Environment.TickCount`, `Stopwatch`, or
   frame time.
3. **No `string.GetHashCode()`**, and no `GetHashCode()` of reference types for decisions. Use
   `Hashing.Fnv1a64(string)`, `Hashing.Mix64`, `Hashing.Combine`.
4. **Iterate `EntityStore.Items` by index** (`for (int i = 0; i < items.Count; i++)`). MUST NOT
   make decisions from `Dictionary`/`HashSet` enumeration order. Use them for lookup only, or sort
   the keys first.
5. **Break ties deterministically** (lowest id, lowest enum value). Use stable sorts: `List.Sort` is
   unstable, so include the id as a final comparison key.
6. **No threads, `Task`, `async`, `Parallel`, `ThreadStatic`** in Core.
7. **Rng call discipline:** draw the same number of values regardless of presentation, logging level
   or debug flags. MUST NOT call `Rng` inside `Debug`-only branches or log formatting.
8. **Scale by `ctx.DeltaTicks`.** Per-tick rates are multiplied by it. Per-year probabilities go
   through `Rng.PerYearToPerTicks(p, ctx.DeltaTicks)`.
9. **No static mutable state** in systems or helpers (static caches of *immutable* data are fine).
   Static state survives between worlds in the same process (tests, editor domain reloads).
10. **Arrays sized by `Counts.*`**, never by literal enum sizes.
11. Floats: prefer `+ - * /` and `Math.Sqrt` in hot gameplay math. Avoid introducing new
    `Math.Pow/Exp/Log/Sin/Cos` in per-entity probability code where a polynomial or table works
    (cross-runtime parity, see ADR-0002).

## 3. Allocation and performance

Budget: **< 3 ms per tick average for 5,000 people across all systems** (contract rule 6).

| Rule | Level |
|---|---|
| No LINQ in `Tick` or anything it calls per entity | MUST |
| No lambdas or closures capturing locals in per-tick paths (they allocate). Pass static methods or cache delegates in fields. | MUST |
| No `new List<>`/arrays/`ToArray()`/`ToList()` per tick. Reuse buffers stored in fields and `Clear()` them. | MUST |
| No string concatenation or formatting per entity per tick. Cache parameterized keys (see `RelationsKeyCache`). Build log/chronicle strings only when the event actually happens. | MUST |
| No `foreach` over `IEnumerable<T>` interfaces in hot loops (boxing of enumerators). Index `IReadOnlyList<T>`. | SHOULD |
| Heavy work runs on `TickInterval` 4 to 30 (or more) and/or sweeps a window per run (`ResourceRegrowthSystem`) | SHOULD |
| Bounded algorithms: path queries use `maxExpansions`, area effects are clamped to radius and map bounds, retries are capped | MUST |
| Skip dead entities early (`if (!p.IsAlive) continue;`) | MUST |
| Prefer struct-of-arrays and `WorldMap` arrays over per-tile objects | SHOULD |
| `GridPathfinder` instances are reused per map size, never created per query | SHOULD |

Measure with `SimulationEngine.Profiles` (see [CONTRIBUTING › Profiling](CONTRIBUTING.md#profiling)).

## 4. Naming and layout

| Element | Convention | Example |
|---|---|---|
| Namespace | `GodSim.Core.<Module>`. Sub-area folders share the module namespace. | `GodSim.Core.Economy` |
| Test namespace | `GodSim.Tests.<Module>` | `GodSim.Tests.Settlements` |
| Types, methods, properties, public fields, constants | `PascalCase` | `TownPlannerSystem`, `MaxEntries` |
| Private fields | `_camelCase` | `_scratch`, `_keyCache` |
| Locals, parameters | `camelCase` | `settlementId` |
| Systems | `<Thing>System`. `Name` = `"<Module>.<Thing>"`. | `Population.Housing` |
| Module factory | `<Module>Module.CreateSystems()`; sub-areas `<SubArea>Module` | `EconomyServicesModule` |
| Blackboard key classes | `<Area>Keys` with `public const string` and an XML doc per key | `SocietyKeys.FreeHousing = "pop.freeHousing"` |
| Blackboard key strings | `"<prefix>.<camelName>"`, one writer. Prefixes in use: `ai.` (`ai.gov.`, `ai.need.`, `ai.terr.`), `econ.`, `pop.`, `lifecycle.`, `prog.`, `build.`, `dipl.`, `mil.`, `nature.`, `rel.`, `hero.`, `obj.`, `game.` | `"econ.foodDays"` |
| Tuning constants | `<Area>Tuning` / `<Area>Balance` static classes | `RelationsTuning`, `LifecycleBalance` |
| Tech keys | `"<age>.<name>"`, lowercase | `"bronze.casting"` |

- One public type per file where sensible. The file is named after the type. Partial catalog files
  may use `Type.Part.cs` (`BuildingsEarly.Bronze.cs`).
- Sub-area helpers are `internal`, or prefixed with the sub-area name, to avoid collisions inside a
  shared module namespace.
- `sealed` by default for classes. `static` for stateless helpers and catalogs.
- No abbreviations beyond the common ones (`Id`, `Db`, `Ctx`, `Rng`, `Min`/`Max`).

## 5. XML documentation

- Every `public` type and member in Core MUST have an XML doc comment (`/// <summary>`).
- Summaries state **behavior and units**: what is computed, how often, the value range, and
  per-tick vs per-year. Example: "Per-capita coverage 0..~2 (1 = fully served)".
- Systems document their interval, inputs (state fields and blackboard keys read), outputs (fields
  and keys written, events published) and failure behavior.
- Use `<see cref="..."/>` for contract types and `<c>"key.name"</c>` for blackboard keys.
- Do not restate the signature. Document invariants and edge cases (null ids, -1, empty lists).

## 6. Error handling and untrusted data

1. **Never throw out of `Tick` on bad data** (contract rule 5). Check `null`, `-1` ids, out-of-range
   enums and empty lists, then skip or clamp.
2. **No NaN or Infinity in state.** Run every computed float that is stored through
   `MathUtil.Sanitize` and/or `MathUtil.Clamp`/`Clamp01`. Guard divisions (`Math.Max(0.001f, x)`).
3. **`Guard.*` is for programmer errors** at API boundaries (constructors, `WorldOps`, catalogs).
   Throwing there is correct. Throwing in a running system is not.
4. **Untrusted input** (save files, `SimConfig`, `GodCommand`, mod and content data, file paths)
   MUST be validated before use: `SimConfig.Sanitize()`, `ICommandValidator`, `InputSanitizer`,
   `ContentValidator`. Reject with `SimValidationException` and a human-readable reason.
5. **Bounded everything:** loop retries, collection growth (logs, chronicle, blackboard keys),
   string lengths (`Chronicle` caps text at 300 characters), and path searches.
6. **Logging:** use `ctx.Log` (`ISimLogger`) via `Info/Warn/Error/Debug`. Never `Console` or
   `UnityEngine.Debug` in Core. Guard expensive messages with `MinLevel`. Do not log per entity per
   tick.
7. `catch` blocks in Core are allowed only at isolation boundaries (the engine, the event bus,
   loaders), or around content-defined formats (`NameGenerator` catches `FormatException`). Never
   swallow exceptions silently. Log them.

## 7. State mutation

- Entity creation and destruction and link changes MUST go through `ctx.Ops` (`WorldOps`). See
  [ADR-0004](adr/0004-worldops-mutation-gateway.md).
- MUST NOT call `EntityStore.Add/Remove` from module code, or write `Person.HomeId`, `WorkplaceId`,
  `SettlementId`, `ArmyId`, `Building.Residents`, `Workers`, `Settlement.BuildingIds`,
  `ResidentIds` or `WorldMap.BuildingAt` directly.
- After changing elevation, biomes or flags, call `map.MarkTerrainChanged()` and publish one
  `TerrainChangedEvent` per batch (see `TerrainDirtyRect`).
- Use `Stockpile.Add/TryConsume/TryPay` (flow accounting). `SetRaw` is for persistence, tests and
  divine effects only.
- MUST NOT edit `Core/Contracts/`. Propose changes in the PR summary.

## 8. Tests

The same test files run in the Unity Test Runner **and** `dotnet test`, so tests use only the
**NUnit 3 basic API**:

| Allowed | Not allowed |
|---|---|
| `[TestFixture]`, `[Test]`, `[TestCase(...)]`, `[SetUp]`, `[TearDown]` | `[TestCaseSource]` with async, `[Parallelizable]`, `[Timeout]`, `[Retry]`, `[Values]`/`[Range]` combinatorics |
| `Assert.AreEqual`, `AreNotEqual`, `IsTrue`, `IsFalse`, `IsNull`, `IsNotNull`, `Greater`, `Less`, `That(x, Is...)`, `Throws<T>`, `DoesNotThrow` | NUnit 4 constraint-only API, `Assert.Multiple`, FluentAssertions, Moq |
| `Assert.AreEqual(expected, actual, delta)` for floats | Exact float equality on computed values |

- **Location:** `Assets/GodSim/Tests/EditMode/<Module>/<SubArea>/`, namespace
  `GodSim.Tests.<Module>`.
- **Fixtures:** use `GodSim.Tests.Shared` (`TestDb.CreateStub()`, `TestDb.FillMissing(db)`,
  `TestWorlds.CreateContext(db, size, seed)`, `TestWorlds.AddSettlement(...)`,
  `TestWorlds.Run(system, ctx, ticks)`, `TestWorlds.RunAll(...)`). Only the lead edits `Shared/`.
  Put module-specific helpers in `<Module>TestKit.cs` (for example `PlanningTestKit`).
- **Tiny worlds:** `new WorldState(seed, new WorldMap(64, 64))` with a hand-filled `GameDatabase`.
  Do not depend on other modules' catalogs in module tests.
- **Naming:** `Method_State_ExpectedResult` or a descriptive sentence:
  `Tick_HomelessPeople_AreAssignedToFreeHouse`.
- **Required tests per system:** the happy path; bad data (dead entities, `-1` ids, empty
  settlement) does not throw; `DeltaTicks` scaling; **determinism** (two runs with the same seed
  give equal results); no NaN after many ticks.
- Use `SimConfig { StrictMode = true }` in tests so faults surface.
- Tests MUST NOT touch the file system outside a temp directory, the network, or the clock.

## 9. Unity-side rules

`Assets/GodSim/Unity/` (`GodSim.Unity`) and `Assets/GodSim/Editor/` (`GodSim.Editor`).

1. **No game logic in MonoBehaviours.** They host the simulation (`GodSimRunner`), render state, and
   translate input into `GodCommand`s. Rules, formulas and decisions live in Core.
2. **Never mutate `WorldState` from Unity code.** The only write path is
   `CommandQueue.TryEnqueue`, plus facade methods on `GodSimulation` (save, load, speed).
3. **Subscribe and unsubscribe symmetrically:** subscribe in `OnEnable`, unsubscribe in
   `OnDisable` (or `Awake`/`OnDestroy`), using the **same delegate instance** stored in a field.
   Re-creating a lambda does not unsubscribe.
4. **Event handlers are O(1):** set dirty flags or enqueue ids, then rebuild in
   `Update`/`LateUpdate`. Handlers run inside the simulation step and count toward its budget.
5. **No per-frame allocations:** no LINQ, no `GetComponent` in `Update` (cache it), no string
   building for the HUD every frame (update on change or at a low rate), pooled GameObjects and
   meshes, `MaterialPropertyBlock` instead of material instances, reused `List<>` buffers.
6. **Coordinates and colors** go through `GodSimCoords` and `GodSimPalette`, never ad hoc math.
7. **Logging** through `UnityLogSink` (implements `ISimLogger`).
8. **Input** uses the Input System package (`ENABLE_INPUT_SYSTEM_PACKAGE`). Bindings are
   documented in [`CONTROLS.md`](CONTROLS.md).
9. **Editor code** stays in `GodSim.Editor` (or under `#if UNITY_EDITOR` when it must live in
   runtime assemblies). Menu items go under `GodSim/`.
10. Serialized fields are `[SerializeField] private`. Expose read-only properties when needed.

## 10. Formatting

- 4 spaces, no tabs. Allman braces for types and methods. Single-line bodies are allowed for
  trivial members (`=> ...`).
- Lines up to about 140 characters.
- `using` directives at the top of the file, outside the namespace, `System*` first.
- One blank line between members. Use section comments (`// ---- people`) in long files, as
  `WorldOps` does.
- `var` when the type is obvious from the right-hand side.
- Literal suffixes: `1f`, `0.5f`, `1UL`. No implicit double-to-float conversions in Core math.
