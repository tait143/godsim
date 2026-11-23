#!/usr/bin/env python3
"""Rebuild package geometry, check reviews and publish the verified building manifest and sheets."""
import argparse
from collections import defaultdict
import hashlib
import json
from pathlib import Path
import runpy
import sys

from PIL import Image, ImageDraw, ImageFont

from building_review import approved_family_sheets, check_entry, director_holds, passing, read_fragment, read_reviews
from building_scope import AGES, RACES, ROOT, specifications
from building_eras import check_families, comparison_sheets
from building_surfaces import current as surface_check, refresh as inspect_surfaces, repeated_shapes


HERE = Path(__file__).resolve().parent
OUTPUT = ROOT / "Assets/GodSim/Art/Models"
RESOURCES = OUTPUT.parent / "Resources"
SHEETS = HERE / "previews/_sheets"
sys.dont_write_bytecode = True


def load_approved(models, partial):
    packages = defaultdict(list)
    for model in models:
        packages[model.package].append(model)
    approved = []
    missing = []
    repeated = repeated_shapes(models)
    held = director_holds(models)
    for package, specs in packages.items():
        entries = read_fragment(package)
        _, reviews = read_reviews(package)
        ids = {m.id for m in specs}
        if entries.keys() - ids or reviews.keys() - ids:
            raise ValueError(f"{package}: unexpected ids in review or manifest fragment")
        if entries.keys() != ids or reviews.keys() != ids or ids & held:
            missing.append(package)
            continue
        candidates = []
        for model in specs:
            entry = entries[model.id]
            check_entry(model, entry)
            checked = surface_check(entry)
            if (model.id in repeated or not checked or checked["findings"] or checked.get("inverted") or
                    not passing(entry, reviews[model.id])):
                break
            candidates.append(dict(entry, sha256=checked["sha256"], sourceIds=list(model.source_ids),
                                   baseAgeIndex=AGES.index(model.ages[0])))
        if len(candidates) == len(specs):
            approved.extend(candidates)
        else:
            missing.append(package)
    if missing and not partial:
        raise ValueError("packages awaiting complete, current approval: " + ", ".join(missing))
    if missing:
        print(f"{len(missing)} packages withheld")
    return approved


def bindings(entries):
    keys = defaultdict(dict)
    for e in entries:
        races = RACES if e["race"] == "neutral" else (e["race"],)
        for race in races:
            for age in e["ages"]:
                group = keys[e["building_type"], race, age]
                if e["variant"] in group:
                    raise ValueError(f"duplicate binding for {e['id']}")
                group[e["variant"]] = e["id"]
    return [dict(buildingType=b, race=r, age=a, modelIds=[v[i] for i in sorted(v)])
            for (b, r, a), v in sorted(keys.items())]


def contact_sheets(entries, axes=("package", "race", "age")):
    groups = defaultdict(list)
    for e in entries:
        for age in e["ages"]:
            card = (e["race"], e["building_type"], age, e["variant"], e["age_previews"][age])
            names = {"package": e["race"] + "_" + e["category"].split("/")[-1].lower(),
                     "race": e["race"], "age": age}
            for axis in axes:
                groups[axis, names[axis]].append(card)
    files = []
    font = ImageFont.load_default(size=13)
    for (axis, name), cards in sorted(groups.items()):
        directory = SHEETS / axis / name
        directory.mkdir(parents=True, exist_ok=True)
        cards.sort(key=lambda c: (c[1], c[0], AGES.index(c[2]), c[3]))
        for start in range(0, len(cards), 30):
            page = cards[start:start + 30]
            cols = 5
            rows = (len(page) + cols - 1) // cols
            sheet = Image.new("RGB", (cols * 190, rows * 162 + 32), (245, 243, 238))
            draw = ImageDraw.Draw(sheet)
            draw.text((10, 7), f"{name.replace('_', ' ').title()}   |   {start+1}-{start+len(page)} of {len(cards)}",
                      fill=(40, 40, 40), font=font)
            for i, (race, building_type, age, variant, path) in enumerate(page):
                x, y = (i % cols) * 190 + 6, (i // cols) * 162 + 32
                with Image.open(ROOT / path) as src:
                    im = src.convert("RGB")
                    sheet.paste(im.resize((122, 122), Image.Resampling.LANCZOS), (x, y))
                    sheet.paste(im.resize((48, 48), Image.Resampling.LANCZOS), (x + 128, y + 72))
                draw.text((x, y + 123), building_type, fill=(34, 31, 26), font=font)
                draw.text((x, y + 139), f"{race.title()} / {age.title()} / {variant}",
                          fill=(68, 62, 52), font=font)
            path = directory / f"{start // 30 + 1:02}.png"
            sheet.save(path)
            files.append(path)
    return files


def family_sheets(entries):
    """Place the same function side by side across ten ages, with one row per race and variant."""
    families = defaultdict(lambda: defaultdict(dict))
    for entry in entries:
        row = families[entry["building_type"]][entry["race"], entry["variant"]]
        row.update(entry["age_previews"])
    names = sorted(families)
    font = ImageFont.load_default(size=13)
    title = ImageFont.load_default(size=18)
    directory = SHEETS / "families"
    directory.mkdir(parents=True, exist_ok=True)
    files = []
    races = (*RACES, "neutral")
    panel_width, row_height = 874, 66
    for start in range(0, len(names), 4):
        page = names[start:start+4]
        heights = [59 + row_height * max(len(families[name]) for name in page[i:i+2])
                   for i in range(0,len(page),2)]
        sheet = Image.new("RGB",(panel_width*2,sum(heights)),(245,243,238))
        draw = ImageDraw.Draw(sheet)
        for i,name in enumerate(page):
            x = (i % 2)*panel_width+8
            y = sum(heights[:i//2])+5
            draw.text((x,y),name,font=title,fill=(34,31,26))
            for column,age in enumerate(AGES):
                draw.text((x+85+column*78,y+25),age.title(),font=font,fill=(68,62,52))
            rows = sorted(families[name],key=lambda pair:(races.index(pair[0]),pair[1]))
            for j,(race,variant) in enumerate(rows):
                top = y+45+j*row_height
                label = race.title() + (f" {variant+1}" if name == "House" else "")
                draw.text((x,top+23),label,font=font,fill=(34,31,26))
                for age,path in families[name][race,variant].items():
                    with Image.open(ROOT / path) as src:
                        thumb = src.convert("RGB").resize((64,64), Image.Resampling.LANCZOS)
                    sheet.paste(thumb,(x+85+AGES.index(age)*78,top))
        path = directory / f"{start//4+1:02}.png"
        sheet.save(path)
        files.append(path)
    return files


def artifact_hashes(entries, sheets, models):
    """Fingerprint each generated artifact; partial runs naturally have smaller inventories."""
    paths = {OUTPUT / "manifest.json", RESOURCES / "GodSim_BuildingIndex.json", *sheets}
    packages = {m.id: m.package for m in models}
    for entry in entries:
        paths.add(RESOURCES / "GodSimBuildingModels" / (entry["id"] + ".buildingart"))
        paths.add(ROOT / entry["path"])
        paths.add(OUTPUT / "_manifests" / (packages[entry["id"]] + ".json"))
        paths.update(ROOT / p for p in entry["age_previews"].values())
    for category in {entry["category"] for entry in entries}:
        paths.update((HERE / "previews" / category).rglob("*.png"))
    textures = ROOT / "Assets/GodSim/Art/Textures"
    paths.update(textures.glob("GodSim_Building*.png"))
    paths.update(textures.glob("GodSim_Building*.png.meta"))
    paths.add(ROOT / "Assets/GodSim/Art/Resources/GodSim_Buildings.mat")
    paths.add(ROOT / "Assets/GodSim/Art/Resources/GodSim_BuildingAtlas.shader")
    paths.add(RESOURCES / "GodSim_Buildings.mat.meta")
    paths.add(RESOURCES / "GodSim_BuildingAtlas.shader.meta")
    return {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in sorted(paths)}


def retire_obsolete_pages(sheets, packages):
    current_pages = set(sheets)
    directories = [SHEETS / "families", *(SHEETS / "race" / name for name in (*RACES, "neutral")),
                   *(SHEETS / "age" / name for name in AGES),
                   *(SHEETS / "package" / name for name in packages)]
    for directory in directories:
        for path in directory.glob("*.png"):
            if path.stem.isdecimal() and path not in current_pages:
                path.unlink()
    for path in (SHEETS / "era_geometry").glob("*.png"):
        race, _, page = path.stem.removeprefix("rc4-").rpartition("-")
        if race in RACES and page.isdecimal() and path not in current_pages:
            path.unlink()


def write_if_changed(path, content):
    data = content.encode()
    if path.is_file() and path.read_bytes() == data:
        return
    path.parent.mkdir(parents=True, exist_ok=True)
    temp = path.with_name(path.name + ".tmp")
    temp.write_bytes(data)
    temp.replace(path)


def publish_runtime(entries, lookup):
    schema = "godsim.buildings/2"
    index_path = RESOURCES / "GodSim_BuildingIndex.json"
    model_dir = RESOURCES / "GodSimBuildingModels"
    previous = json.loads(index_path.read_text())["assets"] if index_path.is_file() else []
    index_assets = []
    for e in entries:
        write_if_changed(model_dir / (e["id"] + ".buildingart"), f"{e['path']}\n{e['sha256']}\n")
        index_assets.append(dict(id=e["id"], baseAgeIndex=e["baseAgeIndex"]))
    manifest = dict(schema=schema, count=len(entries), assets=entries, bindings=lookup)
    index = dict(schema=schema, assets=index_assets, bindings=lookup)
    write_if_changed(OUTPUT / "manifest.json", json.dumps(manifest, indent=1) + "\n")
    write_if_changed(index_path, json.dumps(index, separators=(",", ":")) + "\n")
    retained = {e["id"] for e in entries}
    for old in previous:
        ident = old["id"]
        if ident not in retained and ident.isascii() and ident.replace("_", "").isalnum():
            (model_dir / (ident + ".buildingart")).unlink(missing_ok=True)


def package_outputs(models):
    """A stale model or preview left by another run cannot establish a reproducible build."""
    for m in models:
        yield OUTPUT / m.category / (m.id + ".glb")
        for index, age in enumerate(m.ages):
            stem = m.id if index == 0 else m.id + "__" + age
            yield HERE / "previews" / m.category / (stem + ".png")


def file_stamp(path):
    try:
        info = path.stat()
    except FileNotFoundError:
        return None
    return info.st_ino, info.st_mtime_ns, info.st_ctime_ns


def require_regenerated(before):
    unchanged = []
    for path, previous in before.items():
        current = file_stamp(path)
        if current is None or current == previous:
            unchanged.append(path)
    if unchanged:
        raise ValueError("generator did not recreate the expected outputs: " +
                         ", ".join(str(p) for p in unchanged))


def build(args, models):
    if not args.no_run:
        from forge.atlas import create_textures
        create_textures()
        # One package generator at a time, in this interpreter, to cap peak memory on M1.
        packages = defaultdict(list)
        for model in models:
            packages[model.package].append(model)
        for package, specs in sorted(packages.items()):
            script = HERE / "generators/buildings" / (package + ".py")
            if args.only and args.only not in script.as_posix():
                continue
            if not script.exists():
                if args.partial:
                    continue
                raise ValueError(f"{package}: missing generator {script}")
            before = {p: file_stamp(p) for p in package_outputs(specs)} if args.verify_rebuild else {}
            print(f"Generating {script.stem}", flush=True)
            runpy.run_path(str(script), run_name="__main__")
            require_regenerated(before)
    inspect_surfaces(models)
    entries = sorted(load_approved(models, args.partial), key=lambda e: e["id"])
    check_families(entries)
    approved_sheets = approved_family_sheets(entries) if len(entries) == len(models) else None
    lookup = bindings(entries)
    family = family_sheets(entries)
    eras = comparison_sheets(entries, SHEETS / "era_geometry")
    if approved_sheets is not None:
        for pages, signed in zip((family, eras), approved_sheets):
            actual = {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in pages}
            if actual != signed:
                raise ValueError("Generated family sheets differ from the independently reviewed images")
    sheets = contact_sheets(entries) + family + eras
    retire_obsolete_pages(sheets, {model.package for model in models})
    publish_runtime(entries, lookup)
    print(f"{len(entries)}/{len(models)} approved models, {len(lookup)} runtime lookup keys, {len(sheets)} sheets")
    return entries, sheets


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--no-run", action="store_true", help="assemble existing reviewed assets")
    parser.add_argument("--only", default="", help="regenerate scripts matching a path substring")
    parser.add_argument("--partial", action="store_true", help="publish only fully reviewed packages")
    parser.add_argument("--verify-rebuild", action="store_true", help="generate twice and compare every output")
    parser.add_argument("--hash-dir", type=Path, help="save both complete rebuild inventories in this directory")
    parser.add_argument("--jobs", type=int, choices=(1,), default=1)
    args = parser.parse_args()
    if args.verify_rebuild and (args.partial or args.no_run or args.only):
        parser.error("the reproducibility check requires two complete builds")
    if args.hash_dir and not args.verify_rebuild:
        parser.error("--hash-dir requires --verify-rebuild")
    models = specifications()
    entries, sheets = build(args, models)
    if args.verify_rebuild:
        first = artifact_hashes(entries, sheets, models)
        if args.hash_dir:
            args.hash_dir.mkdir(parents=True, exist_ok=True)
            (args.hash_dir / "pass-1.json").write_text(json.dumps(first, indent=2, sort_keys=True) + "\n")
        entries, sheets = build(args, models)
        second = artifact_hashes(entries, sheets, models)
        if args.hash_dir:
            (args.hash_dir / "pass-2.json").write_text(json.dumps(second, indent=2, sort_keys=True) + "\n")
        differences = [p for p in sorted(first.keys() | second.keys()) if first.get(p) != second.get(p)]
        if differences:
            raise ValueError("non-reproducible outputs: " + ", ".join(differences))
        print(f"Two complete builds match for {len(first)} files. Inventory SHA-256 " +
              hashlib.sha256(json.dumps(first, sort_keys=True).encode()).hexdigest())


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError) as exc:
        sys.exit(str(exc))
