"""glTF 2.0 building export. Each scene is independently loadable and contains one LOD."""
from pathlib import Path
import os
import json
import struct

import numpy as np

from . import atlas

MATERIAL_NAME = 'GodSim_Buildings'
SAMPLER = dict(magFilter=9729, minFilter=9987, wrapS=33071, wrapT=33071)
MATERIAL = dict(name=MATERIAL_NAME, pbrMetallicRoughness=dict(baseColorTexture=dict(index=0),
                metallicFactor=0.0, roughnessFactor=.86), emissiveTexture=dict(index=1),
                emissiveFactor=[1, 1, 1])


def srgb_to_linear(c):
    c = np.clip(c, 0.0, 1.0)
    return np.where(c <= .04045, c/12.92, ((c+.055)/1.055)**2.4)


def image_uris(path):
    return [Path(os.path.relpath(p, Path(path).parent)).as_posix() for p in (atlas.ATLAS, atlas.MASK)]


def encode(levels, path, extras):
    """levels contains (triangles, packed UV array, optional linear RGB tint) tuples."""
    accessors, views, meshes, nodes, scenes = [], [], [], [], []
    binary = bytearray()

    def attribute(array, kind, component_type, target, bounds=False):
        raw = array.tobytes()
        views.append(dict(buffer=0, byteOffset=len(binary), byteLength=len(raw), target=target))
        binary.extend(raw)
        binary.extend(bytes(-len(binary) % 4))
        acc = dict(bufferView=len(views)-1, componentType=component_type, count=len(array), type=kind)
        if bounds:
            acc.update(min=array.min(0).tolist(), max=array.max(0).tolist())
        accessors.append(acc)
        return len(accessors)-1

    for level, (tris, uv, tint) in enumerate(levels):
        p = np.stack([t.p for t in tris]).astype('<f4')
        cross = np.cross(p[:, 1] - p[:, 0], p[:, 2] - p[:, 0])
        lengths = np.linalg.norm(cross, axis=1, keepdims=True)
        if not np.all(np.isfinite(lengths) & (lengths > 1e-12)):
            raise ValueError('degenerate or non-finite triangle after conversion to float32')
        normals = np.repeat(cross/lengths, 3, axis=0)
        vertices = np.column_stack((p.reshape(-1,3), normals, uv.reshape(-1,2))).astype('<f4')
        unique, indices = np.unique(vertices, axis=0, return_inverse=True)
        attrs = dict(POSITION=attribute(unique[:,:3], 'VEC3', 5126, 34962, True),
                     NORMAL=attribute(unique[:,3:6], 'VEC3', 5126, 34962),
                     TEXCOORD_0=attribute(unique[:,6:8], 'VEC2', 5126, 34962))
        if tint is not None:
            color = np.broadcast_to(np.array(tint, dtype='<f4'), (len(unique), 3))
            attrs['COLOR_0'] = attribute(color, 'VEC3', 5126, 34962)
        if len(unique) > 65535:
            raise ValueError('building exceeds 16-bit indices')
        indices = attribute(indices.astype('<u2').reshape(-1), 'SCALAR', 5123, 34963)
        label = f'LOD{level}'
        meshes.append(dict(name=label, primitives=[dict(attributes=attrs, indices=indices, mode=4, material=0)]))
        nodes.append(dict(name=label, mesh=level))
        scenes.append(dict(name=label, nodes=[level]))
    nodes[0]['extras'] = extras
    if len(levels) == 3:
        nodes[0]['extensions'] = {'MSFT_lod': {'ids': [1, 2]}}
    doc = dict(asset=dict(version='2.0', generator='GodSim AssetForge 2.0'), scene=0, scenes=scenes,
               nodes=nodes, meshes=meshes, samplers=[SAMPLER],
               images=[dict(uri=uri) for uri in image_uris(path)], textures=[dict(sampler=0,source=i) for i in (0,1)],
               materials=[MATERIAL], accessors=accessors, bufferViews=views, buffers=[dict(byteLength=len(binary))])
    if len(levels) == 3:
        doc['extensionsUsed'] = ['MSFT_lod']
    encoded = json.dumps(doc, separators=(',', ':'), allow_nan=False).encode('utf-8')
    encoded += b' ' * (-len(encoded) % 4)
    length = 28 + len(encoded) + len(binary)
    return (struct.pack('<4sII', b'glTF', 2, length) + struct.pack('<I4s', len(encoded), b'JSON') +
            encoded + struct.pack('<I4s', len(binary), b'BIN\0') + binary)


def write_glb(path: str, tris, extras=None):
    """Direct single-mesh export for isolated primitive previews. Use publish for building assets."""
    extras = extras or {}
    meta = extras.get('godsim', {})
    uv = atlas.uv_for(tris, (meta.get('ages') or ['dawn'])[0], meta.get('race'))
    data = encode([(tris, uv, None)], path, extras)
    Path(path).write_bytes(data)
    return dict(triangles=len(tris), bytes=len(data))


def unpack(data):
    if len(data) < 28 or struct.unpack_from('<4sII', data) != (b'glTF', 2, len(data)):
        raise ValueError('bad GLB 2.0 header or length')
    length, chunk = struct.unpack_from('<I4s', data, 12)
    end = 20 + length
    if chunk != b'JSON' or length % 4 or end+8 > len(data):
        raise ValueError('bad JSON chunk')
    n, chunk = struct.unpack_from('<I4s', data, end)
    if chunk != b'BIN\0' or n % 4 or end+8+n != len(data):
        raise ValueError('bad BIN chunk')
    doc = json.loads(data[20:end])
    if doc.get('buffers') != [dict(byteLength=n)]:
        raise ValueError('unexpected external buffer or mismatched binary length')
    return doc, memoryview(data)[end+8:]


def read_glb_summary(path):
    return unpack(Path(path).read_bytes())[0]
