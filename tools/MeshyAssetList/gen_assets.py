#!/usr/bin/env python3
"""Generates docs/ASSET_LIST.md, assets/meshy/meshy_assets.csv and .json for GodSim (Meshy text-to-3D)."""
import csv, json, os, sys, zlib
from collections import Counter, OrderedDict

ROOT = sys.argv[1] if len(sys.argv) > 1 else "/home/user/GodSim"
MODELS = "Assets/GodSim/Art/Models"
MAX_PROMPT = 600

AGES = ["dawn", "bronze", "iron", "classical", "medieval", "renaissance", "industrial", "modern", "information", "ascension"]
BANDS = OrderedDict([
    ("ancient", ("Ancient", [0, 1, 2])),
    ("classical", ("Classical-Medieval", [3, 4, 5])),
    ("industrial", ("Industrial-Modern", [6, 7])),
    ("future", ("Future", [8, 9])),
])
BAND_ERA = {"ancient": "ancient era", "classical": "classical-medieval era",
            "industrial": "industrial era", "future": "far-future era"}
RACES = ["human", "elf", "dwarf", "orc", "saurian"]
RNAME = {"human": "Human", "elf": "Elf", "dwarf": "Dwarf", "orc": "Orc", "saurian": "Saurian"}

SUFFIX = ("Storybook Monumental style: stylized, chunky hand-crafted proportions, believable construction, "
          "matte hand-painted colors. Game asset, isometric-friendly readable silhouette.")

NEG_BASE = ("photorealistic, photo scan, gritty grime, noisy texture, text, letters, logo, watermark, gore, blood, "
            "skulls, horror, floating parts, disconnected pieces, thin wispy geometry, multiple objects, scene, "
            "round display pedestal, terrain island, background, blurry, broken mesh")
NEG_EXTRA = {
    "Buildings": "people, characters, animals, vehicles, interior cutaway, ruined, overgrown",
    "Wonders": "people, characters, vehicles, interior cutaway, ruined",
    "Units": "multiple characters, mount, extra limbs, nudity, aggressive pose, weapon pointed at viewer, base plate",
    "Props": "characters, building, base plate",
    "Nature": "pot, planter, vase, building, characters, base plate",
    "Animals": "rider, saddle, harness, multiple animals, base plate, aggressive snarl",
    "Vehicles": "characters, drivers, passengers, base plate, brand logo",
    "Kit": "characters, vehicles, base plate",
    "Ruins": "characters, intact roof",
    "Divine": "characters, base plate",
}

# Race x band: materials + signature motifs (style guide section 2, material ladder section 3).
STYLE = {
    "human": {
        "ancient": "Wattle-and-daub walls on a cobbled plinth, raw timber posts, thick golden thatch gable roof, small blue pennant",
        "classical": "Half-timbered upper storey over grey fieldstone, steep slate gable roof, carved shutters, flower boxes, blue-and-cream banners",
        "industrial": "Red brick with white stone trim, slate gable roofs, sash windows, leaning chimneys, flower boxes, blue enamel accents",
        "future": "Pearl-white panels with warm timber accents, gable-shaped solar glass roof, cyan light strips, hanging flower boxes, blue light banner",
    },
    "elf": {
        "ancient": "Grown from a living tree trunk, curved woven-root walls, broad layered leaf roof, vines, glowing lantern pods, moon-silver charms, nothing square",
        "classical": "Pale heartwood ribs curving like a seed pod, leaf-shingle dome roof, moon-silver filigree, crescent windows, lantern pods, flowering vines",
        "industrial": "Living-wood frame laced with silver-glass greenhouse panes, verdigris leaf dome, curved root-pipes, lantern pods, moon-silver trim",
        "future": "Luminous white living bark spiralling upward, translucent leaf canopy dome, moon-silver rings, glowing lantern pods on vines, all curves",
    },
    "dwarf": {
        "ancient": "Squat and half-sunk into a turf mound, rough-cut stone blocks, thick timber lintels, flat sod roof, rune-carved doorstone, ember glow",
        "classical": "Heavy dressed granite, thick buttresses, flat tiered stone roof, brass-banded doors, carved rune bands, warm forge-glow windows, half-sunk",
        "industrial": "Dark granite with riveted brass plating, flat tiered roof, squat smokestacks, glowing furnace vents, gear-and-rune reliefs",
        "future": "Polished basalt monolith, half-sunk, flat terraced roof, gold-brass geometric runes glowing amber, heat-glow vents",
    },
    "orc": {
        "ancient": "Stitched hide over lashed timber poles, tall spiked cone roof, bone-white tusk spikes, bold red war-paint stripes, stylised horned totem",
        "classical": "Rope-lashed log walls with iron bands, layered hide cone roofs with spikes, red-striped shields, horned totem poles",
        "industrial": "Scrap-iron plates riveted over heavy timber, spiked corrugated cone roofs, red painted stripes, smoking stovepipes, horned emblems",
        "future": "Angular dark alloy plates over stretched synthetic hide, spiked cone roof, glowing red stripes, horned holo-totem, chunky bolts",
    },
    "saurian": {
        "ancient": "Raised on stilts over shallow water, mudbrick and bamboo walls, stepped reed-thatch roof, gold sun disc over the door, teal bead inlays",
        "classical": "Low wide stepped limestone terraces, stilted walkways, star-chart friezes, gold sun discs, teal tile inlays, reed awnings",
        "industrial": "Stepped ochre brick and cast-concrete terraces on stilts, copper roofs gone teal, sun-disc clock faces, teal ceramic inlays",
        "future": "Stepped white-and-gold terraces on slender stilts over reflecting pools, solar sun-disc arrays, teal glass inlays, glowing star charts",
    },
}

DETAILS = {
    "human": ["flower boxes", "laundry line", "crooked signboard", "birdhouse", "stacked firewood", "a cat on the step", "barrels by the door"],
    "elf": ["shell wind chimes", "glowing mushrooms", "a tiny moon-silver owl statue", "fern pots", "a vine swing", "hanging lantern pods"],
    "dwarf": ["stacked ale kegs", "brass lanterns", "a tiny stone ancestor statue", "coal sacks", "a little mine cart", "a rune-carved anvil"],
    "orc": ["a big war drum", "painted hide shields", "a drying-hide rack", "a bubbling cook pot", "a stack of logs", "a horned banner pole"],
    "saurian": ["a fish drying rack", "clay sun pots", "a reed canoe tied below", "a carved frog statue", "a sundial", "coiled rope"],
}

PALETTE = {
    "human": "cream plaster, warm brown timber, brick-red roof, royal blue accents",
    "elf": "ivory bark, moss-green leaves, antique gold wood, pale moon-silver accents",
    "dwarf": "warm grey stone, dark slate roof, copper-brass trim, amber forge glow",
    "orc": "tan hide, smoked brown leather, bone-white trim, war-red stripes",
    "saurian": "sandy ochre stone, jade-green roof, dark wood, gold and turquoise accents",
    "neutral": "warm stone, natural wood, muted earthy tones with one bright accent",
}
BAND_MAT = {
    "ancient": "thatch, hide, raw timber, mudbrick, fieldstone",
    "classical": "white marble, terracotta, grey stone, half-timber, slate, warm plaster",
    "industrial": "red brick, soot, iron girders, concrete, steel, big windows",
    "future": "glass bands, white panels, pearl white and gold, soft emissive cyan",
}

def shape_words(w, d):
    if w == d:
        return {1: "compact square base", 2: "square base", 3: "broad square base", 4: "huge square base"}[w]
    a, b = max(w, d), min(w, d)
    return f"rectangular {a}:{b} base, long side facing front" if w > d else f"long rectangular {a}:{b} base, running front to back"

# ---------------------------------------------------------------- buildings (enum order)
# name, enum, min, max, w, d, category, races ('all' or race), procedural shape
B = [
    ("TownCenter", 0, 0, 9, 2, 2, "Civic"), ("House", 1, 0, 9, 1, 1, "Housing"), ("Well", 2, 0, 9, 1, 1, "Infrastructure"),
    ("Storehouse", 3, 0, 9, 2, 1, "Storage"), ("GathererHut", 4, 0, 2, 1, 1, "Food"), ("HuntingLodge", 5, 0, 4, 1, 1, "Food"),
    ("FishingHut", 6, 0, 9, 1, 1, "Food"), ("WoodcutterCamp", 7, 0, 9, 1, 1, "Resource"), ("StoneQuarry", 8, 0, 9, 2, 2, "Resource"),
    ("SpiritTotem", 9, 0, 2, 1, 1, "Religion"), ("Campfire", 10, 0, 3, 1, 1, "Culture"),
    ("Farm", 11, 1, 9, 3, 3, "Food"), ("Pasture", 12, 1, 9, 3, 3, "Food"), ("Granary", 13, 1, 9, 2, 2, "Storage"),
    ("Mine", 14, 1, 9, 2, 2, "Resource"), ("Smelter", 15, 1, 9, 2, 1, "Industry"), ("Shrine", 16, 1, 9, 1, 1, "Religion"),
    ("Herbalist", 17, 1, 9, 1, 1, "Health"), ("Weaver", 18, 1, 9, 1, 1, "Industry"), ("Palisade", 19, 1, 4, 3, 1, "Military"),
    ("Blacksmith", 20, 2, 9, 2, 1, "Industry"), ("Temple", 21, 2, 9, 2, 2, "Religion"), ("Barracks", 22, 2, 9, 2, 2, "Military"),
    ("Market", 23, 2, 9, 2, 2, "Commerce"), ("Watchtower", 24, 2, 6, 1, 1, "Military"), ("Armory", 25, 2, 9, 2, 1, "Industry"),
    ("GoldMine", 26, 2, 9, 2, 2, "Resource"), ("Scriptorium", 27, 2, 9, 2, 1, "Education"),
    ("Library", 28, 3, 9, 2, 2, "Education"), ("Aqueduct", 29, 3, 9, 1, 3, "Infrastructure"), ("Bathhouse", 30, 3, 9, 2, 2, "Health"),
    ("Theater", 31, 3, 9, 2, 2, "Culture"), ("Harbor", 32, 3, 9, 2, 2, "Commerce"), ("Workshop", 33, 3, 7, 2, 2, "Industry"),
    ("Forum", 34, 3, 9, 3, 2, "Civic"), ("Arena", 35, 3, 9, 3, 3, "Culture"), ("StoneWall", 36, 3, 6, 1, 1, "Military"),
    ("Castle", 37, 4, 5, 3, 3, "Military"), ("Cathedral", 38, 4, 9, 3, 3, "Religion"), ("Monastery", 39, 4, 9, 2, 2, "Religion"),
    ("Mill", 40, 4, 6, 2, 2, "Food"), ("Guildhall", 41, 4, 9, 2, 2, "Commerce"), ("Tavern", 42, 4, 9, 2, 2, "Culture"),
    ("University", 43, 5, 9, 3, 2, "Education"), ("Bank", 44, 5, 9, 2, 2, "Commerce"), ("Observatory", 45, 5, 9, 2, 2, "Education"),
    ("Hospital", 46, 5, 9, 2, 2, "Health"), ("Printworks", 47, 5, 9, 2, 2, "Education"),
    ("Factory", 48, 6, 9, 3, 3, "Industry"), ("CoalMine", 49, 6, 9, 2, 2, "Resource"), ("Sawmill", 50, 6, 9, 2, 2, "Resource"),
    ("RailStation", 51, 6, 9, 3, 3, "Commerce"), ("Foundry", 52, 6, 9, 3, 3, "Industry"),
    ("PowerPlant", 53, 7, 9, 3, 3, "Infrastructure"), ("OilWell", 54, 7, 9, 2, 2, "Resource"), ("IndustrialFarm", 55, 7, 9, 3, 3, "Food"),
    ("ResearchLab", 56, 7, 9, 2, 2, "Education"), ("Stadium", 57, 7, 9, 3, 3, "Culture"), ("MilitaryBase", 58, 7, 9, 3, 3, "Military"),
    ("DataCenter", 59, 8, 9, 2, 2, "Education"), ("ElectronicsPlant", 60, 8, 9, 3, 3, "Industry"), ("Airport", 61, 8, 9, 3, 3, "Commerce"),
    ("MedicalCenter", 62, 8, 9, 2, 2, "Health"),
    ("HydroponicsTower", 63, 9, 9, 2, 2, "Food"), ("FusionReactor", 64, 9, 9, 3, 3, "Infrastructure"), ("Arcology", 65, 9, 9, 3, 3, "Civic"),
]
WONDERS = [  # name, enum, min, max, w, d, race
    ("AscensionGate", 66, 9, 9, 4, 4, "neutral"), ("GrandCathedral", 67, 4, 9, 3, 3, "human"), ("WorldTree", 68, 2, 9, 3, 3, "elf"),
    ("DeepForge", 69, 1, 9, 3, 3, "dwarf"), ("WarIdol", 70, 1, 9, 2, 2, "orc"), ("SunPyramid", 71, 2, 9, 3, 3, "saurian"),
    ("GreatLibrary", 72, 3, 9, 3, 3, "neutral"), ("Colossus", 73, 3, 9, 3, 3, "neutral"), ("GrandBazaar", 74, 4, 9, 3, 3, "neutral"),
]
LABEL = {"TownCenter": "Town Center", "GathererHut": "Gatherer's Hut", "HuntingLodge": "Hunting Lodge", "FishingHut": "Fishing Hut",
         "WoodcutterCamp": "Woodcutter's Camp", "StoneQuarry": "Stone Quarry", "SpiritTotem": "Spirit Totem", "GoldMine": "Gold Mine",
         "StoneWall": "Stone Wall", "CoalMine": "Coal Mine", "RailStation": "Rail Station", "PowerPlant": "Power Plant",
         "OilWell": "Oil Well", "IndustrialFarm": "Industrial Farm", "ResearchLab": "Research Lab", "MilitaryBase": "Military Base",
         "DataCenter": "Data Center", "ElectronicsPlant": "Electronics Plant", "MedicalCenter": "Medical Center",
         "HydroponicsTower": "Hydroponics Tower", "FusionReactor": "Fusion Reactor", "AscensionGate": "Ascension Gate",
         "GrandCathedral": "Grand Cathedral", "WorldTree": "World Tree", "DeepForge": "Deep Forge", "WarIdol": "War Idol",
         "SunPyramid": "Sun Pyramid", "GreatLibrary": "Great Library", "GrandBazaar": "Grand Bazaar"}

def label(t): return LABEL.get(t, t)
def snake(t):
    out = ""
    for i, ch in enumerate(t):
        if ch.isupper() and i > 0: out += "_"
        out += ch.lower()
    return out

# Per type, per band: silhouette + function (serious bones) + type-specific details.
F = {
 "TownCenter": {
  "ancient": "chieftain's longhouse, the biggest roof in the village, smoke hole, carved entrance posts, council bench, fire bowl out front",
  "classical": "town hall on a raised stepped plinth, sturdy bell tower over an arched entrance, notice board, civic flag",
  "industrial": "civic hall of brick and stone with a central clock tower, broad entrance steps, flagpole, iron lamp posts",
  "future": "civic hall with a tall central tower crowned by a glowing ring, glass atrium entrance, plaza steps"},
 "House": {
  "ancient": ["one-room cottage, roof half the height, low front door, smoke hole", "cottage with a side lean-to store and porch, roof half the height"],
  "classical": ["two-storey townhouse, upper floor overhanging, chimney, front door with step", "cottage with a dormer window and covered side porch"],
  "industrial": ["narrow two-storey terraced house, bay window, chimney pots", "corner house with a small shopfront awning on the ground floor"],
  "future": ["compact eco-home with rooftop garden and glowing doorway", "two-level home with balcony, solar roof and planters"]},
 "Well": {
  "ancient": "village well: round stone ring, timber A-frame with rope and wooden bucket",
  "classical": "covered well under a small roof on four posts, winch crank, stone trough",
  "industrial": "cast-iron hand pump on a small brick well-house with a water tank",
  "future": "water purifier column with glowing filter rings over a fountain basin"},
 "Storehouse": {
  "ancient": "long low storage shed with an open front stacked with sacks, logs and baskets",
  "classical": "two-storey warehouse with a hoist beam and pulley above a loft door, crates",
  "industrial": "brick depot with large sliding doors, loading ramp, crates on pallets",
  "future": "storage hall with a sliding hatch and neatly stacked glowing cargo pods"},
 "GathererHut": {"ancient": "humble forager's hut, drying racks of berries and roots, woven baskets by the door"},
 "HuntingLodge": {
  "ancient": "hunter's lodge with antlers over the gable, hide-stretching frame, spear rack",
  "classical": "timber hunting lodge with smokehouse chimney, bow rack, antler sign, small kennel"},
 "FishingHut": {
  "ancient": "fisher's hut on short piles at the water's edge, fish drying racks, nets, tiny dugout canoe",
  "classical": "fishing house with a small jetty, net reels, fish traps, rowing boat",
  "industrial": "cannery shed on a pier with a small crane, ice crates, tin roof",
  "future": "aquaculture station with round fish pens, glowing buoys, small drone boat"},
 "WoodcutterCamp": {
  "ancient": "woodcutter's camp: open shelter, chopping stump with axe, big log pile, sawhorse",
  "classical": "timber yard shed with a saw pit, log stacks, hand cart",
  "industrial": "lumber depot with a steam log-splitter, plank stacks, small crane",
  "future": "forestry station with a sapling nursery dome and log-cutting gantry"},
 "StoneQuarry": {
  "ancient": "open quarry: stepped cut-stone pit, timber lever crane, rough blocks, rubble",
  "classical": "terraced quarry with a treadwheel crane, dressed blocks on a sledge, masons' shelter",
  "industrial": "quarry pit with a steam derrick crane, rail cart track, crusher shed",
  "future": "quarry with crisp laser-cut terraces, block-lifter gantry, neat glowing blocks"},
 "SpiritTotem": {"ancient": "carved spirit totem pole of stacked animal faces with feathers, offerings and a ring of small stones at its base"},
 "Campfire": {
  "ancient": "gathering campfire: stone ring with glowing embers, log seats around it, cooking spit",
  "classical": "village fire plaza: stone hearth with iron brazier, benches, string of lanterns"},
 "Farm": {
  "ancient": "small farmstead: tiny barn hut beside crop rows filling the plot, wattle fence, scarecrow",
  "classical": "farmstead with barn and haystack, ploughed crop rows filling the plot, wooden fence, cart",
  "industrial": "farm with big barn, grain silo, windpump, neat crop rows filling the plot",
  "future": "farm hub with dome greenhouse, sprinkler arms, lush crop rows filling the plot"},
 "Pasture": {
  "ancient": "fenced grazing paddock filling the plot, herder's lean-to, water trough, hay pile, no animals",
  "classical": "pasture with stone-walled paddock, sheep shed, shearing bench, wool bales, no animals",
  "industrial": "dairy pasture with barn, milk churns, wire fences, windpump, no animals",
  "future": "eco-ranch with dome stable, automatic feeder, crisp fenced meadow, no animals"},
 "Granary": {
  "ancient": "raised granary on stilts with tall roof, ladder, grain baskets",
  "classical": "buttressed granary on a raised floor, grain sacks at a loading door",
  "industrial": "brick grain elevator with twin silos and conveyor chute",
  "future": "sealed silo cluster with glowing level gauges"},
 "Mine": {
  "ancient": "mine tunnel into a rocky mound, timber-propped portal, ore baskets, spoil heap",
  "classical": "mine with timber headframe and windlass over the shaft, ore cart on rails, spoil heap",
  "industrial": "mine with tall steel headframe and winding wheel, engine-house chimney, ore cars, spoil heap",
  "future": "automated mine shaft with sleek headframe, glowing lift cage, tidy spoil mound"},
 "Smelter": {
  "ancient": "clay bloomery furnace with bellows under a lean-to, charcoal pile, glowing slag",
  "classical": "smelting house with tall furnace chimney, water-driven bellows, ingot stacks",
  "industrial": "brick blast furnace with riveted chimney, glowing tapping spout, ore bins",
  "future": "induction smelter with a glowing crucible core and heat vents"},
 "Shrine": {
  "ancient": "small open shrine: offering stone under a tiny roof, candles, flower garlands",
  "classical": "small shrine with arched niche, robed god-figure statue, candles",
  "industrial": "neighbourhood shrine with wrought-iron gate, small stained-glass window, candles",
  "future": "meditation shrine with a halo ring on a pillar, soft glow"},
 "Herbalist": {
  "ancient": "herb hut with drying herb bundles under the eaves, mortar and pestle, garden bed",
  "classical": "apothecary cottage with herb garden, jars in the window, leaf-shaped hanging sign",
  "industrial": "pharmacy shop with bottles in a bay window, leaf sign, herb planters",
  "future": "wellness pod with herb terrarium dome, soft green glow"},
 "Weaver": {
  "ancient": "weaver's hut with an upright loom under the porch, dyed yarn hanging to dry",
  "classical": "weaving house with a big window loom, colourful cloth bolts on racks",
  "industrial": "textile workshop with sawtooth roof, spinning frames, bolts of cloth",
  "future": "fabric studio with a glowing loom frame and colourful fabric rolls"},
 "Palisade": {
  "ancient": "palisade wall segment of sharpened log stakes lashed together, earth bank, walkway",
  "classical": "timber palisade segment with plank walkway, iron-bound stakes, small corner platform"},
 "Blacksmith": {
  "ancient": "open-sided smithy, glowing hearth, anvil on a stump, bellows, tool rack",
  "classical": "smithy with big chimney, open work front, anvil, quench barrel, horseshoe sign",
  "industrial": "machine smithy with steam hammer, brick chimney, iron stock racks",
  "future": "fabrication forge with robotic arm and glowing plasma hearth"},
 "Temple": {
  "ancient": "temple hall on an earthen mound, carved door posts, offering bowls, steps",
  "classical": "temple with columned portico, pediment, raised steps, bronze braziers",
  "industrial": "grand temple with domed roof, round window, bell tower",
  "future": "luminous temple with crystal dome, halo ring and light pillars"},
 "Barracks": {
  "ancient": "warrior longhouse with weapon racks and training dummies out front",
  "classical": "barracks hall with parapet walk, drill yard, weapon racks, archery target",
  "industrial": "brick barracks with parade ground, flagpole, sentry box",
  "future": "armoured barracks block with rooftop landing pad and glowing beacon"},
 "Market": {
  "ancient": "open market: cluster of awning stalls with produce baskets, pottery, rugs",
  "classical": "market hall with arcaded open ground floor, stalls beneath, scales, crates",
  "industrial": "covered iron-and-glass market hall with a clock over the entrance",
  "future": "market plaza with glowing sign kiosks and stacked delivery crates"},
 "Watchtower": {
  "ancient": "tall timber watchtower on four braced legs, ladder, lookout platform, signal fire basket",
  "classical": "tall watchtower with fortified lookout top, arrow slits, pennant",
  "industrial": "brick-and-iron lookout tower with searchlight on a lattice frame"},
 "Armory": {
  "ancient": "weapon-maker's shed with racks of spears and shields, grindstone",
  "classical": "armory with barred windows, shield display, heavy doors",
  "industrial": "arsenal workshop with crates, iron shutters, small crane",
  "future": "secure armoury module with sliding blast door, glowing locker racks"},
 "GoldMine": {
  "ancient": "gold mine tunnel in a rocky outcrop, gold-flecked rocks, sluice box, glittering ore baskets",
  "classical": "gold mine with wooden headframe, guarded strongbox, gold ore carts",
  "industrial": "gold mine with steel headframe, stamp-mill shed, gilded ore cars",
  "future": "gold extractor with glowing drill tower and small ingot vault"},
 "Scriptorium": {
  "ancient": "scribes' hall with clay tablet shelves, reading porch, ink pots",
  "classical": "scriptorium with tall arched windows, writing desks, scroll racks",
  "industrial": "records office with tall windows, small printing press, brass plaque",
  "future": "archive with glowing data-scroll racks and a reading lectern"},
 "Library": {
  "classical": "library with columned entrance, tall arched windows, domed reading room, scroll niches",
  "industrial": "public library with grand stair entrance, clock, tall windows, lamp posts",
  "future": "knowledge dome with glass reading galleries and glowing book rings"},
 "Aqueduct": {
  "classical": "aqueduct segment: two tiers of stone arches carrying a water channel, open at both ends",
  "industrial": "riveted iron water pipeline on brick piers with a small pump house",
  "future": "elevated water conduit with a clear glowing channel on slender pylons"},
 "Bathhouse": {
  "classical": "bathhouse with domed hot room, steam vents, columned entrance, pool",
  "industrial": "public baths with glass roof, boiler chimney, tiled facade",
  "future": "spa with heated pools under a glass shell, steam, soft glow"},
 "Theater": {
  "classical": "semicircular open-air theater, tiered stone seats around a stage, mask carvings",
  "industrial": "theatre with ornate facade, marquee canopy, posters, lamps",
  "future": "amphitheatre with a luminous stage arch"},
 "Harbor": {
  "classical": "harbor quay: stone pier, wooden crane, small warehouse, mooring posts, cargo crates",
  "industrial": "port with steel crane, brick warehouse, small lighthouse, bollards",
  "future": "harbor hub with automated crane, glowing berth lights, cargo pods"},
 "Workshop": {
  "classical": "craftsman's workshop with open front, workbenches, lathe, goods crates",
  "industrial": "small manufactory with sawtooth roof, belt-driven machines, chimney"},
 "Forum": {
  "classical": "forum: colonnaded civic hall around a paved plaza, speaker's rostrum, statue",
  "industrial": "assembly hall with grand portico, dome and broad steps",
  "future": "civic forum of glass terraces around a glowing speaker's dais"},
 "Arena": {
  "classical": "oval arena, tiered stone seating, arched outer wall, sand floor, banners",
  "industrial": "brick and iron arena with roofed stands and flags",
  "future": "luminous oval arena with a glowing canopy ring on pylons"},
 "StoneWall": {
  "classical": "thick stone wall segment with crenellated walkway, battered base, tileable ends",
  "industrial": "reinforced bastion wall segment with brick facing and gun slits, tileable ends"},
 "Castle": {"classical": "castle: square keep with corner towers, curtain wall, gatehouse with portcullis, flags"},
 "Cathedral": {
  "classical": "cathedral with tall twin bell towers, buttresses, round window, long nave",
  "industrial": "basilica with copper dome, twin towers, clock",
  "future": "cathedral of white spires and stained-glass prisms"},
 "Monastery": {
  "classical": "monastery: cloister courtyard, chapel bell tower, herb garden",
  "industrial": "abbey with brewery wing, orchard, stone walls",
  "future": "retreat sanctuary with raked garden and glowing prayer rings"},
 "Mill": {
  "classical": "tall windmill with four broad sails on a stone tower, millstone, grain sacks",
  "industrial": "steam roller mill: tall mill building with chimney, flour sacks, hoist"},
 "Guildhall": {
  "classical": "guildhall: tall ornate hall with stepped gable, guild crest sign, merchant scales",
  "industrial": "trade exchange with columned facade, clock, brass doors",
  "future": "commerce tower with glowing crest emblem"},
 "Tavern": {
  "classical": "tavern with hanging signboard, outdoor benches, ale barrels, smoking chimney",
  "industrial": "pub with big front window, gas lamps, barrels, awning",
  "future": "lounge cafe with terrace, string lights, round windows"},
 "University": {
  "classical": "college: quadrangle hall with twin towers, arched gateway, clock",
  "industrial": "university with domed library and lecture-hall wings, ivy",
  "future": "campus spire with glass lecture halls and sky walkways"},
 "Bank": {
  "classical": "bank: solid house with columned front, barred windows, coin sign, strongroom door",
  "industrial": "imposing bank with granite columns and a round vault door",
  "future": "vault tower with gold trim and glowing ledger panels"},
 "Observatory": {
  "classical": "observatory: domed telescope tower with open slot, astrolabe on the terrace",
  "industrial": "observatory with large steel dome and big telescope",
  "future": "deep-sky observatory with radio dish and glowing star dome"},
 "Hospital": {
  "classical": "infirmary: long hall with arched windows, herb garden, stretcher cart",
  "industrial": "hospital with white facade, rows of windows, leaf-and-hand sign",
  "future": "healing pavilion with a soft light dome"},
 "Printworks": {
  "classical": "printing house with press visible, paper drying lines, stacked books",
  "industrial": "print works with rotary presses, paper rolls, chimney",
  "future": "media studio with glowing panels and paper-thin screens"},
 "Factory": {
  "industrial": "factory with sawtooth roof, tall brick smokestacks rising from the boiler house, loading yard, crates",
  "future": "clean factory with automated assembly hall, glowing conveyor, solar roof"},
 "CoalMine": {
  "industrial": "coal mine with steel headframe, winding wheel, coal tipple, black spoil heap",
  "future": "sealed coal works with sleek headframe and clean vents"},
 "Sawmill": {
  "industrial": "steam sawmill with big circular saw shed, log pond, plank stacks, chimney",
  "future": "automated timber plant with saw gantry and neat plank stacks"},
 "RailStation": {
  "industrial": "railway station with arched iron-and-glass train shed, clock tower, platforms with rails",
  "future": "maglev terminal with sweeping glass canopy over a single rail"},
 "Foundry": {
  "industrial": "foundry with blast furnaces, tall chimneys, molten glow from the casting hall, crane",
  "future": "plasma foundry with glowing core furnace and heat-sink fins"},
 "PowerPlant": {
  "industrial": "power plant: boiler hall, two cooling towers, tall chimney, transformer yard",
  "future": "power station with clean cooling towers and glowing energy coils"},
 "OilWell": {
  "industrial": "oil well: nodding pumpjack, lattice derrick, storage tanks, pipes",
  "future": "sealed synthetic fuel well with sleek pump and glowing tanks"},
 "IndustrialFarm": {
  "industrial": "industrial farm: big barn, silos, greenhouse, vast regular crop rows filling the plot",
  "future": "agri-hub with dome greenhouses, irrigation rings, crop rows filling the plot"},
 "ResearchLab": {
  "industrial": "research laboratory with flat roof, glass curtain wall, rooftop dish and antennae",
  "future": "research lab tower with glowing specimen tubes and roof dish array"},
 "Stadium": {
  "industrial": "stadium: open oval stands, four floodlight towers, marked pitch",
  "future": "domed stadium with a retractable glowing roof ring"},
 "MilitaryBase": {
  "industrial": "military base: fenced compound, hangar, barracks blocks, radar dish, watchtower",
  "future": "fortified base with shield pylons, drone hangar, landing pad"},
 "DataCenter": {"future": "data center: windowless server-hall block with cyan light strips, cooling fans, antennae"},
 "ElectronicsPlant": {"future": "electronics plant: clean-room halls, white panels, circuit-pattern roof, cyan accents"},
 "Airport": {"future": "airport: terminal with sweeping roof, control tower, runway section, boarding bridge"},
 "MedicalCenter": {"future": "medical center: tall clinic with glass atrium and rooftop drone pad"},
 "HydroponicsTower": {"future": "hydroponics tower: spiralling glass tower of green crop terraces, glowing grow lights"},
 "FusionReactor": {"future": "fusion reactor: ring-shaped containment hall around a glowing core, cooling fins"},
 "Arcology": {"future": "arcology: soaring self-contained city tower with terraces, sky gardens and glowing rings"},
}

WF = {  # wonders: per band (racial) or single (neutral)
 "DeepForge": {
  "ancient": "Deep Forge wonder: great forge hall carved into a cliff face, glowing magma channel, giant anvil, two carved guardian statues",
  "classical": "Deep Forge wonder: monumental rune-carved gate into the mountain, magma falls, massive bellows, brass chimneys",
  "industrial": "Deep Forge wonder: colossal steam forge in the mountain with brass pipes, riveted stacks, glowing magma crucible",
  "future": "Deep Forge wonder: geothermal megaforge in the mountain with glowing rune rings and brass-gold pylons"},
 "WarIdol": {
  "ancient": "War Idol wonder: towering fire-hardened timber idol with a giant stylised horned mask on piled stone, war drums, painted shields",
  "classical": "War Idol wonder: iron-banded horned idol on a stone mound, flaming braziers, huge drums, red banners",
  "industrial": "War Idol wonder: scrap-iron horned colossus idol with furnace-glow eyes, smoking braziers, drums",
  "future": "War Idol wonder: alloy horned idol with glowing red eyes and stripes, holo-banners, drums"},
 "WorldTree": {
  "ancient": "World Tree wonder: titanic ancient tree, vast leaf canopy, spiral stair round the trunk, shrines in the boughs",
  "classical": "World Tree wonder: titanic tree with silver-leaf canopy, library platforms and healing groves in the boughs",
  "industrial": "World Tree wonder: titanic tree laced with silver-glass greenhouses and vine lifts",
  "future": "World Tree wonder: luminous titanic tree with glowing leaf canopy and slender halo rings"},
 "SunPyramid": {
  "ancient": "Sun Pyramid wonder: stepped pyramid of sun-baked stone, central stairway, summit altar with giant gold sun disc",
  "classical": "Sun Pyramid wonder: grand stepped pyramid with star-chart terraces, gold sun disc, teal bands",
  "industrial": "Sun Pyramid wonder: stepped pyramid with brass telescope and sun clock on the summit",
  "future": "Sun Pyramid wonder: white-gold stepped pyramid with solar crown and glowing star-chart inlays"},
 "GrandCathedral": {
  "classical": "Grand Cathedral wonder: soaring cathedral, flying buttresses, twin bell towers, huge round stained-glass window, blue banners",
  "industrial": "Grand Cathedral wonder: cathedral with iron-and-glass nave and clock spire, blue banners",
  "future": "Grand Cathedral wonder: luminous white-gold cathedral of light with stained-glass spires"},
 "GreatLibrary": "Great Library wonder: monumental hall of knowledge with a deep colonnade, great central dome, scroll galleries, broad stairs with reading benches. Pale marble, terracotta and bronze, warm lamps",
 "Colossus": "Colossus wonder: titanic bronze statue of a robed sky-god raising a lantern-torch, striding astride a stone harbour arch on a tall plinth. Patinated bronze, pale stone",
 "GrandBazaar": "Grand Bazaar wonder: labyrinth of vaulted lamp-lit arcades under many small domes, striped silk awnings, spice sacks and carpets. Warm sandstone, terracotta, brass lamps",
 "AscensionGate": "Ascension Gate wonder: colossal standing ring of pearl-white stone and gold on a stepped circular base, braced by arching buttresses, glowing cyan light filling the ring",
}

def pick(lst, key, n, avoid=""):
    """Deterministic per-asset shuffle of the race's detail pool; skips details whose key word is already in the prompt."""
    avoid = avoid.lower()
    order = sorted(lst, key=lambda c: zlib.crc32((key + "|" + c).encode()))
    out = []
    for c in order:
        head = c.lower().split()[-1].rstrip("s")
        if head not in avoid: out.append(c)
        if len(out) == n: break
    return out

def compose(parts_fixed, details):
    """Join body + details + suffix within the Meshy limit (drops details first, then trims clauses)."""
    for k in range(len(details), -1, -1):
        body = parts_fixed
        if k: body = body + " Charming details: " + ", ".join(details[:k]) + "."
        full = body + " " + SUFFIX
        if len(full) <= MAX_PROMPT: return body, full
    body = parts_fixed
    while len(body + " " + SUFFIX) > MAX_PROMPT and ", " in body:
        body = body.rsplit(", ", 1)[0] + "."
    return body, body + " " + SUFFIX

ITEMS = []

def add(**kw):
    kw.setdefault("variant", 0)
    kw.setdefault("notes", "")
    kw.setdefault("buildingType", "")
    kw.setdefault("ages", [])
    kw.setdefault("band", "")
    kw.setdefault("footprint", None)
    kw.setdefault("targetHeight", None)
    kw.setdefault("texturePrompt", "")
    sub = kw.pop("folder")
    kw["outputPath"] = f"{MODELS}/{sub}/{kw['id']}.glb"
    neg = NEG_BASE + ", " + NEG_EXTRA[kw["category"]]
    kw["negativePrompt"] = neg
    kw["artStyle"] = "realistic"
    body, full = compose(kw.pop("body"), kw.pop("details", []))
    kw["promptCore"] = body
    kw["prompt"] = full
    assert len(full) <= MAX_PROMPT, kw["id"]
    if not kw["texturePrompt"]:
        kw["texturePrompt"] = "Hand-painted stylized texture, soft matte colors, clean readable color blocks, gentle edge highlights."
    ITEMS.append(kw)

# ---------------------------------------------------------------- priorities
P0_PROD = {"GathererHut", "HuntingLodge", "FishingHut", "WoodcutterCamp", "StoneQuarry", "Farm", "Mine", "Smelter"}

def bld_priority(t, band):
    if band == "ancient" and (t in ("TownCenter", "House") or t in P0_PROD): return "P0"
    if band in ("ancient", "classical"): return "P1"
    return "P2"

def tris_for(t, w, d):
    if t == "House": return 1800, "house"
    if t == "TownCenter": return 4500, "townhall"
    if t in ("Palisade", "StoneWall"): return 900, "kit"
    if w * d == 1: return 2500, "building"
    return 3500, "building"

NOTES = {
 "TownCenter": "Renderer stretches it per tier (h +30%, w +5% per tier); model the Camp size.",
 "House": "Most numerous; v0/v1 = Building.Variant parity.",
 "FishingHut": "Dock: sits at highest tile; piles ~0.3 down.",
 "Harbor": "Dock: sits at highest tile; quay front over water.",
 "Farm": "Fields fill the plot; buildings in back corner.",
 "Pasture": "Empty paddock; animals are separate.",
 "IndustrialFarm": "Fields fill the plot.",
 "Palisade": "3-tile segment; ends butt cleanly.",
 "StoneWall": "Tileable segment; corners/gates in Kit.",
 "Aqueduct": "Channel along Z, tiles end-to-end.",
 "Mine": "Headframe + spoil heap required.",
 "Castle": "Medieval–Renaissance only; 1 per town.",
 "GathererHut": "Dawn–Iron only.",
 "SpiritTotem": "Dawn–Iron only.",
 "Campfire": "Dawn–Classical.",
 "HuntingLodge": "Dawn–Medieval.",
 "Watchtower": "Iron–Industrial.",
 "Workshop": "Classical–Modern.",
 "Mill": "Medieval–Industrial; near rivers.",
}

for race in RACES:
    for (t, en, mn, mx, w, d, cat) in B:
        for band, (blabel, bages) in BANDS.items():
            ages = [a for a in bages if mn <= a <= mx]
            if not ages: continue
            form = F[t][band]
            variants = form if isinstance(form, list) else [form]
            for vi, fv in enumerate(variants):
                bid = f"bld_{race}_{snake(t)}_{band}_v{vi}"
                tris, kind = tris_for(t, w, d)
                subj = label(t) + (f" (variant {vi})" if len(variants) > 1 else "")
                body = (f"{RNAME[race]} {label(t).lower()}, {BAND_ERA[band]}, {shape_words(w, d)}: {fv}. "
                        f"{STYLE[race][band]}.")
                det = pick(DETAILS[race], bid, 2, avoid=body)
                note = NOTES.get(t, "")
                add(id=bid, category="Buildings", folder=f"Buildings/{RNAME[race]}", race=race, subject=subj,
                    buildingType=t, band=band, ages=[AGES[a] for a in ages], footprint=[w, d], targetPolycount=tris,
                    kind=kind, priority=bld_priority(t, band), body=body, details=det, variant=vi, notes=note,
                    texturePrompt=f"{PALETTE[race]}; {BAND_MAT[band]}. Hand-painted stylized texture, matte, clean color blocks.")

for (t, en, mn, mx, w, d, race) in WONDERS:
    if race == "neutral":
        ages = list(range(mn, mx + 1))
        wid = f"bld_neutral_{snake(t)}_v0"
        pr = "P1" if mn <= 5 else "P2"
        add(id=wid, category="Wonders", folder="Wonders", race="neutral", subject=label(t), buildingType=t, band="all",
            ages=[AGES[a] for a in ages], footprint=[w, d], targetPolycount=9000, kind="wonder", priority=pr,
            body=f"{WF[t]}. {shape_words(w, d).capitalize()}, monumental and awe-inspiring.",
            details=["tiny pilgrims' offering bowls", "pennants", "lamps"][:2],
            notes="Race-neutral; one model for every age it exists in. Banner pole tinted with civ colour by the renderer."
                  + (" Coastal: harbour arch front (−Z) over water." if t == "Colossus" else "")
                  + (" Endgame: emissive material for the ring light." if t == "AscensionGate" else ""),
            texturePrompt=f"{PALETTE['neutral']}. Hand-painted stylized texture, matte, clean color blocks.")
        continue
    for band, (blabel, bages) in BANDS.items():
        ages = [a for a in bages if mn <= a <= mx]
        if not ages: continue
        wid = f"bld_{race}_{snake(t)}_{band}_v0"
        body = f"{RNAME[race]} {WF[t][band]}, {shape_words(w, d)}. {STYLE[race][band]}."
        add(id=wid, category="Wonders", folder="Wonders", race=race, subject=label(t), buildingType=t, band=band,
            ages=[AGES[a] for a in ages], footprint=[w, d], targetPolycount=9000, kind="wonder",
            priority="P1" if band in ("ancient", "classical") else "P2", body=body, details=pick(DETAILS[race], wid, 1, avoid=body),
            notes=f"{RNAME[race]} only; 1 per civ." + (" Must sit against or into a mountain." if t == "DeepForge" else "")
                  + (" Needs a living forest nearby." if t == "WorldTree" else "")
                  + (" Never gory: stylised horned mask, no skulls." if t == "WarIdol" else ""),
            texturePrompt=f"{PALETTE[race]}; {BAND_MAT[band]}. Hand-painted stylized texture, matte, clean color blocks.")

# ---------------------------------------------------------------- units
LOOK = {
 "human": "Human villager, average build, warm tan skin, undyed linen tunic with a blue sash, leather belt and boots, friendly face",
 "elf": "Elf villager, tall and slender, pale ivory skin, long pointed ears, flowing leaf-green robe with moon-silver clasp, braided hair, serene face",
 "dwarf": "Dwarf villager, short and very stout, ruddy skin, big braided beard with brass beads, leather apron over a wool tunic, heavy boots",
 "orc": "Orc villager, broad and muscular, olive-green skin, small lower tusks, hide vest and kilt with red stripe paint, bead necklace, cheerful grin",
 "saurian": "Saurian villager, upright lizard-folk with a thick tail and small head crest, teal scales, woven reed wrap, gold sun-disc pendant, turquoise beads",
}
BODY = {
 "human": "Human body, average build, warm tan skin",
 "elf": "Elf body, tall and slender, pale ivory skin, long pointed ears",
 "dwarf": "Dwarf body, short and very stout, ruddy skin, big braided beard",
 "orc": "Orc body, broad and muscular, olive-green skin, small lower tusks",
 "saurian": "Saurian lizard-folk body, thick tail, small head crest, teal scales",
}
CHILD = {
 "human": "Human child, warm tan skin, oversized head, short limbs, short linen tunic with blue sash, playful stance",
 "elf": "Elf child, pale ivory skin, long pointed ears, oversized head, little leaf-green smock, playful stance",
 "dwarf": "Dwarf child, very stout, ruddy cheeks, beardless with two thick braids, oversized head, tiny leather apron, playful stance",
 "orc": "Orc child, olive-green skin, tiny tusk nubs, oversized head, little hide vest with a red stripe, playful stance",
 "saurian": "Saurian hatchling-child, teal scales, oversized head, stubby tail, tiny crest, small reed wrap, playful stance",
}
ELDER = {
 "human": "Human elder, warm tan skin, grey hair and beard, slightly stooped, walking stick, linen tunic, blue shawl",
 "elf": "Elf elder, ageless pale face, long silver-white hair, pointed ears, flowing green robe, tall slender staff",
 "dwarf": "Dwarf elder, very stout, ruddy skin, enormous white braided beard with brass beads, slightly stooped, walking stick",
 "orc": "Orc elder, grey-green skin, long tusks, slightly stooped, hide mantle with red stripes, bone-bead necklace, walking staff",
 "saurian": "Saurian elder lizard-folk, faded grey-teal scales, tall crest, slightly stooped, long reed robe, gold sun-disc pendant, walking stick",
}
STAGE = {
 "adult": ("adult", "standing neutral pose, arms slightly away from the body", "P0", "Base mesh for all jobs; job props attach to the right-hand socket."),
 "child": ("child", "child version: oversized head, short limbs, playful stance, simpler clothes", "P0", "Renderer still applies 0.6 scale for children; author at normalised height 1.0."),
 "elder": ("elder", "elder version: slightly stooped, walking stick, greyed hair or faded scales, shawl", "P0", "Renderer applies 0.95 scale."),
}
for race in RACES:
    for st, (nm, desc, pr, note) in STAGE.items():
        uid = f"unit_{race}_villager_{st}"
        look = {"adult": f"{LOOK[race]}, {desc}", "child": CHILD[race], "elder": ELDER[race]}[st]
        add(id=uid, category="Units", folder=f"Units/{RNAME[race]}", race=race, subject=f"Villager ({nm})", kind="unit",
            targetPolycount=700, priority=pr, targetHeight=1.0,
            body=f"{look}. Neutral standing pose, single full-body character, chunky toy-like proportions, feet flat on the ground.",
            notes="Normalised: adult height = 1.0 unit (renderer scales to 0.30 tile). " + note,
            texturePrompt=f"{PALETTE[race]}. Hand-painted stylized character texture, matte, large clean color areas.")

SOLDIER = {
 "human": {
  "ancient": "Human spearman: leather cap, quilted jerkin, round wooden shield painted blue, long spear",
  "classical": "Human man-at-arms: rounded steel helm, mail under a blue tabard, kite shield, halberd",
  "industrial": "Human rifleman: blue greatcoat, peaked cap, leather webbing, long rifle held upright",
  "future": "Human trooper: sleek white-and-blue armour, light visor, compact energy rifle",
 },
 "elf": {
  "ancient": "Elf archer: leaf-layered cloak, bark-plate vest, tall curved longbow, quiver",
  "classical": "Elf warden: moon-silver leaf-scale armour, crescent-crested helm, long glaive",
  "industrial": "Elf ranger: long green-silver coat, brass goggles, slender long rifle",
  "future": "Elf sentinel: flowing moon-silver organic armour, glowing light-bow",
 },
 "dwarf": {
  "ancient": "Dwarf axe-bearer: bronze cap, leather and studs, iron-rimmed round shield, short axe, braided beard",
  "classical": "Dwarf shield-wall warrior: heavy plate with rune bands, great hammer, tower shield",
  "industrial": "Dwarf grenadier: brass-plated armour, goggles, stubby hand-cannon, bandolier",
  "future": "Dwarf exo-warrior: chunky powered armour with glowing amber runes, hammer-drill",
 },
 "orc": {
  "ancient": "Orc warrior: hide armour, stone-headed club, painted hide shield with red stripes, horned headband",
  "classical": "Orc raider: iron-banded leather, horned helmet, spiked pauldrons, big cleaver-axe",
  "industrial": "Orc brute: riveted scrap armour, red war-paint stripes, heavy scatter-gun, horned helm",
  "future": "Orc berserker: heavy dark alloy plates with glowing red stripes, power maul, horned helm",
 },
 "saurian": {
  "ancient": "Saurian spear-thrower: quilted reed armour, feather crest, round shield with gold sun disc, spear and thrower",
  "classical": "Saurian temple guard: bronze scale armour, crested helm, sun-disc glaive, turquoise inlays",
  "industrial": "Saurian marsh rifleman: wide sun hat, teal long coat, brass rifle",
  "future": "Saurian sun guard: white-gold armour, sun-disc emitter shield, glowing spear",
 },
}
for race in RACES:
    for band in BANDS:
        uid = f"unit_{race}_soldier_{band}"
        add(id=uid, category="Units", folder=f"Units/{RNAME[race]}", race=race, subject="Soldier", band=band,
            ages=[AGES[a] for a in BANDS[band][1]], kind="unit", targetPolycount=700,
            priority="P1" if band in ("ancient", "classical") else "P2", targetHeight=1.07,
            body=f"{SOLDIER[race][band]}. {BODY[race]}. Proud ready stance, weapon upright, single full-body character, chunky toy-like proportions.",
            notes="Normalised to adult = 1.0 (renderer SoldierHeight 0.32 tile). Keep tunic/shield areas light so the civ tint reads.",
            texturePrompt=f"{PALETTE[race]}; {BAND_MAT[band]}. Hand-painted stylized character texture, matte.")
for race in RACES:
    add(id=f"unit_{race}_hero", category="Units", folder=f"Units/{RNAME[race]}", race=race, subject="Hero / Prophet", kind="unit",
        targetPolycount=700, priority="P2", targetHeight=1.1,
        body=f"{RNAME[race]} prophet-hero ({BODY[race].split(', ', 1)[1]}): tall ceremonial robes, ornate staff topped with a glowing emblem, flowing mantle, "
             f"{ {'human':'blue and gold','elf':'green and moon-silver','dwarf':'amber and brass','orc':'red and bone','saurian':'purple and gold'}[race]} colours, noble pose. Single full-body character.",
        notes="Spawned by the Prophet power and by battles/wonders; ~10% taller than a villager.",
        texturePrompt=f"{PALETTE[race]}. Hand-painted stylized character texture, matte.")

JOBS = [  # job, prop description, priority
 ("gatherer", "woven forager's basket full of berries and roots", "P0"),
 ("hunter", "short hunting bow with a small quiver of arrows", "P0"),
 ("fisher", "fishing rod with a hanging fish and a coiled net", "P0"),
 ("farmer", "long-handled hoe with a small sack of seed", "P0"),
 ("herder", "curved wooden shepherd's crook", "P1"),
 ("woodcutter", "chunky felling axe with a wooden haft and iron head", "P0"),
 ("quarrier", "stone mason's pick and chisel", "P0"),
 ("miner", "mining pick with a small hanging lantern", "P0"),
 ("smith", "blacksmith hammer and tongs", "P0"),
 ("craftsman", "wooden mallet and hand saw", "P1"),
 ("weaver", "drop spindle and a bolt of coloured cloth", "P1"),
 ("builder", "builder's hammer and a plank over the shoulder", "P0"),
 ("merchant", "balance scales and a bulging coin pouch", "P1"),
 ("priest", "ceremonial staff with a sun-and-star emblem and a censer", "P1"),
 ("scholar", "open book and rolled scroll", "P1"),
 ("healer", "herb satchel with a mortar and pestle", "P1"),
 ("entertainer", "small round lute with ribbons", "P1"),
 ("laborer", "heavy grain sack on a shoulder pad", "P1"),
 ("administrator", "ledger book and wax seal stamp", "P1"),
 ("engineer", "large wrench and rolled blueprint tube", "P2"),
]
for job, desc, pr in JOBS:
    add(id=f"prop_job_{job}_v0", category="Props", folder="Props/Jobs", race="neutral", subject=f"Job prop: {job}", kind="prop",
        targetPolycount=300, priority=pr, targetHeight=0.5,
        body=f"Hand-held prop for a {job}: {desc}. Slightly oversized, chunky and simple, isolated object.",
        notes=f"JobType.{job.capitalize()}; attaches to the villager's right hand. Authored in villager space (adult = 1.0).")

# ---------------------------------------------------------------- nature
NAT = [
 # id, group, subject, prompt body, tris, priority, height, notes
 ("nat_tree_oak_v0", "Temperate", "Oak tree", "Broad round-crowned oak tree, thick gnarled trunk, chunky clustered leaf clumps, a few acorns", 700, "P0", 1.1, "Grassland/Forest/Hills. Replaces TreeKind.Deciduous."),
 ("nat_tree_oak_v1", "Temperate", "Oak tree (tall)", "Tall oak tree with a slightly leaning trunk and three stacked leafy cloud clumps", 700, "P0", 1.2, "Variant for tile variety."),
 ("nat_tree_birch_v0", "Temperate", "Birch tree", "Slender white birch tree with black bark marks and a light airy yellow-green crown", 600, "P1", 1.1, ""),
 ("nat_tree_fruit_v0", "Temperate", "Fruit tree", "Small round fruit tree with red fruit dots and a short sturdy trunk", 600, "P1", 0.8, "Orchards near farms."),
 ("nat_bush_temperate_v0", "Temperate", "Bush", "Rounded leafy bush with a few tiny white flowers", 300, "P1", 0.35, ""),
 ("nat_plant_flowers_v0", "Temperate", "Meadow flowers", "Clump of meadow flowers, chunky petals in yellow, white and violet, grass tufts", 300, "P1", 0.2, "Ground dressing."),
 ("nat_plant_grass_v0", "Temperate", "Grass tuft", "Chunky tuft of tall grass blades", 150, "P2", 0.15, "Ground dressing."),
 ("nat_tree_sapling_v0", "Temperate", "Sapling", "Young sapling with a thin trunk, a stake and a few leaves", 200, "P1", 0.4, "Plant Forest power / regrowth stage."),
 ("nat_tree_spruce_v0", "Conifer", "Spruce tree", "Classic spruce tree of three stacked chunky cone tiers, dark green, short brown trunk", 500, "P0", 1.2, "Taiga/Snow/Mountain. Replaces TreeKind.Conifer."),
 ("nat_tree_spruce_v1", "Conifer", "Spruce tree (slim)", "Slim tall spruce tree of four narrow cone tiers, slight lean", 500, "P0", 1.3, ""),
 ("nat_tree_spruce_snow_v0", "Conifer", "Snowy spruce", "Spruce tree of stacked cone tiers with thick snow caps on every tier", 600, "P1", 1.2, "Snow/Tundra."),
 ("nat_tree_pine_v0", "Conifer", "Pine tree", "Tall pine tree with a bare orange-brown trunk and a flat-topped cloud of needles", 600, "P1", 1.3, "Hills/Taiga."),
 ("nat_tree_dead_v0", "Conifer", "Dead snag", "Bare weathered dead tree with twisted grey branches", 400, "P1", 1.0, "Tundra; drought."),
 ("nat_bush_juniper_v0", "Conifer", "Juniper shrub", "Low spreading juniper shrub with blue berries", 300, "P2", 0.3, ""),
 ("nat_tree_palm_v0", "Tropical", "Palm tree", "Curved coconut palm with ringed trunk and big drooping fronds, coconut cluster", 600, "P0", 1.0, "Beach/Jungle. Replaces TreeKind.Palm."),
 ("nat_tree_palm_v1", "Tropical", "Palm tree (double)", "Pair of palms leaning apart from one base, big fronds", 700, "P1", 1.1, ""),
 ("nat_tree_jungle_v0", "Tropical", "Jungle tree", "Tall jungle tree with buttress roots, hanging vines and a broad flat layered canopy", 700, "P1", 1.4, "Jungle."),
 ("nat_tree_jungle_v1", "Tropical", "Jungle tree (bloom)", "Jungle tree with a round canopy dotted with big pink flowers and vines", 700, "P2", 1.2, ""),
 ("nat_plant_fern_v0", "Tropical", "Fern", "Large fern clump with big curling fronds", 300, "P1", 0.3, ""),
 ("nat_plant_bigleaf_v0", "Tropical", "Giant-leaf plant", "Clump of giant paddle-shaped leaves on short stalks", 300, "P2", 0.4, ""),
 ("nat_tree_acacia_v0", "Arid", "Acacia tree", "Flat-topped savanna acacia tree with a wide umbrella canopy and twisting trunk", 600, "P0", 0.9, "Savanna."),
 ("nat_tree_acacia_v1", "Arid", "Acacia tree (wide)", "Wide two-layer umbrella acacia with a forked trunk", 600, "P1", 1.0, ""),
 ("nat_tree_bottle_v0", "Arid", "Bottle tree", "Fat bottle-shaped trunk tree with a small tuft of branches on top", 500, "P2", 1.0, "Savanna landmark."),
 ("nat_plant_cactus_v0", "Arid", "Cactus", "Chunky column cactus with two raised arms and a pink flower", 300, "P1", 0.6, "Desert."),
 ("nat_plant_cactus_v1", "Arid", "Cactus cluster", "Cluster of round barrel cacti and paddle cacti", 300, "P2", 0.3, "Desert."),
 ("nat_bush_dry_v0", "Arid", "Dry scrub", "Dry tumble-scrub bush with sparse olive leaves", 250, "P1", 0.3, "Desert/Savanna."),
 ("nat_tree_mangrove_v0", "Swamp", "Mangrove tree", "Mangrove tree on tall arching stilt roots with a dense rounded canopy", 700, "P0", 1.0, "Swamp; roots should reach y = -0.2 into water."),
 ("nat_tree_willow_v0", "Swamp", "Swamp willow", "Drooping swamp willow with long hanging curtain branches", 700, "P1", 1.1, ""),
 ("nat_tree_cypress_v0", "Swamp", "Swamp cypress", "Swamp cypress with flared trunk, knobbly root knees and hanging moss", 600, "P2", 1.2, ""),
 ("nat_plant_reeds_v0", "Swamp", "Reeds", "Clump of tall reeds and cattails", 250, "P1", 0.4, "Swamp/Lake shore."),
 ("nat_plant_lilypads_v0", "Swamp", "Lily pads", "Floating lily pads with two white blossoms, flat on the water", 200, "P2", 0.05, "Water surface."),
 ("nat_tree_charred_v0", "Scorched", "Charred tree", "Burnt black tree trunk with a few charred branches and glowing ember cracks", 400, "P1", 0.9, "Scorched / Burning tiles, Volcanic."),
 ("nat_rock_small_v0", "Rocks", "Rock (small)", "Cluster of three chunky faceted grey rocks", 300, "P0", 0.25, ""),
 ("nat_rock_small_v1", "Rocks", "Rock (small, flat)", "Flat slab rocks stacked casually with a moss patch", 300, "P1", 0.2, ""),
 ("nat_rock_large_v0", "Rocks", "Rock (large)", "Large angular boulder outcrop with layered strata", 500, "P0", 0.6, ""),
 ("nat_rock_boulder_v0", "Rocks", "Mossy boulder", "Rounded mossy boulder with a little fern at its foot", 400, "P0", 0.45, ""),
 ("nat_rock_sandstone_v0", "Rocks", "Sandstone rock", "Wind-carved orange sandstone rock with smooth banded layers", 400, "P1", 0.5, "Desert."),
 ("nat_rock_snow_v0", "Rocks", "Snowy rock", "Grey rock outcrop capped with thick snow", 400, "P1", 0.5, "Snow/Tundra/Mountain."),
 ("nat_rock_basalt_v0", "Rocks", "Basalt rock", "Black hexagonal basalt columns with a faint red glow in the cracks", 400, "P1", 0.5, "Volcanic."),
 ("nat_rock_beach_v0", "Rocks", "Shore pebbles", "Smooth rounded shore pebbles and a small driftwood log", 300, "P2", 0.15, "Beach."),
 ("nat_rock_cliff_v0", "Rocks", "Cliff chunk", "Tall chunky cliff rock chunk with ledges, tileable sides", 700, "P2", 1.5, "Mountain dressing."),
 ("nat_crop_tilled_v0", "Crops", "Tilled field", "Square tile of freshly tilled brown soil furrows", 300, "P1", 0.05, "Farmland tile (currently baked into terrain colours)."),
 ("nat_crop_wheat_young_v0", "Crops", "Young grain", "Square tile of neat rows of young green grain sprouts", 500, "P1", 0.12, "Spring/early growth."),
 ("nat_crop_wheat_ripe_v0", "Crops", "Ripe grain", "Square tile of tall golden ripe grain rows with chunky ears", 600, "P1", 0.2, "Summer/harvest."),
 ("nat_crop_vegetables_v0", "Crops", "Vegetable rows", "Square tile of vegetable rows: round cabbages, leafy greens, orange root tops", 600, "P1", 0.1, ""),
 ("nat_crop_rice_v0", "Crops", "Rice paddy", "Square flooded rice paddy tile with low mud walls and green shoots in water", 500, "P2", 0.1, "Saurian/Swamp farms."),
 ("nat_crop_vineyard_v0", "Crops", "Vineyard rows", "Square tile of trellised grape vine rows with purple grape clusters", 600, "P2", 0.25, ""),
 ("nat_node_stone_v0", "Resources", "Stone deposit", "Pale grey stone outcrop with cleanly fractured blocky faces", 500, "P1", 0.45, "TileFlags.StoneDeposit."),
 ("nat_node_stone_v1", "Resources", "Stone deposit (flat)", "Low exposed bedrock shelf with cracked square slabs", 400, "P2", 0.25, ""),
 ("nat_node_ore_v0", "Resources", "Ore deposit", "Dark rock outcrop with rusty red-brown and copper-green ore veins", 500, "P1", 0.45, "TileFlags.OreDeposit; also RevealOre power."),
 ("nat_node_ore_v1", "Resources", "Ore deposit (crystal)", "Rock mound with chunky metallic ore crystals poking out", 500, "P2", 0.4, ""),
 ("nat_node_gold_v0", "Resources", "Gold deposit", "Quartz-white rock outcrop with bright glittering gold veins and nuggets", 500, "P1", 0.4, "TileFlags.GoldDeposit."),
 ("nat_node_coal_v0", "Resources", "Coal seam", "Black glossy coal seam layered in brown rock, coal lumps at its foot", 500, "P2", 0.4, "TileFlags.CoalDeposit (Industrial+)."),
 ("nat_node_oil_v0", "Resources", "Oil seep", "Dark glossy oil seep pool in cracked earth with a few bubbles and stained rocks", 400, "P2", 0.1, "TileFlags.OilDeposit (Modern+)."),
]
for (nid, grp, subj, body, tris, pr, h, note) in NAT:
    add(id=nid, category="Nature", folder=f"Nature/{grp}", race="neutral", subject=f"{grp}: {subj}", kind="nature",
        targetPolycount=tris, priority=pr, targetHeight=h, footprint=[1, 1],
        body=f"{body}. Single isolated nature asset, fits within one 1x1 m tile.", notes=note)

# ---------------------------------------------------------------- animals
ANI = [
 ("ani_sheep_v0", "Sheep", "Fluffy round sheep with a cloud-like cream fleece, dark face and legs", "P1", 0.14, "Pasture."),
 ("ani_cow_v0", "Cow", "Sturdy cow with brown-and-white patches, small horns, bell on a collar", "P1", 0.18, "Pasture."),
 ("ani_goat_v0", "Goat", "Shaggy goat with curled horns and a little beard", "P1", 0.14, "Pasture, hills."),
 ("ani_horse_v0", "Horse", "Stocky draft horse with feathered hooves and a thick mane", "P1", 0.25, "Carts, cavalry later."),
 ("ani_deer_v0", "Deer", "Graceful deer with small antlers and white tail", "P1", 0.2, "Forest wildlife; Hunting Lodge."),
 ("ani_pig_v0", "Pig", "Round pink pig with a curly tail and floppy ears", "P2", 0.1, "Farms."),
 ("ani_chicken_v0", "Chicken", "Plump chicken with a red comb", "P2", 0.06, "Farms."),
 ("ani_boar_v0", "Boar", "Bristly wild boar with small tusks", "P2", 0.12, "Forest/Savanna wildlife."),
 ("ani_wolf_v0", "Wolf", "Grey wolf standing alert, bushy tail", "P2", 0.14, "Taiga wildlife."),
 ("ani_bear_v0", "Bear", "Big brown bear walking on four legs", "P2", 0.2, "Forest/Taiga wildlife."),
 ("ani_rabbit_v0", "Rabbit", "Small brown rabbit sitting up", "P2", 0.05, "Grassland."),
 ("ani_antelope_v0", "Antelope", "Slender savanna antelope with long ringed horns", "P2", 0.18, "Savanna herds."),
 ("ani_camel_v0", "Camel", "Two-humped desert camel with a calm face", "P2", 0.28, "Desert caravans."),
 ("ani_crocodile_v0", "Swamp lurker", "Long low crocodile-like swamp reptile half in water, knobbly back", "P2", 0.06, "Swamp."),
 ("ani_whale_v0", "Whale", "Large friendly whale surfacing with a water spout hole", "P2", 0.3, "Ocean ambience."),
]
for (aid, subj, body, pr, h, note) in ANI:
    add(id=aid, category="Animals", folder="Animals", race="neutral", subject=subj, kind="animal", targetPolycount=500,
        priority=pr, targetHeight=h, body=f"{body}, standing in a calm natural pose. Single animal, cute chunky proportions.",
        notes=f"Not rendered yet (no animal renderer). World height ≈ {h} tile next to a 0.30-tile person. {note}")

# ---------------------------------------------------------------- vehicles & boats per band
VEH = {
 "ancient": [
  ("handcart", "Two-wheeled wooden hand cart loaded with sacks and a jug", 0.2, ""),
  ("oxcart", "Sturdy four-wheeled wooden ox cart with solid wheels and a yoke, empty shafts", 0.25, "Pairs with ani_cow_v0."),
  ("ram", "Siege battering ram: log ram hung under a hide-covered timber shed on wheels", 0.3, "Sieges."),
  ("boat_fishing", "Dugout fishing canoe with a paddle and a small net", 0.1, "Naval is post-1.0; fishing ambience first."),
  ("boat_cargo", "Reed-bundle trading boat with a square sail and cargo baskets", 0.35, ""),
  ("boat_war", "Long war canoe with a carved prow, shields along the sides and a single sail", 0.3, ""),
 ],
 "classical": [
  ("wagon", "Covered wagon with canvas hood, spoked wheels and barrels", 0.3, ""),
  ("trebuchet", "Timber counterweight trebuchet on a wheeled frame", 0.6, "Sieges."),
  ("cannon", "Bronze cannon on a two-wheeled wooden carriage", 0.2, "Renaissance gunpowder."),
  ("boat_fishing", "Small sailing fishing boat with a lateen sail and lobster pots", 0.35, ""),
  ("boat_cargo", "Round-bellied single-mast cargo ship with a big square sail and castles fore and aft", 0.8, "Sail is civ-tint area."),
  ("boat_war", "Three-mast warship with rows of gun ports and flags", 1.0, ""),
 ],
 "industrial": [
  ("locomotive", "Steam locomotive with a tall smokestack, big drive wheels and a coal tender", 0.35, "Rail Station."),
  ("railcar", "Boxy wooden freight rail wagon on iron wheels", 0.3, ""),
  ("truck", "Chunky early motor truck with a canvas-covered bed", 0.25, ""),
  ("car", "Rounded vintage family motorcar", 0.18, ""),
  ("tractor", "Little farm tractor with big rear wheels", 0.2, "Industrial Farm."),
  ("tank", "Chunky boxy armoured tank with a short turret gun", 0.25, "Military Base; no real-world insignia."),
  ("boat_fishing", "Steam fishing trawler with a funnel and net booms", 0.4, ""),
  ("boat_cargo", "Steam cargo ship with two funnels and deck cranes", 1.0, ""),
  ("boat_war", "Ironclad warship with a turret and a single funnel", 1.0, ""),
 ],
 "future": [
  ("maglev", "Sleek streamlined maglev train car with a glowing cyan stripe", 0.3, ""),
  ("hovercar", "Rounded hover car with glowing underside pads", 0.15, ""),
  ("hovertruck", "Boxy hover cargo truck with glowing pads", 0.25, ""),
  ("aircraft", "Stylized airliner with swept wings and rounded nose, white with a cyan stripe", 0.4, "Airport."),
  ("drone", "Quad-rotor delivery drone carrying a small crate", 0.08, ""),
  ("hovertank", "Low wedge-shaped hover tank with a smooth turret and glowing pads", 0.25, ""),
  ("boat_fishing", "Small electric fishing boat with solar canopy", 0.35, ""),
  ("boat_cargo", "Sleek container ship with solar sails", 1.2, ""),
  ("boat_war", "Angular stealth patrol ship with a glowing sensor mast", 1.0, ""),
 ],
}
VEH_MAT = {"ancient": "Raw timber, hide, rope and reed", "classical": "Oak planks, canvas, iron fittings and bronze",
           "industrial": "Riveted iron, steel, brass and red-and-black paint", "future": "White composite panels, glass and soft cyan glow"}
for band, lst in VEH.items():
    for (v, body, h, note) in lst:
        vid = f"veh_{v}_{band}_v0"
        add(id=vid, category="Vehicles", folder="Vehicles", race="neutral", subject=v.replace("_", " ").capitalize(), band=band,
            ages=[AGES[a] for a in BANDS[band][1]], kind="vehicle", targetPolycount=1500 if "boat_cargo" in v or "boat_war" in v else 1000,
            priority="P1" if band in ("ancient", "classical") else "P2", targetHeight=h,
            body=f"{body}. {VEH_MAT[band]}. Single vehicle, toy-like chunky proportions, no crew.",
            notes="Not rendered yet (caravans/naval are roadmap). " + f"World length ≈ {h * 2:.1f} tiles max. {note}".strip())

# ---------------------------------------------------------------- town props
PROPS = [
 ("prop_crates_barrels_v0", "Crates and barrels", "Stack of wooden crates and two banded barrels", "P0", ""),
 ("prop_firewood_stack_v0", "Firewood stack", "Neat stack of split firewood logs with a chopping stump and axe", "P0", ""),
 ("prop_haystack_v0", "Haystack", "Rounded golden haystack with a pitchfork leaning on it", "P0", ""),
 ("prop_market_stall_v0", "Market stall", "Wooden market stall with a striped awning and baskets of produce", "P1", "Market dressing."),
 ("prop_market_stall_v1", "Market stall (cloth)", "Market stall with draped colourful cloth bolts and pottery", "P1", ""),
 ("prop_fence_wattle_v0", "Wattle fence", "One-tile woven wattle fence segment with posts, tileable", "P1", ""),
 ("prop_fence_stone_v0", "Dry-stone wall", "One-tile low dry-stone field wall, tileable", "P1", ""),
 ("prop_scarecrow_v0", "Scarecrow", "Friendly scarecrow with a straw hat and patched coat", "P1", "Farms."),
 ("prop_laundry_line_v0", "Laundry line", "Laundry line between two posts with colourful shirts and sheets", "P1", ""),
 ("prop_signpost_v0", "Signpost", "Crooked wooden signpost with three blank arrow boards", "P1", "No text."),
 ("prop_cart_parked_v0", "Wheelbarrow", "Wooden wheelbarrow with a few stones", "P1", ""),
 ("prop_beehives_v0", "Beehives", "Two woven straw beehives on a little bench", "P2", ""),
 ("prop_bench_v0", "Bench", "Sturdy wooden bench with carved ends", "P2", ""),
 ("prop_fountain_v0", "Fountain", "Round stone fountain with a little spout statue", "P2", "Classical+ plazas."),
 ("prop_statue_small_v0", "Small statue", "Cat-sized stone statue of a sitting cat on a plinth", "P2", "Style-guide charm detail."),
 ("prop_planter_flowers_v0", "Flower planter", "Wooden planter box overflowing with flowers", "P2", ""),
 ("prop_training_dummy_v0", "Training dummy", "Straw training dummy on a post with a wooden shield", "P2", "Barracks dressing."),
 ("prop_cook_pot_v0", "Cooking pot", "Iron cooking pot on a tripod over a small fire", "P2", ""),
 ("prop_streetlight_ancient_v0", "Torch post", "Wooden post with a burning torch basket", "P1", "Emissive flame area."),
 ("prop_streetlight_classical_v0", "Lantern post", "Iron lantern on a wooden post", "P1", ""),
 ("prop_streetlight_industrial_v0", "Gas lamp", "Cast-iron gas street lamp", "P2", ""),
 ("prop_streetlight_future_v0", "Light pole", "Slender white light pole with a glowing cyan ring", "P2", ""),
]
for (pid, subj, body, pr, note) in PROPS:
    add(id=pid, category="Props", folder="Props/Town", race="neutral", subject=subj, kind="prop", targetPolycount=500,
        priority=pr, footprint=[1, 1], body=f"{body}. Single small isolated prop.", notes=note)
RACE_PROPS = {
 "human": ("Banner", "Tall wooden banner pole with a royal-blue cloth banner and a brass finial", "Flower-box cart",
           "Small wooden hand cart turned into a flower planter, blue painted"),
 "elf": ("Banner", "Curved living-wood banner pole with a leaf-green silk banner and a moon-silver crescent finial", "Lantern pod tree",
         "Little curved post hung with three glowing seed-pod lanterns"),
 "dwarf": ("Banner", "Squat stone-footed banner pole with an amber banner and brass anvil finial", "Mine cart",
           "Small brass-banded mine cart full of ore on a short rail piece"),
 "orc": ("Banner", "Lashed timber banner pole with a war-red hide banner and a stylised horned finial", "War drum",
         "Big hide war drum on a timber stand with red stripes"),
 "saurian": ("Banner", "Bamboo banner pole with a purple banner and a gold sun-disc finial", "Sundial",
             "Stepped stone sundial with a gold sun disc and turquoise inlays"),
}
for race, (s1, b1, s2, b2) in RACE_PROPS.items():
    add(id=f"prop_{race}_banner_v0", category="Props", folder="Props/Town", race=race, subject=s1, kind="prop",
        targetPolycount=300, priority="P0", targetHeight=1.0, body=f"{b1}. Single isolated prop.",
        notes="Used on buildings (BuildingRenderer banner) and armies (ArmyRenderer). Keep the cloth light/neutral if civ-tinted.")
    add(id=f"prop_{race}_signature_v0", category="Props", folder="Props/Town", race=race, subject=s2, kind="prop",
        targetPolycount=500, priority="P1", footprint=[1, 1], body=f"{b2}. Single small isolated prop.", notes="Race signature street dressing.")

# ---------------------------------------------------------------- kit
BRIDGE = {
 "ancient": "Rope-and-plank footbridge on log trestles",
 "classical": "Single-arch stone bridge with low parapets",
 "industrial": "Riveted iron truss bridge on brick abutments",
 "future": "Slender white-and-glass bridge with glowing edge lights",
}
ROAD = {"ancient": "packed dirt path with stones", "classical": "cobblestone road with kerbs",
        "industrial": "asphalt road with stone kerbs", "future": "smooth pale paving with glowing seams"}
for band in BANDS:
    pr = "P1" if band in ("ancient", "classical") else "P2"
    add(id=f"kit_bridge_{band}_v0", category="Kit", folder="Kit", race="neutral", subject="Bridge (1 tile)", band=band,
        ages=[AGES[a] for a in BANDS[band][1]], kind="kit", targetPolycount=900, priority=pr, footprint=[1, 1],
        body=f"{BRIDGE[band]}, one tile long, deck runs front to back, ends flush for tiling.",
        notes="River+Road tile (currently drawn as BridgeColor in TerrainMeshBuilder). Deck at y≈0.05 above banks.")
    for piece in ("straight", "corner", "cross"):
        add(id=f"kit_road_{band}_{piece}_v0", category="Kit", folder="Kit", race="neutral", subject=f"Road ({piece})", band=band,
            ages=[AGES[a] for a in BANDS[band][1]], kind="kit", targetPolycount=300, priority="P2", footprint=[1, 1],
            body=f"Flat square 1x1 road tile, {ROAD[band]}, {piece} layout, road strip 0.3 of the tile wide, very thin and flat.",
            notes="Optional overlay: roads are currently baked into terrain vertex colours (RoadWidth 0.30).")
WALL_STYLE = {
 "human": {"palisade": "Raw timber stakes on an earth bank, blue pennant on the post", "stone": "Grey fieldstone with timber hoardings, blue banners"},
 "elf": {"palisade": "Woven living saplings and thorny vines curving into a hedge-wall, moon-silver charms", "stone": "Curved pale stone grown over by roots, leaf-shaped crenels, moon-silver lanterns"},
 "dwarf": {"palisade": "Squat thick timbers on a rough stone footing, brass bands", "stone": "Massive dressed granite blocks, rune bands, brass-banded gate, buttresses"},
 "orc": {"palisade": "Rough lashed logs with tusk spikes and red war-paint stripes, horned totem on the post", "stone": "Piled boulders and timber with iron spikes, red stripes, horned emblem"},
 "saurian": {"palisade": "Bamboo and reed stakes on low stilts, gold sun disc on the post", "stone": "Stepped limestone wall with teal inlays and gold sun discs"},
}
WALLKIT = {
 "palisade": ("ancient", "palisade of sharpened lashed log stakes"),
 "stone": ("classical", "crenellated stone wall"),
}
for race in RACES:
    for mat, (band, desc) in WALLKIT.items():
        for piece, pdesc in (("corner", "corner tower post joining two wall runs at 90 degrees"),
                             ("gate", "gateway with sturdy double doors, wall runs left and right")):
            add(id=f"kit_wall_{race}_{mat}_{piece}_v0", category="Kit", folder="Kit", race=race, subject=f"{mat.capitalize()} wall {piece}",
                band=band, ages=[AGES[a] for a in BANDS[band][1]], kind="kit", targetPolycount=900, priority="P1",
                footprint=[1, 1], body=f"{RNAME[race]} {desc}: {pdesc}, one tile. {WALL_STYLE[race][mat]}.",
                notes=f"Joins {'Palisade' if mat == 'palisade' else 'StoneWall'} segments; the {'industrial' if mat == 'stone' else 'classical'} band reuses this piece.")

# ---------------------------------------------------------------- ruins / construction
RUIN = {
 "ancient": "collapsed thatch-and-timber hut remains, charred posts, fallen thatch",
 "classical": "broken stone walls, toppled column drums, fallen roof tiles",
 "industrial": "roofless red-brick shell, bent iron girders, broken chimney stump",
 "future": "luminous white ruin fragments, cracked glowing panels, half-standing arch, softly glowing",
}
SCAF = {
 "ancient": "lashed wooden pole scaffolding with rope ladders and a log pile",
 "classical": "timber plank scaffolding with a small treadwheel crane and stone blocks",
 "industrial": "steel tube scaffolding with planks and a small steam crane",
 "future": "slim white construction frame with a hovering build drone and glowing guide lines",
}
SIZES = [("small", 1), ("medium", 2), ("large", 3)]
for band in BANDS:
    pr = "P1" if band in ("ancient", "classical") else "P2"
    for sname, s in SIZES:
        add(id=f"ruin_{band}_{sname}_v0", category="Ruins", folder="Ruins", race="neutral", subject=f"Ruin ({sname})", band=band,
            ages=[AGES[a] for a in BANDS[band][1]], kind="building", targetPolycount=900 * s, priority=pr, footprint=[s, s],
            body=f"Ruined building on a {s}x{s} plot: {RUIN[band]}, rubble, grass tufts.",
            notes="Replaces ProceduralBuildingMeshes.GetRuin(w,h,StyleAge); renderer tints it charred for fire/war/divine." +
                  (" Doubles as the luminous ruins of an ascended civ." if band == "future" else ""))
    for sname, s in SIZES:
        add(id=f"con_scaffold_{band}_{sname}_v0", category="Ruins", folder="Construction", race="neutral", subject=f"Scaffolding ({s}x{s})",
            band=band, ages=[AGES[a] for a in BANDS[band][1]], kind="kit", targetPolycount=900, priority=pr, footprint=[s, s],
            body=f"Open construction site on a {s}x{s} plot: {SCAF[band]}, empty frame, no finished walls.",
            notes="Shown on incomplete buildings (ScaffoldMf). Open frame so the rising building reads through it.")
for (rid, subj, body, pr) in [
    ("ruin_rubble_stone_v0", "Stone rubble", "Low pile of broken stone blocks and dust", "P1"),
    ("ruin_rubble_timber_v0", "Timber rubble", "Pile of broken timber beams and planks", "P1"),
    ("ruin_debris_charred_v0", "Charred debris", "Pile of charred black beams with glowing embers", "P1"),
    ("con_site_materials_v0", "Building materials", "Neat pile of planks, cut stone blocks and rope coils", "P1"),
]:
    add(id=rid, category="Ruins", folder="Ruins" if rid.startswith("ruin") else "Construction", race="neutral", subject=subj,
        kind="prop", targetPolycount=500, priority=pr, footprint=[1, 1], body=f"{body}. Single small isolated pile.", notes="")

# ---------------------------------------------------------------- divine
DIV = [
 ("div_meteor_v0", "Meteor", "Craggy dark meteor rock with glowing orange cracks and a molten core", "P1", [1, 1], 500, "Falling body for the Meteor power (VFX adds trail)."),
 ("div_meteor_crater_v0", "Meteor crater", "Scorched round impact crater with raised rim, a half-buried glowing meteor at the centre", "P1", [3, 3], 900, "Scorched tiles; may become Sacred."),
 ("div_meteor_fragment_v0", "Star-metal shard", "Jagged glowing star-metal shard sticking out of scorched earth, gold flecks", "P2", [1, 1], 400, "Meteor can expose ore/gold."),
 ("div_sacred_stones_v0", "Sacred stone circle", "Ring of tall carved standing stones with a flat altar stone at the centre, faint glow", "P1", [2, 2], 900, "TileFlags.Sacred."),
 ("div_sacred_stones_v1", "Sacred monolith", "Single great carved monolith with spiral sky carvings and offerings at its base", "P2", [1, 1], 500, "Sacred."),
 ("div_sacred_spring_v0", "Sacred spring", "Small spring pool ringed with mossy stones, glowing water, ribbons on a branch", "P2", [1, 1], 500, "Sacred."),
 ("div_pilgrim_shrine_v0", "Crater shrine", "Humble pilgrim shrine of stacked stones and candles beside a scorched hollow", "P2", [1, 1], 500, "Chronicle: shrines near meteor craters."),
 ("div_fissure_v0", "Earthquake fissure", "Jagged ground fissure crack with broken raised slabs and dust", "P2", [2, 1], 700, "Earthquake aftermath."),
 ("div_lightning_glass_v0", "Lightning glass", "Scorched rock with branching glassy lightning scar formations", "P2", [1, 1], 400, "Lightning strike remnant."),
 ("div_ascended_relic_v0", "Ascended relic", "Floating-looking luminous pearl-white shard held in a gold cradle on a stepped plinth", "P2", [1, 1], 500, "Ascension legacy / luminous ruins."),
]
for (did, subj, body, pr, fp, tris, note) in DIV:
    add(id=did, category="Divine", folder="Divine", race="neutral", subject=subj, kind="prop", targetPolycount=tris, priority=pr,
        footprint=fp, body=f"{body}. Mythic, awe-inspiring but charming. Single isolated object.", notes=note)

# ---------------------------------------------------------------- output
ids = [i["id"] for i in ITEMS]
assert len(ids) == len(set(ids)), [x for x, c in Counter(ids).items() if c > 1]

FIELDS = ["id", "category", "kind", "race", "subject", "buildingType", "band", "ages", "footprint", "targetHeight",
          "targetPolycount", "priority", "prompt", "negativePrompt", "texturePrompt", "artStyle", "outputPath", "variant", "notes"]

os.makedirs(os.path.join(ROOT, "assets/meshy"), exist_ok=True)
js = {
    "schema": "godsim.meshy.assets/1",
    "generatedFrom": ["Assets/GodSim/Core/Contracts/Data/Enums.cs", "docs/CONTENT_REFERENCE.md", "docs/ART_STYLE_GUIDE.md"],
    "styleSuffix": SUFFIX,
    "meshyDefaults": {"endpoint": "https://api.meshy.ai/openapi/v2/text-to-3d", "ai_model": "latest", "topology": "triangle",
                      "should_remesh": True, "symmetry_mode": "auto", "is_a_t_pose": False, "enable_pbr": False,
                      "art_style": "realistic"},
    "count": len(ITEMS),
    "items": [{k: it[k] for k in FIELDS} for it in ITEMS],
}
with open(os.path.join(ROOT, "assets/meshy/meshy_assets.json"), "w", encoding="utf-8") as f:
    json.dump(js, f, indent=1, ensure_ascii=False)
    f.write("\n")
with open(os.path.join(ROOT, "assets/meshy/meshy_assets.csv"), "w", newline="", encoding="utf-8") as f:
    wtr = csv.writer(f)
    wtr.writerow(FIELDS)
    for it in ITEMS:
        row = []
        for k in FIELDS:
            v = it[k]
            if k == "ages": v = ";".join(v)
            elif k == "footprint": v = "" if v is None else f"{v[0]}x{v[1]}"
            elif v is None: v = ""
            row.append(v)
        wtr.writerow(row)

# summary stats for the markdown writer
with open(os.path.join(os.path.dirname(__file__), "items.json"), "w") as f:
    json.dump(ITEMS, f)
cat = Counter(i["category"] for i in ITEMS)
pri = Counter(i["priority"] for i in ITEMS)
print(len(ITEMS), dict(cat), dict(pri))
print("max prompt", max(len(i["prompt"]) for i in ITEMS), "min", min(len(i["prompt"]) for i in ITEMS))
print("details dropped:", sum(1 for i in ITEMS if i["category"] == "Buildings" and "Charming details" not in i["prompt"]))
