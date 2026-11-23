"""Audit each ordered pair of era meshes and render the geometry without paint at phone scale."""
from collections import defaultdict
import hashlib
from io import BytesIO
import json
from types import SimpleNamespace

import numpy as np
from PIL import Image, ImageDraw

from building_scope import AGES, RACES, ROOT
from forge import gltf
from forge.registry import _access
from forge.render import render


# The coordinator's 61 audited families plus the Elf Herbalist pictured in the same review.
SCOPE = {
    "dwarf": "Cathedral Factory Foundry Monastery OilWell Pasture Sawmill Stadium StoneQuarry Weaver WoodcutterCamp",
    "elf": "Factory Herbalist IndustrialFarm OilWell StoneWall Theater",
    "human": "Harbor Pasture Sawmill Theater",
    "orc": "Bathhouse Herbalist Monastery Observatory PowerPlant Printworks RailStation ResearchLab Scriptorium Shrine Temple Theater University Well",
    "saurian": "Armory Blacksmith Factory Farm FishingHut Foundry Herbalist IndustrialFarm Library MilitaryBase Mill Monastery OilWell Pasture Printworks ResearchLab Sawmill Scriptorium Smelter Stadium StoneWall Tavern Theater University Weaver WoodcutterCamp Workshop",
}
ERAS = ("ancient", "classical", "industrial", "future")
RC4 = ROOT / "tools/AssetForge/reviews/rc4"

# Additional families in which fresh critics held changed models for era identity.
# Some earlier siblings in these families were outside the repair request.
SUPPLEMENT = {
    "dwarf": "CoalMine Herbalist Library MilitaryBase Scriptorium Shrine Tavern Well",
    "elf": "Aqueduct Bank Barracks Bathhouse FishingHut Forum Foundry GoldMine Guildhall Harbor Hospital HuntingLodge Library Market MilitaryBase Mine Monastery Observatory PowerPlant Printworks RailStation Scriptorium Shrine Storehouse Tavern Temple Watchtower Weaver Well Workshop",
    "human": "Foundry GoldMine IndustrialFarm Mine Stadium Weaver Workshop",
    "orc": "Aqueduct Armory Cathedral Forum Harbor OilWell Stadium Weaver Watchtower",
    "saurian": "Bathhouse Hospital StoneQuarry",
}


def serialized_level(entry, level=0):
    return decode_level((ROOT / entry["path"]).read_bytes(), level)


def decode_level(payload, level):
    doc, binary = gltf.unpack(payload)
    primitive = doc["meshes"][level]["primitives"][0]
    faces = _access(doc, binary, primitive["indices"], 5123, "SCALAR").reshape(-1,3)
    attrs = primitive["attributes"]
    points = _access(doc, binary, attrs["POSITION"], 5126, "VEC3")[faces]
    uv = _access(doc, binary, attrs["TEXCOORD_0"], 5126, "VEC2")[faces]
    return points, uv


def corner_keys(points):
    # Same float64, 3-decimal, triangle-corner metric as audit_eras.py.
    rounded = np.round(points.reshape(-1,3).astype("f8"), 3)
    integers = np.rint(rounded*1000).astype("<i4")
    return np.ascontiguousarray(integers).view("V12").ravel()


def ordered_counts(rows):
    keys = {entry["id"]: corner_keys(serialized_level(entry)[0]) for entry in rows}
    unique = {name: np.unique(k) for name,k in keys.items()}
    return [(a,b,int(np.isin(k,unique[b]).sum()),len(k))
            for a,k in keys.items() for b in keys if a!=b]


def catalog_families(entries):
    families = defaultdict(list)
    for entry in entries:
        if entry["kind"] != "wonder":
            families[entry["race"], entry["building_type"], entry["variant"]].append(entry)
    return {key: sorted(rows, key=lambda e: (min(AGES.index(age) for age in e["ages"]), e["id"]))
            for key, rows in sorted(families.items()) if len(rows) > 1}


def catalog_pairs(entries):
    """Include every pair within a variant, even two Town Centers in the same era."""
    for (race, kind, variant), rows in catalog_families(entries).items():
        for a, b, matched, total in ordered_counts(rows):
            yield dict(race=race, family=kind, variant=variant, a=a, b=b, matched=matched, total=total)


def reviewed_exceptions(pairs, hashes, triage):
    """Allow a moderate overlap only for the precise two exports an independent critic kept."""
    decisions = {tuple(sorted((item["a"], item["b"]))): item for item in triage["pairs"]}
    if len(decisions) != len(triage["pairs"]):
        raise ValueError("Duplicate grayscale pair decision")
    kept = []
    for pair in pairs:
        a, b, matched, total = (pair[k] for k in ("a", "b", "matched", "total"))
        if 5 * matched <= 3 * total:
            continue
        item = decisions.get(tuple(sorted((a, b))))
        valid = (item and item["decision"] == "keep" and item["reason"].strip()
                 and item["glb_sha256"] == {a: hashes[a], b: hashes[b]}
                 and 5 * matched <= 4 * total)
        if valid:
            direction = "a_to_b" if a == item["a"] else "b_to_a"
            reviewed = item["shares"][direction]
            valid = (reviewed["matched"], reviewed["total"]) == (matched, total)
        if not valid:
            raise ValueError(f"{a} shares {matched}/{total} LOD0 corners with {b} without current grayscale approval")
        kept.append(dict(pair, reason=item["reason"]))
    return kept


def load_triage(directory):
    triage = json.loads((directory / "triage.json").read_text())
    sheets = directory / "triage"
    actual = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in sheets.glob("*.png")}
    if not triage.get("reviewer") or not actual or triage.get("sheet_sha256") != actual:
        raise ValueError("Independent grayscale review does not match its checkpoint sheets")
    for item in triage["pairs"]:
        path = ROOT / item["evidence"]["path"]
        if path.parent != sheets or path.name not in actual or item["decision"] not in ("keep","rework"):
            raise ValueError(f"Invalid grayscale evidence for {item['a']} and {item['b']}")
    return triage


def groups(entries, include_supplement=False):
    grouped = defaultdict(list)
    for entry in entries:
        race,kind = entry["race"],entry["building_type"]
        core = kind in SCOPE.get(race,"").split()
        if core or (include_supplement and kind in SUPPLEMENT.get(race,"").split()):
            grouped[race,kind,entry["variant"]].append(entry)
    for rows in grouped.values():
        rows.sort(key=lambda e: ERAS.index(e["id"].split("_")[-2]))
    return grouped


def check_families(entries):
    pairs = list(catalog_pairs(entries))
    mandatory = {(row["race"], row["family"], row["variant"])
                 for row in json.loads((RC4 / "checkpoint_pairs.json").read_text())
                 if row["severity"] == "required"}
    for pair in pairs:
        race, kind = pair["race"], pair["family"]
        if kind in SCOPE.get(race, "").split() or (race, kind, pair["variant"]) in mandatory:
            if 5 * pair["matched"] > 3 * pair["total"]:
                raise ValueError(f"{pair['a']} exceeds 60% shared LOD0 corners with {pair['b']}")
    hashes = {entry["id"]: hashlib.sha256((ROOT / entry["path"]).read_bytes()).hexdigest()
              for entry in entries}
    triage = load_triage(RC4)
    return pairs, reviewed_exceptions(pairs, hashes, triage)


def level_image(entry, level=0, size=64, *, unpainted=False, yaw=215):
    points, uv = serialized_level(entry, level)
    return render_level(points, uv, size, unpainted=unpainted, yaw=yaw)


def render_level(points, uv, size, *, unpainted, yaw):
    buffer = BytesIO()
    render([SimpleNamespace(p=p) for p in points], buffer, size=size, uv=uv,
           unpainted=unpainted, yaw=yaw)
    buffer.seek(0)
    return Image.open(buffer).convert("L" if unpainted else "RGB")


def comparison_sheets(entries, directory):
    families = groups(entries, include_supplement=True)
    directory.mkdir(parents=True,exist_ok=True)
    paths = []
    for race in SCOPE:
        keys = sorted(k for k in families if k[0]==race)
        for offset in range(0,len(keys),8):
            page = keys[offset:offset+8]
            sheet=Image.new("L",(690,42+len(page)*76),244)
            pen=ImageDraw.Draw(sheet)
            pen.text((7,4),f"{race.title()}   |   unpainted LOD0, 64 px",fill=22)
            for j,era in enumerate(ERAS):
                pen.text((135+j*137,24),era.title(),fill=40)
            for i,key in enumerate(page):
                y=42+i*76
                pen.text((7,y+26),key[1],fill=22)
                for entry in families[key]:
                    x=129+137*ERAS.index(entry["id"].split("_")[-2])
                    for j,yaw in enumerate((215,145)):
                        sheet.paste(level_image(entry,unpainted=True,yaw=yaw),(x+j*65,y))
            path=directory/f"{race}-{offset//8+1:02}.png"
            sheet.save(path)
            paths.append(path)
    return paths + rc4_sheets(entries, directory) + rc6_sheets(entries, directory)


def age_span(ages):
    """Display chronological contiguous intervals while retaining the authored base age elsewhere."""
    runs = []
    for index in sorted({AGES.index(age) for age in ages}):
        if runs and index==runs[-1][-1]+1:
            runs[-1].append(index)
        else:
            runs.append([index])
    return ", ".join(AGES[run[0]].title() if len(run)==1 else
                     f"{AGES[run[0]].title()} - {AGES[run[-1]].title()}" for run in runs)


def rc4_sheets(entries, directory):
    """Give all ten individual Town Center ages their own labelled pair of cameras."""
    scope = {(row[1], row[2], row[3]) for row in json.loads((RC4 / "coordinator_families.json").read_text())}
    return scoped_sheets(entries,directory,scope,"rc4",2)


def rc6_sheets(entries, directory):
    """Show every affected family, including both house designs, with the two inspection cameras."""
    from building_pictures import REVIEW, family
    ids = {i for _,a,b in json.loads((REVIEW/"coordinator-pairs.json").read_text()) for i in (a,b)}
    screening = json.loads((REVIEW/"screening-input.json").read_text())
    ids.update(row[k] for row in screening["pairs"] for k in ("a","b"))
    scope = {family(e) for e in entries if e["id"] in ids}
    return scoped_sheets(entries,directory,scope,"rc6",4)


def scoped_sheets(entries,directory,scope,prefix,round_number):
    families = catalog_families(entries)
    paths = []
    for race in RACES:
        rows = []
        for key, family in families.items():
            if key[0] == race and key in scope:
                for start in range(0, len(family), 5):
                    label = f"House v{key[2]}" if prefix=="rc6" and key[1]=="House" else key[1]
                    rows.append((label, family[start:start+5]))
        for offset in range(0, len(rows), 7):
            page = rows[offset:offset+7]
            sheet = Image.new("L", (823, 38+len(page)*86), 244)
            pen = ImageDraw.Draw(sheet)
            pen.text((8, 8), f"{race.title()}  |  coordinator round {round_number}  |  unpainted LOD0, two 64 px views", fill=22)
            for i, (kind, family) in enumerate(page):
                y = 36+i*86
                pen.text((8, y+32), kind, fill=22)
                for j, entry in enumerate(family):
                    x = 127+j*138
                    label = age_span(entry["ages"])
                    pen.text((x, y), label, fill=45)
                    for camera, yaw in enumerate((215,145)):
                        sheet.paste(level_image(entry, unpainted=True, yaw=yaw), (x+65*camera, y+15))
            path = directory / f"{prefix}-{race}-{offset//7+1:02}.png"
            sheet.save(path)
            paths.append(path)
    return paths
