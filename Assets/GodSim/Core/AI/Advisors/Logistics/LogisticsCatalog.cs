using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Data-driven index of the buildings the logistics advisor reasons about, derived only from
    /// <see cref="BuildingDefinition"/> fields: which staffed buildings net-produce each resource, which buildings add
    /// storage, and each building's total/max construction cost. Rebuilt whenever the database instance changes
    /// (the runtime database is frozen, so this happens once).
    /// </summary>
    public sealed class LogisticsCatalog
    {
        private GameDatabase _db;
        private readonly List<BuildingType>[] _producers = new List<BuildingType>[Counts.Resources];
        private readonly List<BuildingType> _storage = new List<BuildingType>();
        private readonly List<BuildingType> _storageFallback = new List<BuildingType>();
        private readonly float[] _costTotal = new float[Counts.Buildings];
        private readonly float[] _costMax = new float[Counts.Buildings];

        public LogisticsCatalog()
        {
            for (int i = 0; i < _producers.Length; i++) _producers[i] = new List<BuildingType>();
        }

        /// <summary>Staffed, non-wonder buildings whose per-worker output of the resource exceeds their input of it.</summary>
        public IReadOnlyList<BuildingType> ProducersOf(ResourceType r) => _producers[(int)r];
        /// <summary>Storage-category buildings with <see cref="BuildingDefinition.StorageCapacity"/> &gt; 0.</summary>
        public IReadOnlyList<BuildingType> Storage => _storage;
        /// <summary>Other non-wonder, non-dwelling buildings that add storage (used when no storage building is buildable).</summary>
        public IReadOnlyList<BuildingType> StorageFallback => _storageFallback;
        public float CostTotal(int type) => type >= 0 && type < _costTotal.Length ? _costTotal[type] : 0f;
        public float CostMax(int type) => type >= 0 && type < _costMax.Length ? _costMax[type] : 0f;

        /// <summary>Indexes the database (no-op when already indexed). Returns false for a null database.</summary>
        public bool Ensure(GameDatabase db)
        {
            if (db == null) return false;
            if (ReferenceEquals(db, _db)) return true;
            _db = db;
            for (int i = 0; i < _producers.Length; i++) _producers[i].Clear();
            _storage.Clear();
            _storageFallback.Clear();
            for (int t = 0; t < Counts.Buildings; t++)
            {
                _costTotal[t] = 0f;
                _costMax[t] = 0f;
                if (!db.TryGetBuilding((BuildingType)t, out var def)) continue;
                var cost = def.Cost;
                if (cost != null)
                    for (int c = 0; c < cost.Length; c++)
                    {
                        float a = Amount(cost[c]);
                        _costTotal[t] += a;
                        if (a > _costMax[t]) _costMax[t] = a;
                    }
                if (def.IsWonder || def.UniquePerCivilization) continue;
                if (def.HasJobs)
                    for (int r = 0; r < Counts.Resources; r++)
                        if (OutputOf(def, (ResourceType)r) - InputOf(def, (ResourceType)r) > 0f) _producers[r].Add((BuildingType)t);
                if (def.StorageCapacity > 0f && !def.IsDwelling)
                {
                    if (def.Category == BuildingCategory.Storage) _storage.Add((BuildingType)t);
                    else _storageFallback.Add((BuildingType)t);
                }
            }
            return true;
        }

        /// <summary>True for projects big enough to compete for scarce materials: wonders, total cost &gt;= bigCost, or one cost entry &gt;= half the storage capacity.</summary>
        public bool IsBigProject(BuildingDefinition def, float bigCost, float capacity)
        {
            if (def == null || def.IsDwelling) return false;
            int t = (int)def.Type;
            return def.IsWonder || CostTotal(t) >= bigCost || CostMax(t) >= capacity * 0.5f;
        }

        public static float OutputOf(BuildingDefinition def, ResourceType r) => Sum(def?.OutputsPerWorker, r);
        public static float InputOf(BuildingDefinition def, ResourceType r) => Sum(def?.InputsPerWorker, r);
        public static float CostOf(BuildingDefinition def, ResourceType r) => Sum(def?.Cost, r);

        /// <summary>Sanitized, non-negative amount of a resource entry.</summary>
        public static float Amount(ResourceAmount a)
        {
            float v = MathUtil.Sanitize(a.Amount);
            return v > 0f ? v : 0f;
        }

        private static float Sum(ResourceAmount[] list, ResourceType r)
        {
            if (list == null) return 0f;
            float s = 0f;
            for (int i = 0; i < list.Length; i++) if (list[i].Type == r) s += Amount(list[i]);
            return s;
        }
    }
}
