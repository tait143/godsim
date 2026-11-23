using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Urban design advisor: makes settlements look and work like real places.
    ///
    /// Every run it compares a settlement against its tier's <see cref="UrbanArchetype"/>:
    /// 1. Civic core: walks the cumulative checklist (Hamlet: Town Center, Well, Storehouse, shrine; Village: + Market,
    ///    Granary, Temple; Town: + Library, garrison/walls, Bathhouse/Herbalist, Theater/Tavern; City: + University, Bank,
    ///    Hospital, Cathedral or race equivalent, Forum; ...), resolving each role to the best AVAILABLE building for the
    ///    civ's age and race, and votes strongly for missing roles (overdue foundations first) with a readable reason.
    /// 2. Anticipation: when the (growth-projected) population nears the next tier, it starts planning that tier's core.
    /// 3. Category mix: target buildings per 100 residents, flavoured by race, favored buildings and personality drift.
    ///    The most under-represented categories get votes (strength ~ deficit); strongly over-represented ones get mild
    ///    negative votes so towns stay varied instead of becoming smelter fields.
    /// 4. Housing: ~1 dwelling per 3-5 residents with headroom for projected growth.
    /// 5. Recovery: a sudden loss of buildings (fire, war, disaster) boosts rebuilding of the core and asks for builders.
    /// Votes are soft influence through <see cref="AdvisorBoard"/>; the queue is never touched.
    /// </summary>
    public sealed class UrbanDesignAdvisor : AdvisorSystemBase
    {
        public const string Id = "urban";
        /// <summary>Settlement memory: population at last run (growth projection).</summary>
        public const string PopKey = "ai.urban.pop";
        /// <summary>Settlement memory: placed buildings at last run (loss detection).</summary>
        public const string BuildingsKey = "ai.urban.bld";
        /// <summary>Settlement memory: tick until which the settlement is rebuilding after a loss.</summary>
        public const string RecoverKey = "ai.urban.recover";
        /// <summary>Settlement memory: civic-core completeness 0..1 (UI/learning).</summary>
        public const string CoreKey = "ai.urban.core";

        public const int MaxCoreVotes = 4;
        public const int MaxMixVotes = 3;
        public const int RecoveryTicks = 96;
        public const float MixDeficitThreshold = 0.75f;
        public const float PersonsPerDwelling = 4f;

        /// <summary>How much this advisor's archetype view should weigh per category (others own food/resources/military).</summary>
        private static readonly float[] s_authority = BuildAuthority();

        private readonly UrbanComposition _comp = new UrbanComposition();
        private readonly UrbanSiteSurvey _site = new UrbanSiteSurvey();
        private readonly float[] _mult = new float[UrbanArchetypeCatalog.CategoryCount];
        private readonly float[] _target = new float[UrbanArchetypeCatalog.CategoryCount];
        private readonly float[] _deficit = new float[UrbanArchetypeCatalog.CategoryCount];
        private readonly bool[] _picked = new bool[UrbanArchetypeCatalog.CategoryCount];
        private readonly bool[] _coreWanted = new bool[Counts.Buildings];
        private float _weight = 1f;

        public override string AdvisorId => Id;
        public override int TickInterval => 24;

        /// <summary>Civic-core completeness of the last advised settlement (diagnostics/tests).</summary>
        public float LastCoreCompleteness { get; private set; }
        /// <summary>Largest weighted category deficit ratio of the last advised settlement (diagnostics/tests).</summary>
        public float LastMixPressure { get; private set; }

        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            var state = ctx.State;
            if (db == null || state == null) return;
            int pop = s.Population;
            if (pop <= 0) { Urgency(s, 0f); return; }

            _weight = AdvisorBoard.Weight(civ, AdvisorId);
            var arche = UrbanArchetypeCatalog.Get(s.Tier);
            _comp.Survey(state, db, s);
            if (state.Map != null) _site.Scan(state.Map, s.Center, MathUtil.Clamp(s.TerritoryRadius + 2, 4, 16));
            else _site.AllowAll();
            UrbanRaceFlavor.Fill(db, civ, _mult);
            Array.Clear(_coreWanted, 0, _coreWanted.Length);

            // Plan for the town we are becoming, not the one we were: project two runs of growth ahead.
            float lastPop = MathUtil.Sanitize(s.Mem(PopKey, pop), pop);
            float growth = MathUtil.Clamp(pop - lastPop, 0f, pop * 0.5f);
            float projected = pop + growth * 2f;
            s.Memory[PopKey] = pop;

            bool recovering = UpdateRecovery(ctx.Tick, s);

            float completeness = AdviseCore(ctx, civ, s, arche, recovering, out int missing);
            s.Memory[CoreKey] = completeness;
            AnticipateNextTier(ctx, civ, s, arche, projected);
            float pressure = AdviseMix(ctx, civ, s, arche, projected);
            AdviseHousing(ctx, civ, s, pop, projected);

            if ((missing >= 2 && pop >= 15) || recovering)
                VoteJob(s, civ, JobType.Builder, MathUtil.Clamp(0.1f + 0.08f * missing + (recovering ? 0.2f : 0f), 0f, 0.45f));

            Urgency(s, 0.55f * (1f - completeness) + 0.3f * pressure + (recovering ? 0.15f : 0f));
            LastCoreCompleteness = completeness;
            LastMixPressure = pressure;
        }

        // ------------------------------------------------------------------ recovery
        private bool UpdateRecovery(long tick, Settlement s)
        {
            int current = _comp.TotalExisting;
            float last = MathUtil.Sanitize(s.Mem(BuildingsKey, current), current);
            s.Memory[BuildingsKey] = current;
            if (last - current >= 3f && current < last * 0.85f) s.Memory[RecoverKey] = tick + RecoveryTicks;
            if (!s.Memory.TryGetValue(RecoverKey, out float until)) return false;
            if (tick >= until || float.IsNaN(until)) { s.Memory.Remove(RecoverKey); return false; }
            return true;
        }

        // ------------------------------------------------------------------ civic core
        private float AdviseCore(SimContext ctx, Civilization civ, Settlement s, UrbanArchetype arche, bool recovering, out int missing)
        {
            missing = 0;
            int applicable = 0, satisfied = 0, votes = 0;
            var roles = arche.CoreRoles;
            for (int i = 0; i < roles.Length; i++)
            {
                var st = UrbanRoleResolver.Evaluate(ctx.Db, civ, roles[i], s, _comp, _site, ctx.State);
                if (!st.Applicable) continue;
                applicable++;
                if (st.Satisfied) { satisfied++; continue; }
                missing++;
                if (votes >= MaxCoreVotes || _coreWanted[(int)st.Building]) continue;
                bool overdue = arche.RoleTier[i] < s.Tier;
                float strength = (overdue ? 0.85f : 0.7f) - 0.12f * votes;
                if (strength < 0.3f) strength = 0.3f;
                if (recovering) strength += 0.15f;
                _coreWanted[(int)st.Building] = true;
                string reason = null;
                if (IsStrong(strength))
                {
                    string name = BuildingName(ctx.Db, st.Building);
                    reason = Lead(arche, s) + (overdue ? " still lacks " : " needs ") + Article(name) + name
                        + (overdue ? " (" + UrbanArchetypeCatalog.Get(arche.RoleTier[i]).Name + " core)" : " (civic core)")
                        + (recovering ? ", rebuilding" : "");
                }
                VoteBuild(s, civ, st.Building, strength, reason);
                votes++;
            }
            return applicable == 0 ? 1f : (float)satisfied / applicable;
        }

        private void AnticipateNextTier(SimContext ctx, Civilization civ, Settlement s, UrbanArchetype arche, float projected)
        {
            if (arche.NextTierPopulation <= 0 || projected < 0.8f * arche.NextTierPopulation) return;
            var next = UrbanArchetypeCatalog.Get((SettlementTier)((int)arche.Tier + 1));
            if (next == arche) return;
            for (int i = 0; i < next.CoreRoles.Length; i++)
            {
                if (next.RoleTier[i] != next.Tier) continue;
                var st = UrbanRoleResolver.Evaluate(ctx.Db, civ, next.CoreRoles[i], s, _comp, _site, ctx.State);
                if (!st.Applicable || st.Satisfied || !st.Resolved || _coreWanted[(int)st.Building]) continue;
                _coreWanted[(int)st.Building] = true;
                const float strength = 0.3f;
                string reason = null;
                if (IsStrong(strength))
                {
                    string name = BuildingName(ctx.Db, st.Building);
                    reason = "Growing toward " + Article(next.Name) + next.Name + ": plan " + Article(name) + name;
                }
                VoteBuild(s, civ, st.Building, strength, reason);
                return;
            }
        }

        // ------------------------------------------------------------------ category mix
        private float AdviseMix(SimContext ctx, Civilization civ, Settlement s, UrbanArchetype arche, float projected)
        {
            var db = ctx.Db;
            float per = projected / 100f;
            float pressure = 0f;
            int n = UrbanArchetypeCatalog.CategoryCount;
            int housing = (int)BuildingCategory.Housing, wonder = (int)BuildingCategory.Wonder;
            for (int c = 0; c < n; c++)
            {
                _deficit[c] = 0f;
                _target[c] = 0f;
                _picked[c] = false;
                if (c == housing || c == wonder) continue;
                float target = MathUtil.Sanitize(arche.PerHundred[c] * _mult[c] * per);
                int actual = _comp.CategoryCount[c];
                _target[c] = target;
                _deficit[c] = target - actual;
                if (target >= 1f && _deficit[c] > 0f) pressure = Math.Max(pressure, MathUtil.Clamp01(_deficit[c] / target) * s_authority[c]);
                if (actual >= 3 && actual > target * 1.75f + 1.5f) VoteOverbuilt(db, civ, s, arche, c, actual, target);
            }

            int cast = 0;
            for (int iter = 0; iter < n && cast < MaxMixVotes; iter++)
            {
                int best = -1;
                float bestScore = 0f;
                for (int c = 0; c < n; c++)
                {
                    if (_picked[c] || _deficit[c] < MixDeficitThreshold) continue;
                    float score = _deficit[c] * s_authority[c];
                    if (score > bestScore) { bestScore = score; best = c; }
                }
                if (best < 0) break;
                _picked[best] = true;
                int t = PickForCategory(db, civ, s, ctx.State, (BuildingCategory)best);
                if (t < 0) continue;
                float strength = MathUtil.Clamp(0.15f + 0.2f * _deficit[best], 0.15f, 0.75f) * s_authority[best];
                string reason = null;
                if (IsStrong(strength))
                    reason = Lead(arche, s) + " has " + _comp.CategoryCount[best] + " of ~" + MathUtil.RoundToInt(_target[best]) + " "
                        + UrbanArchetypeCatalog.CategoryNames[best] + " buildings (archetype)";
                VoteBuild(s, civ, (BuildingType)t, strength, reason);
                cast++;
            }
            return pressure;
        }

        private void VoteOverbuilt(GameDatabase db, Civilization civ, Settlement s, UrbanArchetype arche, int category, int actual, float target)
        {
            float excess = actual - target;
            float strength = -MathUtil.Clamp(0.1f + 0.05f * excess, 0.1f, 0.4f);
            bool logged = false;
            for (int t = 0; t < Counts.Buildings; t++)
            {
                if (_coreWanted[t] || !db.TryGetBuilding((BuildingType)t, out var def) || def == null) continue;
                if ((int)def.Category != category || def.IsWonder) continue;
                if (!db.IsBuildingAvailable(civ, (BuildingType)t)) continue;
                string reason = null;
                if (!logged && IsStrong(strength))
                {
                    reason = "Too much " + UrbanArchetypeCatalog.CategoryNames[category] + " for " + Article(arche.Name) + arche.Name
                        + " of " + s.Population + " (" + actual + " vs ~" + MathUtil.RoundToInt(target) + ")";
                    logged = true;
                }
                VoteBuild(s, civ, (BuildingType)t, strength, reason);
            }
        }

        // ------------------------------------------------------------------ housing
        private void AdviseHousing(SimContext ctx, Civilization civ, Settlement s, int pop, float projected)
        {
            float needCap = projected * 1.1f + 2f;
            float cap = _comp.HousingCapacity;
            float shortHomes = (needCap - cap) / PersonsPerDwelling;
            if (s.Homeless > 0) shortHomes = Math.Max(shortHomes, s.Homeless / PersonsPerDwelling);
            int housing = (int)BuildingCategory.Housing;
            if (shortHomes >= 1f)
            {
                int t = PickForCategory(ctx.Db, civ, s, ctx.State, BuildingCategory.Housing);
                if (t < 0) return;
                float strength = MathUtil.Clamp(0.2f + 0.12f * shortHomes, 0.2f, 0.7f) * s_authority[housing];
                string reason = null;
                if (IsStrong(strength))
                    reason = Lead(UrbanArchetypeCatalog.Get(s.Tier), s) + " has homes for " + _comp.HousingCapacity + "; plan ~"
                        + MathUtil.RoundToInt(shortHomes) + " more (3-5 per house)";
                VoteBuild(s, civ, (BuildingType)t, strength, reason);
            }
            else if (cap > pop * 1.6f + 10f && s.Homeless == 0)
            {
                var db = ctx.Db;
                for (int t = 0; t < Counts.Buildings; t++)
                {
                    if (!db.TryGetBuilding((BuildingType)t, out var def) || def == null || !def.IsDwelling) continue;
                    if (!db.IsBuildingAvailable(civ, (BuildingType)t)) continue;
                    VoteBuild(s, civ, (BuildingType)t, -0.15f);
                }
            }
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Best buildable non-wonder building of a category: newer, race-favored and not yet present (variety) wins.</summary>
        private int PickForCategory(GameDatabase db, Civilization civ, Settlement s, WorldState state, BuildingCategory cat)
        {
            var race = UrbanRoleResolver.RaceOf(db, civ.Race);
            int best = -1;
            float bestScore = 0f;
            for (int t = 0; t < Counts.Buildings; t++)
            {
                if (_coreWanted[t] || !db.TryGetBuilding((BuildingType)t, out var def) || def == null) continue;
                if (def.Category != cat || def.IsWonder || def.UniquePerCivilization) continue;
                if (cat == BuildingCategory.Housing && !def.IsDwelling) continue;
                if (!UrbanRoleResolver.Passes(db, civ, def, s, _comp, _site, state)) continue;
                float score = 0.15f * (int)def.MinAge - 0.35f * _comp.Present((BuildingType)t)
                    + (UrbanRoleResolver.IsFavored(race, (BuildingType)t) ? 0.6f : 0f)
                    + (def.HasJobs || (def.Services != null && def.Services.Length > 0) || def.IsDwelling || def.StorageCapacity > 0f ? 0.1f : 0f);
                if (best < 0 || score > bestScore) { best = t; bestScore = score; }
            }
            return best;
        }

        private bool IsStrong(float strength) => Math.Abs(strength * _weight) >= 0.5f;

        private static string Lead(UrbanArchetype arche, Settlement s) => "A " + arche.Name + " of " + s.Population;

        private static string BuildingName(GameDatabase db, BuildingType t)
        {
            if (db != null && db.TryGetBuilding(t, out var def) && def != null && !string.IsNullOrEmpty(def.Name)) return def.Name;
            return t.ToString();
        }

        private static string Article(string name)
        {
            if (string.IsNullOrEmpty(name)) return "a ";
            char c = char.ToUpperInvariant(name[0]);
            return c == 'A' || c == 'E' || c == 'I' || c == 'O' || c == 'U' ? "an " : "a ";
        }

        private static float[] BuildAuthority()
        {
            var a = new float[UrbanArchetypeCatalog.CategoryCount];
            for (int i = 0; i < a.Length; i++) a[i] = 1f;
            a[(int)BuildingCategory.Housing] = 0.8f;
            a[(int)BuildingCategory.Food] = 0.6f;
            a[(int)BuildingCategory.Resource] = 0.6f;
            a[(int)BuildingCategory.Industry] = 0.8f;
            a[(int)BuildingCategory.Storage] = 0.9f;
            a[(int)BuildingCategory.Military] = 0.7f;
            a[(int)BuildingCategory.Wonder] = 0f;
            return a;
        }
    }
}
