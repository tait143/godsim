"""Minimal, dependency-free glTF 2.0 binary (.glb) writer for flat-shaded vertex-coloured meshes.

One mesh with one primitive per material ("base", "emissive", ...). Attributes: POSITION, NORMAL, COLOR_0
(linear RGBA float). Non-shared vertices (flat shading). Unity imports these with glTFast
(com.unity.cloud.gltfast); vertex colours are linear per the glTF spec.
"""
from __future__ import annotations

import json
import struct
from collections import OrderedDict

import numpy as np

MATERIALS = {
    'base': dict(name='GodSim_VertexColor', pbrMetallicRoughness=dict(baseColorFactor=[1, 1, 1, 1], metallicFactor=0.0, roughnessFactor=0.85)),
    'metal': dict(name='GodSim_Metal', pbrMetallicRoughness=dict(baseColorFactor=[1, 1, 1, 1], metallicFactor=0.7, roughnessFactor=0.45)),
    'glass': dict(name='GodSim_Glass', pbrMetallicRoughness=dict(baseColorFactor=[1, 1, 1, 1], metallicFactor=0.1, roughnessFactor=0.15)),
    'emissive': dict(name='GodSim_Emissive', pbrMetallicRoughness=dict(baseColorFactor=[1, 1, 1, 1], metallicFactor=0.0, roughnessFactor=0.6), emissiveFactor=[1.0, 0.85, 0.55]),
}


def srgb_to_linear(c: np.ndarray) -> np.ndarray:
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= 0.04045, c / 12.92, ((c + 0.055) / 1.055) ** 2.4)


def write_glb(path: str, tris, extras: dict = None) -> dict:
    """Writes `tris` (list of forge.mesh.Tri) to a .glb file. Returns basic stats."""
    groups: "OrderedDict[str, list]" = OrderedDict()
    for t in tris:
        groups.setdefault(t.material if t.material in MATERIALS else 'base', []).append(t)
    bin_chunks = []
    offset = 0
    buffer_views, accessors, primitives, materials = [], [], [], []
    mat_index = {}

    def add_view(data: bytes, target: int = None):
        nonlocal offset
        pad = (-len(data)) % 4
        bv = dict(buffer=0, byteOffset=offset, byteLength=len(data))
        if target:
            bv['target'] = target
        buffer_views.append(bv)
        bin_chunks.append(data + b'\x00' * pad)
        offset += len(data) + pad
        return len(buffer_views) - 1

    total_tris = 0
    for mat, ts in groups.items():
        P = np.concatenate([t.p for t in ts]).astype(np.float32)          # (3n,3)
        n = len(ts)
        total_tris += n
        a, b, c = P[0::3], P[1::3], P[2::3]
        N = np.cross(b - a, c - a)
        ln = np.linalg.norm(N, axis=1, keepdims=True)
        ln[ln == 0] = 1
        N = (N / ln).astype(np.float32)
        N = np.repeat(N, 3, axis=0)
        cols = []
        for t in ts:
            col = list(t.color[:3]) + [t.color[3] if len(t.color) > 3 else 1.0]
            cols.append(col)
        C = np.array(cols, dtype=np.float64)
        C[:, :3] = srgb_to_linear(C[:, :3])
        C = np.repeat(C.astype(np.float32), 3, axis=0)
        idx = np.arange(len(P), dtype=np.uint32)

        pv = add_view(P.tobytes(), 34962)
        nv = add_view(N.tobytes(), 34962)
        cv = add_view(C.tobytes(), 34962)
        iv = add_view(idx.tobytes(), 34963)
        accessors.append(dict(bufferView=pv, componentType=5126, count=len(P), type='VEC3', min=P.min(0).tolist(), max=P.max(0).tolist()))
        pa = len(accessors) - 1
        accessors.append(dict(bufferView=nv, componentType=5126, count=len(N), type='VEC3'))
        na = len(accessors) - 1
        accessors.append(dict(bufferView=cv, componentType=5126, count=len(C), type='VEC4'))
        ca = len(accessors) - 1
        accessors.append(dict(bufferView=iv, componentType=5125, count=len(idx), type='SCALAR'))
        ia = len(accessors) - 1
        if mat not in mat_index:
            mat_index[mat] = len(materials)
            materials.append(MATERIALS[mat])
        primitives.append(dict(attributes=dict(POSITION=pa, NORMAL=na, COLOR_0=ca), indices=ia, material=mat_index[mat], mode=4))

    gltf = dict(
        asset=dict(version='2.0', generator='GodSim AssetForge 1.0'),
        scene=0,
        scenes=[dict(nodes=[0])],
        nodes=[dict(mesh=0, name=(extras or {}).get('id', 'asset'))],
        meshes=[dict(primitives=primitives, name=(extras or {}).get('id', 'asset'))],
        materials=materials,
        accessors=accessors,
        bufferViews=buffer_views,
        buffers=[dict(byteLength=offset)],
    )
    if extras:
        gltf['nodes'][0]['extras'] = extras
    js = json.dumps(gltf, separators=(',', ':')).encode('utf-8')
    js += b' ' * ((-len(js)) % 4)
    binary = b''.join(bin_chunks)
    total = 12 + 8 + len(js) + 8 + len(binary)
    with open(path, 'wb') as f:
        f.write(struct.pack('<III', 0x46546C67, 2, total))
        f.write(struct.pack('<II', len(js), 0x4E4F534A))
        f.write(js)
        f.write(struct.pack('<II', len(binary), 0x004E4942))
        f.write(binary)
    return dict(triangles=total_tris, bytes=total)


def read_glb_summary(path: str) -> dict:
    """Parses a .glb header + JSON chunk (validation helper)."""
    with open(path, 'rb') as f:
        data = f.read()
    magic, version, length = struct.unpack_from('<III', data, 0)
    if magic != 0x46546C67 or version != 2 or length != len(data):
        raise ValueError('not a valid GLB 2.0 file')
    jlen, jtype = struct.unpack_from('<II', data, 12)
    if jtype != 0x4E4F534A:
        raise ValueError('first chunk is not JSON')
    js = json.loads(data[20:20 + jlen])
    return js
