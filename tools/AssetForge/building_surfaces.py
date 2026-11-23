"""Inspect exported GodSim LODs for positive-area overlaps between co-oriented triangles.

Coincident edges and opposing contact faces are excluded. A candidate buried inside
another component may be harmless, so inspect the reported surface in the model.
"""
import hashlib
import json
from collections import defaultdict
from pathlib import Path
from functools import lru_cache

import numpy as np

from building_scope import ROOT
from forge import atlas, gltf
from forge.registry import _access


def cross2(a, b):
    return a[0] * b[1] - a[1] * b[0]


def intersection(subject, clip):
    """Clip a triangle against the three inward half-planes of a second triangle."""
    polygon = list(subject)
    if cross2(clip[1] - clip[0], clip[2] - clip[0]) < 0:
        clip = clip[::-1]
    for start, end in zip(clip, np.roll(clip, -1, axis=0)):
        if len(polygon) < 3:
            break
        clipped = []
        edge = end - start
        for a, b in zip(polygon, polygon[1:] + polygon[:1]):
            da, db = cross2(edge, a - start), cross2(edge, b - start)
            if (da >= 0) != (db >= 0):
                clipped.append(a + (b - a) * (da / (da - db)))
            if db >= 0:
                clipped.append(b)
        polygon = clipped
    return polygon


def overlaps(points, min_area=1e-7):
    points = np.asarray(points, dtype='f8')
    normals = np.cross(points[:, 1] - points[:, 0], points[:, 2] - points[:, 0])
    lengths = np.linalg.norm(normals, axis=1)
    if np.any(lengths == 0):
        raise ValueError('degenerate triangle; run building_review.py for asset validation')
    normals /= lengths[:, None]
    lower, upper = points.min(axis=1), points.max(axis=1)
    matches = []
    for i, normal in enumerate(normals[:-1]):
        # Float32 serialization can perturb an originally flat authored plane.
        tolerance = 2e-7
        candidates = np.flatnonzero(
            np.all(lower[i + 1:] <= upper[i] + tolerance, axis=1)
            & np.all(upper[i + 1:] >= lower[i] - tolerance, axis=1)
            & np.all(np.abs(normals[i + 1:] - normal) < 2e-5, axis=1)
        ) + i + 1
        offsets = (points[candidates] - points[i, 0]) @ normal
        axis = int(np.argmax(np.abs(normal)))
        keep = [d for d in range(3) if d != axis]
        a = points[i][:, keep]
        for j in candidates[np.all(np.abs(offsets) < tolerance, axis=1)]:
            b = points[j][:, keep]
            if np.any(np.minimum(a.max(0), b.max(0)) <= np.maximum(a.min(0), b.min(0))):
                continue
            polygon = intersection(a, b)
            if len(polygon) < 3:
                continue
            area = abs(sum(cross2(v, w) for v, w in zip(polygon, polygon[1:] + polygon[:1])))
            area /= 2 * abs(normal[axis])
            if area <= min_area:
                continue
            point = points[i, 0].copy()
            point[keep] = np.mean(polygon, axis=0)
            point[axis] -= np.dot(point - points[i, 0], normal) / normal[axis]
            matches.append((area, i, int(j), point))
    return sorted(matches, key=lambda match: -match[0])


def outward_ray_hits_surface(point, normal, triangles):
    """Two-sided ray/triangle test, ignoring surfaces touching the ray origin."""
    edges, other_edges = triangles[:, 1] - triangles[:, 0], triangles[:, 2] - triangles[:, 0]
    perpendicular = np.cross(normal, other_edges)
    determinant = np.einsum('ij,ij->i', edges, perpendicular)
    valid = np.abs(determinant) > 1e-12
    offset = point - triangles[valid, 0]
    relative_cross = np.cross(offset, edges[valid])
    u = np.einsum('ij,ij->i', offset, perpendicular[valid]) / determinant[valid]
    v = (relative_cross @ normal) / determinant[valid]
    distance = np.einsum('ij,ij->i', relative_cross, other_edges[valid]) / determinant[valid]
    return np.any((distance > 1e-6) & (u >= 0) & (v >= 0) & (u + v <= 1))


def geometry_levels(payload):
    doc, binary = gltf.unpack(payload)
    for mesh in doc['meshes']:
        primitive = mesh['primitives'][0]
        attrs = primitive['attributes']
        faces = _access(doc, binary, primitive['indices'], 5123, 'SCALAR').reshape(-1, 3)
        points = _access(doc, binary, attrs['POSITION'], 5126, 'VEC3')[faces].astype('f8')
        uv = _access(doc, binary, attrs['TEXCOORD_0'], 5126, 'VEC2')[faces[:, 0]]
        col, row = (uv * atlas.SIZE / [atlas.CELL_W, atlas.CELL_H]).astype(int).T
        slots = row % (atlas.ROW // atlas.CELL_H) * (atlas.SIZE // atlas.CELL_W) + col
        yield mesh["name"], points, slots


SURVEY = ROOT / "tools/AssetForge/reviews/surface_checks.json"
VERSION = 1


@lru_cache(maxsize=1)
def survey():
    if SURVEY.exists():
        saved = json.loads(SURVEY.read_text())
        if saved["version"] == VERSION:
            return saved["models"]
    return {}


def findings(payload):
    """Retain a repair location and the number of exposed intersections for each LOD."""
    result = []
    for lod, points, slots in geometry_levels(payload):
        first = None
        count = 0
        for area, i, j, point in overlaps(points):
            n = np.cross(points[i,1]-points[i,0], points[i,2]-points[i,0])
            if outward_ray_hits_surface(point, n / np.linalg.norm(n), points):
                continue
            count += 1
            if first is None:
                first = dict(lod=lod, faces=[i,j], materials=[atlas.SLOTS[slots[i]],atlas.SLOTS[slots[j]]],
                             point=[round(float(x),6) for x in point], area=round(float(area),9))
        if first is not None:
            result.append(dict(first, pairs=count))
    return result


def inverted_shells(points):
    """A closed, consistently wound shell with negative signed volume faces inward."""
    _, welded = np.unique(np.round(points, 6).reshape(-1,3), axis=0, return_inverse=True)
    edges = defaultdict(list)
    for face, vertices in enumerate(welded.reshape(-1, 3).tolist()):
        for a, b in zip(vertices, vertices[1:] + vertices[:1]):
            edges[min(a,b), max(a,b)].append((face, a < b))
    neighbors = defaultdict(list)
    boundaries = set()
    for incidences in edges.values():
        if len(incidences) == 2 and incidences[0][1] != incidences[1][1]:
            a, b = incidences[0][0], incidences[1][0]
            neighbors[a].append(b)
            neighbors[b].append(a)
        else:
            boundaries.update(face for face, direction in incidences)
    seen = set()
    for first in neighbors:
        if first in seen:
            continue
        stack = [first]
        seen.add(first)
        component = []
        while stack:
            face = stack.pop()
            component.append(face)
            for other in neighbors[face]:
                if other not in seen:
                    seen.add(other)
                    stack.append(other)
        if any(face in boundaries for face in component):
            continue
        faces = points[component]
        relative = faces - faces[0,0]
        volume = np.einsum("ij,ij->i", relative[:,0], np.cross(relative[:,1], relative[:,2])).sum() / 6
        if volume < -1e-8:
            yield dict(face=int(first), count=len(component), volume=round(float(volume),9),
                       center=[round(float(x),6) for x in faces.mean(axis=(0,1))])


def check_orientation(payload):
    return [dict(lod=lod, **shell) for lod, points, _ in geometry_levels(payload)
            for shell in inverted_shells(points)]


def refresh(models):
    records = dict(survey())
    changed = False
    for model in models:
        path = ROOT / "Assets/GodSim/Art/Models" / model.category / (model.id + ".glb")
        if not path.exists():
            continue
        payload = path.read_bytes()
        digest = hashlib.sha256(payload).hexdigest()
        row = records.get(model.id)
        if row is None or row["sha256"] != digest:
            row = dict(sha256=digest, findings=findings(payload))
            records[model.id] = row
        if "inverted" not in row:
            row["inverted"] = check_orientation(payload)
            changed = True
    if changed:
        tmp = SURVEY.with_suffix(".tmp")
        tmp.write_text(json.dumps(dict(version=VERSION, models=records), sort_keys=True, indent=1) + "\n")
        tmp.replace(SURVEY)
        survey.cache_clear()
    return records


def current(entry):
    record = survey().get(entry["id"])
    if record and record["sha256"] == hashlib.sha256((ROOT / entry["path"]).read_bytes()).hexdigest():
        return record
    return None


def shape_digest(points):
    """Compare geometry across exports independently of UV seams, triangle order and winding."""
    vertices = np.rint(points * 1_000_000).astype("<i4")
    order = np.lexsort((vertices[:,:,2], vertices[:,:,1], vertices[:,:,0]), axis=1)
    faces = np.take_along_axis(vertices, order[:,:,None], axis=1).reshape(-1, 9)
    return hashlib.sha256(faces[np.lexsort(faces.T[::-1])].tobytes()).hexdigest()


def repeated_shapes(models):
    groups = defaultdict(list)
    for model in models:
        path = ROOT / "Assets/GodSim/Art/Models" / model.category / (model.id + ".glb")
        if not path.exists():
            continue
        _, points, _ = next(geometry_levels(path.read_bytes()))
        groups[(model.building_type, shape_digest(points))].append(model.id)
    return {ident: [other for other in ids if ident != other]
            for ids in groups.values() if len(ids) > 1 for ident in ids}


if __name__ == "__main__":
    from building_scope import specifications
    records = refresh(specifications())
    print(f"{len(records)} models checked, {sum(bool(row['findings']) for row in records.values())} with exposed overlaps")
