using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Data-driven index of the building catalog for the <see cref="CapitalAdvisor"/>. Built once per database
    /// from <see cref="BuildingDefinition"/> fields only (services, outputs, storage, category, wonder flags), so new
    /// or rebalanced content is picked up without code changes. Every list is sorted best-first with a
    /// deterministic tie-break (later MinAge first, then lower type id).
    /// </summary>
    public sealed class CapitalCatalogIndex
    {
        /// <summary>The database this index was built from.</summary>
        public readonly GameDatabase Db;
        /// <summary>Non-wonder buildings providing Administration, most administration first (Forum, Castle, Arcology...).</summary>
        public readonly BuildingType[] Admin;
        /// <summary>Religion buildings providing Faith, grandest tier first (Cathedral, Temple, Shrine...).</summary>
        public readonly BuildingType[] Faith;
        /// <summary>Education buildings providing Research, grandest first (University, Library...).</summary>
        public readonly BuildingType[] Learning;
        /// <summary>Military seats: Defense + Administration (Castle).</summary>
        public readonly BuildingType[] Citadel;
        /// <summary>Culture buildings providing Entertainment, grandest first.</summary>
        public readonly BuildingType[] Culture;
        /// <summary>Storage buildings, largest capacity first.</summary>
        public readonly BuildingType[] Storage;
        /// <summary>All wonders (IsWonder or Wonder category).</summary>
        public readonly BuildingType[] Wonders;
        /// <summary>Per resource: staffed non-wonder producers, highest output per worker first.</summary>
        public readonly BuildingType[][] Producers;
        /// <summary>Per building type: nominal Administration points.</summary>
        public readonly float[] AdminValue;
        /// <summary>Per building type: true for wonders.</summary>
        public readonly bool[] IsWonderType;

        public CapitalCatalogIndex(GameDatabase db)
        {
            Db = db;
            int n = Counts.Buildings;
            AdminValue = new float[n];
            IsWonderType = new bool[n];
            var admin = new List<BuildingType>(); var adminKey = new float[n];
            var faith = new List<BuildingType>(); var faithKey = new float[n];
            var learn = new List<BuildingType>(); var learnKey = new float[n];
            var citadel = new List<BuildingType>(); var citadelKey = new float[n];
            var culture = new List<BuildingType>(); var cultureKey = new float[n];
            var storage = new List<BuildingType>(); var storageKey = new float[n];
            var wonders = new List<BuildingType>(); var wonderKey = new float[n];

            for (int i = 0; i < n; i++)
            {
                var t = (BuildingType)i;
                if (db == null || !db.TryGetBuilding(t, out var d)) continue;
                bool wonder = d.IsWonder || d.Category == BuildingCategory.Wonder;
                float adm = ServiceOf(d, ServiceType.Administration);
                IsWonderType[i] = wonder;
                AdminValue[i] = adm;
                if (wonder) { wonders.Add(t); wonderKey[i] = d.BuildWork; continue; }
                if (d.IsDwelling || t == BuildingType.TownCenter) continue;

                if (adm > 0f) { admin.Add(t); adminKey[i] = adm; }
                float fth = ServiceOf(d, ServiceType.Faith);
                if (d.Category == BuildingCategory.Religion && fth > 0f) { faith.Add(t); faithKey[i] = fth; }
                float res = ServiceOf(d, ServiceType.Research);
                if (d.Category == BuildingCategory.Education && res > 0f) { learn.Add(t); learnKey[i] = res; }
                float def = ServiceOf(d, ServiceType.Defense);
                if (d.Category == BuildingCategory.Military && def > 0f && adm > 0f) { citadel.Add(t); citadelKey[i] = def + adm; }
                float ent = ServiceOf(d, ServiceType.Entertainment);
                if (d.Category == BuildingCategory.Culture && ent > 0f) { culture.Add(t); cultureKey[i] = ent; }
                if (d.Category == BuildingCategory.Storage && d.StorageCapacity > 0f) { storage.Add(t); storageKey[i] = d.StorageCapacity; }
            }

            Admin = Sorted(admin, adminKey, db);
            Faith = Sorted(faith, faithKey, db);
            Learning = Sorted(learn, learnKey, db);
            Citadel = Sorted(citadel, citadelKey, db);
            Culture = Sorted(culture, cultureKey, db);
            Storage = Sorted(storage, storageKey, db);
            Wonders = Sorted(wonders, wonderKey, db);

            Producers = new BuildingType[Counts.Resources][];
            var prod = new List<BuildingType>();
            var prodKey = new float[n];
            for (int r = 0; r < Counts.Resources; r++)
            {
                prod.Clear();
                for (int i = 0; i < n; i++)
                {
                    prodKey[i] = 0f;
                    if (IsWonderType[i] || db == null || !db.TryGetBuilding((BuildingType)i, out var d)) continue;
                    if (!d.HasJobs || d.IsDwelling) continue;
                    float o = OutputOf(d, (ResourceType)r);
                    if (o > 0f) { prod.Add((BuildingType)i); prodKey[i] = o; }
                }
                Producers[r] = Sorted(prod, prodKey, db);
            }
        }

        /// <summary>Total service points of a type a definition provides (sanitized, never negative).</summary>
        public static float ServiceOf(BuildingDefinition d, ServiceType type)
        {
            if (d == null || d.Services == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < d.Services.Length; i++)
                if (d.Services[i].Type == type) sum += MathUtil.Sanitize(d.Services[i].Amount);
            return sum > 0f ? sum : 0f;
        }

        /// <summary>Output per worker of a resource a definition produces (sanitized, never negative).</summary>
        public static float OutputOf(BuildingDefinition d, ResourceType type)
        {
            if (d == null || d.OutputsPerWorker == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < d.OutputsPerWorker.Length; i++)
                if (d.OutputsPerWorker[i].Type == type) sum += MathUtil.Sanitize(d.OutputsPerWorker[i].Amount);
            return sum > 0f ? sum : 0f;
        }

        private static BuildingType[] Sorted(List<BuildingType> list, float[] key, GameDatabase db)
        {
            var a = list.ToArray();
            for (int i = 1; i < a.Length; i++)
            {
                var cur = a[i];
                int j = i - 1;
                while (j >= 0 && Better(cur, a[j], key, db)) { a[j + 1] = a[j]; j--; }
                a[j + 1] = cur;
            }
            return a;
        }

        private static bool Better(BuildingType x, BuildingType y, float[] key, GameDatabase db)
        {
            float kx = key[(int)x], ky = key[(int)y];
            if (kx > ky + 1e-6f) return true;
            if (kx < ky - 1e-6f) return false;
            AgeId ax = db.Building(x).MinAge, ay = db.Building(y).MinAge;
            if (ax != ay) return ax > ay;
            return (int)x < (int)y;
        }
    }
}
