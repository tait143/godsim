"""Reduce an over-budget middle LOD along its existing, locally manifold edges."""
from collections import defaultdict

import numpy as np

from .mesh import Tri


def middle_lod(triangles, high_count):
    if 5 * len(triangles) <= 3 * high_count:
        return triangles
    target = high_count * 57 // 100
    points = np.stack([t.p for t in triangles]).astype("<f4").astype("f8")
    vertices, flat = np.unique(points.reshape(-1, 3), axis=0, return_inverse=True)
    extent = np.ptp(vertices, axis=0).max()
    faces = flat.reshape(-1, 3)
    originals = np.arange(len(faces))
    roles = np.array([t.material for t in triangles])
    shells = None
    while len(faces) > target:
        corners = vertices[faces]
        normals = np.cross(corners[:,1] - corners[:,0], corners[:,2] - corners[:,0])
        areas = np.linalg.norm(normals, axis=1)
        normals /= areas[:,None]
        around = defaultdict(list)
        edges = defaultdict(list)
        for idx, (a, b, c) in enumerate(faces.tolist()):
            for v in (a,b,c):
                around[v].append(idx)
            for u,v in ((a,b),(b,c),(c,a)):
                edges[tuple(sorted((u,v)))].append(idx)
        if shells is None:
            shells, volumes = _closed_shells(corners, edges)
            minimum_volumes = volumes * 1e-9
        rim = {v for edge, group in edges.items() if len(group) != 2 for v in edge}
        neighbours = {v: set(faces[fs].ravel()) - {v} for v,fs in around.items()}
        mats = {v: set(roles[originals[fs]]) for v,fs in around.items()}
        candidates = []
        for (u,v), shared in edges.items():
            if len(shared) != 2 or (u in rim and v in rim):
                continue
            opposite = set(faces[shared].ravel()) - {u,v}
            if neighbours[u] & neighbours[v] != opposite:
                continue
            for drop,keep in ((u,v),(v,u)):
                if drop in rim or not mats[drop] <= mats[keep]:
                    continue
                incident = around[drop]
                delta = vertices[drop] - vertices[keep]
                deviation = np.max(np.abs(normals[incident] @ delta))
                cost = deviation * deviation * 12 + delta @ delta
                candidates.append((cost,drop,keep,shared))
        touched = set()
        removed = set()
        for _,drop,keep,shared in sorted(candidates):
            if drop in touched or keep in touched:
                continue
            adjacent = [n for n in around[drop] if n not in shared]
            if not adjacent:
                continue
            updated = np.where(faces[adjacent] == drop, keep, faces[adjacent])
            # Reject duplicate triangles (in particular, the last faces of small
            # solids) and directions that would fold a roof or flatten a support.
            remaining = [n for n in around[keep] if n not in shared]
            keys = {tuple(sorted(f)) for f in faces[remaining]}
            if any(tuple(sorted(f)) in keys for f in updated):
                continue
            q = vertices[updated]
            cross = np.cross(q[:,1]-q[:,0],q[:,2]-q[:,0])
            size = np.linalg.norm(cross,axis=1)
            if np.any(size <= 1e-10) or np.any(np.sum(cross*normals[adjacent],axis=1) < .72*size):
                continue
            # Thin components are especially deceptive: a short motion down the
            # side of a box can erase half of its long top. Compare the oriented
            # area lost in each major plane, including the two removed faces.
            incident = adjacent + shared
            large = areas[incident] > .012 * extent**2
            if np.any(large):
                before = normals[incident] * areas[incident,None]
                axes = normals[np.asarray(incident)[large]]
                original_contribution = np.maximum(0, axes @ before.T).sum(axis=1)
                new_contribution = np.maximum(0, axes @ cross.T).sum(axis=1)
                lost = original_contribution-new_contribution
                displacement = vertices[keep] - vertices[drop]
                moved = np.abs(axes @ displacement)
                if np.any((lost > .004 * extent**2) | (moved > .023*extent)):
                    continue
                # Opposing broad caps can keep their projected area even as a
                # short cross-thickness edge tilts both into a diagonal wedge.
                across = (moved > .004*extent) & (moved > .96*np.linalg.norm(displacement))
                if np.any(across):
                    keep_faces = np.asarray(around[keep])
                    broad = keep_faces[areas[keep_faces] > .012 * extent**2]
                    if np.any(axes[across] @ normals[broad].T < -.99999):
                        continue
            shell = shells[originals[shared[0]]]
            # Collapsing an edge in one fan must not move a different solid
            # attached only at that vertex.
            if any(shells[originals[i]] != shell for i in incident):
                continue
            if shell >= 0:
                # A short displacement can turn a small closed dish inside out
                # while every surviving triangle still passes the normal test.
                # For a closed vertex fan the change in six times signed volume
                # is displacement dotted with the fan's summed area vectors.
                disp = vertices[keep] - vertices[drop]
                change = disp @ (normals[incident] * areas[incident,None]).sum(axis=0)
                if volumes[shell] + change <= minimum_volumes[shell]:
                    continue
                volumes[shell] += change
            faces[adjacent] = updated
            touched.update(neighbours[drop] | neighbours[keep] | {drop,keep})
            removed.update(shared)
            if len(faces)-len(removed) <= target:
                break
        if not removed:
            break
        retained = [i for i in range(len(faces)) if i not in removed]
        faces, originals = faces[retained], originals[retained]
    if 5 * len(faces) > 3 * high_count:
        retained = _retain_structures(vertices, faces, 3*high_count//5)
        faces, originals = faces[retained], originals[retained]
    return [Tri(vertices[face], triangles[i].color, triangles[i].material, triangles[i].local_uv)
            for face,i in zip(faces,originals)]


def _closed_shells(points, edges):
    adjacency = [[] for _ in points]
    boundary = set()
    for group in edges.values():
        if len(group) == 2:
            a, b = group
            adjacency[a].append(b)
            adjacency[b].append(a)
        else:
            boundary.update(group)
    labels = np.full(len(points), -1, dtype=int)
    volumes = []
    seen = set()
    for start in range(len(points)):
        if start in seen:
            continue
        component = [start]
        seen.add(start)
        for face in component:
            for other in adjacency[face]:
                if other not in seen:
                    seen.add(other)
                    component.append(other)
        if boundary.isdisjoint(component):
            p = points[component] - points[start,0]
            volume = np.einsum("ij,ij->", p[:,0], np.cross(p[:,1], p[:,2]))
            if volume > 0:
                labels[component] = len(volumes)
                volumes.append(volume)
    return labels, np.array(volumes)


def _retain_structures(vertices, faces, limit):
    """Retire compact detached details before disturbing the structural surface."""
    incident = defaultdict(list)
    for index, face in enumerate(faces.tolist()):
        for vertex in face:
            incident[vertex].append(index)
    seen = set()
    components = []
    for start in range(len(faces)):
        if start in seen:
            continue
        stack = [start]
        component = []
        while stack:
            face = stack.pop()
            if face in seen:
                continue
            seen.add(face)
            component.append(face)
            for vertex in faces[face]:
                stack.extend(incident[vertex])
        bounds = np.ptp(vertices[faces[component]].reshape(-1,3), axis=0)
        components.append((np.linalg.norm(bounds), start, component))
    keep = np.ones(len(faces), dtype=bool)
    count = len(faces)
    for _, _, component in sorted(components):
        if count <= limit:
            break
        if len(component) < count:
            keep[component] = False
            count -= len(component)
    return keep
