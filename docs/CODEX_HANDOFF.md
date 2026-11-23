# Handoff: give the GodSim building-art task to Codex

Paste everything below the line into a Claude session that can reach Codex.

---

You are coordinating a job for me. Hand the task below to **Codex running Astra 6.2 at `xhigh`
effort**, then supervise it until it meets the acceptance bar. Do not do the modelling yourself.

**Project.** GodSim is a Unity god simulator. The repository is `tait143/godsim` on GitHub
(https://github.com/tait143/godsim). Its simulation core is finished and tested (2,985 passing tests). What is missing is the
building art.

**The task.** The full brief is in the repository at `docs/BUILDING_ART_TASK.md`. Give Codex that
file as its instructions, unchanged. In short, Codex must:

1. Build a game-ready 3D model for every building: 75 building types, for each of the 5 races,
   across all 10 ages. The work list (958 entries with ids, ages and footprints) is
   `assets/meshy/meshy_assets.json`.
2. Share textures to save memory: one shared atlas (1024×1024 at most), one shared emissive mask
   and one material for all buildings. No per-building textures.
3. Keep iPhone play in mind and still make everything look excellent: the triangle, LOD,
   texture-memory and draw-call budgets are in section 4 of the brief.
4. Spawn many agents: one builder agent per work package (race × building group, about 70
   packages), plus one agent that builds the shared textures first and one that integrates.
5. Pair every builder with a separate **critic agent** that scores how good each model looks,
   lists every error, and sends it back. The builder fixes every issue and resubmits until the
   model passes.
6. Meet the bar: 99 or higher on technical health and 90 or higher on each visual criterion, with
   no open defects. Anything that cannot reach it after three rounds is escalated, not shipped.

**Where things are.**
- `docs/ART_STYLE_GUIDE.md`: the art direction ("Storybook Monumental": serious, believable
  buildings with chunky, charming, playful details; race motifs; materials per age).
- `tools/AssetForge/`: the Python model pipeline (numpy and Pillow, no Blender). It exports `.glb`
  and renders preview images. `generators/_samples/sample_house.py` is a working example.
- `docs/CONTENT_REFERENCE.md`: every building's footprint, age range and function.

**What you must do as coordinator.**
1. Confirm you can reach Codex with Astra 6.2 at `xhigh`. If you cannot, stop and tell me.
2. Have Codex work on a new branch, `art/buildings`, and open a pull request when done.
3. Check Codex's final report (`docs/BUILDING_ART_REPORT.md`) against the brief: counts per race
   and age, budgets met, every critic score, every escalation.
4. Look at the contact sheets yourself before telling me it is done. Report honestly what was
   verified and what was not. Nothing has been opened in a Unity editor so far.

**Rules.** All art is original. Do not edit `Assets/GodSim/Core`. Output must be deterministic.
