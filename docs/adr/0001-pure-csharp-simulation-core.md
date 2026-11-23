# ADR-0001: Pure C# simulation core

- **Status:** Accepted
- **Date:** 2026-11-19
- **Deciders:** lead
- **Related:** [ENGINEERING_CONTRACT §2, §3.1](../ENGINEERING_CONTRACT.md), [ARCHITECTURE §1](../ARCHITECTURE.md#1-layers), `Assets/GodSim/Core/GodSim.Core.asmdef`

## Context

GodSim must run as a Unity 6 game, but we also need:

- fast, headless runs for balancing (hundreds of simulated years in seconds) and CI;
- unit tests that run under `dotnet test` without a Unity license or editor;
- about 50 contributors working in parallel, each able to compile and test a module alone;
- strict determinism, which UnityEngine APIs (`Random`, `Time`, `Mathf`, physics) do not give us.

Unity compiles C# 9 against `netstandard2.1`. Code that compiles in a modern .NET SDK can fail in
Unity if it uses newer language features or APIs.

## Decision

We will put all simulation logic in a pure C# assembly, **`GodSim.Core`**, with
`noEngineReferences: true`. It must compile under Unity (C# 9, `netstandard2.1`) **and** under the
dotnet SDK.

- No `UnityEngine` in Core. Core abstracts logging (`ISimLogger`), math (`MathUtil`), randomness
  (`Rng`) and coordinates (`Vec2i`) itself.
- Language and API whitelist (contract rule 1): no file-scoped namespaces, `record struct`,
  `global using`, `init`, `required`, raw string literals or `System.Text.Json`; `Nullable` off.
  Allowed namespaces: `System`, `System.Collections.Generic`, `System.Text`, `System.IO`,
  `System.Security.Cryptography`, `System.IO.Compression`. `SimulationEngine` also uses
  `System.Diagnostics.Stopwatch`, for profiling only.
- Presentation (`GodSim.Unity`) and editor code (`GodSim.Editor`) reference Core, never the other
  way round. MonoBehaviours hold no game logic.
- The same source files are compiled by Unity (asmdef), by `build/GodSim.Core.csproj` (which
  `GodSim.sln`, `tests/GodSim.Tests`, `tools/GodSim.Headless` and `tools/GodSim.Bench` reference),
  and by the per-module projects in `build/checks/`.

## Consequences

### Positive
- The simulation runs identically in Unity, the headless runner, Unity's Test Runner and
  `dotnet test`.
- Each module compiles and tests in isolation in seconds (`build/checks/<Module>`).
- Simulation code cannot accidentally depend on frame timing or engine state.
- The simulation can be reused for other front ends, such as the WebViewer snapshot pipeline or a
  server.

### Negative / costs
- We maintain our own math, logging and RNG helpers.
- There are two dotnet build flavors:
  - `build/GodSim.Core.csproj` compiles all of Core exactly as Unity does (`netstandard2.1`,
    C# 9). The solution, the full test suite and the tools build against it, so they catch both
    syntax and API drift.
  - The per-module `build/checks/*` projects compile Core sources directly into a `net8.0` test
    assembly. They catch C# 10+ **syntax** (`LangVersion 9.0`) but **not** net5+ **APIs** such as
    `ArgumentNullException.ThrowIfNull` or `Random.Shared`. Run the full build or
    `scripts/run-tests.sh` before merging.
- `Directory.Build.props` enforces `LangVersion 9.0`, `Nullable disable`, `ImplicitUsings disable`
  and `Deterministic` for every SDK project, and enables the analyzers (CA5394 flags
  `System.Random`). `tools/GodSim.Headless` overrides this with `LangVersion latest`, which is
  acceptable because tools never ship in Unity.
- Rendering has to translate Core types (`Vec2i`, `0xRRGGBBAA` colors) into Unity types
  (`GodSimCoords`, `GodSimPalette`).

### Follow-ups
- Consider pointing the module checks at a `netstandard2.1` reference build, so API drift fails
  early as well.

## Alternatives considered

### Simulation in MonoBehaviours / ECS (Unity DOTS)
Ties tests and headless runs to the editor. DOTS adds a steep learning curve and package churn, and
its determinism guarantees depend on Burst float modes. Rejected.

### Shared core as a precompiled DLL
Hides source from Unity's debugger and adds a build step for 50 contributors. Source inclusion
through asmdef is simpler. We can revisit this for modding.
