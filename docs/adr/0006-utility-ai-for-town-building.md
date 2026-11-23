# ADR-0006: Utility AI for town building

- **Status:** Accepted
- **Date:** 2026-11-19
- **Deciders:** lead, AI owners, settlements owners
- **Related:** [ARCHITECTURE §6](../ARCHITECTURE.md#6-data-flow-the-build-pipeline), `Core/AI/Governor/`, `Core/AI/Strategy/`, `Core/Settlements/Planning/TownPlannerSystem.cs`

## Context

Five races across ten ages must grow believable towns **one building at a time**, with no player
input, for dozens of settlements at once, deterministically and inside the per-tick budget. The
AI has to balance housing, food, storage, services, defense, research, faith and wonders. Each race
has its own flavor (Orcs militarist, Elves pious and scholarly). The AI cannot reference the
economy, population or settlement modules' types (module boundaries).

## Decision

We will use a **two-level utility AI** that writes intents into shared state, plus a separate
**planner** that turns intents into placed buildings.

1. **Civilization level (`AI/Strategy/`, every 24 to 48 ticks):** `CivilizationStrategySystem`
   (posture and focus from race personality, threat and power ratio), `ResearchDirectorSystem`
   (scores techs and sets `CurrentResearchTechId`) and `ExpansionSystem` (founds colonies). Results
   go to `Civilization.Memory` (`ai.posture`, `ai.focus`, ...).
2. **Settlement level (`AI/Governor/SettlementGovernorSystem`, every 6 ticks):**
   1. `NeedEvaluator` computes a `SettlementNeeds` vector through `ResponseCurve`s. The vector has
      slots for housing, storage and defense, one per resource and one per service, each 0 to 1.
      Its inputs come from blackboards and state: `pop.freeHousing`, `econ.foodDays`,
      `ServiceCoverage[]`, `Stockpile.Net`, and `ai.threat`/`ai.focus` from the civ strategy.
   2. Candidates are filtered by `db.IsBuildingAvailable`, tier, population, `MaxPerSettlement`
      and hard placement feasibility, using a terrain cache from `TerritoryScanner`.
   3. `BuildingScorer` rates each candidate:
      `utility = sum(need_k x contribution_k) x raceBias x labor x affordability x modernity x repeat x saturation x inputs - redundancy`.
      The contributions come from `BuildingProfile`, which derives them from the building's data.
   4. The best options are appended as `BuildOrder { Type, Priority, Reason }` to
      `Settlement.BuildQueue`, and `JobPriority[]` is written.

   `GovernorSettings` holds the knobs: free housing at or above `max(2, 8% of pop)`, a queue of at
   most 4, Houses stacking up to 2, and stale orders dropped after 3 years. Diagnostics go to
   `ai.need.*`, `ai.gov.*` and `Settlement.LogAi`.
3. **Planner (`Settlements/Planning/TownPlannerSystem`, every 2 ticks):** validates orders, applies
   hard `PlacementRules`, ranks sites with `SiteScorer` and `ZoningMap`, and places through
   `Ops.CreateBuilding`. The AI decides **what**. The planner decides **where**.

Ties are always broken deterministically (lowest id or enum value), and `BuildOrder.Reason` plus
`Settlement.LogAi` record why each decision was made.

## Consequences

### Positive
- Behavior is tunable through data (curves, weights, race personality), without code paths per race.
- Every decision is explainable (`Reason`, `AiLog`), which helps the debug HUD and balancing.
- The interval-based evaluation is cheap, and the planner caps work per settlement (at most 2
  placements per run).
- AI and planner can be tested separately. The interface is a plain `BuildOrder` list.

### Negative / costs
- Utility systems need tuning. Badly shaped curves cause oscillation (build, demolish, build).
  Hysteresis and thresholds in `GovernorSettings` mitigate this.
- The AI sees other modules only through blackboard keys. If a key is renamed or missing, the AI
  silently reads the fallback value. Key names are part of the cross-module contract (ADR-0008).
- The governor (#26) runs before the planner (#28) in the same tick. Whether a new order is placed
  that tick depends on both systems' stagger phases. In the worst case it waits 2 ticks.

## Alternatives considered

### Behavior trees / GOAP
Good for agents with sequential plans. Town building is a continuous priority-balancing problem,
which suits utility scoring better. GOAP search cost scales badly with 75 building types.

### Fixed build orders per race and age
Simple, but brittle. Towns would not react to famine, disasters, god powers or terrain.

### Planner chooses both what and where
Merges two concerns that two different teams own. It would also make "why was this built?" harder
to answer.
