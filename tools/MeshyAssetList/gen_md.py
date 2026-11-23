#!/usr/bin/env python3
import json, os, sys
from collections import Counter, OrderedDict

ROOT = sys.argv[1]
HERE = os.path.dirname(os.path.abspath(__file__))
ITEMS = json.load(open(os.path.join(HERE, "items.json")))
DATA = json.load(open(os.path.join(ROOT, "assets/meshy/meshy_assets.json")))
SUFFIX = DATA["styleSuffix"]

CATS = ["Buildings", "Wonders", "Units", "Props", "Nature", "Animals", "Vehicles", "Kit", "Ruins", "Divine"]
PRI = ["P0", "P1", "P2"]
AGEN = ["dawn", "bronze", "iron", "classical", "medieval", "renaissance", "industrial", "modern", "information", "ascension"]
BANDL = {"ancient": "Ancient", "classical": "Classical-Medieval", "industrial": "Industrial-Modern", "future": "Future", "all": "All"}

# cost model (assumptions are stated in the doc)
CR_PREVIEW, CR_REFINE, CR_PREVIEW_OLD = 20, 10, 5
RETRY = 1.25
USD_PER_CREDIT = 0.02
LAT_MIN, PAR = 5.0, 10
REVIEW_MIN = 6.0

def ages_str(it):
    a = it["ages"]
    if not a: return "all ages"
    idx = [AGEN.index(x) for x in a]
    rng = AGEN[idx[0]].capitalize() if len(idx) == 1 else f"{AGEN[idx[0]].capitalize()}–{AGEN[idx[-1]].capitalize()}"
    if it["band"] and it["band"] != "all":
        return f"{it['band']}: {rng}"
    return rng

def fp(it):
    f = it["footprint"]
    if f: return f"{f[0]}×{f[1]}"
    return f"h {it['targetHeight']}" if it["targetHeight"] else "—"

def esc(s): return (s or "").replace("|", "/").replace("\n", " ")

def table(rows):
    out = ["| id | category | race | building type / subject | band (ages covered) | footprint | target tris | priority | Meshy prompt (core; append style suffix) | notes |",
           "|---|---|---|---|---|---|--:|:-:|---|---|"]
    for it in rows:
        subj = it["subject"] if not it["buildingType"] else (f"{it['buildingType']}" + (f" v{it['variant']}" if it["buildingType"] == "House" else ""))
        out.append(f"| `{it['id']}` | {it['category']} | {it['race']} | {esc(subj)} | {ages_str(it)} | {fp(it)} | {it['targetPolycount']} | {it['priority']} | {esc(it['promptCore'])} | {esc(it['notes'])} |")
    return "\n".join(out)

cat_pri = {c: Counter() for c in CATS}
for it in ITEMS: cat_pri[it["category"]][it["priority"]] += 1
tot = Counter(it["priority"] for it in ITEMS)
N = len(ITEMS)

def credits(n, prev=CR_PREVIEW): return n * (prev + CR_REFINE) * RETRY
def gen_hours(n): return n * RETRY * LAT_MIN / PAR / 60
def review_hours(n): return n * REVIEW_MIN / 60

L = []
w = L.append
w("# GodSim 3D Asset List (Meshy text-to-3D)")
w("")
w("> **Purpose.** The definitive list of every 3D model GodSim needs, each with a ready-to-paste Meshy prompt.")
w("> **Machine-readable twins:** [`assets/meshy/meshy_assets.json`](../assets/meshy/meshy_assets.json) and")
w("> [`assets/meshy/meshy_assets.csv`](../assets/meshy/meshy_assets.csv) hold the same rows. Their `prompt` field is the")
w("> full text (core prompt + style suffix, at most 600 characters, Meshy's prompt limit). How to run the batch and import into Unity:")
w("> [`assets/meshy/README.md`](../assets/meshy/README.md).")
w(">")
w("> **Ground truth:** `Assets/GodSim/Core/Contracts/Data/Enums.cs` (75 `BuildingType`s), `docs/CONTENT_REFERENCE.md`")
w("> (footprints, age windows, race locks), `docs/ART_STYLE_GUIDE.md` (style, race motifs, material ladder, triangle budgets) and")
w("> the Unity renderers (`BuildingRenderer`, `PeopleRenderer`, `ArmyRenderer`, `TerrainMeshBuilder`, `TreeMeshFactory`, `VfxPresets`).")
w("> Where the enum comments and the content data disagree on an age (for example War Idol is commented Bronze and the data says")
w("> Bronze+ unlocked from a Dawn tech), **the content data wins**.")
w("")
w("## Contents")
w("1. [Summary](#1-summary)")
w("2. [Art direction for Meshy](#2-art-direction-for-meshy)")
w("3. [Technical spec](#3-technical-spec)")
w("4. [Priority tiers](#4-priority-tiers)")
w("5. [Asset tables](#5-asset-tables)")
w("6. [Not Meshy: VFX and UI icons](#6-not-meshy-vfx-and-ui-icons)")
w("7. [Decisions to confirm](#7-decisions-to-confirm)")
w("")
w("## 1. Summary")
w("")
w(f"**{N} Meshy assets** in total: **P0 {tot['P0']}**, **P1 {tot['P1']}**, **P2 {tot['P2']}**.")
w("")
w("| Category | P0 | P1 | P2 | Total | What it covers |")
w("|---|--:|--:|--:|--:|---|")
WHAT = {
 "Buildings": "66 non-wonder BuildingTypes × 5 races × the age bands each one exists in; House has 2 variants",
 "Wonders": "5 racial wonders per band (own race only) + 4 race-neutral (Great Library, Colossus, Grand Bazaar, Ascension Gate)",
 "Units": "villager adult/child/elder per race, soldier per race per band, hero/prophet per race",
 "Props": "20 job props (one per JobType except None/Soldier), town props, race banners and signature props",
 "Nature": "trees and plants for 5 biome groups + scorched, rocks, crop tiles, resource nodes",
 "Animals": "livestock and wildlife",
 "Vehicles": "carts, siege engines, trains, cars, aircraft and boats per band",
 "Kit": "bridges and road tiles per band; palisade and stone wall corner/gate per race",
 "Ruins": "ruins per band in 3 sizes, rubble, scaffolding per band in 3 sizes, building-material pile",
 "Divine": "meteor, crater, sacred stones, spring, fissure and other god-power aftermath",
}
for c in CATS:
    cp = cat_pri[c]
    w(f"| {c} | {cp['P0']} | {cp['P1']} | {cp['P2']} | {sum(cp.values())} | {WHAT[c]} |")
w(f"| **Total** | **{tot['P0']}** | **{tot['P1']}** | **{tot['P2']}** | **{N}** | |")
w("")
bb = Counter(it["band"] for it in ITEMS if it["category"] == "Buildings")
w(f"Building models by band (all 5 races): Ancient {bb['ancient']}, Classical-Medieval {bb['classical']}, "
  f"Industrial-Modern {bb['industrial']}, Future {bb['future']} ({bb['ancient'] // 5}/{bb['classical'] // 5}/{bb['industrial'] // 5}/{bb['future'] // 5} per race).")
w("")
w("### Cost and time estimate")
w("")
w("One Meshy **preview** (geometry, remeshed to the target polycount) plus one **refine** (texture) per asset, with a 25% re-roll allowance.")
w("")
w("| Tier | Assets | Credits (latest model: 20 + 10) | ≈ USD @ $0.02/credit | Credits (Meshy-5: 5 + 10) | ≈ USD | Generation wall-clock (10 parallel) | Review + Unity import (6 min/asset) |")
w("|---|--:|--:|--:|--:|--:|--:|--:|")
for name, n in [("P0", tot["P0"]), ("P1", tot["P1"]), ("P2", tot["P2"]), ("**All**", N)]:
    c1, c2 = credits(n), credits(n, CR_PREVIEW_OLD)
    w(f"| {name} | {n} | {c1:,.0f} | ${c1 * USD_PER_CREDIT:,.0f} | {c2:,.0f} | ${c2 * USD_PER_CREDIT:,.0f} | {gen_hours(n):.1f} h | {review_hours(n):.0f} h |")
w("")
w("Assumptions (verify before buying credits; docs.meshy.ai was not reachable from the authoring session):")
w("- Credit prices per task: preview 20 credits on the current default model (`ai_model: \"latest\"`), 5 on `meshy-5`/`meshy-4`; refine 10 credits.")
w("- About US$0.02 per credit, the Pro-plan list rate. Larger plans are cheaper per credit.")
w("- About 5 minutes of latency per asset (preview plus refine), with 10 tasks in flight at once. The real concurrency limit depends on the plan.")
w("- Human time: about 6 minutes per asset to pick the best roll, check scale, pivot and orientation in Unity, and log a re-roll. Count roughly 1 person-day per 60–80 assets.")
w("")
w("## 2. Art direction for Meshy")
w("")
w("**Style: Storybook Monumental.** Gravitas lives in the silhouette and whimsy in the details. Every prompt has four parts:")
w("")
w("1. **Subject and silhouette.** Race, building type, era, footprint shape, then the age-correct structure. A mine gets a headframe and a spoil heap. Industrial chimneys rise from the boiler house.")
w("2. **Race × band style.** Materials from the age ladder, plus the race's signature motifs, which appear on every asset the race owns.")
w("3. **Charming details.** 1–2 chosen per asset from the race's pool, on top of the type's own functional details (2–4 in total).")
w("4. **The global style suffix.** It is identical on every asset, so the whole set reads as one family.")
w("")
w("### Global style suffix (appended to every prompt)")
w("")
w("```text")
w(SUFFIX)
w("```")
w("")
w("### Negative prompt")
w("")
w("Base, used on every asset:")
w("")
w("```text")
w(DATA["items"][0]["negativePrompt"].split(", people")[0])
w("```")
w("")
w("Then add the line for the asset's category (already merged into each row's `negativePrompt`):")
w("")
w("| Category | Extra negatives |")
w("|---|---|")
EXTRA = OrderedDict()
for it in DATA["items"]:
    if it["category"] not in EXTRA:
        base = DATA["items"][0]["negativePrompt"].split(", people")[0]
        EXTRA[it["category"]] = it["negativePrompt"][len(base) + 2:]
for c, e in EXTRA.items(): w(f"| {c} | {e} |")
w("")
w("Guidance:")
w("- Newer Meshy models may ignore `negative_prompt`, so the positive prompts are written to exclude problems on their own. Examples: \"single isolated object\", \"no animals\" on pastures, \"no crew\" on vehicles.")
w("- Keep the negative list short. If an unwanted feature keeps appearing (Meshy likes round display bases), re-roll with a new seed instead of growing the list.")
w("- **Never** name existing games, films, studios, brands, real nations, real monuments or real uniforms in a prompt. Everything is original.")
w("- **Orcs are never gory.** They get stylised horned masks and totems, with no skulls or trophies. The War Idol lore mentions trophy skulls; the art deliberately does not show them.")
w("- **No text.** Signs are blank or carry pictograms (a leaf, a coin, an anvil). Never use red-cross medical symbols: hospitals use a leaf-and-hand sign.")
w("")
w("### Texture prompt (refine step)")
w("")
w("Each row carries a `texturePrompt` for the refine call: the race palette (from `ArchitectureStyle` colours) plus the band materials. For example:")
w("")
w("```text")
w(next(i for i in DATA["items"] if i["id"] == "bld_dwarf_mine_ancient_v0")["texturePrompt"])
w("```")
w("")
w("### Race and band quick reference (as used in the prompts)")
w("")
w("| Race | Colours (wall / roof / trim / accent / banner) | Signature motifs | Roof | Proportions |")
w("|---|---|---|---|---|")
w("| Human | `#D8C7A1` / `#8C3B2A` / `#5A3A1E` / `#3B6FD8` / blue | timber framing, flower boxes, chimneys, shutters, blue banners, cobbled plinths | gable / hip | balanced, upright |")
w("| Elf | `#E9E4CF` / `#4E8F3A` / `#A8894A` / `#BFD8E6` / green | living trunks, leaf roofs, curved ribs, lantern pods, moon-silver; nothing square | leaf / dome | tall, slender, round |")
w("| Dwarf | `#7A7268` / `#45403B` / `#B87333` / `#C8741E` / amber | squat carved stone, rune bands, brass trim, half-sunk halls, forge glow, buttresses | flat / tiered | squat 0.75 h, wide 1.25 w |")
w("| Orc | `#8A6A48` / `#5B4530` / `#E6D9BC` / `#B3261E` / red | hide and lashed timber, tusk spikes, red war-paint stripes, stylised horned totems, drums | cone / spiked | chunky, sprawling, spiky |")
w("| Saurian | `#C8B07A` / `#3D7A57` / `#5C4A2E` / `#E3A82B` / purple | stilts over water, stepped pyramids, gold sun discs, reed thatch, teal inlays, star charts | pyramid / stepped | low wide bases, stepped |")
w("")
w("| Band | Ages | Material ladder in the prompts |")
w("|---|---|---|")
w("| Ancient | Dawn, Bronze, Iron | thatch and hide, then mudbrick and ochre with copper accents, then fieldstone and dark timber |")
w("| Classical-Medieval | Classical, Medieval, Renaissance | white marble and columns, then grey stone, half-timber and slate, then warm plaster and terracotta domes |")
w("| Industrial-Modern | Industrial, Modern | red brick, soot, smokestacks and iron girders, then concrete, steel and big windows |")
w("| Future | Information, Ascension | glass bands, white panels, cyan accents and antennae, then pearl white and gold with emissive cyan and supported rings |")
w("")
w("## 3. Technical spec")
w("")
w("| Topic | Rule |")
w("|---|---|")
w("| Axes | Y-up. Front/door faces **−Z**. Right-handed glTF convention; glTFast converts to Unity. |")
w("| Scale | **1 tile = 1 m = 1 unit.** Buildings, nature, kit, ruins and divine objects fit their footprint W×D tiles, with at most +25% overhang for roofs and props. Footprint W runs along X and D along Z, so a 2×1 building has its long side to the front. |")
w("| Pivot | Ground centre: the footprint is centred on the origin and the ground sits at y = 0. Dock shapes (Fishing Hut, Harbor) are placed at the highest footprint tile, so piles extend about 0.3 below y = 0. |")
w("| Units | Normalised the way `UnitMeshes` already does it: **adult height = 1.0 unit**, pivot at the feet. The renderer scales people to 0.30 tile (`PeopleRenderer.PersonHeight`) and soldiers to 0.32 tile (`ArmyRenderer.SoldierHeight`). Child and elder models are also authored at height 1.0, because the renderer applies 0.6 and 0.95 itself. Job props are authored in the same villager space and attach to a right-hand socket. |")
w("| Animals and vehicles | World scale next to a 0.30-tile person. Each row's `targetHeight` and notes give the size (for example a sheep is 0.14 and a cargo ship is at most 2 tiles long). |")
w("| Topology | Triangles (`topology: triangle`), remeshed by Meshy to `target_polycount`. Closed surfaces where visible, no floating parts, no z-fighting (decals offset 0.01). |")
w("| Materials | One material per model. A base-colour texture is enough, and `enable_pbr: false` is the default because the look is matte and flat-ish. Emissive areas (windows, forge glow, Ascension light) can be split into a second `emissive` material in Unity. Keep tint areas (banners, soldier tunics, sails) light and neutral so the civ-colour tint reads. |")
w("| Export | **GLB** (primary, imported with glTFast). FBX is kept as a fallback. Textures are embedded in the GLB, at most 1024² (512² for props, nature and units). |")
w("| LOD | One LOD from Meshy. The renderer already swaps far buildings to a box LOD, and a second LOD can be decimated in Unity later. |")
w("| Folder | `Assets/GodSim/Art/Models/<Category>/…`: `Buildings/<Race>/`, `Wonders/`, `Units/<Race>/`, `Props/Jobs/`, `Props/Town/`, `Nature/<Group>/`, `Animals/`, `Vehicles/`, `Kit/`, `Ruins/`, `Construction/`, `Divine/`. The `outputPath` column is authoritative. |")
w("")
w("**Triangle targets** (from the style guide's `forge.registry` budgets, and set as Meshy `target_polycount`):")
w("")
w("| Kind | Target tris | Used for |")
w("|---|--:|---|")
w("| house | 1800 | House v0/v1 |")
w("| townhall | 4500 | Town Center |")
w("| building | 2500 (1×1) / 3500 (larger) | every other building; ruins 900 × size |")
w("| wonder | 9000 | racial and shared wonders, Ascension Gate |")
w("| kit | 900 (road tiles 300) | Palisade, Stone Wall, wall corners/gates, bridges, scaffolding |")
w("| unit | 700 | villagers, soldiers, heroes |")
w("| prop | 500 (job props and banners 300) | town props, rubble, divine objects |")
w("| nature | 150–700 | trees 500–700, plants 150–300, rocks 300–700, crop tiles 300–600, nodes 400–500 |")
w("| animal | 500 | all animals |")
w("| vehicle | 1000 (large ships 1500) | carts, siege engines, trains, cars, aircraft, boats |")
w("")
w("**Naming.** `[a-z0-9_]`, matching the existing ids (`bld_dwarf_mine_bronze_v0`, `nat_tree_oak_v2`, `unit_orc_soldier_iron`, `prop_market_stall_v1`):")
w("")
w("| Pattern | Example |")
w("|---|---|")
w("| `bld_<race>_<building_type_snake>_<band>_v<variant>` | `bld_dwarf_mine_ancient_v0`, `bld_human_house_classical_v1` |")
w("| `bld_neutral_<wonder>_v0` (race-neutral wonders, one per wonder) | `bld_neutral_great_library_v0` |")
w("| `unit_<race>_villager_<stage>` (adult, child, elder), `unit_<race>_soldier_<band>`, `unit_<race>_hero` | `unit_orc_soldier_industrial` |")
w("| `prop_job_<job>_v0`, `prop_<thing>_v0`, `prop_<race>_banner_v0` | `prop_job_miner_v0` |")
w("| `nat_<kind>_<name>_v<n>` (kind: tree, plant, bush, rock, crop, node) | `nat_tree_oak_v0`, `nat_node_gold_v0` |")
w("| `ani_<animal>_v0`, `veh_<vehicle>_<band>_v0`, `kit_<piece>_..._v0`, `ruin_...`, `con_...`, `div_..._v0` | `veh_boat_cargo_classical_v0`, `kit_wall_orc_palisade_gate_v0` |")
w("")
w("The band ids are `ancient` (Dawn–Iron), `classical` (Classical–Renaissance), `industrial` (Industrial–Modern) and `future` (Information–Ascension). Each row's `ages[]` lists the ages the model actually covers, which is the band clipped to the building's MinAge..MaxAge. That matches `AssetMeta.ages` in `tools/AssetForge`, so Meshy models can share the existing manifest and art-library lookup.")
w("")
w("## 4. Priority tiers")
w("")
w(f"- **P0, the vertical slice ({tot['P0']} assets).** Enough to make a Dawn–Iron world look complete at a glance:")
w("  - all 5 races' Town Center and House v0/v1 in the Ancient band;")
w("  - the core Dawn–Bronze production buildings in the Ancient band: Gatherer's Hut, Hunting Lodge, Fishing Hut, Woodcutter's Camp, Stone Quarry, Farm, Mine and Smelter;")
w("  - villager adult, child and elder for every race;")
w("  - the 9 job props those workers use;")
w("  - 7 basic trees, at least one per biome group: oak ×2, spruce ×2, palm, acacia, mangrove;")
w("  - 3 rocks;")
w("  - 3 town props (crates and barrels, firewood, haystack) and the 5 race banners.")
w(f"- **P1, the full Ancient and Classical-Medieval bands ({tot['P1']} assets).** This covers:")
w("  - every other building, racial wonder, soldier, vehicle, bridge, wall-kit piece, ruin and scaffold in those two bands;")
w("  - the three shared Classical/Medieval wonders;")
w("  - most job props, nature, rocks, crops and resource nodes (except coal and oil, which are Industrial+);")
w("  - livestock and deer;")
w("  - the main divine objects (meteor, crater, sacred stones).")
w(f"- **P2, the rest ({tot['P2']} assets).** This covers:")
w("  - the Industrial-Modern and Future bands;")
w("  - the Ascension Gate;")
w("  - heroes;")
w("  - wildlife;")
w("  - road overlay tiles (roads are currently baked into the terrain colours);")
w("  - extra nature variants and the remaining divine aftermath.")
w("")
w("Generate P0 in one sitting, review it together, adjust the suffix and the race lines if needed, then batch P1.")
w("")
w("## 5. Asset tables")
w("")
w("The prompt column shows the **core** prompt. Append the global style suffix from section 2 to get the full prompt. The CSV/JSON `prompt` field already includes it.")
w("")
w("### 5.1 Buildings")
w("")
RN = {"human": "Human", "elf": "Elf", "dwarf": "Dwarf", "orc": "Orc", "saurian": "Saurian"}
for race in ["human", "elf", "dwarf", "orc", "saurian"]:
    rows = [i for i in ITEMS if i["category"] == "Buildings" and i["race"] == race]
    pc = Counter(i["priority"] for i in rows)
    w(f"#### {RN[race]} ({len(rows)}: P0 {pc['P0']}, P1 {pc['P1']}, P2 {pc['P2']})")
    w("")
    w(table(rows))
    w("")
SECTIONS = [
    ("5.2 Wonders", lambda i: i["category"] == "Wonders"),
    ("5.3 Units: villagers, soldiers, heroes", lambda i: i["category"] == "Units"),
    ("5.4 Job props (villager attachments)", lambda i: i["category"] == "Props" and i["id"].startswith("prop_job_")),
    ("5.5 Town props", lambda i: i["category"] == "Props" and not i["id"].startswith("prop_job_")),
    ("5.6 Nature: trees, plants, rocks, crops, resource nodes", lambda i: i["category"] == "Nature"),
    ("5.7 Animals", lambda i: i["category"] == "Animals"),
    ("5.8 Vehicles and boats", lambda i: i["category"] == "Vehicles"),
    ("5.9 Kit: bridges, roads, wall pieces", lambda i: i["category"] == "Kit"),
    ("5.10 Ruins, rubble and construction", lambda i: i["category"] == "Ruins"),
    ("5.11 Divine artifacts", lambda i: i["category"] == "Divine"),
]
for title, pred in SECTIONS:
    rows = [i for i in ITEMS if pred(i)]
    pc = Counter(i["priority"] for i in rows)
    w(f"### {title} ({len(rows)}: P0 {pc['P0']}, P1 {pc['P1']}, P2 {pc['P2']})")
    w("")
    w(table(rows))
    w("")
w("## 6. Not Meshy: VFX and UI icons")
w("")
w("These are particles, shaders or 2D art. They are listed for completeness and **are not counted** in the totals above.")
w("")
w("| Kind | Items | Source / owner | Notes |")
w("|---|---|---|---|")
w("| Particle presets (15) | Dust, Clash, Explosion, RockDust, Sparkles, Motes, Mist, Splash, Flame, Smoke, Embers, LightPillar, HeatHaze, Rain, Snow | `Unity/Runtime/Effects/VfxPresets.cs` (`VfxKind`) | Needs 2D sprite sheets: soft puff, spark, ember, raindrop, snowflake, ring and glow. |")
w("| God-power VFX (19) | Bless, Rain, Fertility, Inspire, Heal, BountifulHarvest, Prophet, RaiseLand, LowerLand, PlantForest, RevealOre, SpawnTribe, Lightning, Wildfire, Earthquake, Meteor, Plague, Flood, Drought | `GodPowerVFX.cs` | Built from the presets. Meteor uses `div_meteor_v0` as the falling body and Lightning needs a bolt ribbon shader. |")
w("| Fire, weather, day/night | Building fires, wildfire front, storms, rain and snow, sky gradient | `FireVFX.cs`, `WeatherVFX.cs`, `DayNightCycle.cs` | Shader and particle work. |")
w("| Decals and overlays | Selection ring, power-radius ring, territory borders, overlay ramps | `Interaction/*`, `Overlays/*` | Projector or shader. |")
w("| UI icons, buildings (75) | One per `BuildingType` | HUD build menu and inspector | 2D. Can be rendered from the P0/P1 models as 128² thumbnails. |")
w("| UI icons, other | 13 resources, 9 services, 19 god powers, 21 jobs, 5 race crests, 10 age emblems, 12 chronicle categories, 7 settlement tiers, overlay legends | `HudSkin`, `HudPowerBar`, `HudChronicle` | 2D, about 171 icons. |")
w("")
w("## 7. Decisions to confirm")
w("")
w("1. **Band ids and the age split.** The ids are `ancient`, `classical`, `industrial` and `future`. Renaissance sits in the `classical` band, so a Renaissance Bank looks like its Medieval sibling. StyleAge maps to a band with no blending.")
w("2. **Racial wonders get a model per band; shared wonders get one model.**")
w("   - That makes 19 racial-wonder models plus 4 shared ones.")
w("   - Alternative: one timeless model per racial wonder. That is 5 models instead of 19, and saves 14 × 9000-tri generations.")
w("3. **Textures instead of vertex colours.** Meshy outputs UV-textured meshes, but the style guide and the instanced renderers expect flat vertex colours with a per-instance tint.")
w("   - Proposal: import with base-colour textures and add an import step that bakes the texture into `COLOR_0` for instanced people, soldiers and trees, so the tint keeps working.")
w("   - Alternative: keep the textures and switch those renderers to per-race materials.")
w("4. **Scope of things the game does not render yet.** There is no animal, vehicle, boat or road-overlay renderer today, and naval play is post-1.0. Those 30 vehicles, 15 animals and 12 road tiles are budgeted anyway: ancient and classical vehicles in P1, the rest in P2.")
w("   - Option: defer all of them and save about 60 generations.")
w("5. **Meshy API parameters are assumed, not verified.** docs.meshy.ai was blocked from the authoring session, so the endpoint, credit costs and the `art_style` / `negative_prompt` behaviour should be checked once before the batch runs.")
w("   - The assumed values are listed in `assets/meshy/README.md`.")
w("   - Each prompt fits Meshy's 600-character prompt limit.")
w("")
open(os.path.join(ROOT, "docs/ASSET_LIST.md"), "w", encoding="utf-8").write("\n".join(L) + "\n")
print("md bytes", os.path.getsize(os.path.join(ROOT, "docs/ASSET_LIST.md")))
for name, n in [("P0", tot["P0"]), ("P1", tot["P1"]), ("P2", tot["P2"]), ("All", N)]:
    print(name, n, round(credits(n)), round(credits(n) * USD_PER_CREDIT), round(gen_hours(n), 1), round(review_hours(n)))
