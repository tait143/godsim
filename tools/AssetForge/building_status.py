"""Refresh the catalog and review tables in the art report from files on disk."""
from collections import defaultdict
import json

from building_review import CRITERIA, director_holds, passing, read_fragment, read_reviews
from building_scope import AGES, RACES, ROOT, specifications, work_list
from building_surfaces import current as surface_check, repeated_shapes


START = "<!-- BEGIN GENERATED BUILDING INVENTORY -->"
END = "<!-- END GENERATED BUILDING INVENTORY -->"


def inventory(progress=None):
    models = specifications()
    rows = work_list()
    groups = defaultdict(list)
    for m in models:
        groups[m.package].append(m)
    generated, approved, reviews = {}, set(), {}
    surface_holds = {}
    orientation_holds = {}
    surface_pending = set()
    repeated = repeated_shapes(models)
    director = director_holds(models)
    missing, awaiting, incomplete = {}, {}, []
    for package, specs in groups.items():
        entries = read_fragment(package)
        rounds, latest = read_reviews(package)
        reviews[package] = rounds
        for model in specs:
            entry = entries.get(model.id)
            if entry and (ROOT / entry["path"]).is_file():
                generated[model.id] = entry
                record = surface_check(entry)
                if record is None:
                    surface_pending.add(model.id)
                elif record["findings"]:
                    surface_holds[model.id] = record["findings"]
                if record and record.get("inverted"):
                    orientation_holds[model.id] = record["inverted"]
                if passing(entry, latest.get(model.id)):
                    approved.add(model.id)
        missing[package] = [m.id for m in specs if m.id not in generated]
        awaiting[package] = [m.id for m in specs if m.id in generated and m.id not in approved]
        if missing[package] or awaiting[package] or any(
                m.id in surface_holds or m.id in surface_pending or m.id in orientation_holds or m.id in repeated or m.id in director for m in specs):
            incomplete.append(package)
    manifest = ROOT / "Assets/GodSim/Art/Models/manifest.json"
    published = set()
    if manifest.exists():
        published = {entry["id"] for entry in json.loads(manifest.read_text())["assets"]}
    if progress is not None:
        progress.extend(["## Package checklist", "",
                         f"{len(generated):,}/{len(models):,} models generated. {len(approved):,} have current individual "
                         f"reviews and {len(published):,} are bound in the runtime manifest. "
                         "The [art report](BUILDING_ART_REPORT.md) lists outstanding IDs and geometry findings.", "",
                         "| Package | Due | Generated | Passing critique | In manifest | Next work |",
                         "|---|---:|---:|---:|---:|---|"])
        for package, specs in sorted(groups.items()):
            ids = {m.id for m in specs}
            present = len(ids & generated.keys())
            reviewed = len(ids & approved)
            live = len(ids & published)
            actions = []
            if missing[package]:
                actions.append("model generation")
            if ids & surface_holds.keys() or ids & orientation_holds.keys() or ids & repeated.keys():
                actions.append("mesh repairs")
            if ids & surface_pending:
                actions.append("surface checks")
            if ids & director:
                actions.append("art director re-review")
            if awaiting[package]:
                actions.append("critique or revisions")
            if not actions:
                actions.append("package passed" if live == len(ids) else "publish reviewed package")
            progress.append(f"| `{package}` | {len(ids)} | {present} | {reviewed} | {live} | {', '.join(actions)} |")
    out = [START, "## Scope and output", "",
           f"{len(rows)} work-list entries cover {len(set(m.building_type for m in models))} building types. "
           f"The plan requires {len(models):,} distinct meshes and {sum(len(m.ages) for m in models):,} model/age previews. "
           "Counts include two house variants per race and age. Each racial and shared wonder has one mesh.", "",
           f"{len(generated):,} models have a GLB and fragment on disk. "
           f"{len(approved):,} have a current passing independent review. "
           f"{len(published):,} are in the runtime manifest. "
           f"{len(groups)-len(incomplete)} of {len(groups)} complete packages clear both review and surface checks.", "",
           "| Race | Source IDs | Required GLBs | Generated GLBs | Passing reviews | In manifest |",
           "|---|---:|---:|---:|---:|---:|"]
    for race in (*RACES, "neutral"):
        by_race = {m.id for m in models if m.race == race}
        out.append(f"| {race.title()} | {sum(r['race']==race for r in rows)} | {len(by_race)} | "
                   f"{len(by_race & generated.keys())} | {len(by_race & approved)} | {len(by_race & published)} |")
    out.extend(["", "| Age | Source IDs covering age | Required previews | Preview files present | Passing model/age reviews |",
                "|---|---:|---:|---:|---:|"])
    for age in AGES:
        needed = [m.id for m in models if age in m.ages]
        exists = sum(i in generated and age in generated[i].get("age_previews", {}) and
                     (ROOT / generated[i]["age_previews"][age]).is_file() for i in needed)
        out.append(f"| {age.title()} | {sum(age in r['ages'] for r in rows)} | {len(needed)} | "
                   f"{exists} | {len(set(needed) & approved)} |")
    out.extend(["", "Published model/age appearances by race, including alternate houses and shared wonders in their own row.", "",
                "| Race | " + " | ".join(age.title() for age in AGES) + " |",
                "|---|" + "---:|" * len(AGES)])
    for race in (*RACES, "neutral"):
        shipped = [m for m in models if m.race == race and m.id in published]
        out.append("| " + race.title() + " | " + " | ".join(str(sum(age in m.ages for m in shipped))
                                                           for age in AGES) + " |")
    out.extend(["", "## Geometry and texture budgets", "",
                "| Category | LOD0 limit per model | Generated | Peak LOD0 | Total LOD0 | Total LOD1 | Total LOD2 |",
                "|---|---:|---:|---:|---:|---:|---:|"])
    for kind, limit in (("house", 700), ("building", 1500), ("townhall", 2500), ("wonder", 5000)):
        entries = [e for e in generated.values() if e["kind"] == kind]
        counts = [e["lod_triangles"] for e in entries]
        sums = [sum(n[level] for n in counts) for level in range(3)]
        out.append(f"| {kind} | {limit:,} | {len(entries)} | {max([n[0] for n in counts],default=0):,} | "
                   f"{sums[0]:,} | {sums[1]:,} | {sums[2]:,} |")
    out.extend(["", f"Total GLB file size is {sum(e['bytes'] for e in generated.values()):,} bytes."])
    out.extend(["", "## Independent model critiques", ""])
    if any(reviews.values()):
        out.extend(["S = silhouette/readability, I = race/age identity, B = believable construction, "
                    "H = charm, C = colour/material, T = technical health.", "",
                    "| Package | Round | Model | S | I | B | H | C | T | Open findings |",
                    "|---|---:|---|---:|---:|---:|---:|---:|---:|---|"])
    else:
        out.append("No independent production-model scores have been recorded.")
    for package, rounds in reviews.items():
        for review in rounds:
            for item in review["models"]:
                scores = " | ".join(str(item["scores"][k]) for k in CRITERIA)
                findings = " / ".join(str(d).replace("|", "/") for d in item["defects"]) or "None"
                out.append(f"| [{package}](../tools/AssetForge/reviews/{package}.json) | {review['round']} | "
                           f"`{item['id']}` | {scores} | {findings} |")
    out.extend(["", "## Escalations", ""])
    escalations = []
    for package, rounds in reviews.items():
        history = defaultdict(list)
        for review in rounds:
            for item in review["models"]:
                history[item["id"]].append((review["round"], item))
        for ident, attempts in history.items():
            consecutive = 0
            for number, attempt in attempts:
                scores = attempt["scores"]
                if attempt["defects"] or scores["technical"] < 99 or min(scores[k] for k in CRITERIA[:-1]) < 90:
                    consecutive += 1
                    if consecutive == 3:
                        reason = " / ".join(attempt["defects"]) or str(scores)
                        escalations.append(f"- `{ident}` ({package}, round {number}) — {reason}")
                else:
                    consecutive = 0
    out.extend(escalations or ["No model failed three successive independent reviews."])
    out.extend(["", "## Surface checks", "",
                f"{len(generated)-len(surface_pending)} current models have been checked across their exported LODs. "
                f"{len(surface_holds)} have exposed, same-facing coplanar surfaces. {len(surface_pending)} still await the check. "
                "The gate withholds any affected package. Each repair location below is a point in tile coordinates on two overlapping triangles. "
                "Full LOD evidence and mesh hashes are in [the geometry survey](../tools/AssetForge/reviews/surface_checks.json).", ""])
    for ident, results in sorted(surface_holds.items()):
        r = results[0]
        out.append(f"- `{ident}` — {r['lod']} faces {r['faces']}, {' / '.join(r['materials'])} at {r['point']}. "
                   f"Separate or trim the contacting surfaces. {sum(l['pairs'] for l in results)} exposed pairs across all LODs.")
    if orientation_holds:
        out.extend(["", "These closed shells have inward normals. Reverse their face winding and inspect the corrected surfaces.", ""])
        for ident, shells in sorted(orientation_holds.items()):
            out.append(f"- `{ident}` — " + ", ".join(f"{s['lod']} face {s['face']} near {s['center']}" for s in shells))
    if repeated:
        out.extend(["", "These IDs share identical LOD0 geometry within one building type. "
                    "Each listed group needs distinct geometry for its intended race, era or variant.", ""])
        for ids in sorted({tuple(sorted([ident, *others])) for ident, others in repeated.items()}):
            out.append("- " + ", ".join(f"`{ident}`" for ident in ids))
    out.extend(["", "## Outstanding package and model IDs", "",
                "The following lists use exact output IDs. A work-list ID can cover more than one mesh for houses and town centers. "
                "The original work-list IDs for each mesh are available from `building_scope.specifications()`.", ""])
    for package in incomplete:
        out.append(f"### {package}")
        out.append("")
        for label, values in (("Missing GLB or fragment", missing[package]), ("Awaiting passing review", awaiting[package]),
                              ("Open art director finding", [m.id for m in groups[package] if m.id in director]),
                              ("Requires surface repair", [m.id for m in groups[package] if m.id in surface_holds]),
                              ("Requires normal repair", [m.id for m in groups[package] if m.id in orientation_holds]),
                              ("Repeated geometry", [m.id for m in groups[package] if m.id in repeated]),
                              ("Awaiting geometry survey", [m.id for m in groups[package] if m.id in surface_pending])):
            if values:
                out.append(label + " — " + ", ".join(f"`{v}`" for v in values) + ".")
                out.append("")
    if not incomplete:
        out.append("All package models have passing reviews.")
    return "\n".join(out + [END])


def main():
    path = ROOT / "docs/BUILDING_ART_REPORT.md"
    progress = []
    rows = inventory(progress)
    content = path.read_text() if path.exists() else "# GodSim building art report\n\n"
    if START in content:
        before, middle = content.split(START, 1)
        _, after = middle.split(END, 1)
        content = before + rows + after
    else:
        content = content.rstrip() + "\n\n" + rows + "\n"
    path.write_text(content)
    progress_path = ROOT / "docs/BUILDING_ART_PROGRESS.md"
    if progress_path.exists():
        first = "<!-- BEGIN GENERATED PACKAGE CHECKLIST -->"
        last = "<!-- END GENERATED PACKAGE CHECKLIST -->"
        checkpoint = progress_path.read_text()
        table = "\n".join([first, *progress, last])
        if first in checkpoint:
            before, middle = checkpoint.split(first, 1)
            _, after = middle.split(last, 1)
            checkpoint = before + table + after
        else:
            checkpoint = checkpoint.rstrip() + "\n\n" + table + "\n"
        progress_path.write_text(checkpoint)


if __name__ == "__main__":
    main()
