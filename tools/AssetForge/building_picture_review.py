"""Prepare native phone-size views and audit the fourth coordinator repair against its checkpoint."""
import argparse
from collections import defaultdict
import gzip
import hashlib
from io import BytesIO
import json
from pathlib import Path
import subprocess

from PIL import Image, ImageDraw

from building_eras import age_span, catalog_families, check_families, decode_level, level_image, render_level
from building_pictures import REVIEW, check_catalog, family, feature, file_sha, listed, picture_pairs
from building_review import CRITERIA, approved_family_sheets, passing, read_fragment, read_reviews
from building_scope import AGES, ROOT, specifications
from building_surfaces import geometry_levels, shape_digest


CHECKPOINT = "1b382ec99f5b245fe337a9a6ced9ade002408fae"
MANIFEST = "Assets/GodSim/Art/Models/manifest.json"
# Four superseded entries cited this intermediate composite. Its surviving family
# views and fresh final approvals are indexed separately in the report.
MISSING_INTERMEDIATE_PAGE = (
    "saurian_infrastructure",8,"tools/AssetForge/reviews/rc6/models/saurian_infrastructure-01.png",
    "1191d2814caece67d1b6587b2c102df12c2ebcd128b2c76840fb6f975123536a",
)


def checkpoint(path):
    return json.loads(subprocess.check_output(["git","show",f"{CHECKPOINT}:{path}"], cwd=ROOT))


def inventory():
    packages = {s.id:s.package for s in specifications()}
    entries = {e["id"]:e for package in sorted(set(packages.values())) for e in read_fragment(package).values()}
    previous = {e["id"]:e for e in checkpoint(MANIFEST)["assets"]}
    if entries.keys() != previous.keys() or entries.keys() != packages.keys():
        raise ValueError("Incomplete model inventory")
    changed = {i for i,e in entries.items() if file_sha(ROOT/e["path"]) != previous[i]["sha256"]}
    return entries, packages, changed


def verify_checkpoint():
    entries=checkpoint(MANIFEST)["assets"]
    # Fetch in one Git invocation; using separate processes for a thousand PNGs
    # obscures the cost of the actual comparison. Git's size prefix delimits PNG bytes.
    names=[e["preview"] for e in entries if e["kind"]!="wonder"]
    query="".join(f"{CHECKPOINT}:{name}\n" for name in names)
    result=BytesIO(subprocess.check_output(["git","cat-file","--batch"],cwd=ROOT,input=query.encode()))
    features, hashes={}, {}
    for name in names:
        header=result.readline().split()
        if len(header)!=3 or header[1]!=b"blob":
            raise ValueError(f"Missing checkpoint preview: {name}")
        raw=result.read(int(header[2]))
        if result.read(1)!=b"\n":
            raise ValueError(f"Invalid Git object boundary for {name}")
        hashes[name]=hashlib.sha256(raw).hexdigest()
        features[name]=feature(BytesIO(raw))
    pairs=picture_pairs(entries,lambda e:features[e["preview"]])
    for limit,path in ((.700,"checkpoint-scores.json"),(.800,"coordinator-pairs.json")):
        if [list(r) for r in pairs if r[0]>=limit]!=json.loads((REVIEW/path).read_text()):
            raise ValueError(f"{path} differs from checkpoint primary preview measurements")
    by_id={e["id"]:e for e in entries}
    scope=listed(entries)
    expected={(a,b):score for score,a,b in pairs if score>=.750 and family(by_id[a]) not in scope}
    rows=json.loads((REVIEW/"screening-input.json").read_text())["pairs"]
    if len(rows)!=len(expected) or {(r["a"],r["b"]):r["score"] for r in rows}!=expected:
        raise ValueError("Supplemental screening omits or misreports a checkpoint pair")
    pages = {}
    images = {}
    for index,row in enumerate(sorted(rows,key=lambda r:(r["a"],r["b"]))):
        page,slot = divmod(index,6)
        name = f"{page+1:02}.png"
        if (row["page"],row["row"])!=(name,slot+1):
            raise ValueError(f"Wrong image location for {row['a']}, {row['b']}")
        if name not in pages:
            with Image.open(REVIEW/"screening"/name) as im:
                pages[name]=im.convert("L")
        for side,ident in enumerate((row["a"],row["b"])):
            e=by_id[ident]
            if row["sha256"][ident]!=[e["sha256"],hashes[e["preview"]]]:
                raise ValueError(f"Wrong source evidence for {ident}")
            if ident not in images:
                payload=subprocess.check_output(["git","show",f"{CHECKPOINT}:{e['path']}"],cwd=ROOT)
                if hashlib.sha256(payload).hexdigest()!=e["sha256"]:
                    raise ValueError(f"Checkpoint manifest GLB hash mismatch for {ident}")
                points,uv=decode_level(payload,0)
                images[ident]=[render_level(points,uv,64,unpainted=True,yaw=yaw) for yaw in (215,145)]
            for camera,tile in enumerate(images[ident]):
                x,y=8+335*side+72*camera,50+97*slot
                if pages[name].crop((x,y,x+64,y+64)).tobytes()!=tile.tobytes():
                    raise ValueError(f"Screened tile differs from checkpoint GLB for {ident} on {name}")
    return pairs


def family_page(key, entries, directory):
    race,building,variant = key
    group = [entries[i:i+5] for i in range(0,len(entries),5)]
    width = 215*max(map(len,group))+12
    sheet = Image.new("RGB",(width,33+len(group)*96),"#f1efea")
    draw = ImageDraw.Draw(sheet)
    draw.text((7,6),f"{race.title()} {building}  v{variant}  |  painted and two unpainted views, 64 px",fill="black")
    for row,subset in enumerate(group):
        for col,entry in enumerate(subset):
            x,y = 6+215*col,31+96*row
            draw.text((x,y),age_span(entry["ages"]),fill="black")
            with Image.open(ROOT/entry["preview"]) as im:
                sheet.paste(im.convert("RGB").resize((64,64),Image.Resampling.LANCZOS),(x,y+17))
            for j,yaw in enumerate((215,145)):
                sheet.paste(level_image(entry,unpainted=True,yaw=yaw),(x+70+66*j,y+17))
    sheet.save(directory/f"{race}-{building}-v{variant}.png")


def model_page(package, number, entries, directory):
    lod_column=max(340,6+110*max(len(e["ages"]) for e in entries))
    sheet = Image.new("RGB",(lod_column+622,164*len(entries)),"#f1efea")
    draw = ImageDraw.Draw(sheet)
    for i,entry in enumerate(entries):
        y=164*i
        draw.text((7,y+3),entry["id"]+"   "+"/".join(map(str,entry["lod_triangles"])),fill="black")
        for j,age in enumerate(sorted(entry["ages"],key=AGES.index)):
            x=6+110*j
            draw.text((x,y+20),age.title(),fill="black")
            with Image.open(ROOT/entry["age_previews"][age]) as im:
                sheet.paste(im.convert("RGB").resize((105,105),Image.Resampling.LANCZOS),(x,y+36))
        for j,(level,yaw) in enumerate(((0,215),(1,215),(1,145),(1,35),(2,215))):
            x=lod_column+123*j
            draw.text((x,y+20),f"LOD{level}  {yaw} deg",fill="black")
            sheet.paste(level_image(entry,level=level,size=116,yaw=yaw),(x,y+36))
    sheet.save(directory/f"{package}-{number:02}.png")


def make_views(selected):
    entries, packages, changed = inventory()
    selected=set(selected) or set(packages.values())
    by_package=defaultdict(list)
    for ident in sorted(changed):
        if packages[ident] in selected:
            by_package[packages[ident]].append(entries[ident])
    models, families = REVIEW/"models", REVIEW/"families"
    models.mkdir(exist_ok=True)
    families.mkdir(exist_ok=True)
    grouped=catalog_families(entries.values())
    for package,rows in sorted(by_package.items()):
        for key in sorted({family(e) for e in rows}):
            family_page(key,grouped[key],families)
        for i in range(0,len(rows),4):
            model_page(package,i//4+1,rows[i:i+4],models)
    print(f"Rendered {sum(map(len,by_package.values()))} models in {len(by_package)} packages")


def inspection_archive():
    """Resolve the exact inspection PNG when a sibling's revision replaced a composite page."""
    archives=defaultdict(list)
    for path in sorted(REVIEW.rglob("*.png")):
        archives[file_sha(path)].append(path.relative_to(ROOT).as_posix())
    records=[]
    _,packages,changed=inventory()
    for package in sorted({packages[i] for i in changed}):
        rounds,latest=read_reviews(package)
        first=len(checkpoint(f"tools/AssetForge/reviews/{package}.json")["rounds"])
        for review in rounds[first:]:
            for item in review["models"]:
                for field in ("inspected_sha256","frozen_png_sha256","visual_evidence_sha256"):
                    for name,digest in item.get(field,{}).items():
                        path=(REVIEW.parent/name if name.startswith("rc6/") else ROOT/name)
                        if path.is_file() and file_sha(path)==digest:
                            continue
                        matches=archives.get(digest,[])
                        source=(package,review["round"],name,digest)
                        if not matches and (latest[item["id"]] is item or source!=MISSING_INTERMEDIATE_PAGE):
                            raise ValueError(f"Missing signed inspection image for {item['id']}: {name} {digest}")
                        records.append(dict(package=package,round=review["round"],model=item["id"],
                                            cited_path=name,sha256=digest,archived_paths=matches))
    return records


def audit():
    from build_all import SHEETS, load_approved
    original_pairs=verify_checkpoint()
    entries,packages,changed=inventory()
    approved=load_approved(specifications(),False)
    for dirname,expected in zip(("families","era_geometry"),approved_family_sheets(approved)):
        if expected!={p.name:file_sha(p) for p in (SHEETS/dirname).glob("*.png")}:
            raise ValueError(f"Stale art-direction pages in {dirname}")
    publication=json.loads((ROOT/MANIFEST).read_text())
    if publication["assets"]!=sorted(approved,key=lambda e:e["id"]) or publication["bindings"]!=checkpoint(MANIFEST)["bindings"]:
        raise ValueError("Published models or bindings disagree with the reviewed catalog")
    if inspection_archive()!=json.loads((REVIEW/"inspection-archive.json").read_text()):
        raise ValueError("Historical inspection image index differs from the package ledgers or files")
    corners,_=check_families(approved)
    pairs=check_catalog(approved,corners)
    targets=listed(approved)
    votes=json.loads((REVIEW/"picture-reviews.json").read_text())["pairs"]
    extra={i for vote in votes if vote["decision"]=="rework" for i in (vote["a"],vote["b"])}
    unexpected=sorted(i for i in changed if family(entries[i]) not in targets and i not in extra)
    if unexpected:
        raise ValueError(f"Unreviewed scope expansion: {unexpected}")
    baseline_inventory=subprocess.check_output(
        ["git","show",f"{CHECKPOINT}:tools/AssetForge/reviews/rc5/rebuild-inventory.json.gz"],cwd=ROOT)
    baseline_hashes=json.loads(gzip.decompress(baseline_inventory))
    changed_previews=[]
    for entry in approved:
        for age,path in entry["age_previews"].items():
            if file_sha(ROOT/path)!=baseline_hashes[path]:
                if entry["id"] not in changed:
                    raise ValueError(f"Age preview outside the revised GLBs changed: {path}")
                changed_previews.append((entry["id"],age))
    glb_files={p.relative_to(ROOT).as_posix() for p in (ROOT/"Assets/GodSim/Art/Models/Buildings").rglob("*.glb")}
    if glb_files!={e["path"] for e in approved}:
        raise ValueError("Building model directory disagrees with the manifest")
    required=json.loads((REVIEW/"coordinator-pairs.json").read_text())
    pair_scores={(a,b):score for score,a,b in pairs}
    repairs=[(a,b) for _,a,b in required]+[(r["a"],r["b"]) for r in votes if r["decision"]=="rework"]
    for a,b in repairs:
        if not changed.intersection((a,b)):
            raise ValueError(f"Neither GLB changed for reopened pair {a}, {b}")
    models=[]
    for package in sorted({packages[i] for i in changed}):
        rounds,latest=read_reviews(package)
        old=checkpoint(f"tools/AssetForge/reviews/{package}.json")["rounds"]
        if rounds[:len(old)]!=old:
            raise ValueError(f"Checkpoint history changed in {package}")
        fresh={m["id"] for r in rounds[len(old):] for m in r["models"]}
        for ident in sorted(i for i in changed if packages[i]==package):
            if ident not in fresh or not passing(entries[ident],latest[ident]):
                raise ValueError(f"No current independent post-checkpoint approval for {ident}")
            path=entries[ident]["path"]
            old_glb=subprocess.check_output(["git","show",f"{CHECKPOINT}:{path}"],cwd=ROOT)
            new_glb=(ROOT/path).read_bytes()
            changed_levels=[i for i,((_,a,_),(_,b,_)) in enumerate(zip(
                geometry_levels(old_glb),geometry_levels(new_glb),strict=True)) if shape_digest(a)!=shape_digest(b)]
            if 0 not in changed_levels:
                raise ValueError(f"Revised model has unchanged LOD0 geometry: {ident}")
            models.append(dict(id=ident,package=package,lods=entries[ident]["lod_triangles"],
                               changed_levels=changed_levels,scores={k:latest[ident]["scores"][k] for k in CRITERIA}))
    scoped=[r for r in pairs if family(entries[r[1]]) in targets]
    scoped_corners=[p for p in corners if family(entries[p["a"]]) in targets]
    def peak(rows):
        return max(rows,key=lambda p:p["matched"]/p["total"])
    by_family=[]
    for key in sorted(targets):
        sub=[r for r in scoped if family(entries[r[1]])==key]
        directions=[p for p in scoped_corners if family(entries[p["a"]])==key]
        by_family.append(dict(family=key,changed=sum(family(entries[i])==key for i in changed),
                              picture_count=len(sub),picture_peak=sub[0],corner_peak=peak(directions)))
    return dict(checkpoint=CHECKPOINT,model_count=len(entries),compared=len(pairs),scoped_pairs=len(scoped),
                changed_age_previews=sorted(changed_previews),
                original_above_750=sum(score>=.750 for score,_,_ in original_pairs),
                scoped_peak=scoped[0],corner_peak=peak(scoped_corners),catalog_corner_peak=peak(corners),
                retained_corner_directions=[p for p in corners if 5*p["matched"]>3*p["total"]],
                catalog_peak=pairs[0],high_pairs=[r for r in pairs if r[0]>=.75],
                listed_results=[dict(a=a,b=b,before=s,after=pair_scores[a,b]) for s,a,b in required],
                screened_results=[dict(row,after=pair_scores[row["a"],row["b"]]) for row in votes],
                lod1_above_sixty=sum(5*e["lod_triangles"][1]>3*e["lod_triangles"][0] for e in approved),
                families=[list(k) for k in sorted(targets)],family_results=by_family,changed_models=models)


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    commands=parser.add_subparsers(dest="command",required=True)
    views=commands.add_parser("views")
    views.add_argument("packages",nargs="*")
    commands.add_parser("audit")
    commands.add_parser("inspection-archive")
    args=parser.parse_args()
    if args.command=="views":
        make_views(args.packages)
    elif args.command=="inspection-archive":
        print(json.dumps(inspection_archive(),indent=2))
    else:
        print(json.dumps(audit(),indent=2))
