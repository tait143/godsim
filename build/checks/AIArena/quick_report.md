# GodSim AI Quality Report

**Overall AI rating: 7.6 / 10** (good)

- Arena: 2 seeds x 40 years, 128x128 map, preset Continents; seeds 1001, 1002
- Generated 2026-11-19 12:05 UTC; wall time 6 s of simulation (sequential)
- Command: `GodSim.AIArena --quick --out /home/user/GodSim/build/checks/AIArena/quick_report.md --json /home/user/GodSim/build/checks/AIArena/quick_results.json`

## Headline numbers

| Metric | Value |
|---|---|
| Civilizations surviving | 100% of 10 |
| Population per civ (start -> final, peak) | x4.82 median; final 276, peak 276 avg |
| Ages gained per civ | 2.9 (years per age 14) |
| Starvation share of deaths | 0% |
| Homeless (time avg) | 2.1% |
| People per dwelling (target 3-5) | 2.78 avg, sd 1.22, 22.3% of samples in band; houses/capita 0.46 |
| Construction stalled / avg build time | 74.9% / 0.45 y |
| Idle adults | 12.4% |
| Food security | 194.2 days |
| Expansions | 7.5 per civ-century, first at year 14 |
| Wars / battles / disasters per world-century | 2.5 / 2.5 / 55 |
| Research | 81.8 techs per civ-century, 291.2 research/y |
| Gold solvency | 100% of samples |
| AI decisions (AiLog lines) | 40.2 per settlement-year |
| Performance | 0.734 ms/tick avg, p99 5.16 ms, max 29.6 ms at ~416 people |
| Faults | 0 system faults, 0 systems disabled, 0 handler faults, 0 logged errors |

## Score card

| Sub-score | Weight | Score | Inputs |
|---|---:|---:|---|
| Survival & stability | 20% | **9.8** | civ survival 100%, starvation 0% of deaths, homeless 2.1% |
| Growth & expansion | 15% | **10.0** | median pop x4.82 (target x3.7 for 40 y), 7.5 expansions/civ/century |
| Urban quality | 15% | **3.8** | 22.3% of samples at 3-5 ppl/house (avg 2.78, sd 1.22), tier-mix fit 83.3%, stall 74.9%, idle 12.4% |
| Progression | 15% | **5.4** | 2.9 ages gained/civ (expected 0.36), 14 y/age, 81.8 techs/century |
| Economy | 10% | **10.0** | gold solvent 100% of samples, food security 194.2 days |
| Resilience | 10% | **7.3** | 4 destruction incidents, 50% rebuilt, avg 2.07 y, final/peak pop 99.1%+ |
| Race diversity | 5% | **3.1** | mean pairwise cosine distance of race building-type vectors 0.078 |
| Drama | 5% | **7.4** | 2.5 wars, 2.5 battles, 55 disasters per world-century |
| Performance & robustness | 5% | **10.0** | 0.734 ms/tick avg (p99 5.16, max 29.6) at ~416 people; 0 system faults, 0 disabled, 0 crashed sims |
| **Overall** | 100% | **7.6** | weighted sum |

## Top 5 problems

1. **Construction stalls**: seen in 2/2 seeds, worst on seed 1001 (severity 10.2).
   - Elf civ #2: 81.4% of construction time stalled, 0 buildings stuck > 2 y at end, avg build 0.21 y
   - Reproduce: `dotnet run -c Release --project tools/GodSim.AIArena -- --seed-list 1001 --years 40 --size 128 --preset Continents`
2. **Idle workers**: seen in 2/2 seeds, worst on seed 1002 (severity 6.5).
   - Saurian civ #5: 32.6% of adults unemployed on average
   - Reproduce: `dotnet run -c Release --project tools/GodSim.AIArena -- --seed-list 1002 --years 40 --size 128 --preset Continents`
3. **Ages advance too fast (< 25 y/age)**: seen in 2/2 seeds, worst on seed 1001 (severity 5).
   - Human civ #1: 2 ages in 40 y (14 y/age), now Iron
   - Reproduce: `dotnet run -c Release --project tools/GodSim.AIArena -- --seed-list 1001 --years 40 --size 128 --preset Continents`
4. **Settlements do not rebuild after destruction**: seen in 1/2 seeds, worst on seed 1001 (severity 6).
   - 1/3 incidents rebuilt (7 buildings lost)
   - Reproduce: `dotnet run -c Release --project tools/GodSim.AIArena -- --seed-list 1001 --years 40 --size 128 --preset Continents`
5. **Over-built housing (< 2.5 people/house)**: seen in 2/2 seeds, worst on seed 1002 (severity 3.4).
   - Human civ #1: 1.87 people per dwelling (target 3-5)
   - Reproduce: `dotnet run -c Release --project tools/GodSim.AIArena -- --seed-list 1002 --years 40 --size 128 --preset Continents`

Other issues: AI decision churn (> 60 decisions per settlement-year) (1/2 seeds, e.g. 1002); Chronic homelessness (1/2 seeds, e.g. 1002); Building mix misses tier essentials (1/2 seeds, e.g. 1002)

## Per race

| Race | Civs | Score | Survive | Pop x | Peak | Ages | Y/age | Starv% | Homeless% | Ppl/house | In band% | Tier fit | Stall% | Idle% | Food d | Exp/c | Wars/c | Battle win% | Techs/c | Solvent% | AI/s-y |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Dwarf | 2 | 8.2 | 100% | 2.18 | 31 | 1 | 19 | 0 | 0.1 | 2.13 | 15 | 87% | 73 | 12 | 189 | 2.5 | 1.3 | - | 42.5 | 100 | 31.1 |
| Elf | 2 | 8.6 | 100% | 2.5 | 36 | 1 | 19 | 0 | 0.4 | 2.49 | 22 | 97% | 77 | 2.2 | 314 | 3.8 | 0 | - | 41.3 | 100 | 27.1 |
| Human | 2 | 8.8 | 100% | 4.82 | 69 | 2 | 16 | 0 | 1.1 | 2.32 | 15 | 91% | 78 | 1.5 | 224 | 7.5 | 0 | - | 60 | 100 | 42.7 |
| Orc | 2 | 8.7 | 100% | 13.07 | 183 | 2.5 | 13 | 0 | 0.8 | 2.31 | 22 | 83% | 72 | 17.3 | 124 | 15 | 2.5 | 0 | 75 | 100 | 39.3 |
| Saurian | 2 | 8.2 | 100% | 75.89 | 1063 | 8 | 5 | 0 | 8.2 | 4.65 | 38 | 59% | 74 | 29.2 | 120 | 8.8 | 1.3 | 100 | 190 | 100 | 60.9 |
| All | 10 | 8.7 | 100% | 4.82 | 276 | 2.9 | 14 | 0 | 2.1 | 2.78 | 22 | 83% | 75 | 12.4 | 194 | 7.5 | 1 | 50 | 81.8 | 100 | 40.2 |

Score = civ-level composite: 10 x (0.3 survival + 0.2 growth + 0.2 urban + 0.2 progression + 0.1 economy), same sub-formulas as the score card.

### Population curves (average civ, start to end)

| Race | Curve | Start | 25% | 50% | 75% | End |
|---|---|---:|---:|---:|---:|---:|
| Dwarf | `▄▄▄▄▄▅▅▆▆▆▆▆▇▇▇▇████████` | 14 | 20 | 25 | 31 | 31 |
| Elf | `▃▅▅▅▅▆▆▆▇▇▇▇▇▇████▇█████` | 14 | 27 | 30 | 34 | 35 |
| Human | `▂▂▂▂▂▂▃▃▃▃▄▄▄▅▅▅▅▆▆▆▇███` | 14 | 24 | 36 | 49 | 68 |
| Orc | `▁▁▁▁▁▁▁▂▂▂▂▂▃▃▃▄▄▄▄▄▅▇▇█` | 14 | 33 | 55 | 85 | 183 |
| Saurian | `         ▁▁▁▁▁▂▂▂▃▃▄▅▆▇█` | 14 | 49 | 139 | 348 | 1063 |
| All | ` ▁▁▁▁▁▁▁▁▁▁▂▂▂▂▂▃▃▃▄▅▇▇█` | 14 | 30 | 57 | 109 | 276 |

### Ages reached

| Race | Final ages (per civ) | Year each age was reached (per civ) |
|---|---|---|
| Dwarf | Bronze x2 | 22; 17 |
| Elf | Dawn x1, Iron x1 | -; 8/37 |
| Human | Iron x2 | 14/29; 16/34 |
| Orc | Iron x1, Classical x1 | 14/27; 15/27/38 |
| Saurian | Information x2 | 10/20/28/32/34/36/37/38; 9/19/26/31/33/35/37/40 |

## Urban form: building mix by race (% of completed buildings at end)

| Race | Housing | Food | Resource | Industry | Storage | Civic | Religion | Education | Health | Culture | Military | Commerce | Infrastructure | Wonder |
|---|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|---:|
| Dwarf | 37.4 | 11.4 | 13.8 | 4.1 | 10.6 | 3.3 | 4.1 | 0 | 1.6 | 5.7 | 4.1 | 0 | 4.1 | 0 |
| Elf | 24.8 | 12.8 | 12.8 | 5 | 15.6 | 3.5 | 7.8 | 0 | 7.1 | 5.7 | 0.7 | 0 | 4.3 | 0 |
| Human | 33 | 10.6 | 15.1 | 6 | 11.5 | 3.7 | 2.3 | 1.4 | 1.8 | 9.2 | 0.9 | 0 | 4.6 | 0 |
| Orc | 50.4 | 12.2 | 14 | 1.8 | 3.4 | 3.4 | 0.5 | 0.3 | 1 | 5.5 | 4.2 | 0 | 3.4 | 0 |
| Saurian | 64.6 | 3.9 | 8.3 | 3.9 | 6.8 | 1.9 | 0.9 | 0.9 | 1.6 | 1.1 | 1.1 | 1.1 | 3.8 | 0 |
| All | 50.4 | 8.5 | 11.7 | 3.8 | 7.7 | 2.8 | 1.9 | 0.7 | 2 | 4.2 | 2.1 | 0.5 | 3.9 | 0 |

### Race diversity (cosine distance of building-type mix; 0 = identical behaviour)

| | Dwarf | Elf | Human | Orc | Saurian |
|---|---:|---:|---:|---:|---:|
| Dwarf | 0.000 | 0.103 | 0.017 | 0.015 | 0.045 |
| Elf | 0.103 | 0.000 | 0.084 | 0.164 | 0.202 |
| Human | 0.017 | 0.084 | 0.000 | 0.043 | 0.078 |
| Orc | 0.015 | 0.164 | 0.043 | 0.000 | 0.027 |
| Saurian | 0.045 | 0.202 | 0.078 | 0.027 | 0.000 |

Mean pairwise distance: 0.078 (0.25+ scores 10).

## Conflict, disasters & recovery

- Wars: 2 total (0 attacker wins, 1 defender wins, 1 white peace, 0 ongoing); avg duration 7.5 y; longest 8.5 y.
- Most common aggressor -> defender: Orc -> Saurian x1, Orc -> Dwarf x1
- Battles: 2, settlement captures: 2, civ collapses: 0, new civs mid-game: 0.
- Disasters: 44 (Plague 22, Wildfire 7, Flood 5, Earthquake 5, Storm 4, Volcano 1).
- Destruction incidents (fire/disaster/war/divine): 4, buildings lost 8, rebuilt 50% (avg 2.07 y to regain the pre-incident building count), 0 settlements lost before rebuilding.

## Per seed

| Seed | Years | Civs alive | World pop (end) | Best age | Wars | Battles | Captures | Disasters | Incidents rebuilt | ms/tick | p99 | Faults | Hash | Status |
|---|---:|---:|---:|---|---:|---:|---:|---:|---:|---:|---:|---:|---|---|
| 1001 | 40 | 5/5 | 1294 | Information | 1 | 2 | 2 | 27 | 1/3 | 0.852 | 5.16 | 0 | `406c11650036e5e5` | ok |
| 1002 | 40 | 5/5 | 1463 | Information | 1 | 0 | 0 | 17 | 1/1 | 0.617 | 2.78 | 0 | `5e76b4d3d3cf6881` | ok |

## Performance & faults

Slowest systems (avg ms per run, EMA, across seeds where they ranked top 6): Security.Integrity 2.838, AI.SettlementGovernor 0.93, TownPlanner 0.787, AI.Advisor.urban 0.587, AI.Advisor.food 0.561, AI.Advisor.specialize 0.547, AI.Advisor.supply 0.532, AI.Advisor.health 0.522

## AI internals (civ.Memory at end)

| Key | Dwarf | Elf | Human | Orc | Saurian |
|---|---:|---:|---:|---:|---:|
| `ai.score.construction` | 0.917 | 0.7 | 0.686 | 0.722 | 0.789 |
| `ai.score.food` | 0.95 | 1 | 1 | 0.793 | 0.987 |
| `ai.score.growth` | 0.514 | 0.518 | 0.62 | 0.83 | 0.99 |
| `ai.score.happiness` | 0.922 | 0.958 | 0.93 | 0.875 | 0.933 |
| `ai.score.health` | 0.968 | 0.995 | 0.924 | 0.954 | 0.999 |
| `ai.score.housing` | 1 | 1 | 0.993 | 1 | 0.796 |
| `ai.score.military` | 0.65 | 0.701 | 0.971 | 0.679 | 0.945 |
| `ai.score.overall` | 0.797 | 0.78 | 0.839 | 0.795 | 0.877 |
| `ai.score.research` | 1 | 1 | 0.963 | 0.909 | 0.993 |
| `ai.score.wealth` | 0.773 | 0.639 | 0.85 | 0.752 | 0.713 |
| `ai.w.ages` | 1.064 | 0.919 | 1.137 | 1.026 | 0.815 |
| `ai.w.capital` | 0.963 | 0.934 | 1.048 | 1.137 | 0.926 |
| `ai.w.defense` | 0.995 | 0.84 | 0.822 | 1.334 | 0.854 |
| `ai.w.faith` | 0.973 | 0.98 | 1.039 | 1.047 | 1.232 |
| `ai.w.food` | 1.043 | 1.019 | 0.983 | 1.055 | 0.984 |
| `ai.w.frontier` | 0.866 | 0.831 | 1.145 | 1.254 | 1.016 |
| `ai.w.god` | 1.019 | 1.195 | 1.044 | 0.98 | 1.318 |
| `ai.w.growth` | 1.003 | 0.995 | 0.994 | 1.02 | 1.044 |
| `ai.w.happiness` | 1.022 | 0.981 | 1.001 | 0.997 | 1.011 |
| `ai.w.health` | 0.989 | 0.987 | 0.996 | 0.977 | 1.005 |
| `ai.w.infra` | 1.206 | 0.962 | 1.051 | 1.025 | 0.94 |
| `ai.w.labor` | 0.845 | 0.411 | 0.596 | 0.526 | 0.542 |
| `ai.w.logistics` | 1.053 | 0.961 | 0.585 | 0.775 | 1.06 |
| `ai.w.prospect` | 1.257 | 0.936 | 1.147 | 0.865 | 0.987 |
| `ai.w.race_dwarf` | 0.473 | 0.985 | 1.03 | - | 1.03 |
| `ai.w.race_elf` | 1.015 | 0.471 | 0.91 | 1.03 | 0.955 |
| `ai.w.race_human` | - | - | 1.039 | - | - |
| `ai.w.race_orc` | 0.97 | 0.91 | 0.985 | 0.909 | 0.714 |
| `ai.w.race_saurian` | 0.985 | 1.075 | - | 0.97 | 0.4 |
| `ai.w.recovery` | 1.003 | 1.005 | 1.021 | 1.013 | 1.014 |
| `ai.w.science` | 1.143 | 1.394 | 1.24 | 0.991 | 1.226 |
| `ai.w.seasons` | 0.98 | 0.996 | 0.999 | 1.006 | 1.003 |
| `ai.w.specialize` | 0.915 | 0.888 | 1.079 | 0.563 | 0.724 |
| `ai.w.supply` | 1.122 | 0.99 | 0.821 | 0.719 | 0.72 |
| `ai.w.treasury` | 1.18 | 0.917 | 1.479 | 0.833 | 1.008 |
| `ai.w.urban` | 1.106 | 0.984 | 1.045 | 1.047 | 1.055 |
| `ai.w.war` | 1.081 | 0.599 | 0.682 | 1.521 | 0.712 |

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
