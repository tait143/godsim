using System;
using System.Collections.Generic;

namespace GodSim.Core.History
{
    /// <summary>
    /// Pure, read-only query helpers over WorldState/GameDatabase for UIs and tools. Nothing here mutates state or
    /// keeps static mutable buffers; the hot helpers fill caller-provided arrays/lists so they allocate nothing.
    /// Results are deterministic (entity stores are iterated by index; ties break on ascending id).
    /// </summary>
    public static class WorldStatistics
    {
        /// <summary>Number of <see cref="BuildingCategory"/> values (size for category arrays).</summary>
        public static readonly int CategoryCount = Enum.GetValues(typeof(BuildingCategory)).Length;

        // ================================================================== population
        /// <summary>Living people per race into <paramref name="result"/> (indexed by RaceId). Returns the total.</summary>
        public static int PopulationByRace(WorldState state, int[] result)
        {
            Guard.NotNull(result, nameof(result));
            Array.Clear(result, 0, result.Length);
            if (state == null) return 0;
            int total = 0;
            var people = state.People.Items;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (!p.IsAlive) continue;
                total++;
                int r = (int)p.Race;
                if (r >= 0 && r < result.Length) result[r]++;
            }
            return total;
        }

        /// <summary>Allocating convenience overload (array length = Counts.Races).</summary>
        public static int[] PopulationByRace(WorldState state)
        {
            var r = new int[Counts.Races];
            PopulationByRace(state, r);
            return r;
        }

        /// <summary>
        /// Residents of living settlements grouped by the current age of the owning civilization, into
        /// <paramref name="result"/> (indexed by AgeId). Returns the total counted.
        /// </summary>
        public static int PopulationByAge(WorldState state, int[] result)
        {
            Guard.NotNull(result, nameof(result));
            Array.Clear(result, 0, result.Length);
            if (state == null) return 0;
            int total = 0;
            var settlements = state.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (!s.IsAlive) continue;
                var civ = state.Civilizations.Get(s.CivilizationId);
                if (civ == null) continue;
                int a = (int)civ.Age;
                int n = s.Population;
                total += n;
                if (a >= 0 && a < result.Length) result[a] += n;
            }
            return total;
        }

        /// <summary>Allocating convenience overload (array length = Counts.Ages).</summary>
        public static int[] PopulationByAge(WorldState state)
        {
            var r = new int[Counts.Ages];
            PopulationByAge(state, r);
            return r;
        }

        // ================================================================== buildings & housing
        /// <summary>
        /// Standing buildings of a settlement per <see cref="BuildingCategory"/> into <paramref name="result"/>
        /// (length &gt;= <see cref="CategoryCount"/>). Unfinished buildings count only if <paramref name="includeUnfinished"/>.
        /// Returns the total counted.
        /// </summary>
        public static int BuildingsByCategory(WorldState state, GameDatabase db, Settlement settlement, int[] result, bool includeUnfinished = false)
        {
            Guard.NotNull(result, nameof(result));
            Array.Clear(result, 0, result.Length);
            return AccumulateCategories(state, db, settlement, result, includeUnfinished);
        }

        /// <summary>Same as the settlement overload, summed over every living settlement of a civilization.</summary>
        public static int BuildingsByCategory(WorldState state, GameDatabase db, Civilization civ, int[] result, bool includeUnfinished = false)
        {
            Guard.NotNull(result, nameof(result));
            Array.Clear(result, 0, result.Length);
            if (state == null || civ == null) return 0;
            int total = 0;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s != null && s.IsAlive) total += AccumulateCategories(state, db, s, result, includeUnfinished);
            }
            return total;
        }

        private static int AccumulateCategories(WorldState state, GameDatabase db, Settlement settlement, int[] result, bool includeUnfinished)
        {
            if (state == null || db == null || settlement == null) return 0;
            int total = 0;
            var ids = settlement.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed || (!b.IsComplete && !includeUnfinished)) continue;
                if (!db.TryGetBuilding(b.Type, out var def)) continue;
                total++;
                int c = (int)def.Category;
                if (c >= 0 && c < result.Length) result[c]++;
            }
            return total;
        }

        /// <summary>Houses, capacity, residents, homeless and average residents per house of a settlement.</summary>
        public static HousingStats HousingSummary(WorldState state, GameDatabase db, Settlement settlement)
        {
            var h = new HousingStats();
            if (state == null || db == null || settlement == null) return h;
            var ids = settlement.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                if (!db.TryGetBuilding(b.Type, out var def) || !def.IsDwelling) continue;
                if (!b.IsComplete) { h.HousesUnderConstruction++; continue; }
                h.Houses++;
                h.Capacity += Math.Max(0, b.HousingCapacity);
                h.Residents += b.Residents.Count;
            }
            var residents = settlement.ResidentIds;
            for (int i = 0; i < residents.Count; i++)
            {
                var p = state.People.Get(residents[i]);
                if (p != null && p.IsAlive && p.HomeId < 0) h.Homeless++;
            }
            h.AvgPerHouse = h.Houses > 0 ? h.Residents / (float)h.Houses : 0f;
            return h;
        }

        // ================================================================== rankings
        /// <summary>
        /// The <paramref name="n"/> most populous living settlements into <paramref name="results"/> (cleared first),
        /// largest first; ties break on ascending id. Returns the number written.
        /// </summary>
        public static int TopSettlements(WorldState state, int n, List<Settlement> results)
        {
            Guard.NotNull(results, nameof(results));
            results.Clear();
            if (state == null || n <= 0) return 0;
            var all = state.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (!s.IsAlive) continue;
                if (results.Count == n && !Before(s, results[n - 1])) continue;
                int pos = results.Count;
                while (pos > 0 && Before(s, results[pos - 1])) pos--;
                results.Insert(pos, s);
                if (results.Count > n) results.RemoveAt(results.Count - 1);
            }
            return results.Count;
        }

        private static bool Before(Settlement a, Settlement b) => a.Population != b.Population ? a.Population > b.Population : a.Id < b.Id;

        /// <summary>Civ score: population + 50 x settlements + 200 x age + research / 100.</summary>
        public static float Score(int population, int settlements, AgeId age, float totalResearch) =>
            population + 50f * settlements + 200f * (int)age + Math.Max(0f, MathUtil.Sanitize(totalResearch)) / 100f;

        /// <summary>
        /// Ranks civilizations by <see cref="Score(int, int, AgeId, float)"/> into <paramref name="results"/> (cleared
        /// first), best first; ties break on ascending id. Population counts residents of living settlements.
        /// </summary>
        public static int CivRanking(WorldState state, List<CivScore> results, bool includeFallen = false)
        {
            Guard.NotNull(results, nameof(results));
            results.Clear();
            if (state == null) return 0;
            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (!civ.IsAlive && !includeFallen) continue;
                int pop = 0, count = 0;
                var ids = civ.SettlementIds;
                for (int k = 0; k < ids.Count; k++)
                {
                    var s = state.Settlements.Get(ids[k]);
                    if (s == null || !s.IsAlive) continue;
                    count++;
                    pop += s.Population;
                }
                float research = Math.Max(0f, MathUtil.Sanitize(civ.TotalResearch));
                var row = new CivScore
                {
                    CivId = civ.Id, Population = pop, Settlements = count, Age = civ.Age, TotalResearch = research,
                    IsAlive = civ.IsAlive, Score = Score(pop, count, civ.Age, research),
                };
                int pos = results.Count;
                while (pos > 0 && Better(row, results[pos - 1])) pos--;
                results.Insert(pos, row);
            }
            return results.Count;
        }

        private static bool Better(CivScore a, CivScore b) => a.Score != b.Score ? a.Score > b.Score : a.CivId < b.CivId;

        /// <summary>
        /// Civilizations owning completed wonders into <paramref name="results"/> (cleared first), ordered by wonder
        /// count, then total wonder footprint, then ascending civ id.
        /// </summary>
        public static int LargestWonderOwners(WorldState state, GameDatabase db, List<WonderOwnership> results)
        {
            Guard.NotNull(results, nameof(results));
            results.Clear();
            if (state == null || db == null) return 0;
            var buildings = state.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++)
            {
                var b = buildings[i];
                if (b.IsDestroyed || !b.IsComplete || b.CivilizationId < 0) continue;
                if (!db.TryGetBuilding(b.Type, out var def) || !(def.IsWonder || def.Category == BuildingCategory.Wonder)) continue;
                int area = Math.Max(1, b.Width) * Math.Max(1, b.Height);
                int idx = -1;
                for (int k = 0; k < results.Count; k++) if (results[k].CivId == b.CivilizationId) { idx = k; break; }
                if (idx < 0)
                {
                    results.Add(new WonderOwnership { CivId = b.CivilizationId, WonderCount = 1, TotalArea = area, LargestWonder = b.Type, LargestArea = area });
                    continue;
                }
                var row = results[idx];
                row.WonderCount++;
                row.TotalArea += area;
                if (area > row.LargestArea) { row.LargestArea = area; row.LargestWonder = b.Type; }
                results[idx] = row;
            }
            // Insertion sort (stable, allocation-free; lists are tiny).
            for (int i = 1; i < results.Count; i++)
            {
                var cur = results[i];
                int j = i - 1;
                while (j >= 0 && WonderBefore(cur, results[j])) { results[j + 1] = results[j]; j--; }
                results[j + 1] = cur;
            }
            return results.Count;
        }

        private static bool WonderBefore(WonderOwnership a, WonderOwnership b)
        {
            if (a.WonderCount != b.WonderCount) return a.WonderCount > b.WonderCount;
            if (a.TotalArea != b.TotalArea) return a.TotalArea > b.TotalArea;
            return a.CivId < b.CivId;
        }

        // ================================================================== lifespan
        /// <summary>Mean age (years) at death of everyone who died while the tracker listened; 0 if none.</summary>
        public static float AverageLifespanAtDeath(LifespanTracker tracker) => tracker != null ? tracker.Average : 0f;

        /// <summary>Mean age (years) at death for one race; 0 if none recorded.</summary>
        public static float AverageLifespanAtDeath(LifespanTracker tracker, RaceId race) => tracker != null ? tracker.AverageFor(race) : 0f;

        /// <summary>
        /// Estimated life expectancy (years) of a race: the race's natural LifeExpectancy plus the population-weighted
        /// LifespanBonusYears of its living civilizations (progression folds age and tech bonuses into civ modifiers),
        /// blended toward the observed mean age at death once enough deaths were recorded (up to 50% weight at 200+
        /// deaths). The observed mean includes child and violent deaths, so it pulls the estimate down in harsh times.
        /// Returns 0 if the race is not registered.
        /// </summary>
        public static float LifeExpectancyEstimate(WorldState state, GameDatabase db, RaceId race, LifespanTracker tracker = null)
        {
            if (db == null) return 0f;
            int ri = (int)race;
            if (ri < 0 || ri >= db.Races.Count || db.Races[ri] == null) return 0f;
            float baseYears = Math.Max(0f, MathUtil.Sanitize(db.Races[ri].LifeExpectancy));

            double bonus = 0; long weight = 0; int civCount = 0; double plainBonus = 0;
            if (state != null)
            {
                var civs = state.Civilizations.Items;
                for (int i = 0; i < civs.Count; i++)
                {
                    var civ = civs[i];
                    if (!civ.IsAlive || civ.Race != race) continue;
                    float b = MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.LifespanBonusYears), -1000f, 1000f);
                    int pop = 0;
                    var ids = civ.SettlementIds;
                    for (int k = 0; k < ids.Count; k++) { var s = state.Settlements.Get(ids[k]); if (s != null && s.IsAlive) pop += s.Population; }
                    bonus += (double)b * pop;
                    weight += pop;
                    plainBonus += b;
                    civCount++;
                }
            }
            float avgBonus = weight > 0 ? (float)(bonus / weight) : (civCount > 0 ? (float)(plainBonus / civCount) : 0f);
            float estimate = Math.Max(0f, baseYears + avgBonus);

            if (tracker != null)
            {
                long deaths = tracker.CountFor(race);
                if (deaths > 0)
                {
                    float w = 0.5f * MathUtil.Clamp01(deaths / 200f);
                    estimate = MathUtil.LerpUnclamped(estimate, tracker.AverageFor(race), w);
                }
            }
            return MathUtil.Sanitize(estimate);
        }

        /// <summary>World-wide estimate: <see cref="LifeExpectancyEstimate(WorldState, GameDatabase, RaceId, LifespanTracker)"/> weighted by living population per race.</summary>
        public static float LifeExpectancyEstimate(WorldState state, GameDatabase db, LifespanTracker tracker = null)
        {
            if (state == null || db == null) return 0f;
            var byRace = new int[Counts.Races];
            int total = PopulationByRace(state, byRace);
            if (total == 0) return 0f;
            double sum = 0;
            for (int r = 0; r < byRace.Length; r++)
                if (byRace[r] > 0) sum += (double)LifeExpectancyEstimate(state, db, (RaceId)r, tracker) * byRace[r];
            return MathUtil.Sanitize((float)(sum / total));
        }
    }
}
