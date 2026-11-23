# Contributing to GodSim

Thanks for helping build GodSim. Read these first:

1. [`ENGINEERING_CONTRACT.md`](ENGINEERING_CONTRACT.md): the binding rules (architecture, hard rules, entry points).
2. [`ARCHITECTURE.md`](ARCHITECTURE.md): how the pieces fit.
3. [`CODING_STANDARDS.md`](CODING_STANDARDS.md): how to write the code.
4. [`SECURITY.md`](SECURITY.md): reporting vulnerabilities and secure-coding expectations (maintained by the security team).

## Prerequisites

| Tool | Version | Used for |
|---|---|---|
| .NET SDK | 8.0+ | module checks, full test suite, headless runner |
| Unity | 6 LTS (`6000.0.23f1`, see `ProjectSettings/ProjectVersion.txt`) | editor, play mode, Unity Test Runner |
| bash, grep | any | `scripts/check-unity-compat.sh` |
| Git | 2.30+ | workflow |

## Workflow

### Branching

- `main` is always green: it builds in Unity and dotnet, and every test passes.
- Work on short-lived branches cut from `main`:

| Prefix | For |
|---|---|
| `feat/<module>-<topic>` | New behavior: `feat/settlements-road-hierarchy` |
| `fix/<module>-<topic>` | Bug fixes: `fix/population-homeless-count` |
| `perf/<module>-<topic>` | Performance work with measurements |
| `content/<area>-<topic>` | Data and balancing only: `content/buildings-late-costs` |
| `docs/<topic>` | Documentation |
| `contract/<topic>` | Contract changes (lead only) |

- Rebase on `main` before opening and before merging. Keep PRs under about 600 changed lines where
  possible.
- **Ownership:** work only inside your module or sub-area folder, its tests and its
  `build/checks/<Name>` project. `Core/Contracts/`, `Core/Composition/` and `Tests/EditMode/Shared/`
  are owned by the lead.

### Pull requests

Every PR description must contain these sections (reviewers will ask for any that are missing):

```markdown
## Summary
What and why, in 2-5 lines. Link the issue / ADR.

## Module(s)
e.g. Settlements/Planning

## Determinism impact
None | Changes RNG draw count/order | Changes system order/count (stagger phase) | New iteration over unordered collection (justify)

## Save-format impact
None | New/changed WorldState field (contract) | New blackboard key(s): list them | Enum appended: which

## Security impact
None | Touches untrusted input (save/config/command/content/paths) | New validation rules

## Performance
Profile numbers before/after for affected systems (AvgMs, 5,000-pop world) or "n/a"

## Contract change requests
Fields/types you would like added to Core/Contracts (do not edit them yourself)

## Tests
What you added; output of the module check and the full suite
```

### Review checklist

Reviewers tick every line. Authors self-review against it first.

**Correctness and boundaries**
- [ ] Only files in the author's module or sub-area changed. No edits to `Core/Contracts/`, `Core/Composition/` or `Tests/EditMode/Shared/`.
- [ ] No reference to another module's types. Cross-module data goes through `WorldState`, `GameDatabase`, events or blackboards.
- [ ] Structural mutations go through `ctx.Ops`. Loops skip `!IsAlive` / `IsDestroyed`.
- [ ] `Tick` cannot throw on bad data (`null`, `-1`, dead entities, empty lists). Stored floats are sanitized or clamped.
- [ ] Rates are scaled by `ctx.DeltaTicks`. Yearly probabilities use `Rng.PerYearToPerTicks`.

**Determinism**
- [ ] Randomness only from `ctx.Rng`. No `System.Random`, `Guid`, clocks, threads or async.
- [ ] No decisions from `Dictionary`/`HashSet` iteration. Ties broken by id. Sorts are stable or keyed by id.
- [ ] RNG draw count does not depend on logging, debug flags or presentation.
- [ ] If system order or count changed (`DefaultComposition.CreateSystems()`, lead-owned): the system is appended at the end of its group where possible, golden hashes were regenerated on purpose, and the change is noted in the PR and CHANGELOG.
- [ ] A determinism test exists (same seed twice gives an equal result).

**Save format**
- [ ] Enums only appended, never reordered or removed.
- [ ] New blackboard keys are namespaced, documented in the module's `*Keys` class, cleaned up when their subject disappears, and listed in ARCHITECTURE.
- [ ] Anything that must survive save/load lives in `WorldState` or a blackboard, not in system fields.
- [ ] Contract field requests list the persistence impact.

**Security**
- [ ] Untrusted inputs are validated (`SimConfig.Sanitize`, `ICommandValidator`, `InputSanitizer`, `ContentValidator`, save loading).
- [ ] Every loop, search, retry and collection is bounded. No unbounded growth of logs or keys.
- [ ] No file or network access outside Persistence's `SaveFileStore` and the tools.
- [ ] No secrets, keys or personal data in code, tests or logs.

**Performance**
- [ ] No LINQ, closures or per-tick allocations in hot paths. Buffers are reused.
- [ ] Heavy work runs on an interval or a window. Profile numbers are included for hot systems.

**Unity compatibility and style**
- [ ] `scripts/check-unity-compat.sh` passes. C# 9 / netstandard2.1 only.
- [ ] XML docs on public APIs. Naming per CODING_STANDARDS.
- [ ] Tests use the NUnit basic API only.

### Merging

- At least one approval from a module owner, plus the lead for contract, composition, save-format
  or system-order changes.
- Squash-merge with a Conventional Commit title (see below).
- Add a line to `CHANGELOG.md` under **[Unreleased]** for user-visible or cross-module changes.

## Running the checks

### 1. Isolated module check (every PR)

Each module or sub-area has a dotnet test project that compiles **only** Contracts, the shared test
fixtures, your module folder and your test folder. That way you are never blocked by other modules
being incomplete.

First-time setup for a new module or sub-area. The script generates the project from
`build/checks/_template`:

```bash
scripts/new-module-check.sh <Name> <CorePath> [TestsPath]
# e.g. scripts/new-module-check.sh SettlementsPlanning Settlements/Planning
```

Or copy the template by hand:

```bash
mkdir -p build/checks/<Name>
cp build/checks/_template/Template.Tests.csproj build/checks/<Name>/<Name>.Tests.csproj
# then edit the two "your module" Compile globs, e.g.
#   ../../../Assets/GodSim/Core/Economy/Services/**/*.cs
#   ../../../Assets/GodSim/Tests/EditMode/Economy/Services/**/*.cs
```

Run it:

```bash
cd build/checks/<Name> && dotnet test -nologo -v q --disable-build-servers
```

It must pass with **0 errors** (warnings are reviewed). There are about 40 check projects in
`build/checks/`. Examples: `AIGovernor`, `AIStrategy`, `BuildingsEarly`, `DiplomacyMilitary`,
`DivineNature`, `EconomyServices`, `PersistenceFormat`, `PopulationSociety`, `Security`,
`SettlementsConstruction`, `UnityTerrain`, `World`. Do not build other teams' check folders while
they are mid-work.

The template compiles your sources into a `net8.0` test assembly with `LangVersion 9.0`, and
`CS1591` (missing XML docs) is suppressed. That catches C# 10+ **syntax** but not net5+ **APIs**.
The full build below catches both, because `build/GodSim.Core.csproj` targets `netstandard2.1`.

### 2. Full test suite

```bash
dotnet test                                 # repo root: builds GodSim.sln, runs tests/GodSim.Tests
scripts/run-tests.sh                        # the CI equivalent: compat check -> build -> test -> headless smoke
scripts/run-tests.sh -c Debug --no-smoke    # options: -c Debug|Release, --skip-compat, --no-smoke, --years N, --seed N
```

`GodSim.sln` contains:

- `build/GodSim.Core.csproj`: all of Core, `netstandard2.1`, C# 9, exactly what Unity compiles;
- `tests/GodSim.Tests`;
- `tools/GodSim.Headless`;
- `tools/GodSim.Bench`.

`tests/GodSim.Tests` compiles every `Assets/GodSim/Tests/EditMode/**` file plus the integration
suites in `tests/GodSim.Tests/Integration/`:

- `DeterminismTests`
- `SaveLoadContinuityTests`
- `GodPowerIntegrationTests`
- `AgeProgressionSmokeTests`
- `SanityRunTests`
- `ConfigSanitizeTests`

Integration runs use `SimConfig.StrictMode = true`, so a faulting system fails the test instead of
being isolated.

In Unity, open **Window > General > Test Runner > EditMode > Run All**. The same EditMode files run
there. Also try **GodSim > Determinism Check** and **GodSim > Validate Content**.

### 3. Unity compatibility script

```bash
scripts/check-unity-compat.sh                         # default: scan Assets/GodSim/Core + validate asmdefs
scripts/check-unity-compat.sh --warnings-as-errors -q # strict, violations only
scripts/test-check-unity-compat.sh                    # self-test of the scanner
```

The scanner strips comments and blanks string contents before matching, so prose such as "never
use System.Random" in a comment does not trigger it.

| Severity | Rules |
|---|---|
| Error | `UNITY_ENGINE` (UnityEngine/UnityEditor in Core), `SYSTEM_RANDOM`, `WALL_CLOCK` (`DateTime.Now/UtcNow/Today`), `TICK_COUNT`, `GUID_NEWGUID`, `THREADING` (threads, tasks, async, `Parallel`), `SYSTEM_TEXT_JSON`, `FILE_SCOPED_NAMESPACE`, `RECORD`, `INIT_ACCESSOR`, `GLOBAL_USING`, `RAW_STRING_LITERAL`, `PARAM_NULL_CHECK` (`!!`), `REQUIRED_MEMBER` |
| Warning | `STRING_HASH` (`string.GetHashCode`) |
| Asmdef | `GodSim.Core.asmdef` must keep `noEngineReferences`, and asmdef JSON must be valid |

Exit codes: `0` clean, `1` violations (printed as file:line), `2` usage or environment error. A
reviewed exception is suppressed on its own line with a trailing comment:
`// godsim-compat: allow WALL_CLOCK <reason>`. Reviewers must agree with the reason.

The script does **not** check for net5+ APIs. The full `dotnet test` build against
`netstandard2.1` catches those.

### 4. Headless smoke run

```bash
dotnet run --project tools/GodSim.Headless -- --years 200 --out snapshot.json
```

This simulates 200 years and writes a JSON snapshot. Other options:

- `--seed`, `--width`, `--height`, `--preset`, `--races`
- `--save` / `--load`
- `--hash` (print the state hash)
- `--strict`, `--benchmark`, `--history-interval`, `--powers-demo`
- `--ascii`, `--quiet`, `--help`

To inspect the map, settlements, history and chronicle, open `tools/WebViewer/index.html` (a
self-contained file that needs no build or network) and load the snapshot. See
`tools/WebViewer/README.md`.

## Profiling

**Budget:** under 3 ms per tick on average for a 5,000-person world, across all systems.

### Benchmarks and budgets (preferred for numbers)

```bash
dotnet run -c Release --project tools/GodSim.Bench -- --list              # list benchmarks
dotnet run -c Release --project tools/GodSim.Bench -- --budget            # run all; exit 1 if a budget fails
dotnet run -c Release --project tools/GodSim.Bench -- --quick --filter stress --json bench.json
```

The CI budgets in `tools/GodSim.Bench/Harness/Budgets.cs` are:

- `stress/people5000` under 3 ms per tick (mean);
- `EventBus.Publish` with 0 and 5 subscribers allocates 0 B;
- a 128-tile pathfinder query under 0.5 ms (median), on open ground and with 22% obstacles.

### Per-system profile

`SimulationEngine.Profiles` (also `GodSimulation.Engine.Profiles`) exposes, per system: `LastMs`,
`AvgMs` (EMA, α = 0.05), `Runs`, the fault counters and `Disabled`. `LastStepMs` covers the whole
step.

1. Run a long simulation so the population reaches the target size (200+ years), with
   `dotnet run -c Release --project tools/GodSim.Headless -- --years 200 --benchmark`, or in a
   test.
2. Dump `Profiles` (see the snippet in
   [ARCHITECTURE §12](ARCHITECTURE.md#12-performance-budgets-and-profiling)).
3. Compare **per-tick cost**, which is `AvgMs / TickInterval`. `AvgMs` is measured per run, so a
   24-tick system at 2 ms averages about 0.08 ms per tick but spikes to 2 ms on the tick it runs.
4. Report Release-build numbers only, and state the machine and the population.

For deeper analysis use
`dotnet-trace collect --format speedscope -- dotnet run -c Release --project tools/GodSim.Headless -- --years 200`
or `dotnet-counters monitor`. GC allocations per second should stay flat in steady state.

### In Unity

- Press **`F3`** in play mode for the performance overlay: FPS, sim ms per tick, per-system
  timings and faults, and entity counts.
- **Window > Analysis > Profiler**, CPU module. Look at `GodSimRunner.Update` and the GC Alloc
  column, which should read 0 B per frame in steady state.
- `GodSimRunner.FrameBudgetMs` (default 8 ms) caps the simulation time per frame. At high speed
  (up to 240 ticks per second) the runner falls behind instead of dropping frames. Check
  `LastFrameSteps`/`LastFrameStepMs` and `Profiles` before blaming rendering.
- Deep Profile is very slow with this many calls. Prefer `Profiler.BeginSample` in Unity-side code
  only (never in Core).

### Performance PR evidence

Include a before/after table:

```text
system                     interval  AvgMs(before)  AvgMs(after)  per-tick(after)
Population.Housing         4         0.420          0.180         0.045
```

## Commit messages

We use [Conventional Commits](https://www.conventionalcommits.org/) with the module as scope:

```text
<type>(<scope>): <imperative summary, <= 72 chars>

<body: what and why, wrapped at 100>

<footer: Refs #123, BREAKING CHANGE: ..., Save-Format: ..., Determinism: ...>
```

| Type | Use |
|---|---|
| `feat` | New behavior |
| `fix` | Bug fix |
| `perf` | Performance with no behavior change |
| `refactor` | Restructure with no behavior change |
| `content` | Definitions and balancing data |
| `test` | Tests only |
| `docs` | Documentation |
| `build` / `ci` | Build projects, scripts, CI |
| `chore` | Everything else |

Scopes: `contracts`, `world`, `geography`, `races`, `progression`, `buildings`, `settlements`,
`population`, `economy`, `ai`, `diplomacy`, `divine`, `religion`, `heroes`, `storytelling`, `history`,
`objectives`, `modding`, `persistence`, `security`, `composition`, `unity`, `editor`, `headless`,
`bench`, `savetool`, `webviewer`, `scripts`, `docs`.

Footers:

- `Determinism: changes RNG draw order in Population.Births` for any determinism-surface change.
- `Save-Format: appends GodPowerType.X` for any save impact.
- `BREAKING CHANGE:` for contract or API breaks.

Examples:

```text
feat(settlements): prefer riverside sites for mills
fix(population): stop double-counting homeless after house destruction
perf(economy): cache per-settlement storage buildings between runs
content(buildings): rebalance Iron age military costs
```

## Reporting issues

Include the seed, `SimConfig` (or the headless command line), the tick or year, expected vs actual
behavior, and if possible a save file or snapshot. Report security issues privately as described in
[`SECURITY.md`](SECURITY.md), not in public issues.
