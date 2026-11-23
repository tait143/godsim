# ADR-0002: Deterministic simulation

- **Status:** Accepted
- **Date:** 2026-11-19
- **Deciders:** lead
- **Related:** [ENGINEERING_CONTRACT §3.2](../ENGINEERING_CONTRACT.md), [ARCHITECTURE §8](../ARCHITECTURE.md#8-determinism-model), `Contracts/Foundation/Rng.cs`, `Contracts/Simulation/SimulationEngine.cs`

## Context

We want reproducible worlds from a seed, replays driven by a command log, golden-state regression
tests, bug reports that can be replayed exactly, and possibly lockstep multiplayer later. We need
all of that without serializing every frame. Sources of nondeterminism in .NET and Unity include
`System.Random` seeding, `string.GetHashCode()` (randomized per process in .NET Core), `Dictionary`
and `HashSet` iteration order, wall-clock time, `Guid`, threads, and frame-rate-dependent updates.

## Decision

We will make the simulation a **deterministic function of (seed, `SimConfig`, content, system order,
command log)**.

- **One RNG:** PCG32 `Rng` lives in `WorldState` and is reached through `ctx.Rng`. Its state
  (`State`, `Increment`) is serialized. `Rng.Fork(salt)` creates sub-streams deterministically, and
  forking advances the parent.
- **Stable hashing:** `Hashing.Fnv1a64`, `Mix64` and `Combine`. Never `string.GetHashCode()`.
- **Ordered iteration:** `EntityStore<T>.Items`, by index (insertion order, swap-remove).
  `SortedDictionary` for relations and `SortedSet` for known techs. Unordered collections are used
  for lookup only, never for decisions.
- **Fixed tick:** 1 tick = 1 day. Systems run in a fixed registration order, staggered by
  `(Tick + index) % interval`, and receive `DeltaTicks = interval`.
- **No time, no threads:** no `DateTime.Now`, `Environment.TickCount`, `Guid.NewGuid`, threads or
  async in Core. `Stopwatch` only feeds `SystemProfile`.
- **Commands at tick boundaries:** `CommandQueue` stamps `Sequence` and `IssuedTick`. The Divine
  group drains it at the start of the next step. `OnAccepted` lets a recorder log commands for replay.
- **Verification:**
  - `StateHasher.Compute(WorldState)` is FNV-1a 64 over a canonical encoding.
    `ComputeSections` pinpoints which of its 14 sections diverged.
  - `WorldHash` covers the map.
  - `ReplayLog` stores hash checkpoints.
  - `tests/GodSim.Tests/Integration/DeterminismTests.cs` and `SaveLoadContinuityTests.cs`.
  - The Editor menu item **GodSim > Determinism Check**, and headless `--hash`.

  Every module also ships a "two runs, same seed, same result" test.

## Consequences

### Positive
- Bugs reproduce from a seed plus a command log. Balancing comparisons are apples to apples.
- Save files can be validated against a state hash, and desyncs can be detected.
- The engine can run headless at any speed with the same result.

### Negative / costs
- **Fragility.** One stray `Dictionary` iteration or extra `Rng` call changes the whole future. Code
  review must check for it (see the CONTRIBUTING checklist).
- **System order is part of the determinism surface.** Adding or reordering a system shifts later
  systems' stagger phase and changes outcomes. Golden hashes must be regenerated deliberately.
- **Floating point.** Basic IEEE operations are reproducible. `Math.Pow`, `Math.Exp`, `Math.Log` and
  `Math.Cos` (used by `PerYearToPerTicks`, `Logistic` and `Gaussian`) are not guaranteed
  bit-identical across CoreCLR, Mono and IL2CPP or across CPUs. We guarantee determinism for the same
  build on the same platform. Cross-runtime parity is a tracked goal, not a guarantee.
- A single shared RNG stream couples systems. A change in how often system A draws changes what
  system B sees. Modules that want isolation can `Fork` a stream in `Initialize`. The fork must be
  persisted if it outlives a tick.

- **Save/load continuity** requires that every piece of state influencing the future is
  persisted. `Stockpile`'s private per-period flow counters are not, and they are non-zero
  between ticks (see ARCHITECTURE §8).

### Follow-ups
- Fix the Stockpile period-counter gap: add contract accessors, or roll statistics last in the
  tick.
- A cross-runtime golden test (Mono in the Unity Test Runner vs CoreCLR in `dotnet test`).
- Consider fixed-point or table-based replacements for `Pow`/`Exp` in hot probability code if parity
  fails.

## Alternatives considered

### Snapshot-only replays (record state every N ticks)
Large files, and they do not help reproduce bugs between snapshots. Rejected as the primary
mechanism, though it could complement determinism.

### Per-system RNG streams by default
Better isolation, but more state to persist and more chances to forget one. We keep one stream with
opt-in forks.

### Fixed-point math everywhere
Strong cross-platform guarantee, but it would cost readability and velocity across 50 contributors.
Deferred unless cross-runtime parity is required (for example for lockstep multiplayer).
