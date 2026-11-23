# ADR-0007: Housing rule: 3 to 5 residents per dwelling

- **Status:** Accepted
- **Date:** 2026-11-19
- **Deciders:** lead, game design
- **Related:** [ENGINEERING_CONTRACT §5](../ENGINEERING_CONTRACT.md), `Contracts/Data/GameDatabase.cs` (`Validate`), `Contracts/State/WorldOps.cs` (`CreateBuilding`), `Core/Population/Society/HousingSystem.cs`

## Context

The core fantasy of GodSim is watching towns grow **one building at a time**. If housing capacity
were large or abstract, towns would look static while the population exploded. If it were one person
per house, settlements of 5,000 people would need 5,000 buildings, which would blow the planner and
render budgets. Houses also carry race and age identity (`StyleRace`, `StyleAge`). They are the most
common building, so they are the main way the player sees cultural and technological change.

## Decision

- A `House` holds **3 to 5 residents**. `GameDatabase.Validate()` rejects any `House` definition
  where `HousingMin != 3` or `HousingMax != 5` ("core design rule").
- `HousingCapacity` is **rolled per building at placement**
  (`Rng.RangeInclusive(HousingMin, HousingMax)` in `WorldOps.CreateBuilding`). Streets therefore
  show natural variation, and the average is about 4 people per house.
- Settlements grow by **one dwelling per 3 to 5 people**. The AI governor keeps free housing at or
  above `max(2, 8% of population)` (`GovernorSettings`). `HousingSystem` publishes `pop.freeHousing`,
  `pop.housingCapacity` and `pop.dwellings` for the AI.
- Homeless people (`Settlement.Homeless`) lose happiness and health. Construction prioritizes
  dwellings while anyone is homeless, and the planner may exceed its site cap by one House.
- Other dwellings (for example late-age high-density housing) may use other ranges. Only `House` is
  pinned to 3 to 5.
- Houses look different by race and by the age they were built in. `MaintenanceSystem` renovates
  houses that lag the civilization's age.

## Consequences

### Positive
- Growth is visible and legible: about 250 houses for 1,000 people.
- The rule is simple enough to explain in one sentence in the README and the HUD.
- It is enforced in one place (`Validate`) and cannot be broken by content edits.

### Negative / costs
- Large cities produce many House entities. The planner, renderer and pathing must handle hundreds
  of small buildings per settlement (`MaxBuildingsPerSettlement = 600` by default).
- Households (families) must fit into 3 to 5 slots. `HousingSystem` groups households and may split
  large families.
- Capacity is fixed at placement. Renovation does not re-roll it, so persistence must save it.

## Alternatives considered

### Abstract housing capacity per settlement tier
Cheap, but it removes the "one building at a time" fantasy.

### Fixed 4 residents per house
Uniform streets and fewer interesting edge cases for the AI. The 3 to 5 roll adds variety at zero
cost.

### Apartment blocks from the Industrial age replacing houses
Can be added as separate dwelling types. The House rule stays the baseline.
