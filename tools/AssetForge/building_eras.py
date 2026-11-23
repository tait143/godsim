"""Audit each ordered pair of era meshes and render the geometry without paint at phone scale."""
from collections import defaultdict
from io import BytesIO
from types import SimpleNamespace

import numpy as np
from PIL import Image, ImageDraw

from building_scope import ROOT
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
    doc, binary = gltf.unpack((ROOT / entry["path"]).read_bytes())
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
    for rows in groups(entries).values():
        for a,b,count,total in ordered_counts(rows):
            if count*5 > total*3:
                raise ValueError(f"{a} shares {count}/{total} LOD0 corners with {b}; maximum 60%")


def level_image(entry, level=0, size=64, *, unpainted=False, yaw=215):
    points, uv = serialized_level(entry, level)
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
    return paths
