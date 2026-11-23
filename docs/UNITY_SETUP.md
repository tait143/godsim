# GodSim — Unity Setup

How to open, run, test and build GodSim in Unity. For architecture rules, see
[ENGINEERING_CONTRACT.md](ENGINEERING_CONTRACT.md). For keys, see [CONTROLS.md](CONTROLS.md).
Checked against the project on disk on 2026-11-19.

## 1. Requirements

| Item | Requirement |
|---|---|
| Unity | **Unity 6 LTS.** The project is saved with `6000.0.23f1` (`ProjectSettings/ProjectVersion.txt`). **2022.3 LTS** is supported with one package change (§1.1) |
| Render pipeline | Built-in Render Pipeline out of the box. URP through a shader fallback chain (§5) |
| Packages (`Packages/manifest.json`) | `com.unity.inputsystem` 1.11.2, `com.unity.test-framework` 1.4.5, `com.unity.ugui` 2.0.0, plus the built-in modules (animation, audio, IMGUI, JSON serialize, particle system, physics, UI, UI Elements) |
| OS (Editor) | Windows 10/11, macOS 12+, Ubuntu 22.04+ |
| Hardware | A DX11, Metal or Vulkan GPU. 8 GB RAM minimum, 16 GB for large worlds |
| Optional | .NET SDK 8 for building and testing Core outside Unity (`build/`, `tests/`, `tools/`) |

### 1.1 Running on 2022.3 LTS

`GodSim.Core` is pure C# 9 on `netstandard2.1` with no engine references, so it compiles unchanged.

1. In `Packages/manifest.json`, change `"com.unity.ugui": "2.0.0"` to `"1.0.0"`. UGUI 2.0 ships
   only with Unity 6. The HUD is IMGUI, so nothing else depends on the version.
2. Open the project in 2022.3 and accept the version prompt. Don't commit the rewritten
   `ProjectVersion.txt` unless the team is switching versions.
3. Keep Input System 1.11.x and Test Framework 1.4.x. Both support 2022.3.

## 2. Opening the project

1. Clone the repository. **The repository root is the Unity project root** (`Assets/`, `Packages/`,
   `ProjectSettings/`).
2. In Unity Hub: **Add → Add project from disk** → choose the repository root. Open it with Unity 6
   LTS. The first import compiles four assemblies.
3. If Unity asks to enable the new Input System backends, choose **Yes**. Then set **Edit → Project
   Settings → Player → Active Input Handling = Both** (§6).

## 3. First run

1. Run **GodSim → Create Demo Scene**. It does the following:
   - Creates or loads the settings asset **`Assets/GodSim/Settings/GodSimSettings.asset`**. That is
     a `GodSimSettings` ScriptableObject that mirrors `SimConfig` plus render options such as
     height scale, territory display and `MaxVisiblePeople` = 6000.
   - Adds the layers `Terrain` and `Buildings` if they are missing.
   - Builds **`Assets/GodSim/Scenes/GodSimDemo.unity`**, asking before it overwrites:
     - a directional light
     - a camera with `GodCameraController`
     - a `GodSim` root with `GodSimRunner`, `TerrainRenderer`, `BuildingRenderer`,
       `PeopleRenderer`, `ArmyRenderer`, `SettlementLabels`, `GodPowerVFX`, `FireVFX`,
       `WeatherVFX`, `DayNightCycle`, `SelectionController`, `GodPowerController`, `GameShortcuts`
       and `GodSimHUD`, plus `GodSimAudio` if it exists.

     Missing component types are skipped with a warning in the report.
2. Press **Play**. The runner creates a world from the settings: 192×192 Continents, all five
   races, seed `20261119` by default. The tutorial script and content exist in
   `Unity/Runtime/Tutorial`, but no scene component drives them yet **(design target: a
   TutorialPresenter on the root)**.
3. To change the seed, map, races or difficulty, edit the settings asset, or the runner's
   Inspector (custom editor `GodSimRunnerEditor`: Slower and Faster buttons, live stats). Values
   are sanitized (`SimConfig.Sanitize`), so the Inspector can't corrupt the sim.

Other menu items:

| Menu | What it does |
|---|---|
| **GodSim → Create Settings Asset** | Creates or pings the settings asset |
| **GodSim → World Preview** | Editor window that previews world generation for a seed and preset without entering Play |
| **GodSim → Simulate 100 Years (Headless)** | Runs the Core sim in the Editor with a progress bar and reports the timings |
| **GodSim → Validate Content** | Builds the `GameDatabase` and runs `Security.ContentValidator` |
| **GodSim → Determinism Check** | Runs the same seed twice for 20 years and compares the state hashes |

> **Known issue:** the HUD and `GameShortcuts` both handle Space, +/− and the power digits, so a
> press can apply twice. Untick *Handle Time Hotkeys* and *Handle Power Hotkeys* on `GodSimHUD`
> until it's fixed (CONTROLS.md).

> If the **GodSim** menu is missing, the Editor assembly failed to compile. Fix the first Console
> error.

## 4. Assembly layout

| Assembly | Path | References | Notes |
|---|---|---|---|
| `GodSim.Core` | `Assets/GodSim/Core/` | — | `noEngineReferences: true`. Deterministic, C# 9 / `netstandard2.1`. Also built by `build/GodSim.Core.csproj` (dotnet, `Deterministic=true`) |
| `GodSim.Unity` | `Assets/GodSim/Unity/` | `GodSim.Core`, `Unity.InputSystem` | Runtime MonoBehaviours: runner, terrain and buildings, units, VFX, audio, HUD, tutorial, overlays. Version define `ENABLE_INPUT_SYSTEM_PACKAGE` when the package is present. Shaders live in `Unity/Shaders/` |
| `GodSim.Editor` | `Assets/GodSim/Editor/` | `GodSim.Core`, `GodSim.Unity` | Editor only (root namespace `GodSim.Unity.Editor`): menu, runner inspector, world preview |
| `GodSim.Tests.EditMode` | `Assets/GodSim/Tests/EditMode/` | `GodSim.Core`, `nunit.framework.dll` | Editor only, constrained by `UNITY_INCLUDE_TESTS`. The same files compile in dotnet |

**Rules that keep it working:**

- Never add `using UnityEngine;` under `Core/`.
- Core never references Unity. Unity reads `WorldState` and `EventBus` events and only writes back
  through `GodSimulation.TryCastPower` (→ `CommandQueue`).
- Do all coordinate conversion in `Unity/Runtime/Core/GodSimCoords.cs`. 1 tile = 1 world unit, and
  tile (x, y) maps to world (x, height, y).
- Core logs reach the Console through `UnityLogSink` (`ISimLogger`).

## 5. Render pipeline notes

`MaterialLibrary` creates every material at runtime and picks shaders in this order:

| Material | Built-in RP | URP or other SRP |
|---|---|---|
| Vertex-color lit (terrain, merged buildings, instanced people) | `GodSim/VertexColorLit` (`Unity/Shaders/GodSimVertexColorLit.shader`) | `Shader Graphs/GodSimVertexColorLit` if you authored one → `Universal Render Pipeline/Simple Lit` → `Standard` |
| Water | `GodSim/Water` (`Unity/Shaders/GodSimWater.shader`) | `Shader Graphs/GodSimWater` → transparent Simple Lit tinted blue |
| Flat markers and overlays | `Unlit/Color` / `Sprites/Default` | `Universal Render Pipeline/Unlit` |
| Last resort | `Hidden/InternalErrorShader` (never null) | the same |

- **Built-in RP.** It works out of the box.
- **URP.** The built-in GodSim shaders are skipped, because their ForwardBase passes don't render
  under an SRP. **Simple Lit ignores vertex colors**, so the world renders flat-tinted until you add
  the Shader Graph. To convert:
  1. Install **Universal RP**: 17.x for Unity 6, 14.x for 2022.3.
  2. **Assets → Create → Rendering → URP Asset (with Universal Renderer)**. Assign it in **Project
     Settings → Graphics** and in every **Quality** level.
  3. Create a Lit Shader Graph named **`GodSimVertexColorLit`**: a *Vertex Color* node × a `_Color`
     property into Base Color, with GPU instancing enabled. Optionally add a transparent
     **`GodSimWater`** graph.
  4. Runtime materials pick the graphs up automatically. The Render Pipeline Converter is only
     needed for assets you added yourself.
- **Player builds.** `Shader.Find` only finds shaders that are included in the build. Add
  `GodSim/VertexColorLit` and `GodSim/Water` (or your graphs) to **Project Settings → Graphics →
  Always Included Shaders**, or reference them from a material under `Resources/`.
- **HDRP.** Not supported yet (ROADMAP Beta).

## 6. Input System

`InputAdapter` is the only place that reads input. It is allocation-free and returns neutral
values when a device is missing.

- When **`ENABLE_INPUT_SYSTEM`** is defined (Active Input Handling = *Input System Package* or
  *Both*), keys map from `KeyCode` to `Keyboard.current`, and the mouse is read from
  `Mouse.current`. Scroll is normalized from the platform's 120-per-notch units.
- Otherwise it uses the legacy `UnityEngine.Input`.

| Active Input Handling | Result |
|---|---|
| **Both** (recommended) | New Input System path |
| Input System Package (New) | New Input System path. Legacy `Input.*` would throw, which is why everything goes through `InputAdapter` |
| Input Manager (Old) | Legacy path |

Changing this setting restarts the Editor. The IMGUI HUD reads `Event.current` keyboard events,
which work under every setting.

## 7. Running tests

### 7.1 Unity Test Runner

1. **Window → General → Test Runner → EditMode → Run All**.
2. Tests live in `Assets/GodSim/Tests/EditMode/<Module>/`, in the namespace
   `GodSim.Tests.<Module>`. They use only basic NUnit 3, build tiny worlds by hand, and need no
   scene.

If no tests appear, check that the Test Framework package is installed and that
`GodSim.Tests.EditMode.asmdef` is unchanged. It is Editor-only, `UNITY_INCLUDE_TESTS`,
`overrideReferences` with `nunit.framework.dll`.

### 7.2 Outside Unity (CI)

```bash
# one module in isolation (what each module owner runs)
cd build/checks/<Module> && dotnet test -nologo -v q --disable-build-servers

# full integration tests (sanity runs, scenarios)
dotnet test tests/GodSim.Tests -nologo

# headless simulation / benchmarks
dotnet run -c Release --project tools/GodSim.Headless -- --years 200 --hash
dotnet run -c Release --project tools/GodSim.Bench -- --quick --budget
```

Unity and dotnet compile the same source files. A test that passes in one passes in the other.

## 8. Building a player

1. **File → Build Profiles** (Unity 6) or **Build Settings** (2022.3). Add
   `Assets/GodSim/Scenes/GodSimDemo.unity` and pick the platform.
2. Recommended Player Settings:

| Setting | Value |
|---|---|
| Scripting Backend | **IL2CPP** for release, Mono for iteration |
| API Compatibility | .NET Standard 2.1 |
| Managed Stripping | Low. If you raise it, add a `link.xml` that preserves `GodSim.Core` |
| Active Input Handling | Both |
| Color Space | Linear |
| Always Included Shaders | The GodSim shaders (§5) |

3. Saves (`SaveFileStore`: versioned binary, HMAC integrity, backups) and replays go under
   `Application.persistentDataPath`.

**Determinism.** Saves, replays and state hashes are bit-exact within one build on one platform.
Treat cross-backend (Mono and IL2CPP) and cross-CPU (x64 and ARM64) determinism as unproven until
the replay-hash test passes on each target (ROADMAP R1).

## 9. Performance tips

| Budget | Value |
|---|---|
| Simulation | Under 3 ms per tick on average with 5,000 people (contract). Bench case `stress/people5000`. Measured on 2026-11-19: about 0.4–0.6 ms at 300–3,000 people, about 2 ms at 7,000, about 6–9 ms at the 40,000 cap |
| Frame | 60 FPS on a GTX 1060-class GPU at 192² |

- **F3 overlay.** Per-system `AvgMs` and faults come from `SimulationEngine.Profiles`. A system
  disabled after 25 consecutive faults (`MaxSystemFaults`) shows its `LastError`.
- **Runner.**
  - Speeds are 1, 4, 16, 64 and 240 ticks/s.
  - `FrameBudgetMs` (default 8) caps the sim time per frame, and the backlog is capped at 2 s. At
    240 ticks/s on big worlds, the sim will lag real time instead of freezing input.
  - Lower `FrameBudgetMs` on weak machines.
- **Rendering.**
  - Terrain is chunked (`TerrainChunk`). Rebuild only on `TerrainChangedEvent`.
  - People and armies are GPU-instanced (`InstanceBuffer`) and capped by `MaxVisiblePeople`.
  - The HUD rebuilds text at 4 Hz and caches every `GUIContent`. Don't format strings in `OnGUI`.
- **Iteration.** Under **Enter Play Mode Options**, turn off *Reload Domain*. Core keeps no static
  mutable state, and `MaterialLibrary` re-creates destroyed materials.
- **Large worlds** (512²+). Lower `MaxPopulation`. Watch the 40,000-person cap: runaway growth
  reaches it by about year 100 today (BALANCE I-3).

## 10. Troubleshooting

| Symptom | Cause and fix |
|---|---|
| `UnityEngine` not found in `Core/` | Engine code was added to Core. Move it to `Unity/` |
| `InvalidOperationException … UnityEngine.Input class …` | Active Input Handling is *New* only, and some code bypassed `InputAdapter`. Route it through `InputAdapter`, or set the handling to **Both** |
| Package Manager error on `com.unity.ugui@2.0.0` in 2022.3 | Downgrade it to 1.0.0 (§1.1) |
| The **GodSim** menu is missing | `GodSim.Editor` failed to compile. Fix the first Console error |
| Pink, or flat untextured, world under URP | No Shader Graph. Create `GodSimVertexColorLit` (§5) |
| Pink world in a **player** build only | The GodSim shaders were stripped. Add them to Always Included Shaders |
| `SimValidationException: GameDatabase invalid` on Play | A catalog registered bad data, for example a House that isn't 3–5. The message lists every problem. Run **GodSim → Validate Content** |
| Compile errors in `Core/Composition/DefaultComposition.cs` (a missing `…System`) | A module that Composition wires was mid-change while you pulled (this happened on 2026-11-19 for Religion, Storytelling and Integrity). Pull again. Don't comment out wiring in commits |
| Space or number keys seem to do nothing | The double hotkey handling (§3, known issue) |
| The world freezes but the UI works | A system was disabled after repeated faults. Check F3. Set `StrictMode` in the settings to break on the first exception |
| No tests in the Test Runner | The Test Framework is missing, or the asmdef was edited (§7.1) |
| Different results on two machines with the same seed | Different builds or backends. Run **GodSim → Determinism Check** on each machine (ROADMAP R1) |
