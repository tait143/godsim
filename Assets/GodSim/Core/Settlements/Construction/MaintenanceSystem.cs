using System;
using System.Collections.Generic;

namespace GodSim.Core.Settlements
{
    /// <summary>
    /// Upkeep of finished buildings, and how towns change through the ages (runs every 12 ticks).
    /// <list type="bullet">
    /// <item><b>Decay:</b> complete buildings lose 0.5% Condition per year, x2 for a job building with no
    /// workers and x1.5 for a building whose style is two or more ages behind its civilization. At
    /// Condition &lt;= 0 the building collapses (<see cref="DestroyCause.Decay"/>).</item>
    /// <item><b>Repair:</b> up to three buildings below 0.7 Condition per settlement per run (worst first) are
    /// repaired by +0.2 for 2 Wood + 1 Stone per footprint tile (or 4 Wood per tile when there is no stone).</item>
    /// <item><b>Renovation:</b> buildings whose <see cref="Building.StyleAge"/> lags the civilization's age (or
    /// whose style race differs after a conquest) are rebuilt in the current style, 1-3 per settlement per
    /// run by tier, oldest style first. Each costs 30% of the building's cost (only while the stockpile
    /// keeps a reserve). Dwellings that cannot be paid for renovate for free, but slower (a chance per run).
    /// Renovation sets StyleAge/StyleRace, increments Level, restores Condition and publishes
    /// <see cref="BuildingUpgradedEvent"/>.</item>
    /// <item><b>Obsolescence:</b> buildings past their definition's MaxAge are demolished
    /// (<see cref="DestroyCause.Demolished"/>) once they have no workers or residents, at most one per settlement per run.</item>
    /// </list>
    /// </summary>
    public sealed class MaintenanceSystem : ISimSystem
    {
        /// <summary>Base Condition lost per year by a complete building.</summary>
        public const float BaseDecayPerYear = 0.005f;
        /// <summary>Decay multiplier for a job building with no workers.</summary>
        public const float UnstaffedDecayMultiplier = 2f;
        /// <summary>Decay multiplier for buildings styled two or more ages behind their civilization.</summary>
        public const float OutdatedDecayMultiplier = 1.5f;
        /// <summary>Age gap at which <see cref="OutdatedDecayMultiplier"/> applies.</summary>
        public const int OutdatedAgeGap = 2;
        /// <summary>Buildings below this Condition are repaired when resources allow.</summary>
        public const float RepairThreshold = 0.7f;
        /// <summary>Condition restored per repair.</summary>
        public const float RepairAmount = 0.2f;
        /// <summary>Maximum repairs per settlement per run.</summary>
        public const int MaxRepairsPerRun = 3;
        /// <summary>Wood per footprint tile for a repair.</summary>
        public const float RepairWoodPerTile = 2f;
        /// <summary>Stone per footprint tile for a repair.</summary>
        public const float RepairStonePerTile = 1f;
        /// <summary>Wood per footprint tile for a repair when there is no stone.</summary>
        public const float RepairWoodOnlyPerTile = 4f;
        /// <summary>Fraction of the building's cost paid for a renovation.</summary>
        public const float RenovationCostFraction = 0.3f;
        /// <summary>Extra fraction of the cost that must remain in stock after paying (keeps construction funded).</summary>
        public const float RenovationReserveFraction = 0.3f;
        /// <summary>Chance per run that an unaffordable dwelling renovates for free.</summary>
        public const float FreeDwellingRenovationChance = 0.25f;
        /// <summary>Maximum free dwelling renovations per settlement per run.</summary>
        public const int MaxFreeDwellingRenovationsPerRun = 1;
        /// <summary>Upper bound for <see cref="Building.Level"/>.</summary>
        public const int MaxLevel = 1000;

        private readonly List<Building> _buildings = new List<Building>();
        private readonly List<Building> _collapsed = new List<Building>();
        private readonly List<Building> _repairs = new List<Building>();
        private readonly List<Building> _renovations = new List<Building>();
        private readonly ResourceAmount[] _repairCost = new ResourceAmount[2];
        private readonly ResourceAmount[] _repairWoodOnly = new ResourceAmount[1];

        /// <inheritdoc />
        public string Name => "Maintenance";

        /// <inheritdoc />
        public int TickInterval => 12;

        /// <summary>Renovations allowed per settlement per run: 1 up to Village, 2 for Town/City, 3 beyond.</summary>
        public static int RenovationsPerRun(SettlementTier tier) =>
            tier <= SettlementTier.Village ? 1 : (tier <= SettlementTier.City ? 2 : 3);

        /// <summary>Decay multiplier for a building (unstaffed job building x2, two+ ages outdated x1.5). civ may be null.</summary>
        public static float DecayMultiplier(BuildingDefinition def, Building b, Civilization civ)
        {
            if (b == null) return 1f;
            float m = 1f;
            if (def != null && def.HasJobs && b.Workers.Count == 0) m *= UnstaffedDecayMultiplier;
            if (civ != null && ConstructionShared.AgeGap(civ.Age, b.StyleAge) >= OutdatedAgeGap) m *= OutdatedDecayMultiplier;
            return m;
        }

        /// <summary>True if the building's look lags its civilization (older age style, or another race's style).</summary>
        public static bool NeedsRenovation(Building b, Civilization civ)
        {
            if (b == null || civ == null) return false;
            return b.StyleAge < civ.Age || (b.StyleRace != civ.Race && b.StyleAge <= civ.Age);
        }

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { ClearScratch(); }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            int dt = Math.Max(0, ctx.DeltaTicks);
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                UpdateSettlement(ctx, s, dt);
            }
            ClearScratch();
        }

        private void ClearScratch()
        {
            _buildings.Clear();
            _collapsed.Clear();
            _repairs.Clear();
            _renovations.Clear();
        }

        private void UpdateSettlement(SimContext ctx, Settlement s, int dt)
        {
            var state = ctx.State;
            var db = ctx.Db;
            var civ = ConstructionShared.CivOf(state, s);
            ClearScratch();

            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = state.Buildings.Get(ids[k]);
                if (b != null && !b.IsDestroyed && b.SettlementId == s.Id) _buildings.Add(b);
            }
            if (_buildings.Count == 0) return;

            float decayBase = BaseDecayPerYear * dt / SimTime.TicksPerYear;
            Building obsolete = null;
            for (int k = 0; k < _buildings.Count; k++)
            {
                var b = _buildings[k];
                if (!db.TryGetBuilding(b.Type, out var def)) continue;
                bool isObsolete = civ != null && def.MaxAge < civ.Age;
                if (isObsolete && b.Type != BuildingType.TownCenter && !def.IsWonder
                    && b.Workers.Count == 0 && b.Residents.Count == 0
                    && (obsolete == null || b.Id < obsolete.Id))
                    obsolete = b;
                if (!b.IsComplete) continue;

                float condition = ConstructionShared.SafeCondition(b.Condition) - decayBase * DecayMultiplier(def, b, civ);
                condition = MathUtil.Clamp01(MathUtil.Sanitize(condition, 0f));
                b.Condition = condition;
                if (condition <= 0f) { _collapsed.Add(b); continue; }
                if (condition < RepairThreshold && !b.IsOnFire && !isObsolete) InsertByCondition(_repairs, b);
                if (!isObsolete && !def.IsWonder && !b.IsOnFire && NeedsRenovation(b, civ)) InsertByStyle(_renovations, b);
            }

            // ---- collapse
            for (int k = 0; k < _collapsed.Count; k++)
            {
                var b = _collapsed[k];
                if (b.IsDestroyed) continue;
                db.TryGetBuilding(b.Type, out var def);
                ctx.Ops.DestroyBuilding(b, DestroyCause.Decay);
                s.LogAi(ConstructionShared.NameOf(def, b) + " collapsed from neglect");
            }

            // ---- repair (worst first)
            int repairs = 0;
            for (int k = 0; k < _repairs.Count && repairs < MaxRepairsPerRun; k++)
            {
                var b = _repairs[k];
                if (b.IsDestroyed) continue;
                if (TryPayRepair(s, b))
                {
                    b.Condition = MathUtil.Clamp01(b.Condition + RepairAmount);
                    repairs++;
                }
            }

            // ---- obsolescence (one per run)
            if (obsolete != null && !obsolete.IsDestroyed)
            {
                db.TryGetBuilding(obsolete.Type, out var odef);
                ctx.Ops.DestroyBuilding(obsolete, DestroyCause.Demolished);
                s.LogAi("Demolished obsolete " + ConstructionShared.NameOf(odef, obsolete) + " (unused since the " + (civ != null ? civ.Age.ToString() : "new") + " age)");
            }

            // ---- renovation (oldest style first)
            if (civ == null || _renovations.Count == 0) return;
            int budget = RenovationsPerRun(s.Tier);
            int free = 0;
            for (int k = 0; k < _renovations.Count && budget > 0; k++)
            {
                var b = _renovations[k];
                if (b.IsDestroyed || !b.IsComplete) continue;
                if (!db.TryGetBuilding(b.Type, out var def)) continue;
                bool ok = s.Stockpile.CanAfford(def.Cost, RenovationCostFraction + RenovationReserveFraction)
                          && s.Stockpile.TryPay(def.Cost, RenovationCostFraction);
                if (!ok && def.IsDwelling && free < MaxFreeDwellingRenovationsPerRun && ctx.Rng.Chance(FreeDwellingRenovationChance))
                {
                    ok = true;
                    free++;
                }
                if (!ok) continue;
                Renovate(ctx, b, civ);
                budget--;
            }
        }

        private static void Renovate(SimContext ctx, Building b, Civilization civ)
        {
            if (b.StyleAge < civ.Age) b.StyleAge = civ.Age;
            b.StyleRace = civ.Race;
            b.Level = MathUtil.Clamp(b.Level, 0, MaxLevel - 1) + 1;
            b.Condition = 1f;
            ctx.Events?.Publish(new BuildingUpgradedEvent { BuildingId = b.Id, NewLevel = b.Level, StyleAge = b.StyleAge });
        }

        private bool TryPayRepair(Settlement s, Building b)
        {
            int tiles = ConstructionShared.Footprint(b);
            _repairCost[0] = new ResourceAmount(ResourceType.Wood, RepairWoodPerTile * tiles);
            _repairCost[1] = new ResourceAmount(ResourceType.Stone, RepairStonePerTile * tiles);
            if (s.Stockpile.TryPay(_repairCost)) return true;
            _repairWoodOnly[0] = new ResourceAmount(ResourceType.Wood, RepairWoodOnlyPerTile * tiles);
            return s.Stockpile.TryPay(_repairWoodOnly);
        }

        // Stable insertion sorts (deterministic; lists are small).
        private static void InsertByCondition(List<Building> list, Building b)
        {
            int pos = list.Count;
            list.Add(b);
            while (pos > 0 && (list[pos - 1].Condition > b.Condition || (list[pos - 1].Condition == b.Condition && list[pos - 1].Id > b.Id)))
            {
                list[pos] = list[pos - 1];
                pos--;
            }
            list[pos] = b;
        }

        private static void InsertByStyle(List<Building> list, Building b)
        {
            int pos = list.Count;
            list.Add(b);
            while (pos > 0 && (list[pos - 1].StyleAge > b.StyleAge || (list[pos - 1].StyleAge == b.StyleAge && list[pos - 1].Id > b.Id)))
            {
                list[pos] = list[pos - 1];
                pos--;
            }
            list[pos] = b;
        }
    }
}
