# GodSim — Game Design Document

| | |
|---|---|
| Owner | Lead game designer |
| Status | Living document. Reconciled against the code on disk on 2026-11-19 |
| Source of truth | Vocabulary and rules: `Assets/GodSim/Core/Contracts/`. Numbers: the catalogs (`Core/Races`, `Core/Progression/Content`, `Core/Buildings`, `Core/Divine/Powers`) and system constants. **If this document and the code disagree, the code wins, and this document gets fixed, or the code gets a ticket when it breaks a pillar.** |
| Related | [ENGINEERING_CONTRACT.md](ENGINEERING_CONTRACT.md) · [BALANCE.md](BALANCE.md) (targets, live tables, measured runs) · [CONTROLS.md](CONTROLS.md) · [UNITY_SETUP.md](UNITY_SETUP.md) · [ROADMAP.md](ROADMAP.md). Written by other owners: [ARCHITECTURE.md](ARCHITECTURE.md), [AI_DESIGN.md](AI_DESIGN.md), [AI_TUNING.md](AI_TUNING.md), [CONTENT_REFERENCE.md](CONTENT_REFERENCE.md), [ART_STYLE_GUIDE.md](ART_STYLE_GUIDE.md), [MODDING.md](MODDING.md), [SAVE_FORMAT.md](SAVE_FORMAT.md), [SECURITY.md](SECURITY.md) |

**(design target)** marks intent that the code does not implement yet. Untagged statements were
checked against code or data on disk.

---

## 1. Vision

> *You are a young god over a living world. Five peoples wake at the dawn of history and build,
> pray, trade and fight on their own. You can watch them for ten thousand days or a thousand
> years. You can make rain fall, raise mountains and call down fire. They will remember what you
> did.*

GodSim is a top-down, autonomous civilization simulator. The player never places a building or
orders a unit. Each civilization runs itself: its AI decides what to build, what to research,
where to expand and whom to fight. The player is a deity with a limited pool of mana, 19 divine
powers and a list of divine objectives. The game is about watching history happen and choosing
when to touch it.

### 1.1 Pillars

Every feature must serve at least one pillar. If a feature breaks a pillar, it is cut.

| # | Pillar | What it means | We do | We don't |
|---|---|---|---|---|
| P1 | **Watch living civilizations grow** | The world is worth watching with zero input. People are born, take jobs, marry, age and die. Towns grow one building at a time | Simulate individuals: every person has a home, a job, parents, traits and a name. Show construction progress. Surface what the AI is thinking (`Settlement.AiLog`, `BuildOrder.Reason`) | Abstract people into a population number. Let towns pop into existence |
| P2 | **Meaningful divine intervention** | A god power changes outcomes in ways the player can see and that last. Each power has a cost, a cooldown and a moral weight | Mana from believers, alignment drift, visible aftermath (craters, scorched land, forests, lakes). Mortals react through devotion, favor and religious doctrine | Offer pure-upside powers. Turn the god into a mayor through micromanagement |
| P3 | **Emergent history** | Stories come from systems colliding, not from scripts. The chronicle records them so the player can retell them | A chronicle with categories and importance. Named people and places, heroes, leaders, wars with a casus belli, collapses, doctrines, and the Ascension | Scripted campaigns, or hidden causes |
| P4 | **Readable towns that visibly evolve through ages** | From any zoom you can tell a town's race, size, age and roughly its function | Race architecture × age materials, emergent districts, a town center that upgrades by tier, and renovation that spreads through old buildings | Clutter that makes towns identical. An art set that changes with age but not race |

**Pillar test.** In a 5-minute observation clip with no player input, a new viewer should be able to:
(a) point at the biggest town, (b) say which of two towns is more advanced, (c) see a building
under construction, and (d) read a chronicle entry and find it on the map.

### 1.2 Player fantasy

- **The gardener god.** "I nursed a few elf families through a drought. A thousand years later their
  World Tree still stands at the heart of a glass metropolis."
- **The trickster god.** "I revealed a sea of ore beside the orcs. Then I watched the dwarves go to
  war over it."
- **The wrathful god.** "The humans grew arrogant, so I sent plague and meteor. Their survivors built a
  cathedral in my name, and their priests now preach of a Wrathful Judge."
- **The historian.** "I hardly touched anything. I just read the chronicle like a novel."

All four are valid ways to play. Objectives reward each style: Merciful, Wrathful, Peacekeeper,
Eternal Watch and others (§14.2). The game must never punish the historian.

### 1.3 Audience and comparables

Players of autonomous sims and god games: WorldBox, Dwarf Fortress legends mode, Black & White,
"AI-only" Civilization games, and the Songs of Syx and Banished crowd who like to watch. Sessions
run from 20 minutes to many hours. The game ships on PC with mouse and keyboard first.

---

## 2. Core loop

```
        +-------------------------------------------------------------+
        |                                                             |
        v                                                             |
  +-----------+      +-------------+      +----------------+      +-----------+
  |  OBSERVE  | ---> |  INTERVENE  | ---> |  CONSEQUENCES  | ---> | CHRONICLE |
  +-----------+      +-------------+      +----------------+      +-----------+
  zoom, follow,      spend mana on a      the sim reacts over     history records
  inspect, read      power: aim it,       days to centuries:      it, objectives
  AI log, minimap,   size the radius,     growth, famine, war,    tick, the player
  overlays           time it              devotion, doctrine      jumps to the spot
```

| Loop | Time scale | Player does | Game answers with |
|---|---|---|---|
| Micro | seconds | Arms a power and casts it | VFX, a `GodPowerCastEvent` toast, or a rejection reason (`GodCommandRejectedEvent`) |
| Meso | minutes (1–20 sim years) | Nurses a town through winter, breaks a siege, starts a famine | Tier changes, births and deaths, buildings, battles, objectives completed |
| Macro | hours (100–1,000+ sim years) | Shapes which civs dominate, ascend or collapse | Age advances, wonders, conquests, doctrines, the Ascension Gate |

**Hooks back into Observe:**

- chronicle toasts (importance ≥ 2)
- minimap pings
- objective completions
- age banners
- the AI log line for each settlement ("Build Campfire (u=0.21): entertainment coverage 0.00, pop 7")

---

## 3. World and time

- **Time.** 1 tick = 1 day. 96 ticks make a year (4 seasons × 24 days).
  - **Pacing target:** each age lasts 80–200 years, so a full arc to Ascension takes about
    1,000–1,300 years.
  - ⚠ The current data reaches Ascension in 42–79 years. See BALANCE §2.1.
- **Speeds** (`GodSimRunner.SpeedLevels`): **1, 4, 16, 64 and 240 ticks/s**, plus pause. That is
  96 s, 24 s, 6 s, 1.5 s and 0.4 s per sim year. A 1,000-year history takes about 1 h 40 m at
  16 ticks/s, or about 25 m at 64 ticks/s. The runner caps the backlog at 2 s and works inside an
  8 ms frame budget.
- **Map.** Default 192 × 192. Range 64–1024. Presets: `Continents`, `Pangaea`, `Archipelago`,
  `Highlands`.
- **Biomes (16).** DeepOcean, ShallowWater, Lake, Beach, Grassland, Savanna, Forest, Jungle, Desert,
  Swamp, Taiga, Tundra, Hills, Mountain, Snow and Volcanic.
- **Tile features.** Rivers, roads, deposits (ore, stone, gold, coal, oil), fertile soil, ruins,
  sacred sites, fire and scorch, farmland, walls and trees.
  - Deposits hold about 250 units per tile and run out.
  - Trees hold 60 wood per tile and regrow 2/yr, up to 160.
- **Movement.** Land only. Mountain 4×, Snow and Volcanic 3×, Swamp and Jungle 2.2×, Hills, Forest
  and Taiga 1.6×. Naval units are planned (ROADMAP).
- **Start.** Each race gets one civ with 14 people by default:
  - 75% adults, paired into couples, plus children
  - about one starter house per 4 founders
  - starting devotion of 0.4 + 0.4 × piety.

---

## 4. The five races

All races share the same buildings and ages. They differ in life cycle, aptitudes, AI personality,
layout, architecture, racial techs and one unique wonder. The numbers below come from
`Core/Races/*Race.cs`. The lore comes from `RaceLore`, which also gives each race its own name for
the player-god.

### 4.1 Humans: the Restless Kingdoms ("the Lantern Above")

- **Lore.** Short-lived, adaptable and endlessly fertile. Their towns began at fords and crossroads,
  bartering salt for flint. A neighbor worth trading with is a neighbor not worth killing. Other
  races call them "a candle that burns at both ends".
- **Habitat.** Grassland 1.0, Forest 0.8, Hills 0.7, Beach and Savanna 0.6.
- **Life.** Adult at 16, elder at 55, about 70 years. Fertility 0.30 per year, up to 6 children.
- **Strengths.** Trade aptitude 1.25 (the best of any race). Build speed 1.05. Gold and Goods
  ×1.15, Cloth ×1.1. Balanced everywhere else. Disease resistance 0.95 (crowded towns).
- **Personality.** Mercantile 0.8, diplomatic 0.7, expansionist 0.65, low xenophobia 0.2.
- **Architecture.** Timber-frame, gable roofs, lime plaster and red clay tile. Grid-like towns
  (road affinity 0.8, compactness 0.55, spacing 14).
- **Favored buildings.** Farm, Market, Guildhall, Cathedral, Harbor.
- **Wonder.** **Grand Cathedral** (Medieval; `classical.chivalric_orders` unlocks it early). Racial
  techs: Chivalric Orders, Great Guilds.
- **AI tendencies.** Expand along rivers and trade early. They fight wars of opportunity over borders
  and are the most common victim of orc aggression.

### 4.2 Elves: the Long Memory ("the Dreaming Root")

- **Lore.** Elves count history in the lifetimes of trees. They grew up with the forest rather than
  settling it. Every child is rare and treasured. The schism they call "the Withering" left them
  ruled by councils of many voices.
- **Habitat.** Forest 1.0, Jungle 0.8, Grassland 0.7, Taiga 0.6.
- **Life.** Adult at 40, elder at 280, about 350 years. Fertility 0.05 per year, up to 3 children.
- **Strengths.**
  - Research 1.3, faith 1.25, base health 1.2, disease resistance 1.3.
  - Wood ×1.3, Cloth ×1.2, Electronics ×1.1. Ore ×0.7.
  - They eat the least (0.04 food per tick).
- **Personality.** Curious 0.85, pious 0.75, xenophobic 0.75, peaceful (aggression 0.2), slow to
  expand (0.3).
- **Architecture.** Living wood, leaf roofs, tree platforms (`BuildsInTrees`). Sprawling, organic
  paths (compactness 0.25, roads 0.1, spacing 18).
- **Favored buildings.** Library, Temple, Herbalist, Woodcutter's Camp, Observatory.
- **Wonder.** **World Tree** (Iron; `bronze.sylvan_lore` unlocks it early). Racial techs: Sylvan
  Lore, Grove Wardens.
- **AI tendencies.** Never start wars (aggression is below every war threshold). They defend and
  expect help from the god.
- ⚠ **Balance.** Elves currently stall below the Bronze population gate and get conquered by year
  70–80 in every measured seed (BALANCE I-4).

### 4.3 Dwarves: the Deep Halls ("the Anvil-Heart")

- **Lore.** Born under stone, they measure wealth in what they have carved. Their history is a ledger
  of oaths and grudges. A clan that wrongs another builds it a hall in recompense. Their holds
  grow deep rather than wide.
- **Habitat.** Hills 1.0, Mountain 0.9, Taiga 0.7, Tundra 0.5, Volcanic 0.45. They may build on
  rugged Mountain tiles, which nobody else can (`PlacementRules.UsesRugged`).
- **Life.** Adult at 30, elder at 150, about 200 years. Fertility 0.10 per year, up to 4 children.
- **Strengths.**
  - Build speed 1.3. Military 1.15. Health 1.15.
  - Ore ×1.5, Stone and Metal ×1.4, Tools ×1.35, Weapons and Machinery ×1.2.
- **Personality.** Industrious 0.95, mercantile 0.65, xenophobic 0.55, cautious expansion 0.35.
- **Architecture.** Carved stone, flat roofs, half-sunk halls (`BuildsUnderground`). Dense terraces
  (compactness 0.85, spacing 16).
- **Favored buildings.** Mine, Stone Quarry, Smelter, Blacksmith, Stone Wall.
- **Wonder.** **Deep Forge** (Bronze): produces Metal and Tools, and adds Defense. Racial techs:
  Deep Delving (Mine in Dawn), Runesmithing.
- **AI tendencies.** Mines and smelters before markets. Walls early.
- ⚠ **Balance.** The House costs only Wood, and dwarf home sites often have no trees. Dwarves stall
  at 13–22 people (BALANCE I-5).

### 4.4 Orcs: the Red Horde ("the Thunder-Hunter")

- **Lore.** Orcs grew up on the hard edges of the world, where one drought could kill half a clan.
  They breed quickly, come of age in a dozen years and travel light. Their songs remember a Long
  Peace under the Tusk-Moot, and wonder whether there will ever be enough room for another.
- **Habitat.** Savanna 1.0, Hills 0.8, Tundra 0.7, Grassland 0.65, Desert 0.6.
- **Life.** Adult at 12, elder at 38, about 50 years. Fertility 0.45 per year, up to 8 children.
  They eat the most (0.06).
- **Strengths.**
  - Military 1.4 (the best). Health 1.25, disease resistance 1.25.
  - Weapons ×1.4.
  - Weak at research (0.75) and trade (0.7).
- **Personality.** Aggressive 0.9, expansionist 0.85, low diplomacy 0.2, mercantilism 0.25.
- **Architecture.** Hide and timber, spiked cone roofs. Sprawling camps (compactness 0.2, roads
  0.2, spacing 12, the tightest).
- **Favored buildings.** Barracks, Armory, Hunting Lodge, Watchtower, Arena.
- **Wonder.** **War Idol** (Bronze; `dawn.warbands` unlocks it in Dawn): Defense and Faith. Racial
  techs: Warbands, Blood Rites.
- **AI tendencies.**
  - The only race that wages opportunist wars (aggression ≥ 0.6).
  - They expand from smaller parents (22 people instead of 30).
  - They raze captured camps (aggression > 0.7).
  - ⚠ They currently start about 95% of all wars (BALANCE §7).

### 4.5 Saurians: the Patient Scales ("the Sun that Remembers")

- **Lore.** They hatched in the warm southern mires and build stilted villages above the flood line.
  Their priests tracked the sun on carved stone until calendars became temples, and temples became
  stepped pyramids. They don't conquer. They wait, and let fever and heat wear invaders down.
- **Habitat.** Swamp 1.0, Jungle 0.9, Beach 0.8. Cold-blooded: Snow 0, Tundra 0.02.
- **Life.**
  - Adult at 10, elder at 70, about 90 years.
  - Fertility 0.32 per year, with **clutches of 1–3**, up to 8 children.
  - ⚠ Effectively the fastest-growing race (BALANCE §2.2).
- **Strengths.** Faith 1.35 (the best). Food ×1.2 (fishing), Gold ×1.2, Stone ×1.1, Research 1.05.
- **Personality.** Pious 0.9, patient (aggression 0.35), balanced elsewhere.
- **Architecture.** Stilts (`BuildsOnStilts`) and stepped-pyramid roofs (spacing 15).
- **Favored buildings.** Fishing Hut, Shrine, Temple, Harbor, Bathhouse.
- **Wonder.** **Sun Pyramid** (Iron; `bronze.star_charts` unlocks it early). Racial techs: Star
  Charts, Clutch Rites.
- **AI tendencies.** Coastal and riverine. They produce the most faith, and so the most mana. As
  pious believers, they grow more devoted when the god is wrathful.

### 4.6 Comparison

| | Human | Elf | Dwarf | Orc | Saurian |
|---|---|---|---|---|---|
| Adult / elder / life expectancy | 16 / 55 / 70 | 40 / 280 / 350 | 30 / 150 / 200 | 12 / 38 / 50 | 10 / 70 / 90 |
| Fertility per year · max children | 0.30 · 6 | 0.05 · 3 | 0.10 · 4 | 0.45 · 8 | 0.32 (clutch 1–3) · 8 |
| Food per tick | 0.05 | 0.04 | 0.05 | 0.06 | 0.045 |
| Build / Research / Faith / Military / Trade | 1.05 / 1.0 / 1.0 / 1.0 / **1.25** | 0.85 / **1.3** / 1.25 / 0.9 / 0.85 | **1.3** / 0.95 / 0.9 / 1.15 / 1.05 | 0.95 / 0.75 / 0.9 / **1.4** / 0.7 | 1.0 / 1.05 / **1.35** / 1.05 / 0.95 |
| Aggression / Expansion / Piety / Xenophobia | 0.45 / 0.65 / 0.5 / 0.2 | 0.2 / 0.3 / 0.75 / 0.75 | 0.4 / 0.35 / 0.45 / 0.55 | 0.9 / 0.85 / 0.4 / 0.6 | 0.35 / 0.45 / 0.9 / 0.35 |
| Wonder (MinAge · early unlock) | Grand Cathedral (Medieval · Classical tech) | World Tree (Iron · Bronze) | Deep Forge (Bronze) | War Idol (Bronze · Dawn) | Sun Pyramid (Iron · Bronze) |

**Racial affinity** (live; a row is how that race sees each column, from −1 to +1). Opinion is
built partly from affinity × 30.

| | Human | Elf | Dwarf | Orc | Saurian |
|---|---|---|---|---|---|
| **Human** | 1.0 | 0.2 | 0.35 | −0.3 | 0.1 |
| **Elf** | 0.15 | 1.0 | −0.3 | −0.6 | 0.05 |
| **Dwarf** | 0.35 | −0.3 | 1.0 | −0.45 | 0.05 |
| **Orc** | −0.25 | −0.6 | −0.4 | 1.0 | −0.1 |
| **Saurian** | 0.1 | 0.1 | 0.0 | −0.1 | 1.0 |

Cross-race marriage needs both affinities to be above 0.3 (`LifecycleBalance`). **Only Humans and
Dwarves can intermarry.**

**Balance intent.**

- Short-lived races (orc, human) win on momentum.
- Long-lived races (elf, dwarf) win on quality per person and on tech.
- Saurians win on mana and patience.
- No race should take the first Ascension in more than 40% of seeds.
- ⚠ Today Saurians or Orcs win every measured seed.

---

## 5. The ten ages

**Advancement** (`AgeAdvanceCheck`) requires all three:

1. `TotalResearch` ≥ the threshold.
2. At least `RequiredTechsFromPreviousAge` techs from the previous age. This is 60% of that age's
   general techs, which is 5 for every age.
3. `MinPopulation` and `MinSettlements` met civ-wide.

Entering an age:

- recomputes `civ.Modifiers` (the production and research bonuses below)
- adds lifespan and health bonuses, and raises soldier strength
- makes new buildings available
- changes the style new buildings get (`StyleAge`)
- adds a legendary chronicle entry and an objective milestone.

**Each civilization has its own age.** `WorldState.WorldAge` is the highest age any living civ has
reached.

| # | Age | Threshold · pop · settlements | Prod / Res bonus | Soldier strength | Theme color | Key unlocks | Visual identity (`ArchitectureNotes`) |
|---|---|---|---|---|---|---|---|
| 0 | **Dawn** | 0 · 0 · 1 | 0 / 0 | 1 | `#C9A66B` | Town Center, House, Well, Storehouse, Gatherer's Hut, Hunting Lodge, Fishing Hut, Woodcutter's Camp, Stone Quarry, Spirit Totem, Campfire | Thatch, hide and bent saplings. Round huts around a fire pit. Rough timber totems |
| 1 | **Bronze** | 400 · 25 · 1 | +5% / +4% | 1.4 | `#CD7F32` | Farm, Pasture, Granary, Mine, Smelter, Shrine, Herbalist, Weaver, Palisade · Deep Forge, War Idol | Sun-dried mudbrick, flat reed roofs, ochre plaster, wattle fences |
| 2 | **Iron** | 1,400 · 50 · 1 | +10% / +9% | 2 | `#6E7377` | Blacksmith, Temple, Barracks, Market, Watchtower, Armory, Gold Mine, Scriptorium · World Tree, Sun Pyramid | Fieldstone foundations under timber and thatch or slate. Stone temples, wooden towers |
| 3 | **Classical** | 3,200 · 90 · 2 | +16% / +15% | 2.6 | `#E8E2D0` | Library, Aqueduct, Bathhouse, Theater, Harbor, Workshop, Forum, Arena, Stone Wall · Great Library, Colossus | Marble, colonnades, pediments, terracotta roofs, arches, plazas |
| 4 | **Medieval** | 6,200 · 160 · 2 | +23% / +22% | 3.5 | `#5B6E8C` | Castle, Cathedral, Monastery, Mill, Guildhall, Tavern · Grand Cathedral, Grand Bazaar | Mortared stone, half-timbered upper floors, steep roofs, keeps, gothic arches |
| 5 | **Renaissance** | 10,500 · 260 · 3 | +31% / +30% | 5 | `#C0623B` | University, Bank, Observatory, Hospital, Printworks | Terracotta, stucco, domes, loggias, symmetrical facades, courtyards |
| 6 | **Industrial** | 16,500 · 400 · 3 | +42% / +40% | 8 | `#8E3B2E` | Factory, Coal Mine, Sawmill, Rail Station, Foundry | Red-brick mills and row houses, girders, smokestacks, soot |
| 7 | **Modern** | 25,000 · 600 · 4 | +55% / +53% | 14 | `#9AA5B1` | Power Plant, Oil Well, Industrial Farm, Research Lab, Stadium, Military Base | Reinforced concrete and steel, large windows, flat roofs, avenues |
| 8 | **Information** | 36,000 · 850 · 4 | +70% / +68% | 22 | `#3FA9F5` | Data Center, Electronics Plant, Airport, Medical Center | Glass curtain walls, solar panels, illuminated signage |
| 9 | **Ascension** | 50,000 · 1,200 · 5 | +90% / +88% | 35 | `#F5E6A8` | Hydroponics Tower, Fusion Reactor, Arcology, **Ascension Gate** | Luminous white and gold, floating terraces, organic arcologies, crystalline spires |

Lifespan bonus by age, in years: 0, 1, 2, 4, 6, 8, 11, 15, 20, 25. Health bonus: 0 to +0.30.

**Pacing target.** Dawn 80–120 years, middle ages 120–200, late ages 80–140. Ascension lasts until
the Gate opens.

⚠ **Measured.** Bronze arrives in year 8–13. Ascension arrives in year 42–79. Research outruns the
thresholds by 10–50× from Iron on, and only the population gates hold. BALANCE §5 has the fix.

**Tech tree** (`TechCatalog`): 94 techs, 8–10 per age, 10 of them racial. Effects:

- production, research, growth, faith, health, happiness, military, build-speed, storage and trade
  multipliers
- lifespan
- early building unlocks, for example `dawn.agriculture` → Farm and `iron.philosophy` → Library.

The capstone is **Transcendence** (`ascension.transcendence`, 2,400 RP). ⚠ It is not required for
the Ascension Gate (§20.3).

**Obsolescence.** Buildings past their `MaxAge` are no longer built. Once they have no workers or
residents, `MaintenanceSystem` demolishes them, one per settlement per run:

| Building | Obsolete after |
|---|---|
| Gatherer's Hut, Spirit Totem | Iron |
| Campfire | Classical |
| Hunting Lodge, Palisade | Medieval |
| Castle | Renaissance |
| Watchtower, Stone Wall, Mill | Industrial |
| Workshop | Modern |

**Racial reinterpretation.** Renderers combine `ArchitectureStyle` (race: roof, roundness, colors,
underground, trees or stilts) with the age material notes. A Modern elf city is glass and living
wood, not soot-brick (design target for art).

---

## 6. Settlement growth

### 6.1 Tiers

These come from `SettlementGrowthSystem`, which runs once per season. A settlement drops a tier
only when it falls below 80% of that tier's threshold.

| Tier | Population | Territory radius | Character |
|---|---|---|---|
| Camp | 0–9 | 6 | Starter huts around the town center and the fire |
| Hamlet | 10+ | 8 | First specialist huts. Temple, Barracks and wall minimum tier |
| Village | 30+ | 10 | Fields ring the core. Market, Library, Castle tier |
| Town | 80+ | 13 | Districts emerge. Cathedral, University, wonders |
| City | 200+ | 16 | Industrial and civic quarters. Airport, Arcology |
| Metropolis | 500+ | 20 | Dense core with sprawling outskirts |
| Megalopolis | 1,200+ | 24 | Late-age super-city, near the 600-building cap |

- Upgrades to Town and above go in the chronicle.
- A settlement with no living residents for 2 years is abandoned.
- A settlement without a Town Center queues "rebuild seat of power" at priority 10.

BALANCE §2.5 has the target building counts per tier.

### 6.2 The dwelling rule

**One dwelling per 3–5 people.** Each `House` rolls `HousingCapacity` from 3 to 5, and
`GameDatabase.Validate` enforces that. Twenty more people means 4–7 new roofs.

- **Governor.** Keeps free housing at least max(2, 8% of the population) and anticipates 0.25 years
  of growth (`GovernorSettings`).
- **Housing system.** Places families together, spouses and children first, in the house with the
  most free space nearest to them.
- **Homelessness.**
  - Homeless people move toward the "unhoused" happiness (−0.2) and health (−0.10) targets.
  - They face winter exposure at day 84 (−0.05 health).
  - Births drop to ×0.4 when more than 25% of a settlement is homeless.
  - When three or more are homeless, people migrate.
- ⚠ **At scale, the rule breaks.** Runaway civs reach 6–13 people per house (BALANCE §2.4). That
  feeds plague through the crowding formula.

### 6.3 Zoning and districts

There is no zoning paint. Districts emerge from `PlacementRule`, `Desirability` (−1…1) and the
race's layout settings:

- **Core (`Central`).** Town center, temples, markets, libraries, forums, wonders. Desirability
  0.3–0.9.
- **Residential ring.** Houses (+0.1) prefer pleasant neighbors and avoid industry.
- **Outskirts (`Outskirts`).** Farms (`Fertile`), lumber (`NearForest`), quarries (`NearStone`),
  barracks, palisades, arenas, stadiums.
- **Industry (`Isolated`).** Smelter (−0.6), Blacksmith and Armory (−0.4), Factory, Foundry, Coal
  Mine, Power Plant (−0.8). This forms the visible industrial belt from the Industrial age on.
- **Waterfront (`NearWater`, `Coastal`, `NearRiver`).** Fishing huts, harbors, mills.
- **Race layout.** Compactness 0.2 (orc sprawl) to 0.85 (dwarf density). Road affinity 0.1 (elf
  paths) to 0.8 (human grid). Spacing 12–18 tiles between a race's own towns.

### 6.4 Renovation through the ages

`MaintenanceSystem` runs every 12 ticks.

- **Renovation.** A building whose `StyleAge` lags its civ's age (or whose `StyleRace` differs after
  a conquest) is rebuilt in the current style.
  - 1–3 buildings per settlement per run, by tier, oldest style first.
  - Each costs 30% of the building's cost, and only while a reserve stays in the stockpile.
  - Dwellings that can't be paid for renovate for free, more slowly (25% chance per run).
  - Each renovation sets `StyleAge`, increments `Level`, restores condition and publishes
    `BuildingUpgradedEvent`.
- **Decay.** Condition falls 0.5% per year. That is doubled for an unstaffed job building, and
  ×1.5 when the style is two or more ages behind. Buildings below 0.7 condition get repaired: up to
  3 per run, at 2 Wood + 1 Stone per tile.
- **The result for P4.** A city carries its old quarters for a while after each age change, then
  the new style spreads through it.

### 6.5 Expansion

`ExpansionSystem` runs every 48 ticks.

- **Parent requirements.** A parent settlement needs:
  - population ≥ 30 (orcs 22)
  - at least 25 days of food
  - happiness ≥ 0.4.
- **The party.** It sends 6–10 settlers, at most 25% of its people. They carry 40 Food, 30 Wood and
  10 Stone.
- **Site requirements.** At least 40 buildable tiles within radius 5, average habitability ≥ 0.15,
  and the race's spacing respected.
- **Limits.** There is a cooldown of 4–14 years, shorter for expansionist races. The cap is
  `MaxSettlementsPerCiv` (24).

---

## 7. Economy and production chains

**Resources (13).** Food, Wood, Stone, Ore, Metal, Gold, Tools, Cloth, Goods, Weapons, Fuel,
Machinery, Electronics.

**Services (9).** Faith, Health, Research, Entertainment, Defense, Water, Power, Commerce,
Administration.

```
 RAW (tile-bound, depletes)        PROCESSED                              USES
 --------------------------        ---------                              ----
 Trees  --Woodcutter (0.12)-->  WOOD -------------------------------+--> every building, repairs, Tools/Weapons/Goods input
          Sawmill (0.25, Ind.)                                      |
 Stone  --Quarry (0.08)------>  STONE ------------------------------+--> walls, temples, castles, wonders, repairs
 Ore    --Mine (0.07)-------->  ORE --Smelter (0.06 -> 0.05)-----> METAL --+--Blacksmith (+Wood) 0.04 --> TOOLS --> mid/late construction
                                    \--Foundry (+Fuel, 0.09, Ind.)-> METAL +--Armory (+Wood) 0.03 -----> WEAPONS --> soldier x1.3
 Gold   --Gold Mine (0.03)--->  GOLD <-- Commerce x0.02/pt, Bank 0.04, Grand Bazaar 0.06  --> upkeep, Classical+ costs, Electronics
 Coal   --Coal Mine (0.10)--->  FUEL --+
 Oil    --Oil Well (0.18)---->  FUEL --+--Factory (+Metal) --> MACHINERY 0.05 + GOODS 0.04 --> Modern+ costs, Gate
                                        \--Power Plant (service: 20 Power)
 Pasture ------------------>   FOOD 0.10 + CLOTH 0.04
 Weaver -------------------->  CLOTH 0.05 --Workshop (+Wood) --> GOODS 0.04 --> Medieval+ costs, trade
 Metal + Gold --Electronics Plant 0.04--> ELECTRONICS --> Data Center (service), Information+ costs, Ascension Gate (600)

 FOOD: Gatherer 0.09, Hunter 0.12, Fisher 0.15 (Dawn) -> Farm 0.22, Pasture 0.10 (Bronze; Farm in Dawn by tech)
       -> Mill 0.12 (Medieval) -> Industrial Farm 0.60 (Modern) -> Hydroponics 1.20 (Ascension); idle adults forage 0.035
```

All rates are per worker per tick before efficiency, terrain, season and modifiers.

**Rules.**

- **Production** (`ProductionSystem`) = rate × slots × efficiency × terrain × civ production
  modifier × `ProductionMultiplier` × (for food) the weather yield.
  - Efficiency = staffed skill and experience ÷ slots × condition, × 1.25 for Industry with Power
    coverage. It is capped at 1.5.
  - The terrain factor depends on trees, deposits, water or fertility within a small radius.
  - Missing inputs reduce output pro rata.
- **Storage.** Each settlement has one capacity per resource: base 150, plus building storage
  (Town Center 100, Storehouse 120, Granary 150, Grand Bazaar 200, …), times `Modifiers.Storage`.
  Food spoils at 0.15% per tick above 60% of capacity. **Construction pays the full cost when it
  starts.** A wonder's whole cost must fit in one stockpile.
- **Upkeep.** `UpkeepGold` per tick. Unpaid upkeep wears condition (−1% per 24 ticks).
- **Commerce.** Each commerce point makes 0.02 Gold per tick.
- **Trade** (`TradeSystem`). Moves surplus between a civ's settlements within 80 tiles. With a trade
  agreement it also moves surplus between civs, earning 0.1 Gold per traded unit.
- **AI economy.** The AI reads `Stockpile.Net`, `econ.foodDays` and service coverage to choose
  builds and job priorities.

---

## 8. Population model

### 8.1 Life cycle

```
 birth --> CHILD --(AdulthoodAge)--> ADULT --(ElderAge)--> ELDER --> death (hazard climbs from 60% of life
             |                         |  partner, work, bear children            expectancy: 50% at LE, 95% at 1.3x LE)
             |                         +--> migrate, found towns, march to war, become a hero or leader
             +--> starvation, disease, violence, disaster, divine wrath, exposure at any stage
```

- **Births** (`BirthSystem`). Only adult women bear children. The yearly chance is:

  ```
  FertilityPerYear × Modifiers.Growth × GrowthMultiplier
    × food factor (0.3 below 10 food-days … 1.2 at 60+)
    × housing factor (0.4 when more than 25% are homeless)
    × (0.6 + 0.8 × happiness)
    × 1.4 if the mother has the Fertile trait
  ```

  - The mother needs satiation ≥ 0.4 and health ≥ 0.3.
  - Twins happen 2% of the time. Saurian clutches have 1–3 young.
  - In a tiny tribe with fewer than 2 couples, unpartnered women may bear children at ×0.3, so a
    tribe can't die out from bad pairing luck.
- **Families.**
  - The settlement pairs people off each run: 2 pairings plus one per 20 residents.
  - The age gap must be at most 25% of the race's life expectancy.
  - Cross-race pairs need mutual affinity above 0.3.
- **Mortality.** Children have a 2% per year base hazard, halved by health care. Adults have 0.3%
  per year. Senescence starts at 60% of life expectancy. Sickly ×1.5, Hardy ×0.7.
- **Traits.** Strong, Clever, Devout, Fertile, Sickly, Brave, Lazy, Charismatic, Greedy, Wise, Hardy,
  Hero.
- **Skill and experience.** Skill (about 0.6–1.4) and experience (0–1) scale worker output by
  0.8 + 0.4 × experience. Experience grows 0.002 per tick on the job.
- **Leaders.** `LeadershipSystem` keeps every civ led. The best adult of the capital takes over:
  Wise, Charismatic and Hero traits count, as do maturity, skill, health and a dynastic bonus. Each
  succession goes in the chronicle.
- **Heroes.** `HeroSystem` allows up to 3 living heroes per civ, 1 per settlement. Roles are General,
  Sage, Prophet, Architect, Explorer, Merchant Prince, Healer and Bard. Each role has a small,
  bounded effect, and every hero gets a biography in the chronicle when they die. The Prophet power
  creates heroes directly.

### 8.2 Needs, happiness, health and faith

`WellbeingSystem` runs every 6 ticks. Each person moves smoothly toward three targets.

**Happiness target:**

```
0.35 + 0.20·satiation + (housed ? +0.15 : −0.20)
     + 0.10·min(1, Entertainment) + 0.08·min(1, Faith) + 0.07·min(1, Health)
     + civ HappinessBonus − 0.08 while at war ± traits (Devout, Greedy, …)
```

**Health target:**

```
0.40 + 0.30·satiation + 0.12·Health coverage + 0.08·Water coverage
     + (housed ? +0.06 : −0.10) + 0.25·(race BaseHealth − 1) + civ HealthBonus − old age
```

**Faith target:** min(1, Faith coverage) × civ Devotion. Devout people get ×1.25 + 0.1.

**Unrest** rises while settlement happiness is below 0.35. It decays above that.

**Migration** (`MigrationSystem`, every 48 ticks):

- Homeless (at least 3), unemployed or unhappy (below 0.3) residents move to a better settlement
  of their own civ within 80 tiles. At most 5% move per run.
- **Defection.** When unrest is at least 0.8, residents may move to *another civ's* settlement
  within 60 tiles that is at least 0.25 happier. This is P3: emergent loss of a city's heart.

**Service demand per capita per tick:**

| Service | Demand |
|---|---|
| Water | 0.10 |
| Power | 0.15 |
| Faith, Health, Entertainment | 0.05 |
| Defense, Commerce, Administration | 0.04 |
| Research | 0.03 |

Coverage = output ÷ demand, clamped 0–3.

**Epidemics.** The chance per settlement per year is 2% + 6% × crowding density, where density
depends on people per building, Health and Water. An outbreak kills 2–10% of residents, less with
Health coverage, and there is a 5-year cooldown. ⚠ Currently 40–95% of all deaths (BALANCE I-8).

### 8.3 Jobs

`JobAssignmentSystem` fills `JobSlots` using `settlement.JobPriority` from the AI. Builders answer
`DesiredBuilders` = 2 × paid sites + 1, capped at adults ÷ 3. Idle adults add 0.3 labor each and
forage. Soldiers come from Barracks (6), Castle (10), Watchtower (1) and Military Base (20).

---

## 9. AI design

All AI is deterministic. It uses `ctx.Rng` only for tie-breaks and jitter, and explains itself in
`AiLog`.

### 9.1 Civilization strategy

`CivilizationStrategySystem` runs every 24 ticks.

- **Posture:** Peaceful, Defensive or Militarist.
- **Focus:** Growth, Research, Faith, Military or Wealth.
- Both come from personality × situation: threat, power ratio against the strongest rival (shown
  as `ai.powerRatio`), active wars, and hostile neighbors within 40 tiles. A 0.1 hysteresis stops
  flip-flopping. Both are written to `Civilization.Memory` (`ai.posture`, `ai.focus`) for the
  governors to read.
- **Wonder planner.** Order of preference:
  1. The **Ascension Gate** in the Ascension age.
  2. The racial unique.
  3. The shared wonder that best fits the focus.

  It queues a wonder at priority 6 once 50% of the cost can be afforded, and retries every 3 years.
- **Research director** (`ResearchDirectorSystem`, every 24 ticks). Picks `CurrentResearchTechId` by
  focus, the effects the civ needs, required-tech progress and cost. In a food crisis (fewer than
  20 food-days) it switches to food techs.
- **Expansion** (§6.5).

### 9.2 Settlement governor

`SettlementGovernorSystem` runs every 6 ticks. It is a utility AI.

```
score(building) = Π considerations, each a ResponseCurve over a normalized input:
                  need(category signal) × affordability × placement feasibility × posture/focus bias
                  × race favored bias × saturation(count) × repeat cooldown
```

- **Need signals.**
  - housing shortfall
  - food-days and net food
  - storage pressure
  - service coverage gaps (faith, health, water, entertainment, administration, commerce)
  - unemployment against open slots
  - defense against threat (at war, enemies within 40 tiles)
  - territory features such as forest, ore and coast (rescanned yearly).
- **Queue rules.** At most 4 orders and at most 2 new orders per run. The first order needs
  utility ≥ 0.2, the second ≥ 0.35. Houses may stack 2-deep. Stale orders drop after 3 years. If
  placement fails twice, that type is blocked for a year.
- **Town planner.** Picks the site (site scoring, zoning map, roads). `ConstructionSystem` then pays
  and builds.

### 9.3 Rules for the AI

1. **Explainable.** Every order and every war carries a human-readable reason.
2. **Imperfect, not stupid.** Bad bets are fine. Degenerate states are bugs: starving beside a full
   granary, 200 homeless with 0 houses queued.
   - ⚠ The governor currently builds 3–6× more job slots than it has workers (BALANCE I-6).
3. **Personality visible.** Two races in the same situation choose visibly differently.
4. **Responds to the god.**
   - Blessings raise favor and devotion.
   - Wrath lowers favor, but makes pious races (piety > 0.6: Elf, Saurian) more devoted.
   - Doctrines adapt to the god's behaviour (§12.4).

---

## 10. Diplomacy and war

### 10.1 Relations

- **Contact.** Civs meet within 45 tiles (`ContactSystem`).
- **Opinion** runs from −100 to 100 and drifts each run toward the sum of these drivers
  (`RelationsTuning`):
  - race affinity × 30
  - trade agreement +15
  - shared enemy +10
  - shared god favor +8
  - border friction −10 per close pair (within 20 tiles), at most −30
  - xenophobia × 20
  - recent war −25 for 20 years
  - diplomacy personality × 10
- **Stances** (with hysteresis):

| Stance | Enter | Leave | Extra condition |
|---|---|---|---|
| Allied | ≥ 60 | < 45 | diplomacy ≥ 0.4 |
| Friendly | ≥ 25 | < 15 | |
| Neutral | −25 or above | | |
| Hostile | below −25 | | |
| War | declared | | |

- **Trade agreements** need opinion ≥ 15 and mercantilism ≥ 0.4.

### 10.2 War

**Declaring** (`WarDecisionSystem`):

- A *hatred war* needs opinion ≤ −40 and aggression ≥ 0.35.
- An *opportunist war* needs aggression ≥ 0.6, opinion ≤ −10 and a strength ratio ≥ 1.3.
- Each has a 30% per year chance once its conditions are met. A civ fights at most 2 active wars.
- **Casus belli:** border dispute, holy war (opposed god-views, §12.4), conquest, old grudge, call
  to arms (allies).

**Armies** (`ArmySystem`):

- Raised from 70% of soldiers, with at least 4.
- Strength = soldiers × age soldier strength (1 → 35) × race military × modifiers × morale. Weapons
  at 0.5 per soldier add ×1.3.
- They march at 0.25 tiles per tick × move speed over terrain, and engage at range 2.
- They retreat at morale 0.3, or when 30% of their soldiers are left.
- Each battle publishes `BattleEvent`.

**Sieges and conquest:**

- An army besieges within 2 tiles. Siege damage reduces defense (`ServiceOutput[Defense]` ×
  (1 − damage) + garrison) by up to 90%, and repairs at 1% per tick afterwards.
- A settlement is captured when its defense falls below 15% and its garrison is at most 1. That
  gives +25 war score and `SettlementCapturedEvent`, then `TransferSettlement`. The settlement keeps
  its people, who renovate into the conqueror's style over time.
- **Razing.** Orcs (aggression above 0.7) raze captured camps. Any civ at its settlement cap razes.
  **A people's last settlement is always captured, never razed.**
- A civ with no settlements collapses (`CivilizationCollapsedEvent`, legendary chronicle).

**Peace:**

- Decisive at war score ±60. The loser pays 30% tribute.
- Stalemate after 8 years with |score| below 25: 40% per year peace chance.
- Exhaustion when 40% of soldiers are lost. Hard stop at 25 years.
- A 10-year truce follows.
- ⚠ The 10-year truce lets orcs declare again right away (BALANCE §7).

### 10.3 The god in war

Lightning on an army, Bless on the defenders, Earthquake against the walls (condition damage), Flood
at a river crossing. Divine meddling shifts the favor of the civs affected. It can also shift their
religious view of the god, which can turn the next war into a holy war.

---

## 11. Divine powers

Every cast is a `GodCommand`:

1. HUD → `GodSimulation.TryCastPower` → `CommandQueue.TryEnqueue`.
2. `GodCommandValidator` checks it: power exists, target in bounds, radius in range, enough mana,
   not on cooldown, and the rate limiter (token bucket: `MaxCommandsPerTick` per tick globally plus
   a per-source bucket).
3. `GodCommandSystem` validates it again when it runs (TOCTOU), applies the effect, charges mana,
   starts the cooldown, shifts alignment and applies mortal reactions.

Rejected casts say why, for example "Not enough mana: …". Casts made while paused are queued and run
on the next tick.

| # | Key | Power | Cat. | Mana | CD (days) | Radius | Align Δ | Effect (code) | Counterplay and consequences |
|---|---|---|---|---|---|---|---|---|---|
| 0 | 1 | **Bless** | Blessing | 20 | 24 | 3–10 | +0.03 | Everyone in range +0.2 happiness, +0.1 health, +0.05 faith | Wears off as wellbeing drifts back. Cheap, frequent |
| 1 | 2 | **Rain** | Blessing | 15 | 12 | 4–12 | +0.01 | 48 days of rain, ends drought, +0.15 moisture in range, puts out fires | ⚠ The weather is world-global: it ends *everyone's* drought (BALANCE I-11) |
| 2 | 3 | **Fertility** | Blessing | 30 | 48 | 3–8 | +0.03 | Enriches soil in range and blesses couples with births | Creates housing and food pressure |
| 3 | 4 | **Inspire** | Blessing | 40 | 96 | 2–8 (needs a settlement) | +0.02 | Research burst of 50 + 2 × civ population | Can't skip the population or settlement gates |
| 4 | 5 | **Heal** | Blessing | 25 | 24 | 2–8 | +0.03 | Health set to 1 for everyone in range | Doesn't fix crowding or missing Health coverage, so plague returns |
| 5 | 6 | **Bountiful Harvest** | Blessing | 30 | 48 | 3–8 | +0.02 | +40 Food to each settlement in range | Capped by storage. The surplus above 60% spoils |
| 6 | 7 | **Prophet** | Blessing | 80 | 480 | 0–4 (needs a settlement) | +0.04 | The most faithful adult nearby becomes a Hero, Devout and Charismatic prophet. Their civ gets +0.2 devotion | The prophet can die. Long cooldown |
| 7 | 8 | **Raise Land** | Shaping | 10 | 2 | 1–6 | 0 | Raises elevation (0.12 per cast), hold to paint. Skips roads, walls, farmland, fires and deposits | Can dam rivers and strand harbors |
| 8 | 9 | **Lower Land** | Shaping | 10 | 2 | 1–6 | 0 | Lowers elevation, hold to paint. Can make lakes and bays | Can drown low towns and farms |
| 9 | 0 | **Plant Forest** | Shaping | 8 | 4 | 1–6 | +0.005 | Adds trees (wood, hunting) | Wildfire fuel. Takes land the AI wanted for farms |
| 10 | Shift+1 | **Reveal Ore** | Shaping | 35 | 96 | 1–5 | 0 | Reveals ore, stone and gold deposits | Mine booms, border friction |
| 11 | Shift+2 | **Spawn Tribe** | Creation | 150 | 960 | 0 (land target) | +0.05 | A new civ of the chosen race. Founders aged 14–40, scaled for slow-maturing races. Devotion 0.9, favor 0.5. Needs 30+ buildable tiles within 5, at least 10 tiles from any settlement | Instant neighbors. Population caps apply |
| 12 | Shift+3 | **Lightning** | Wrath | 5 | 1 | 0–1 | −0.01 | Kills 1–2 people nearest the strike and sets the building on fire | Rain puts out fires. Pious races gain fear-devotion |
| 13 | Shift+4 | **Wildfire** | Wrath | 25 | 24 | 1–5 | −0.04 | Ignites the area. Fire spreads through trees and buildings | Rain and Flood stop it. Winter halves its spread, summer and drought make it 1.8× |
| 14 | Shift+5 | **Earthquake** | Wrath | 60 | 192 | 3–8 | −0.06 | Buildings lose 0.3–0.8 condition (less at the rim). Each person has up to a 3% death chance | AI repairs and rebuilds |
| 15 | Shift+6 | **Meteor** | Wrath | 120 | 480 | 2–6 | −0.08 | A crater (may become a lake) and scorched land. The core is destroyed, the rest damaged and burning, everyone in range dies. 50% chance of an ore or gold deposit | ⚠ Not age-gated (`MinWorldAge` Dawn). Phoenix objective if a town is rebuilt nearby |
| 16 | Shift+7 | **Plague** | Wrath | 70 | 288 | 2–8 | −0.08 | Everyone in range −0.4 health. 10–25% die at once, the frailest first | Disease resistance (Elf 1.3, Orc 1.25). Heal |
| 17 | Shift+8 | **Flood** | Wrath | 50 | 192 | 3–8 | −0.05 | Low land near water floods: farmland washed away, fires out, buildings damaged, 1–2% drown | Higher ground is safe. Raise Land protects |
| 18 | Shift+9 | **Drought** | Wrath | 45 | 192 | 4–12 | −0.04 | 96 days of drought (food ×0.5), ends rain, −0.2 moisture in range | ⚠ The weather is world-global: it hits every civ |

**Mortal reactions.**

- Blessings: favor +0.05 to +0.15 and devotion +0.02 to +0.05, scaled by cost.
- Wrath: favor −0.10 to −0.20. Pious races (Elf, Saurian) gain devotion.
- Shaping and Creation: no reaction.

**Design targets not yet in code:**

- Gate Meteor (Iron), Plague and Earthquake (Bronze) by `MinWorldAge`.
- Regional weather for Rain and Drought.
- Elves should resent wildfire. Today their piety makes them *more* devoted when the god burns
  their forest, which is a lore clash.

---

## 12. Faith, mana, alignment and religion

### 12.1 Mana

`FaithSystem` runs every 6 ticks.

- **Regen per tick** = Σ over civs [ Σ settlement Faith output × Devotion × 0.02 × `Modifiers.Faith`
  + population × Devotion × 0.0004 ] × `ManaRegenMultiplier`.
- **Capacity** = 300 + 0.5 × followers, capped at 5,000. Objectives can raise it further.
- **Starting mana** is `StartingMana` (120).
- **Implication.** Believers are income. Smiting your most pious people strangles your power. A
  saurian theocracy funds a lot of wrath.
- Measured regen: about 7–22 per year in Dawn, about 1,000 per year late. ⚠ That is a late-game
  flood (BALANCE §6).

### 12.2 Devotion and favor

- **Devotion** (0–1) drifts toward 0.3 + 0.5 × average settlement faith + 0.2 × GodFavor. Blessings,
  prophets and faith buildings push it up.
- **GodFavor** (−1 to 1) decays 1% per run toward 0.
- Civs with a strong shared favor get +8 opinion of each other.

### 12.3 Alignment (−1 wrathful … +1 benevolent)

`GodState.Alignment` moves by each power's `AlignmentDelta`, clamped to ±1. Today it feeds three
things:

1. Religious doctrine selection (§12.4).
2. The **Merciful** (≥ 0.7) and **Wrathful** (≤ −0.7) objectives.
3. The HUD.

| Alignment | Intended world reaction (design target, beyond doctrine) |
|---|---|
| ≥ +0.6 *Benevolent* | Blessings −10% cost, wrath +25%. Civs drift toward "Loving Parent". The chronicle calls you "the Merciful" |
| −0.6 … +0.6 | No modifiers |
| ≤ −0.6 *Wrathful* | Wrath −10% cost, blessings +25%. Fear-devotion spikes, then decays. Civs drift toward "Wrathful Judge". "The Dreadful" |

Alignment drifts back toward 0 when no powers are cast **(design target)**.

### 12.4 Religion (doctrines)

`Core/Religion`: each civ holds one doctrine on each of 4 axes. The choice is deterministic: race
personality plus the god's observed behavior (kindness, wrath, activity, silence), with a small
seeded jitter.

| Axis | Doctrines |
|---|---|
| Worship | Sun Worship, Ancestor Veneration, Nature Spirits, Celestial Order |
| Ethos | Mercy, Martial Zeal, Asceticism, Prosperity Gospel |
| Practice | Pilgrimage, Monasticism, Sacrifice, Festivals |
| View of the god | Loving Parent, Wrathful Judge, Distant Watcher, Trickster |

- **Opposed views.** Loving Parent ⟷ Wrathful Judge, and Distant Watcher ⟷ Trickster. They
  justify **holy wars**.
- **Reformations** force a doctrine change.
- How you behave as a god is written into your peoples' faith. That makes P2 and P3 visible in the
  chronicle.
- `ReligionSystem` runs every 48 ticks. Its tuning lives in `ReligionTuning`.

### 12.5 World events (storytelling)

`Storytelling/WorldEventSystem` runs every 48 ticks. Each civ has a 6% × √(settlements) chance per
run (capped at ×3) of drawing one weighted card from `WorldEventCatalog`. The decks are Prosperity,
Faith, Calamities and Politics, with golden ages, raids and secession among them. Per-event
cooldowns are kept in Memory, and a civ rests for 1.5 years after an event. The events give the
chronicle authored-feeling beats on top of the emergent systems.

---

## 13. Natural world

### 13.1 Seasons and weather

`SeasonWeatherSystem` and `NaturalEffects`:

| Season | Days | Food yield | Notes |
|---|---|---|---|
| Spring | 0–23 | 1.05 | Flood season |
| Summer | 24–47 | 1.25 | Fire season: spread ×1.8 |
| Autumn | 48–71 | 1.10 | Storm peak |
| Winter | 72–95 | **0.40** | Fire spread ×0.5. Exposure on day 84 for the homeless (−0.05 health) |

- **Yield modifiers.** Rainfall runs 0.5–1.5 and maps to a yield of 0.8–1.2. Drought ×0.5, rain
  ×1.1. The total is clamped to 0.1–2.0.
- **Climate.** A seeded multi-century oscillation (±0.15) drives `TemperatureOffset`.
- **Storms.** Daily chance of 0.3–1.2% depending on season.

### 13.2 Natural disasters

`DisasterSystem` rolls per civilization per interval, × `DisasterFrequency`:

| Disaster | Base chance per civ per year |
|---|---|
| Earthquake | 1.2% |
| Volcano | 0.6% (needs Volcanic biome) |
| Flood | 1.6% (spring) |
| Wildfire | 1.6% (summer or drought) |
| Storm | 1.6% × season weight |

- Each civ has a 3-year cooldown, during which the chance is ×0.25.
- **Wilderness:** volcanoes 2% per year, wildfires 3% per year.
- **Epidemics** roll per settlement (§8.2).
- **Drought** comes from the weather. **Famine** is a consequence (`FamineEvent` when the food ratio
  falls below 0.7).
- **Lightning and Meteor** come only from god powers.
- **Opening protection (design target).** Disasters should be minor severity for the first 20
  years. Today severity is uniform from 0.35 to 1.0.

### 13.3 Fire

`FireSystem`:

- Burning tiles and buildings burn for 6–20 ticks.
- Buildings lose 0.04 condition per tick while burning.
- Trees lose 15% + 3 wood per tick.
- Fire spreads to neighbors, scaled by season, rain and fuel moisture. Roads, farmland and walls
  block it.
- Burned land becomes `Scorched`. It recovers about 15% per year and regrows trees at 40 wood.

---

## 14. Objectives and endgame

### 14.1 The Ascension Gate

- **The building.** An Ascension-age wonder, one per civilization, 4×4, Town tier, 250 population.
  - Cost: 500 Wood, 900 Stone, 900 Metal, 600 Gold, 400 Tools, 300 Fuel, 600 Machinery and 600
    Electronics. BuildWork 3,000.
  - Once built it provides 50 Faith and 50 Research.
- **The AI** makes the Gate its first wonder priority in the Ascension age.
- **On completion** (`ObjectiveSystem`):
  - The first Gate records the world's **Ascension victory** (`game.ascended`, tick, civ) and a
    legendary Divine chronicle entry: *"Ascension! The Gate of the X blazes open above Y: mortals
    step beyond the veil and the heavens crown their god. The world endures, and the watch goes
    on."*
  - The god receives the "Ascension" objective reward.
  - The sandbox continues.
- **Design targets:**
  - **Transcendence of the civ.** Its people leave the world, its cities become luminous ruins
    (`Ruins` and `Sacred` flags) that others may resettle, and a per-civ "transcended" flag is set
    (`Civilization.Memory["asc.transcended"]` until the contract has a field).
  - Rival civs gain a casus belli against a civ building the Gate.
  - An end-of-history summary screen.
- ⚠ **Measured: no Gate was placed** in 96 years of Ascension (BALANCE I-1). The endgame is
  currently unreachable.

### 14.2 Divine objectives

`ObjectiveCatalog` defines 61 objectives with mana and max-mana rewards (some also shift
alignment). Some are hidden and some repeat. Categories:

| Category | Examples |
|---|---|
| First steps | First Miracle, First Light (a hamlet), Hundred Souls, Rain Maker, First Century |
| Prosperity | Village Green → Teeming Metropolis → Endless City, Great Hearth (250 in one settlement), Seeker of Truths, A Thousand Roofs, the age milestones (Bronze Shepherd … Threshold of Heaven) |
| Wonders | Wonder of the World, Seven Wonders, Racial Pride ×5, Keeper of Scrolls, Shadow of the Colossus, Crossroads of the World, **Ascension** |
| Faith | Merciful (alignment ≥ 0.7), Prophet's Legacy, Devoted Flock, **Peacekeeper** (100 years without war), Brimming Chalice, Healer of Nations, Many Peoples |
| Wrath | Wrathful (≤ −0.7), Witness to War, Drums of War, Hammer of Heaven, Pestilence (hidden), **Phoenix** (a meteor-razed town rebuilt within 50 years, hidden), Twilight of Nations (hidden) |
| Legend | Eternal Watch (1,000 years), Oracle of the Network, Threshold of Heaven |

These objectives make every play style (§1.2) a progression path.

---

## 15. Session flow: the first 10 minutes

The tutorial script (`Unity/Runtime/Tutorial`) runs these steps in order:

1. Welcome
2. Camera
3. Peoples
4. Inspect
5. Time
6. Chronicle
7. Mana
8. Bless
9. Shape
10. Build (watch the AI build)
11. Ages
12. Wrath
13. Objectives
14. Free play

The script and content exist. The scene component that presents them is a design target.

The plan assumes Standard settings, all 5 races, and 4 ticks/s rising to 16 ticks/s. At the target
pacing, 10 minutes is about 30–60 sim years.

| Time | Player sees | Teaches | Systems |
|---|---|---|---|
| 0:00–0:45 | The New World menu (Esc menu → new world): seed, map, races. Generation takes under 3 s at 192² | Defaults are good | WorldGenerator, StartLocationFinder |
| 0:45–1:30 | The camera opens over a camp. *"Welcome, young god."* Five banners on the minimap | WASD or arrows (Shift for fast), middle-drag pan, right-drag or Q/E rotate, wheel or PgUp/PgDn zoom, click the minimap | Camera, minimap |
| 1:30–2:30 | *Peoples* and *Inspect*: click a settlement and read the AI log line explaining its next build | Selection, the inspector | Selection, Governor |
| 2:30–3:30 | *Time*: pause with Space, speed with + and − | Speeds 1, 4, 16, 64, 240 ticks/s | Runner |
| 3:30–4:30 | *Chronicle*: the first births and the first finished house. Click an entry to jump | Jump to event | Chronicle |
| 4:30–6:00 | *Mana* and *Bless*: press **1**, use `[` and `]` to size the ring, click a camp. Golden glow, happiness up | Powers, mana, radius, Esc or right-click to cancel | GodCommand pipeline |
| 6:00–7:30 | *Shape*: hold **8** (Raise Land) to paint a hill, and press **0** to plant forest by the elves | Hold-to-paint | Shaping powers |
| 7:30–8:30 | *Build*: watch the camps grow. Winter arrives (day 72) and food dips | Seasons, food | Weather, Production |
| 8:30–10:00 | *Ages*: the first tech and the age progress. *Wrath*: Lightning (Shift+3) costs only 5 mana. *Objectives*: First Miracle completes | Ages, alignment, objectives | Progression, Objectives |

**Success criteria (telemetry):** at least 1 power cast, at least 3 selections, at least 1
chronicle jump, and no stretch of more than 60 seconds without an event of importance ≥ 1.

⚠ With today's pacing, a civ can reach Iron within these 10 minutes at 16 ticks/s.

---

## 16. UX principles

1. **Diegetic first, panels second.** Smoke from working industry, banners in civ colors, seasonal
   fields, a day and night cycle, weather VFX. Panels confirm what the world shows.
2. **Every cause is inspectable.** Chronicle entries carry a position, civ and settlement. AI orders
   carry reasons. Rejections carry reasons.
3. **No hidden failure.** Famine, homelessness, unrest and plague show markers or overlays before
   they kill. Overlays: Territory, Population Density, Happiness, Health, Faith, Fertility,
   Resources, Elevation, Moisture, Temperature, Danger, Wealth, Age, Religion. The `OverlayMode`
   model is on disk. The renderer and selector are a design target.
4. **The god never micromanages.** There is no build or job UI. The influence verbs are the 19
   powers.
5. **Respect the player's time.** Pause works anywhere. Speeds go up to 240 ticks/s. Toasts are
   throttled by importance. The HUD refreshes text at 4 Hz with cached strings.
6. **Readable silhouettes.** `BuildingShape` per category, `RoofStyle` per race, materials per age.
   Silhouettes are tested in grayscale.
7. **Consistent feedback grammar.**
   - The targeting ring is colored by category: blessing gold, shaping green, creation cyan, wrath
     red, and grey when the power can't be afforded or is cooling down.
   - Each power has its own VFX preset (`GodPowerVFX`) and camera shake for wrath.

---

## 17. Accessibility

| Area | Commitment |
|---|---|
| Colour | Colourblind-safe civ banners plus a pattern or icon per race. Overlays use perceptual ramps (`ColorRamps`), with diverging red-blue for happiness. Never colour alone |
| Text | UI scale on top of DPI scaling (`GodSimHUD.UiScale`). High-contrast skin. Dyslexia-friendly font option **(design target)** |
| Motion and flash | Settings to reduce camera shake (`AddShake` trauma) and flash, and to set particle density **(design target)** |
| Input | Remapping **(design target)**. Mouse-only play through the power bar. Hold-to-paint has a toggle option **(design target)**. No chords required beyond Shift for powers 11–19 |
| Time pressure | None. Casting while paused queues the command |
| Audio | Generative music and procedural SFX (`Unity/Runtime/Audio`). Every cue has a visual counterpart |
| Cognitive | A skippable tutorial with a tip rotation, chronicle importance filters, and an F1 help overlay |

---

## 18. Difficulty and tuning knobs (`SimConfig`)

Every value is clamped by `SimConfig.Sanitize()` (untrusted input). The same fields are exposed in
the Unity `GodSimSettings` asset.

| Field | Default | Sanitized range | Effect |
|---|---|---|---|
| `Seed` | 20261119 | any | Deterministic world and history |
| `MapWidth` / `MapHeight` | 192 | 64–1024 | World size |
| `Preset` | Continents | 4 presets | Landmass |
| `StartingRaces` | all 5 | 1–16 entries | Which civs spawn (duplicates allowed) |
| `InitialPopulationPerRace` | 14 | 2–200 | Starting camp size |
| `MaxPopulation` | 40,000 | 100–250,000 | Hard cap. ⚠ Reached by year 100 today |
| `MaxSettlementsPerCiv` | 24 | 1–128 | Expansion cap. A civ at the cap razes conquests |
| `MaxBuildingsPerSettlement` | 600 | 10–5,000 | Planner cap |
| `MaxArmies` | 256 | 0–4,096 | War scale |
| `MaxCommandsPerTick` | 4 | 1–64 | God command rate limit (validator). ⚠ `GodCommandSystem` has its own hard cap of 8 |
| `CommandQueueCapacity` | 32 | 1–1,024 | Pending commands |
| `GrowthMultiplier` | 1 | 0–10 | Birth rate |
| `ResearchMultiplier` | 1 | 0–50 | Research rate |
| `ProductionMultiplier` | 1 | 0–10 | All production |
| `DisasterFrequency` | 1 | 0–10 | Natural disasters and epidemics (0 = off) |
| `EnableWarfare` / `EnableDisasters` | true | bool | Peaceful or sandbox worlds |
| `StartingMana` | 120 | 0–100,000 | Opening budget. Sanitize falls back to 100 on NaN |
| `ManaRegenMultiplier` | 1 | 0–100 | Regen |
| `StrictMode` | false | bool | Rethrow system faults (CI) |
| `MaxSystemFaults` | 25 | 1–100,000 | Fault isolation threshold |

**Difficulty presets (design target):**

| Preset | Growth | Research | Production | Disasters | Mana start / regen | Warfare |
|---|---|---|---|---|---|---|
| Gentle | 1.25 | 1.25 | 1.2 | 0.5 | 200 / 1.5 | on |
| Standard | 1 | 1 | 1 | 1 | 120 / 1 | on |
| Harsh | 0.85 | 0.9 | 0.85 | 1.75 | 60 / 0.75 | on |
| Peaceful | 1 | 1 | 1 | 0.5 | 120 / 1 | **off** |
| Sandbox | 1 | 1 | 1 | 1 | 100,000 / 100 | on |
| Fast History | 1.5 | 2.5 | 1.25 | 1 | 200 / 2 | on |

The presets assume the structural pacing fixes in BALANCE §5.2 and §10. Global multipliers alone
can't fix pacing (BALANCE §9.5).

---

## 19. Telemetry and analytics (local only, opt-in)

**Principles.**

- Off by default. The player opts in on first launch or in Settings.
- **No network code.** No personal data.
- Telemetry is read-only over the sim. It never touches `ctx.Rng` or mutates state, so replays and
  hashes are unaffected.

**Storage.** Newline-delimited JSON at `Application.persistentDataPath/telemetry/<session>.jsonl`.
Keep the last 20 sessions or 50 MB. Settings gets **View folder**, **Delete all** and **Export bug
bundle** (a zip with the telemetry, the last save, the replay log and the Unity log, attached by
the user).

| Group | Data |
|---|---|
| Session | Start and end, real time per speed level, pause time, settings and seed |
| Engagement | Powers cast per type, rejections by reason, selections, chronicle opens and jumps, overlay use, tutorial step completion and skips |
| Pacing | Sim year and real time at each age entry per civ, first tier-ups, first war, first collapse, Gate placed and completed |
| Health | FPS p50/p95/p99, `LastStepMs` p50/p95, per-system `AvgMs`, faults and disabled systems (`SimulationEngine.Profiles`) |
| Outcomes | Peak population, civs alive, final alignment, doctrines, objectives completed, deaths by cause |

`Core/History` (`HistoryRecorder`, `WorldStatistics`, `EraSummary`) already samples the world and
the civs deterministically. Telemetry should export those samples rather than recompute them. The
headless runner and Bench emit the same schema, so playtests and balance sweeps can be compared
directly.

---

## 20. Risks, open questions and code inconsistencies

### 20.1 Design risks

| Risk | Pillar | Mitigation |
|---|---|---|
| **Passivity.** Players feel they don't matter | P2 | Tight mana loop, objectives, doctrines that react to the god, visible aftermath |
| **Messy towns** | P4 | Desirability districts, roads, race layout, silhouette tests. ⚠ Producer over-building hurts readability |
| **Pacing** | P1 | ⚠ Currently 20× too fast. BALANCE §5.2 and §10 |
| **Race imbalance** | P1 | ⚠ Saurian and Orc snowball, Elf and Dwarf get trapped. BALANCE I-3 to I-5 |
| **Wrath is optimal** | P2 | Mana is tied to believers. But pious races gain devotion from wrath, and late mana floods (BALANCE §6) |
| **Chronicle spam** | P3 | Importance levels, cooldowns (famine, battle). ⚠ About 500–900 famine events per run |
| **Cross-platform float determinism** | Tech | ROADMAP R1 |

### 20.2 Open questions

- **Q1.** Dense late-age dwellings? `Arcology` has no housing, and `House` is fixed at 3–5 by
  contract. A 2,000-person Megalopolis needs 400–670 houses against a 600-building cap.
- **Q2.** Should the god have a fail state? Current answer: no. Low mana is the consequence.
- **Q3.** Should alignment change prices (§12.3)? Today it only drives doctrines and objectives.
- **Q4.** Rebellion. **Answered in code** (`Storytelling/Secession`). A Village+ settlement can break
  away and found its own civilization, keeping its knowledge, when all of these hold:
  - unrest ≥ 0.6 and happiness ≤ 0.3
  - it is at least 40 tiles from the capital, or ethnically different
  - it has at least 3 residents.

  The new state starts at −50 opinion with its parent. It is never the capital or the last
  settlement, and it can't happen while 48 civs are alive. Open: does a breakaway inherit the
  parent's doctrines?
- **Q5.** Regional weather. It needs either a contract change or a per-settlement Memory model
  (BALANCE I-11).
- **Q6.** A persisted per-civ "transcended" flag (contract) against `Memory["asc.transcended"]`.

### 20.3 Code and contract inconsistencies (for the lead)

| # | Inconsistency | Where |
|---|---|---|
| C1 | Mana cap. The contract `GodState.MaxMana` defaults to 500. `FaithSystem` replaces it with 300 + 0.5 × followers (cap 5,000). `DefaultComposition` seeds max(500, StartingMana) | `WorldState.cs`, `FaithSystem`, `DefaultComposition` |
| C2 | Starting mana. `GodState.Mana` defaults to 100. `SimConfig.StartingMana` is 120, but Sanitize falls back to 100 | `WorldState.cs`, `SimConfig.cs` |
| C3 | Command rate. `SimConfig.MaxCommandsPerTick` (4, validator) and `GodCommandSystem.MaxCommandsPerTick` (8, a hard-coded const) are separate limits | `SimConfig`, `GodCommandSystem` |
| C4 | Pacing. The contract says "ages last about 80–200 years". The data gives 2–15 years per age after Iron | `AgeCatalog`, `TechQueries.ResearchPerResident`, building Research outputs |
| C5 | Growth. The contract lists orc "fertility" as a strength. The data makes Saurians the fastest growers by far | Race files, `LifecycleBalance.SaurianClutch*` |
| C6 | Weather is world-global (`WeatherState`), but Rain and Drought are aimed with a radius | Contract, `GodPowerEffects` |
| C7 | `MinWorldAge` is Dawn for every power, so the gating field is unused | `GodPowerCatalog` |
| C8 | Services ignore inputs. Power Plant and Data Center give full service with no Fuel or Electronics | `ServiceSystem.ComputeServices` |
| C9 | The Transcendence capstone tech isn't required for the Ascension Gate, and the Gate is never placed by the AI in practice | `TechCatalog`, `BuildingsLate`, `CivilizationStrategySystem` |
| C10 | Enum comments against data: Farm says "Bronze" but `dawn.agriculture` unlocks it in Dawn for every race. War Idol says "Bronze" but is Dawn for orcs. Mill says "boosts farms" but is a stand-alone producer | `Enums.cs` comments, catalogs |
| C11 | The House costs only Wood, while the dwarf habitat is treeless hills and mountains | `BuildingsEarly.Dawn`, `DwarfRace` |
| C12 | The headless tool lives at `tools/GodSim.Headless`, but the contract says `tools/Headless/` | Contract §2 |
| C13 | The contract's module list (§2) and entry-point table (§6) omit modules that now exist and are wired by `DefaultComposition`: Heroes, History, Objectives, Religion, Storytelling (world events and secession), Geography, Modding and `Security.IntegritySystem`. The system-order line also omits them (they run after Diplomacy) | Contract §2 and §6, `DefaultComposition` |
| C14 | Hotkey double-handling. The demo scene adds both `GameShortcuts` and `GodSimHUD` (with `_handleTimeHotkeys`/`_handlePowerHotkeys` true). Both toggle pause on Space and change speed on +/−, and `GodPowerController` and the HUD both toggle powers on 1–0. Each press is applied twice | `GodSimMenu.RootComponents`, `GameShortcuts`, `GodSimHUD.HandleKeyboard`, `GodPowerController.HandleHotkeys` |

---

## 21. Glossary

| Term | Meaning |
|---|---|
| Tick | One sim day. 96 make a year |
| Civ | A civilization: one race, many settlements, one age |
| Tier | Settlement size class, Camp → Megalopolis (10/30/80/200/500/1,200) |
| Devotion | How much a civ worships the god (0–1). It drives mana |
| Favor | How a civ perceives the god (−1 smitten … +1 blessed) |
| Alignment | The god's moral position (−1 wrathful … +1 benevolent) |
| Doctrine | A civ's religious stance on one of 4 axes. It depends on the god's behavior |
| Chronicle | The bounded (2,000-entry) world history log |
| Governor | The settlement-level utility AI |
| Posture / Focus | The civ-level strategic stance (Peaceful, Defensive, Militarist) and development focus |
| StyleAge / StyleRace | The visual style stamp on each building, updated by renovation |
