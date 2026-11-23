"""Inspect overlapping faces on the exported LODs of a building."""
import argparse
from pathlib import Path
import sys

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from building_surfaces import geometry_levels, overlaps, outward_ray_hits_surface
from forge import atlas


def inspect(path, limit, exposed_only=False):
    for mesh_name, points, slots in geometry_levels(path.read_bytes()):
        matches = overlaps(points)
        if not exposed_only:
            print(f'{path.stem} {mesh_name}: {len(matches)} co-oriented overlapping pairs')
        printed = 0
        for area, i, j, point in matches:
            location = ', '.join(f'{coord:.5f}' for coord in point)
            normal = np.cross(points[i, 1] - points[i, 0], points[i, 2] - points[i, 0])
            blocked = outward_ray_hits_surface(point, normal / np.linalg.norm(normal), points)
            if exposed_only and blocked:
                continue
            if exposed_only and printed == 0:
                print(f'{path.stem} {mesh_name}: exposed overlapping surfaces')
            visibility = 'normal ray blocked' if blocked else 'exposed along normal'
            print(f'  faces {i}/{j}  {atlas.SLOTS[slots[i]]} + {atlas.SLOTS[slots[j]]}'
                  f'  area {area:.7g} tile²  at ({location})  {visibility}')
            printed += 1
            if limit and printed >= limit:
                break


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('glb', nargs='+', type=Path)
    parser.add_argument('--limit', type=int, default=8,
                        help='maximum examples per LOD, or 0 for all (default: 8)')
    parser.add_argument('--exposed-only', action='store_true',
                        help='print only intersections visible along the outward normal')
    args = parser.parse_args()
    if args.limit < 0:
        parser.error('--limit must be nonnegative')
    for glb in args.glb:
        inspect(glb, args.limit, args.exposed_only)
