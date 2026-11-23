"""Reproduce the RC4 catalog measurements and prepare serialized model images for independent critique."""
import argparse
from collections import Counter, defaultdict
import hashlib
import json
from pathlib import Path
import subprocess

from PIL import Image, ImageDraw

from building_eras import RC4, age_span, catalog_families, check_families, level_image
from building_repair_audit import lod_positions
from building_review import CRITERIA, passing, read_fragment, read_reviews
from building_scope import AGES, ROOT, specifications

CHECKPOINT = "7e905bbc7c393fdfa647a0bf364239328e001203"


def checkpoint_json(path):
    return json.loads(subprocess.check_output(["git", "show", f"{CHECKPOINT}:{path}"], cwd=ROOT))


def entries_and_changes():
    entries = []
    for package in sorted({model.package for model in specifications()}):
        entries.extend(read_fragment(package).values())
    baseline = checkpoint_json("Assets/GodSim/Art/Models/manifest.json")["assets"]
    original = {e["id"]: e["sha256"] for e in baseline}
    require_inventory(entries, original)
    changed = {e["id"] for e in entries if hashlib.sha256((ROOT/e["path"]).read_bytes()).hexdigest() != original[e["id"]]}
    return sorted(entries, key=lambda e:e["id"]), changed


def require_inventory(entries, checkpoint):
    ids = [e["id"] for e in entries]
    actual = set(ids)
    expected = set(checkpoint)
    if len(ids) != len(actual) or actual != expected:
        raise ValueError(f"Catalog differs from checkpoint ID set: missing={sorted(expected-actual)}, "
                         f"unexpected={sorted(actual-expected)}, duplicate entries={len(ids)-len(actual)}")


def check_repair_reviews(changed, entries, new_rounds):
    latest = {item["id"]:item for review in new_rounds for item in review["models"]}
    for ident in sorted(changed):
        if not passing(entries[ident], latest.get(ident)):
            raise ValueError(f"{ident}: missing current passing independent review since {CHECKPOINT[:7]}")
    return [latest[ident]["scores"] for ident in sorted(changed)]


def review_pages(directory, race):
    entries, changed = entries_and_changes()
    directory.mkdir(parents=True, exist_ok=True)
    families = catalog_families(entries)
    for family, rows in families.items():
        if family[0] != race or not any(r["id"] in changed for r in rows):
            continue
        images = []
        for row in rows:
            images.append([level_image(row, unpainted=True, yaw=a) for a in (215,145)])
        sheet = Image.new("RGB", (max(500,142*len(rows)),123), "#eeeae4")
        pen = ImageDraw.Draw(sheet)
        pen.text((5,3),f"{race.title()} {family[1]}  |  unpainted LOD0 at 64 px",fill="black")
        for i,(row,angles) in enumerate(zip(rows,images)):
            x = 142*i+4
            pen.text((x,25), age_span(row["ages"]), fill="black")
            for j,img in enumerate(angles):
                sheet.paste(img,(x+65*j,45))
        sheet.save(directory/f"{race}-{family[1]}.png")

    targets = [e for e in entries if e["race"]==race and e["id"] in changed]
    for page in range(0,len(targets),4):
        batch = targets[page:page+4]
        width=1152
        sheet = Image.new("RGB",(width,194*len(batch)),"#eeeae4")
        pen=ImageDraw.Draw(sheet)
        for i,row in enumerate(batch):
            top=194*i
            pen.text((4,top+1),row["id"]+"    "+" / ".join(str(v) for v in row["lod_triangles"]),fill="black")
            for j,age in enumerate(sorted(row["age_previews"],key=AGES.index)):
                path = row["age_previews"][age]
                with Image.open(ROOT/path) as src:
                    img=src.convert("RGB").resize((118,118),Image.Resampling.LANCZOS)
                x=4+j*120
                sheet.paste(img,(x,top+31))
                pen.text((x,top+16),age,fill="black")
            for j,(lod,yaw,size) in enumerate(((0,215,120),(1,215,154),(1,125,154),(1,35,154),(2,215,120))):
                x=(379,505,665,825,988)[j]
                img=level_image(row,level=lod,size=size,yaw=yaw)
                pen.text((x,top+16),f"LOD{lod}  {yaw} deg",fill="black")
                sheet.paste(img,(x,top+31))
        sheet.save(directory/f"{race}-changed-{page//4+1:02}.png")
    print(f"{len(targets)} changed {race} models in {directory}")


def audit():
    from build_all import load_approved
    entries, changed = entries_and_changes()
    load_approved(specifications(), partial=False)
    pairs, kept = check_families(entries)
    by_key=defaultdict(list)
    for pair in pairs:
        by_key[pair["race"],pair["family"],pair["variant"]].append(pair)
    original = json.loads((RC4/"checkpoint_pairs.json").read_text())
    triage = json.loads((RC4/"triage.json").read_text())["pairs"]
    current_by_id = {e["id"]:e for e in entries}
    levels_changed={}
    for ident in sorted(changed):
        path=current_by_id[ident]["path"]
        before=subprocess.check_output(["git","show",f"{CHECKPOINT}:{path}"],cwd=ROOT)
        previous=lod_positions(before)
        current=lod_positions((ROOT/path).read_bytes())
        levels_changed[ident]=[i for i in range(3) if previous[i]!=current[i]]
        if 0 not in levels_changed[ident]:
            raise ValueError(f"{ident}: changed export retains its checkpoint LOD0 positions")
    families = []
    for previous in original:
        key=previous["race"],previous["family"],previous["variant"]
        rows=by_key[key]
        ids={p["a"] for p in rows}
        peak=max(rows,key=lambda p:p["matched"]/p["total"])
        families.append(dict(race=key[0],family=key[1],variant=key[2],severity=previous["severity"],
                             changed=sorted(changed & ids),peak=peak))
    old_above={(p["a"],p["b"]) for family in original if family["severity"]=="triage" for p in family["pairs"]}
    votes=set()
    for item in triage:
        for name,a,b in (("a_to_b",item["a"],item["b"]),("b_to_a",item["b"],item["a"])):
            s=item["shares"][name]
            if 5*s["matched"]>3*s["total"]:
                votes.add((a,b))
    if votes != old_above:
        raise ValueError("Triage coverage differs from the moderate checkpoint comparisons")
    inspected={i for family in families for i in family["changed"]}
    if inspected!=changed:
        raise ValueError(f"Unexpected model changes outside the coordinator list: {sorted(changed-inspected)}")
    new_reviews={}
    new_rounds=[]
    for pkg in sorted({m.package for m in specifications()}):
        old=checkpoint_json(f"tools/AssetForge/reviews/{pkg}.json")["rounds"]
        rounds,_=read_reviews(pkg)
        if rounds[:len(old)]!=old:
            raise ValueError(f"Historical reviews modified: {pkg}")
        new_rounds.extend(rounds[len(old):])
        for r in rounds[len(old):]:
            new_reviews.setdefault(pkg,[]).append(dict(round=r["round"],critic=r["critic"],
                    evaluated=[m["id"] for m in r["models"]],held=[m["id"] for m in r["models"] if m["defects"]]))
    current_scores=check_repair_reviews(changed, current_by_id, new_rounds)
    above=lambda row:5*row["lod_triangles"][1]>3*row["lod_triangles"][0]
    peak=max(pairs,key=lambda p:p["matched"]/p["total"])
    tied=[p for p in pairs if p["matched"]*peak["total"]==peak["matched"]*p["total"]]
    return dict(checkpoint=CHECKPOINT,models=len(entries),families_compared=len(by_key),
                directed_comparisons=len(pairs),catalog_peak=peak,catalog_peak_pairs=tied,
                lod1_above_sixty=sum(map(above,entries)),changed_by_race=dict(sorted(Counter(current_by_id[i]["race"] for i in changed).items())),
                changed_models=sorted(changed),changed_position_levels=levels_changed,families=families,
                triage_counts=dict(Counter(x["decision"] for x in triage)),
                kept_directions=kept,package_reviews=new_reviews,
                changed_score_minima={k:min(s[k] for s in current_scores) for k in CRITERIA},
                changed_pair_peak=max((p for p in pairs if p["a"] in changed or p["b"] in changed),
                                      key=lambda p:p["matched"]/p["total"]))


def fraction(row):
    return f"{100*row['matched']/row['total']:.3f}% ({row['matched']:,}/{row['total']:,})"


def update_report(result):
    """Retain every surviving exception in the report, including its converse measurement."""
    families=result["families"]
    triage=json.loads((RC4/"triage.json").read_text())["pairs"]
    surviving={tuple(sorted((p["a"],p["b"]))) for p in result["kept_directions"]}
    peak=result["catalog_peak"]
    peak_labels=" and ".join(f"`{row['a']}` to `{row['b']}`" for row in result["catalog_peak_pairs"])
    required=max((r["peak"] for r in families if r["severity"]=="required"),
                 key=lambda p:p["matched"]/p["total"])
    changed_peak=result["changed_pair_peak"]
    revisions=[r for rounds in result["package_reviews"].values() for r in rounds]
    counts=Counter(i for r in revisions for i in r["evaluated"])
    failed={i for r in revisions for i in r["held"]}
    fourth=sorted(i for i,n in counts.items() if n>3)
    lines=["<!-- RC4 FINAL RESULTS -->","",
           f"The repair changed **{len(result['changed_models'])} LOD0 meshes in {sum(bool(f['changed']) for f in families)} families**. "
           f"All 27 mandatory families pass in every direction. Their highest remaining comparison is {fraction(required)}, "
           f"from `{required['a']}` to `{required['b']}`. The highest comparison involving any revised GLB is "
           f"{fraction(changed_peak)}. The LOD1 ceiling is exceeded by {result['lod1_above_sixty']} of {result['models']:,} models. "
           "[Final all-pairs audit and changed IDs](../tools/AssetForge/reviews/rc4/final_audit.json)","",
           f"The catalog-wide peak is **{fraction(peak)}** for {peak_labels}. "
           f"Exactly {len(result['kept_directions'])} directions in {len(surviving)} distinct pairs from "
           f"{len({(p['race'],p['family'],p['variant']) for p in result['kept_directions']})} families exceed 60%. "
           f"Another {result['triage_counts']['keep']-len(surviving)} checkpoint exceptions dropped below the ceiling "
           "when one of the models was rebuilt for a different comparison. Both GLBs in every surviving pair "
           "match the bytes the independent critic viewed.","",
           "| Race | Listed families | Mandatory families rebuilt | Moderate families rebuilt | Changed GLBs |",
           "|---|---:|---:|---:|---:|"]
    for race in ("human","elf","dwarf","orc","saurian"):
        rows=[f for f in families if f["race"]==race]
        by_severity=Counter(f["severity"] for f in rows if f["changed"])
        lines.append(f"| {race.title()} | {len(rows)} | {by_severity['required']} | {by_severity['triage']} "
                     f"| {result['changed_by_race'][race]} |")
    lines+=["",f"Independent package critics recorded {len(revisions)} new package rounds and "
            f"{sum(counts.values())} model evaluations. {len(failed)} distinct models received a package hold during this repair. "
            f"{len(fourth)} received a fourth or later individual evaluation in round two. The lowest scores among changed "
            "models are "+", ".join(f"{k} {result['changed_score_minima'][k]:g}" for k in CRITERIA)+". "
            "The full score table and historical escalations follow below.","",
            "### Retained grayscale comparisons","",
            "Fractions refer to the indexed corners of the source in that column. The evidence links open the two "
            "checkpoint cameras at 64 pixels. Each row identifies its visual distinction.","",
            "| First model | Second model | First compared to second | Second compared to first | Critic observation |",
            "|---|---|---:|---:|---|"]
    for item in sorted(triage,key=lambda i:(i["race"],i["family"],i["a"],i["b"])):
        if tuple(sorted((item["a"],item["b"]))) not in surviving:
            continue
        links=item["evidence"]
        lines.append(f"| `{item['a']}` | `{item['b']}` | {fraction(item['shares']['a_to_b'])} | "
                     f"{fraction(item['shares']['b_to_a'])} | {item['reason'].replace('|','/')} "
                     f"[Row {links['row']}](../{links['path']}) |")
    lines+=["","<!-- END RC4 FINAL RESULTS -->"]
    p=ROOT/"docs/BUILDING_ART_REPORT.md"
    s=p.read_text()
    start=s.index("<!-- RC4 FINAL RESULTS -->")
    end_marker="<!-- END RC4 FINAL RESULTS -->"
    end=s.index(end_marker,start)+len(end_marker) if end_marker in s[start:] else start+len(lines[0])
    p.write_text(s[:start]+"\n".join(lines)+s[end:])


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    commands=parser.add_subparsers(dest="command",required=True)
    view=commands.add_parser("views")
    view.add_argument("race")
    view.add_argument("directory",type=Path)
    commands.add_parser("audit")
    args=parser.parse_args()
    if args.command=="views":
        review_pages(args.directory,args.race)
    else:
        result=audit()
        (RC4/"final_audit.json").write_text(json.dumps(result,indent=1)+"\n")
        update_report(result)
        print(f"{len(result['changed_models'])} changed, {len(result['kept_directions'])} kept directions")


if __name__=="__main__":
    main()
