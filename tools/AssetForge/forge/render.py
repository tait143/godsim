"""Software preview renderer (numpy z-buffer). Renders the god's-eye 3/4 view used in-game so artists can judge
silhouettes, proportions and colour without Unity. Output: PNG with soft background, ground shadow and a crisp
toon outline (depth/normal edges) matching the in-game look.
"""
from __future__ import annotations

import math

import numpy as np
from PIL import Image

from .gltf import srgb_to_linear  # noqa: F401  (kept for parity)

LIGHT = np.array([-0.55, 0.85, -0.5])
LIGHT = LIGHT / np.linalg.norm(LIGHT)


def _rot(yaw_deg: float, pitch_deg: float) -> np.ndarray:
    y, p = math.radians(yaw_deg), math.radians(pitch_deg)
    Ry = np.array([[math.cos(y), 0, math.sin(y)], [0, 1, 0], [-math.sin(y), 0, math.cos(y)]])
    Rx = np.array([[1, 0, 0], [0, math.cos(p), -math.sin(p)], [0, math.sin(p), math.cos(p)]])
    return Rx @ Ry


def render(tris, path: str, size: int = 512, yaw: float = 215.0, pitch: float = 32.0, margin: float = 0.12, footprint=None, background=(0.93, 0.92, 0.88)):
    if not tris:
        Image.new('RGB', (size, size), tuple(int(c * 255) for c in background)).save(path)
        return
    R = _rot(yaw, pitch)
    P = np.stack([t.p for t in tris])                      # (n,3,3)
    world_n = np.cross(P[:, 1] - P[:, 0], P[:, 2] - P[:, 0])
    wl = np.linalg.norm(world_n, axis=1, keepdims=True)
    wl[wl == 0] = 1
    world_n = world_n / wl
    cols = np.array([t.color[:3] for t in tris], dtype=np.float64)
    emissive = np.array([t.material == 'emissive' for t in tris])
    lam = np.clip(world_n @ LIGHT, 0, 1)
    sky = 0.5 + 0.5 * world_n[:, 1]
    shade = 0.58 + 0.42 * lam + 0.1 * sky
    shade = np.where(emissive, 1.15, shade)
    face_col = np.clip(cols * shade[:, None], 0, 1)

    V = P.reshape(-1, 3) @ R.T                               # view space: x right, y up, z depth (toward viewer +)
    # ground shadow ellipse (projected footprint)
    mins, maxs = V.min(0), V.max(0)
    span = max(maxs[0] - mins[0], maxs[1] - mins[1]) * (1 + 2 * margin)
    cx, cy = (maxs[0] + mins[0]) / 2, (maxs[1] + mins[1]) / 2
    scale = size / span
    X = (V[:, 0] - cx) * scale + size / 2
    Y = size / 2 - (V[:, 1] - cy) * scale
    Z = V[:, 2]
    X, Y, Z = X.reshape(-1, 3), Y.reshape(-1, 3), Z.reshape(-1, 3)

    img = np.empty((size, size, 3))
    gy = np.linspace(0, 1, size)[:, None]
    img[:] = np.array(background) * (1.02 - 0.08 * gy)[:, :, None] if False else np.array(background)
    img = img * (1.03 - 0.1 * gy)[..., None]
    zbuf = np.full((size, size), -np.inf)
    nbuf = np.zeros((size, size), dtype=np.int32) - 1

    # soft contact shadow under the model (ground plane y=0)
    base = P.reshape(-1, 3)
    gmin, gmax = base.min(0), base.max(0)
    sh_c = np.array([(gmin[0] + gmax[0]) / 2, 0.0, (gmin[2] + gmax[2]) / 2]) @ R.T
    sx, sy = (sh_c[0] - cx) * scale + size / 2, size / 2 - (sh_c[1] - cy) * scale
    rx = (gmax[0] - gmin[0] + gmax[2] - gmin[2]) * 0.42 * scale
    ry = rx * math.sin(math.radians(pitch)) * 0.9
    yy, xx = np.mgrid[0:size, 0:size]
    d = ((xx - sx) / max(rx, 1)) ** 2 + ((yy - sy) / max(ry, 1)) ** 2
    img *= (1 - 0.28 * np.clip(1.2 - d, 0, 1) ** 1.5)[..., None]

    for i in range(len(tris)):
        x0, x1, x2 = X[i]
        y0, y1, y2 = Y[i]
        area = (x1 - x0) * (y2 - y0) - (x2 - x0) * (y1 - y0)
        if abs(area) < 1e-9:
            continue
        xmin, xmax = int(max(0, math.floor(min(x0, x1, x2)))), int(min(size - 1, math.ceil(max(x0, x1, x2))))
        ymin, ymax = int(max(0, math.floor(min(y0, y1, y2)))), int(min(size - 1, math.ceil(max(y0, y1, y2))))
        if xmin > xmax or ymin > ymax:
            continue
        gx, gy2 = np.meshgrid(np.arange(xmin, xmax + 1) + 0.5, np.arange(ymin, ymax + 1) + 0.5)
        w0 = ((x1 - gx) * (y2 - gy2) - (x2 - gx) * (y1 - gy2)) / area
        w1 = ((x2 - gx) * (y0 - gy2) - (x0 - gx) * (y2 - gy2)) / area
        w2 = 1 - w0 - w1
        inside = (w0 >= -1e-6) & (w1 >= -1e-6) & (w2 >= -1e-6)
        if not inside.any():
            continue
        z = w0 * Z[i, 0] + w1 * Z[i, 1] + w2 * Z[i, 2]
        sub = zbuf[ymin:ymax + 1, xmin:xmax + 1]
        mask = inside & (z > sub)
        if not mask.any():
            continue
        sub[mask] = z[mask]
        img[ymin:ymax + 1, xmin:xmax + 1][mask] = face_col[i]
        nbuf[ymin:ymax + 1, xmin:xmax + 1][mask] = i

    # toon outline: depth or face-normal discontinuities
    filled = np.isfinite(zbuf)
    edge = np.zeros_like(filled)
    zf = np.where(filled, zbuf, -1e9)
    depth_th = 0.04 * span
    for dy, dx in ((0, 1), (1, 0)):
        a = zf[: size - dy, : size - dx]
        b = zf[dy:, dx:]
        e = np.abs(a - b) > depth_th
        edge[: size - dy, : size - dx] |= e
        edge[dy:, dx:] |= e
        na, nb = nbuf[: size - dy, : size - dx], nbuf[dy:, dx:]
        both = (na >= 0) & (nb >= 0) & (na != nb)
        if both.any():
            dots = np.einsum('ij,ij->i', world_n[na[both]], world_n[nb[both]])
            crease = np.zeros_like(both)
            crease[both] = dots < 0.55
            edge[: size - dy, : size - dx] |= crease
    img[edge] = img[edge] * 0.35 + np.array([0.12, 0.1, 0.09]) * 0.65
    Image.fromarray((np.clip(img, 0, 1) * 255).astype(np.uint8)).save(path)


def contact_sheet(paths, out_path: str, cols: int = 6, cell: int = 256, labels=None):
    from PIL import ImageDraw
    rows = (len(paths) + cols - 1) // cols
    sheet = Image.new('RGB', (cols * cell, rows * (cell + 18)), (245, 243, 238))
    draw = ImageDraw.Draw(sheet)
    for i, p in enumerate(paths):
        try:
            im = Image.open(p).convert('RGB').resize((cell, cell))
        except Exception:
            continue
        x, y = (i % cols) * cell, (i // cols) * (cell + 18)
        sheet.paste(im, (x, y))
        if labels:
            draw.text((x + 4, y + cell + 2), str(labels[i])[:40], fill=(40, 40, 40))
    sheet.save(out_path)
