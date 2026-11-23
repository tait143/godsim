"""Reproduce the coordinator's 64-pixel picture metric and check reviewed exceptions."""
from collections import defaultdict
import hashlib
from itertools import combinations
import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageFilter

from building_scope import ROOT


REVIEW = ROOT / "tools/AssetForge/reviews/rc6"


def feature(path):
    with Image.open(path) as src:
        image = src.convert("RGB").resize((64, 64), Image.Resampling.LANCZOS)
    brightness = np.asarray(image).astype(float).mean(2)
    edges = np.asarray(image.convert("L").filter(ImageFilter.FIND_EDGES)).astype(float)
    return tuple((plane - plane.mean()) / (plane.std() + 1e-6) for plane in (brightness, edges))


def likeness(a, b):
    return round((float((a[0]*b[0]).mean()) + float((a[1]*b[1]).mean())) / 2, 3)


def family(entry):
    return entry["race"], entry["building_type"], entry["variant"]


def listed(entries):
    by_id = {entry["id"]: entry for entry in entries}
    result = set()
    for _, a, b in json.loads((REVIEW / "coordinator-pairs.json").read_text()):
        key = family(by_id[a])
        if key != family(by_id[b]):
            raise ValueError(f"Listed comparison crosses families: {a}, {b}")
        result.add(key)
    return result


def picture_pairs(entries, read_feature=None):
    groups = defaultdict(list)
    for entry in entries:
        if entry["kind"] != "wonder":
            groups[family(entry)].append(entry)
    result = []
    for rows in groups.values():
        if len(rows) < 2:
            continue
        features = {e["id"]: (read_feature(e) if read_feature else feature(ROOT/e["preview"])) for e in rows}
        for a,b in combinations(sorted(features), 2):
            result.append((likeness(features[a],features[b]),a,b))
    return sorted(result, reverse=True)


def file_sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def decisions_from_screening(original, reviewed):
    """Every original judgment remains tied to the actual checkpoint files and image row."""
    def keyed(rows):
        result={(row["a"],row["b"]):row for row in rows}
        if len(result)!=len(rows):
            raise ValueError("Duplicate picture pair")
        return result
    inputs, votes = keyed(original["pairs"]), keyed(reviewed["pairs"])
    if inputs.keys()!=votes.keys() or original["sheet_sha256"]!=reviewed["sheet_sha256"]:
        raise ValueError("Incomplete image screening or different review pages")
    if not reviewed.get("critic","").strip():
        raise ValueError("Missing independent picture critic")
    for pair,row in inputs.items():
        vote=votes[pair]
        if (vote.get("decision") not in ("keep","rework") or not vote.get("reason","").strip()
                or any(vote.get(k)!=value for k,value in row.items())):
            raise ValueError(f"Invalid picture judgment or source evidence: {pair}")
    return votes


def check_catalog(entries, corners):
    by_id = {entry["id"]: entry for entry in entries}
    targets = listed(entries)
    pairs = picture_pairs(entries)
    for score,a,b in pairs:
        if family(by_id[a]) in targets and score >= .700:
            raise ValueError(f"Listed family picture likeness {score:.3f}: {a}, {b}")
    for p in corners:
        if family(by_id[p["a"]]) in targets and 5*p["matched"] > 3*p["total"]:
            raise ValueError(f"Listed family exceeds 60% shared corners: {p['a']}, {p['b']}")
    reviews = json.loads((REVIEW / "picture-reviews.json").read_text())
    inputs = json.loads((REVIEW / "screening-input.json").read_text())
    decisions = decisions_from_screening(inputs,reviews)
    pages = {p.name:file_sha(p) for p in (REVIEW/"screening").glob("*.png")}
    if pages != reviews["sheet_sha256"]:
        raise ValueError("The reviewed unpainted pair pages have changed")
    ordered = {(p["a"],p["b"]):p for p in corners}
    for score,a,b in pairs:
        vote = decisions.get((a,b))
        if vote and vote["decision"] == "rework":
            if score >= .700:
                raise ValueError(f"Reopened pair still scores {score:.3f}: {a}, {b}")
            for pair in (ordered[a,b],ordered[b,a]):
                if 5*pair["matched"]>3*pair["total"]:
                    raise ValueError(f"Reopened pair exceeds 60% shared corners: {a}, {b}")
        if score < .750:
            continue
        if not vote or vote["decision"] != "keep" or not vote["reason"].strip():
            raise ValueError(f"Picture likeness {score:.3f} lacks independent acceptance: {a}, {b}")
        if vote["score"] != score or vote["page"] not in pages:
            raise ValueError(f"Wrong picture evidence: {a}, {b}")
        for ident in (a,b):
            entry = by_id[ident]
            if vote["sha256"][ident] != [file_sha(ROOT/entry["path"]),file_sha(ROOT/entry["preview"])]:
                raise ValueError(f"Stale picture review for {ident}")
    return pairs
