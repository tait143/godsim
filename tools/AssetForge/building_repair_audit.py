"""Compare the coordinator's checkpoint with current serialized geometry and review ledgers."""
from collections import Counter, defaultdict
import hashlib
import json
import subprocess

from building_eras import ERAS, groups, ordered_counts
from building_review import read_fragment, read_reviews
from building_scope import ROOT, specifications
from forge import gltf
from forge.registry import _access


CHECKPOINT = "91863de5a788e77dc2baaaed86fbd9a52c58df63"


def previous(relative):
    return subprocess.check_output(["git", "show", f"{CHECKPOINT}:{relative}"], cwd=ROOT)


def lod_positions(payload):
    document, binary = gltf.unpack(payload)
    hashes = []
    for mesh in document["meshes"]:
        primitive, = mesh["primitives"]
        indices = _access(document, binary, primitive["indices"], 5123, "SCALAR").ravel()
        positions = _access(document, binary, primitive["attributes"]["POSITION"], 5126, "VEC3")
        hashes.append(hashlib.sha256(positions[indices].tobytes()).hexdigest())
    return hashes


def ratio_summary(entries):
    def ratio(entry):
        hi, mid, _ = entry["lod_triangles"]
        return mid / hi

    peak = max(entries, key=ratio)
    return dict(above_sixty=sum(5 * e["lod_triangles"][1] > 3 * e["triangles"] for e in entries),
                above_sixty_five=sum(20 * e["lod_triangles"][1] > 13 * e["triangles"] for e in entries),
                peak_id=peak["id"], peak_triangles=peak["lod_triangles"], peak_ratio=ratio(peak))


def audit():
    baseline = json.loads(previous("Assets/GodSim/Art/Models/manifest.json"))["assets"]
    before = {e["id"]: e for e in baseline}
    entries, revisions = [], {}
    packages = sorted({model.package for model in specifications()})
    for package in packages:
        entries.extend(read_fragment(package).values())
        old = json.loads(previous(f"tools/AssetForge/reviews/{package}.json"))["rounds"]
        rounds, _ = read_reviews(package)
        if rounds[:len(old)] != old:
            raise ValueError(f"{package} has modified historical review rounds")
        for r in rounds[len(old):]:
            revisions.setdefault(package, []).append(dict(
                round=r["round"], critic=r["critic"], evaluated=len(r["models"]),
                held=[m["id"] for m in r["models"] if m["defects"]]))
    changed = {}
    changed_high = set()
    for e in sorted(entries, key=lambda v: v["id"]):
        payload = (ROOT / e["path"]).read_bytes()
        if hashlib.sha256(payload).hexdigest() == before[e["id"]]["sha256"]:
            continue
        old = lod_positions(previous(e["path"]))
        new = lod_positions(payload)
        levels = [i for i in range(3) if old[i] != new[i]]
        changed[e["id"]] = levels
        if 0 in levels:
            changed_high.add(e["id"])
    original_groups = groups(entries)
    reviewed_groups = groups(entries, include_supplement=True)
    all_groups = defaultdict(list)
    for entry in entries:
        if entry["id"].split("_")[-2] in ERAS and entry["building_type"] not in ("House", "TownCenter"):
            all_groups[entry["race"], entry["building_type"], entry["variant"]].append(entry)
    families, all_peak = [], None
    for key, rows in sorted(all_groups.items()):
        if len(rows)<2:
            continue
        comparisons = ordered_counts(rows)
        peak = max(comparisons, key=lambda p: p[2]/p[3])
        if all_peak is None or peak[2]/peak[3] > all_peak[2]/all_peak[3]:
            all_peak = peak
        if key not in reviewed_groups:
            continue
        scope = ("coordinator visual example" if key[:2] == ("elf","Herbalist") else
                 "coordinator list" if key in original_groups else "additional critic finding")
        if scope == "additional critic finding":
            comparisons = [pair for pair in comparisons if pair[0] in changed_high or pair[1] in changed_high]
        if not comparisons:
            raise ValueError(f"Missing revised geometry or era comparisons in {key}")
        a,b,shared,total = max(comparisons, key=lambda p: p[2]/p[3])
        families.append(dict(race=key[0], building_type=key[1], scope=scope,
                             ordered_pairs=len(comparisons), shared=shared, corners=total,
                             max_fraction=shared/total, source=a, compared_to=b))
    return dict(checkpoint=CHECKPOINT, models=len(entries), prior_lod1=ratio_summary(baseline),
                current_lod1=ratio_summary(entries), changed_by_race=dict(Counter(
                    e["race"] for e in entries if e["id"] in changed)),
                changed_models=len(changed), changed_lod0=len(changed_high),
                changed_position_levels=changed, era_families=families, repair_reviews=revisions,
                catalog_wide_era_peak=dict(source=all_peak[0], compared_to=all_peak[1],
                                           shared=all_peak[2], corners=all_peak[3]))


if __name__ == "__main__":
    report = ROOT / "tools/AssetForge/reviews/coordinator_round1.json"
    report.write_text(json.dumps(audit(), indent=2) + "\n")
