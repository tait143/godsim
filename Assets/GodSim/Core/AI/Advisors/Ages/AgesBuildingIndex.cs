using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Data-driven, precomputed view of the building catalog for the age advisor. Built once per
    /// <see cref="GameDatabase"/> from <see cref="BuildingDefinition"/> fields only (MinAge/MaxAge, Category,
    /// outputs, services, storage, cost): no building type is hard-coded. Everything is ordered deterministically
    /// (score, then enum index) and reason strings are cached so the advisor does not allocate while advising.
    /// </summary>
    public sealed class AgesBuildingIndex
    {
        /// <summary>Hard placement requirements (soft preferences such as Central/Outskirts/Isolated are excluded).</summary>
        public const PlacementRule HardPlacement =
            PlacementRule.NearWater | PlacementRule.Coastal | PlacementRule.OnHills | PlacementRule.NearMountain |
            PlacementRule.NearForest | PlacementRule.NearOre | PlacementRule.NearStone | PlacementRule.NearGold |
            PlacementRule.NearCoal | PlacementRule.NearOil | PlacementRule.Fertile | PlacementRule.NearRiver;

        private readonly GameDatabase _db;
        private readonly List<BuildingType>[] _signatures = new List<BuildingType>[Counts.Ages];
        private readonly List<BuildingType>[] _wonders = new List<BuildingType>[Counts.Ages];
        private readonly List<BuildingType>[] _sunset = new List<BuildingType>[Counts.Ages];
        private readonly List<BuildingType>[] _equivalents = new List<BuildingType>[Counts.Buildings];
        private readonly List<BuildingType>[] _producers = new List<BuildingType>[Counts.Resources];
        private readonly List<BuildingType> _storage = new List<BuildingType>();
        private readonly string[] _ageNames = new string[Counts.Ages];
        private readonly string[] _dawnReason = new string[Counts.Buildings];
        private readonly string[] _localReason = new string[Counts.Buildings];
        private readonly string[] _spreadReason = new string[Counts.Buildings];
        private readonly string[] _obsoleteReason = new string[Counts.Buildings];
        private readonly string[] _sunsetReason = new string[Counts.Buildings];
        private readonly string[][] _stockReason = new string[Counts.Ages][];
        private readonly string[] _storageReason = new string[Counts.Ages];
        private readonly Dictionary<int, string> _replaceReasons = new Dictionary<int, string>();

        private AgesBuildingIndex(GameDatabase db) { _db = db; }

        /// <summary>The database this index was built from.</summary>
        public GameDatabase Db => _db;

        /// <summary>Non-wonder, non-dwelling buildings whose MinAge is exactly <paramref name="age"/>, most significant first.</summary>
        public IReadOnlyList<BuildingType> Signatures(AgeId age) => _signatures[(int)age];
        /// <summary>Wonders that first become available in <paramref name="age"/>.</summary>
        public IReadOnlyList<BuildingType> Wonders(AgeId age) => _wonders[(int)age];
        /// <summary>Buildings whose MaxAge is exactly <paramref name="age"/> (obsolete once the civ leaves it).</summary>
        public IReadOnlyList<BuildingType> Sunset(AgeId age) => _sunset[(int)age];
        /// <summary>Functional equivalents of a type (same outputs/services/category, longer-lived), best first.</summary>
        public IReadOnlyList<BuildingType> Equivalents(BuildingType t) => _equivalents[(int)t];
        /// <summary>Producers of a resource (per-worker output x slots), best first.</summary>
        public IReadOnlyList<BuildingType> Producers(ResourceType r) => _producers[(int)r];
        /// <summary>Storage buildings, best first.</summary>
        public IReadOnlyList<BuildingType> Storage => _storage;

        public string AgeName(AgeId a) => _ageNames[(int)a];
        public string DawnReason(BuildingType t) => _dawnReason[(int)t];
        public string LocalReason(BuildingType t) => _localReason[(int)t];
        public string SpreadReason(BuildingType t) => _spreadReason[(int)t];
        public string ObsoleteReason(BuildingType t) => _obsoleteReason[(int)t];
        public string SunsetReason(BuildingType t) => _sunsetReason[(int)t];
        public string StockReason(AgeId next, ResourceType r) => _stockReason[(int)next][(int)r];
        public string StorageReason(AgeId next) => _storageReason[(int)next];

        /// <summary>"X is obsolete: replacing it with Y" (cached per pair, bounded).</summary>
        public string ReplaceReason(BuildingType obsolete, BuildingType equivalent, bool sunset)
        {
            int key = ((int)obsolete << 9) | ((int)equivalent << 1) | (sunset ? 1 : 0);
            if (_replaceReasons.TryGetValue(key, out var r)) return r;
            string o = NameOf(obsolete), e = NameOf(equivalent);
            r = sunset ? ("Retiring " + o + " soon: " + e + " carries its work into the next age")
                       : (o + " is obsolete: " + e + " takes over its work");
            if (_replaceReasons.Count < 4096) _replaceReasons[key] = r;
            return r;
        }

        // ================================================================== build
        /// <summary>Builds the index from a (normally frozen) database. Never throws on missing slots.</summary>
        public static AgesBuildingIndex Build(GameDatabase db)
        {
            var ix = new AgesBuildingIndex(db);
            for (int a = 0; a < Counts.Ages; a++)
            {
                ix._signatures[a] = new List<BuildingType>();
                ix._wonders[a] = new List<BuildingType>();
                ix._sunset[a] = new List<BuildingType>();
                ix._ageNames[a] = AgeLabel(db, (AgeId)a);
            }
            for (int i = 0; i < Counts.Buildings; i++) ix._equivalents[i] = new List<BuildingType>();
            for (int r = 0; r < Counts.Resources; r++) ix._producers[r] = new List<BuildingType>();
            if (db == null) return ix;

            var defs = db.Buildings;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null) continue;
                var t = (BuildingType)i;
                if (d.MaxAge < AgeId.Ascension) ix._sunset[(int)d.MaxAge].Add(t);
                if (IsCore(d)) continue;
                if (d.IsWonder || d.UniquePerCivilization || d.Category == BuildingCategory.Wonder) ix._wonders[(int)d.MinAge].Add(t);
                else if (!d.IsDwelling && d.MinAge > AgeId.Dawn) ix._signatures[(int)d.MinAge].Add(t);
            }
            for (int a = 0; a < Counts.Ages; a++)
            {
                SortBy(ix._signatures[a], db, Significance);
                SortBy(ix._wonders[a], db, Significance);
            }

            // Functional equivalents (for every type that can go obsolete).
            var scores = new List<KeyValuePair<float, int>>();
            for (int o = 0; o < defs.Count; o++)
            {
                var od = defs[o];
                if (od == null || od.MaxAge >= AgeId.Ascension) continue;
                scores.Clear();
                for (int c = 0; c < defs.Count; c++)
                {
                    if (c == o) continue;
                    float sim = EquivalenceScore(od, defs[c]);
                    if (sim > 0f) scores.Add(new KeyValuePair<float, int>(sim, c));
                }
                scores.Sort(CompareScoreDesc);
                for (int k = 0; k < scores.Count; k++) ix._equivalents[o].Add((BuildingType)scores[k].Value);
            }

            // Producers per resource.
            for (int r = 0; r < Counts.Resources; r++)
            {
                scores.Clear();
                for (int c = 0; c < defs.Count; c++)
                {
                    var d = defs[c];
                    if (d == null || IsCore(d) || d.IsWonder || d.UniquePerCivilization) continue;
                    float rate = OutputRate(d, (ResourceType)r);
                    if (rate <= 0f) continue;
                    int inputs = d.InputsPerWorker == null ? 0 : d.InputsPerWorker.Length;
                    float score = rate * Math.Max(1, d.JobSlots) / (1f + 0.5f * inputs) + 0.002f * (int)d.MinAge;
                    scores.Add(new KeyValuePair<float, int>(score, c));
                }
                scores.Sort(CompareScoreDesc);
                for (int k = 0; k < scores.Count; k++) ix._producers[r].Add((BuildingType)scores[k].Value);
            }

            // Storage buildings: dedicated storage first, then anything with capacity.
            scores.Clear();
            for (int c = 0; c < defs.Count; c++)
            {
                var d = defs[c];
                if (d == null || IsCore(d) || d.IsWonder || d.UniquePerCivilization || d.IsDwelling) continue;
                bool dedicated = d.Category == BuildingCategory.Storage;
                if (!dedicated && d.StorageCapacity <= 0f) continue;
                if (!dedicated && d.StorageCapacity < 60f) continue; // incidental storage is not a storage plan
                scores.Add(new KeyValuePair<float, int>(MathUtil.Sanitize(d.StorageCapacity) + (dedicated ? 1000f : 0f), c));
            }
            scores.Sort(CompareScoreDesc);
            for (int k = 0; k < scores.Count; k++) ix._storage.Add((BuildingType)scores[k].Value);

            // Cached explanation strings.
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                if (d == null) continue;
                string n = NameOf(db, (BuildingType)i);
                string age = ix._ageNames[(int)d.MinAge];
                ix._dawnReason[i] = "The " + age + " dawns: raising the first " + n;
                ix._localReason[i] = "The " + age + " reaches us: our first " + n;
                ix._spreadReason[i] = age + " ideas spread: a " + n + " like the capital's";
                int nextAfterMax = Math.Min((int)d.MaxAge + 1, Counts.Ages - 1);
                ix._obsoleteReason[i] = n + " is obsolete since the " + ix._ageNames[nextAfterMax];
                ix._sunsetReason[i] = n + " will be obsolete in the " + ix._ageNames[nextAfterMax];
            }
            for (int a = 0; a < Counts.Ages; a++)
            {
                ix._stockReason[a] = new string[Counts.Resources];
                for (int r = 0; r < Counts.Resources; r++)
                    ix._stockReason[a][r] = "The " + ix._ageNames[a] + " nears: stockpiling " + (ResourceType)r + " for its new buildings";
                ix._storageReason[a] = "The " + ix._ageNames[a] + " nears: storage for the new era's materials";
            }
            return ix;
        }

        // ================================================================== data helpers (pure)
        /// <summary>Town centers and dwellings are managed by other systems; never treated as signatures/equivalents.</summary>
        public static bool IsCore(BuildingDefinition d) => d == null || d.Type == BuildingType.TownCenter || d.IsDwelling;

        /// <summary>Per-worker output of a resource (0 if the building does not produce it).</summary>
        public static float OutputRate(BuildingDefinition d, ResourceType r)
        {
            if (d == null || d.OutputsPerWorker == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < d.OutputsPerWorker.Length; i++)
                if (d.OutputsPerWorker[i].Type == r) sum += Math.Max(0f, MathUtil.Sanitize(d.OutputsPerWorker[i].Amount));
            return sum;
        }

        /// <summary>Service points of a service type (0 if not provided).</summary>
        public static float ServiceRate(BuildingDefinition d, ServiceType s)
        {
            if (d == null || d.Services == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < d.Services.Length; i++)
                if (d.Services[i].Type == s) sum += Math.Max(0f, MathUtil.Sanitize(d.Services[i].Amount));
            return sum;
        }

        /// <summary>
        /// How much of <paramref name="reference"/>'s function one <paramref name="d"/> provides: overlapping outputs
        /// (rate x slots), overlapping services and storage. Falls back to 1 for a same-category building when the
        /// reference has no measurable function. Always finite and &gt;= 0.
        /// </summary>
        public static float FunctionalValue(BuildingDefinition d, BuildingDefinition reference)
        {
            if (d == null || reference == null) return 0f;
            float v = 0f;
            bool measurable = false;
            int slots = Math.Max(1, d.JobSlots);
            if (reference.OutputsPerWorker != null)
                for (int i = 0; i < reference.OutputsPerWorker.Length; i++)
                {
                    measurable = true;
                    v += OutputRate(d, reference.OutputsPerWorker[i].Type) * slots * 10f;
                }
            if (reference.Services != null)
                for (int i = 0; i < reference.Services.Length; i++)
                {
                    measurable = true;
                    v += ServiceRate(d, reference.Services[i].Type);
                }
            if (reference.StorageCapacity > 0f && reference.Category == BuildingCategory.Storage)
            {
                measurable = true;
                v += Math.Max(0f, MathUtil.Sanitize(d.StorageCapacity)) / 100f;
            }
            if (!measurable) v = d.Category == reference.Category ? 1f : 0f;
            return MathUtil.Sanitize(v);
        }

        /// <summary>
        /// Similarity of candidate <paramref name="c"/> as a modern replacement for <paramref name="o"/>; 0 = not an
        /// equivalent. The candidate must outlive the old building and share its outputs, services or (for buildings
        /// without a measurable function) its category.
        /// </summary>
        public static float EquivalenceScore(BuildingDefinition o, BuildingDefinition c)
        {
            if (o == null || c == null || c.Type == o.Type || IsCore(c)) return 0f;
            if (c.IsWonder || c.UniquePerCivilization || c.Category == BuildingCategory.Wonder) return 0f;
            if (c.MaxAge <= o.MaxAge) return 0f; // must outlive the building it replaces
            if (o.IsDwelling != c.IsDwelling) return 0f;

            float outShare = 0f, outTotal = 0f;
            if (o.OutputsPerWorker != null)
                for (int i = 0; i < o.OutputsPerWorker.Length; i++)
                {
                    float a = Math.Max(0f, MathUtil.Sanitize(o.OutputsPerWorker[i].Amount));
                    outTotal += a;
                    if (OutputRate(c, o.OutputsPerWorker[i].Type) > 0f) outShare += a;
                }
            float svcShare = 0f, svcTotal = 0f;
            if (o.Services != null)
                for (int i = 0; i < o.Services.Length; i++)
                {
                    float a = Math.Max(0f, MathUtil.Sanitize(o.Services[i].Amount));
                    svcTotal += a;
                    if (ServiceRate(c, o.Services[i].Type) > 0f) svcShare += a;
                }
            float outOverlap = outTotal > 0f ? outShare / outTotal : 0f;
            float svcOverlap = svcTotal > 0f ? svcShare / svcTotal : 0f;
            bool hasFunction = outTotal > 0f || svcTotal > 0f;
            bool sameCategory = c.Category == o.Category;
            bool storageBoth = o.Category == BuildingCategory.Storage && (c.Category == BuildingCategory.Storage || c.StorageCapacity >= 60f);

            if (hasFunction && outOverlap <= 0f && svcOverlap <= 0f) return 0f;
            if (!hasFunction && !sameCategory && !storageBoth) return 0f;

            float sim = 2.5f * outOverlap + 2f * svcOverlap + (sameCategory ? 1f : 0f) + (storageBoth ? 1.5f : 0f);
            if (c.Job != JobType.None && c.Job == o.Job) sim += 0.3f;
            // Extra input chains make a replacement less of a drop-in.
            int extraInputs = 0;
            if (c.InputsPerWorker != null)
                for (int i = 0; i < c.InputsPerWorker.Length; i++)
                    if (!HasInput(o, c.InputsPerWorker[i].Type)) extraInputs++;
            sim -= 0.4f * extraInputs;
            // Capacity relative to the old building and modernity break ties.
            float ov = FunctionalValue(o, o);
            if (ov > 0f) sim += 0.25f * MathUtil.Clamp(FunctionalValue(c, o) / ov, 0f, 2f);
            sim += 0.06f * (int)c.MinAge;
            return sim >= 0.9f ? MathUtil.Sanitize(sim) : 0f;
        }

        private static bool HasInput(BuildingDefinition d, ResourceType r)
        {
            if (d.InputsPerWorker == null) return false;
            for (int i = 0; i < d.InputsPerWorker.Length; i++) if (d.InputsPerWorker[i].Type == r) return true;
            return false;
        }

        /// <summary>How much a building defines its age (economy and services first; visual landmarks a little).</summary>
        public static float Significance(BuildingDefinition d)
        {
            if (d == null) return 0f;
            float s = 1f;
            if (d.HasJobs && d.OutputsPerWorker != null && d.OutputsPerWorker.Length > 0) s += 0.6f;
            if (d.Services != null && d.Services.Length > 0) s += 0.5f;
            switch (d.Category)
            {
                case BuildingCategory.Industry: case BuildingCategory.Resource: case BuildingCategory.Food: s += 0.5f; break;
                case BuildingCategory.Education: case BuildingCategory.Infrastructure: s += 0.4f; break;
                case BuildingCategory.Religion: case BuildingCategory.Health: case BuildingCategory.Civic: s += 0.3f; break;
                case BuildingCategory.Commerce: case BuildingCategory.Military: s += 0.25f; break;
                default: s += 0.15f; break;
            }
            s += 0.1f * MathUtil.Clamp(MathUtil.Sanitize(d.VisualHeight), 0f, 3f);
            if (d.MaxPerSettlement == 1) s += 0.1f;
            return MathUtil.Sanitize(s);
        }

        private static void SortBy(List<BuildingType> list, GameDatabase db, Func<BuildingDefinition, float> score)
        {
            var tmp = new List<KeyValuePair<float, int>>(list.Count);
            for (int i = 0; i < list.Count; i++)
            {
                db.TryGetBuilding(list[i], out var d);
                tmp.Add(new KeyValuePair<float, int>(score(d), (int)list[i]));
            }
            tmp.Sort(CompareScoreDesc);
            list.Clear();
            for (int i = 0; i < tmp.Count; i++) list.Add((BuildingType)tmp[i].Value);
        }

        /// <summary>Total order: score descending, then enum index ascending (deterministic with an unstable sort).</summary>
        private static int CompareScoreDesc(KeyValuePair<float, int> a, KeyValuePair<float, int> b)
        {
            int c = b.Key.CompareTo(a.Key);
            return c != 0 ? c : a.Value.CompareTo(b.Value);
        }

        private string NameOf(BuildingType t) => NameOf(_db, t);

        private static string NameOf(GameDatabase db, BuildingType t)
        {
            if (db != null && db.TryGetBuilding(t, out var d) && !string.IsNullOrEmpty(d.Name)) return d.Name;
            return t.ToString();
        }

        private static string AgeLabel(GameDatabase db, AgeId a)
        {
            string n = null;
            if (db != null && (int)a < db.Ages.Count && db.Ages[(int)a] != null) n = db.Ages[(int)a].Name;
            if (string.IsNullOrEmpty(n)) n = a.ToString();
            if (n.StartsWith("The ", StringComparison.Ordinal)) n = n.Substring(4);
            if (!n.EndsWith("Age", StringComparison.Ordinal) && !n.EndsWith("Era", StringComparison.Ordinal)) n += " Age";
            return n;
        }
    }
}
