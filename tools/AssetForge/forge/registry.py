"""Validate the actual GLB payload before publishing geometry, atlas previews and manifest entries."""
import json
import os
import re
from dataclasses import asdict, dataclass, field
from functools import lru_cache
from pathlib import Path
from tempfile import NamedTemporaryFile
from typing import List, Optional

import numpy as np
from PIL import Image

from . import atlas, gltf, style
from .render import render

ROOT = str(Path(__file__).resolve().parents[3])
MODELS = os.path.join(ROOT, 'Assets/GodSim/Art/Models')
PREVIEWS = os.path.join(ROOT, 'tools/AssetForge/previews')
MANIFESTS = os.path.join(MODELS, '_manifests')
BUDGETS = dict(house=700, building=1500, townhall=2500, wonder=5000, prop=500, nature=700, unit=700,
               vehicle=1500, animal=500, kit=900, fx=400, icon=300)
ID_RE = re.compile(r'[a-z0-9_]{3,100}')
CIVIC = {'TownCenter', 'Castle', 'Forum', 'Arcology'}
WONDERS = {'DeepForge', 'WarIdol', 'WorldTree', 'SunPyramid', 'GreatLibrary', 'Colossus', 'GrandCathedral',
           'GrandBazaar', 'AscensionGate'}


@dataclass
class AssetMeta:
    id: str
    kind: str
    category: str
    building_type: Optional[str] = None
    race: Optional[str] = None
    ages: List[str] = field(default_factory=list)
    footprint: Optional[List[int]] = None
    variant: int = 0
    tags: List[str] = field(default_factory=list)
    notes: str = ''


class AssetError(ValueError):
    pass


def require(condition, message):
    if not condition:
        raise AssetError(message)


def _meta(meta):
    require(ID_RE.fullmatch(meta.id) and all(re.fullmatch('[A-Za-z0-9_]+', p) for p in meta.category.split('/')),
            f'{meta.id}: invalid id or output category')
    require(meta.kind in BUDGETS, f'{meta.id}: unknown kind {meta.kind}')
    require(meta.race in (*style.RACES, 'neutral', None), f'{meta.id}: unknown race')
    require(all(age in style.AGES for age in meta.ages) and len(meta.ages) == len(set(meta.ages)),
            f'{meta.id}: unknown or repeated age')
    if meta.building_type or meta.category.startswith('Buildings/'):
        require(meta.ages and meta.footprint and len(meta.footprint) == 2 and min(meta.footprint) > 0,
                f'{meta.id}: building requires ages and a positive footprint')


def _budget(meta):
    if meta.building_type == 'House':
        return 700
    if meta.building_type in CIVIC:
        return 2500
    if meta.building_type in WONDERS:
        return 5000
    if meta.building_type:
        return min(1500, BUDGETS[meta.kind])
    return BUDGETS[meta.kind]


def _geometry(p, meta):
    require(np.isfinite(p).all() and len(p), f'{meta.id}: empty or non-finite mesh')
    lo = p.min(axis=(0,1))
    hi = p.max(axis=(0,1))
    require(abs(lo[1]) < .001 and hi[1] > .001, f'{meta.id}: ground contact y={lo[1]:.5f}')
    if meta.footprint:
        half = np.asarray(meta.footprint) / 2
        require(np.all(lo[[0,2]] >= -half-.0001) and np.all(hi[[0,2]] <= half+.0001),
                f'{meta.id}: geometry outside footprint {meta.footprint}, bounds {lo.tolist()}, {hi.tolist()}')
        require(np.all(lo[[0,2]] < 0) and np.all(hi[[0,2]] > 0), f'{meta.id}: origin outside footprint mass')
    return [lo.tolist(), hi.tolist()]


def validate(builder, meta):
    _meta(meta)
    n = len(builder.tris)
    require(0 < n <= _budget(meta), f'{meta.id}: {n} triangles, budget {_budget(meta)}')
    p = np.stack([t.p for t in builder.tris]).astype('<f4')
    bounds = _geometry(p, meta)
    require(np.all(np.linalg.norm(np.cross(p[:,1]-p[:,0], p[:,2]-p[:,0]), axis=1) > 1e-12),
            f'{meta.id}: degenerate triangle')
    return dict(triangles=n, bounds=bounds)


def _shared_files():
    material = atlas.ART / 'Resources/GodSim_Buildings.mat'
    shader = material.with_name('GodSim_BuildingAtlas.shader')
    files = [atlas.ATLAS, atlas.MASK, material, shader]
    files += [Path(str(p) + '.meta') for p in files]
    stamps = tuple((p.stat().st_mtime_ns, p.stat().st_size) for p in files)
    return _check_shared_files(stamps, tuple(files))


@lru_cache(maxsize=1)
def _check_shared_files(stamps, files):
    total = 0
    material = files[2].read_text()
    for i, p in enumerate(files[:3+1]):
        match = re.search(r'^guid: ([0-9a-f]{32})$', files[i+4].read_text(), re.M)
        require(match, f'{p.name}: missing GUID')
        if i != 2:
            require(f'guid: {match[1]},' in material, f'{p.name}: missing shared material reference')
        if i >= 2:
            continue
        with Image.open(p) as im:
            expected = atlas.SIZE if i == 0 else atlas.SIZE//4
            require(im.size == (expected, expected), f'{p.name}: invalid dimensions {im.size}')
            total += atlas.astc_bytes(*im.size)
        settings = files[i+4].read_text()
        ios = re.search(r'buildTarget: iPhone\n((?:    [^\n]+\n)+)', settings)
        require(ios and 'textureFormat: 50\n' in ios[1] and 'overridden: 1\n' in ios[1]
                and f'maxTextureSize: {expected}\n' in ios[1], f'{p.name}: iPhone ASTC 6x6 override missing')
    require(total < 6000000 and 'm_EnableInstancingVariants: 1' in material,
            'shared texture budget or material instancing mismatch')
    return total


def _access(doc, binary, index, component, kind):
    require(type(index) is int and 0 <= index < len(doc['accessors']), 'invalid accessor index')
    acc = doc['accessors'][index]
    require(acc['componentType'] == component and acc['type'] == kind and not acc.get('normalized') and
            'sparse' not in acc, f'accessor {index}: invalid format')
    view_id = acc['bufferView']
    require(type(view_id) is int and 0 <= view_id < len(doc['bufferViews']), 'invalid buffer view index')
    view = doc['bufferViews'][view_id]
    base, length, offset = view.get('byteOffset', 0), view['byteLength'], acc.get('byteOffset', 0)
    dtype = np.dtype('<f4' if component == 5126 else '<u2')
    channels = {'SCALAR':1, 'VEC2':2, 'VEC3':3}[kind]
    count = acc['count']
    n = count * channels * dtype.itemsize
    require(view['buffer'] == 0 and 'byteStride' not in view and type(count) is int and count > 0 and
            base >= 0 and length > 0 and offset >= 0 and offset+n <= length and base+length <= len(binary)
            and (base+offset) % dtype.itemsize == 0, f'accessor {index}: out of bounds or misaligned')
    return np.frombuffer(binary, dtype, count=count*channels, offset=base+offset).reshape(count, channels)


def _serialized(data, path, expected=None):
    doc, binary = gltf.unpack(data)
    nodes, meshes = doc['nodes'], doc['meshes']
    require(doc.get('images') == [dict(uri=u) for u in gltf.image_uris(path)] and
            doc.get('textures') == [dict(sampler=0,source=i) for i in (0,1)] and doc.get('samplers') == [gltf.SAMPLER]
            and doc.get('materials') == [gltf.MATERIAL], 'unauthorized texture, sampler or material')
    require(doc.get('asset', {}).get('version') == '2.0' and doc.get('scene') == 0 and len(nodes)==len(meshes)==3
            and doc.get('scenes') == [dict(name=f'LOD{i}',nodes=[i]) for i in range(3)]
            and nodes[0].get('extensions') == {'MSFT_lod': {'ids':[1,2]}}
            and doc.get('extensionsUsed') == ['MSFT_lod'] and not doc.get('extensionsRequired'),
            'invalid LOD hierarchy or unsupported extension')
    extras = nodes[0]['extras']
    meta = AssetMeta(**extras['godsim'])
    _meta(meta)
    require(extras['id'] == meta.id and (expected is None or asdict(meta) == asdict(expected)),
            'GLB metadata mismatch')
    age_index = style.AGES.index((meta.ages or ['dawn'])[0])
    counts, bounds = [], []
    for i, mesh in enumerate(meshes):
        label = f'{meta.id} LOD{i}'
        allowed = {'name','mesh','extras','extensions'} if i == 0 else {'name','mesh'}
        require(nodes[i].keys() <= allowed and nodes[i]['mesh'] == i and nodes[i]['name'] == f'LOD{i}'
                and mesh['name'] == f'LOD{i}' and len(mesh['primitives']) == 1, f'{label}: wrong mesh/node')
        prim = mesh['primitives'][0]
        attrs = prim['attributes']
        require(prim.keys() == {'attributes','indices','mode','material'} and prim['material'] == 0
                and prim['mode'] == 4 and {'NORMAL','POSITION','TEXCOORD_0'} <= attrs.keys()
                <= {'NORMAL','POSITION','TEXCOORD_0','COLOR_0'}, f'{label}: invalid primitive')
        positions = _access(doc, binary, attrs['POSITION'], 5126, 'VEC3')
        normals = _access(doc, binary, attrs['NORMAL'], 5126, 'VEC3')
        uv = _access(doc, binary, attrs['TEXCOORD_0'], 5126, 'VEC2')
        indices = _access(doc, binary, prim['indices'], 5123, 'SCALAR').reshape(-1)
        require(len(indices)%3 == 0 and indices.max()<len(positions) and len(positions)==len(uv)==len(normals),
                f'{label}: invalid indices or attribute counts')
        if 'COLOR_0' in attrs:
            tint = _access(doc,binary,attrs['COLOR_0'],5126,'VEC3')
            require(len(tint)==len(positions) and np.all((tint>=0)&(tint<=1)),f'{label}: invalid tint')
        require(np.all(np.isfinite(positions)) and np.all(np.isfinite(normals)) and np.all(np.isfinite(uv)),
                f'{label}: non-finite attribute')
        acc = doc['accessors'][attrs['POSITION']]
        require(np.array_equal(acc['min'], positions.min(0)) and np.array_equal(acc['max'], positions.max(0)),
                f'{label}: false accessor bounds')
        faces = indices.reshape(-1,3)
        p = positions[faces]
        bounds.append(_geometry(p, meta))
        cross = np.cross(p[:,1]-p[:,0], p[:,2]-p[:,0])
        magnitude = np.linalg.norm(cross,axis=1)
        require(np.all(magnitude>1e-12), f'{label}: degenerate triangle')
        require(np.allclose(normals[faces], (cross/magnitude[:,None])[:,None,:], atol=2e-4),
                f'{label}: normal disagrees with winding')
        _, position_ids = np.unique(positions, axis=0, return_inverse=True)
        canonical = np.sort(position_ids.reshape(-1)[faces], axis=1)
        require(len(np.unique(canonical,axis=0)) == len(faces), f'{label}: duplicate geometric face')
        pixels = uv * atlas.SIZE
        cell = np.floor(pixels / [atlas.CELL_W, atlas.CELL_H]).astype('i4')
        require(np.all(cell[faces]==cell[faces[:,0],None,:]) and np.all((cell[:,0]>=0)&(cell[:,0]<16))
                and np.all((cell[:,1]//3)==age_index), f'{label}: triangle crosses patch or incorrect age row')
        local = pixels - cell*[atlas.CELL_W, atlas.CELL_H]
        require(np.all(local>=atlas.GUTTER) and np.all(local<=[atlas.CELL_W-atlas.GUTTER,atlas.CELL_H-atlas.GUTTER]),
                f'{label}: UV outside inset patch')
        uv_edges = uv[faces[:,1:]] - uv[faces[:,0],None,:]
        uv_area = uv_edges[:,0,0]*uv_edges[:,1,1] - uv_edges[:,0,1]*uv_edges[:,1,0]
        require(np.all(np.abs(uv_area)>1e-12), f'{label}: collapsed UV triangle')
        counts.append(len(faces))
    hi, mid, low = counts
    require(0 < low < mid < hi <= _budget(meta), f'{meta.id}: invalid LOD chain {counts}, budget {_budget(meta)}')
    require(.32*hi-1 <= mid <= .68*hi+1 and low <= max(32,.32*hi+1),
            f'{meta.id}: LOD triangle ratios {counts} must approximate 50% and 20% (or simple silhouette)')
    memory = _shared_files()
    return dict(triangles=hi,lod_triangles=counts,bounds=bounds[0],bytes=len(data),texture_bytes_astc=memory)


def validate_glb(path, meta=None):
    """Re-open the file and validate actual serialized indices, geometry, UVs, LODs, references and sizes."""
    try:
        return _serialized(Path(path).read_bytes(), path, meta)
    except (KeyError, IndexError, TypeError, ValueError) as error:
        raise AssetError(f'{Path(path).name}: {error}') from error


def _atomic(path, payload):
    path = Path(path)
    path.parent.mkdir(parents=True,exist_ok=True)
    with NamedTemporaryFile(dir=path.parent, delete=False) as out:
        tmp = Path(out.name)
        try:
            out.write(payload)
            out.close()
            tmp.replace(path)
        finally:
            tmp.unlink(missing_ok=True)


def publish(builder, meta: AssetMeta, generator: str, preview: bool = True, *, lods, preview_size=160):
    _meta(meta)
    if len(lods) != 2:
        raise AssetError(f'{meta.id}: supply two authored lower LOD builders')
    out = Path(MODELS) / meta.category / (meta.id+'.glb')
    base_age = (meta.ages or ['dawn'])[0]
    levels = [(b.tris, atlas.uv_for(b.tris,base_age,meta.race), b.tint) for b in (builder,*lods)]
    data = gltf.encode(levels,out,dict(id=meta.id,godsim=asdict(meta)))
    stats = _serialized(data,out,meta)
    _atomic(out,data)
    age_previews = {}
    if preview:
        directory = Path(PREVIEWS) / meta.category
        directory.mkdir(parents=True,exist_ok=True)
        for age in meta.ages or [base_age]:
            stem = meta.id if age == base_age else f'{meta.id}__{age}'
            path = directory / (stem+'.png')
            uv = levels[0][1].copy()
            uv[:,:,1] += (style.AGES.index(age)-style.AGES.index(base_age))*atlas.ROW_UV
            render(builder.tris,path,size=preview_size,uv=uv,tint=builder.tint)
            age_previews[age] = path.relative_to(ROOT).as_posix()
    entry = dict(asdict(meta), **stats, path=out.relative_to(ROOT).as_posix(), preview=age_previews.get(base_age),
                 age_previews=age_previews)
    _append_manifest(generator,entry)
    return entry


def _fragment(generator):
    require(ID_RE.fullmatch(generator), 'invalid generator name')
    return Path(MANIFESTS) / (generator+'.json')


def _append_manifest(generator,entry):
    path = _fragment(generator)
    data = json.loads(path.read_bytes()) if path.exists() else []
    data = [e for e in data if e['id'] != entry['id']] + [entry]
    _atomic(path,json.dumps(sorted(data,key=lambda e:e['id']),indent=1).encode())


def reset_manifest(generator):
    _atomic(_fragment(generator),b'[]')
