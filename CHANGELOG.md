# Changelog

All notable changes to this project are documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/). This project
adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0.0, minor
versions may break save files and the module contract. Such breaks are listed under **Changed**
with a `Save-Format:` or `Contract:` note. Any change to the system order or count also gets a
`Determinism:` note, because it changes every seed's history.

## [Unreleased]

### Pending
- Per-module `CreateSystems()` factories from contract §6 for Settlements, Population, Economy,
  AI, Diplomacy and Divine. Composition lists systems directly and does not need them.
- `DefaultComposition.BuildDatabase()` does not run `ContentValidator` yet.
- `docs/SECURITY.md` (security team) and `docs/MODDING.md`, both referenced from code or docs.

### Changed
- Save-Format: schema 2. Each settlement record now ends with the `Stockpile` current-period flow
  counters (`PeriodProduced`/`PeriodConsumed`); schema-1 files still load (counters = 0).
  `StateHasher.EncodingVersion` is 2 and hashes these counters.
- Determinism: save -> load -> continue now matches a straight run. `Initialize` keeps persisted
  values on a world that has already ticked (Faith mana/capacity, Military defense/power, Storage
  capacity, Services output/coverage). `FireSystem` mirrors its burn timers, scan/process cursors
  and sweep phase into world Memory (`nature.fire.*`) and restores them on load without RNG draws.
  Governor build-order reasons no longer read the unsaved `Settlement.AiLog`; the per-advisor
  breakdown appears only in the AI log line.
- AI: `AdvisorBoard.VoteBuild` takes an optional `logScale`. Advisors pass `CycleScale`, so a
  reason is logged when the vote is strong per 24-tick council cycle.

### Known issues
- Saves store tech references as registration indices. Inserting a tech mid-catalog remaps old
  saves.

## [0.1.0] - 2026-11-19

The initial architecture and the first full wave of simulation, presentation and tooling.

### Added

**Contracts (`Assets/GodSim/Core/Contracts`)**
- Foundation:
  - PCG32 `Rng` with serializable state, `Fork` and `PerYearToPerTicks`.
  - Stable `Hashing` (FNV-1a 64, SplitMix-style `Mix64`).
  - `Vec2i`, `MathUtil`, `Guard`, `SimValidationException`, `ISimLogger`.
  - A bounded A* `GridPathfinder`.
- Data:
  - Shared enums: 5 races, 10 ages, 13 resources, 9 services, 16 biomes, 75 building types,
    22 jobs, 19 god powers.
  - Definitions for races, ages, techs, buildings and god powers.
  - A freezable, self-validating `GameDatabase` (House must hold 3 to 5).
- State:
  - `WorldState` (schema v1) and the struct-of-arrays `WorldMap`.
  - Entities: `Person`, `Building`, `Settlement`, `Civilization`, `Army`.
  - `EntityStore<T>`, `IdAllocator`, and `Stockpile` with flow statistics.
  - Diplomacy, god, weather, chronicle and stats state.
  - The `WorldOps` mutation gateway with deferred removal.
- Events: an allocation-free struct `EventBus` with fault isolation, and 31 events.
- Simulation:
  - `ISimSystem` and `SimulationEngine`: fixed order, staggered intervals, per-system profiling,
    fault isolation.
  - `SimConfig.Sanitize`.
  - `GodCommand`, `CommandQueue`.

**Composition:** `DefaultComposition` (database build, canonical order of 44 systems, world
creation and civilization seeding) and the `GodSimulation` facade (`CreateNew`, `Load`, `Save`,
`ComputeHash`, `Step`/`StepMany`, `TryCastPower`).

**World and Geography**
- `WorldGenerator`: domain-warped fBm, four presets, percentile sea level, rivers, biomes,
  deposits.
- `GradientNoise`/`FractalNoise`, `StartLocationFinder`, `MapStatistics`, `WorldHash`.
- The `GeographyIndex` region atlas (in progress).

**Races:** Human, Elf, Dwarf, Orc and Saurian definitions, `RaceCatalog`, `RaceLore` and
`RaceQueries`.

**Progression:** `AgeCatalog` (10 ages), `TechCatalog` (94 techs), `TechTreeReport`,
`ModifierSystem`, `ResearchSystem`, `AgeProgressionSystem`, `TechQueries` and `ProgressionModule`.

**Buildings:** all 75 building types across `Early`, `Middle` and `Late`, aggregated by
`BuildingCatalog`.

**Settlements:** `TownPlannerSystem` (zoning, site scoring, access roads), `RoadNetworkSystem`,
`ConstructionSystem`, `SettlementGrowthSystem` (tiers from Camp to Megalopolis) and
`MaintenanceSystem` (decay, repair, renovation by age and race, obsolescence).

**Population:** `NeedsSystem`, `HousingSystem`, `JobAssignmentSystem`, `AgingSystem`,
`BirthSystem`, `FamilySystem`, `WellbeingSystem`, `MigrationSystem` and `LeadershipSystem`.

**Economy:** `StorageSystem`, `ServiceSystem`, `ProductionSystem`, `UpkeepSystem`, `TradeSystem`,
`ResourceRegrowthSystem` and `EconomyStatsSystem`.

**AI:** `CivilizationStrategySystem`, `ResearchDirectorSystem`, `SettlementGovernorSystem`
(`NeedEvaluator`, `BuildingScorer`, `TerritoryScanner`, response curves) and `ExpansionSystem`.

**Diplomacy:** `ContactSystem`, `RelationsSystem`, `WarDecisionSystem`, `MilitarySystem` and
`ArmySystem` (raising, marching, combat, sieges, conquest).

**Divine:** `SeasonWeatherSystem`, `GodCommandSystem` (re-validation, mana, cooldowns, alignment),
`FaithSystem`, `DisasterSystem` and `FireSystem`, plus `GodPowerCatalog` (19 powers) and
`GodPowerEffects` (blessings, shaping, wrath).

**Emergent and meta:**
- `ReligionSystem`: 16 doctrines with deterministic selection, holy sites, schisms and holy wars.
- `HeroSystem`, with the hero registry kept in blackboards.
- `WorldEventSystem` (Storytelling): calamities, faith, politics, prosperity and secession events.
- `HistoryRecorder` and `ChronicleNarrator`.
- `ObjectiveSystem` and `ObjectiveCatalog`, including the Ascension victory.

**Persistence:**
- `SaveSerializer`: `header | tag | payload`, HMAC-SHA256 or SHA-256 tag, GZip, hardened loading.
- `StateValidator`, `SaveMigrator`.
- `SaveFileStore` (`.gsav` slots, atomic writes).
- Key providers and `InstallKeyFile`.
- `StateHasher` (14 sections).
- `ReplayLog`.

**Security:** `GodCommandValidator` (two-phase validation), the deterministic `RateLimiter`,
`InputSanitizer`, `ContentValidator`/`ContentReport` (deep content checks), and `IntegritySystem`
(a yearly and on-load integrity check and repair).

**Modding:** `SafeJson` (strict, bounded), `ModManifest`, `ModPack`, `ModHardCaps`, `ModText`
sanitization, `SemVersion`.

**Unity:**
- `GodSimRunner`: an 8 ms frame budget, speeds from 1 to 240 ticks per second, a 2 s backlog cap.
- Terrain chunks and minimap, building meshes, instanced people.
- Camera, selection and god-power controllers, with Input System support.
- HUD, toasts and history charts.
- Vertex-color and water shaders.

**Editor:** the **GodSim** menu (*Create Demo Scene*, *Create Settings Asset*, *World Preview*,
*Simulate 100 Years (Headless)*, *Validate Content*, *Determinism Check*), the runner and settings
inspectors, and a build validator.

**Tools and build:**
- `GodSim.sln`, `Directory.Build.props` (C# 9, deterministic builds, analyzers) and
  `build/GodSim.Core.csproj` (netstandard2.1).
- `tests/GodSim.Tests`, with integration suites for determinism, save/load continuity, god powers,
  age progression, sanity runs and config sanitizing.
- About 40 isolated `build/checks/*` projects and the template.
- `tools/GodSim.Headless`, `tools/GodSim.Bench` (CI budgets), `tools/GodSim.SaveTool`,
  `tools/GodSim.ContentReport`, and `tools/WebViewer` (a self-contained `index.html`, a sample
  snapshot and screenshots).
- Scripts: `check-unity-compat.sh` (with self-test), `run-tests.sh`, `new-module-check.sh`.

**Docs:**
- `README.md`, `ENGINEERING_CONTRACT.md`, `ARCHITECTURE.md`.
- ADR-0001 to ADR-0008.
- `CONTRIBUTING.md`, `CODING_STANDARDS.md`.
- `CONTROLS.md`, `GAME_DESIGN.md`, `CONTENT_REFERENCE.md`, `SAVE_FORMAT.md`, `ROADMAP.md`,
  `UNITY_SETUP.md`.
- This changelog.

### Notes
- Contract: §6 gives the order as "Divine → ... → Diplomacy". The canonical order in
  `DefaultComposition` continues with emergent systems (religion, heroes, world events) and meta
  systems (history, objectives, integrity/security). §2 does not list the modules Religion,
  Heroes, Storytelling, History, Objectives, Modding and Geography. §2 also names the headless
  tool `tools/Headless/`, but it lives at `tools/GodSim.Headless/`. The contract should be updated
  to match.
- Determinism: system registration order sets each system's stagger phase. Any change to the
  order or count changes outcomes for a given seed.

[Unreleased]: https://example.invalid/godsim/compare/v0.1.0...HEAD
[0.1.0]: https://example.invalid/godsim/releases/tag/v0.1.0
