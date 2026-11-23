# GodSim — Engineering Contract

Every contributor (human or agent) follows this file. It defines the architecture boundaries, the
shared vocabulary, and the rules that keep the simulation deterministic, secure and Unity-compatible.

## 1. What we are building

A top-down **god simulator**. The player is a deity. Five races (Human, Elf, Dwarf, Orc, Saurian)
found civilizations that an AI runs on its own. Each AI builds towns one building at a time. A new
dwelling goes up for every 3–5 people (a `House` holds 3–5 residents). Civilizations research
technologies and move through **10 ages** (Dawn → Ascension). The god watches, and can help or
punish them with divine powers.

## 2. Architecture

```
Assets/GodSim/
  Core/                      GodSim.Core.asmdef  (noEngineReferences: true) — pure C#, deterministic
    Contracts/               SHARED: Foundation, Data (enums/definitions/db), State, Events, Simulation  [owner: lead]
    World/                   world generation (noise, biomes, rivers, deposits, start locations)
    Races/                   5 race definitions (RaceCatalog)
    Progression/             10 ages, tech tree, research, age advancement, modifiers
    Buildings/               BuildingCatalog: every BuildingType definition (data + balancing)
    Settlements/             town planner (zoning, site scoring, roads), construction, tiers, territory
    Population/              lifecycle, needs, housing assignment, jobs, families, migration, happiness
    Economy/                 production, services, storage, trade, upkeep, flow statistics
    AI/                      civilization + settlement governor utility AI (what to build/research, expansion)
    Diplomacy/               relations, wars, armies, battles, sieges, conquest, treaties
    Divine/                  god powers, mana/faith, seasons/weather, natural disasters
    Persistence/             versioned binary saves, integrity (HMAC), safe file store, state hashing
    Security/                command validation, rate limiting, input sanitization, content validation
    Composition/             GodSimulation facade + DefaultComposition (wires every module)  [owner: lead]
  Unity/                     GodSim.Unity.asmdef — MonoBehaviours: runner, rendering, camera, input, HUD
  Editor/                    GodSim.Editor.asmdef — scene builder, inspectors, menu items
  Tests/EditMode/<Module>/   NUnit tests; compile in Unity Test Runner AND in dotnet (tests/)
build/checks/<Module>/       per-module isolated compile/test projects (dotnet)
tests/                       full-solution test project (dotnet, NUnit)
tools/Headless/              console runner + JSON snapshot exporter
tools/WebViewer/             static HTML viewer for snapshots
```

**Dependency rule:** every module depends ONLY on `Core/Contracts`. Modules never reference each
other's types. They talk through shared state (`WorldState`), the shared database (`GameDatabase`),
events (`EventBus`) and the blackboards (`Settlement.Memory`, `Civilization.Memory`,
`WorldState.Memory`). Only `Composition/` knows every module.

## 3. Hard rules

1. **Unity compatibility:** C# 9 max, `netstandard2.1`. No `UnityEngine` in Core. No file-scoped
   namespaces, `record struct`, `global using`, `init` accessors, `required`, raw string literals,
   or `System.Text.Json`. `Nullable` is disabled. Stick to `System`, `System.Collections.Generic`,
   `System.Text`, `System.IO`, `System.Security.Cryptography`, and `System.IO.Compression`.
2. **Determinism:** all randomness goes through `ctx.Rng` (PCG32 `Rng`). Never use `System.Random`,
   `UnityEngine.Random`, `Guid.NewGuid`, `DateTime.Now`, `Environment.TickCount`,
   `string.GetHashCode()` (use `Hashing.Fnv1a64`), or `HashSet`/`Dictionary` iteration order for
   decisions. Iterate `EntityStore.Items` by index. No threads or async in Core.
3. **Mutations go through `ctx.Ops` (WorldOps)** for anything that creates or destroys an entity or
   changes links: births/deaths, homes, jobs, migration, buildings, settlements, armies, conquest,
   chronicle. Removal is deferred until the engine flushes, so while iterating, skip entities with
   `!IsAlive` / `IsDestroyed`.
4. **Never edit `Core/Contracts/`.** If you need a new field, use the `Memory` blackboards with
   namespaced keys (`"ai.lastExpand"`). Report any contract change you would like in your final summary.
5. **Never throw out of `Tick` on bad data.** Check `null`/`-1` ids, and clamp values with
   `MathUtil.Clamp/Sanitize`. No NaN or Infinity may enter state.
6. **Performance:** a world with 5,000 people should average under 3 ms per tick across all
   systems. Avoid LINQ and allocations in per-tick loops; reuse lists stored in fields. Heavy work
   runs on intervals (`TickInterval` 4–30) and is staggered.
7. **Names:** namespace `GodSim.Core.<Module>`, one public type per file where sensible, XML doc
   comments on public APIs, `PascalCase` types/members, `_camelCase` private fields.
8. **Tests:** NUnit 3 basic API only (`[Test]`, `[TestCase]`, `[SetUp]`, `Assert.AreEqual/IsTrue/
   That/Throws`) so the same files run in the Unity Test Runner. Put them in
   `Assets/GodSim/Tests/EditMode/<Module>/`, namespace `GodSim.Tests.<Module>`. Tests may build
   tiny worlds by hand (`new WorldState(seed, new WorldMap(64,64))`, a fresh `GameDatabase` filled
   with minimal definitions you construct in the test).

## 4. Time & units

- 1 tick = 1 day. `SimTime.TicksPerYear = 96` (4 seasons × 24 days). Ages last ~80–200 years.
- "PerTick" rates in definitions are per day. A system with `TickInterval = N` receives
  `ctx.DeltaTicks = N` and must scale its rates by it.
- Per-year probabilities become per-interval chances via `Rng.PerYearToPerTicks(p, ctx.DeltaTicks)`.

## 5. Gameplay rules shared by all modules

- **Housing:** `House` holds 3–5 residents, with `HousingCapacity` rolled at placement. There is
  one dwelling building per 3–5 people. Homeless people lose happiness and health. Houses look
  different by race and by the age they were built in (`StyleRace`, `StyleAge`).
- **Availability:** `db.IsBuildingAvailable(civ, type)` is the single source of truth. A building
  is available when `civ.Age >= MinAge` (or the tech `UnlockBuilding` sets
  `civ.UnlockedBuildings[type]`), `civ.Age <= MaxAge`, and the race is allowed.
- **Build pipeline:** the AI appends a `BuildOrder` to `settlement.BuildQueue`. The town planner
  picks a site and calls `Ops.CreateBuilding`. Construction pays `Cost`, then accrues
  `BuildWork` from builders and finally calls `Ops.CompleteBuilding`.
- **Jobs:** buildings with `Job`/`JobSlots` employ adults. `settlement.JobPriority[job]` (written
  by the AI) biases assignment. `settlement.DesiredBuilders` (written by construction) asks for
  builders, who have job `Builder` and workplace -1. `Soldier` jobs come from Barracks, Castle
  and similar buildings.
- **Services:** the economy computes `settlement.ServiceOutput[]` (raw points per tick) and
  `ServiceCoverage[]` (per capita, where 1 means fully served). Research = ServiceOutput[Research].
  Mana is derived from ServiceOutput[Faith] and devotion. Defense = ServiceOutput[Defense] plus
  soldiers.
- **Flow statistics:** `Stockpile.Add/TryConsume` tally flows automatically. The economy calls
  `RollStatistics()` once per tick per settlement. The AI reads `Stockpile.Net(type)`,
  `AvgIncome` and `AvgExpense`.
- **Modifiers:** progression recomputes `civ.Modifiers` (race × age × techs). Every module
  multiplies by the relevant field: Production[res], Research, Growth, BuildSpeed, Faith,
  Military, Storage, Trade, Service[svc], HealthBonus, HappinessBonus and LifespanBonusYears.
- **Commands:** player god powers are `GodCommand`s that go through `CommandQueue.TryEnqueue`,
  which validates them with an `ICommandValidator` from Security. The divine module drains the
  queue, re-validates each command (to avoid TOCTOU) and executes it.

### Races (flavor all modules share)
| Race | Habitat | Lifespan | Strengths | Personality | Architecture |
|---|---|---|---|---|---|
| Human | grassland, forest, hills | ~70 | balanced, fertile, trade | moderate | timber → stone → brick → glass; gable roofs |
| Elf | forest, jungle | ~350 | research, faith, wood, health | peaceful, xenophobic | organic, tree platforms, leaf/dome roofs |
| Dwarf | hills, mountain | ~200 | ore, metal, tools, stone, defense | industrious, stubborn | carved stone, half-sunk halls, flat/tiered roofs |
| Orc | savanna, tundra, desert, hills | ~50 | military, fertility, hardiness | aggressive, expansionist | hide & timber, spiked cone roofs |
| Saurian | swamp, jungle, beach (hot) | ~90 | fishing, faith, clutches (high fertility) | pious, patient | stilts, stepped pyramids |

### Ages
Dawn, Bronze, Iron, Classical, Medieval, Renaissance, Industrial, Modern, Information, Ascension.
To advance, a civ needs `TotalResearch >= AgeDefinition.ResearchThreshold`, plus
`RequiredTechsFromPreviousAge` techs from the previous age, plus `MinPopulation`.

## 6. Module entry points (used by Composition)

Each module exposes exactly these (static) entry points:

| Module | Entry points |
|---|---|
| World | `World.WorldGenerator.Generate(WorldGenSettings settings, Rng rng) : WorldMap`; `World.WorldGenSettings.FromConfig(SimConfig c)`; `World.StartLocationFinder.Find(WorldMap map, GameDatabase db, IReadOnlyList<RaceId> races, Rng rng) : Vec2i[]` |
| Races | `Races.RaceCatalog.Register(GameDatabase db)` |
| Progression | `Progression.AgeCatalog.Register(GameDatabase db)`; `Progression.TechCatalog.Register(GameDatabase db)`; `Progression.ProgressionModule.CreateSystems() : IEnumerable<ISimSystem>` |
| Buildings | `Buildings.BuildingCatalog.Register(GameDatabase db)` |
| Settlements | `Settlements.SettlementsModule.CreateSystems()` |
| Population | `Population.PopulationModule.CreateSystems()` |
| Economy | `Economy.EconomyModule.CreateSystems()` |
| AI | `AI.AIModule.CreateSystems()` |
| Diplomacy | `Diplomacy.DiplomacyModule.CreateSystems()` |
| Divine | `Divine.GodPowerCatalog.Register(GameDatabase db)`; `Divine.DivineModule.CreateSystems()` |
| Persistence | `Persistence.SaveSerializer.Serialize(WorldState s, SaveOptions o) : byte[]`; `Persistence.SaveSerializer.Deserialize(byte[] data, GameDatabase db, SaveOptions o) : WorldState`; `Persistence.StateHasher.Compute(WorldState s) : ulong`; `Persistence.SaveFileStore` |
| Security | `Security.GodCommandValidator : ICommandValidator` (ctor `(SimConfig config)`); `Security.InputSanitizer`; `Security.ContentValidator.Validate(GameDatabase db) : List<string>` |

System order per tick (Composition): Divine → Economy → Population → Progression → AI → Settlements → Diplomacy.

## 7. Isolated build & test for your module

Contracts compile alone. Copy `build/checks/_template/Template.Tests.csproj` to
`build/checks/<Module>/<Module>.Tests.csproj` and replace `MODULE` with your folder name. Then run:

```bash
cd build/checks/<Module> && dotnet test -nologo -v q --disable-build-servers
```

This must pass with **0 errors** before you finish. Do not build other modules' folders. They are
being written at the same time and may be incomplete.
