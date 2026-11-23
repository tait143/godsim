using System;
using System.Collections.Generic;

namespace GodSim.Core.Progression
{
    /// <summary>
    /// Read-only queries about research and age progression, shared by the progression systems, the AI
    /// and the UI. Every method is null-safe, never throws on bad data and never returns NaN.
    /// The per-tick helpers do not allocate (the attainability table is computed once per frozen database and cached).
    /// </summary>
    public static class TechQueries
    {
        /// <summary>Research points each resident contributes per tick, on top of research buildings.</summary>
        public const float ResearchPerResident = 0.004f;

        /// <summary>
        /// Research points per point of Research service output (Town Center, Scriptorium, Library ...). Balance pass
        /// 2026-11-19: was implicitly 1. Research buildings stack per settlement and per age, so late civs out-researched
        /// the age windows 20-60x; the service share is damped, the per-resident share is unchanged.
        /// </summary>
        public const float ResearchPerServicePoint = 0.4f;

        // ------------------------------------------------------------------ research targets

        /// <summary>
        /// Fills <paramref name="buffer"/> (cleared first) with the ids of every tech the civ can research now, in ascending
        /// id order: <see cref="GameDatabase.CanResearch"/> holds and the cost is finite (<see cref="IsAffordable"/>).
        /// Does not allocate.
        /// </summary>
        /// <returns>The number of researchable techs.</returns>
        public static int ResearchableTechs(Civilization civ, GameDatabase db, List<int> buffer)
        {
            if (buffer == null) return 0;
            buffer.Clear();
            if (civ == null || db == null) return 0;
            var techs = db.Techs;
            for (int i = 0; i < techs.Count; i++)
                if (CanResearchNow(civ, db, techs[i])) buffer.Add(techs[i].Id);
            return buffer.Count;
        }

        /// <summary>True if <paramref name="techId"/> names a registered, affordable tech the civ can research now.</summary>
        public static bool IsValidResearchTarget(Civilization civ, GameDatabase db, int techId)
        {
            if (civ == null || db == null || techId < 0) return false;
            return CanResearchNow(civ, db, db.Tech(techId));
        }

        /// <summary>
        /// Deterministic fallback target: the cheapest researchable tech, ties broken by lowest id.
        /// Techs with a non-finite cost are never chosen. Returns -1 when nothing is researchable.
        /// </summary>
        public static int CheapestResearchable(Civilization civ, GameDatabase db)
        {
            if (civ == null || db == null) return -1;
            int best = -1;
            float bestCost = float.MaxValue;
            var techs = db.Techs;
            for (int i = 0; i < techs.Count; i++)
            {
                var t = techs[i];
                if (!CanResearchNow(civ, db, t)) continue;
                float cost = SafeCost(t);
                if (best < 0 || cost < bestCost || (cost == bestCost && t.Id < best)) { best = t.Id; bestCost = cost; }
            }
            return best;
        }

        /// <summary>
        /// The tech the research system actually pursues: the AI's <see cref="Civilization.CurrentResearchTechId"/> when valid,
        /// otherwise <see cref="CheapestResearchable"/>; -1 when nothing is researchable. This is the live value of
        /// <see cref="ProgressionKeys.ResearchTarget"/>, which the research system refreshes on every run.
        /// </summary>
        public static int EffectiveResearchTarget(Civilization civ, GameDatabase db)
        {
            if (civ == null || db == null) return -1;
            return IsValidResearchTarget(civ, db, civ.CurrentResearchTechId) ? civ.CurrentResearchTechId : CheapestResearchable(civ, db);
        }

        /// <summary>
        /// Cost of the most expensive tech of <paramref name="age"/> that <paramref name="race"/> can obtain
        /// (<see cref="IsAttainable"/>). Falls back to any affordable tech of that age, then to every affordable tech.
        /// Techs with a non-finite cost are ignored. 0 when the database has no affordable techs.
        /// </summary>
        public static float MaxTechCostInAge(GameDatabase db, AgeId age, RaceId race)
        {
            if (db == null) return 0f;
            var attainable = AttainableFor(db, race);
            var techs = db.Techs;
            float raceMax = 0f, ageMax = 0f, anyMax = 0f;
            for (int i = 0; i < techs.Count; i++)
            {
                var t = techs[i];
                if (!IsAffordable(t)) continue;
                float c = SafeCost(t);
                if (c > anyMax) anyMax = c;
                if (t.Age != age) continue;
                if (c > ageMax) ageMax = c;
                if (attainable != null && i < attainable.Length && attainable[i] && c > raceMax) raceMax = c;
            }
            return raceMax > 0f ? raceMax : (ageMax > 0f ? ageMax : anyMax);
        }

        /// <summary>
        /// True if the tech's cost is a finite number research can reach (≤ <see cref="ResearchSystem.MaxTotalResearch"/>).
        /// A NaN or infinite cost passes <see cref="GameDatabase.Validate"/>, but such a tech can never be learned, so it is
        /// never a research target and does not count toward age requirements.
        /// </summary>
        public static bool IsAffordable(TechDefinition tech)
        {
            if (tech == null) return false;
            float c = tech.Cost;
            return !float.IsNaN(c) && !float.IsInfinity(c) && c <= ResearchSystem.MaxTotalResearch;
        }

        // ------------------------------------------------------------------ research rate

        /// <summary>Research points per tick the civ currently generates (see <see cref="ResearchPerTick(WorldState, GameDatabase, Civilization, SimConfig)"/>).</summary>
        public static float ResearchPerTick(SimContext ctx, Civilization civ)
        {
            if (ctx == null) return 0f;
            return ResearchPerTick(ctx.State, ctx.Db, civ, ctx.Config);
        }

        /// <summary>
        /// Research points per tick: sum over alive settlements still owned by the civ of
        /// (<see cref="ResearchPerServicePoint"/> × ServiceOutput[Research] + <see cref="ResearchPerResident"/> × population), times
        /// <c>civ.Modifiers.Research</c> and <c>config.ResearchMultiplier</c>. Never negative or NaN.
        /// </summary>
        public static float ResearchPerTick(WorldState state, GameDatabase db, Civilization civ, SimConfig config)
        {
            if (state == null || civ == null) return 0f;
            float raw = 0f;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (!Owns(civ, s)) continue;
                float output = MathUtil.Sanitize(s.ServiceOutput[(int)ServiceType.Research]);
                if (output < 0f) output = 0f;
                raw += ResearchPerServicePoint * output + ResearchPerResident * s.Population;
            }
            float mod = MathUtil.Sanitize(civ.Modifiers.Research, 1f);
            if (mod < 0f) mod = 0f;
            float cfg = config != null ? MathUtil.Sanitize(config.ResearchMultiplier, 1f) : 1f;
            if (cfg < 0f) cfg = 0f;
            float rate = MathUtil.Sanitize(raw * mod * cfg);
            return rate < 0f ? 0f : (rate > 1e9f ? 1e9f : rate);
        }

        // ------------------------------------------------------------------ civ statistics

        /// <summary>Living population of the civ: residents of its alive settlements that it still owns.</summary>
        public static int PopulationOf(WorldState state, Civilization civ)
        {
            if (state == null || civ == null) return 0;
            int pop = 0;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (Owns(civ, s)) pop += s.Population;
            }
            return pop;
        }

        /// <summary>Number of alive settlements owned by the civ (dead or captured ones still listed are ignored).</summary>
        public static int AliveSettlementsOf(WorldState state, Civilization civ)
        {
            if (state == null || civ == null) return 0;
            int n = 0;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++) if (Owns(civ, state.Settlements.Get(ids[i]))) n++;
            return n;
        }

        /// <summary>Number of techs of <paramref name="age"/> the civ knows.</summary>
        public static int TechsKnownInAge(Civilization civ, GameDatabase db, AgeId age)
        {
            if (civ == null || db == null || civ.KnownTechs.Count == 0) return 0;
            int n = 0;
            var techs = db.Techs;
            for (int i = 0; i < techs.Count; i++)
                if (techs[i] != null && techs[i].Age == age && civ.KnownTechs.Contains(techs[i].Id)) n++;
            return n;
        }

        // ------------------------------------------------------------------ attainability

        /// <summary>
        /// True if <paramref name="race"/> can ever obtain the tech, starting from no techs. The tech must be allowed for
        /// the race and have a finite cost (<see cref="IsAffordable"/>). Every prerequisite must be from the same or an
        /// earlier age and attainable too, applied transitively. A general tech gated behind another race's tech is
        /// therefore not attainable.
        /// </summary>
        public static bool IsAttainable(GameDatabase db, int techId, RaceId race)
        {
            if (db == null) return false;
            var a = AttainableFor(db, race);
            return a != null && techId >= 0 && techId < a.Length && a[techId];
        }

        /// <summary>Number of techs of <paramref name="age"/> that <paramref name="race"/> can obtain (<see cref="IsAttainable"/>).</summary>
        public static int TechsAvailableInAge(GameDatabase db, AgeId age, RaceId race)
        {
            if (db == null) return 0;
            var a = AttainableFor(db, race);
            if (a == null) return 0;
            int n = 0;
            var techs = db.Techs;
            for (int i = 0; i < techs.Count && i < a.Length; i++)
                if (a[i] && techs[i] != null && techs[i].Age == age) n++;
            return n;
        }

        /// <summary>
        /// Number of techs of <paramref name="age"/> the civ already knows or can still obtain. This caps the age
        /// requirement, so a civ is never asked for techs it can never learn. The count is a safe lower bound: a known
        /// foreign-race tech can open more techs to the civ than its race alone could reach.
        /// </summary>
        public static int TechsAvailableToCiv(Civilization civ, GameDatabase db, AgeId age)
        {
            if (civ == null || db == null) return 0;
            var a = AttainableFor(db, civ.Race);
            int n = 0;
            var techs = db.Techs;
            for (int i = 0; i < techs.Count; i++)
            {
                var t = techs[i];
                if (t == null || t.Age != age) continue;
                if ((a != null && i < a.Length && a[i]) || civ.KnownTechs.Contains(t.Id)) n++;
            }
            return n;
        }

        // ------------------------------------------------------------------ age progression

        /// <summary>
        /// Evaluates every requirement of the civ's next age: research threshold, techs known from the current age
        /// (requirement capped at <see cref="TechsAvailableToCiv"/>), population and settlements.
        /// </summary>
        public static AgeAdvanceCheck EvaluateNextAge(Civilization civ, GameDatabase db, WorldState state)
        {
            var c = new AgeAdvanceCheck();
            if (civ == null) return c;
            c.CurrentAge = civ.Age;
            c.TotalResearch = MathUtil.Sanitize(civ.TotalResearch);
            if (db == null || (int)civ.Age >= Counts.Ages - 1 || civ.Age >= AgeId.Ascension)
            {
                c.NextAge = civ.Age;
                c.Progress = db == null ? 0f : 1f;
                return c;
            }
            var next = (AgeId)((int)civ.Age + 1);
            var def = db.Ages[(int)next];
            c.NextAge = next;
            if (def == null) { c.Progress = 0f; return c; }
            c.HasNextAge = true;

            var curDef = db.Ages[(int)civ.Age];
            float from = curDef != null ? MathUtil.Sanitize(curDef.ResearchThreshold) : 0f;
            c.ResearchThreshold = MathUtil.Sanitize(def.ResearchThreshold);
            if (from > c.ResearchThreshold) from = 0f;
            c.ResearchProgress = c.TotalResearch >= c.ResearchThreshold ? 1f : MathUtil.InverseLerp(from, c.ResearchThreshold, c.TotalResearch);

            int available = TechsAvailableToCiv(civ, db, civ.Age);
            c.TechsRequired = MathUtil.Clamp(def.RequiredTechsFromPreviousAge, 0, available);
            c.TechsKnown = TechsKnownInAge(civ, db, civ.Age);
            float techP = c.TechsRequired <= 0 ? 1f : MathUtil.Clamp01(c.TechsKnown / (float)c.TechsRequired);

            c.Population = PopulationOf(state, civ);
            c.MinPopulation = Math.Max(0, def.MinPopulation);
            float popP = c.MinPopulation <= 0 ? 1f : MathUtil.Clamp01(c.Population / (float)c.MinPopulation);

            c.Settlements = AliveSettlementsOf(state, civ);
            c.MinSettlements = Math.Max(0, def.MinSettlements);
            float setP = c.MinSettlements <= 0 ? 1f : MathUtil.Clamp01(c.Settlements / (float)c.MinSettlements);

            float p = Math.Min(Math.Min(c.ResearchProgress, techP), Math.Min(popP, setP));
            c.Progress = MathUtil.Clamp01(MathUtil.Sanitize(p));
            return c;
        }

        /// <summary>0..1 progress estimate toward the next age (minimum over all criteria); 1 at the final age.</summary>
        public static float ProgressToNextAge(Civilization civ, GameDatabase db, WorldState state)
        {
            return EvaluateNextAge(civ, db, state).Progress;
        }

        /// <summary>
        /// Human-readable list of unmet requirements for the next age (e.g. "Population 4 / 10").
        /// Empty when every requirement is met or the civ is at the final age.
        /// </summary>
        public static List<string> MissingRequirements(Civilization civ, GameDatabase db, WorldState state)
        {
            var list = new List<string>();
            if (civ == null || db == null) return list;
            var c = EvaluateNextAge(civ, db, state);
            if (!c.HasNextAge)
            {
                if (civ.Age < AgeId.Ascension) list.Add("The next age is not defined.");
                return list;
            }
            string nextName = ProgressionText.AgeName(db.Ages[(int)c.NextAge], c.NextAge);
            if (!c.ResearchMet)
                list.Add("Research " + MathUtil.FloorToInt(c.TotalResearch) + " / " + MathUtil.FloorToInt(c.ResearchThreshold) + " to enter the " + nextName);
            if (!c.TechsMet)
                list.Add(ProgressionText.AgeName(db.Ages[(int)c.CurrentAge], c.CurrentAge) + " technologies known " + c.TechsKnown + " / " + c.TechsRequired);
            if (!c.PopulationMet)
                list.Add("Population " + c.Population + " / " + c.MinPopulation);
            if (!c.SettlementsMet)
                list.Add("Settlements " + c.Settlements + " / " + c.MinSettlements);
            return list;
        }

        // ------------------------------------------------------------------ helpers

        internal static bool Owns(Civilization civ, Settlement s) => s != null && s.IsAlive && s.CivilizationId == civ.Id;

        /// <summary>Cost used for comparisons and pool arithmetic. Never NaN or infinite; unaffordable techs map to the cap.</summary>
        internal static float SafeCost(TechDefinition t)
        {
            if (!IsAffordable(t)) return ResearchSystem.MaxTotalResearch;
            float c = t.Cost;
            return c < 0f ? 0f : c;
        }

        /// <summary><see cref="GameDatabase.CanResearch"/> plus the affordability and null-safety guards the systems need.</summary>
        private static bool CanResearchNow(Civilization civ, GameDatabase db, TechDefinition t)
        {
            return t != null && t.Prerequisites != null && IsAffordable(t) && db.CanResearch(civ, t);
        }

        // Attainability table: [race][techId]. Immutable once built and swapped atomically, so readers never see a torn
        // cache. Only frozen databases are cached, because their tech graph can no longer change. Keyed weakly on the
        // database instance so the cache never keeps a discarded database alive.
        private sealed class AttainabilityCache
        {
            public readonly WeakReference<GameDatabase> Db;
            public readonly int TechCount;
            public readonly bool[][] ByRace;

            public AttainabilityCache(GameDatabase db, int techCount, bool[][] byRace)
            {
                Db = new WeakReference<GameDatabase>(db);
                TechCount = techCount;
                ByRace = byRace;
            }
        }

        private static AttainabilityCache _attainability;

        /// <summary>Attainability flags indexed by tech id for one race, or null for an undefined race value.</summary>
        private static bool[] AttainableFor(GameDatabase db, RaceId race)
        {
            int r = (int)race;
            if (r < 0 || r >= Counts.Races) return null;
            var techs = db.Techs;
            if (!db.IsFrozen) return ComputeAttainable(techs, race); // content may still change: never cached
            var cache = _attainability;
            if (cache != null && cache.TechCount == techs.Count && cache.Db.TryGetTarget(out var cachedDb) && ReferenceEquals(cachedDb, db))
                return cache.ByRace[r];
            var byRace = new bool[Counts.Races][];
            for (int i = 0; i < byRace.Length; i++) byRace[i] = ComputeAttainable(techs, (RaceId)i);
            _attainability = new AttainabilityCache(db, techs.Count, byRace);
            return byRace[r];
        }

        /// <summary>
        /// Iterative, memoised depth-first search over prerequisites (no recursion, so hostile content cannot overflow the
        /// stack). Cycles, which are only possible before Freeze, unknown prerequisite ids and later-age prerequisites all make
        /// a tech unattainable.
        /// </summary>
        private static bool[] ComputeAttainable(IReadOnlyList<TechDefinition> techs, RaceId race)
        {
            int n = techs.Count;
            var state = new byte[n]; // 0 unvisited, 1 on the DFS stack, 2 attainable, 3 not attainable
            var next = new int[n];   // next prerequisite index to inspect while a node is on the stack
            var stack = new int[n];  // each node is pushed at most once (state 0 -> 1)
            for (int root = 0; root < n; root++)
            {
                if (state[root] != 0) continue;
                if (!BaseAttainable(techs[root], race)) { state[root] = 3; continue; }
                int top = 0;
                stack[0] = root;
                state[root] = 1;
                next[root] = 0;
                while (top >= 0)
                {
                    int id = stack[top];
                    var t = techs[id];
                    var pre = t.Prerequisites;
                    int preCount = pre != null ? pre.Length : 0;
                    byte verdict = 2;
                    bool descended = false;
                    while (next[id] < preCount)
                    {
                        int p = pre[next[id]];
                        if (p < 0 || p >= n || techs[p] == null || techs[p].Age > t.Age) { verdict = 3; break; }
                        byte s = state[p];
                        if (s == 2) { next[id]++; continue; }
                        if (s == 0)
                        {
                            if (!BaseAttainable(techs[p], race)) { state[p] = 3; verdict = 3; break; }
                            state[p] = 1;
                            next[p] = 0;
                            stack[++top] = p;
                            descended = true;
                            break;
                        }
                        verdict = 3; // 3: unattainable prerequisite; 1: prerequisite cycle
                        break;
                    }
                    if (descended) continue;
                    state[id] = verdict;
                    top--;
                }
            }
            var result = new bool[n];
            for (int i = 0; i < n; i++) result[i] = state[i] == 2;
            return result;
        }

        private static bool BaseAttainable(TechDefinition t, RaceId race) => t != null && t.AllowedRaces.Allows(race) && IsAffordable(t);
    }
}
