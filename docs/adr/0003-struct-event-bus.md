# ADR-0003: Struct event bus

- **Status:** Accepted
- **Date:** 2026-11-19
- **Deciders:** lead
- **Related:** [ARCHITECTURE §10](../ARCHITECTURE.md#10-event-bus-semantics-and-fault-isolation), `Contracts/Events/EventBus.cs`, `Contracts/Events/SimEvents.cs`

## Context

Renderers, the HUD, the chronicle view and some modules need to know when things happen (a building
completed, a person died, terrain changed). Polling all of `WorldState` every frame does not scale
to thousands of entities. The notification mechanism must:

- not allocate per event (thousands of births and deaths per simulated year);
- not let a buggy subscriber, such as a UI script, crash or corrupt the simulation;
- be safe when handlers subscribe or unsubscribe during dispatch (common in Unity `OnDisable`);
- keep modules decoupled (contract rule: modules never reference each other's types).

## Decision

We will use a **synchronous, typed `EventBus` whose events are structs** (`where T : struct`).

- Events are plain data structs declared in `Contracts/Events/SimEvents.cs` (31 types). Only the
  lead adds event types, so the shared vocabulary stays stable.
- Dispatch is synchronous, in subscription order, on the publishing thread, inside the publishing
  system's `Tick`.
- Handler arrays are copy-on-write. Subscribe and unsubscribe allocate, publish does not, and
  changing subscriptions during a publish is safe.
- Every handler call is wrapped in try/catch. Faults are logged and counted (`HandlerFaults`) and do
  not reach the publisher, unless `StrictMode` is set (from `SimConfig.StrictMode`, used in tests
  and CI).
- `WorldOps` publishes all entity lifecycle events, so events and state changes cannot drift apart.

## Consequences

### Positive
- No boxing and no per-publish allocation. `Action<T>` with a struct `T` is invoked directly.
- Presentation updates incrementally (`BuildingCompletedEvent` swaps one mesh).
- A faulty subscriber cannot halt the simulation in production. In CI it fails loudly.

### Negative / costs
- Handlers run inside the simulation step. Slow handlers count against the publishing system's
  profile and the 3 ms budget. Subscribers must do O(1) work (set dirty flags) and defer the rest to
  `Update`/`LateUpdate`.
- Handlers **must not mutate simulation state**. Doing so would make results depend on which
  presentation is attached, which breaks determinism. This is enforced by review, not by the type
  system.
- Some events carry strings (`GodPowerCastEvent.Message`, `ChronicleAddedEvent.Entry.Text`), which
  are allocated by the publisher. That is acceptable for rare events only.
- With no queueing there is no replay of missed events. A late subscriber must read current state
  first.
- A swallowed handler exception can leave a subscriber half-updated. `HandlerFaults` should be
  surfaced in debug UI.

## Alternatives considered

### C# `event` fields per module
Couples subscribers to publisher types, which violates module boundaries. An exception in one
handler aborts the rest.

### Queued (deferred) events, dispatched at end of tick
Removes re-entrancy hazards and handler time from systems, but the queue holds stale ids after the
removal flush and needs buffers per type. We may add an opt-in presentation-side queue later.

### Class-based events / `object` payloads
Allocate per publish. Rejected on performance grounds.
