# GodSim — Roadmap

Milestones run from the vertical slice to 1.0 and on to live updates. Every milestone has measurable
exit criteria, checked mostly by the headless runner (`tools/GodSim.Headless`), the bench
(`tools/GodSim.Bench`) and the EditMode and integration tests. Dates are relative: M+n = n months
after the contracts froze.

Status reconciled with the code on 2026-11-19. In the tables, ✅ = on disk and compiling, 🟡 = on
disk but incomplete or failing a gate, ⬜ = not started.

## Milestone overview

| Milestone | Target | Theme | Exit gate |
|---|---|---|---|
| M0 Foundations | **done** | Contracts, deterministic engine, module skeletons, isolated builds | `build/checks/Contracts` compiles. The modules have checks |
| **M1 Vertical slice** | M+2 | One complete, readable loop from Dawn to Iron for all 5 races | §M1 exit criteria |
| **M2 Alpha** | M+5 | All 10 ages, 75 buildings, 19 powers, war, religion, objectives, replay | Feature-complete. A 1,500-year soak with no faults, inside the pacing bands |
| **M3 Beta** | M+8 | Balance, art per race and age, URP/HDRP materials, UX, accessibility, data packs v1, localization groundwork | BALANCE bands met over a 50-seed sweep. Usability targets met |
| **M4 1.0** | M+11 | Steam, localization, performance, stability | Certification checklist. ≥ 99.5% crash-free in the external beta |
| Post-1.0 | M+12 → | Multiplayer lockstep, naval units, more races, Workshop | Separate plans |

---

## M1 — Vertical slice (Dawn → Iron)

**Goal.** Prove the four pillars on a small scope. A first-time player watches camps become Iron-age
towns, understands why the AI builds what it builds, and changes history with a handful of powers.

| Area | Scope | Status |
|---|---|---|
| World | Generator (4 presets), rivers, deposits, start finder, World Preview window | ✅ |
| Races | 5 race definitions, lore, names, architecture styles | ✅ |
| Progression | Ages, techs, research, age advancement, modifiers | ✅ data. 🟡 pacing (BALANCE I-2) |
| Buildings | All 75 definitions | ✅ |
| Settlements | Planner, zoning, roads, construction, tiers, renovation, maintenance | ✅. 🟡 producer over-building (I-6) |
| Population | Lifecycle, families, housing (3–5), jobs, wellbeing, migration, leadership | ✅. 🟡 runaway or trapped growth (I-3, I-4), disease share (I-8) |
| Economy | Production, services, storage, upkeep, trade, regrowth, stats | ✅. 🟡 service input gating (I-7) |
| AI | Civ strategy, research director, expansion, settlement governor | ✅. 🟡 the Gate is never placed (I-1, not M1-critical) |
| Divine | 19 powers, faith and mana, seasons and weather, disasters, fire | ✅. 🟡 regional weather (I-11), age gating (I-12) |
| Persistence | Versioned binary saves, HMAC, safe file store, state hasher, replay log | ✅ |
| Unity | Runner, terrain, buildings, people and army instancing, camera, selection, power targeting, HUD (top bar, civ panel, inspector, power bar, chronicle, minimap, F1, F3), VFX, audio, day and night | ✅. 🟡 hotkey double-handling (C14) |
| Unity | Tutorial presenter, overlay renderer | 🟡 data on disk, presenter not wired |
| Composition | `DefaultComposition` wiring every module, including Religion, Heroes, Storytelling, History, Objectives and Integrity | ✅ the full Core compiled under dotnet late on 2026-11-19, after Religion, Storytelling and Integrity landed |

**Exit criteria:**

- [ ] `DefaultComposition` compiles with every module. `tests/GodSim.Tests` passes.
- [ ] **Pacing:** with all 5 races on seeds 1–10 at Standard, the first civ enters Iron in years
  170–260. No civ reaches Classical before year 250 (BALANCE §2.1).
- [ ] **No trapped races:** every race reaches Bronze by year 150 in ≥ 8 of 10 seeds.
- [ ] **Dwelling rule holds:** median houses per person stays 0.20–0.33 and homeless stays below 5%
  through year 300.
- [ ] **Performance:** the Bench budget `stress/people5000` averages under 3 ms per tick. p95 is
  under 6 ms.
- [ ] **Determinism:** the same seed and command log give identical `StateHasher` hashes at ticks
  1k, 10k and 50k. Save, load and continue matches the hash of an uninterrupted run.
- [ ] **Input:** every binding in CONTROLS.md works in the demo scene, with no double handling.
- [ ] **Playtest:** 4 of 5 new players can explain the reason behind one AI build.

## M2 — Alpha (feature complete)

- **Content and systems:**
  - All 10 ages playable. The Ascension Gate is placed and completed by the AI (fix I-1). Full
    **civ transcendence**: the people leave, the cities become luminous ruins, and the per-civ flag
    is set (GDD §14.1).
  - All 19 powers with `MinWorldAge` gating (I-12) and **regional weather** for Rain and Drought
    (I-11).
  - Diplomacy and war tuned (BALANCE §7): truce length, war weariness, home-terrain defense.
  - Religion (`ReligionSystem`, doctrines, reformations, holy wars), heroes and
    storytelling/world events (`WorldEventSystem`) integrated and chronicled.
  - Objectives (61) shown in the HUD with progress and rewards.
  - Rebellion and secession of high-unrest cities (`Storytelling/Secession`, on disk) tuned and
    chronicled.
- **Replays.**
  - `ReplayLog` records accepted commands with hash checkpoints.
  - A replay player feeds them back with `Source = "replay"` and reports the first divergence.
- **Headless tooling** (BALANCE §9.1 requests):
  - `--config Key=Value`
  - `--seeds a..b` batch runs
  - `--metrics out.csv`
  - an age-entry event log.

  These are the foundation of the balance pipeline. The WebViewer gets age and population graphs.
- **Exit criteria:**
  - [ ] A 1,500-year soak on 10 seeds: 0 disabled systems and 0 NaN or Infinity
    (`StateValidator`).
  - [ ] Every age reached by some civ in ≥ 8 of 10 seeds, inside the BALANCE §2.1 bands.
  - [ ] A replay of a 2-hour session reproduces the final hash.
  - [ ] `ContentValidator.Validate(db)` reports 0 issues.

## M3 — Beta (content-complete polish)

- **Balance.** All BALANCE.md bands met over a 50-seed sweep. No race takes the first Ascension in
  more than 40% of seeds. Disease is 5–15% of deaths. Mana is not capped out after Industrial.
- **Visuals.**
  - Per-race × per-age architecture for every `BuildingShape`, with grayscale silhouette tests.
  - **URP materials:** a `GodSimVertexColorLit` Shader Graph, splat-blended terrain, water,
    seasonal foliage.
  - **HDRP variants** for trailers.
  - The overlay renderer for all 14 `OverlayMode`s.
- **UX.**
  - The tutorial presenter (14 steps on disk).
  - Chronicle aggregation ("12 births in Oakvale this year").
  - The end-of-history summary, and overlay and minimap pings.
- **Accessibility.** Remapping, UI scale (done), a colorblind palette, reduced flash and shake, a
  toggle for hold-to-paint, and mouse-only play.
- **Modding: data packs v1** (`Core/Modding` groundwork is on disk: `SafeJson` with `JsonLimits`,
  `ModManifest`, `SemVersion`, `ModHardCaps`, `ModPack`).
  - A pack is `mod.json` plus `data/*.json` patch documents, applied **before**
    `GameDatabase.Freeze()`.
  - Validation: `ContentValidator` and `GameDatabase.Validate()`, plus hard caps on sizes, counts
    and numeric ranges.
  - **No code execution.** Strings are sanitized (`InputSanitizer`).
  - Pack ids and versions are hashed into the save header, so a save records its exact content.
- **Localization groundwork.** Every UI string goes through a string table. Chronicle and lore
  templates are keyed and take parameters. Race lore and doctrine names are moved into tables.
- **Telemetry** (local, opt-in) and the bug-bundle export (GDD §19).
- **Exit criteria:**
  - [ ] 80% of testers finish the first 10 minutes without help.
  - [ ] Balance bands met.
  - [ ] Average sim tick < 3 ms at 5k people. p99 frame time < 25 ms on min spec.

## M4 — 1.0

- **Steam integration** behind a `GODSIM_STEAM` define, in a separate assembly, so Core stays
  engine- and SDK-free:
  - **Achievements** mapped from the objectives: Ascension, Merciful, Wrathful, Peacekeeper,
    Eternal Watch, Seven Wonders, Phoenix.
  - **Cloud saves**, verified with HMAC on load.
  - **Rich presence** ("Watching the Medieval Age, Year 612").
  - **Steam Workshop** for data packs. It goes through the same validation path as local mods.
- **Localization.** EN, FR, DE, ES, PT-BR, RU, ZH-Hans, JA and KO. Pseudo-localization for text
  expansion. CJK font fallback in `HudSkin`.
- **Performance.**
  - 10k-person worlds at 60 FPS on recommended spec. Core stays single-threaded.
  - Rendering may use Jobs and Burst in `GodSim.Unity`.
- **Stability.** Save migration (`SaveMigrator`, `WorldState.SchemaVersion`), corrupt-save recovery
  (`SaveFileStore` backups) and crash bundles.
- **Content.** The full chronicle template set, endings for each alignment, and the end-of-history
  screen.
- **Exit criteria:**
  - [ ] Crash-free sessions ≥ 99.5%.
  - [ ] 0 known save-corruption bugs.
  - [ ] Every string localized.
  - [ ] Steam checklist passed.

---

## Post-1.0 themes

| Theme | Scope |
|---|---|
| **Multiplayer-ready lockstep** | Several gods in one world. The deterministic core already isolates player input as `GodCommand`s with a `Sequence`, a `Source` (`"network:<peer>"`) and a per-source rate limiter. Peers exchange only commands. Each one simulates with an input delay of about 4–8 ticks, compares `StateHasher.Compute` every N ticks, and keeps `ReplayLog` for desync forensics. Needs R1 solved. Adds a per-god `GodState` (a contract change: one mana pool per player) and contested devotion |
| **Naval units** | Ships from Harbor and docks, naval move costs on water tiles, transports for cross-sea invasion, fishing fleets. It makes Archipelago maps strategic, and gives the Colossus and Harbor their purpose |
| **More races** | Append to `RaceId` and `RaceMask` (never reorder; saves store numbers). Candidates: Gnomes (Industrial tinkerers), Merfolk (needs naval), Undead (no births, they convert the dead). Each needs a race file, lore, an architecture style, a unique wonder, racial techs and AI tuning |
| **Advanced modding** | Data packs v2: art packs (meshes and textures by key), chronicle template packs, doctrine packs. Still no code |
| **Rendering** | Full URP/HDRP material libraries, seasonal terrain, weather VFX tiers |
| **God interaction** | Omens and commandments (GDD Q5), rival AI gods |
| **Gamepad and Steam Deck** | A radial power wheel, cursor snapping, a verified Deck profile |

---

## Risk register

L = likelihood, I = impact (1–5). Score = L × I. Sorted by score.

| ID | Risk | L | I | Score | Owner | Mitigation | Trigger / signal |
|---|---|---|---|---|---|---|---|
| R13 | **Balance collapse.** Pacing is about 20× too fast. One race snowballs to the 40k cap while others are trapped. The endgame Gate is never built | 5 | 5 | **25** | Design with Progression, Population and AI | BALANCE §5.2 and §10 fixes. A headless sweep gate in CI (pacing bands), owned by design | First Ascension before year 900, or any race below Bronze at year 150 |
| R1 | **Cross-platform float determinism.** Lockstep and replays desync across x64 and ARM, or Mono and IL2CPP | 4 | 5 | 20 | Lead engineer | A cross-platform replay-hash CI lane. Avoid transcendental math in decision paths. Consider fixed-point for combat and economy hot paths. Quantize at save boundaries | Hash mismatch in the cross-platform replay test |
| R3 | **Parallel module integration.** About 50 contributors drift in semantics. Composition references modules that don't exist yet | 4 | 4 | 16 | Lead | The contract, a Composition-owned integration test, a headless smoke test per merge, the GDD §20.3 inconsistency list | `DefaultComposition` fails to compile. Integration soak faults |
| R2 | **Performance at 5k+ people** | 3 | 5 | 15 | Systems engineer | Interval and staggered systems, zero-allocation loops, the Bench `--budget` gate. Measured: about 0.5 ms at 3k, about 6–9 ms at 40k | Average > 3 ms at 5k |
| R11 | **Art scope.** 75 buildings × 5 races × 10 ages | 5 | 3 | 15 | Production | Procedural meshes (`BuildingShape` × `ArchitectureStyle` × age notes). Hand-made art only for wonders | Art backlog burn-down |
| R4 | **Passive feel** | 3 | 4 | 12 | Design | Objectives, doctrines reacting to the god, a tighter mana loop (fix the late flood), the tutorial | Telemetry: < 1 power per 5 minutes |
| R5 | **Unreadable towns** at scale | 3 | 4 | 12 | Art and design | District rules, silhouette tests, overlays. Fix producer over-building | Pillar test failures |
| R6 | **Building cap vs the dwelling rule** (600 cap, Megalopolis at 1,200+) | 4 | 3 | 12 | Design | Decide GDD Q1 by Alpha: a dense dwelling type, or a cap that grows with tier | A settlement stalls at 600 buildings, or more than 5 people per house |
| R7 | **Pacing drift** after fixes | 4 | 3 | 12 | Design | Headless sweeps every week. Age-time bands in CI | An age outside 80–200 years in > 20% of runs |
| R14 | **Input ownership.** Duplicate hotkey handlers (C14) | 4 | 2 | 8 | Unity UX | One input router. The HUD handles UI only, and the controllers handle world input | Any binding that fires twice |
| R8 | **Mod security** (malicious packs) | 2 | 5 | 10 | Security | Data only, `SafeJson` limits, `ModHardCaps`, a validator, a frozen database, HMAC saves, fuzz tests | Fuzz failures |
| R9 | **Save compatibility** across versions | 3 | 3 | 9 | Persistence | Schema version, `SaveMigrator`, append-only enums | Migration test failures |
| R10 | **Unity version churn** (Unity 6 vs 2022.3 package differences) | 3 | 2 | 6 | Tools | A CI matrix with both versions. No version-specific APIs | The 2022.3 lane fails to compile |
| R12 | **Chronicle spam** (about 500–900 famine events per run) | 3 | 2 | 6 | Design | Importance tiers, aggregation, cooldowns | Telemetry: the chronicle is closed within 2 s of opening |
