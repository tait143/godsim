"""publish(): validate -> write .glb -> render preview -> record manifest entry.

Output layout (repo-relative):
  Assets/GodSim/Art/Models/<category>/<asset_id>.glb
  tools/AssetForge/previews/<category>/<asset_id>.png
  Assets/GodSim/Art/Models/_manifests/<generator>.json      (one file per generator script; merged by build_all.py)
"""
from __future__ import annotations

import json
import os
import re
from dataclasses import asdict, dataclass, field
from typing import List, Optional

import numpy as np

from .gltf import write_glb
from .render import render

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..', '..', '..'))
MODELS = os.path.join(ROOT, 'Assets', 'GodSim', 'Art', 'Models')
PREVIEWS = os.path.join(ROOT, 'tools', 'AssetForge', 'previews')
MANIFESTS = os.path.join(MODELS, '_manifests')

BUDGETS = {  # max triangles per kind
    'house': 1800, 'building': 3500, 'wonder': 9000, 'townhall': 4500,
    'prop': 500, 'nature': 700, 'unit': 700, 'vehicle': 1500, 'animal': 500, 'kit': 900, 'fx': 400, 'icon': 300,
}

ID_RE = re.compile(r'^[a-z0-9_]{3,64}$')


@dataclass
class AssetMeta:
    id: str                       # unique, lowercase [a-z0-9_], e.g. "bld_human_house_dawn_v0"
    kind: str                     # house | building | wonder | townhall | prop | nature | unit | vehicle | animal | kit | fx | icon
    category: str                 # output subfolder, e.g. "Buildings/Human", "Nature", "Props"
    building_type: Optional[str] = None   # GodSim BuildingType enum name (e.g. "House", "TownCenter") if a building
    race: Optional[str] = None            # human | elf | dwarf | orc | saurian | None for shared
    ages: List[str] = field(default_factory=list)   # ages this model represents (e.g. ["dawn"], or ["classical","medieval"])
    footprint: Optional[List[int]] = None           # [w, d] tiles for buildings
    variant: int = 0
    tags: List[str] = field(default_factory=list)
    notes: str = ''


class AssetError(Exception):
    pass


def validate(builder, meta: AssetMeta):
    if not ID_RE.match(meta.id):
        raise AssetError(f'bad asset id {meta.id!r} (use [a-z0-9_], 3-64 chars)')
    if meta.kind not in BUDGETS:
        raise AssetError(f'unknown kind {meta.kind}')
    builder.drop_degenerate()
    st = builder.stats()
    n = st['triangles']
    if n == 0:
        raise AssetError('empty mesh')
    if n > BUDGETS[meta.kind]:
        raise AssetError(f'{meta.id}: {n} triangles exceeds the {meta.kind} budget {BUDGETS[meta.kind]}')
    (x0, y0, z0), (x1, y1, z1) = st['bounds']
    if y0 < -0.6:
        raise AssetError(f'{meta.id}: geometry goes too far below ground (min y {y0:.2f}); pivot must be at ground level')
    if meta.footprint:
        w, d = meta.footprint
        if (x1 - x0) > w * 1.25 + 0.3 or (z1 - z0) > d * 1.25 + 0.3:
            raise AssetError(f'{meta.id}: model {x1 - x0:.2f}x{z1 - z0:.2f} overflows footprint {w}x{d} (allowed +25%)')
        cxm, czm = (x0 + x1) / 2, (z0 + z1) / 2
        if abs(cxm) > w * 0.35 + 0.15 or abs(czm) > d * 0.35 + 0.15:
            raise AssetError(f'{meta.id}: model not centred on the footprint (centre {cxm:.2f},{czm:.2f})')
    P = np.concatenate([t.p for t in builder.tris])
    if not np.all(np.isfinite(P)):
        raise AssetError(f'{meta.id}: NaN/Inf vertex')
    return st


def publish(builder, meta: AssetMeta, generator: str, preview: bool = True) -> dict:
    """Validate and export one asset. `generator` = the calling script's stable name (manifest file)."""
    st = validate(builder, meta)
    out_dir = os.path.join(MODELS, *meta.category.split('/'))
    os.makedirs(out_dir, exist_ok=True)
    glb_path = os.path.join(out_dir, meta.id + '.glb')
    info = write_glb(glb_path, builder.tris, extras=dict(id=meta.id, godsim=asdict(meta)))
    prev_rel = None
    if preview:
        pdir = os.path.join(PREVIEWS, *meta.category.split('/'))
        os.makedirs(pdir, exist_ok=True)
        ppath = os.path.join(pdir, meta.id + '.png')
        render(builder.tris, ppath)
        prev_rel = os.path.relpath(ppath, ROOT)
    entry = asdict(meta)
    entry.update(path=os.path.relpath(glb_path, ROOT).replace(os.sep, '/'), triangles=info['triangles'], bytes=info['bytes'],
                 bounds=[list(map(float, st['bounds'][0])), list(map(float, st['bounds'][1]))], preview=prev_rel and prev_rel.replace(os.sep, '/'))
    _append_manifest(generator, entry)
    return entry


def _append_manifest(generator: str, entry: dict):
    os.makedirs(MANIFESTS, exist_ok=True)
    path = os.path.join(MANIFESTS, re.sub(r'[^a-z0-9_]', '_', generator.lower()) + '.json')
    data = []
    if os.path.exists(path):
        try:
            with open(path) as f:
                data = json.load(f)
        except Exception:
            data = []
    data = [e for e in data if e.get('id') != entry['id']] + [entry]
    data.sort(key=lambda e: e['id'])
    with open(path, 'w') as f:
        json.dump(data, f, indent=1)


def reset_manifest(generator: str):
    path = os.path.join(MANIFESTS, re.sub(r'[^a-z0-9_]', '_', generator.lower()) + '.json')
    if os.path.exists(path):
        os.remove(path)
