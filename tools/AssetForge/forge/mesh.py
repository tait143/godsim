"""MeshBuilder: stylized low-poly modelling primitives with per-face colours (flat shaded).

Conventions (match Unity + GodSim tile space): Y is up, 1 unit = 1 tile, the ground is y = 0, footprints are
centred on the origin spanning x in [-w/2, w/2] and z in [-d/2, d/2], and the FRONT (door) faces -Z. All faces
are counter-clockwise when seen from outside (glTF front-face convention).

Usage::

    m = MeshBuilder(seed="human_house_dawn")
    m.box((0, 0.5, 0), (1.6, 1.0, 1.4), wall)
    with m.transform(translate=(0, 1.0, 0)):
        m.gable_roof((0, 0, 0), 1.8, 1.6, 0.7, roof)
    m.wobble(0.02)          # hand-crafted imperfection (deterministic)
"""
from __future__ import annotations

import contextlib
import math
from typing import List, Optional, Sequence, Tuple

import numpy as np

from . import style

Vec3 = Tuple[float, float, float]


class Tri:
    __slots__ = ('p', 'color', 'material', 'local_uv')

    def __init__(self, p: np.ndarray, color, material: str, local_uv):
        self.p = p            # (3,3) float64
        self.color = color    # (r,g,b) or (r,g,b,a) sRGB 0..1
        self.material = material
        self.local_uv = local_uv


class MeshBuilder:
    def __init__(self, seed: str = 'asset', tint=None):
        self.seed = seed
        self.tint = tint   # optional linear RGB multiplier, independent of atlas selection
        self.tris: List[Tri] = []
        self._stack: List[np.ndarray] = [np.eye(4)]
        self._face_counter = 0

    # ------------------------------------------------------------------ transforms
    @contextlib.contextmanager
    def transform(self, translate: Vec3 = (0, 0, 0), rotate_y: float = 0.0, scale=1.0, rotate_x: float = 0.0, rotate_z: float = 0.0):
        """Nested local transform; rotations in degrees, applied scale -> rotX -> rotZ -> rotY -> translate."""
        s = np.diag([*(scale if isinstance(scale, (tuple, list)) else (scale, scale, scale)), 1.0])
        rx, ry, rz = (math.radians(a) for a in (rotate_x, rotate_y, rotate_z))
        Rx = np.array([[1, 0, 0, 0], [0, math.cos(rx), -math.sin(rx), 0], [0, math.sin(rx), math.cos(rx), 0], [0, 0, 0, 1]])
        Ry = np.array([[math.cos(ry), 0, math.sin(ry), 0], [0, 1, 0, 0], [-math.sin(ry), 0, math.cos(ry), 0], [0, 0, 0, 1]])
        Rz = np.array([[math.cos(rz), -math.sin(rz), 0, 0], [math.sin(rz), math.cos(rz), 0, 0], [0, 0, 1, 0], [0, 0, 0, 1]])
        T = np.eye(4)
        T[:3, 3] = translate
        local = T @ Ry @ Rz @ Rx @ s
        self._stack.append(self._stack[-1] @ local)
        try:
            yield self
        finally:
            self._stack.pop()

    def _xf(self, pts) -> np.ndarray:
        M = self._stack[-1]
        P = np.asarray(pts, dtype=np.float64)
        H = np.c_[P, np.ones(len(P))]
        return (H @ M.T)[:, :3]

    def _mirror_flips(self) -> bool:
        return np.linalg.det(self._stack[-1][:3, :3]) < 0

    # ------------------------------------------------------------------ raw faces
    def _face(self, points, color, material, facet):
        local = np.asarray(points, dtype=np.float64)
        # Map a whole convex face once. Both triangles of a quad then agree along the diagonal.
        normal = np.cross(local[1] - local[0], local[2] - local[0])
        axis = int(np.abs(normal).argmax())
        u_axis, v_axis = ((2, 1), (0, 2), (0, 1))[axis]
        uv = local[:, [u_axis, v_axis]].copy()
        uv[:, 1] *= -1
        uv -= uv.min(0)
        uv /= np.maximum(uv.max(0), 1e-12)
        world = self._xf(local)
        mirror = self._mirror_flips()
        if facet:
            self._face_counter += 1
            color = style.jitter(tuple(color[:3]), f'{self.seed}:{self._face_counter}', 0.035) + tuple(color[3:])
        for i in range(1, len(local) - 1):
            ids = [0, i + 1, i] if mirror else [0, i, i + 1]
            self.tris.append(Tri(world[ids], color, material, uv[ids]))

    def tri(self, a, b, c, color, material: str = 'base', facet: bool = True):
        self._face((a, b, c), color, material, facet)

    def quad(self, a, b, c, d, color, material: str = 'base'):
        """Quad a-b-c-d counter-clockwise from outside."""
        self._face((a, b, c, d), color, material, True)

    def poly(self, pts: Sequence[Vec3], color, material: str = 'base'):
        """Convex polygon (CCW from outside), fan-triangulated."""
        self._face(pts, color, material, True)

    # ------------------------------------------------------------------ primitives
    def box(self, center: Vec3, size: Vec3, color, top=None, bottom: bool = False, sides=None, material: str = 'base', taper: float = 0.0):
        """Axis-aligned box. taper > 0 shrinks the top face (0..0.9) for a sturdy, hand-built look."""
        cx, cy, cz = center
        hx, hy, hz = size[0] / 2, size[1] / 2, size[2] / 2
        tx, tz = hx * (1 - taper), hz * (1 - taper)
        y0, y1 = cy - hy, cy + hy
        b = [(cx - hx, y0, cz - hz), (cx + hx, y0, cz - hz), (cx + hx, y0, cz + hz), (cx - hx, y0, cz + hz)]
        t = [(cx - tx, y1, cz - tz), (cx + tx, y1, cz - tz), (cx + tx, y1, cz + tz), (cx - tx, y1, cz + tz)]
        sc = sides or color
        self.quad(b[1], b[0], t[0], t[1], sc, material)   # front -Z
        self.quad(b[2], b[1], t[1], t[2], style.shade(sc, 0.92), material)
        self.quad(b[3], b[2], t[2], t[3], style.shade(sc, 0.85), material)
        self.quad(b[0], b[3], t[3], t[0], style.shade(sc, 0.95), material)
        self.quad(t[3], t[2], t[1], t[0], top or style.shade(color, 1.05), material)
        if bottom:
            self.quad(b[0], b[1], b[2], b[3], style.shade(color, 0.6), material)

    def cylinder(self, base: Vec3, radius: float, height: float, color, segments: int = 8, top_radius: Optional[float] = None,
                 cap_top: bool = True, cap_bottom: bool = False, material: str = 'base', top=None):
        """Cylinder / frustum / cone (top_radius = 0) standing on `base`."""
        tr = radius if top_radius is None else top_radius
        bx, by, bz = base
        ring_b, ring_t = [], []
        for i in range(segments):
            a = 2 * math.pi * i / segments
            ring_b.append((bx + math.cos(a) * radius, by, bz + math.sin(a) * radius))
            ring_t.append((bx + math.cos(a) * tr, by + height, bz + math.sin(a) * tr))
        for i in range(segments):
            j = (i + 1) % segments
            f = 0.85 + 0.15 * math.cos(2 * math.pi * (i + 0.5) / segments - 2.3)
            if tr > 1e-6:
                self.quad(ring_b[j], ring_b[i], ring_t[i], ring_t[j], style.shade(color, f), material)
            else:
                self.tri(ring_b[j], ring_b[i], (bx, by + height, bz), style.shade(color, f), material)
        if cap_top and tr > 1e-6:
            self.poly(list(reversed(ring_t)), top or style.shade(color, 1.05), material)
        if cap_bottom:
            self.poly(ring_b, style.shade(color, 0.6), material)

    def cone(self, base: Vec3, radius: float, height: float, color, segments: int = 8, material: str = 'base'):
        self.cylinder(base, radius, height, color, segments, top_radius=0.0, cap_top=False, material=material)

    def sphere(self, center: Vec3, radius: float, color, rings: int = 4, segments: int = 8, squash: float = 1.0, material: str = 'base', hemisphere: bool = False):
        cx, cy, cz = center
        if hemisphere:
            self.dome(center, radius, color, max(1, rings // 2), segments, squash, material)
            return
        def pt(ri, si):
            phi = math.pi * ri / rings
            th = 2 * math.pi * si / segments
            return (cx + radius * math.sin(phi) * math.cos(th), cy + radius * squash * math.cos(phi), cz + radius * math.sin(phi) * math.sin(th))
        for ri in range(rings):
            for si in range(segments):
                a, b, c, d = pt(ri, si), pt(ri, si + 1), pt(ri + 1, si + 1), pt(ri + 1, si)
                f = 0.8 + 0.2 * (1 - ri / rings)
                if ri == 0:
                    self.tri(a, c, d, style.shade(color, f), material)
                elif ri == rings - 1:
                    self.tri(a, b, d, style.shade(color, f), material)
                else:
                    self.quad(a, b, c, d, style.shade(color, f), material)

    def dome(self, base: Vec3, radius: float, color, rings: int = 3, segments: int = 10, squash: float = 1.0, material: str = 'base'):
        """Half-sphere sitting on `base` (open bottom)."""
        cx, cy, cz = base
        def pt(ri, si):
            phi = (math.pi / 2) * ri / rings
            th = 2 * math.pi * si / segments
            return (cx + radius * math.cos(phi) * math.cos(th), cy + radius * squash * math.sin(phi), cz + radius * math.cos(phi) * math.sin(th))
        top = (cx, cy + radius * squash, cz)
        for ri in range(rings):
            for si in range(segments):
                a, b = pt(ri, si), pt(ri, si + 1)
                f = 0.82 + 0.18 * ri / rings
                if ri == rings - 1:
                    self.tri(b, a, top, style.shade(color, f), material)
                else:
                    c, d = pt(ri + 1, si + 1), pt(ri + 1, si)
                    self.quad(b, a, d, c, style.shade(color, f), material)

    def prism_x(self, base: Vec3, length: float, width: float, height: float, color, gable_color=None, material: str = 'base', overhang: float = 0.0):
        """Triangular prism with the ridge along X (a gable roof body). base = centre of the roof's bottom."""
        bx, by, bz = base
        hl, hw = length / 2 + overhang, width / 2 + overhang
        A, B = (bx - hl, by, bz - hw), (bx + hl, by, bz - hw)
        C, D = (bx + hl, by, bz + hw), (bx - hl, by, bz + hw)
        R0, R1 = (bx - hl, by + height, bz), (bx + hl, by + height, bz)
        self.quad(B, A, R0, R1, color, material)
        self.quad(D, C, R1, R0, style.shade(color, 0.85), material)
        gc = gable_color or style.shade(color, 0.9)
        self.tri(C, B, R1, gc, material)
        self.tri(A, D, R0, style.shade(gc, 0.95), material)
        self.quad(A, B, C, D, style.shade(color, 0.5), material)

    def gable_roof(self, base: Vec3, length: float, width: float, height: float, color, overhang: float = 0.12, gable_color=None, material: str = 'base'):
        self.prism_x(base, length, width, height, color, gable_color, material, overhang)

    def hip_roof(self, base: Vec3, length: float, width: float, height: float, color, overhang: float = 0.12, ridge: float = 0.4, material: str = 'base'):
        bx, by, bz = base
        hl, hw = length / 2 + overhang, width / 2 + overhang
        rl = max(0.0, hl - hw) * ridge + 0.001
        A, B, C, D = (bx - hl, by, bz - hw), (bx + hl, by, bz - hw), (bx + hl, by, bz + hw), (bx - hl, by, bz + hw)
        R0, R1 = (bx - rl, by + height, bz), (bx + rl, by + height, bz)
        self.quad(B, A, R0, R1, color, material)
        self.quad(D, C, R1, R0, style.shade(color, 0.85), material)
        self.tri(C, B, R1, style.shade(color, 0.92), material)
        self.tri(A, D, R0, style.shade(color, 0.95), material)
        self.quad(A, B, C, D, style.shade(color, 0.5), material)

    def pyramid(self, base: Vec3, length: float, width: float, height: float, color, material: str = 'base', apex_offset: Vec3 = (0, 0, 0)):
        bx, by, bz = base
        hl, hw = length / 2, width / 2
        A, B, C, D = (bx - hl, by, bz - hw), (bx + hl, by, bz - hw), (bx + hl, by, bz + hw), (bx - hl, by, bz + hw)
        P = (bx + apex_offset[0], by + height, bz + apex_offset[2])
        self.tri(B, A, P, color, material)
        self.tri(C, B, P, style.shade(color, 0.9), material)
        self.tri(D, C, P, style.shade(color, 0.82), material)
        self.tri(A, D, P, style.shade(color, 0.95), material)

    def stepped_pyramid(self, base: Vec3, size: float, steps: int, step_h: float, color, shrink: float = 0.78, material: str = 'base'):
        s = size
        y = base[1]
        for i in range(steps):
            self.box((base[0], y + step_h / 2, base[2]), (s, step_h, s), style.shade(color, 1 - 0.04 * i), material=material)
            y += step_h
            s *= shrink
        return y, s

    def wedge(self, base: Vec3, length: float, width: float, height: float, color, material: str = 'base'):
        """Lean-to / shed roof: slopes down toward -Z."""
        bx, by, bz = base
        hl, hw = length / 2, width / 2
        A, B, C, D = (bx - hl, by, bz - hw), (bx + hl, by, bz - hw), (bx + hl, by, bz + hw), (bx - hl, by, bz + hw)
        E, F = (bx + hl, by + height, bz + hw), (bx - hl, by + height, bz + hw)
        self.quad(B, A, F, E, color, material)
        self.quad(D, C, E, F, style.shade(color, 0.8), material)
        self.tri(C, B, E, style.shade(color, 0.9), material)
        self.tri(A, D, F, style.shade(color, 0.9), material)
        self.quad(A, B, C, D, style.shade(color, 0.55), material)

    # ------------------------------------------------------------------ details (the "fun" layer)
    def door(self, x: float, z_front: float, w: float, h: float, color=None, arch: bool = False):
        """Dark inset door on the front face (-Z side), slightly proud of the wall."""
        c = color or style.COMMON['door']
        z = z_front - 0.012
        self.quad((x + w / 2, 0.0, z), (x - w / 2, 0.0, z), (x - w / 2, h, z), (x + w / 2, h, z), c, 'door')
        if arch:
            self.tri((x + w / 2, h, z), (x - w / 2, h, z), (x, h + w * 0.45, z), c, 'door')

    def window(self, x: float, y: float, z_front: float, w: float, h: float, lit: bool = True, frame=None):
        c = style.COMMON['window_lit'] if lit else style.COMMON['window_dark']
        z = z_front - 0.012
        if frame is not None:
            self.quad((x + w / 2 + 0.03, y - 0.03, z + 0.004), (x - w / 2 - 0.03, y - 0.03, z + 0.004), (x - w / 2 - 0.03, y + h + 0.03, z + 0.004), (x + w / 2 + 0.03, y + h + 0.03, z + 0.004), frame)
        self.quad((x + w / 2, y, z), (x - w / 2, y, z), (x - w / 2, y + h, z), (x + w / 2, y + h, z), c, material='emissive' if lit else 'glass')

    def chimney(self, pos: Vec3, h: float, color, lean: float = 4.0):
        with self.transform(translate=pos, rotate_z=lean):
            self.box((0, h / 2, 0), (0.18, h, 0.18), color, taper=0.1)
            self.box((0, h + 0.03, 0), (0.24, 0.06, 0.24), style.shade(color, 0.8))

    def banner(self, pos: Vec3, h: float, color, pole=None):
        pc = pole or style.COMMON['darkwood']
        self.cylinder(pos, 0.03, h, pc, segments=5)
        x, y, z = pos
        self.quad((x, y + h * 0.98, z), (x + 0.3, y + h * 0.95, z), (x + 0.32, y + h * 0.6, z), (x, y + h * 0.55, z), color, 'banner')
        self.quad((x, y + h * 0.55, z + 0.002), (x + 0.32, y + h * 0.6, z + 0.002), (x + 0.3, y + h * 0.95, z + 0.002), (x, y + h * 0.98, z + 0.002), style.shade(color, 0.8), 'banner')

    def flower_box(self, x: float, y: float, z_front: float, w: float, seed: str = ''):
        z = z_front - 0.05
        self.box((x, y, z), (w, 0.06, 0.08), style.COMMON['wood'], material='timber')
        cols = [style.hex_rgb('#e85d75'), style.hex_rgb('#f2c14e'), style.hex_rgb('#f5f5f5'), style.hex_rgb('#7a5cd1')]
        n = max(2, int(w / 0.09))
        for i in range(n):
            c = cols[int(style.stable_rand(f'{self.seed}{seed}{i}') * len(cols))]
            fx = x - w / 2 + (i + 0.5) * w / n
            self.box((fx, y + 0.055, z), (0.04, 0.05, 0.04), style.COMMON['leaf'], material='leaf')
            self.box((fx, y + 0.091, z), (0.045, 0.022, 0.045), c, material='flower' if i % 2 == 0 else 'ivory')

    def fence(self, a: Vec3, b: Vec3, posts: int = 5, h: float = 0.25, color=None):
        c = color or style.COMMON['wood']
        ax, ay, az = a
        bx, by, bz = b
        for i in range(posts):
            t = i / max(1, posts - 1)
            self.box((ax + (bx - ax) * t, ay + h / 2, az + (bz - az) * t), (0.05, h, 0.05), style.shade(c, 0.9))
        L = math.dist((ax, az), (bx, bz))
        ang = -math.degrees(math.atan2(bz - az, bx - ax))
        with self.transform(translate=((ax + bx) / 2, ay, (az + bz) / 2), rotate_y=ang):
            self.box((0, h * 0.7, 0), (L, 0.03, 0.03), c)
            self.box((0, h * 0.35, 0), (L, 0.03, 0.03), c)

    def barrel(self, pos: Vec3, r: float = 0.08, h: float = 0.16):
        self.cylinder(pos, r, h, style.COMMON['wood'], segments=7, top=style.shade(style.COMMON['wood'], 1.2))
        self.cylinder((pos[0], pos[1] + h * 0.25, pos[2]), r * 1.04, h * 0.06, style.hex_rgb('#4a4a4a'), segments=7)
        self.cylinder((pos[0], pos[1] + h * 0.7, pos[2]), r * 1.04, h * 0.06, style.hex_rgb('#4a4a4a'), segments=7)

    def crate(self, pos: Vec3, s: float = 0.14):
        self.box((pos[0], pos[1] + s / 2, pos[2]), (s, s, s), style.shade(style.COMMON['wood'], 1.1))

    def lantern(self, pos: Vec3, h: float = 0.45):
        self.cylinder(pos, 0.02, h, style.COMMON['darkwood'], segments=4)
        self.box((pos[0], pos[1] + h + 0.05, pos[2]), (0.08, 0.1, 0.08), style.COMMON['window_lit'], material='emissive')

    def tree(self, pos: Vec3, h: float = 1.0, kind: str = 'round', leaf=None):
        lc = leaf or style.COMMON['leaf']
        self.cylinder(pos, 0.05 * h, h * 0.45, style.COMMON['wood'], segments=5)
        x, y, z = pos
        if kind == 'conifer':
            for i in range(3):
                self.cone((x, y + h * (0.3 + 0.22 * i), z), h * (0.32 - 0.07 * i), h * 0.38, style.shade(lc, 0.85 + 0.07 * i), segments=7)
        else:
            self.sphere((x, y + h * 0.7, z), h * 0.32, lc, rings=3, segments=7)

    # ------------------------------------------------------------------ post effects
    def wobble(self, amount: float = 0.015, keep_ground: bool = True):
        """Deterministic per-vertex displacement (shared positions move together, so meshes stay closed)."""
        if amount <= 0:
            return
        cache = {}
        for t in self.tris:
            for k in range(3):
                key = tuple(np.round(t.p[k], 4))
                if key not in cache:
                    r = [style.stable_rand(f'{self.seed}|w|{key}|{i}') - 0.5 for i in range(3)]
                    off = np.array(r) * 2 * amount
                    if keep_ground and abs(key[1]) < 1e-4:
                        off[1] = 0.0
                    cache[key] = off
                t.p[k] = t.p[k] + cache[key]

    # ------------------------------------------------------------------ queries
    def stats(self) -> dict:
        if not self.tris:
            return dict(triangles=0, bounds=((0, 0, 0), (0, 0, 0)))
        P = np.concatenate([t.p for t in self.tris])
        return dict(triangles=len(self.tris), bounds=(tuple(P.min(0)), tuple(P.max(0))))

    def drop_degenerate(self, eps: float = 1e-10) -> int:
        keep = []
        for t in self.tris:
            n = np.cross(t.p[1] - t.p[0], t.p[2] - t.p[0])
            if float(np.dot(n, n)) > eps and np.all(np.isfinite(t.p)):
                keep.append(t)
        dropped = len(self.tris) - len(keep)
        self.tris = keep
        return dropped
