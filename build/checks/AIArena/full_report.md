# GodSim AI Quality Report

**Overall AI rating: 5.6 / 10** (weak)

- Arena: 8 seeds x 150 years, 160x160 map, preset Continents; seeds 1001, 1002, 1003, 1004, 1005, 1006, 1007, 1008
- Generated 2026-11-19 12:05 UTC; wall time 611.3 s of simulation (sequential)
- Command: `GodSim.AIArena --rescore build/checks/AIArena/full_results.json --out build/checks/AIArena/full_report.md`

## Headline numbers

| Metric | Value |
|---|---|
| Civilizations surviving | 55% of 40 |
| Population per civ (start -> final, peak) | x2.32 median; final 8000, peak 10086 avg |
| Ages gained per civ | 5.25 (years per age 21) |
| Starvation share of deaths | 0.1% |
| Homeless (time avg) | 27.8% |
| People per dwelling (target 3-5) | 7.14 avg, sd 10.53, 52.1% of samples in band; houses/capita 0.218 |
| Construction stalled / avg build time | 65.9% / 0.28 y |
| Idle adults | 49.3% |
| Food security | 131 days |
| Expansions | 5.38 per civ-century, first at year 21 |
| Wars / battles / disasters per world-century | 11.08 / 46 / 115.2 |
| Research | 35.6 techs per civ-century, 11232.3 research/y |
| Gold solvency | 99.3% of samples |
| AI decisions (AiLog lines) | 5.2 per settlement-year |
| Performance | 5.237 ms/tick avg, p99 45.33 ms, max 269.7 ms at ~19170 people |
| Faults | 0 system faults, 0 systems disabled, 0 handler faults, 0 logged errors |

## Score card

| Sub-score | Weight | Score | Inputs |
|---|---:|---:|---|
| Survival & stability | 20% | **5.2** | civ survival 55%, starvation 0.1% of deaths, homeless 27.8% |
| Growth & expansion | 15% | **5.5** | median pop x2.32 (target x11 for 150 y), 5.38 expansions/civ/century |
| Urban quality | 15% | **3.1** | 52.1% of samples at 3-5 ppl/house (avg 7.14, sd 10.53), tier-mix fit 74.6%, stall 65.9%, idle 49.3% |
| Progression | 15% | **6.7** | 5.25 ages gained/civ (expected 1.36), 21 y/age, 35.6 techs/century |
| Economy | 10% | **10.0** | gold solvent 99.3% of samples, food security 131 days |
| Resilience | 10% | **3.5** | 224 destruction incidents, 54.9% rebuilt, avg 5.56 y, final/peak pop 50%+ |
| Race diversity | 5% | **10.0** | mean pairwise cosine distance of race building-type vectors 0.258 |
| Drama | 5% | **2.0** | 11.08 wars, 46 battles, 115.2 disasters per world-century |
| Performance & robustness | 5% | **5.9** | 5.237 ms/tick avg (p99 45.33, max 269.7) at ~19170 people; 0 system faults, 0 disabled, 0 crashed sims |
| **Overall** | 100% | **5.6** | weighted sum |

## Top 5 problems

1. **Chronic homelessness**: seen in 8/8 seeds, worst on seed 1001 (severity 12).
   - Orc civ #4: 63.2% homeless avg (max decade 72.5%), 8.46 people/house
   - Reproduce: `dotnet run -c Release --project tools/GodSim.AIArena -- --seed-list 1001 --years 150 --size 160 --preset Continents`
2. **Overcrowding: too few dwellings (> 6 people/house)**: seen in 8/8 seeds, worst on seed 1001 (severity 12).
   - Saurian civ #5: 15.91 people per dwelling (target 3-5), 30% of samples in band
   - Reproduce: `dotnet run -c Release --project tools/GodSim.AIArena -- --seed-list 1001 --years 150 --size 160 --preset Continents`
3. **Idle workers**: seen in 8/8 seeds, worst on seed 1001 (severity 12).
   - Human civ #1: 63.4% of adults unemployed on average
   - Reproduce: `dotnet run -c Release --project tools/GodSim.AIArena -- --seed-list 1001 --years 150 --size 160 --preset Continents`
4. **Construction stalls**: seen in 8/8 seeds, worst on seed 1003 (severity 11.4).
   - Dwarf civ #3: 91.4% of construction time stalled, 0 buildings stuck > 2 y at end, avg build 0.07 y
   - Reproduce: `dotnet run -c Release --project tools/GodSim.AIArena -- --seed-list 1003 --years 150 --size 160 --preset Continents`
5. **Civilization collapsed**: seen in 7/8 seeds, worst on seed 1001 (severity 12).
   - Elf civ #2 collapsed in year 72.7 (peak 26 pop in year 55)
   - Reproduce: `dotnet run -c Release --project tools/GodSim.AIArena -- --seed-list 1001 --years 150 --size 160 --preset Continents`

Other issues: Tick budget exceeded (> 3 ms avg) (8/8 seeds, e.g. 1007); World population pinned at the MaxPopulation safety cap (8/8 seeds, e.g. 1001); Starvation deaths (3/8 seeds, e.g. 1006); Ages advance too fast (< 25 y/age) (8/8 seeds, e.g. 1001); Settlements do not rebuild after destruction (3/8 seeds, e.g. 1008); Building mix misses tier essentials (2/8 seeds, e.g. 1005)

## Per race

| Race | Civs | Score | Survive | Pop x | Peak | Ages | Y/age | Starv% | Homeless% | Ppl/house | In band% | Tier fit | Stall% | Idle% | Food d | Exp/c | Wars/c | Battle win% | Techs/c | Solvent% | AI/s-y |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Dwarf | 8 | 5.6 | 25% | 0 | 50 | 1.5 | 27 | 15.6 | 5.7 | 3.63 | 57 | 75% | 78 | 30.1 | 76 | 2.1 | 2.1 | 0 | 13.7 | 100 | 4.1 |
| Elf | 8 | 6.6 | 25% | 0 | 43 | 1.38 | 46 | 5.2 | 0.5 | 3.08 | 68 | 95% | 55 | 2.4 | 285 | 1.4 | 1.3 | 0 | 13.3 | 100 | 2.9 |
| Human | 8 | 7.5 | 75% | 33.89 | 1572 | 6.63 | 15 | 1.1 | 21.1 | 4.46 | 59 | 74% | 71 | 49.3 | 160 | 7.9 | 4.3 | 16 | 43.6 | 100 | 4.6 |
| Orc | 8 | 7.5 | 88% | 1427.18 | 22158 | 7.88 | 10 | 0 | 43.6 | 8.25 | 44 | 63% | 70 | 77.4 | 67 | 9.3 | 10.5 | 61 | 50.9 | 98 | 5.5 |
| Saurian | 8 | 7.1 | 63% | 1428.68 | 26606 | 8.88 | 5 | 0 | 68 | 16.29 | 32 | 65% | 55 | 87.1 | 67 | 6.2 | 4 | 54 | 56.7 | 99 | 8.9 |
| All | 40 | 6.3 | 55% | 2.32 | 10086 | 5.25 | 21 | 0.1 | 27.8 | 7.14 | 52 | 75% | 66 | 49.3 | 131 | 5.4 | 4.4 | 50 | 35.6 | 99 | 5.2 |

Score = civ-level composite: 10 x (0.3 survival + 0.2 growth + 0.2 urban + 0.2 progression + 0.1 economy), same sub-formulas as the score card.

### Population curves (average civ, start to end)

| Race | Curve | Start | 25% | 50% | 75% | End |
|---|---|---:|---:|---:|---:|---:|
| Dwarf | `▃▄▅▅▆▇███▇█▇▆▄▄▄▅▄▄▃▃▂▂▂` | 14 | 34 | 31 | 20 | 10 |
| Elf | `▄▄▄▄▄▆▆▇▇▇███▇▆▅▅▄▄▄▄▂▂▂` | 14 | 25 | 31 | 18 | 9 |
| Human | `       ▁▁▁▁▂▂▃▄▅▆▆▆▆▇▇██` | 14 | 68 | 298 | 986 | 1321 |
| Orc | `         ▁▁▁▂▃▄▅▅▆▆▆████` | 14 | 167 | 3928 | 14887 | 20534 |
| Saurian | `        ▁▁▂▃▄▅▆▇█▇▇▇▆▆▆▆` | 14 | 685 | 10721 | 24081 | 18126 |
| All | `        ▁▁▂▃▃▅▆▇████████` | 14 | 196 | 3002 | 7998 | 8000 |

### Ages reached

| Race | Final ages (per civ) | Year each age was reached (per civ) |
|---|---|---|
| Dwarf | Iron x4, Bronze x4 | 16/62; 16/53; 31; 28; 11/40; 16; 44; 25/47 |
| Elf | Bronze x5, Iron x3 | 52; 33/102; 30/82; 22; 34; 65; 38/61; 75 |
| Human | Ascension x2, Modern x2, Classical x1, Information x1, Industrial x1, Medieval x1 | 13/29/51; 12/29/39/57/65/79/85/94/101; 12/24/43/57/70/79/89; 11/41/46/63/75/87/115/136; 12/31/53/68/85/100/134; 13/25/46/57/61/66/72/76/80; 12/26/42/52/77/84; 15/28/41/93 |
| Orc | Ascension x6, Iron x1, Modern x1 | 13/24/35/47/55/63/69/73/77; 16/30; 12/24/37/47/56/63/69/75/79; 15/37/51/73/80/136/146; 12/26/39/48/55/61/66/70/73; 15/28/39/48/56/63/69/74/78; 11/21/31/38/43/47/52/55/58; 15/27/37/45/47/49/51/52/54 |
| Saurian | Ascension x7, Information x1 | 9/19/28/34/37/39/41/43/45; 9/20/29/34/37/40/43/46/47; 10/20/28/34/39/41/43/45/46; 9/20/29/35/40/44/47/50/52; 11/22/30/35/38/40/42/44/46; 11/21/29/32/35/38/41/44/46; 9/20/29/34/37/41/44/47/49; 11/22/31/33/36/40/43/57 |

## Urban form: building mix by race (% of completed buildings at end)

| Race | Housing | Food | Resource | Industry | Storage | Civic | Religion | Education | Health | Culture | Military | Commerce | Infrastructure | Wonder |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Dwarf | 11.4 | 23.9 | 25.4 | 3 | 25.4 | 2.5 | 0.5 | 0 | 3 | 1.5 | 1 | 0 | 2.5 | 0 |
| Elf | 13.5 | 24.6 | 21.3 | 6.8 | 13 | 1.4 | 1.9 | 0 | 9.2 | 3.4 | 2.9 | 0 | 1.9 | 0 |
| Human | 64.6 | 8.5 | 8.8 | 4.5 | 4.5 | 1.4 | 0.5 | 0.2 | 3.1 | 0.6 | 0.3 | 1.3 | 1.7 | 0 |
| Orc | 79.3 | 3.8 | 6.1 | 3.2 | 2.3 | 0.9 | 0.1 | 0.1 | 1.4 | 0.3 | 0.6 | 0.7 | 1.1 | 0 |
| Saurian | 79.7 | 4.3 | 6.1 | 2.7 | 2 | 0.8 | 0.4 | 0.1 | 1.2 | 0.3 | 0.3 | 1.1 | 1 | 0 |
| All | 77.2 | 4.7 | 6.6 | 3.2 | 2.6 | 0.9 | 0.3 | 0.1 | 1.5 | 0.4 | 0.5 | 0.9 | 1.1 | 0 |

### Race diversity (cosine distance of building-type mix; 0 = identical behaviour)

| | Dwarf | Elf | Human | Orc | Saurian |
|---|---:|---:|---:|---:|---:|
| Dwarf | 0.000 | 0.321 | 0.281 | 0.328 | 0.340 |
| Elf | 0.321 | 0.000 | 0.402 | 0.440 | 0.455 |
| Human | 0.281 | 0.402 | 0.000 | 0.005 | 0.007 |
| Orc | 0.328 | 0.440 | 0.005 | 0.000 | 0.000 |
| Saurian | 0.340 | 0.455 | 0.007 | 0.000 | 0.000 |

Mean pairwise distance: 0.258 (0.25+ scores 10).

## Conflict, disasters & recovery

- Wars: 133 total (59 attacker wins, 39 defender wins, 32 white peace, 3 ongoing); avg duration 4.2 y; longest 15 y.
- Most common aggressor -> defender: Orc -> Saurian x41, Orc -> Human x36, Orc -> Dwarf x18, Orc -> Elf x16, Human -> Orc x8, Human -> Saurian x7
- Battles: 552, settlement captures: 225, civ collapses: 18, new civs mid-game: 0.
- Disasters: 1382 (Plague 1042, Wildfire 113, Flood 72, Storm 67, Earthquake 55, Volcano 33).
- Destruction incidents (fire/disaster/war/divine): 224, buildings lost 6258, rebuilt 54.9% (avg 5.56 y to regain the pre-incident building count), 79 settlements lost before rebuilding.

## Per seed

| Seed | Years | Civs alive | World pop (end) | Best age | Wars | Battles | Captures | Disasters | Incidents rebuilt | ms/tick | p99 | Faults | Hash | Status |
|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 1001 | 150 | 3/5 | 40000 | Ascension | 17 | 164 | 29 | 170 | 17/34 | 5.628 | 45.33 | 0 | `200714c3c5ca1816` | ok |
| 1002 | 150 | 4/5 | 40000 | Ascension | 5 | 6 | 6 | 156 | 23/29 | 4.716 | 29.53 | 0 | `2fa1141e0f47e480` | ok |
| 1003 | 150 | 2/5 | 40000 | Ascension | 18 | 83 | 45 | 195 | 13/19 | 5.573 | 42.49 | 0 | `b9452a219d2acd32` | ok |
| 1004 | 150 | 5/5 | 40000 | Ascension | 18 | 21 | 13 | 172 | 17/31 | 4.81 | 30.79 | 0 | `2038e8a03c8390af` | ok |
| 1005 | 150 | 2/5 | 40000 | Ascension | 23 | 28 | 10 | 183 | 16/34 | 5.567 | 43.91 | 0 | `068a91ab04ba0874` | ok |
| 1006 | 150 | 3/5 | 40000 | Ascension | 20 | 178 | 93 | 169 | 18/25 | 4.929 | 29.04 | 0 | `618304322c6b9807` | ok |
| 1007 | 150 | 1/5 | 40000 | Ascension | 17 | 44 | 15 | 179 | 14/31 | 5.806 | 42.42 | 0 | `67e7c6994827011e` | ok |
| 1008 | 150 | 2/5 | 40000 | Ascension | 15 | 28 | 14 | 158 | 5/21 | 4.867 | 28.31 | 0 | `1cc0c71ff6d48346` | ok |

## Performance & faults

Slowest systems (avg ms per run, EMA, across seeds where they ranked top 6): Security.Integrity 19.147, Population.Migration 14.833, Population.Wellbeing 5.872, Population.Housing 5.3, Population.Jobs 4.52, AI.SettlementGovernor 3.237, Population.Aging 3.104

## Methodology

Each seed runs the full DefaultComposition headless (`GodSimulation.CreateNew` then one `Step` at a time). The arena only observes: it subscribes to EventBus events (births, deaths, buildings, settlements, ages, techs, wars, battles, captures, disasters, famines) and samples state every season (24 ticks). Only civilizations present at world creation are scored (rebels/spawned tribes are reported but not scored).

| Sub-score | Weight | Formula (all terms clamped to 0..1) |
|---|---:|---|
| Survival & stability | 20% | 10 x (0.4 x survival + 0.3 x (1 - starvation%/20) + 0.3 x (1 - homeless%/25)) |
| Growth & expansion | 15% | 10 x (0.7 x clamp(ln(growth)/ln(1+years/15, max 12)) + 0.3 x clamp(expansions per century / 2)) |
| Urban quality | 15% | 10 x (0.3 x in-band% + 0.15 x (1 - sd/2) + 0.2 x tier-fit + 0.2 x (1 - stall%/50) + 0.15 x (1 - idle%/30)) |
| Progression | 15% | 10 x (0.7 x clamp(ages gained / (years/110)) [x clamp(y-per-age/40) if faster than 40 y] + 0.3 x clamp(techs per century / 8)) |
| Economy | 10% | 10 x (0.5 x solvency% + 0.5 x clamp(food days / 60)) |
| Resilience | 10% | 10 x (0.4 x rebuilt% + 0.3 x (1 - recovery years/10) + 0.3 x clamp((final/peak - 0.5)/0.5)); untested parts count 0.8 |
| Race diversity | 5% | 10 x clamp(mean cosine distance / 0.25) |
| Drama | 5% | 10 x (0.45 x band(wars/century, 1..6, soft 4) + 0.35 x band(disasters/century, 3..40, soft 20) + 0.2 x band(battles per war, 1..30, 0 battles = 0)) |
| Performance & robustness | 5% | 10 x (0.5 x clamp(1 - (ms/tick - 1.5)/4.5) + 0.5 x robustness), robustness = 0 on any crash else 1 - 0.5 x disabled - 0.5 x min(1, faults/50) |

Metric definitions:
- *People per dwelling*: settlement population / completed dwellings (`IsDwelling`), per seasonal sample of settlements with 6+ people; *in band* = 3..5. sd = std-dev across those samples (spread of the 3-5 rule).
- *Tier fit*: fraction of categories expected at the settlement's tier that have at least one completed building (Camp: Housing+Food; Hamlet: +Resource; Village: +Storage, Religion; Town: +Industry, Military, Culture; City+: +Commerce, Health, Education).
- *Stall*: share of under-construction building-time in which `ConstructionProgress` did not increase since the previous sample (includes waiting for materials/cost).
- *Idle*: `Settlement.Unemployed` / adult residents. *Food security*: food stock / max(avg food expense, half of race FoodPerTick x pop), capped at 365 days.
- *Gold solvent*: a settlement sample with gold > 1 or non-negative net gold flow.
- *Expansion*: `SettlementFoundedEvent` after world creation. *War outcome*: final war score > 15 attacker, < -15 defender, else white peace.
- *Destruction incident*: a settlement losing a building to fire, disaster, war or divine power; recovered when its completed-building count returns to its value at the sample before the loss.
- *AI decisions*: new `Settlement.AiLog` lines per settlement-year (detected every tick; the log is bounded to 16 lines).
- *Diversity*: each race's building-type vector (completed buildings at end, each civ normalized then summed); cosine distance between races.
- *Top problems*: per problem kind the worst instance; ranked by severity x (0.5 + 0.5 x share of seeds affected).
