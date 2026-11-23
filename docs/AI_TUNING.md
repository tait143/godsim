# GodSim — Civilization AI Tuning Guide

> Companion to [AI_DESIGN.md](AI_DESIGN.md). Every number below is cited as `file:line` (paths are relative to
> `Assets/GodSim/Core/`) or as `file › method` when it is an inline literal. The **safe range** is the range in which the
> system stays stable in our experience or as enforced by `Sanitize()`. Leave it only with an arena run to back the change.
> Snapshot of 2026-11-19. Files are still landing, so re-run `grep -n "const" AI/**` before trusting a line number.

## 0. Golden rules

1. **One knob, one arena run.** Change a single constant, then run ≥ 8 seeds × 300 years (§3) and compare the subscores. Never tune from one seed.
2. **Prefer data over code.** If one building is wrong, fix its `BuildingDefinition` (cost, `MaxPerSettlement`, `Desirability`, `Placement`) or the race's `FavoredBuildings`/traits before you touch a shared multiplier.
3. **Advisor strengths are relative.** A vote is `strength(±1) × ai.w(0..3)`, accumulated up to ±3, decayed ×0.5 every 6 ticks, and added to utility at `AdvisorBiasScale` (0.35). Tune `AdvisorBiasScale` to change how much the Council matters overall. Tune per-advisor constants to change the balance *between* advisors.
4. Keep every value `Sanitize`d and every rate scaled by `ctx.DeltaTicks`. Determinism tests must stay green.

## 1. Settlement Governor (`AI/Governor`)

### 1.1 `GovernorSettings` (instance knobs, sanitized; pass a custom instance to `new SettlementGovernorSystem(settings)`)
| Field | file:line | Meaning | Default | Safe range (Sanitize clamp) |
|---|---|---|---|---|
| `QueueLimit` | GovernorSettings.cs:11 | max orders in the queue (all origins) | 4 | 3–6 (1–32) |
| `MaxQueuedDwellingsPerType` | :13 | queued Houses of one type | 2 | 1–3 (1–QueueLimit) |
| `MaxQueuedPerType` | :15 | queued orders of one non-dwelling type | 1 | 1 (1–QueueLimit) |
| `MaxOrdersPerRun` | :17 | new orders per 6-tick run | 2 | 1–2 |
| `OrderThreshold` | :19 | min utility for the first order | 0.2 | 0.15–0.3 |
| `IdleOrderThreshold` | :21 | min utility when nothing is queued or building | 0.12 | 0.08–0.2 (≤ OrderThreshold) |
| `SecondOrderThreshold` | :23 | min utility for the second order of a run | 0.35 | 0.3–0.6 (≥ OrderThreshold) |
| `StaleOrderTicks` | :25 | drop non-wonder orders older than this | 288 (3 y) | 1–5 y |
| `TerritoryRefreshTicks` | :27 | territory scan cache life | 96 | 48–192 |
| `MinFreeHousing` | :29 | min spare beds | 2 | 1–4 |
| `FreeHousingFraction` | :31 | spare beds as a fraction of population | 0.08 | 0.04–0.12 |
| `DefaultDwellingCapacity` | :33 | expected residents per House | 4 | 3–5 (contract 3–5) |
| `HousingLeadYears` | :35 | growth anticipation for housing | 0.25 | 0.1–0.5 |
| `ThreatRadius` | :37 | hostile settlement radius (tiles) | 40 | 30–60 |
| `RepeatCooldownTicks` | :39 | ×0.7 repeat penalty window | 48 | 24–96 |
| `PlacementBlockTicks` | :41 | type blocked after placement failures | 96 | 48–192 |
| `PlacementFailureAttempts` | :43 | planner failures before the block | 2 | 2–4 (planner drops at 4) |
| `EvictionUtility` | :45 | min utility to evict on a full queue | 0.7 | 0.6–0.9 |
| `EvictionMargin` | :47 | priority margin over the victim | 3 | 2–5 |
| `AdvisorBiasScale` | :50 | utility per unit of council bias | 0.35 | 0.2–0.5 (0–5) |
| `AdvisorVetoBias` | :52 | bias at or below which a candidate is vetoed | −1 | −1.5 to −0.5 (−4 to 0) |
| `CriticalNeed` | :54 | need level that overrides negative advice | 0.8 | 0.7–0.9 |
| `StallTicks` | :56 | drop the least urgent unaffordable order after this idle time | 96 | 48–192 |

### 1.2 Need curves (`NeedEvaluator.cs`)
| Consideration | line | Curve | Effect |
|---|---|---|---|
| `HousingDeficit` | 18 | Linear(0, 6) | housing need = 0.55 + 0.45·score when deficit > 0 (`EvaluateHousing`) |
| `FoodReserveDays` | 20 | Logistic(0, 60, k 9, mid 0.4, inverted) | ≈0.5 at 24 days, ≈0 above 50 days |
| `FoodNetRatio` | 22 | Linear(0.25, −0.6) | net/consumption: 1 at −60%, 0 at +25% (×0.85 measured / ×0.55 estimated) |
| `ServiceCoverage` | 24 | Linear(0.7, 0) | 1 when uncovered, 0 at 70% coverage |
| `ServicePopulation` | 26 | Linear(0, 21) | services scale 0.3 → 1 over 0–21 people |
| `StorageFill` | 28 | Linear(0.85, 1) | starts at 85% fill |
| `DefenseRatio` | 30 | Inverse(0, 1.2, 1.5) | current/desired defense |
| `ResourceShortfall` | 32 | Quadratic(0, 1, exp 0.7) | concave shortfall |
| `ResourceRefill` | 34 | Linear(0, 96) | ticks of income needed to refill |
| `GoldDeficit` | 36 | Linear(0, 1) | upkeep deficit fraction |

Inline literals (`NeedEvaluator › …`): food `daysNeed ×0.6` when a fix is pending, `netNeed ×0.6` above 90 days, cap 0.1 at ≥ 95% storage.
Resource reserve `tierScale = 1.5 + 0.25·tier`. Bootstrap 0.65 / 0.5. Chain need `0.45 + 0.35·(1 − stock/season)`. Upstream ×0.7.
Service weights: Faith/Research `0.25 + 0.4·trait` (×1.25 on matching focus), Health 0.5, Water 0.55, Entertainment 0.4, Administration 0.4, Power 0.35, capped at 0.85.
Storage `0.3 + 0.5·fill`, damped ×0.3 while pending and ×0.2 if the last storage order did not raise capacity.
Defense desired `1 + pop·(0.02 + 0.1·threat) + 3·threat + 3 at war`. Threat floors: defensive 0.4, militarist 0.55, hostile near 0.65, war 0.85 / 1.0, `0.6·ai.threat`, `min(pop/400, 0.15)`.
Commerce `0.35 + 0.5·deficit` (×0.5 with 2 years of upkeep banked, ×1.15 on wealth focus).

### 1.3 Scoring factors (`BuildingScorer.cs`)
| Factor | line | Value |
|---|---|---|
| Favored / Unique race bias | 142–143 | ×1.3 / ×1.15 |
| Category × trait | 147–152 | 0.85 + 0.3·trait (Industry/Resource 0.9 + 0.2·Industriousness) |
| Labor | ~165–172 | 1 / 0.75 / 0.3; 1.15 with many idle workers; food at need ≥ 0.75 → 1; soldiers at war ≥ 0.85 |
| Affordability | 203 | 1 now / 0.6 within a year / 0.25 |
| Modernity | 217–219 | ×1.12 current age (×1.25 if the age is not adopted yet, except Dawn); ×0.92 last age before obsolete; ×0.92 if ≥ 3 ages old |
| Repeat | 231 | ×0.7 inside `RepeatCooldownTicks` |
| Saturation | 239 | ×0.25 at ≥ 2 + pop/5 copies |
| Inputs | 253 | ×0.5 when an input is unsupplied |
| Redundancy | 264 | −0.25 per queued and −0.12 per building (dwellings −0.05 / −0.03) |

### 1.4 Job priorities (`SettlementGovernorSystem › WriteJobPriorities`, ~line 470–510)
Food jobs `2 + (20 − days)/20` below 20 days, 1.5 below 40, else `1 + 0.3·foodNeed` · Builder 1.5 with work · Soldier 0.5 / 1.5 / 2 / 2.5 (line 495) ·
Scholar/Priest `0.75 + trait + 0.5·service` (498–499) · Merchant `1.5 + 0.5·gold` or `0.8 + 0.4·Mercantilism` · then `+ AdvisorBoard.JobBias`, clamped to
[`MinJobPriority` 0.1 (:25), `MaxJobPriority` 5 (:27)]. Growth EMA α = 0.35 (line 162).

## 2. Advisor Council (`AI/Advisors/AdvisorBoard.cs`)

| Constant | line | Meaning | Default | Safe range |
|---|---|---|---|---|
| `MaxContribution` | 29 | max \|strength\| per vote | 1 | fixed (advisors assume ±1) |
| `MaxTotal` | 30 | accumulated bias clamp | 3 | 2–4 |
| `DecayPerCouncil` | 31 | multiplier every council run | 0.5 | 0.5–0.8 |
| `AdvisorCouncilSystem.TickInterval` | 185 | decay cadence | 6 | 6–12 |
| `AdvisorSystemBase.TickInterval` | 204 | default advisor cadence | 12 | 6–48 |
| `Weight` clamp | 89 | learned weight | [0, 3], default 1 | 0.3–2 in practice |
| log threshold | 60 | log a vote when \|v\| ≥ | 0.5 | 0.5–1 |

**Interval and decay interaction.** If an advisor with interval *I* votes *v* every run, then just after its vote the bias settles at
`v / (1 − 0.5^(I/6))`. Before its next vote it falls to `v·0.5^(I/6)/(1 − 0.5^(I/6))`. Effective mean influence:

| Interval | Peak | Trough | Note |
|---|---|---|---|
| 6 (`recovery`, `seasons`) | 2.0v | 1.0v | strongest per unit vote |
| 12 (`food`, `growth`, `faith`, `supply`, `labor`, `logistics`, `defense`, `war`) | 1.33v | 0.33v | |
| 24 (`science`, `treasury`, `health`, `happiness`, `race_*`, `ages`, `frontier`, `urban`, `capital`, `god`) | 1.07v | 0.13v | |
| 48 (`specialize`, `prospect`) | 1.004v | ~0.004v | almost invisible 3/4 of the time |

To make cadences comparable, either set `DecayPerCouncil = 0.5^(6/I_ref)` per advisor (requires a contract change) or
scale strengths by `I/12`. **Do not** fix this with `ai.w`, because the learner will undo it.

### 2.1 Advisor constants (landed advisors)
| Advisor | Constant | file:line | Default | Safe range |
|---|---|---|---|---|
| food | `MinFit` | Advisors/Food/FoodSecurityAdvisor.cs:29 | 0.15 | 0.1–0.3 |
| food | `ObsoleteRatio` | :30 | 0.7 | 0.6–0.85 |
| food | `StrongVote` | :31 | 0.5 | 0.4–0.8 |
| food | `FamineMemoryTicks` | :32 | 192 | 96–384 |
| food | `Horizon` / `Step` | Food/FoodSecurityForecast.cs:84/85 | 192 / 2 | 96–288 / 2–4 |
| food | `MaxCoverage` | :86 | 9.99 | — |
| food | terrain radius | Food/FoodSecurityTerrain.cs:11–12 | 4–14 | — |
| growth | `DefaultBedsPerDwelling` | Growth/GrowthAdvisorModel.cs:12 | 4 | 3–5 |
| growth | `DefaultWaterPerCapita` | :14 | 0.12 | — |
| growth | `BaselineMortality` | :16 | 0.006 | — |
| growth | `SaurianClutch` / `TwinFactor` | :20 / :22 | 2 / 1.02 | — |
| growth | `MaxGrowthRate` / `MaxDeclineRate` | :24 / :25 | 0.6 / 0.5 | — |
| growth | `TrendTimeConstantYears` | :27 | 1 | 0.5–2 |
| growth | `SamplesForFullTrust` / `MaxTrendTrust` | :29 / :31 | 6 / 0.5 | 4–10 / 0.3–0.7 |
| growth | `MinWaterPopulation` / `WaterCoverageTarget` | :33 / :35 | 8 / 0.9 | 6–15 / 0.7–0.95 |
| faith | `GraceWindowTicks` | Faith/FaithAdvisor.cs:32 | 144 | 96–288 |
| faith | `RebuildWindowTicks` | :34 | 192 | 96–384 |
| faith | `SacredRescanTicks` | :36 | 96 | 48–192 |
| faith | `PerceptionMargin` | :38 | 5 | 3–10 |
| faith | `TempleClassFaith` | Faith/FaithAdvisorLadder.cs:15 | 2.5 | — |
| health | `SpikeDecay` / `HazardDecay` | Health/HealthAdvisor.cs:27 / :28 | 0.55 / 0.6 | 0.4–0.8 |
| health | `EmaAlpha` | :29 | 0.3 | 0.2–0.5 |
| health | `NeighborReach` | :30 | 36 | 24–64 |
| health | `HotThreshold` / `MaxHazard` | :31 / :32 | 0.15 / 1.5 | — |
| science | `CrisisStart` / `CrisisFull` / `CrisisBackOff` | Science/ScienceAdvisor.cs:28/30/32 | 0.3 / 0.75 / 0.6 | keep Start < BackOff < Full |
| science | `WonderBehindMin` | :34 | 0.35 | 0.2–0.5 |
| science | `BaseYearsPerAge` | Science/ScienceKeys.cs:45 | 100 | 80–150 (arena expects ~110) |
| science | `ResearchPerPersonPerTick` | :47 | 0.004 | — |
| science | `RateAlpha` / `RateAlphaFast` | :49 / :51 | 0.3 / 0.6 | — |
| science | `BehindThreshold` / `AheadThreshold` | Science/SciencePacePlan.cs:17 / :19 | 0.05 / −0.3 | — |
| supply | `RescanTicks` / `ReachMargin` | Supply/SupplyChainTerrain.cs:51 / :53 | 96 / 3 | — |
| treasury | `GoldPerCommercePoint` | Treasury/TreasuryBuildingBook.cs:30 | 0.02 | — |
| treasury | `PrestigeUpkeep` | :32 | 0.08 | — |
| treasury | `MaxYears` | Treasury/TreasuryLedger.cs:14 | 99 | — |
| race_human | `MarketFirst` … `BarracksVote` | Races/Human/HumanCultureAdvisor.cs:39–55 | 0.25–1.0 | ±0.2 per vote |
| race_human | `GathererSuppress` / `HunterSuppress` | :41 / :42 | −0.6 / −0.3 | ≥ AdvisorVetoBias/2, otherwise it vetoes |
| race_human | `FamineFoodDays` | :57 | 30 | 20–40 |
| race_human | `StarvationMemoryTicks` / `PostWarWindowTicks` | :58 / :59 | 96 / 384 | — |
| defense | `ArmyRadius` / `SettlementReach` / `SettlementNear` | Defense/DefenseAdvisorKeys.cs:45/47/49 | 30 / 64 / 10 | — |
| defense | `ProjectionFraction` | :51 | 0.4 | — |
| defense | `BattleMemoryTicks` / `ShockMemoryTicks` | :53 / :55 | 144 / 96 | — |
| defense | `AlertDecayPer12` | :57 | 0.8 | 0.6–0.9 |
| ages | `WaveTicks` … `SiteRefreshTicks` | Ages/AgesKeys.cs:38–60 | see file (`PrepStart` 0.85, `ObsoleteNegative` 0.6, `SunsetNegative` 0.55) | — |

Advisors that landed during the final scan and are not yet tabulated here: `labor`, `logistics`, `recovery` (6), `seasons` (6),
`urban`, `capital`, `frontier`, `prospect` (48), `specialize` (48), `race_elf`, `race_dwarf`, `race_orc`, `race_saurian`. Their constants are
in each folder's `*Keys.cs`, `*Catalog.cs` or `*Tuning`.

## 3. Civ Mind (`AI/Strategy`)

| Constant | file:line | Meaning | Default | Safe range |
|---|---|---|---|---|
| `CivilizationStrategySystem.Interval` | Strategy/CivilizationStrategySystem.cs:31 | cadence | 24 | 24–48 |
| `WonderPriority` | :33 | wonder order priority | 6 | **raise to 12–20** (governor orders are u×10) |
| `WonderAffordFraction` | :35 | stock fraction needed to queue | 0.5 | 0.3–0.7 |
| `WonderRetryTicks` | :37 | per-wonder retry | 288 | 192–480 |
| `Hysteresis` | :39 | posture and focus stickiness | 0.1 | 0.05–0.2 |
| `UrgentMargin` | :41 | score lead that overrides the dwell time | 0.5 | 0.3–0.8 |
| `MinDwellTicks` | :43 | minimum time in a posture or focus | 96 | 48–192 |
| `MinNeighbourRadius` | :45 | hostile neighbour radius | 40 | 30–60 |
| `ArmyThreatRadius` | :47 | hostile army near our towns | 16 | 12–30 |
| `RecoveryTicks` | :49 | recovery-focus fade | 192 | 96–384 |
| `MinRecoverySeverity` | :51 | disaster severity that triggers recovery | 0.2 | 0.1–0.4 |
| `PopulationCrashFraction` | :53 | pop ≤ this × recent peak counts as a disaster | 0.8 | 0.7–0.9 |
| `WonderMinTrend` | :55 | pause wonders below this economy trend | −0.15 | −0.3 to 0 |
| posture weights | `› UpdatePosture` (~286–303) | see AI_DESIGN §3.1 (incl. weariness −1.0 militarist / +0.9 peaceful, armyNear +0.45 defensive, fear/gratitude ±0.15–0.2) | — | ±20% |
| focus weights | `› UpdateFocus` (~395–406) | see AI_DESIGN §3.1 | — | ±20% |
| `ResearchDirectorSystem.Interval` | Strategy/ResearchDirectorSystem.cs:18 | cadence | 24 | — |
| `FoodCrisisDays` | :20 | food-crisis trigger | 20 | 15–30 |
| tech score multipliers | `› Score` | focus ×2, war ×2 (militarist ×1.3), food crisis ×3, current-age +1 / +2.5 | — | — |
| `ExpansionSystem.Interval` | Strategy/ExpansionSystem.cs:28 | cadence | 48 | 24–96 |
| `MaxSiteCandidates` | :30 | samples per attempt | 400 | 200–800 (perf) |
| `MinParentPopulation` / `…Orc` | :32 / :34 | parent pop | 30 / 22 | 20–45 |
| `MinParentFoodDays` | :36 | parent food | 25 | 20–40 |
| `MinParentHappiness` | :38 | parent happiness | 0.4 | 0.3–0.55 |
| `MinSettlers` / `MaxSettlers` / `AbsoluteMinSettlers` | :40/:42/:44 | colony size | 6 / 10 / 4 | — |
| `MaxSettlerFraction` | :46 | parent share that may leave | 0.25 | 0.15–0.35 |
| `MinBuildableTiles` / `BuildableRadius` | :48 / :50 | site room | 40 / 5 | 30–60 |
| `ScoreRadius` / `MinAverageHabitability` | :52 / :54 | site quality | 4 / 0.15 | — |
| `MinCooldownYears` / `MaxCooldownYears` | :56 / :58 | expansion cooldown, lerped by Expansionism | 4 / 14 | 3–8 / 10–20 |
| `SupplyFood/Wood/Stone` | :60/:62/:64 | colony start kit | 40 / 30 / 10 | — |
| `ParentFoodReserveDays` | :66 | parent keeps | 15 | 10–25 |
| `BlockedRetryTicks` | :68 | failed-parent cooldown | 192 | 96–384 |
| `DefaultFoodPerTick` / `NoConsumptionFoodDays` | Strategy/StrategyHelpers.cs:9 / :11 | fallbacks | 0.05 / 999 | — |

## 4. Institutions and feedback (`AI/{Envoys,GodPerception,Personality,Learning,Governance,WarPlanning}`)

| Constant | file:line | Default | Notes |
|---|---|---|---|
| `PactMinOpinion` | Envoys/EnvoyTuning.cs:15 | 30 | |
| `GiftMinStock` / `GiftMaxShare` / `GiftChancePerYear` / `GiftCooldownYears` | :21 / :23 / :35 / :37 | 60 / 0.10 / 0.8 / 2 | |
| `TributeStrengthRatio` / `DesperateStrengthRatio` | :45 / :47 | 2 / 4 | |
| `TributeMinBullyAggression` / `TributeShare` | :49 / :53 | 0.55 / 0.15 | |
| `HegemonRatio` / `HegemonLeaveRatio` | :71 / :73 | 1.6 / 1.3 | coalition trigger and hysteresis |
| `PactYears` / `PactChancePerYear` | :87 / :95 | 10 / 0.6 | |
| `BetrayalYears` / `BetrayalShock` | :99 / :101 | 60 / −35 | |
| `MarriageChancePerYear` / `MarriageImpulse` | :117 / :119 | 0.15 / 25 | |
| `GratitudeGain` / `FearGain` | GodPerception/GodPerceptionTuning.cs:11 / :12 | 0.22 / 0.28 | per divine act |
| `ResentGain` / `UncertaintyGain` | :13 / :14 | 0.30 / 0.30 | |
| `GratitudeRetention` / `FearRetention` | :23 / :24 | 0.97 / 0.94 | per 24-tick reference run |
| `MaxNudgePerRun` / `MaxPendingNudge` | :30 / :32 | 0.02 / 0.06 | devotion and favor nudges |
| `AdoreGratitude` | :51 | 0.75 | |
| `MaxStepPerRun` | Personality/PersonalityModel.cs:12 | 0.02 | trait drift speed |
| `MaxDeviation` | :13 | 0.35 | max distance from the race default; keep ≤ 0.35 so races stay distinct |
| `NotableDeviation` / `CalmDeviation` | :14 / :15 | 0.2 / 0.15 | band hysteresis |
| `ExperienceHalfLifeYears` | :16 | 30 | |
| `ChronicleCooldownYears` | :18 | 50 | |
| Learning trait gains | Learning/AdvisorDomains.cs:44–70 | per domain (e.g. science Curiosity 0.8, war Aggression 1.0 / Diplomacy −0.6) | prior weight = f(traits) |

## 5. Diplomacy and war (`Diplomacy/Relations/RelationsTuning.cs`, not AI-owned; coordinate with the Diplomacy owner)

| Constant | line | Default | | Constant | line | Default |
|---|---|---|---|---|---|---|
| `ContactRange` | 12 | 45 | | `AllyEnter` / `AllyLeave` | 48 / 50 | 60 / 45 |
| `OpinionDriftPerRun` | 16 | 0.10 | | `FriendlyEnter` / `FriendlyLeave` | 54 / 56 | 25 / 15 |
| `AffinityWeight` | 20 | 30 | | `NeutralFloor` | 58 | −25 |
| `TradeBonus` | 22 | 15 | | `MaxActiveWarsToDeclare` | 70 | 2 |
| `BorderFrictionPerPair` / `MaxBorderFriction` | 32 / 34 | 10 / 30 | | `HatredOpinion` / `HatredMinAggression` | 72 / 74 | −40 / 0.35 |
| `XenophobiaWeight` | 36 | 20 | | `OpportunistMinAggression` / `…StrengthRatio` | 76 / 80 | 0.6 / 1.3 |
| `RecentWarPenalty` / `RecentWarYears` | 38 / 40 | 25 / 20 | | `DeclarationChancePerYear` | 82 | 0.3 |
| `DiplomacyWeight` | 42 | 10 | | `StalemateYears` / `StalematePeaceChancePerYear` | 86 / 90 | 8 / 0.4 |
| `DecisiveScore` / `TributeFraction` | 92 / 94 | 60 / 0.3 | | `ExhaustionLossFraction` | 96 | 0.4 |
| `MaxWarYears` | 100 | 25 | | `TruceYears` | 102 | 10 |

Military (`Diplomacy/Military`): `ArmySystem.DecisionInterval` 12, `RetreatMorale` 0.3, `RestTicksAfterCampaign` 48 (ArmySystem.cs:24–42);
`MilitaryMath.RaiseFraction` 0.7, `MinSoldiersToRaise` 4, `CaptureWarScore` 25, `RazeAggression` 0.7 (MilitaryMath.cs:36–48).

## 6. Town Planner (`Settlements/Planning`)

| Constant | file:line | Default | Safe range |
|---|---|---|---|
| `MaxOrdersPerSettlementPerRun` | TownPlannerSystem.cs:24 | 2 | 1–3 |
| `MaxAttemptsPerOrder` | :26 | 4 | 3–6 |
| `MaxCandidatesPerOrder` / `MinCandidatesPerOrder` | :28 / :30 | 300 / 96 | perf-bound |
| `MaxTerritoryRadius` | :34 | 24 | 16–32 |
| `MaxConcurrentSites` / `PeoplePerExtraSite` | :36 / :38 | 6 / 12 | 4–8 / 8–16 |
| `WorkBudgetPerRun` | :43 | 1500 | perf-bound |
| `JitterAmount` / `PreferredSiteBonus` | :53 / :55 | 0.05 / 0.25 | ≤ 0.1 / 0.1–0.5 |
| `GridSpacing` | SiteScorer.cs:16 | 4 | 3–6 |
| `FertileThreshold` / `ForestTreeCount` | PlacementRules.cs:16 / :19 | 0.45 / 3 | — |
| `InfluenceRadius` / `ResidentialRadius` | ZoningMap.cs:16 / :18 | 4 / 3 | — |
| `RoadCost` / `MaxFieldTiles` / `FieldMinFertility` | PlannerRoads.cs:17 / :23 / :25 | 0.3 / 8 / 0.3 | — |
| `MaxLinkDistance` / `RetryDelayTicks` / `MaxFailures` | RoadNetworkSystem.cs:24 / :28 / :30 | 60 / 96 / 3 | — |
| Site-score weights | `SiteScorer › Score` lines 40–94 | outskirts 1.0, isolated 0.7, central 0.35 + 0.4·compact, zoning ±0.3 / −0.35, road +0.15, race style +0.15, grid +0.2 / −0.25, resources +0.3, fertility +0.3 | ±50% |

## 7. Using `tools/GodSim.AIArena`

The arena (landed: `SimRunner.cs`, `Scoring.cs`, `ArenaResults.cs`, `ReportWriter.cs`; the CLI entry point is still landing) runs seeded
headless worlds. It only observes: it subscribes to events and samples between ticks, and records crashes instead of throwing. It then writes
a 0–10 AI rating, a race table and a ranked problem list, each with a reproduce command.

```bash
# full benchmark (values illustrative; check the CLI help once Program.cs lands)
dotnet run -c Release --project tools/GodSim.AIArena -- --years 300 --size 128 --preset default
# reproduce a reported problem (exact form emitted by ReportWriter)
dotnet run -c Release --project tools/GodSim.AIArena -- --seed-list 1337 --years 300 --size 128 --preset default
```

**Rating** (`Scoring.cs:154–204`, weights sum to 1.0):

| Key | Weight | What it measures |
|---|---|---|
| `survival` | 0.20 | civs alive, starvation %, homeless % |
| `growth` | 0.15 | log growth multiple versus `min(12, 1 + years/15)` (0.7) + expansions per century (0.3) |
| `urban` | 0.15 | 3–5 people per house in-band % (0.3), people/house variance (0.15), tier fit (0.2), stall % (0.2), idle % (0.15) |
| `progression` | 0.15 | ages gained versus `years/110` (0.7) + techs per century / 8 (0.3) |
| `economy` | 0.10 | gold-solvent % (0.5) + food days / 60 (0.5) |
| `resilience` | 0.10 | rebuild after destruction, recovery time, peak retention |
| `diversity` | 0.05 | mean race distance / 0.25 |
| `drama` | 0.05 | wars per century in band 1..6 (soft 4), disasters 3..40, battles per war 1..30 |
| `performance` | 0.05 | ms/tick versus 1.5–6 ms, faults, disabled systems |

Grades: ≥ 9 excellent, ≥ 7.5 good, ≥ 6 fair, ≥ 4 weak. A crash caps the rating.

**Target metrics for 10/10** (derived from the scoring bands and problem detectors at `Scoring.cs:319–343`):

| Metric | Target | Problem detector |
|---|---|---|
| civ survival | 100% at 300 y | `collapse`, `decline` (< 75% of initial pop) |
| starvation deaths | < 2% of deaths | `starvation` (> 10% and ≥ 5 deaths) |
| homeless | < 3% avg | `homeless` (> 10%) |
| people per house | 3.5–4.5, ≥ 90% of samples in band | `overcrowded` (> 6), `overbuilt` (< 2.5) |
| construction stalled | < 10% | `stall` (> 35%) |
| idle adults | < 8% | `idle` (> 20%) |
| food days | ≥ 60 | — |
| gold solvent | ≥ 95% | `insolvent` (< 60%) |
| years per age | 90–130 | `stuck-age` (none in 90 y), `fast-age` (< 25 y) |
| expansions | ≥ 2 per century | `no-expansion` |
| wars | 1–6 per century, none > 40 y | `endless-war`, `no-war` |
| race diversity | mean distance ≥ 0.25 | — |
| tick cost | ≤ 1.5 ms avg at the arena's population | `perf` (> 3 ms) |
| AI log lines | read `AiLinesPerSettlementYear`; aim for 5–20 | — |

## 8. Tuning playbook

### 8.1 House spam (`overbuilt`, people/house < 2.5)
1. Check double counting first: the governor housing need **plus** `growth` House votes. Lower `GrowthAdvisor` pre-build strength or raise `AdvisorVetoBias` sensitivity before touching governor knobs.
2. Lower `FreeHousingFraction` (0.08 → 0.05) and/or `HousingLeadYears` (0.25 → 0.15).
3. Raise `MaxQueuedDwellingsPerType` only if *under*-built. For spam, set it to 1.
4. Verify `DefaultDwellingCapacity` matches the catalog's `HousingCapacity` range (3–5), and that `pop.freeHousing` is written (otherwise the governor falls back to `cap − pop`).
5. Check `RedundancyPenalty` for dwellings (0.05 / 0.03 is mild on purpose).

### 8.2 Starvation spirals (`starvation`, food days ↘)
1. Look at `ai.food.sit` and `ai.need.food` in the inspector. If the need is high but no farm is ordered, check `LaborFactor` (no workers → 0.3; food bypasses it only at need ≥ 0.75) and `Affordability`.
2. Lower the `FoodReserveDays` midpoint (0.4 → 0.5 shifts urgency earlier) or raise the `FoodNetRatio` weight (0.85).
3. Make sure Science backs off: `CrisisBackOff` 0.6 needs `food` urgency published. Expansion should not drain the parent: raise `MinParentFoodDays` to 30–35 or `ParentFoodReserveDays` to 20.
4. Raise `food` `StrongVote` or the food-job priority floor (`WriteJobPriorities`: 2–3 below 20 days).
5. Check that a winter is not the real cause. Tune `FoodSecurityForecaster.Horizon` and the winter buffer by Expansionism.

### 8.3 Stalled construction (`stall`, stuck builds)
1. Unaffordable orders hold stock reserved "ahead". Lower `StallTicks` (96 → 48) so `BreakStall` drops them sooner.
2. Raise the effective `WonderPriority`, or keep it and set `WonderAffordFraction` to 0.7 so wonders are queued only when nearly funded.
3. Check `Builder` priority (1.5) against `DesiredBuilders`. Raise it to 2 when the queue has more than 2 entries.
4. Placement: the governor blocks after 2 failures and the planner drops after 4. Align them (`PlacementFailureAttempts = MaxAttemptsPerOrder − 1`). If the log shows `Gave up … (no space)`, raise `MaxTerritoryRadius`.
5. `supply` must see construction demand. Check that `ai.supply.worst` names the missing resource.

### 8.4 Endless wars (`endless-war` > 40 y)
1. `MaxWarYears` is 25 in `RelationsTuning`. A war lasting more than 40 years means wars are being re-declared, not prolonged. Raise `TruceYears` (10 → 15) and `RecentWarPenalty`.
2. Feed `war.weariness` into peace: raise `StalematePeaceChancePerYear` by weariness, or end the war when both sides exceed 0.8. This needs a Diplomacy-owner change; the key is ready.
3. Lower `DeclarationChancePerYear` (0.3 → 0.2) or raise `HatredMinAggression`.
4. Check posture: militarist `a·0.9` with a war bonus `0.3 + 0.8a` locks Orcs. Raise `Hysteresis` only if posture flip-flops. Otherwise reduce the war term.

### 8.5 Identical races (`diversity` < 5)
1. Confirm the race culture advisors are **registered**. Today none run in real games (AI_DESIGN §14.1).
2. Raise `AdvisorBiasScale` (0.35 → 0.45) so culture votes matter. Check the slow-advisor decay asymmetry (§2): race advisors run every 24 ticks.
3. Widen race data: `FavoredBuildings` (×1.3), trait spread, `Compactness`/`RoadAffinity`. Race distinctness should come from data first.
4. Keep `Personality.MaxDeviation` ≤ 0.35 so drift never erases the race default.
5. Migrate static-trait readers (Strategy, ResearchDirector, Expansion, NeedEvaluator, Diplomacy) to `AdvisorBoard.Trait` so drift and culture actually diverge behavior.
