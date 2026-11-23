# AssetForge building pipeline

AssetForge writes game-space buildings in metres per tile, with Y up, ground at zero and the entrance facing −Z. Every production GLB contains three authored meshes and references the two shared PNGs. NumPy and Pillow are the only third-party Python dependencies.

## Generate a reference house

From the repository root, using the project’s Python environment:

```sh
export PYTHONDONTWRITEBYTECODE=1
(cd tools/AssetForge && python -c 'from forge.atlas import create_textures; create_textures()')
python tools/AssetForge/generators/_samples/sample_house.py
```

The Dawn, Medieval and Industrial houses and their previews live under the respective `_Samples` directories. They demonstrate atlas use and LOD construction. The production work list and critic acceptance process are in [the building brief](../../docs/BUILDING_ART_TASK.md). The silhouettes, age materials, race motifs and scale follow [the art style guide](../../docs/ART_STYLE_GUIDE.md). Use the newer budgets from the brief.

## Author a building

Construct three complete versions of the building. Preserve supporting walls, roof silhouette and important projecting parts at distance. Group details by LOD in the generator. A lower LOD may replace a continuous many-sided shell with a simpler shell, remove small fittings, or combine small parts into a single mass.

```python
from forge import AssetMeta, MeshBuilder, publish, style

meta = AssetMeta(
    id='bld_dwarf_mine_bronze_v0', kind='building',
    category='Buildings/Dwarf/Extraction', building_type='Mine',
    race='dwarf', ages=['bronze', 'iron'], footprint=[2, 2],
)
levels = [build_mine(meta, level) for level in range(3)]
entry = publish(levels[0], meta, 'dwarf_extraction',
                lods=(levels[1], levels[2]), preview_size=160)
```

`build_mine` in this example stands for the artist’s construction function. The sample house has a runnable version of this pattern. Supply an individual mesh per age when the brief requires a new silhouette. For a multi-age mesh, place the authored base age first in `ages`.

MeshBuilder offers box, cylinder, cone, sphere, dome, prism, gable and hip roofs, pyramid, stepped pyramid, wedge, and custom counter-clockwise `tri`, `quad`, and convex `poly` faces. A transform accepts position, scale and degree rotations and preserves winding under reflections. Each planar face receives a continuous local UV projection before triangulation. Atlas packing reserves five pixels of gutter on every side.

Choose a surface with `material='roof'` (or another key) on the primitive. This argument selects atlas pixels for the one shared GPU material. Race-aware roles are `wall`, `roof`, `trim`, `accent` and `banner`. Detail slots are defined in `forge.atlas.DETAILS`. They include thatch, timber, plaster, fieldstone, brick, roof tile (`roof_tile`), slate, metal plate (`metal`), glass, leaf, hide and the three emission slots `emissive`, `forge` and `ascension`. The default `base` surface maps the input RGB to the closest non-emitting shared swatch for that race and age. Explicit roles keep the material assignment stable across color variations. In both cases, the atlas supplies the actual color. For a separate per-vertex linear RGB multiplier, pass `tint=(r,g,b)` to MeshBuilder. Untinted models omit `COLOR_0`.

## Texture layout and Unity

`forge.atlas.create_textures()` deterministically paints both shared PNGs in `Assets/GodSim/Art/Textures`. The atlas is 1024 × 1024 with ten 96-pixel age rows. Each row holds 48 patches with extruded edges, arranged in three strips of 16. A 256 × 256 emission texture uses the same layout. Each source PNG has a matching Unity importer configuration with mipmaps, clamping and an explicit iPhone ASTC 6×6 override. The complete mip chains need **666,384 bytes** at that format.

The glTF V coordinate increases downward. Add `(target_age_index - authored_age_index) * 96 / 1024` to glTF V. For Unity UVs converted to bottom-origin, apply the negative of that delta. Both textures use the same adjusted coordinates. The shared built-in-pipeline material is `Resources/GodSim_Buildings.mat`. Its shader accepts instanced `_AgeRowOffset` in Unity UV units, `_Color` for instance tint, and `_UseVertexTint` (default 0). A renderer can enable vertex tint for meshes with a color channel. When combining mixed geometry, fill missing vertex colors with white. The two PNG references, shader and material use checked-in Unity GUIDs.

A glTF viewer opens scene 0 (LOD0). Scenes 1 and 2 expose only LOD1 and LOD2 respectively. Each scene contains one identity-transform root, one indexed mesh and one primitive. The optional `MSFT_lod` link also identifies the two lower nodes for clients that understand it. Load the appropriate mesh for display. All external image URIs are relative to the GLB directory and point back to the same two PNGs.

## Verify and publish

`forge.registry.validate_glb(path)` reopens the GLB and reads the actual binary accessors. It checks counts, indices, finite coordinates, accessor limits, normals against winding, duplicate and degenerate triangles, floor contact, exact footprint limits, per-face UV patch containment, base-age row, inset margins, one material, both exact texture references, the LOD structure and the iPhone texture budget. For a manifest comparison, pass the expected `AssetMeta` as the second argument.

LOD0 limits are 700 triangles for House, 1,500 for a standard building, 2,500 for Town Center, Castle, Forum or Arcology, and 5,000 for wonders. Each chain decreases strictly. LOD1 targets 50% of LOD0 with a 32–68% band and one triangle of rounding tolerance. LOD2 targets 20%, with a ceiling of 32% or a 32-triangle simple silhouette. A critic still assesses silhouettes, support, entrance direction, visibility and overlapping surfaces. Numerical normal consistency alone cannot identify an artist’s reversed custom face.

`publish` validates the serialized bytes before replacing a model. It renders the base preview as `<id>.png` and each additional age as `<id>__<age>.png` using the exact UV-row shift. It records `lod_triangles`, `texture_bytes_astc` (the same shared allocation for every model), bounds and the `age_previews` mapping in the generator’s manifest fragment. Each generator owns its fragment. Every production output belongs in its race and group folders beneath `Assets/GodSim/Art/Models/Buildings` and `tools/AssetForge/previews/Buildings`.

Unity shader compilation, glTFast import, draw-call counts and physical device measurements require separate verification.

Use `building_review.py <package>` to run serialized and geometric checks and print the image paths an independent critic must inspect. It reports exposed coplanar triangles, inward-facing closed shells and identical LOD0 shapes within the package. The catalog assembly also checks shapes across race packages.

Run `build_all.py --no-run --partial` to publish whole packages that pass current reviews and geometry checks and have no open art-director findings. `building_status.py` updates the art report and production checklist. After all packages pass, `build_all.py --verify-rebuild` executes every generator twice in sequence, checks each expected model and age preview was rewritten, and compares the SHA-256 inventories. The comparison covers models, age previews, shared resources, manifests, resource bindings, generator-produced contact sheets and all canonical contact sheets. Add `--hash-dir DIRECTORY` to save both full inventories. In `previews/_sheets/families/`, each row shows one race and house variant across ten ages at 64 pixels. Empty cells mark ages absent from the published catalog.
