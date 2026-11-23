# ADR-0008: Module boundaries and blackboards

- **Status:** Accepted
- **Date:** 2026-11-19
- **Deciders:** lead
- **Related:** [ENGINEERING_CONTRACT §2, §3.4, §6](../ENGINEERING_CONTRACT.md), [ARCHITECTURE §2](../ARCHITECTURE.md#2-the-dependency-rule), `Contracts/State/Entities.cs` (`Memory`)

## Context

About 50 contributors write a dozen modules **in parallel**. If modules referenced each other's
types, nobody could compile until everyone had finished, and every rename would ripple across teams.
Modules still need to share small facts that are not worth a contract field, such as "days of food
left", "the AI's posture" or "the tick of the last war between A and B".

## Decision

1. **Dependency rule:** a module depends only on `Core/Contracts`. Only `Core/Composition`
   references modules. It registers content through the catalog entry points in contract §6, and
   `DefaultComposition.CreateSystems()` instantiates systems directly in canonical order.
2. **Shared channels:** `WorldState` (state), `GameDatabase` (frozen definitions), `EventBus`
   (notifications), and the **blackboards** `Settlement.Memory`, `Civilization.Memory` and
   `WorldState.Memory`. Each blackboard is a `Dictionary<string, float>` with ordinal comparison and
   is serialized.
3. **Key conventions:**
   - Namespaced `"<prefix>.<camelName>"`. The prefix identifies the writing module. Prefixes in
     use:
     - `ai.` (and `ai.gov.`, `ai.need.`, `ai.terr.`)
     - `econ.`
     - `pop.` and `lifecycle.`
     - `prog.`
     - `build.`
     - `dipl.` and `mil.`
     - `nature.`
     - `rel.`
     - `hero.`
     - `obj.` and `game.`
   - **One writer per key.** Other modules read the literal string, because they cannot reference
     the writer's constant. The writer documents each key in a public `*Keys` class and in the
     ARCHITECTURE module catalog. The `*Keys` classes are `CivStrategyKeys`, `GovernorKeys`,
     `EconomyServiceKeys`, `SocietyKeys`, `LifecycleMemoryKeys`, `ProgressionKeys`,
     `RelationsMemoryKeys`, `HeroKeys` and `ObjectiveKeys`.
   - Modules with no contract fields of their own (Heroes, Objectives, Religion) keep **all**
     persistent state in blackboards. For example, the hero registry lives under
     `hero.<personId>.*` in `WorldState.Memory`.
   - Values are floats. Ticks stored as floats are exact up to 2^24 (about 174,000 game years).
     Enums and booleans are stored as whole numbers.
   - Parameterized keys (`build.wait.<id>`, `dipl.lastWar.<lo>.<hi>`) must be removed when the
     subject disappears, and their strings should be cached to avoid per-tick allocation
     (see `RelationsKeyCache`).
   - Never iterate a blackboard to make decisions (its order is unspecified).
4. **Contract changes** (`Core/Contracts`) are made only by the lead. Modules propose them in their
   PR summary. A blackboard key that proves essential to several modules is a candidate for
   promotion to a real field.
5. **Sub-areas:** large modules are split into folders owned by different teams (for example
   `Economy/Production` and `Economy/Services`). Each has an isolated check project
   `build/checks/<ModuleSubArea>`. Sub-area helper types are `internal`, or prefixed with the
   sub-area name, to avoid collisions inside the shared namespace.

## Consequences

### Positive
- Teams compile and test independently from day one (`build/checks/*`).
- Composition is the only place that knows the full system list and order.
- Blackboards make it cheap to share a value without a contract round trip, and they are
  automatically persisted.

### Negative / costs
- Blackboard keys are **stringly typed**. A typo silently reads the fallback value. Readers should
  use `Mem(key, fallback)` with a sensible fallback, and integration tests should assert that key
  producers and consumers agree.
- Blackboards are `float` only. Structured data needs a contract change.
- Two sub-areas of one module share a namespace (`GodSim.Core.Economy`), so public type names must
  not collide.
- Prefix discipline is by convention only:
  - `trade.first.<lo>.<hi>` (written by Economy) does not use `econ.`.
  - Population uses both `pop.` and `lifecycle.`.
  - `ai.threat` is used by both strategy and governor code.
- Blackboards grow with per-entity keys (`hero.<id>.*`, `build.wait.<id>`). The writer must remove
  keys when the subject is gone, or saves and hashes grow without bound.

## Alternatives considered

### Modules expose interfaces registered in a service locator
Introduces runtime coupling and initialization-order problems, and still needs shared interface
types in Contracts.

### Typed per-module state classes added to `WorldState`
Type-safe, but every addition is a contract change that blocks parallel work.

### Events only
Events are transient. Values like "days of food left" must be readable at any time and persisted.
