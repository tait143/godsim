# ADR-0004: WorldOps mutation gateway

- **Status:** Accepted
- **Date:** 2026-11-19
- **Deciders:** lead
- **Related:** [ENGINEERING_CONTRACT §3.3](../ENGINEERING_CONTRACT.md), [ARCHITECTURE §9](../ARCHITECTURE.md#9-deferred-removal-and-worldops-invariants), `Contracts/State/WorldOps.cs`, `Contracts/State/EntityStore.cs`

## Context

The entity graph is full of bidirectional links: people and homes, people and workplaces, people
and settlements, buildings and settlements, settlements and civilizations, soldiers and armies,
spouses, and map tiles and buildings. A dozen modules, written in parallel, create and destroy
entities. Without a single gateway, each module would update links its own way, and dangling ids
would appear (a resident of a demolished house, a worker of a dead building). Removing items from
an `EntityStore` while another system iterates it would also skip elements or throw.

## Decision

We will route **every** structural mutation through `WorldOps` (`ctx.Ops`):

- **Creation:** `CreateCivilization`, `CreateSettlement`, `FoundSettlement`, `CreatePerson`,
  `CreateBuilding`, `CreateArmy`.
- **Destruction:** `KillPerson`, `DestroyBuilding`, `DestroySettlement`, `DisbandArmy`,
  `CollapseCivilization`.
- **Links:** `AssignHome`/`ClearHome`, `AssignJob`/`ClearJob`, `MovePersonToSettlement`,
  `TransferSettlement`, `ClaimTerritory`/`ReleaseTerritory`, `CompleteBuilding`.
- **History:** `AddChronicle`.

Each operation updates both sides of every link, updates `WorldStats`, publishes the matching
event, and writes chronicle entries for notable changes.

**Removal is deferred.** Destroy operations flag the entity (`IsAlive = false` or
`IsDestroyed = true`) and enqueue its id in `WorldState.Pending*Removals`. `SimulationEngine` calls
`WorldOps.FlushRemovals()` after every system and after `Initialize`. Systems iterating a store must
skip flagged entities. Pending lists are `internal`, never serialized, and always empty between
ticks.

Plain field updates that do not create or destroy entities or change links (`Person.Health`,
`Settlement.Happiness`, `Stockpile`, `Building.Condition`) may be written directly by the owning
module.

## Consequences

### Positive
- Links stay consistent no matter which module causes a death or a demolition.
- Iterating while killing is safe within a system. Every later system sees a compacted store.
- Events and stats cannot be forgotten, because they are emitted at the single choke point.
- Ids are monotonic (`IdAllocator`) and never reused, so stale ids in events or saves resolve to
  `null`, never to a different entity.

### Negative / costs
- `WorldOps` lives in Contracts. New link types or operations require a contract change by the lead.
- Flagged entities are visible until the flush. Every loop needs an `IsAlive`/`IsDestroyed` check,
  and code that forgets it acts on corpses for the rest of that system's run.
- `EntityStore.Remove` swap-removes, so store order changes after a flush. That is deterministic,
  but ids are not sorted.
- Known gaps, which systems must tolerate: `DestroySettlement` does not clean up armies that
  reference the settlement, and `CreateBuilding` can claim footprint tiles without
  `TerritoryChangedEvent`.

## Alternatives considered

### Immediate removal with reverse iteration
Fragile: it only works for the store being iterated, not for stores reached through links.

### ECS-style command buffers per system
More general, but heavier. Structural changes would only become visible at the end of the tick,
which complicates gameplay logic (for example, "is this house free right now?").

### Let each module manage its own links
Exactly the failure mode we want to avoid with ~50 parallel contributors.
