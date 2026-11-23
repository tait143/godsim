# AssetForge — GodSim's procedural model pipeline

Python generators build original stylized low-poly models in code and export Unity-ready `.glb` files. Each
model gets vertex colours and flat shading, Y-up, with 1 unit = 1 tile. A preview PNG and a manifest entry
are written alongside. Read `docs/ART_STYLE_GUIDE.md` first.

```text
tools/AssetForge/
  forge/            shared toolkit (lead-owned; do not edit from generator work)
    mesh.py         MeshBuilder: box, cylinder/cone/frustum, sphere, dome, gable/hip roofs, prism, pyramid,
                    stepped pyramid, wedge, poly/quad/tri, transforms (with-block), details (door, window,
                    chimney, banner, flower_box, fence, barrel, crate, lantern, tree), wobble()
    style.py        palettes: AGE_MATERIALS, RACE_STYLE, COMMON, blended(race, age, key), shade/mix/jitter
    gltf.py         GLB writer (+ read_glb_summary for validation)
    render.py       numpy preview renderer (god's-eye 3/4 view, toon outline, contact sheets)
    registry.py     publish(builder, AssetMeta, generator): validate -> .glb -> preview -> manifest
  generators/<group>/<name>.py   one script per artist; run with: python3 tools/AssetForge/generators/<group>/<name>.py
  generators/_samples/sample_house.py   reference example
  previews/         rendered PNGs (check them!)
  build_all.py      runs every generator and merges manifests -> Assets/GodSim/Art/Models/manifest.json
```

The outputs are `Assets/GodSim/Art/Models/<Category>/<id>.glb` plus `_manifests/<generator>.json`, which
`build_all.py` merges into `manifest.json`. Unity imports the `.glb` files via glTFast
(`com.unity.cloud.gltfast`). `GodSim.Unity.ArtLibrary` maps (BuildingType, race, age) to a model, and falls
back to procedural meshes when nothing matches.

## Writing a generator
```python
import os, sys
sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..')))
from forge import MeshBuilder, style, publish, AssetMeta
GEN = 'dwarf_extraction'            # unique generator name == manifest file
m = MeshBuilder(seed='bld_dwarf_mine_bronze_v0')
...                                  # model it (see sample_house.py)
publish(m, AssetMeta(id='bld_dwarf_mine_bronze_v0', kind='building', category='Buildings/Dwarf/Extraction',
                     building_type='Mine', race='dwarf', ages=['bronze', 'iron'], footprint=[2, 2]), GEN)
```
- `ages` lists the ages this model should represent. ArtLibrary picks the model whose age range is
  closest to `Building.StyleAge`.
- Use `with m.transform(translate=..., rotate_y=..., scale=...)` for sub-assemblies.
- Wrap helpers in your own module-level functions. Never edit `forge/`. If you need a primitive that is
  missing, write it locally in your generator using `m.quad`/`m.tri`/`m.poly`.
