"""Match independent art reviews to the specific model and age previews inspected."""
import hashlib
import json
from pathlib import Path

from building_scope import ROOT


REVIEWS = ROOT / "tools/AssetForge/reviews"
FRAGMENTS = ROOT / "Assets/GodSim/Art/Models/_manifests"
CRITERIA = ("silhouette", "identity", "construction", "charm", "colour", "technical")


def director_holds(models):
    path = REVIEWS / "art_direction_holds.json"
    if not path.exists():
        raise ValueError(f"Missing art direction findings ledger: {path}")
    known = {m.id for m in models}
    held = set()
    for finding in json.loads(path.read_text()):
        ids = set(finding["models"])
        if not ids or ids-known or finding["status"] not in ("open","closed"):
            raise ValueError(f"Invalid art direction finding in {path}")
        if finding["status"] == "open":
            held.update(ids)
    return held


def read_fragment(package):
    path = FRAGMENTS / (package + ".json")
    if not path.exists():
        return {}
    rows = json.loads(path.read_text())
    by_id = {entry["id"]: entry for entry in rows}
    if len(by_id) != len(rows):
        raise ValueError(f"duplicate model id in {path}")
    return by_id


def evidence(entry):
    paths = [entry["path"], *entry["age_previews"].values()]
    paths += ["Assets/GodSim/Art/Textures/GodSim_BuildingAtlas.png",
              "Assets/GodSim/Art/Textures/GodSim_BuildingEmissive.png",
              "Assets/GodSim/Art/Resources/GodSim_BuildingAtlas.shader",
              "Assets/GodSim/Art/Resources/GodSim_Buildings.mat"]
    digest = hashlib.sha256()
    for name in sorted(set(paths)):
        digest.update(name.encode("utf-8") + b"\0")
        digest.update(hashlib.sha256((ROOT / name).read_bytes()).digest())
    return digest.hexdigest()


def catalog_evidence(entries):
    digest = hashlib.sha256()
    for entry in sorted(entries, key=lambda e: e["id"]):
        digest.update(f"{entry['id']}\t{evidence(entry)}\n".encode("utf-8"))
    return digest.hexdigest()


def approved_family_sheets(entries):
    """Require current independent catalog sign-off before publishing a complete catalog."""
    path = REVIEWS / "art_direction_approval.json"
    if not path.exists():
        raise ValueError(f"Missing independent catalog approval: {path}")
    approval = json.loads(path.read_text())
    painted = approval.get("family_sheets")
    unpainted = approval.get("era_sheets")
    if (approval.get("schema") != "godsim.art-direction/1" or not approval.get("reviewer")
            or approval.get("model_count") != len(entries)
            or not all(isinstance(group, dict) and group for group in (painted, unpainted))
            or approval.get("evidence_sha256") != catalog_evidence(entries)):
        raise ValueError("Independent art direction approval does not match the current catalog")
    return painted, unpainted


def read_reviews(package):
    path = REVIEWS / (package + ".json")
    if not path.exists():
        return [], {}
    doc = json.loads(path.read_text())
    if doc.get("package") != package:
        raise ValueError(f"wrong package in {path}")
    rounds = doc["rounds"]
    latest = {}
    for number, review in enumerate(rounds, 1):
        if review["round"] != number or not review["critic"].strip():
            raise ValueError(f"invalid round or missing critic in {path}")
        seen = set()
        for item in review["models"]:
            ident = item["id"]
            if ident in seen:
                raise ValueError(f"duplicate {ident} in round {number} of {package}")
            seen.add(ident)
            if not isinstance(item.get("defects"), list):
                raise ValueError(f"missing defect list for {ident}")
            if any(type(item["scores"].get(k)) not in (int, float) or
                   not 0 <= item["scores"][k] <= 100 for k in CRITERIA):
                raise ValueError(f"invalid scores for {ident}")
            latest[ident] = item
    return rounds, latest


def passing(entry, review):
    if review is None or review["defects"] or not review.get("observations", "").strip():
        return False
    if review["scores"]["technical"] < 99 or min(review["scores"][k] for k in CRITERIA[:-1]) < 90:
        return False
    return review.get("evidence_sha256") == evidence(entry)


def check_entry(model, entry):
    expected = model.metadata()
    for key in ("id", "kind", "race", "building_type", "variant", "ages", "footprint", "category"):
        if entry[key] != getattr(expected, key):
            raise ValueError(f"{model.id}: wrong {key} in manifest fragment")
    file = Path("Assets/GodSim/Art/Models") / model.category / (model.id + ".glb")
    if entry["path"] != file.as_posix() or not (ROOT / file).is_file():
        raise ValueError(f"{model.id}: missing or misfiled GLB")
    if set(entry["age_previews"]) != set(model.ages):
        raise ValueError(f"{model.id}: expected previews for {model.ages}")
    for i, age in enumerate(model.ages):
        suffix = "" if i == 0 else "__" + age
        path = Path("tools/AssetForge/previews") / model.category / (model.id + suffix + ".png")
        if entry["age_previews"][age] != path.as_posix() or not (ROOT / path).is_file():
            raise ValueError(f"{model.id}: missing or misfiled {age} preview")
    if entry["preview"] != entry["age_previews"][model.ages[0]]:
        raise ValueError(f"{model.id}: invalid primary preview")
    from forge.registry import validate_glb
    stats = validate_glb(ROOT / file, expected)
    for key in ("triangles", "lod_triangles", "bytes"):
        if entry[key] != stats[key]:
            raise ValueError(f"{model.id}: serialized {key} disagrees with manifest fragment")
    return stats


if __name__ == "__main__":
    import argparse
    import sys
    from building_scope import package_models
    from building_surfaces import current as surface_check, findings, check_orientation, repeated_shapes

    parser = argparse.ArgumentParser(description="Print the serialized validation and image evidence for a critic.")
    parser.add_argument("package")
    args = parser.parse_args()
    entries = read_fragment(args.package)
    models = package_models(args.package)
    errors = []
    for model in models:
        entry = entries[model.id]
        stats = check_entry(model, entry)
        print(model.id, *stats["lod_triangles"], evidence(entry), *entry["age_previews"].values())
        checked = surface_check(entry)
        issues = checked["findings"] if checked else findings((ROOT / entry["path"]).read_bytes())
        for issue in issues:
            errors.append(f"{model.id} {issue['lod']}: {issue['pairs']} exposed coplanar pairs. "
                          f"Inspect faces {issue['faces']} ({' / '.join(issue['materials'])}) at {issue['point']}.")
        orientations = (checked["inverted"] if checked and "inverted" in checked
                        else check_orientation((ROOT / entry["path"]).read_bytes()))
        for shell in orientations:
            errors.append(f"{model.id} {shell['lod']}: closed shell containing face {shell['face']} "
                          f"has inward normals at {shell['center']}. Reverse its {shell['count']} faces.")
    for ident, others in repeated_shapes(models).items():
        errors.append(f"{ident}: LOD0 has the same geometry as {', '.join(others)}.")
    if errors:
        sys.exit("\n".join(errors))
