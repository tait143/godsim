# GodSim — Balance

Targets, live numbers and the method for tuning GodSim. Companion to [GAME_DESIGN.md](GAME_DESIGN.md).

- **Live numbers** come from the catalogs and system constants on disk. They were read and dumped on
  2026-11-19, and every one names the file it came from. They go stale as soon as someone edits a
  catalog. Run `GodSim → Validate Content` or the content dump (§9.4) again before quoting them.
- **Targets** are the design intent. When a live number misses its band, the target wins unless
  design changes it here.
- **Measured** values come from headless runs of the simulation as it stood on 2026-11-19. They are
  marked ⚠ where they miss the target. These runs used a scratch composition equal to
  `DefaultComposition` minus `ReligionSystem`, `WorldEventSystem` and `IntegritySystem`, because
  those modules didn't compile yet. Seeds: 20261119, 42, 7. Settings: Standard, 192², all races, no
  god input.
- **Confirmed with the real composition.** Later the same day, the full `DefaultComposition` (all
  systems) ran through `tools/GodSim.Headless --seed 20261119 --years 120`. The pattern held:

  | Measure | Result |
  |---|---|
  | Saurian Ascension | year 50 |
  | World population | 40,000 cap at year 100 (only 3,822 buildings) |
  | Elves | still Bronze at year 100 (pop 36) |
  | Dwarves | Iron at year 80 (pop 50) |
  | Humans | Industrial at year 100 |
  | Speed | 7.5 ms/tick at the cap |

  Runs are deterministic for a given build. Identical output was verified on two runs.

---

## 1. Units and conversions

| Quantity | Rule |
|---|---|
| Time | 1 tick = 1 day. 1 season = 24 ticks. **1 year = 96 ticks** (`SimTime`) |
| "PerTick" rates | Per day. A system with `TickInterval = N` scales by `ctx.DeltaTicks = N` |
| Per-year chances | `Rng.PerYearToPerTicks(p, DeltaTicks)` |
| Yearly from per-tick | × 96 (e.g. 0.05 food/tick = 4.8 food/year) |
| Real time (Unity) | `GodSimRunner.SpeedLevels` = **1, 4, 16, 64 and 240 ticks/s**. That is 96 s, 24 s, 6 s, 1.5 s and 0.4 s per sim year |

**Session math.** A 1,000-year history at 64 ticks/s takes about 25 minutes. At 16 ticks/s it takes
about 1 hour 40 minutes. Most players will spend most of their time at 16–64 ticks/s.

---

## 2. Target curves

### 2.1 Age timing (first civilization to enter, Standard settings)

The contract target is that **each age lasts about 80–200 years**, so the full arc is about
1,000–1,300 years to Ascension, and the Gate opens around year 1,100–1,500.

| Age | Target entry year | Target years in age | Measured first entry (3 seeds) | Status |
|---|---|---|---|---|
| Dawn | 0 | 80–120 | 0 | — |
| Bronze | 80–120 | 90–140 | 8–13 | ⚠ about 10× too fast |
| Iron | 170–260 | 100–160 | 18–25 | ⚠ |
| Classical | 270–420 | 120–180 | 26–37 | ⚠ |
| Medieval | 390–600 | 140–200 | 30–48 | ⚠ |
| Renaissance | 530–800 | 120–180 | 34–57 | ⚠ |
| Industrial | 650–980 | 100–150 | 36–64 | ⚠ |
| Modern | 750–1,130 | 90–140 | 38–70 | ⚠ |
| Information | 840–1,270 | 80–120 | 40–75 | ⚠ |
| Ascension | 920–1,390 | until the Gate | **42–79** | ⚠ about 20× too fast |
| Gate complete | 1,000–1,500 | — | **never** (0 Gates placed in 96 years of Ascension, seed 42) | ⚠ blocker |

**Spread target.** At year 500, the leading civ should be 1–3 ages ahead of the trailing civ. That
creates the "Medieval kingdom next to a Bronze horde" drama.

**Measured spread.** It is extreme. The leader reaches Ascension by year 42–79. The elves and dwarves
sit in Dawn or Bronze for 40–200 years, then get conquered or die out (§2.2).

### 2.2 Population over time (per civilization)

**Targets (design):**

| Year | Human | Elf | Dwarf | Orc | Saurian | World (5 civs) |
|---|---|---|---|---|---|---|
| 25 | 30–45 | 18–25 | 20–30 | 35–55 | 30–50 | 150–200 |
| 100 | 80–140 | 30–50 | 45–80 | 100–180 | 90–160 | 400–600 |
| 250 | 200–350 | 70–120 | 120–200 | 250–450 | 220–400 | 0.9k–1.5k |
| 500 | 450–800 | 160–300 | 280–450 | 500–900 | 450–800 | 2k–3.2k |
| 1,000 | 900–1,600 | 400–700 | 600–1,000 | 900–1,600 | 900–1,500 | 4k–6k |

The world target at year 1,000 (about 5,000 people) is the contract performance budget (3 ms per
tick). `MaxPopulation` (40,000) should be a safety net that normal play never reaches.

**Measured (seed 20261119):**

| Year | Human | Elf | Dwarf | Orc | Saurian | World |
|---|---|---|---|---|---|---|
| 25 | 41 | 21 | 18 | 68 | **201** | 349 |
| 50 | 85 | 21 | 20 | 446 | **2,337** | 2,909 |
| 75 | 202 | 23 | 15 | 3,712 | **11,647** | 15,599 |
| 100 | 154 | conquered (y80) | 13 | 13,795 | 26,038 | **40,000 (cap)** ⚠ |

All three seeds show two failure modes:

1. **Runaway.** One or two civs (usually Saurian, then Orc) grow exponentially. The world hits the
   40,000 cap by year 90–100.
2. **Low-population trap.** Elves (pop 17–25), dwarves (13–22) and sometimes humans (21–30) stall
   below the Bronze `MinPopulation` of 25 for 40–130 years. Elves were conquered in every seed
   (years 70–80). Dwarves collapsed in 2 of 3 seeds.

**Fertility math (from the race files):**

| Race | Fertile window (adult, before elder) | Fertility/yr | Max children | Note |
|---|---|---|---|---|
| Saurian | 10–70 | 0.32 × clutch 1–3 | 8 | The **fastest grower** by a wide margin (earliest adulthood, longest window, clutches). It is not the orcs, as the contract table implies |
| Orc | 12–38 | 0.45 | 8 | |
| Human | 16–55 | 0.30 | 6 | |
| Dwarf | 30–150 | 0.10 | 4 | |
| Elf | 40–280 | 0.05 | 3 | About 1.5 surviving daughters per mother per 70-year generation, so growth is about 0.6%/yr. 14 → 25 takes about 100 years with perfect food and housing |

**Per-age `MinPopulation` problem.** The civ-wide gates are 25, 50, 90, 160, 260, 400, 600, 850 and
1,200 (`AgeCatalog.MinPopulations`). They are the same for every race. At the elf growth rate,
reaching 1,200 takes about 750 years. Recommendation: either use race-scaled gates (Elf ×0.5,
Dwarf ×0.7), or make the gate a share of habitable land. Either way it is applied in
`AgeAdvanceCheck`, so no contract change is needed.

### 2.3 Food per capita

| Item | Value | Source |
|---|---|---|
| Consumption | `RaceDefinition.FoodPerTick`: Human 0.05, Elf 0.04, Dwarf 0.05, Orc 0.06, Saurian 0.045. Children ×0.6, elders ×0.8 | Race files, `LifecycleBalance` |
| Average eater | About 0.045 food/tick, about 4.3 food/year | — |
| Seasonal yield | Spring 1.05, Summer 1.25, Autumn 1.10, **Winter 0.40**. Rainfall 0.8–1.2×, drought ×0.5, rain ×1.1, clamped 0.1–2.0 | `NaturalEffects` |

**Targets:**

- **Annual production at 1.10–1.25× consumption.** Below 1.05 the town starves in winter. Above 1.4
  the AI is wasting labour.
- **Entering winter (day 72), `econ.foodDays` ≥ 20.** Winter yields 0.4×, so a balanced town
  loses about 60% of a day's food for 24 days, about 14.4 days of stock.
- **Steady-state `econ.foodDays` of 30–90.** Spoilage is 0.15%/tick above 60% of capacity
  (`StorageSystem`).
- **Fewer than 1 `FamineEvent` per settlement per 50 years, and starvation under 2% of deaths.**
  Measured: starvation is under 0.1% of deaths ✓. FamineEvent counts are high (513–900 per run),
  which risks chronicle noise.

**People fed per worker** (output ÷ 0.05, before efficiency, terrain, race, age and tech modifiers):

| Source | Output/worker/tick | People fed per worker | Available |
|---|---|---|---|
| Forager (unemployed adult) | 0.035 (Dawn–Bronze), 0.015 later | 0.7 / 0.3 | always |
| Gatherer's Hut | 0.09 | 1.8 | Dawn–Iron |
| Hunting Lodge | 0.12 × hunting factor ≤1.2 | 2.4 | Dawn–Medieval |
| Fishing Hut | 0.15 × fishing factor ≤1.2 | 3.0 | Dawn+ |
| Farm | 0.22 × fertility | 4.4 | Bronze+ (Dawn with `dawn.agriculture`) |
| Pasture | 0.10 + 0.04 Cloth | 2.0 | Bronze+ |
| Mill (Laborer) | 0.12 | 2.4 | Medieval–Industrial |
| Harbor (Merchant) | 0.08 + 3 Commerce | 1.6 | Classical+ |
| Industrial Farm | 0.60 | 12 | Modern+ |
| Hydroponics Tower | 1.20 | 24 | Ascension |

**Target food-worker share:** 35–45% of the workforce in Dawn, 25–35% in Bronze–Medieval, under 10%
from Modern on.

### 2.4 Houses per capita (the dwelling rule)

| Metric | Target | Live rule | Measured |
|---|---|---|---|
| Houses per person | **0.20–0.33** (one per 3–5) | House `HousingMin`/`Max` = 3/5, validated by `GameDatabase` | Early towns 0.27–0.34 ✓. Late big civs: Saurian 0.078 (1 per 12.9), Orc 0.17 (1 per 5.7) ⚠ |
| Occupancy | 80–95% | Governor keeps free housing ≥ max(2, 8% of pop) and looks 0.25 years ahead | — |
| Homeless | < 5% of pop | Births drop ×0.4 when homeless > 25% (`LifecycleBalance`) | Late runaway civs at least 55–65% homeless (people ÷ house capacity) ⚠ |

Homelessness feeds plague. Crowding is pop ÷ (buildings + 1), so a housing deficit drives the
epidemic chance to its 8%/yr maximum. In seed 42, **disease caused 95% of all deaths** (12,809
disease against 561 old age).

### 2.5 Buildings per settlement tier

Tier thresholds (live, `SettlementGrowthSystem`): Hamlet 10, Village 30, Town 80, City 200,
Metropolis 500, Megalopolis 1,200. A settlement drops a tier below 80% of its threshold.
Territory radius by tier: 6, 8, 10, 13, 16, 20, 24.

| Tier | Population | Target buildings (houses + other) | Target houses | Measured example |
|---|---|---|---|---|
| Camp | 1–9 | 3–8 | 1–3 | Human camp, pop 7: 6 ✓ |
| Hamlet | 10–29 | 6–15 | 3–8 | Dwarf hamlet, pop 21: **43** (6 farms, 5 gatherer huts, 5 fishing huts, 5 quarries, 6 woodcutters) ⚠ |
| Village | 30–79 | 15–35 | 7–20 | Human village, pop 34: **63** (8 farms, 8 fishing huts, 8 quarries, 8 woodcutters) ⚠ |
| Town | 80–199 | 35–80 | 18–50 | Orc town, pop 115: 123 (17 hunting lodges, 19 woodcutters) ⚠ |
| City | 200–499 | 80–180 | 45–125 | — |
| Metropolis | 500–1,199 | 180–380 | 110–300 | Saurian, pop 736: 277, 203 complete houses ✓ |
| Megalopolis | 1,200+ | 380–600 (`MaxBuildingsPerSettlement`) | 250–400 | Cap risk: above about 2,000 people the 3–5 rule alone needs more than 400 houses |

**Job slots against the workforce.** Target: total job slots ≤ 1.2× working adults. Measured in
early villages: **3–6×** ⚠. The governor keeps queueing producers. The staff spreads thin
(efficiency = staffed skill ÷ slots), output per building falls, and every new building costs
Wood, which is the scarcest resource (§3.3).

---

## 3. Production chains

### 3.1 Rates per worker per tick (live, from the building catalog)

Effective output = `OutputsPerWorker × slots × efficiency × terrain × Modifiers.Production[res] ×
SimConfig.ProductionMultiplier × weather (food only) × DeltaTicks`. The parts:

- **Efficiency** = Σ(skill × (0.8 + 0.4 × experience)) ÷ slots × condition × power bonus. The power
  bonus is 1.25 for Industry with Power coverage ≥ 1. The result is clamped at ≤ 1.5.
- **Terrain factors** (`ProductionTerrain`):

| Factor | Formula |
|---|---|
| Wood | min(1.2, 0.3 + 0.7·trees/16), radius 4 |
| Deposits | min(1.2, 0.7 + 0.1·n), radius 3 |
| Fishing | min(1.2, 0.35 + 0.65·water/12), radius 3 |
| Hunting | min(1.2, 0.3 + 0.7·trees/30), radius 5 |

- **Depletion.** Deposits hold 250 per tile. Trees hold 60 wood per tile and regrow 2/yr up to 160.

| Chain | Building (job × slots) | In per worker·tick | Out per worker·tick | Ratio note |
|---|---|---|---|---|
| Wood | Woodcutter's Camp (Woodcutter ×2) | — | 0.12 Wood | Sawmill (Laborer ×3) 0.25 |
| Stone | Stone Quarry (Quarrier ×3) | — | 0.08 Stone | |
| Ore | Mine (Miner ×3) | — | 0.07 Ore | |
| Metal | Smelter (Smith ×2) | 0.06 Ore | 0.05 Metal | 1 miner feeds 1.17 smelters |
| Metal (late) | Foundry (Laborer ×4) | 0.06 Ore + 0.03 Fuel | 0.09 Metal | 1.5 metal per ore |
| Tools | Blacksmith (Smith ×2) | 0.04 Metal + 0.02 Wood | 0.04 Tools | 1 smith ← 0.8 smelter ← 0.7 miner + 0.17 woodcutter |
| Weapons | Armory (Smith ×2) | 0.04 Metal + 0.02 Wood | 0.03 Weapons | Soldiers want 0.5 Weapons each for ×1.3 strength (`MilitaryMath`) |
| Gold | Gold Mine (Miner ×3) | — | 0.03 Gold | Also Bank 0.04, Grand Bazaar 0.06, and Commerce ×0.02 gold per point per tick |
| Cloth | Weaver (Weaver ×2) | — | 0.05 Cloth | Pasture 0.04 as a by-product |
| Goods | Workshop (Craftsman ×3) | 0.03 Wood + 0.02 Cloth | 0.04 Goods | Printworks: 0.02 Wood → 0.02 Goods (+1.5 Research) |
| Machinery and Goods | Factory (Laborer ×6) | 0.05 Metal + 0.04 Fuel | 0.05 Machinery + 0.04 Goods | |
| Fuel | Coal Mine (Miner ×4) / Oil Well (Engineer ×2) | — | 0.10 / 0.18 Fuel | |
| Electronics | Electronics Plant (Engineer ×4) | 0.04 Metal + 0.01 Gold | 0.04 Electronics | |
| Power (service) | Power Plant (Engineer ×3) | 0.05 Fuel | 20 Power per building | ⚠ The service doesn't depend on the input (§10, I-7) |
| Research (service) | Data Center (Engineer ×4) | 0.01 Electronics | 30 Research per building | ⚠ Same issue |

### 3.2 Services: per-capita demand and building output

Coverage = output ÷ (population × need), clamped 0–3 (`ServiceSystem`, `ServiceNeeds`).

| Service | Need per capita/tick | Example sources (points/tick, full staff) |
|---|---|---|
| Faith | 0.05 | Totem 0.5, Shrine 1, Temple 3, Monastery 3, Cathedral 8, Grand Cathedral 20, Gate 50 |
| Health | 0.05 | Herbalist 1.5, Bathhouse 3, Hospital 8, Medical Center 25 |
| Research | 0.03 | Town Center 0.2, Scriptorium 1.2, Library 3, Observatory 4, University 7, Research Lab 15, Great Library 25, Data Center 30 |
| Entertainment | 0.05 | Campfire 0.8, Tavern 3, Theater 4, Arena 6, Stadium 15 |
| Defense | 0.04 | Palisade 2, Watchtower 3, Stone Wall 4, Castle 20, Military Base 60, plus 1.5 per soldier |
| Water | 0.10 | Well 3, Bathhouse 2, Aqueduct 10, Arcology 20 |
| Power | 0.15 | Power Plant 20, Fusion Reactor 80 |
| Commerce | 0.04 | Market 2, Harbor 3, Guildhall 3, Bank 5, Rail 8, Airport 20 |
| Administration | 0.04 | Town Center 2, Forum 6, Castle 3, Arcology 20 |

**Target:** coverage of 0.8–1.2 for the services a tier expects (GDD §8.2). Coverage above 2 means
wasted buildings.

### 3.3 Scarcity targets

| Resource | Target | Observation |
|---|---|---|
| Wood | The universal building material. Stock ≥ 30 in a growing town | ⚠ Measured at 0–8 in most growing settlements, even with 8–19 woodcutter camps. **The House costs only Wood (12)**, so dwarves (treeless hills: `trees r6 = 0` at their capital) can't house growth. That is a direct cause of the low-population trap |
| Stone | A secondary limit from Iron on | OK |
| Gold | A Classical+ gate: Library 10, Forum 15, Arena 20 | Commerce gold is 0.02 per point. One market gives about 3.8 gold/year. Civs without a gold deposit progress slowly. Acceptable, but watch it |
| Electronics and Machinery | Ascension Gate inputs (600 each) | Gate never placed. See §10, I-1 |

---

## 4. Building cost and age tables (live catalog summary)

These tables were generated from `BuildingCatalog.Register` (all 75 types). Notes:

- Cost is paid **in full when construction starts** (`Stockpile.TryPay`). It must fit in one
  settlement's stockpile, which has a single capacity shared by each resource: base 150, plus
  building `StorageCapacity`, times `Modifiers.Storage`.
- A site left unpaid for more than 3 years is abandoned.
- Work is in builder-ticks.
- "Avail" shows the age when the building goes obsolete (–MaxAge) and any race restriction.
- Tech early unlocks are listed after the tables.
- Abbreviations: Wd Wood, St Stone, Or Ore, Mt Metal, Au Gold, Tl Tools, Cl Cloth, Gd Goods,
  Wp Weapons, Fu Fuel, Mc Machinery, El Electronics, Fd Food.

#### Dawn

| Building | Cat | Avail | Tier / pop | Size | Cost | Work | Jobs | In → Out per worker·tick | Services /tick | Storage | Upkeep Au/tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| TownCenter (max 1) | Civic | ✓ | Camp | 2×2 | 40 Wd, 20 St | 60 | - | - | 2 Admin, 1 Def, 0.2 Res, 0.2 Faith | 100 | - |
| House (houses 3-5) | Housing | ✓ | Camp | 1×1 | 12 Wd | 18 | - | - | - | - | - |
| Well | Infrastructure | ✓ | Camp / 5 | 1×1 | 8 St, 4 Wd | 12 | - | - | 3 Water | - | - |
| Storehouse | Storage | ✓ | Camp / 8 | 2×1 | 20 Wd, 6 St | 24 | - | - | - | 120 | - |
| GathererHut | Food | –Iron | Camp | 1×1 | 8 Wd | 10 | Gatherer ×3 | 0.09 Fd | - | - | - |
| HuntingLodge | Food | –Medieval | Camp | 1×1 | 10 Wd, 2 St | 12 | Hunter ×2 | 0.12 Fd | - | - | - |
| FishingHut | Food | ✓ | Camp | 1×1 | 10 Wd | 12 | Fisher ×2 | 0.15 Fd | - | - | - |
| WoodcutterCamp | Resource | ✓ | Camp | 1×1 | 8 Wd | 12 | Woodcutter ×2 | 0.12 Wd | - | - | - |
| StoneQuarry | Resource | ✓ | Camp | 2×2 | 14 Wd | 20 | Quarrier ×3 | 0.08 St | - | - | - |
| SpiritTotem | Religion | –Iron | Camp / 5 | 1×1 | 10 Wd, 4 St | 14 | - | - | 0.5 Faith | - | - |
| Campfire | Culture | –Classical | Camp | 1×1 | 6 Wd | 6 | - | - | 0.8 Ent | - | - |

#### Bronze

| Building | Cat | Avail | Tier / pop | Size | Cost | Work | Jobs | In → Out per worker·tick | Services /tick | Storage | Upkeep Au/tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Farm | Food | ✓ | Camp | 3×3 | 20 Wd, 5 St | 25 | Farmer ×4 | 0.22 Fd | - | - | - |
| Pasture | Food | ✓ | Camp / 8 | 3×3 | 18 Wd | 20 | Herder ×2 | 0.1 Fd, 0.04 Cl | - | - | - |
| Granary (max 3) | Storage | ✓ | Camp / 15 | 2×2 | 25 Wd, 20 St | 35 | - | - | - | 150 | - |
| Mine | Resource | ✓ | Camp / 8 | 2×2 | 20 Wd, 10 St | 30 | Miner ×3 | 0.07 Or | - | - | - |
| Smelter | Industry | ✓ | Camp / 10 | 2×1 | 15 Wd, 25 St | 35 | Smith ×2 | 0.06 Or → 0.05 Mt | - | - | - |
| Shrine | Religion | ✓ | Camp / 10 | 1×1 | 20 St, 10 Wd, 2 Mt | 30 | Priest ×1 | - | 1 Faith | - | - |
| Herbalist | Health | ✓ | Camp / 12 | 1×1 | 14 Wd, 6 St | 18 | Healer ×1 | - | 1.5 Health | - | - |
| Weaver | Industry | ✓ | Camp / 12 | 1×1 | 15 Wd, 5 St, 2 Mt | 20 | Weaver ×2 | 0.05 Cl | - | - | - |
| Palisade | Military | –Medieval | Hamlet / 20 | 3×1 | 30 Wd | 30 | - | - | 2 Def | - | - |
| DeepForge (wonder) | Wonder | Dwarf only | Village | 3×3 | 120 Wd, 300 St | 600 | Smith ×6 | 0.05 Or → 0.08 Mt, 0.03 Tl | 6 Def | - | 0.03 |
| WarIdol (wonder) | Wonder | Orc only | Hamlet | 2×2 | 180 Wd, 120 St | 300 | - | - | 10 Def, 4 Faith | - | 0.03 |

#### Iron

| Building | Cat | Avail | Tier / pop | Size | Cost | Work | Jobs | In → Out per worker·tick | Services /tick | Storage | Upkeep Au/tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Blacksmith | Industry | ✓ | Camp / 15 | 2×1 | 20 Wd, 20 St, 8 Mt | 40 | Smith ×2 | 0.04 Mt, 0.02 Wd → 0.04 Tl | - | - | - |
| Temple (max 2) | Religion | ✓ | Hamlet / 25 | 2×2 | 60 St, 30 Wd, 10 Mt, 5 Tl | 80 | Priest ×2 | - | 3 Faith | - | - |
| Barracks (max 2) | Military | ✓ | Hamlet / 30 | 2×2 | 40 Wd, 30 St, 10 Mt, 4 Tl | 60 | Soldier ×6 | - | 2 Def | - | 0.01 |
| Market (max 2) | Commerce | ✓ | Village / 40 | 2×2 | 40 Wd, 30 St, 6 Mt, 3 Tl | 55 | Merchant ×2 | - | 2 Com | - | 0.01 |
| Watchtower | Military | –Industrial | Camp / 15 | 1×1 | 20 Wd, 25 St, 4 Mt | 35 | Soldier ×1 | - | 3 Def | - | - |
| Armory | Industry | ✓ | Hamlet / 30 | 2×1 | 25 Wd, 25 St, 10 Mt, 4 Tl | 45 | Smith ×2 | 0.04 Mt, 0.02 Wd → 0.03 Wp | - | - | - |
| GoldMine | Resource | ✓ | Camp / 15 | 2×2 | 25 Wd, 15 St, 5 Mt, 4 Tl | 40 | Miner ×3 | 0.03 Au | - | - | - |
| Scriptorium | Education | ✓ | Hamlet / 30 | 2×1 | 30 Wd, 30 St, 4 Mt, 3 Tl | 50 | Scholar ×2 | - | 1.2 Res | - | - |
| WorldTree (wonder) | Wonder | Elf only | Village | 3×3 | 250 Wd, 60 St, 60 Cl | 600 | - | - | 8 Faith, 6 Res, 6 Health | - | 0.04 |
| SunPyramid (wonder) | Wonder | Saurian only | Village | 3×3 | 60 Wd, 300 St, 40 Mt | 700 | - | - | 10 Faith, 5 Res | - | 0.05 |

#### Classical

| Building | Cat | Avail | Tier / pop | Size | Cost | Work | Jobs | In → Out per worker·tick | Services /tick | Storage | Upkeep Au/tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Library | Education | ✓ | Village / 25 | 2×2 | 30 Wd, 40 St, 5 Cl, 10 Au | 140 | Scholar ×3 | - | 3 Res | - | 0.02 |
| Aqueduct | Infrastructure | ✓ | Village / 30 | 1×3 | 60 St, 10 Wd, 5 Tl | 160 | - | - | 10 Water | - | 0.02 |
| Bathhouse | Health | ✓ | Village / 20 | 2×2 | 35 St, 15 Wd, 4 Tl | 120 | Healer ×1 | - | 3 Health, 2 Water | - | 0.02 |
| Theater | Culture | ✓ | Village / 25 | 2×2 | 25 Wd, 35 St, 8 Cl | 130 | Entertainer ×2 | - | 4 Ent | - | 0.02 |
| Harbor (max 2) | Commerce | ✓ | Village / 20 | 2×2 | 60 Wd, 20 St, 6 Tl, 6 Cl | 180 | Merchant ×3 | 0.08 Fd | 3 Com | 100 | 0.03 |
| Workshop | Industry | –Modern | Hamlet / 12 | 2×2 | 30 Wd, 15 St, 8 Tl | 100 | Craftsman ×3 | 0.03 Wd, 0.02 Cl → 0.04 Gd | - | 30 | 0.015 |
| Forum (max 1) | Civic | ✓ | Village / 30 | 3×2 | 60 St, 20 Wd, 15 Au | 180 | Administrator ×2 | - | 6 Admin | - | 0.03 |
| Arena | Culture | ✓ | Village / 45 | 3×3 | 90 St, 30 Wd, 8 Tl, 20 Au | 260 | Entertainer ×3 | - | 6 Ent | - | 0.04 |
| StoneWall (max 24) | Military | –Industrial | Hamlet / 10 | 1×1 | 30 St, 2 Tl, 2 Au | 100 | - | - | 4 Def | - | 0.01 |
| GreatLibrary (wonder) | Wonder | ✓ | Town / 80 | 3×3 | 400 St, 150 Wd, 120 Au, 40 Cl, 40 Gd, 30 Tl | 750 | Scholar ×4 | - | 25 Res | - | 0.05 |
| Colossus (wonder) | Wonder | ✓ | Town / 70 | 3×3 | 350 St, 200 Mt, 50 Tl, 120 Au | 700 | - | - | 20 Com | - | 0.05 |

#### Medieval

| Building | Cat | Avail | Tier / pop | Size | Cost | Work | Jobs | In → Out per worker·tick | Services /tick | Storage | Upkeep Au/tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Castle (max 1) | Military | –Renaissance | Village / 40 | 3×3 | 200 St, 60 Wd, 20 Mt, 15 Tl, 30 Au | 400 | Soldier ×10 | - | 20 Def, 3 Admin | 80 | 0.05 |
| Cathedral (max 1) | Religion | ✓ | Town / 60 | 3×3 | 180 St, 50 Wd, 40 Au, 10 Gd, 10 Cl | 380 | Priest ×4 | - | 8 Faith | - | 0.04 |
| Monastery | Religion | ✓ | Hamlet / 15 | 2×2 | 40 Wd, 50 St, 5 Tl | 170 | Priest ×3 | - | 3 Faith, 1.5 Res | - | 0.02 |
| Mill | Food | –Industrial | Hamlet / 10 | 2×2 | 45 Wd, 20 St, 6 Tl | 120 | Laborer ×2 | 0.12 Fd | - | 40 | 0.015 |
| Guildhall | Commerce | ✓ | Village / 30 | 2×2 | 40 Wd, 40 St, 15 Au, 5 Gd | 170 | Merchant ×2 | - | 3 Com | - | 0.02 |
| Tavern | Culture | ✓ | Hamlet / 10 | 2×2 | 35 Wd, 10 St, 4 Gd | 80 | Merchant ×1 | - | 3 Ent, 1 Com | 20 | 0.01 |
| GrandCathedral (wonder) | Wonder | Human only | Town | 3×3 | 150 Wd, 400 St, 120 Au, 40 Tl, 60 Gd | 700 | - | - | 20 Faith, 5 Ent | - | 0.06 |
| GrandBazaar (wonder) | Wonder | ✓ | Town / 90 | 3×3 | 300 St, 250 Wd, 100 Cl, 80 Gd, 150 Au | 850 | Merchant ×4 | 0.06 Au | 25 Com | 200 | 0.05 |

#### Renaissance

| Building | Cat | Avail | Tier / pop | Size | Cost | Work | Jobs | In → Out per worker·tick | Services /tick | Storage | Upkeep Au/tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| University (max 1) | Education | ✓ | Town / 60 | 3×2 | 120 St, 40 Wd, 40 Au, 15 Gd, 10 Cl | 320 | Scholar ×5 | - | 7 Res | - | 0.05 |
| Bank | Commerce | ✓ | Town / 60 | 2×2 | 80 St, 20 Wd, 10 Mt, 50 Au | 220 | Merchant ×2 | 0.04 Au | 5 Com | 50 | 0.03 |
| Observatory | Education | ✓ | Village / 35 | 2×2 | 60 St, 20 Wd, 15 Mt, 10 Tl, 5 Gd | 200 | Scholar ×2 | - | 4 Res | - | 0.03 |
| Hospital | Health | ✓ | Village / 40 | 2×2 | 70 St, 30 Wd, 20 Cl, 10 Tl, 20 Au | 240 | Healer ×4 | - | 8 Health | - | 0.04 |
| Printworks | Education | ✓ | Village / 30 | 2×2 | 40 Wd, 30 St, 20 Mt, 12 Tl | 200 | Craftsman ×2 | 0.02 Wd → 0.02 Gd | 1.5 Res | 20 | 0.02 |

#### Industrial

| Building | Cat | Avail | Tier / pop | Size | Cost | Work | Jobs | In → Out per worker·tick | Services /tick | Storage | Upkeep Au/tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| Factory (max 4) | Industry | ✓ | Village | 3×3 | 60 Wd, 80 St, 60 Mt, 40 Tl | 400 | Laborer ×6 | 0.05 Mt, 0.04 Fu → 0.05 Mc, 0.04 Gd | - | - | 0.08 |
| CoalMine (max 4) | Resource | ✓ | Village | 2×2 | 50 Wd, 30 St, 20 Mt, 20 Tl | 250 | Miner ×4 | 0.1 Fu | - | - | 0.05 |
| Sawmill (max 3) | Resource | ✓ | Village | 2×2 | 40 Wd, 30 St, 15 Mt, 20 Tl | 200 | Laborer ×3 | 0.25 Wd | - | - | 0.04 |
| RailStation (max 1) | Commerce | ✓ | Town | 3×3 | 60 Wd, 80 St, 60 Mt, 20 Tl, 20 Fu | 450 | Merchant ×3 | - | 8 Com | 60 | 0.08 |
| Foundry (max 3) | Industry | ✓ | Village | 3×3 | 30 Wd, 100 St, 30 Mt, 25 Tl, 15 Fu | 350 | Laborer ×4 | 0.06 Or, 0.03 Fu → 0.09 Mt | - | - | 0.07 |

#### Modern

| Building | Cat | Avail | Tier / pop | Size | Cost | Work | Jobs | In → Out per worker·tick | Services /tick | Storage | Upkeep Au/tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| PowerPlant (max 2) | Infrastructure | ✓ | Town | 3×3 | 120 St, 80 Mt, 20 Tl, 30 Fu, 30 Mc | 500 | Engineer ×3 | 0.05 Fu → - | 20 Power | - | 0.12 |
| OilWell (max 4) | Resource | ✓ | Village | 2×2 | 30 St, 60 Mt, 30 Tl | 250 | Engineer ×2 | 0.18 Fu | - | - | 0.06 |
| IndustrialFarm (max 8) | Food | ✓ | Village | 3×3 | 40 Wd, 30 Mt, 15 Tl, 20 Mc | 200 | Farmer ×4 | 0.6 Fd | - | - | 0.05 |
| ResearchLab (max 2) | Education | ✓ | Town | 2×2 | 90 St, 50 Mt, 20 Tl, 15 Mc | 400 | Scholar ×6 | - | 15 Res | - | 0.1 |
| Stadium (max 1) | Culture | ✓ | Town / 100 | 3×3 | 40 Wd, 200 St, 80 Mt, 15 Mc | 600 | Entertainer ×4 | - | 15 Ent | - | 0.1 |
| MilitaryBase (max 1) | Military | ✓ | Town | 3×3 | 150 St, 150 Mt, 40 Tl, 40 Fu, 30 Mc | 550 | Soldier ×20 | - | 60 Def | - | 0.15 |

#### Information

| Building | Cat | Avail | Tier / pop | Size | Cost | Work | Jobs | In → Out per worker·tick | Services /tick | Storage | Upkeep Au/tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| DataCenter (max 2) | Education | ✓ | Town | 2×2 | 60 St, 80 Mt, 40 Au, 30 Mc, 40 El | 450 | Engineer ×4 | 0.01 El → - | 30 Res | - | 0.14 |
| ElectronicsPlant (max 2) | Industry | ✓ | Town | 3×3 | 80 St, 100 Mt, 30 Au, 30 Tl, 40 Mc | 500 | Engineer ×4 | 0.04 Mt, 0.01 Au → 0.04 El | - | - | 0.12 |
| Airport (max 1) | Commerce | ✓ | City / 200 | 3×3 | 250 St, 150 Mt, 80 Au, 60 Fu, 60 Mc, 30 El | 800 | Merchant ×6 | - | 20 Com | - | 0.18 |
| MedicalCenter (max 1) | Health | ✓ | Town | 2×2 | 120 St, 60 Mt, 50 Au, 20 Mc, 25 El | 500 | Healer ×6 | - | 25 Health | - | 0.14 |

#### Ascension

| Building | Cat | Avail | Tier / pop | Size | Cost | Work | Jobs | In → Out per worker·tick | Services /tick | Storage | Upkeep Au/tick |
|---|---|---|---|---|---|---|---|---|---|---|---|
| HydroponicsTower (max 6) | Food | ✓ | Town | 2×2 | 60 St, 120 Mt, 40 Au, 40 Mc, 30 El | 450 | Engineer ×3 | 1.2 Fd | - | - | 0.1 |
| FusionReactor (max 2) | Infrastructure | ✓ | Town | 3×3 | 200 St, 300 Mt, 150 Au, 120 Mc, 100 El | 800 | Engineer ×4 | - | 80 Power | - | 0.2 |
| Arcology (max 2) | Civic | ✓ | City / 250 | 3×3 | 500 St, 400 Mt, 200 Au, 60 Tl, 150 Mc, 120 El | 800 | Administrator ×6 | - | 20 Admin, 10 Ent, 20 Water | 150 | 0.2 |
| AscensionGate (wonder) | Wonder | ✓ | Town / 250 | 4×4 | 500 Wd, 900 St, 900 Mt, 600 Au, 400 Tl, 300 Fu, 600 Mc, 600 El | 3000 | - | - | 50 Faith, 50 Research | - | 0.2 |

**Tech early unlocks** (`TechCatalog`, a building available before its MinAge):

| Age of tech | Tech → building |
|---|---|
| Dawn | `dawn.agriculture` → Farm (every race) · `dawn.herbal_lore` → Herbalist · `dawn.deep_delving` → Mine (Dwarf) · `dawn.warbands` → War Idol (Orc) |
| Bronze | `bronze.iron_working` → Blacksmith · `bronze.sylvan_lore` → World Tree (Elf) · `bronze.star_charts` → Sun Pyramid (Saurian) |
| Iron | `iron.philosophy` → Library · `iron.engineering` → Aqueduct |
| Classical | `classical.monasticism` → Monastery · `classical.chivalric_orders` → Grand Cathedral (Human) |
| Medieval | `medieval.scholasticism` → University · `medieval.banking` → Bank |
| Renaissance | `renaissance.steam_power` → Coal Mine |
| Industrial | `industrial.electricity` → Power Plant |
| Modern | `modern.computing` → Electronics Plant |
| Information | `information.vertical_farming` → Hydroponics · `information.fusion_power` → Fusion Reactor |

**Cost curve sanity check.** Cost and BuildWork rise smoothly. Dwellings are 12 Wd and 18 work.
Typical producers cost 20–50 materials early and 150–400 late. Wonders cost 300–1,100 materials
plus 300–850 work. The Gate costs 4,800 materials and 3,000 work.

**Two anomalies:**

1. The Late catalog sets `MinPopulation = 0` almost everywhere and gates on `MinTier`. The Early
   and Middle catalogs use both. This is harmless, but inconsistent for anyone reading the data.
2. Mill is a stand-alone food producer (0.12 per laborer). The enum comment says it "boosts farms".

---

## 5. Tech cost per age against research output

### 5.1 Live tables

**Research per tick** = Σ over settlements of (ServiceOutput[Research] + 0.004 × population),
times `Modifiers.Research` (race × (1 + age bonus) × (1 + Σ tech research bonuses)), times
`SimConfig.ResearchMultiplier`. Sources: `TechQueries.ResearchPerTick`, `ResearchPerResident =
0.004`.

| Age | Threshold (cumulative) | Window to next | Techs (general) | Σ cost, general | Required techs | Age research bonus |
|---|---|---|---|---|---|---|
| Dawn | 0 | 400 | 10 (8) | 162 | — | 0 |
| Bronze | 400 | 1,000 | 10 (8) | 455 | 5 | +4% |
| Iron | 1,400 | 1,800 | 10 (8) | 780 | 5 | +9% |
| Classical | 3,200 | 3,000 | 10 (8) | 1,260 | 5 | +15% |
| Medieval | 6,200 | 4,300 | 10 (8) | 1,790 | 5 | +22% |
| Renaissance | 10,500 | 6,000 | 9 (9) | 2,930 | 5 | +30% |
| Industrial | 16,500 | 8,500 | 9 (9) | 4,220 | 5 | +40% |
| Modern | 25,000 | 11,000 | 9 (9) | 5,460 | 5 | +53% |
| Information | 36,000 | 14,000 | 9 (9) | 6,950 | 5 | +68% |
| Ascension | 50,000 | (14,000) | 8 (8) | 10,050 (capstone 2,400) | 5 | +88% |

Population and settlement gates per age: 0/1, 25/1, 50/1, 90/2, 160/2, 260/3, 400/3, 600/4, 850/4
and 1,200/5.

The tech catalog is internally consistent. Each age's tech costs come to about 40–70% of its window
(`TechTreeReport.CostToWindowRatio`). **The problem is output against thresholds:**

| Age | Implied target research/yr (window ÷ target years) | Typical measured research/yr in that age | Measured years in age |
|---|---|---|---|
| Dawn | 3–5 | 20–37 | 8–13, or stuck behind the population gate for 35–200 years |
| Bronze | 7–11 | 70–100 | 7–14 |
| Iron | 11–18 | 130–650 | 8–26 |
| Classical | 17–25 | 500–1,000 | 4–13 |
| Medieval | 22–31 | 500–1,200 | 4–22 |
| Renaissance | 33–50 | 1,000–2,200 | 2–16 |
| Industrial → Information | 57–175 | 1,100–7,600 | 2–5 each |
| Ascension | — | 30,000–180,000 | — |

From Iron on, **research outruns the thresholds by 10–50×**. The ages are then gated only by
`MinPopulation`/`MinSettlements`, and the runaway civs clear those within a few years (§2.2).

### 5.2 Recommendation (owners: Progression, with Economy for building output)

1. **Cut raw research output by about 10×.** Set `ResearchPerResident` 0.004 → 0.0004. Scale
   building Research services ×0.1–0.2 (Scriptorium 1.2 → 0.15, Library 3 → 0.4, …, Data Center
   30 → 4). Keep the ratios between buildings.
2. **Then multiply the thresholds and tech costs by an age curve**, roughly ×1, ×1.5, ×2, ×3, ×4,
   ×5, ×6, ×7, ×8, ×9 from Dawn to Ascension. This makes late ages need sustained research
   investment, not one lucky building.
3. **Stack tech research multipliers additively per age, not across all ages.** By Ascension,
   `Modifiers.Research` reaches about ×6–7. That is the main reason late ages pass in 2–4 years.
4. **Quick check before any data change:** run the headless sweep (§9) with
   `ResearchMultiplier = 0.05` (and `GrowthMultiplier = 0.35`). See §9.5 for the measured effect.

---

## 6. Mana economy

**Live formulas** (`FaithSystem`, `GodCommandSystem`, `GodPowerCatalog`):

- Regen per tick = Σ over civs [ Σ Faith service × Devotion × 0.02 × `Modifiers.Faith` +
  population × Devotion × 0.0004 ] × `ManaRegenMultiplier`. It is applied every 6 ticks.
- **MaxMana** = 300 + 0.5 × followers, capped at 5,000.
  - This differs from the contract default `GodState.MaxMana = 500`. The composition seeds
    max(500, StartingMana), and the first FaithSystem run overwrites it.
  - `ObjectiveSystem` rewards can raise it further.
- Starting mana is `SimConfig.StartingMana` = 120. The contract default `GodState.Mana` is 100, and
  Sanitize falls back to 100 on NaN.
- Devotion drifts toward 0.3 + 0.5 × average settlement faith + 0.2 × GodFavor, closing 0.2% of the
  gap per tick. Starting devotion is 0.4 + 0.4 × piety (`SeedCivilization`).
- **Mortal reactions to a cast.**
  - Blessings: favor +0.05 to +0.15 and devotion +0.02 to +0.05, scaled by mana cost ÷ 150.
  - Wrath: favor −0.10 to −0.20. Races with **Piety > 0.6 (Elf 0.75, Saurian 0.9)** gain
    devotion from fear.
  - Shaping and Creation: no reaction.

| # | Power | Category | Mana | Cooldown (days) | Radius | Alignment Δ |
|---|---|---|---|---|---|---|
| 0 | Bless | Blessing | 20 | 24 | 3–10 | +0.03 |
| 1 | Rain | Blessing | 15 | 12 | 4–12 | +0.01 |
| 2 | Fertility | Blessing | 30 | 48 | 3–8 | +0.03 |
| 3 | Inspire | Blessing | 40 | 96 | 2–8 (needs a settlement in range) | +0.02 |
| 4 | Heal | Blessing | 25 | 24 | 2–8 | +0.03 |
| 5 | Bountiful Harvest | Blessing | 30 | 48 | 3–8 | +0.02 |
| 6 | Prophet | Blessing | 80 | 480 | 0–4 (needs a settlement in range) | +0.04 |
| 7 | Raise Land | Shaping | 10 | 2 | 1–6 | 0 |
| 8 | Lower Land | Shaping | 10 | 2 | 1–6 | 0 |
| 9 | Plant Forest | Shaping | 8 | 4 | 1–6 | +0.005 |
| 10 | Reveal Ore | Shaping | 35 | 96 | 1–5 | 0 |
| 11 | Spawn Tribe | Creation | 150 | 960 | 0 (land target, race parameter) | +0.05 |
| 12 | Lightning | Wrath | 5 | 1 | 0–1 | −0.01 |
| 13 | Wildfire | Wrath | 25 | 24 | 1–5 | −0.04 |
| 14 | Earthquake | Wrath | 60 | 192 | 3–8 | −0.06 |
| 15 | Meteor | Wrath | 120 | 480 | 2–6 | −0.08 |
| 16 | Plague | Wrath | 70 | 288 | 2–8 | −0.08 |
| 17 | Flood | Wrath | 50 | 192 | 3–8 | −0.05 |
| 18 | Drought | Wrath | 45 | 192 | 4–12 | −0.04 |

`MinWorldAge` is **Dawn for all 19 powers**, so nothing is gated by age yet. The design target in
GDD §11 was to gate Meteor at Iron.

**Cost tiers as shipped:**

| Tier | Mana | Powers |
|---|---|---|
| T1 | 5–20 | Lightning, Plant Forest, Raise Land, Lower Land, Rain, Bless |
| T2 | 25–45 | Heal, Wildfire, Fertility, Bountiful Harvest, Reveal Ore, Inspire, Drought |
| T3 | 50–80 | Flood, Earthquake, Plague, Prophet |
| T4 | 120–150 | Meteor, Spawn Tribe |

**Regen targets against measured values (no casting, Standard):**

| Phase | Target regen/yr | Target "Bless-equivalents"/yr | Measured regen/yr | Measured MaxMana |
|---|---|---|---|---|
| Dawn (y0–25) | 15–30 | 1–1.5 | 7–22 | 380–470 |
| Bronze–Iron | 40–80 | 2–4 | 13–50 | 450–600 |
| Classical–Renaissance | 100–200 | 5–10 | 86–480 | 0.8k–3.7k |
| Industrial+ | 200–400 | 10–20 | 700–1,300 ⚠ | 5,000 (cap) ⚠ |

**Interpretation.**

- The early game is tight, as intended. At 4–16 ticks/s, one Bless costs about 1 year of regen, or
  25–100 real seconds. Starting mana covers the first 6 Blesses.
- The late game is **flooded**. The pool sits at the 5,000 cap with more than 1,000/yr regen, which
  is about 8 Meteors a year. This is a symptom of the runaway population (followers drive both
  regen and MaxMana).

**Target fixes:**

- Cap `MaxManaPerFollower` growth with diminishing returns: 300 + 60·√followers, capped at 2,000.
- Make the follower term logarithmic.
- Scale wrath costs with world age (×1 Dawn … ×2 Ascension). That keeps late Meteor a
  decision, not spam.

---

## 7. War frequency

**Live rules** (`RelationsTuning`):

- **Opinion drivers:**
  - affinity ×30
  - trade +15
  - shared enemy +10
  - shared favor +8
  - border friction −10 per close pair, capped at −30
  - xenophobia ×20
  - recent war −25 for 20 years
- **Stances:**

| Stance | Enter | Leave |
|---|---|---|
| Allied | ≥ 60 | < 45 |
| Friendly | ≥ 25 | < 15 |
| Hostile | below −25 | — |

- **Declaring war:**
  - *Hatred war:* opinion ≤ −40 and aggression ≥ 0.35.
  - *Opportunist war:* aggression ≥ 0.6, opinion ≤ −10 and strength ratio ≥ 1.3.
  - Each has a 30%/yr roll.
  - A civ fights at most 2 active wars.
- **Ending a war:**
  - Decisive at war score ±60.
  - Stalemate after 8 years with |score| < 25 and a 40%/yr peace chance.
  - Hard stop at 25 years.
  - Truce of 10 years.

**Who can declare:**

| Race (aggression) | Hatred war (≥ 0.35) | Opportunist war (≥ 0.6) |
|---|---|---|
| Elf (0.20) | never | never |
| Saurian (0.35) | borderline | never |
| Dwarf (0.40) | yes | never |
| Human (0.45) | yes | never |
| Orc (0.90) | yes | yes |

**Targets (Standard, after first contact):**

| Metric | Target | Measured (3 seeds) |
|---|---|---|
| Wars per 100 years, world | 3–6 | 7–10 ⚠ (15–20 in 140–300 years) |
| Share of wars started by orcs | 40–60% | about 95% ⚠ (almost every war is Orc vs X) |
| Average war length | 5–15 years | short. Many re-declare right after the 10-year truce ⚠ |
| Captures per war | 0.3–1.0 | 0.5–2.5 |
| Civ collapses by conquest per 500 years | 0–1 | 2–4 in 130–300 years ⚠ (elves always, dwarves often) |
| Years with ≥ 1 active war | 25–45% | — (instrument it) |

**Levers:**

- Raise `TruceYears` from 10 to 20.
- Add war weariness that lowers declaration chance for 30 years after a war.
- Scale the opportunist chance with the posture `ai.posture == Militarist`.
- Give elves and dwarves a defense multiplier for their home terrain (forest and hills).

---

## 8. Disaster frequency

**Live rates** (`DisasterSystem`, `WellbeingSystem`). All are × `DisasterFrequency`.

| Event | Rate | Scope | Notes |
|---|---|---|---|
| Earthquake | 1.2%/yr | per civ | |
| Volcano | 0.6%/yr | per civ | Plus 2%/yr wilderness eruption when Volcanic tiles exist |
| Flood | 1.6%/yr (rolled in spring at 4×) | per civ | |
| Wildfire | 1.6%/yr (summer or drought, 4×) | per civ | Plus 3%/yr wilderness fire |
| Storm | 1.6%/yr × season weight (autumn 1.4, winter 1.2, summer 0.8, spring 0.6) | per civ | |
| Per-civ cooldown | 3 years (× 0.25 chance inside it) | | |
| Epidemic (Plague) | 2% + 6% × density per **settlement** per year | per settlement | Cooldown 5 years. Kills 2–8% (+2% × density), less with Health coverage |
| Drought, Famine | weather and food driven | | Not random rolls |
| Lightning, Meteor | god powers only | | |

**Targets (Standard):**

| Metric | Target | Measured |
|---|---|---|
| Natural disasters touching a settlement, per civ per 10 years | 0.5–1.0 | about 0.7 (by the constants) ✓ |
| "Notable" disasters (importance ≥ 2), world | 1 per 10–20 years | — (instrument it) |
| Epidemics per settlement per 100 years | 1–3 | 2–8 ⚠. They scale with settlement count and crowding |
| Disease share of deaths | 5–15% | **40–95%** ⚠ (seed 20261119: 35,600 disease vs 51,580 old age. Seed 42: 12,809 vs 561) |
| Disaster deaths share | < 3% | under 1% ✓ |
| First 20 years | minor severity only (GDD §13.3) | not implemented. Severity is uniform 0.35–1.0 |

**Levers:**

- Base the epidemic density on houses, not all buildings. Cap it by Health coverage.
- Lower the base chance to 0.5%/yr.
- Count a civ-wide epidemic cooldown, not just a per-settlement one.

---

## 9. Tuning methodology (headless)

### 9.1 Tools

| Tool | Path | Use |
|---|---|---|
| Headless runner | `tools/GodSim.Headless` (the contract says `tools/Headless/`; the path on disk wins) | Long deterministic runs, tables, chronicle highlights, JSON snapshots, save and load, state hash |
| Bench | `tools/GodSim.Bench` | Performance budgets: 5,000 people under 3 ms/tick, 0-allocation event bus, path under 0.5 ms |
| Web viewer | `tools/WebViewer/index.html` | Visual inspection of `--out` snapshots |
| Editor | **GodSim → Simulate 100 Years (Headless)**, **Validate Content**, **Determinism Check** | Quick checks inside Unity |
| Content dump | The catalogs plus `GameDatabase` in a tiny console app (how §4 was produced) | Regenerating these tables |

```bash
# one run: 300 years, sample every 10 years, ASCII map, final hash, snapshot for the viewer
dotnet run -c Release --project tools/GodSim.Headless -- --seed 20261119 --years 300 --history-interval 10 --ascii --hash --out artifacts/s20261119.json

# race matchups
dotnet run -c Release --project tools/GodSim.Headless -- --seed 7 --years 500 --races Elf,Orc

# performance gate
dotnet run -c Release --project tools/GodSim.Bench -- --quick --budget --json artifacts/bench.json
```

**Missing runner features** (request to the tools owner):

- `--config Key=Value` overrides for every `SimConfig` field (`ResearchMultiplier`,
  `GrowthMultiplier`, `DisasterFrequency`, `EnableWarfare` and the rest).
- `--seeds a..b` batch mode.
- `--metrics out.csv` with one row per civ per sample, holding the §9.3 columns.
- An event log of age entries with year and population.

### 9.2 Protocol

1. **Freeze a baseline.** Use 20 seeds (1–20) plus 20261119 and 42, Standard settings, 192²
   Continents, all races, 1,500 years. Record the §9.3 metrics and the hashes.
2. **Change one knob family at a time.** For example, research output only. Never retune growth
   and research in the same pass.
3. **Rerun the sweep and compare distributions, not single seeds.** Use the median plus p10 and p90
   for each metric.
4. **Accept a change only if:**
   - every metric it targets moves into its band
   - no other metric leaves its band
   - perf stays inside budget (Bench `--budget`).
5. **Check robustness.** Repeat the accepted change on the Pangaea, Archipelago and Highlands
   presets, on 128² and 256² maps, and with 2-race subsets (each pair). Each race should reach
   Medieval in at least 80% of solo runs.
6. **Check determinism.** Run the same seed twice and require an identical `--hash`. Save at year
   N, load and continue, and require the same final hash.
7. **Write it down.** Put the change, its reason and the before and after medians in the
   "Balance log" at the end of this file.

### 9.3 Metrics to watch (with bands)

| Metric | Source | Band |
|---|---|---|
| Year of first entry into each age, per race | `AgeAdvancedEvent` | §2.1 |
| Years spent in each age | the same | 80–200 |
| Population per civ at y25, 100, 250, 500 and 1,000 | `TechQueries.PopulationOf` | §2.2 |
| World population and the % of `MaxPopulation` | `LivingPopulation` | < 25% of the cap at y1,000 |
| Houses per person, homeless % | Settlement `Homeless`, building counts | 0.20–0.33, < 5% |
| Buildings per tier, job slots ÷ workers | Settlement lists | §2.5, ≤ 1.2 |
| Food days (median, winter minimum) | `econ.foodDays` | 30–90, ≥ 10 in winter |
| Wood stock, median | Stockpile | ≥ 30 |
| Research/yr per civ | `prog.researchPerTick` × 96 | §5.1 implied band |
| Mana regen/yr, Mana/MaxMana | `GodState` | §6 |
| Wars per 100 years, aggressor mix, captures, collapses | Diplomacy events | §7 |
| Deaths by cause (shares) | `WorldStats.DeathsByCause` | Old age ≥ 50%, disease 5–15%, starvation < 2%, violence 2–10% |
| Disasters and epidemics per 100 years | `DisasterEvent` | §8 |
| Ascension Gate placed, completed, year | `game.ascended`, building list | Placed 50–150 years after Ascension entry |
| Faulted or disabled systems | `SimulationEngine.Profiles` | 0 |
| ms/tick average and p95 | Engine / Bench | < 3 ms at 5k people |

### 9.4 Regenerating the tables in this document

Build a throwaway console project that compiles `Core/Contracts`, `Core/Races`,
`Core/Progression/Content`, `Core/Buildings` and `Core/Divine/Powers/GodPowerCatalog.cs`. Then:

1. Call the five `Register` methods.
2. Print `db.Validate()`, which should be 0 errors (it was on 2026-11-19).
3. Print `db.Buildings`, `db.Ages`, `db.TechsOfAge(...)` and `db.GodPowers`.

Keep that project out of the repository, or add it later as `tools/GodSim.ContentReport`. That
folder exists today with only a stub.

### 9.5 First experiment: global multipliers as a stopgap

Seed 20261119, up to 400 years, no god input.

| Config | First Ascension | Other civs | World pop | Notes |
|---|---|---|---|---|
| Baseline (×1, ×1) | Saurian y49, Orc y66 | Elf conquered y80, Dwarf collapsed y219, Human stuck in Medieval | 40,000 cap at y100 | 0 Gates |
| `ResearchMultiplier` 0.05 | Saurian y97, Orc y222 | Elf and Dwarf dead by y208, Human down to pop 12 by y350 | 40,000 by y100 | The Saurian population still explodes (38k at y100) |
| Research 0.05 + `GrowthMultiplier` 0.35 | Saurian y133 | Human, Elf, Dwarf and Orc **still in Dawn or Bronze at y400**. Orc died out y284 | 40,000 by about y200 | No wars at all. Disease 46% of deaths |

**Conclusion.** The global knobs cannot fix pacing. Research scales with population, and the
Saurian clutch growth outruns any global growth multiplier. Lower growth also pushes the other four
races deeper into the low-population trap. The fixes have to be structural: §5.2 for research and
§10 I-3 to I-5 for population, housing and wood. They can't be done through `SimConfig` alone.

---

## 10. Known balance issues (prioritized)

| ID | Issue | Evidence | Owner | Proposed fix |
|---|---|---|---|---|
| I-1 | **The Ascension Gate is never built.** The endgame can't be reached | Seed 42: Saurians in Ascension for 96 years with 4,515 storage, 0 Gates placed | AI Strategy and Buildings | Check `CivilizationStrategySystem.Qualifies`, the 50% affordability check, the 4×4 footprint placement, and the Electronics and Machinery supply (600 each, paid atomically) |
| I-2 | **Pacing is about 20× too fast** after Iron | §2.1, §5.1 | Progression and Economy | §5.2 |
| I-3 | **Runaway growth.** The 40,000 cap is hit by year 100 | §2.2 | Population | Tie births to free housing more strongly, lower Saurian clutch size or fertility, and cap per-settlement growth by services |
| I-4 | **Low-population trap** for Elf and Dwarf (and sometimes Human), stuck below `MinPopulation` 25 | §2.2 | Progression, Population and AI | Race-scaled `MinPopulation`. A starting population or fertility boost for elves. A stone House variant or race cost for dwarves |
| I-5 | **Wood starvation.** House = 12 Wood only, and dwarf start sites have no trees | §3.3 | Buildings and AI | A race-aware dwelling cost (Dwarf: stone), or a cheaper stone-hut variant. The governor should weigh the woodcutter's terrain factor |
| I-6 | **Overbuilding producers.** Job slots are 3–6× the workers | §2.5 | AI Governor | Add a "staffing ratio" consideration that penalizes new job buildings when the open slots exceed 20% of adults |
| I-7 | **Service buildings ignore their inputs.** PowerPlant gives 20 Power with no Fuel. DataCenter gives 30 Research with no Electronics | `ServiceSystem.ComputeServices` uses staffing × condition only | Economy | Multiply services by the production input fraction for buildings that have inputs |
| I-8 | **Disease dominates deaths** (40–95%) | §8 | Population | §8 levers. Fixing I-3 (housing) also helps |
| I-9 | **The mana flood** late in the game | §6 | Divine | §6 target fixes |
| I-10 | **Orcs fight forever.** About 95% of wars are Orc vs X. Elves are always conquered | §7 | Diplomacy | §7 levers |
| I-11 | **Local weather powers act on the whole world.** Rain sets 48 days of rain and Drought sets 96 days of drought on the single `WeatherState`, so a Drought aimed at one rival halves food yields for every civ for a year | `GodPowerEffects.Rain/Drought`, contract `WeatherState` | Divine (and Lead for the contract) | Regional weather: per-settlement drought and rain timers in `Settlement.Memory` (`"wx.droughtUntil"`) read by production. The global state is kept for climate |
| I-12 | **No age gating on powers.** `MinWorldAge` is Dawn for all 19 powers, so Meteor and Plague are available in year 0 | `GodPowerCatalog` | Divine | Gate Meteor (Iron), Plague (Bronze) and Earthquake (Bronze), or scale costs with world age |
| I-13 | Contract and code mismatches | GAME_DESIGN §20.3 | Lead | — |

---

## Balance log

| Date | Change | Reason | Result (median, 3+ seeds) |
|---|---|---|---|
| 2026-11-19 | Baseline measured (no changes) | Establish a reference | §2.1 to §8 measured columns |
| 2026-11-19 | Experiment: `ResearchMultiplier` 0.05, then also `GrowthMultiplier` 0.35 (not adopted) | Test whether the global knobs can fix pacing | First Ascension moves from y49 to y97, then to y133. The world still hits the 40k cap. The minority races are trapped worse. Rejected (§9.5) |
