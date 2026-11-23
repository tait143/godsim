# Task brief: build every GodSim building model (all races, all ages)

**Run with:** Astra 6.2 at `xhigh` effort, executing through Codex agents.
**Repository root:** `GodSim/` (Unity project; pure C# simulation in `Assets/GodSim/Core`).

## 1. Goal

Produce a finished, game-ready 3D model for every building the game can show: 75 building types
across 5 races (Human, Elf, Dwarf, Orc, Saurian) and 10 ages (Dawn to Ascension). The models must
look excellent on an iPhone screen and run well on one. All buildings share a small set of textures
so memory stays low.

The quality bar is 99% of the lead's standard, defined in section 6. Nothing ships below it.

## 2. What already exists (read these first)

| Path | What it is |
|---|---|
| `docs/ART_STYLE_GUIDE.md` | The art direction, "Storybook Monumental": serious, believable construction with chunky, charming proportions and playful details. Race motifs and the age material ladder are defined here. |
| `tools/AssetForge/` | The Python model pipeline (numpy and Pillow only, no Blender). `forge/mesh.py` builds meshes, `forge/gltf.py` writes `.glb`, `forge/render.py` renders preview PNGs, `forge/registry.py` validates and publishes. `README.md` explains it. |
| `tools/AssetForge/generators/_samples/sample_house.py` | A working reference generator. Run it and look at its previews. |
| `assets/meshy/meshy_assets.json` | The work list: 958 building entries with id, race, building type, ages covered, footprint and priority. Use the ids and footprints; ignore the Meshy prompts. |
| `docs/CONTENT_REFERENCE.md` | Generated from the game data: every building's footprint, age range, race limits and function. |
| `Assets/GodSim/Core/Races/*.cs` | Race lore, colours and architecture flags. |
| `Assets/GodSim/Unity/Runtime/Buildings/` | The Unity renderer and its procedural fallback meshes. |

## 3. Step 0, before any building is modelled: shared textures

The pipeline today colours models with per-vertex colours and writes no UVs. Change that first.
One agent owns this step, and every other agent waits for it.

1. **One shared atlas for all buildings.** Create `Assets/GodSim/Art/Textures/GodSim_BuildingAtlas.png`,
   1024×1024 at most. It holds:
   - palette swatches and soft gradients for every race and age material;
   - a few small tiling detail patches: thatch, timber, plaster, fieldstone, brick, roof tile,
     slate, metal plate, glass, leaf, hide.
2. **One shared emissive mask**, 256×256 at most, for lit windows, forge glow and Ascension light.
3. **One material** (`GodSim_Buildings`) used by every building, so Unity can batch and instance them.
4. **Ages by UV row.** Lay the atlas out so each age is a row. A mesh can then be re-skinned to
   another age by shifting its UVs to another row, with no new texture and no new mesh data beyond UVs.
5. **Pipeline changes.** Extend `forge/mesh.py`, `forge/gltf.py` and `forge/render.py` to write
   `TEXCOORD_0`, sample the atlas in previews, and keep vertex colour only as an optional tint.
   Extend `forge/registry.py` to enforce the budgets in section 4.
6. **No per-building textures, ever.** The validator must reject a model that references anything
   but the shared atlas and mask.

## 4. iPhone budgets (enforced by the validator)

| Item | Budget |
|---|---|
| House | 700 triangles or fewer |
| Standard building | 1,500 triangles or fewer |
| Town Center, Castle, large civic | 2,500 triangles or fewer |
| Wonder | 5,000 triangles or fewer |
| LODs | Every model ships LOD0, LOD1 at about 50%, and LOD2 at about 20% or a simple silhouette |
| Materials per model | 1 (the shared material); emissive comes from the shared mask |
| Texture memory, all buildings | Under 6 MB on device (ASTC compressed atlas plus mask) |
| Draw calls | A 200-building town must batch into a handful of draw calls |
| Scale and pivot | 1 unit = 1 tile, Y up, pivot at ground centre, door facing −Z, fits its footprint |

Readability matters as much as the numbers. A phone shows a building at roughly 40–80 pixels, so
silhouettes and roof shapes must read at that size.

## 5. Scope and how to split it across agents

Cover every race and every age in which a building exists (age ranges are in the work list).

- **Town Center and House:** a distinct model for each of the 10 ages, per race. Houses get 2–3
  variants per age, because towns contain many of them.
- **Every other building:** a distinct mesh per era for each race, covering each age it exists in.
  Adjacent ages may share a mesh and differ by atlas row (section 3, item 4), but each age must look
  visibly different.
- **Racial wonders and shared wonders:** one model each, at wonder quality.

Use one builder agent per work package. A sensible split is race × building group, about 14 groups:
town centers; houses by era (three packages); food and farming; resource extraction; industry;
religion; knowledge and health; culture and commerce; military; infrastructure and storage; wonders.
That gives roughly 70 building packages plus the shared-texture agent and an integration agent.
Each agent owns only its own generator file and output folder.

## 6. Critic and fix loop (required for every work package)

Every builder agent is paired with a separate critic agent. The critic did not build the models.

1. The builder produces its models and preview images, then hands off.
2. The critic looks at every preview and every validator result, and scores each model 0–100 on:
   - silhouette and readability at phone size;
   - race identity and age correctness against the style guide;
   - construction believability (no floating parts, sensible structure);
   - charm (2–4 well-placed playful details);
   - colour balance and material use from the shared atlas;
   - technical health: budgets, LODs, UVs inside the atlas, pivot, scale, door direction,
     no flipped or degenerate faces, no z-fighting.
3. The critic lists every defect with the model id and a specific fix.
4. The builder fixes every listed defect and resubmits. Repeat until every model scores **99 or
   higher on technical health and 90 or higher on each visual criterion**, with zero open defects.
5. A model that cannot reach the bar after three rounds is escalated, with the reason, to the lead
   agent. It is never shipped quietly.

After all packages pass, one art-director agent reviews contact sheets across races and ages for
consistency: the same building must read as one family across ages, and each race must be
recognisable at a glance.

## 7. Deliverables

- `Assets/GodSim/Art/Models/Buildings/<Race>/<Group>/<id>.glb`, with LODs.
- The shared atlas, emissive mask and material from section 3.
- `Assets/GodSim/Art/Models/manifest.json`, mapping (building type, race, age) to a model id.
- A Unity loader (`GodSim.Unity.ArtLibrary`) that picks the model from the manifest and falls back
  to the existing procedural mesh when one is missing. Imports go through glTFast.
- Preview images and contact sheets per race and per age under `tools/AssetForge/previews/`.
- `docs/BUILDING_ART_REPORT.md`: counts per race and age, triangle and memory totals against the
  budgets, every critic score, every escalation, and what was not verified.

## 8. Rules

- Everything is original. No imitation of existing games or franchises.
- Deterministic output: the same generator run gives byte-identical models.
- Do not edit `Assets/GodSim/Core`. The simulation is finished and tested.
- Nothing has been opened in a Unity editor from this environment. State plainly in the report
  what was checked only by the Python validator and previews.
