# Meshy batch: GodSim 3D assets

This folder drives the Meshy text-to-3D batch for every GodSim model. The full list, art direction, spec and priorities are in [`docs/ASSET_LIST.md`](../../docs/ASSET_LIST.md).

| File | What it is |
|---|---|
| `meshy_assets.json` | The batch input: `{ schema, styleSuffix, meshyDefaults, count, items[] }`. There is one item per model, and each item's `prompt` is ready to send. |
| `meshy_assets.csv` | The same rows for spreadsheets. `ages` is `;`-separated and `footprint` is `WxD`. |

## Item fields

| Field | Meaning |
|---|---|
| `id` | The asset id, which is also the file name. Pattern `[a-z0-9_]`, matching the existing ids (`bld_dwarf_mine_ancient_v0`). |
| `category`, `kind` | `category` is the table group. `kind` is the AssetForge budget kind (`house`, `building`, `townhall`, `wonder`, `prop`, `nature`, `unit`, `vehicle`, `animal`, `kit`). |
| `race` | `human`, `elf`, `dwarf`, `orc`, `saurian` or `neutral`. |
| `subject`, `buildingType` | The subject in plain words, and the exact `BuildingType` enum name when the asset is a building. |
| `band`, `ages[]` | `band` is one of `ancient`, `classical`, `industrial`, `future` (`all` for race-neutral wonders). `ages[]` lists the lower-case `AgeId` names the model covers. |
| `footprint` | `[w, d]` in tiles (1 tile = 1 m), or `null` for things scaled by height, such as units and job props. |
| `targetHeight` | For height-scaled assets: units in villager space (adult = 1.0), everything else in tiles. |
| `targetPolycount` | Sent as Meshy `target_polycount` (triangles). |
| `priority` | `P0`, `P1` or `P2`. Run in this order. |
| `prompt` | The full Meshy prompt: core text plus the global style suffix, at most 600 characters. |
| `negativePrompt` | The base list plus category extras. Send it; newer models may ignore it. |
| `texturePrompt` | The refine-step texture prompt: race palette plus band materials. |
| `artStyle` | `realistic`, the textured mode. Stylisation comes from the prompt. |
| `outputPath` | Where the final, normalised GLB goes inside the Unity project. |
| `variant`, `notes` | `variant` is the variant index (House uses `v0`/`v1`). `notes` holds placement and renderer caveats. |

## Meshy API (Text to 3D v2)

> **Verify first.** `docs.meshy.ai` was blocked from the session that wrote this file. The endpoints and fields below are Meshy's documented v2 API as last known. Check them against https://docs.meshy.ai/en/api/text-to-3d before the first paid run, especially credit costs, `ai_model` names, and whether `negative_prompt`/`art_style` still apply.

Every call sends the header `Authorization: Bearer $MESHY_API_KEY`. The base URL is `https://api.meshy.ai`.

| Step | Call | Key fields |
|---|---|---|
| 1. Preview (geometry) | `POST /openapi/v2/text-to-3d` | `mode: "preview"`, `prompt` (≤ 600 chars), `negative_prompt`, `art_style` (`realistic` or `sculpture`), `ai_model` (`latest`, `meshy-5` or `meshy-4`), `topology: "triangle"`, `target_polycount` (100–300000), `should_remesh: true`, `symmetry_mode` (`off`, `auto` or `on`), `is_a_t_pose: false`, `seed` (int, for reproducible re-rolls). The response is `{ "result": "<preview_task_id>" }`. |
| 2. Poll | `GET /openapi/v2/text-to-3d/{id}` (or `…/{id}/stream` for server-sent events) | `status` is `PENDING`, `IN_PROGRESS`, `SUCCEEDED`, `FAILED` or `CANCELED`. Also `progress`, `thumbnail_url`, `model_urls.{glb,fbx,obj,usdz}`, `texture_urls[]` and `task_error.message`. |
| 3. Refine (texture) | `POST /openapi/v2/text-to-3d` | `mode: "refine"`, `preview_task_id`, `texture_prompt` (≤ 600 chars), `enable_pbr: false` (matte look; base colour is enough), `ai_model`. The response is `{ "result": "<refine_task_id>" }`. |
| 4. Poll again, then download | `GET /openapi/v2/text-to-3d/{refine_id}` | Download `model_urls.glb` (and `fbx` as a backup) right away. The links are signed and expire. |
| Utilities | `GET /openapi/v1/balance` and `GET /openapi/v2/text-to-3d?page_num=1&page_size=50` | Check the credit balance before a batch, and list or resume tasks. A `remesh` endpoint (`/openapi/v1/remesh`) can re-target the polycount and format of a finished task without regenerating it. |

The common error codes are 401 (bad key), 402 (out of credits) and 429 (rate limit or too many tasks in flight). On 429, back off exponentially. Meshy also documents a **test-mode API key** that returns sample results without spending credits. Use it to dry-run the batch script end to end.

### Example request (P0 Dwarf mine)

```bash
# 1) preview
curl -sS https://api.meshy.ai/openapi/v2/text-to-3d \
  -H "Authorization: Bearer $MESHY_API_KEY" -H "Content-Type: application/json" \
  -d @- <<'JSON'
{
  "mode": "preview",
  "prompt": "Dwarf mine, ancient era, square base: mine tunnel into a rocky mound, timber-propped portal, ore baskets, spoil heap. Squat and half-sunk into a turf mound, rough-cut stone blocks, thick timber lintels, flat sod roof, rune-carved doorstone, ember glow. Charming details: ... Storybook Monumental style: stylized, chunky hand-crafted proportions, believable construction, matte hand-painted colors. Game asset, isometric-friendly readable silhouette.",
  "negative_prompt": "photorealistic, photo scan, gritty grime, noisy texture, text, letters, logo, watermark, gore, blood, skulls, horror, floating parts, ...",
  "art_style": "realistic",
  "ai_model": "latest",
  "topology": "triangle",
  "target_polycount": 3500,
  "should_remesh": true,
  "symmetry_mode": "auto",
  "is_a_t_pose": false,
  "seed": 1234567
}
JSON
# -> {"result":"<PREVIEW_ID>"}

# 2) poll until status == SUCCEEDED
curl -sS https://api.meshy.ai/openapi/v2/text-to-3d/<PREVIEW_ID> -H "Authorization: Bearer $MESHY_API_KEY"

# 3) refine with the row's texturePrompt
curl -sS https://api.meshy.ai/openapi/v2/text-to-3d \
  -H "Authorization: Bearer $MESHY_API_KEY" -H "Content-Type: application/json" \
  -d '{"mode":"refine","preview_task_id":"<PREVIEW_ID>","enable_pbr":false,"ai_model":"latest","texture_prompt":"warm grey stone, dark slate roof, copper-brass trim, amber forge glow; thatch, hide, raw timber, mudbrick, fieldstone. Hand-painted stylized texture, matte, clean color blocks."}'
```

Copy the exact `prompt` and `negativePrompt` for each item from `meshy_assets.json`; the example above is shortened.

### Running the batch

A batch runner is not included in this folder. It should do the following:

1. Load `meshy_assets.json` and filter by `priority`, starting with `P0`. Skip any id whose `outputPath` already exists, so the batch can resume.
2. Merge `meshyDefaults` with the per-item fields. Use `seed = crc32(id)` so a re-run reproduces the same roll, and add 1 to the seed for each re-roll.
3. Keep about 10 tasks in flight, which depends on the plan. Send the preview, poll every 10–15 s, send the refine, poll again, then download the GLB and thumbnail to `build/meshy_raw/<id>/`. Keep raw files outside `Assets/`.
4. Normalise each file (scale, pivot and facing, below), then copy it to `outputPath`.
5. Store optional Meshy output in a separate category manifest. For the production building catalog, AssetForge consumes the work-list IDs and footprints, generates its own original meshes and publishes packages after independent review. See the [building pipeline](../../tools/AssetForge/README.md).
6. Log the task ids, credits used and failures to a CSV for the review pass.

Work in this order: generate P0, review it as a group, and tune `styleSuffix` and the race lines in `docs/ASSET_LIST.md` if the family does not read as one. Then run P1 and P2.

### Review checklist (per asset)

- The silhouette reads at 64 px from the god's-eye camera (pitch about 32°). The roof is clearly darker or lighter than the walls.
- The race motifs are visible, and the model looks the right age next to the same building's other bands.
- There is no text, logo, gore or imitation of real-world landmarks or other franchises.
- There is no display base, floating parts or merged neighbouring objects.
- The triangle count is within about 10% of `targetPolycount`.

## Unity import

1. The project manifest pins `com.unity.cloud.gltfast` at 6.10.3. Unity Package Manager resolves it when the project is opened in an environment with the dependency available.
2. **Normalise scale, pivot and facing.** Do this before or at import; Meshy output comes at an arbitrary size and is centred on its bounds.
   - **Nature, kit, ruins and divine objects.** Scale uniformly so the XZ bounds fit `footprint` (W along X, D along Z), with at most +25% overhang. Translate so that `min.y = 0` and the XZ centre is at the origin. Production buildings must fit the exact work-list footprint.
   - **Units.** Scale so the height is 1.0 with the feet at y = 0.
   - **Animals and vehicles.** Scale to `targetHeight`, in tiles.
   - **Facing.** The front or door must face −Z. If Meshy put it on +Z, rotate 180° around Y. Tools for this step: an `AssetPostprocessor` that reads the manifest, a Blender batch, or Meshy remesh with `origin_at: "bottom"` and `resize_height`, if available.
3. Drop an optional asset on a tile grid and inspect the bounds. Production building meshes must fit their exact assigned footprint.
4. **Materials.**
   - The project uses the Built-in render pipeline, and the instanced renderers (`PeopleRenderer`, `ArmyRenderer`, terrain trees) multiply vertex colours by a per-instance tint.
   - For instanced assets (villagers, soldiers, trees), bake the base-colour texture into vertex colours (`COLOR_0`) at import and use `MaterialLibrary.VertexColorInstanced`. Alternatively, give each race an instancing-enabled textured material.
   - Production buildings use the shared `GodSim_Buildings` material. Its atlas and emissive mask cover the whole catalog, including windows, forges and Ascension lighting.

## How ids map to the game

**Age to band.** `StyleAge` picks the band:

| `AgeId` | Band id |
|---|---|
| Dawn, Bronze, Iron | `ancient` |
| Classical, Medieval, Renaissance | `classical` |
| Industrial, Modern | `industrial` |
| Information, Ascension | `future` |

**Buildings.** `GodSim.Unity.ArtLibrary` reads the generated runtime index and resolves the exact building type, race and age. It selects a listed model variant and applies the age's atlas-row offset. Shared wonders have explicit bindings for every eligible race. Missing bindings or unusable model imports select the existing procedural mesh.

- **Fallback.** `ProceduralBuildingMeshes.cs` (and its far box LOD) stays as the fallback for any missing model. A partial art drop, such as P0 only, therefore always renders a complete world.
- An authored Town Center gains height with settlement tier and keeps its exported footprint. The procedural fallback retains its original tier scaling.
- **Rotation.** The renderer applies `Rotation × 90°` and swaps W/D for odd rotations. Models must face −Z at rotation 0.

**Other assets.**

| Game hook | Asset ids |
|---|---|
| Ruins: `ProceduralBuildingMeshes.GetRuin(w, h, StyleAge, id)` | `ruin_<band>_<small/medium/large>_v0`, picked by the largest side (1, 2, or 3+). The renderer keeps its charred tint for fire, war and divine causes. |
| Construction: `ScaffoldMf` | `con_scaffold_<band>_<small/medium/large>_v0`, scaled to the footprint. |
| Banners: building banner and army banner | `prop_<race>_banner_v0` |
| People: `PeopleRenderer` | `unit_<race>_villager_<adult/child/elder>`, with job props `prop_job_<job>_v0` on a right-hand socket (job = `JobType` in lower case). |
| Soldiers: `ArmyRenderer` | `unit_<race>_soldier_<band>` |
| Trees: `TreeKind` (Conifer, Deciduous, Palm) | Biome groups: temperate `nat_tree_oak/birch/fruit`, conifer `nat_tree_spruce/pine`, tropical `nat_tree_palm/jungle`, arid `nat_tree_acacia/bottle` and `nat_plant_cactus`, swamp `nat_tree_mangrove/willow/cypress`. Extending `TreeKind` is an append-only enum change; the per-biome choice is in `TerrainMeshBuilder.ChooseKind`. |
| Tile flags | `nat_node_<ore/stone/gold/coal/oil>_v*` for the deposit flags, `nat_crop_*` for Farmland, `div_sacred_*` for Sacred, `div_meteor_crater_v0` for Scorched plus meteor, `kit_bridge_<band>_v0` for River plus Road. |
| Not rendered yet | Animals (`ani_*`), vehicles and boats (`veh_*`) and road tiles (`kit_road_*`) are generated for upcoming renderers: caravans, naval and pastures. |
