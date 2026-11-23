# GodSim.Bench

A small performance harness for GodSim Core. It has no dependencies: no BenchmarkDotNet, only `Stopwatch` and the GC counters. It covers world generation, the full simulation at steady state, a synthetic population stress test, micro-benchmarks of the `Core/Contracts` primitives, and save/load. It prints a console table, can write JSON (`--json`), and has a CI gate (`--budget`).

## Running

```bash
# full run (~2-6 min depending on the sim)
dotnet run -c Release --project tools/GodSim.Bench

# CI: fast, machine-readable, fails the job when a budget is exceeded
dotnet run -c Release --project tools/GodSim.Bench -- --quick --budget --json artifacts/bench.json

# one area
dotnet run -c Release --project tools/GodSim.Bench -- --filter contracts
dotnet run -c Release --project tools/GodSim.Bench -- --filter pathfinder,eventbus --seed 42
dotnet run -c Release --project tools/GodSim.Bench -- --list
```

| Argument | Meaning |
|---|---|
| `--quick` | Fewer iterations and shorter tick windows. Steady state is checked at years 5/25 instead of 25/100/200. The 20k stress case is skipped unless a filter selects it. |
| `--filter <a,b>` | Runs the cases whose `category/name` contains any of the comma-separated substrings (case-insensitive). |
| `--json <file>` | Writes every result, its metrics, the per-system rows and the budget verdicts (schema `godsim.bench/1`). |
| `--budget` | Exit code 1 if a budget FAILs, 3 if a benchmark case crashed, otherwise 0. |
| `--seed <n>` | World and RNG seed (default `20261119`). Every run is deterministic for a given seed. |
| `--verbose` | Echoes simulation warnings and errors, and prints stack traces for crashed cases. |
| `--list` | Lists the cases. |

Always use `-c Release`. The project forces `<Optimize>true</Optimize>`, but `GodSim.Core` is built in whatever configuration you pass. The harness prints a WARNING when either assembly was compiled without optimizations. Runtime settings: workstation non-concurrent GC (closest to Unity's single-threaded behaviour) and `TieredPGO` on.

## What is measured

| Category | Case | Details |
|---|---|---|
| `contracts` | `pathfinder` | `GridPathfinder` on a 256x256 grid. 128-tile paths: open straight, open diagonal, open 4-way, 22% walls at unit cost, and 22% walls with 1..3 cost multipliers. Worst case: an unreachable goal with `maxExpansions` 20000, and the same with no bound (a full flood). Metrics: `expansions`, `ns_per_expansion`, `path_tiles`. |
| | `entitystore` | `EntityStore<T>` at 100k: add (fresh store), random-hit `Get`, `TryGet` miss, iteration by index, random-order `Remove`. |
| | `stockpile` | `Add`+`TryConsume`, `Has`, `TryPay` (3 costs), and `RollStatistics` in three states: active flows, idle/decayed, fresh. |
| | `eventbus` | `Publish` with 0 subscribers, 5 subscribers, 0 subscribers after an unsubscribe, and 1 instance subscriber with a 40-byte struct. Each must allocate 0 B. |
| | `rng` | Throughput of `NextUInt`, `NextFloat`, `Range`, `Chance`, `Gaussian` and `PerYearToPerTicks`. |
| | `worldops` | `CreatePerson` cost and allocation. `KillPerson`+`FlushRemovals` in settlements of 100, 1k and 10k residents, which shows how removal scales. |
| `worldgen` | `generate128/256/512` | `WorldGenerator.Generate(WorldGenSettings.FromConfig(cfg), new Rng(seed))`. Also checks determinism (same seed gives the same map hash) and reports land ratio and ms per megatile. |
| `sim` | `steady192` | `GodSimulation.CreateNew` on 192x192. The world advances to years 25/100/200, and at each checkpoint 500 ticks are measured: ms/tick, the per-system breakdown, entity counts, alloc/tick, GCs and `ComputeHash()` time. |
| `stress` | `people5000`, `people20000` | Creates a world and lets it run organically for 2 years. People are then injected through `WorldOps.CreatePerson`, round-robin over the living settlements with matching race and ages from 0 to elder, and food is topped up. After 48 settle ticks, 500 ticks are measured per system. `MaxPopulation` is raised so the cap does not interfere. |
| `persistence` | `save192` | `SaveSerializer.Serialize` and `Deserialize`, plus `StateHasher.Compute`, on a world at year 25. Persistence is bound by reflection (see below). |

### Persistence detection (for the lead)

`GodSim.Core.Persistence.SaveSerializer` and `SaveOptions` are looked up by reflection in the `GodSim.Core` assembly at runtime, so the bench compiles whether or not Persistence exists:

- If the type is missing, the result is **SKIPPED** ("SaveSerializer not found").
- If the type exists but `Serialize(WorldState, SaveOptions)` does not, the result is **ERROR** (contract drift).
- `SaveOptions` must have a public parameterless constructor.
- When `StateHasher.Compute` exists, the round trip is verified (`roundtrip_hash_match`).

`GodSimulation` (`GodSim.Core.Composition`) and `WorldGenerator`/`WorldGenSettings` are referenced directly. **The bench will not compile until the composition root exists.** `build/checks/Bench/` proves that the bench compiles against Contracts plus stubs of those types.

## Reading the results

- **median / p95 / mean** are per *op*. The op is in the `op` column: a path, a call, a tick, a world. For micro-benchmarks, each sample is one iteration of N ops divided by N, so p95 shows iteration-to-iteration jitter. For sim and stress, every tick is timed on its own, so p95 is the real frame-spike indicator. Watch it for systems with `TickInterval > 1`.
- **alloc/op** is the `GC.GetAllocatedBytesForCurrentThread()` delta over the measured window divided by the number of ops. Per-tick loops should be at 0 B. **gc0/1/2** are the collections during the window. Any gen2 collection in a sim window is a problem.
- **Per-system table** (sim and stress): this harness reads `SystemProfile.LastMs` whenever `Runs` advances.
  - `ms/tick` is the amortized cost (total / ticks measured). It is comparable across systems with different intervals, and the column sum is the tick cost.
  - `ms/run` is the cost of one run. `max ms` is the worst single run, which is the spike a staggered interval system causes.
  - `engine AvgMs` is the engine's own value: an EMA per *run* with alpha 0.05. Divide it by the interval to compare it with `ms/tick`.
  - `(sum of systems)` below about 95% of the tick means the overhead (event publishing in `Step`, `FlushRemovals`) is significant.
  - `faults` greater than 0 means a system threw. Faulting or disabled systems look cheap, so do not trust a budget PASS while faults are non-zero. The budget detail warns about this.
- `log_errors` / `log_warnings` count the simulation logger. Use `--verbose` to see them.
- `stress` notes "population drifted" when the population changed by more than 25% during the window, for example through mass starvation or a housing collapse. The ms/tick is then for a smaller world.

## Budgets (`--budget`)

| Budget | Benchmark | Limit |
|---|---|---|
| 5,000 people average tick | `stress/people5000` mean | < 3 ms/tick (ENGINEERING_CONTRACT §3.6) |
| EventBus publish, 0 subscribers | `contracts/eventbus.publish(0 subscribers)` | 0 bytes total |
| EventBus publish, 5 subscribers | `contracts/eventbus.publish(5 subscribers)` | 0 bytes total |
| Pathfinder 128-tile path (open) | `contracts/pathfinder.path128.open.straight` median | < 0.5 ms |
| Pathfinder 128-tile path (22% walls) | `contracts/pathfinder.path128.obstacles22pct` median | < 0.5 ms |

Each budget ends as one of:

- **SKIPPED**: the case was filtered out or is not applicable. It does not fail the run.
- **ERROR**: the case crashed, for example because a module is missing. It fails the run.

Wall-clock budgets depend on the machine, so pin CI to one runner class. Compare JSON files from the same host, not across hosts.

## Profiling tips

- **Find the hot system first.** Run `--filter stress/people5000` and sort the per-system table by `ms/tick`. Then look at that system's `max ms`, which shows interval spikes.
- **dotnet-trace** (CPU sampling):
  ```bash
  dotnet tool install -g dotnet-trace
  dotnet build -c Release tools/GodSim.Bench
  dotnet-trace collect --profile cpu-sampling -- dotnet tools/GodSim.Bench/bin/Release/net8.0/GodSim.Bench.dll --filter people5000
  # open the .nettrace in PerfView / Visual Studio, or: dotnet-trace convert --format speedscope trace.nettrace
  ```
  Allocation hunting: `dotnet-trace collect --profile gc-verbose -- ...` gives the allocation tick events with their types and stacks.
- **dotnet-counters** (live GC and allocation-rate view during a long run):
  ```bash
  dotnet tool install -g dotnet-counters
  dotnet-counters monitor --counters System.Runtime -- dotnet tools/GodSim.Bench/bin/Release/net8.0/GodSim.Bench.dll --filter steady192
  ```
  Watch `Allocation Rate` and `% Time in GC`. At steady state the allocation rate should be close to 0.
- **JIT noise.** Each `Measure` warms up for at least 200 ms in quick mode and 500 ms in full mode so tiering and PGO can settle. Without that warmup, the pathfinder looked 15x slower. To exclude tiering completely, set `DOTNET_TieredCompilation=0` (no PGO). To approximate Unity's Mono/IL2CPP code quality more closely, also set `DOTNET_TieredPGO=0`.
- **Unity Profiler markers (suggestion for the Unity layer).** In `GodSim.Unity`, wrap each system with `static readonly ProfilerMarker` instances, created once per system name, and wrap `SimulationEngine.Step` as well. Core has no Unity references, so the markers belong in the runner: either step the systems through a thin adapter or read `Engine.Profiles` after each Step and emit `ProfilerRecorder`/custom counters. Use Deep Profile only for one-off investigation, because it distorts small methods heavily. Check GC Alloc per frame in the CPU module's Hierarchy view, where per-tick allocations show up immediately. The numbers here come from CoreCLR with dynamic PGO, which is usually faster than Mono or IL2CPP, so add some headroom to the 3 ms budget on device.
