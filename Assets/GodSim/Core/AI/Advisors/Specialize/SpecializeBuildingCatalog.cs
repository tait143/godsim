using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Data-driven classification of every <see cref="BuildingDefinition"/> into the specialization role it serves,
    /// derived only from definition fields (outputs per worker x job slots, services, category). Built once per
    /// database; lookups are allocation-free.
    /// </summary>
    public sealed class SpecializeBuildingCatalog
    {
        /// <summary>Placement flags that are hard requirements (the planner rejects sites failing any of them).</summary>
        public const PlacementRule HardPlacement = PlacementRule.NearWater | PlacementRule.Coastal | PlacementRule.OnHills |
            PlacementRule.NearMountain | PlacementRule.NearForest | PlacementRule.NearOre | PlacementRule.NearStone |
            PlacementRule.NearGold | PlacementRule.NearCoal | PlacementRule.NearOil | PlacementRule.Fertile | PlacementRule.NearRiver;

        /// <summary>Role each building type serves (None = not a specialty producer).</summary>
        public readonly SpecializeRole[] RoleOf = new SpecializeRole[Counts.Buildings];
        /// <summary>Role output of one fully staffed building (resource units or service points per tick).</summary>
        public readonly float[] Weight = new float[Counts.Buildings];
        /// <summary>Buildings providing Administration (civic seat of government; capital lean).</summary>
        public readonly bool[] IsCivic = new bool[Counts.Buildings];

        private readonly int[][] _byRole = new int[SpecializeRoles.Count][];
        private readonly int[] _civic;
        private int _civicCount;
        private readonly float[] _scratch = new float[SpecializeRoles.Count];

        /// <summary>The database this catalog was built from (null until <see cref="Build"/>).</summary>
        public GameDatabase Source { get; private set; }

        public SpecializeBuildingCatalog()
        {
            for (int r = 0; r < _byRole.Length; r++) _byRole[r] = new int[0];
            _civic = new int[Counts.Buildings];
        }

        /// <summary>Number of producer types of a role (sorted by output weight, best first).</summary>
        public int CountFor(SpecializeRole role) => _byRole[(int)role].Length;

        /// <summary>The i-th best producer type of a role.</summary>
        public BuildingType At(SpecializeRole role, int i) => (BuildingType)_byRole[(int)role][i];

        public int CivicCount => _civicCount;
        public BuildingType CivicAt(int i) => (BuildingType)_civic[i];

        /// <summary>(Re)classifies every registered building. Cheap; call when the database changes.</summary>
        public void Build(GameDatabase db)
        {
            Source = db;
            int n = Counts.Buildings;
            var counts = new int[SpecializeRoles.Count];
            _civicCount = 0;
            for (int t = 0; t < n; t++)
            {
                RoleOf[t] = SpecializeRole.None;
                Weight[t] = 0f;
                IsCivic[t] = false;
                if (db == null || !db.TryGetBuilding((BuildingType)t, out var def) || def == null) continue;
                Classify(def, out var role, out float w, out bool civic);
                RoleOf[t] = role;
                Weight[t] = w;
                IsCivic[t] = civic;
                if (role != SpecializeRole.None) counts[(int)role]++;
                if (civic) _civic[_civicCount++] = t;
            }
            for (int r = 0; r < SpecializeRoles.Count; r++)
            {
                var list = new int[counts[r]];
                int k = 0;
                for (int t = 0; t < n; t++)
                    if ((int)RoleOf[t] == r && r != 0) list[k++] = t;
                // insertion sort: weight desc, then type asc (deterministic)
                for (int i = 1; i < list.Length; i++)
                {
                    int v = list[i]; int j = i - 1;
                    while (j >= 0 && (Weight[list[j]] < Weight[v] || (Weight[list[j]] == Weight[v] && list[j] > v))) { list[j + 1] = list[j]; j--; }
                    list[j + 1] = v;
                }
                _byRole[r] = list;
            }
        }

        private void Classify(BuildingDefinition def, out SpecializeRole role, out float weight, out bool civic)
        {
            role = SpecializeRole.None; weight = 0f; civic = false;
            if (def.Type == BuildingType.TownCenter || def.IsWonder || def.UniquePerCivilization || def.IsDwelling) return;
            var cat = def.Category;
            if (cat == BuildingCategory.Housing || cat == BuildingCategory.Wonder || cat == BuildingCategory.Military) return;

            float slots = def.HasJobs ? Math.Max(1, def.JobSlots) : 1f;
            float commerce = 0f, faith = 0f, research = 0f, admin = 0f;
            var svc = def.Services ?? ServiceAmount.None;
            for (int i = 0; i < svc.Length; i++)
            {
                float a = Math.Max(0f, MathUtil.Sanitize(svc[i].Amount));
                switch (svc[i].Type)
                {
                    case ServiceType.Commerce: commerce += a; break;
                    case ServiceType.Faith: faith += a; break;
                    case ServiceType.Research: research += a; break;
                    case ServiceType.Administration: admin += a; break;
                }
            }
            civic = admin > 0f && cat != BuildingCategory.Storage;

            var outs = def.OutputsPerWorker ?? ResourceAmount.None;
            if (cat == BuildingCategory.Commerce || commerce > 0f)
            {
                float gold = 0f;
                for (int i = 0; i < outs.Length; i++) if (outs[i].Type == ResourceType.Gold) gold += Math.Max(0f, MathUtil.Sanitize(outs[i].Amount)) * slots;
                role = SpecializeRole.MarketTown;
                weight = Math.Max(0.5f, commerce + gold);
                return;
            }
            if (cat == BuildingCategory.Storage) return;

            for (int r = 0; r < _scratch.Length; r++) _scratch[r] = 0f;
            for (int i = 0; i < outs.Length; i++)
            {
                var rr = SpecializeRoles.RoleForResource(outs[i].Type);
                if (rr == SpecializeRole.None) continue;
                _scratch[(int)rr] += Math.Max(0f, MathUtil.Sanitize(outs[i].Amount)) * slots;
            }
            int best = 0; float bestW = 0f;
            for (int r = 1; r < _scratch.Length; r++) if (_scratch[r] > bestW) { bestW = _scratch[r]; best = r; }
            if (best != 0) { role = (SpecializeRole)best; weight = bestW; return; }

            if (faith > 0f || research > 0f)
            {
                if (research > faith) { role = SpecializeRole.ScholarCity; weight = research; }
                else { role = SpecializeRole.TempleCity; weight = faith; }
                return;
            }
            if (cat == BuildingCategory.Religion) { role = SpecializeRole.TempleCity; weight = 0.5f; }
            else if (cat == BuildingCategory.Education) { role = SpecializeRole.ScholarCity; weight = 0.5f; }
        }

        /// <summary>True if every hard placement flag of the definition is satisfiable given the flags a site survey found.</summary>
        public static bool PlacementOk(BuildingDefinition def, PlacementRule satisfiable)
        {
            if (def == null) return false;
            var hard = def.Placement & HardPlacement;
            return (hard & ~satisfiable) == 0;
        }
    }
}
