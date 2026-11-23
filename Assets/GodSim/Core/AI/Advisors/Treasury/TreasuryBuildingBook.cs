using System;

namespace GodSim.Core.AI
{
    /// <summary>How the treasury sees a building type (derived purely from its <see cref="BuildingDefinition"/>).</summary>
    public enum TreasuryRole : byte
    {
        /// <summary>Needed for survival or growth (housing, food, industry...). Never cut.</summary>
        Essential = 0,
        /// <summary>Yields more gold than it costs to maintain (commerce, gold mines).</summary>
        GoldSource = 1,
        /// <summary>Culture / entertainment: nice to have, first to go when money is short.</summary>
        Luxury = 2,
        /// <summary>Military: trimmed only in peacetime once basic defense exists.</summary>
        Military = 3,
        /// <summary>Upkeep-heavy civic/religious/education prestige: trimmed only in severe crises.</summary>
        Prestige = 4,
        /// <summary>Wonders (ordered by civ strategy; the treasury funds them but never votes on them).</summary>
        Wonder = 5
    }

    /// <summary>
    /// Per-database economic profile of every building type, computed once per (frozen) database:
    /// upkeep, gold mined at full staff, commerce points, gold inputs, gold/total cost and the treasury role.
    /// Allocation-free after construction.
    /// </summary>
    public sealed class TreasuryBuildingBook
    {
        /// <summary>Gold per Commerce point per tick (mirrors the economy's ServiceSystem rate; before Trade modifier).</summary>
        public const float GoldPerCommercePoint = 0.02f;
        /// <summary>Upkeep per tick at which civic/religious/education buildings count as prestige spending.</summary>
        public const float PrestigeUpkeep = 0.08f;

        private GameDatabase _db;

        public readonly BuildingDefinition[] Defs = new BuildingDefinition[Counts.Buildings];
        public readonly TreasuryRole[] Roles = new TreasuryRole[Counts.Buildings];
        public readonly float[] Upkeep = new float[Counts.Buildings];
        public readonly float[] MinedGold = new float[Counts.Buildings];
        public readonly float[] CommercePoints = new float[Counts.Buildings];
        public readonly float[] GoldInputs = new float[Counts.Buildings];
        public readonly float[] GoldCost = new float[Counts.Buildings];
        public readonly float[] TotalCost = new float[Counts.Buildings];
        public readonly float[] Entertainment = new float[Counts.Buildings];

        /// <summary>Binds to a database (recomputes when the database changes or is still mutable). False if db is null.</summary>
        public bool Bind(GameDatabase db)
        {
            if (db == null) return false;
            if (ReferenceEquals(db, _db) && db.IsFrozen) return true;
            _db = db;
            for (int t = 0; t < Counts.Buildings; t++)
            {
                db.TryGetBuilding((BuildingType)t, out var def);
                Defs[t] = def;
                if (def == null)
                {
                    Roles[t] = TreasuryRole.Essential;
                    Upkeep[t] = MinedGold[t] = CommercePoints[t] = GoldInputs[t] = GoldCost[t] = TotalCost[t] = Entertainment[t] = 0f;
                    continue;
                }
                int slots = def.HasJobs ? Math.Max(0, def.JobSlots) : 0;
                Upkeep[t] = NonNeg(def.UpkeepGold);
                MinedGold[t] = slots > 0 ? SumRes(def.OutputsPerWorker, ResourceType.Gold) * slots : 0f;
                GoldInputs[t] = slots > 0 ? SumRes(def.InputsPerWorker, ResourceType.Gold) * slots : 0f;
                CommercePoints[t] = SumSvc(def.Services, ServiceType.Commerce);
                Entertainment[t] = SumSvc(def.Services, ServiceType.Entertainment);
                GoldCost[t] = SumRes(def.Cost, ResourceType.Gold);
                TotalCost[t] = SumAll(def.Cost);
                Roles[t] = Classify(def, t);
            }
            return true;
        }

        /// <summary>Net gold per tick at full staffing (mined + commerce × rate × trade − gold inputs − upkeep).</summary>
        public float NetYield(int t, float trade)
        {
            if (t < 0 || t >= Counts.Buildings) return 0f;
            return MathUtil.Sanitize(MinedGold[t] + CommercePoints[t] * GoldPerCommercePoint * trade - GoldInputs[t] - Upkeep[t]);
        }

        /// <summary>True when the type is registered and treated as a wonder.</summary>
        public bool IsWonder(int t) => t >= 0 && t < Counts.Buildings && Roles[t] == TreasuryRole.Wonder && Defs[t] != null;

        private TreasuryRole Classify(BuildingDefinition def, int t)
        {
            if (def.IsWonder || def.Category == BuildingCategory.Wonder) return TreasuryRole.Wonder;
            if (def.IsDwelling || def.Type == BuildingType.TownCenter) return TreasuryRole.Essential;
            float gross = MinedGold[t] + CommercePoints[t] * GoldPerCommercePoint;
            if (gross > 0f && gross - GoldInputs[t] - Upkeep[t] > 0f) return TreasuryRole.GoldSource;
            if (def.Category == BuildingCategory.Military) return TreasuryRole.Military;
            if (def.Category == BuildingCategory.Culture) return TreasuryRole.Luxury;
            switch (def.Category)
            {
                case BuildingCategory.Housing: case BuildingCategory.Food: case BuildingCategory.Infrastructure:
                case BuildingCategory.Health: case BuildingCategory.Storage: case BuildingCategory.Resource:
                case BuildingCategory.Industry:
                    return TreasuryRole.Essential;
            }
            if (Entertainment[t] > 0f && Entertainment[t] >= MaxOtherService(def)) return TreasuryRole.Luxury;
            if (Upkeep[t] >= PrestigeUpkeep &&
                (def.Category == BuildingCategory.Religion || def.Category == BuildingCategory.Education || def.Category == BuildingCategory.Civic))
                return TreasuryRole.Prestige;
            return TreasuryRole.Essential;
        }

        private static float MaxOtherService(BuildingDefinition def)
        {
            float m = 0f;
            var a = def.Services;
            if (a == null) return 0f;
            for (int i = 0; i < a.Length; i++)
                if (a[i].Type != ServiceType.Entertainment && a[i].Amount > m) m = a[i].Amount;
            return m;
        }

        private static float SumRes(ResourceAmount[] a, ResourceType type)
        {
            if (a == null) return 0f;
            float s = 0f;
            for (int i = 0; i < a.Length; i++) if (a[i].Type == type) s += NonNeg(a[i].Amount);
            return s;
        }

        private static float SumAll(ResourceAmount[] a)
        {
            if (a == null) return 0f;
            float s = 0f;
            for (int i = 0; i < a.Length; i++) s += NonNeg(a[i].Amount);
            return s;
        }

        private static float SumSvc(ServiceAmount[] a, ServiceType type)
        {
            if (a == null) return 0f;
            float s = 0f;
            for (int i = 0; i < a.Length; i++) if (a[i].Type == type) s += NonNeg(a[i].Amount);
            return s;
        }

        internal static float NonNeg(float v) { v = MathUtil.Sanitize(v); return v > 0f ? v : 0f; }
    }
}
