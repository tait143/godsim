"""Regression tests for mirrored winding, atlas rows and validation of serialized bytes."""
import io
import json
from pathlib import Path
import struct
import sys
import unittest

import numpy as np
from PIL import Image

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from forge import MeshBuilder, atlas, gltf, registry
from forge.lod import middle_lod
from forge.render import render
from generators._samples.sample_house import human_house


class BuildingPipelineTests(unittest.TestCase):
    sample = Path(registry.MODELS) / '_Samples/sample_human_house_dawn.glb'

    def test_reference_glbs_and_regeneration(self):
        for age in ('dawn', 'medieval', 'industrial'):
            path = self.sample.with_name(f'sample_human_house_{age}.glb')
            stats = registry.validate_glb(path)
            self.assertEqual(stats['texture_bytes_astc'], 666384)
            doc, _ = gltf.unpack(path.read_bytes())
            levels = []
            for lod in range(3):
                builder = human_house(age, 0, lod)
                uv = atlas.uv_for(builder.tris, age, 'human')
                levels.append((builder.tris, uv, builder.tint))
            self.assertEqual(gltf.encode(levels, path, doc['nodes'][0]['extras']), path.read_bytes())

    def test_convex_surfaces_point_out_including_after_a_reflection(self):
        color = (1, 1, 1)
        shapes = [
            ('box', ((0,.5,0), (1,1,1), color), dict(bottom=True), (0,.5,0)),
            ('cylinder', ((0,0,0), .5, 1, color), dict(cap_bottom=True), (0,.5,0)),
            ('cone', ((0,0,0), .5, 1, color), {}, (0,.2,0)),
            ('sphere', ((0,.5,0), .5, color), {}, (0,.5,0)),
            ('dome', ((0,0,0), .5, color), {}, (0,.15,0)),
            ('gable_roof', ((0,0,0), .8, .6, .6, color), {}, (0,.2,0)),
            ('hip_roof', ((0,0,0), .8, .6, .6, color), {}, (0,.2,0)),
            ('pyramid', ((0,0,0), .8, .6, .6, color), {}, (0,.2,0)),
            ('wedge', ((0,0,0), .8, .6, .6, color), {}, (0,.16,.11)),
        ]
        for name, args, options, interior in shapes:
            for mirror in (1, -1):
                with self.subTest(primitive=name, mirror=mirror):
                    builder = MeshBuilder()
                    with builder.transform(scale=(mirror,1,1)):
                        getattr(builder, name)(*args, **options)
                    vertices = np.stack([tri.p for tri in builder.tris])
                    normals = np.cross(vertices[:,1]-vertices[:,0], vertices[:,2]-vertices[:,0])
                    outward = np.sum(normals * (vertices.mean(1) - interior), axis=1)
                    self.assertTrue(np.all(outward > 1e-9))

    def test_door_and_window_face_negative_z(self):
        builder = MeshBuilder()
        builder.door(0, -.4, .3, .5, arch=True)
        builder.window(.2, .4, -.4, .2, .2, frame=(1,1,1))
        vertices = np.stack([tri.p for tri in builder.tris])
        normals = np.cross(vertices[:,1]-vertices[:,0], vertices[:,2]-vertices[:,0])
        self.assertTrue(np.all(normals[:,2] < 0))

    def test_atlas_recolor_and_camera_bounds(self):
        builder = human_house('dawn', 0)
        uv = atlas.uv_for(builder.tris, 'dawn', 'human')

        def view(row=0, yaw=215):
            pixels = io.BytesIO()
            shifted = uv.copy()
            shifted[:,:,1] += row * atlas.ROW_UV
            render(builder.tris, pixels, size=128, yaw=yaw, uv=shifted)
            with Image.open(pixels) as image:
                return np.array(image)

        first = view()
        for row in range(1, 10):
            self.assertGreater(np.count_nonzero(view(row) != first), 2000)
        for yaw in (0,45,90,145,180,215,270,315):
            image = view(yaw=yaw).astype(float)
            visible = np.linalg.norm(image - image[:,:1], axis=2) > 80
            y, x = np.nonzero(visible)
            self.assertGreater(min(x.min(), y.min()), 0)
            self.assertLess(max(x.max(), y.max()), 127)

    def test_validator_rejects_invalid_serialized_output(self):
        def change_first(doc, binary, attribute, value):
            index = doc['meshes'][0]['primitives'][0]['attributes'][attribute]
            view = doc['bufferViews'][doc['accessors'][index]['bufferView']]
            struct.pack_into('<f', binary, view['byteOffset'], value)

        attacks = {
            'external image': lambda d,b: d['images'][0].update(uri='model.png'),
            'extra image': lambda d,b: d['images'].append(dict(bufferView=0)),
            'material': lambda d,b: d['materials'][0].update(emissiveTexture=dict(index=0)),
            'default scene draws two LODs': lambda d,b: d['scenes'][0]['nodes'].append(1),
            'UV': lambda d,b: change_first(d,b,'TEXCOORD_0',.999),
            'position': lambda d,b: change_first(d,b,'POSITION',float('nan')),
            'normal': lambda d,b: change_first(d,b,'NORMAL',.9),
            'accessor bounds': lambda d,b: d['accessors'][0].update(count=1000000),
        }
        for label, attack in attacks.items():
            with self.subTest(fault=label):
                doc, original = gltf.unpack(self.sample.read_bytes())
                binary = bytearray(original)
                attack(doc, binary)
                encoded = json.dumps(doc, separators=(',', ':')).encode()
                encoded += b' ' * (-len(encoded) % 4)
                header = struct.pack('<4sII', b'glTF', 2, 28 + len(encoded) + len(binary))
                data = (header + struct.pack('<I4s', len(encoded), b'JSON') + encoded +
                        struct.pack('<I4s', len(binary), b'BIN\0') + binary)
                with self.assertRaises(registry.AssetError):
                    registry._serialized(data, self.sample)

    def test_middle_lod_ceiling_applies_to_the_actual_exported_index_count(self):
        doc,_ = gltf.unpack(self.sample.read_bytes())
        extras = doc["nodes"][0]["extras"]
        builders = [human_house("dawn",0,i) for i in range(3)]
        high = builders[0].tris
        count = len(high)*3//5
        def payload(n):
            levels = [(t,atlas.uv_for(t,"dawn","human"),None)
                      for t in (high,high[:n],builders[2].tris)]
            return gltf.encode(levels,self.sample,extras)
        self.assertEqual(registry._serialized(payload(count),self.sample)["lod_triangles"][1],count)
        with self.assertRaisesRegex(registry.AssetError,"LOD triangle ratios"):
            registry._serialized(payload(count+1),self.sample)

    def test_reduction_preserves_large_wall_roof_and_tread_planes(self):
        builder = MeshBuilder()
        color = (1, 1, 1)
        builder.box((0,.35,0), (1.5,.7,1.1), color, material='wall', bottom=True)
        builder.gable_roof((0,.715,0), 1.74, 1.28, .33, color, material='roof')
        builder.box((0,.7,0), (1.77,.03,1.32), color, material='eave', bottom=True)
        for j in range(3):
            builder.box((0,(j+.5)*.03,-.66-j*.11), (.83,.03,.13), color,
                        material='step', bottom=True)
        for i in range(4):
            builder.cylinder((i*.18-.52,.03,.81), .05, .035, color, segments=5,
                             top_radius=.08, cap_bottom=True, material='gold')
        high_count = len(builder.tris) + 12
        reduced = middle_lod(builder.tris, high_count)
        self.assertLessEqual(5 * len(reduced), 3 * high_count)
        # Measure actual area on each broad original plane. A diagonal slice
        # can preserve both the bounds and the silhouette of an eave or stair.
        def area_on_plane(tris, point, normal):
            planar = tris[np.max(np.abs((tris-point) @ normal), axis=1) < 1e-6]
            vectors = np.cross(planar[:,1]-planar[:,0], planar[:,2]-planar[:,0])
            return np.maximum(0, vectors @ normal).sum() / 2

        for role in ('wall', 'roof', 'eave', 'step'):
            old, new = [np.stack([t.p for t in tris if t.material == role])
                        for tris in (builder.tris, reduced)]
            for face in old:
                cross = np.cross(face[1]-face[0], face[2]-face[0])
                if np.linalg.norm(cross) < .08:
                    continue
                normal = cross / np.linalg.norm(cross)
                with self.subTest(role=role, normal=normal):
                    self.assertAlmostEqual(area_on_plane(old, face[0], normal),
                                           area_on_plane(new, face[0], normal), places=6)

    def test_reduction_keeps_each_flared_dish_closed_and_facing_outward(self):
        builder = MeshBuilder()
        color = (1, 1, 1)
        builder.box((0,.04,0), (1.4,.08,1), color, bottom=True)
        for role, x in [('left', -.255), ('right', .255)]:
            builder.cylinder((x,.1157,-.085), .0309, .024, color, segments=5,
                             top_radius=.0456, cap_bottom=True, material=role)
        reduced = middle_lod(builder.tris, 50)
        self.assertLessEqual(len(reduced), 30)
        for role in ('left', 'right'):
            with self.subTest(dish=role):
                p = np.stack([t.p for t in reduced if t.material == role])
                _, faces = np.unique(p.reshape(-1, 3), axis=0, return_inverse=True)
                faces = faces.reshape(-1, 3)
                edges = np.concatenate([faces[:,[0,1]], faces[:,[1,2]], faces[:,[2,0]]])
                directed = sorted(map(tuple, edges))
                self.assertEqual(directed, sorted(map(tuple, edges[:,::-1])))
                relative = p - p[0,0]
                volume = np.einsum('ij,ij->', relative[:,0],
                                   np.cross(relative[:,1], relative[:,2])) / 6
                self.assertGreater(volume, 0)


if __name__ == '__main__':
    unittest.main()
