"""Recheck the five coordinator round-three pairs from the serialized catalog."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys

import numpy as np

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE.parents[1]))

from build_all import SHEETS, load_approved
from building_eras import check_families, corner_keys
from building_review import approved_family_sheets
from building_scope import ROOT, specifications
from building_surfaces import geometry_levels


CHECKPOINT = "9b09d383b0e39239795c77638a49e5356438cd86"
TARGETS = (
    ("Orc Bank", "orc_bank", "classical", "industrial"),
    ("Elf Gold Mine", "elf_gold_mine", "ancient", "industrial"),
    ("Elf Fishing Hut", "elf_fishing_hut", "industrial", "future"),
    ("Saurian Bank", "saurian_bank", "industrial", "future"),
    ("Dwarf Well", "dwarf_well", "ancient", "industrial"),
)


def verified_pairs(current, baseline, by_direction):
    """Reconstruct both evidence tables from the ten endpoints and every sibling hash."""
    earlier, final = [], []
    for label, name, first, second in TARGETS:
        a, b = (f"bld_{name}_{era}_v0" for era in (first, second))
        old_hashes, new_hashes, old_corners = {}, {}, {}
        for ident, entry in current.items():
            if ident.rsplit("_", 2)[0] != f"bld_{name}" or entry["variant"] != 0:
                continue
            payload = subprocess.check_output(["git", "show", f"{CHECKPOINT}:{entry['path']}"], cwd=ROOT)
            old_hashes[ident] = hashlib.sha256(payload).hexdigest()
            if old_hashes[ident] != baseline[ident]:
                raise ValueError(f"{ident}: checkpoint GLB disagrees with its published hash")
            new_hashes[ident] = entry["sha256"]
            if ident in (a, b):
                _, positions, _ = next(geometry_levels(payload))
                old_corners[ident] = corner_keys(positions)
        old_pairs, new_pairs = [], []
        for source, other in ((a, b), (b, a)):
            corners = old_corners[source]
            matched = int(np.isin(corners, np.unique(old_corners[other])).sum())
            old_pairs.append(dict(source=source, compared_to=other, matched=matched, total=len(corners)))
            pair = by_direction[source, other]
            new_pairs.append(dict(source=source, compared_to=other, matched=pair["matched"], total=pair["total"]))
        earlier.append(dict(family=label, first=first, second=second, sha256=old_hashes, directions=old_pairs))
        final.append(dict(family=label, first=first, second=second, sha256=new_hashes, directions=new_pairs))
    for name, measured in (("checkpoint", earlier), ("final", final)):
        archive = HERE / f"{name}-pairs.json"
        if json.loads(archive.read_text()) != measured:
            raise ValueError(f"{archive.name} differs from the serialized GLB measurements or hashes")
    return final


def audit():
    manifest_path = "Assets/GodSim/Art/Models/manifest.json"
    before = json.loads(subprocess.check_output(
        ["git", "show", f"{CHECKPOINT}:{manifest_path}"], cwd=ROOT))
    baseline = {entry["id"]: entry["sha256"] for entry in before["assets"]}
    approved = load_approved(specifications(), partial=False)
    signed = approved_family_sheets(approved)
    for directory, expected in zip(("families", "era_geometry"), signed):
        pages = {path.name: hashlib.sha256(path.read_bytes()).hexdigest()
                 for path in (SHEETS / directory).glob("*.png")}
        if pages != expected:
            raise ValueError(f"The {directory} pages differ from the signed art direction review")
    current = {entry["id"]: entry for entry in approved}
    if baseline.keys() != current.keys():
        raise ValueError("The round-three catalog differs from the checkpoint set of model IDs")
    published = json.loads((ROOT / manifest_path).read_text())
    if published["assets"] != sorted(approved, key=lambda entry: entry["id"]):
        raise ValueError("Published catalog differs from the reviewed exports")
    if published["bindings"] != before["bindings"]:
        raise ValueError("The art-only repair changed runtime lookup bindings")

    comparisons, kept = check_families(approved)
    by_direction = {(pair["a"], pair["b"]): pair for pair in comparisons}
    requested = verified_pairs(current, baseline, by_direction)
    changed = {ident for ident, entry in current.items() if entry["sha256"] != baseline[ident]}
    scoped_changes = set()
    rows = []
    for family in requested:
        directions = []
        for original in family["directions"]:
            source, other = original["source"], original["compared_to"]
            pair = by_direction[source, other]
            if 5 * pair["matched"] > 3 * pair["total"]:
                raise ValueError(f"{source} shares {pair['matched']}/{pair['total']} corners with {other}")
            directions.append(pair)
        endpoints = {pair["a"] for pair in directions}
        revised = endpoints & changed
        if not revised:
            raise ValueError(f"{family['family']}: neither requested model was revised")
        scoped_changes.update(revised)
        rows.append(dict(family=family["family"], changed=sorted(revised), directions=directions))
    if changed != scoped_changes:
        raise ValueError(f"Changed models outside the five requested pairs: {sorted(changed - scoped_changes)}")
    peak = max(comparisons, key=lambda pair: pair["matched"] / pair["total"])
    lod1_over = sum(5 * entry["lod_triangles"][1] > 3 * entry["lod_triangles"][0] for entry in approved)
    return dict(checkpoint=CHECKPOINT, models=len(current), comparisons=len(comparisons), lod1_above_sixty=lod1_over,
                changed=sorted(changed), requested=rows, catalog_peak=peak,
                retained_directions=len(kept),
                retained_pairs=len({frozenset((pair["a"], pair["b"])) for pair in kept}),
                retained_families=len({(pair["race"], pair["family"], pair["variant"]) for pair in kept}),
                retained=kept)


if __name__ == "__main__":
    print(json.dumps(audit(), indent=2))
