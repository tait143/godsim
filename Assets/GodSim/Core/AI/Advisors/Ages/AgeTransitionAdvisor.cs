using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Age Transition advisor ("ages"): makes civilizations visibly modernize through the ten ages.
    ///
    /// Three overlapping plans, all read from <see cref="BuildingDefinition"/> data via <see cref="AgesBuildingIndex"/>:
    /// <list type="number">
    /// <item><b>Adoption wave</b> - for ~20 years after an <see cref="AgeAdvancedEvent"/>, vote the signature buildings
    /// that first become available in the new age (MinAge == civ.Age). The capital pilots them at full strength;
    /// other settlements follow by size, more strongly once the capital has shown the way. Scaled by the civ's
    /// Curiosity/Industriousness (plus Piety/Aggression/Mercantilism for matching categories) and fading over the wave.</item>
    /// <item><b>Replacement planning</b> - for buildings whose MaxAge is behind the civ (obsolete), vote their best
    /// available functional equivalent (shared outputs/services/category) in proportion to how much of that function
    /// still rests on obsolete buildings, and vote against more obsolete types. Before a transition, the same logic
    /// runs ahead of time on "sunset" types (MaxAge == civ.Age) so production never collapses on demolition.</item>
    /// <item><b>Pre-transition stockpiling</b> - when readiness for the next age reaches ~85% (or research is fast and
    /// the ETA is short), sum the construction costs of the next age's signature buildings, project income over the
    /// expected time to transition, vote producers (and one link of their input chain) for shortfalls, and vote
    /// storage when the new era's materials will not fit.</item>
    /// </list>
    /// Soft influence only: never touches the BuildQueue. Deterministic, allocation-free while advising (reason
    /// strings are cached), and never votes for a building <see cref="GameDatabase.IsBuildingAvailable"/> rejects.
    /// </summary>
    public sealed class AgeTransitionAdvisor : AdvisorSystemBase
    {
        public const string Id = "ages";

        public override string AdvisorId => Id;
        public override int TickInterval => 24;

        private readonly Action<AgeAdvancedEvent> _onAgeAdvanced;
        private SimContext _ctx;
        private EventBus _subscribedBus;
        private AgesBuildingIndex _index;
        private readonly AgesSiteSurvey _sites = new AgesSiteSurvey();
        private readonly Dictionary<int, AgesCivSnapshot> _civ = new Dictionary<int, AgesCivSnapshot>();

        // Reusable per-settlement scratch (no per-tick allocations).
        private readonly int[] _counts = new int[Counts.Buildings];
        private readonly bool[] _queued = new bool[Counts.Buildings];
        private readonly List<int> _present = new List<int>(32);
        private readonly float[] _need = new float[Counts.Resources];
        private readonly float[] _shortfall = new float[Counts.Resources];

        public AgeTransitionAdvisor() { _onAgeAdvanced = OnAgeAdvanced; }

        /// <summary>The catalog index in use (built lazily; exposed for UI/tests).</summary>
        public AgesBuildingIndex Index => _index;

        public override void Initialize(SimContext ctx)
        {
            _ctx = ctx;
            if (ctx == null) return;
            if (_subscribedBus != null) _subscribedBus.Unsubscribe(_onAgeAdvanced);
            _subscribedBus = ctx.Events;
            if (_subscribedBus != null) _subscribedBus.Subscribe(_onAgeAdvanced);
            if (ctx.Db != null) EnsureIndex(ctx.Db);
        }

        private void OnAgeAdvanced(AgeAdvancedEvent e)
        {
            var ctx = _ctx;
            if (ctx == null || ctx.State == null) return;
            var civ = ctx.State.Civilizations.Get(e.CivilizationId);
            if (civ == null) return;
            civ.Memory[AgesKeys.EnteredTick] = (float)ctx.State.Tick;
            civ.Memory[AgesKeys.EnteredAge] = (int)e.NewAge;
        }

        private AgesBuildingIndex EnsureIndex(GameDatabase db)
        {
            if (_index == null || !ReferenceEquals(_index.Db, db)) _index = AgesBuildingIndex.Build(db);
            return _index;
        }

        // ================================================================== advising
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            if (db == null || ctx.State == null) return;
            if (_ctx == null) _ctx = ctx;
            var ix = EnsureIndex(db);
            var cc = Snapshot(ctx, civ, db);
            int mask = _sites.Get(ctx, s);
            CountBuildings(ctx, s);
            int rank = Rank(ctx, civ, s, cc);

            float waveU = AdoptionWave(ctx, civ, s, ix, cc, mask, rank);
            float replaceU = PlanReplacements(ctx, civ, s, ix, cc, mask);
            float prepU = PrepareTransition(ctx, civ, s, ix, cc, mask, rank);

            float u = Math.Max(waveU, Math.Max(replaceU, prepU));
            Urgency(s, u);
            AgesPhase phase = AgesPhase.Idle;
            if (u > 0.05f) phase = waveU >= replaceU && waveU >= prepU ? AgesPhase.Adoption : (prepU >= replaceU ? AgesPhase.Preparing : AgesPhase.Modernizing);
            s.Memory[AgesKeys.Phase] = (int)phase;
        }

        // ------------------------------------------------------------------ 1. adoption wave
        private float AdoptionWave(SimContext ctx, Civilization civ, Settlement s, AgesBuildingIndex ix, AgesCivSnapshot cc, int mask, int rank)
        {
            if (cc.Wave <= 0f) { s.Memory.Remove(AgesKeys.Adoption); return 0f; }
            var db = ctx.Db;
            var sigs = ix.Signatures(civ.Age);
            bool isLead = rank == 0;
            float rankFactor = isLead ? 1f : Math.Max(0.3f, 0.8f / (1f + 0.25f * (rank - 1)));
            int eligible = 0, adopted = 0, order = 0;
            for (int i = 0; i < sigs.Count; i++)
            {
                var t = sigs[i];
                if (!db.TryGetBuilding(t, out var d) || !db.IsBuildingAvailable(civ, t)) continue;
                if (!AgesSiteSurvey.Feasible(d, mask)) continue;
                if (d.MinTier > s.Tier || s.Population < d.MinPopulation) continue;
                eligible++;
                int ti = (int)t;
                if (_counts[ti] > 0 || _queued[ti]) { adopted++; continue; }
                bool piloted = !isLead && cc.LeadCounts[ti] > 0;
                float diffusion = isLead ? 1f : (piloted ? 1.05f : 0.55f);
                float orderFactor = order == 0 ? 1f : (order == 1 ? 0.8f : (order == 2 ? 0.65f : 0.5f));
                order++;
                float v = AgesTuning.WaveBase * cc.Wave * rankFactor * diffusion * orderFactor * TraitFactor(cc, d.Category);
                string reason = isLead ? ix.DawnReason(t) : (piloted ? ix.SpreadReason(t) : ix.LocalReason(t));
                VoteBuild(s, civ, t, MathUtil.Clamp(v, 0f, 1f), reason);
            }

            // Wonders of the new age: the capital (or leading city) dreams of them, gently.
            if (isLead)
            {
                var wonders = ix.Wonders(civ.Age);
                for (int i = 0; i < wonders.Count; i++)
                {
                    var t = wonders[i];
                    if (!db.TryGetBuilding(t, out var d) || !db.IsBuildingAvailable(civ, t)) continue;
                    if (!AgesSiteSurvey.Feasible(d, mask) || d.MinTier > s.Tier || s.Population < d.MinPopulation) continue;
                    if (_queued[(int)t] || CivOwns(ctx, civ, t)) continue;
                    VoteBuild(s, civ, t, 0.35f * cc.Wave * TraitFactor(cc, d.Category));
                }
            }

            int missing = eligible - adopted;
            if (missing > 0) VoteJob(s, civ, JobType.Builder, 0.3f * cc.Wave * rankFactor);
            float adoption = eligible > 0 ? adopted / (float)eligible : 1f;
            s.Memory[AgesKeys.Adoption] = adoption;
            return eligible > 0 ? MathUtil.Clamp01(cc.Wave * (1f - adoption) * (isLead ? 0.85f : 0.6f)) : 0f;
        }

        // ------------------------------------------------------------------ 2. replacement planning
        private float PlanReplacements(SimContext ctx, Civilization civ, Settlement s, AgesBuildingIndex ix, AgesCivSnapshot cc, int mask)
        {
            var db = ctx.Db;
            float worst = 0f;

            // Obsolete buildings still standing: secure their function with a modern equivalent.
            for (int p = 0; p < _present.Count; p++)
            {
                int ti = _present[p];
                var t = (BuildingType)ti;
                if (!db.TryGetBuilding(t, out var od) || od.MaxAge >= civ.Age || AgesBuildingIndex.IsCore(od)) continue;
                VoteBuild(s, civ, t, -AgesTuning.ObsoleteNegative, ix.ObsoleteReason(t));
                float risk = ObsoleteShare(db, civ.Age, od, false);
                int e = BestEquivalent(db, civ, s, ix, t, mask, civ.Age);
                if (e >= 0 && risk > 0.02f)
                {
                    float v = (0.3f + 0.7f * risk) * (0.85f + 0.3f * cc.Industriousness);
                    VoteBuild(s, civ, (BuildingType)e, MathUtil.Clamp(v, 0f, 1f), ix.ReplaceReason(t, (BuildingType)e, false));
                    if (db.TryGetBuilding((BuildingType)e, out var ed) && ed.Job != JobType.None && _counts[e] > 0) VoteJob(s, civ, ed.Job, 0.3f * risk);
                }
                if (risk > worst) worst = risk;
            }

            // Sunset types (obsolete after the next transition): stop investing, prepare the successor.
            if (cc.Prep > 0f && civ.Age < AgeId.Ascension)
            {
                var sunset = ix.Sunset(civ.Age);
                for (int i = 0; i < sunset.Count; i++)
                {
                    var t = sunset[i];
                    if (!db.TryGetBuilding(t, out var od) || AgesBuildingIndex.IsCore(od) || !db.IsBuildingAvailable(civ, t)) continue;
                    int e = BestEquivalent(db, civ, s, ix, t, mask, (AgeId)((int)civ.Age + 1));
                    if (e < 0) continue; // no successor yet: keep building what works
                    VoteBuild(s, civ, t, -AgesTuning.SunsetNegative * cc.Prep, ix.SunsetReason(t));
                    if (_counts[(int)t] <= 0) continue;
                    float risk = ObsoleteShare(db, civ.Age, od, true);
                    if (risk <= 0.02f) continue;
                    float v = (0.2f + 0.55f * risk) * cc.Prep;
                    VoteBuild(s, civ, (BuildingType)e, MathUtil.Clamp(v, 0f, 1f), ix.ReplaceReason(t, (BuildingType)e, true));
                    float u = risk * cc.Prep * 0.7f;
                    if (u > worst) worst = u;
                }
            }
            return MathUtil.Clamp01(worst * 0.9f);
        }

        /// <summary>
        /// Share (0..1) of a function (outputs/services of <paramref name="od"/>) provided by buildings that are obsolete
        /// now (or, for <paramref name="sunset"/>, after the next transition) in the counted settlement.
        /// </summary>
        private float ObsoleteShare(GameDatabase db, AgeId age, BuildingDefinition od, bool sunset)
        {
            float old = 0f, modern = 0f;
            for (int q = 0; q < _present.Count; q++)
            {
                int ti = _present[q];
                if (!db.TryGetBuilding((BuildingType)ti, out var d) || AgesBuildingIndex.IsCore(d)) continue;
                float v = _counts[ti] * AgesBuildingIndex.FunctionalValue(d, od);
                if (v <= 0f) continue;
                bool retiring = sunset ? d.MaxAge <= age : d.MaxAge < age;
                if (retiring) old += v; else modern += v;
            }
            float total = old + modern;
            return total > 0f ? MathUtil.Clamp01(MathUtil.Sanitize(old / total)) : 0f;
        }

        /// <summary>First equivalent that is available, survives <paramref name="mustSurvive"/>, fits the site and the settlement.</summary>
        private int BestEquivalent(GameDatabase db, Civilization civ, Settlement s, AgesBuildingIndex ix, BuildingType t, int mask, AgeId mustSurvive)
        {
            var eq = ix.Equivalents(t);
            for (int i = 0; i < eq.Count; i++)
            {
                var c = eq[i];
                if (!db.TryGetBuilding(c, out var d) || d.MaxAge < mustSurvive) continue;
                if (CanAdopt(db, civ, s, c, d, mask)) return (int)c;
            }
            return -1;
        }

        // ------------------------------------------------------------------ 3. pre-transition stockpiling
        private float PrepareTransition(SimContext ctx, Civilization civ, Settlement s, AgesBuildingIndex ix, AgesCivSnapshot cc, int mask, int rank)
        {
            if (cc.Prep <= 0f || civ.Age >= AgeId.Ascension) return 0f;
            var db = ctx.Db;
            var next = (AgeId)((int)civ.Age + 1);
            Array.Clear(_need, 0, _need.Length);
            var sigs = ix.Signatures(next);
            int want = rank == 0 ? 4 : (rank <= 2 ? 3 : 2);
            int taken = 0;
            for (int i = 0; i < sigs.Count && taken < want; i++)
            {
                if (!db.TryGetBuilding(sigs[i], out var d) || !d.AllowedRaces.Allows(civ.Race)) continue;
                if ((int)d.MinTier > (int)s.Tier + 1 || !AgesSiteSurvey.Feasible(d, mask)) continue;
                if (d.Cost != null)
                    for (int k = 0; k < d.Cost.Length; k++)
                    {
                        int r = (int)d.Cost[k].Type;
                        if (r >= 0 && r < _need.Length) _need[r] += Math.Max(0f, MathUtil.Sanitize(d.Cost[k].Amount));
                    }
                taken++;
            }
            if (taken == 0) return 0f;

            var stock = s.Stockpile;
            float cap = Math.Max(1f, MathUtil.Sanitize(stock.Capacity, Stockpile.DefaultCapacity));
            float horizon = Math.Min(cc.EtaTicks, AgesTuning.MaxHorizonTicks);
            float pressure = 0f, worst = 0f;
            int best = -1, second = -1;
            for (int r = 0; r < _need.Length; r++)
            {
                _shortfall[r] = 0f;
                float need = _need[r];
                if (need <= 0f) continue;
                float have = stock.Get((ResourceType)r);
                float projected = have + Math.Max(0f, MathUtil.Sanitize(stock.Net((ResourceType)r))) * horizon;
                float sf = MathUtil.Clamp01(MathUtil.Sanitize((need - projected) / need));
                _shortfall[r] = sf;
                float pr = (have + need) / cap;
                if (pr > pressure) pressure = pr;
                if (sf > worst) worst = sf;
                if (sf <= 0f) continue;
                if (best < 0 || sf > _shortfall[best]) { second = best; best = r; }
                else if (second < 0 || sf > _shortfall[second]) second = r;
            }

            // Storage: will the new era's materials fit next to what we already hold?
            if (pressure > AgesTuning.StoragePressureStart)
            {
                int st = FirstStorage(db, civ, s, ix, mask);
                if (st >= 0)
                {
                    float v = (0.35f + 0.6f * MathUtil.InverseLerp(AgesTuning.StoragePressureStart, 1.4f, pressure)) * cc.Prep;
                    VoteBuild(s, civ, (BuildingType)st, MathUtil.Clamp(v, 0f, 1f), ix.StorageReason(next));
                }
            }

            if (best >= 0) StockpileResource(db, civ, s, ix, cc, mask, next, (ResourceType)best, _shortfall[best], 1f);
            if (second >= 0) StockpileResource(db, civ, s, ix, cc, mask, next, (ResourceType)second, _shortfall[second], 0.75f);

            float storageU = pressure > 1f ? MathUtil.Clamp01((pressure - 1f) * 0.5f) : 0f;
            return MathUtil.Clamp01(cc.Prep * Math.Max(worst * 0.8f, storageU));
        }

        private void StockpileResource(GameDatabase db, Civilization civ, Settlement s, AgesBuildingIndex ix, AgesCivSnapshot cc, int mask, AgeId next, ResourceType r, float shortfall, float scale)
        {
            if (shortfall < 0.1f) return;
            int p = FirstProducer(db, civ, s, ix, r, mask);
            if (p < 0) return;
            db.TryGetBuilding((BuildingType)p, out var pd);
            bool has = _counts[p] > 0;
            float v = (0.3f + 0.6f * shortfall) * cc.Prep * scale * (0.8f + 0.4f * cc.Industriousness) * (has ? 0.6f : 1f);
            VoteBuild(s, civ, (BuildingType)p, MathUtil.Clamp(v, 0f, 1f), ix.StockReason(next, r));
            if (has && pd != null && pd.Job != JobType.None) VoteJob(s, civ, pd.Job, 0.5f * shortfall * cc.Prep * scale);

            // One link up the chain: a Smelter is useless without ore.
            if (pd == null || pd.InputsPerWorker == null) return;
            for (int i = 0; i < pd.InputsPerWorker.Length; i++)
            {
                var ir = pd.InputsPerWorker[i].Type;
                if (ir == r) continue;
                var st = s.Stockpile;
                if (st.Net(ir) > 0.01f && st.Get(ir) > 20f) continue;
                int ip = FirstProducer(db, civ, s, ix, ir, mask);
                if (ip < 0 || ip == p) continue;
                VoteBuild(s, civ, (BuildingType)ip, MathUtil.Clamp(v * 0.5f, 0f, 1f));
                if (_counts[ip] > 0 && db.TryGetBuilding((BuildingType)ip, out var idf) && idf.Job != JobType.None)
                    VoteJob(s, civ, idf.Job, 0.25f * shortfall * cc.Prep * scale);
            }
        }

        /// <summary>Best producer of a resource that is buildable here; prefers ones that survive the next transition.</summary>
        private int FirstProducer(GameDatabase db, Civilization civ, Settlement s, AgesBuildingIndex ix, ResourceType r, int mask)
        {
            var list = ix.Producers(r);
            for (int pass = 0; pass < 2; pass++)
                for (int i = 0; i < list.Count; i++)
                {
                    var t = list[i];
                    if (!db.TryGetBuilding(t, out var d)) continue;
                    if (pass == 0 && d.MaxAge <= civ.Age && civ.Age < AgeId.Ascension) continue;
                    if (CanAdopt(db, civ, s, t, d, mask)) return (int)t;
                }
            return -1;
        }

        private int FirstStorage(GameDatabase db, Civilization civ, Settlement s, AgesBuildingIndex ix, int mask)
        {
            var list = ix.Storage;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (db.TryGetBuilding(t, out var d) && CanAdopt(db, civ, s, t, d, mask)) return (int)t;
            }
            return -1;
        }

        // ------------------------------------------------------------------ shared helpers
        /// <summary>Available to the civ, placeable here, allowed by tier/population, and under its per-settlement cap.</summary>
        private bool CanAdopt(GameDatabase db, Civilization civ, Settlement s, BuildingType t, BuildingDefinition d, int mask)
        {
            if (d == null || !db.IsBuildingAvailable(civ, t)) return false;
            if (d.MinTier > s.Tier || s.Population < d.MinPopulation) return false;
            if (d.MaxPerSettlement >= 0 && _counts[(int)t] >= d.MaxPerSettlement) return false;
            return AgesSiteSurvey.Feasible(d, mask);
        }

        private static float TraitFactor(AgesCivSnapshot cc, BuildingCategory cat)
        {
            float f = 0.65f + 0.35f * cc.Curiosity + 0.35f * cc.Industriousness;
            switch (cat)
            {
                case BuildingCategory.Education: case BuildingCategory.Culture: case BuildingCategory.Health: f *= 0.8f + 0.4f * cc.Curiosity; break;
                case BuildingCategory.Industry: case BuildingCategory.Resource: case BuildingCategory.Food:
                case BuildingCategory.Storage: case BuildingCategory.Infrastructure: f *= 0.8f + 0.4f * cc.Industriousness; break;
                case BuildingCategory.Religion: f *= 0.8f + 0.4f * cc.Piety; break;
                case BuildingCategory.Military: f *= 0.8f + 0.4f * cc.Aggression; break;
                case BuildingCategory.Commerce: f *= 0.8f + 0.4f * cc.Mercantilism; break;
            }
            return MathUtil.Sanitize(f, 1f);
        }

        private void CountBuildings(SimContext ctx, Settlement s)
        {
            Array.Clear(_counts, 0, _counts.Length);
            Array.Clear(_queued, 0, _queued.Length);
            _present.Clear();
            var ids = s.BuildingIds;
            var store = ctx.State.Buildings;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = store.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int ti = (int)b.Type;
                if (ti < 0 || ti >= _counts.Length) continue;
                if (_counts[ti] == 0) _present.Add(ti);
                _counts[ti]++;
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                var o = q[i];
                if (o != null && (int)o.Type < _queued.Length) _queued[(int)o.Type] = true;
            }
        }

        private static void CountInto(SimContext ctx, Settlement s, int[] counts)
        {
            Array.Clear(counts, 0, counts.Length);
            if (s == null) return;
            var ids = s.BuildingIds;
            var store = ctx.State.Buildings;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = store.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int ti = (int)b.Type;
                if (ti >= 0 && ti < counts.Length) counts[ti]++;
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                var o = q[i];
                if (o != null && (int)o.Type < counts.Length && counts[(int)o.Type] == 0) counts[(int)o.Type] = 1;
            }
        }

        private static bool CivOwns(SimContext ctx, Civilization civ, BuildingType t)
        {
            var sids = civ.SettlementIds;
            for (int i = 0; i < sids.Count; i++)
            {
                var s = ctx.State.Settlements.Get(sids[i]);
                if (s == null || !s.IsAlive) continue;
                var ids = s.BuildingIds;
                for (int k = 0; k < ids.Count; k++)
                {
                    var b = ctx.State.Buildings.Get(ids[k]);
                    if (b != null && !b.IsDestroyed && b.Type == t) return true;
                }
            }
            return false;
        }

        /// <summary>0 = leading settlement (capital, or the largest if the capital fell), then by population.</summary>
        private static int Rank(SimContext ctx, Civilization civ, Settlement s, AgesCivSnapshot cc)
        {
            if (s.Id == cc.LeadId) return 0;
            int r = 1;
            var sids = civ.SettlementIds;
            for (int i = 0; i < sids.Count; i++)
            {
                var o = ctx.State.Settlements.Get(sids[i]);
                if (o == null || !o.IsAlive || o.Id == s.Id || o.Id == cc.LeadId) continue;
                if (o.Population > s.Population || (o.Population == s.Population && o.Id < s.Id)) r++;
            }
            return r;
        }

        // ------------------------------------------------------------------ civ-level snapshot (once per run per civ)
        private AgesCivSnapshot Snapshot(SimContext ctx, Civilization civ, GameDatabase db)
        {
            if (!_civ.TryGetValue(civ.Id, out var cc))
            {
                if (_civ.Count > 1024) _civ.Clear();
                cc = new AgesCivSnapshot();
                _civ[civ.Id] = cc;
            }
            if (cc.Stamp == ctx.Tick && cc.Valid) return cc;
            cc.Stamp = ctx.Tick;
            cc.Valid = true;

            cc.Curiosity = AdvisorBoard.Trait(civ, db, AITrait.Curiosity);
            cc.Industriousness = AdvisorBoard.Trait(civ, db, AITrait.Industriousness);
            cc.Piety = AdvisorBoard.Trait(civ, db, AITrait.Piety);
            cc.Aggression = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            cc.Mercantilism = AdvisorBoard.Trait(civ, db, AITrait.Mercantilism);

            // Leading settlement: the capital, or the most populous settlement if the capital is gone.
            var capital = ctx.State.Settlements.Get(civ.CapitalId);
            int lead = capital != null && capital.IsAlive && capital.CivilizationId == civ.Id ? capital.Id : -1;
            int population = 0, bestPop = -1;
            var sids = civ.SettlementIds;
            for (int i = 0; i < sids.Count; i++)
            {
                var o = ctx.State.Settlements.Get(sids[i]);
                if (o == null || !o.IsAlive) continue;
                population += o.Population;
                if (capital == null || !capital.IsAlive)
                    if (o.Population > bestPop || (o.Population == bestPop && o.Id < lead)) { bestPop = o.Population; lead = o.Id; }
            }
            cc.LeadId = lead;
            CountInto(ctx, ctx.State.Settlements.Get(lead), cc.LeadCounts);

            // Age entry (event-driven; falls back to civ.AgeEnteredTick after loads or late subscription).
            if (!civ.Memory.TryGetValue(AgesKeys.EnteredAge, out float ea) || (int)ea != (int)civ.Age || !civ.Memory.ContainsKey(AgesKeys.EnteredTick))
            {
                civ.Memory[AgesKeys.EnteredTick] = (float)civ.AgeEnteredTick;
                civ.Memory[AgesKeys.EnteredAge] = (int)civ.Age;
            }
            long entered = (long)MathUtil.Sanitize(civ.Mem(AgesKeys.EnteredTick), 0f);
            long elapsed = Math.Max(0L, ctx.Tick - entered);
            cc.Wave = civ.Age > AgeId.Dawn && elapsed < AgesTuning.WaveTicks
                ? 1f - (1f - AgesTuning.WaveTail) * (elapsed / (float)AgesTuning.WaveTicks)
                : 0f;

            // Research rate (EMA) and ETA to the next threshold.
            float rate = MathUtil.Sanitize(civ.Mem(AgesKeys.ResearchRate));
            float lastR = civ.Mem(AgesKeys.LastResearch, -1f);
            long lastT = (long)civ.Mem(AgesKeys.LastResearchTick, -1f);
            float total = MathUtil.Sanitize(civ.TotalResearch);
            if (lastR >= 0f && lastT >= 0 && ctx.Tick > lastT)
            {
                float inst = Math.Max(0f, (total - lastR) / (ctx.Tick - lastT));
                rate = MathUtil.Sanitize(rate + (inst - rate) * AgesTuning.RateAlpha);
            }
            if (lastT < 0 || ctx.Tick > lastT)
            {
                civ.Memory[AgesKeys.ResearchRate] = rate;
                civ.Memory[AgesKeys.LastResearch] = total;
                civ.Memory[AgesKeys.LastResearchTick] = (float)ctx.Tick;
            }

            cc.Progress = NextAgeProgress(civ, db, population, out float remaining);
            float eta = remaining <= 0f ? 0f : (rate > 1e-4f ? remaining / rate : 1e6f);
            cc.EtaTicks = MathUtil.Clamp(MathUtil.Sanitize(eta, 1e6f), 0f, 1e6f);
            float etaYears = cc.EtaTicks / SimTime.TicksPerYear;

            float prep = 0f;
            if (civ.Age < AgeId.Ascension)
            {
                if (cc.Progress >= AgesTuning.PrepStart) prep = 0.5f + 0.5f * MathUtil.InverseLerp(AgesTuning.PrepStart, 1f, cc.Progress);
                if (cc.Progress >= AgesTuning.PrepEarlyStart && remaining > 0f && etaYears < AgesTuning.PrepEtaYears)
                    prep = Math.Max(prep, 0.35f + 0.4f * (1f - etaYears / AgesTuning.PrepEtaYears));
            }
            cc.Prep = MathUtil.Clamp01(MathUtil.Sanitize(prep));

            civ.Memory[AgesKeys.Progress] = cc.Progress;
            civ.Memory[AgesKeys.EtaYears] = MathUtil.Clamp(etaYears, 0f, 9999f);
            civ.Memory[AgesKeys.Prep] = cc.Prep;
            return cc;
        }

        /// <summary>
        /// Readiness 0..1 for the next age: the minimum of research (within the current age's span), civ population and
        /// required previous-age techs. <paramref name="remainingResearch"/> is the research still missing.
        /// </summary>
        public static float NextAgeProgress(Civilization civ, GameDatabase db, int civPopulation, out float remainingResearch)
        {
            remainingResearch = 0f;
            if (civ == null || db == null || civ.Age >= AgeId.Ascension) return 0f;
            var cur = db.Ages[(int)civ.Age];
            var next = db.Ages[(int)civ.Age + 1];
            if (next == null) return 0f;
            float lo = cur != null ? MathUtil.Sanitize(cur.ResearchThreshold) : 0f;
            float hi = MathUtil.Sanitize(next.ResearchThreshold);
            float total = MathUtil.Sanitize(civ.TotalResearch);
            float research = hi <= lo + MathUtil.Epsilon ? (total >= hi ? 1f : 0f) : MathUtil.InverseLerp(lo, hi, total);
            remainingResearch = Math.Max(0f, hi - total);
            float pop = next.MinPopulation <= 0 ? 1f : MathUtil.Clamp01(civPopulation / (float)next.MinPopulation);
            float techs = 1f;
            if (next.RequiredTechsFromPreviousAge > 0)
            {
                int known = 0;
                var list = db.TechsOfAge(civ.Age);
                for (int i = 0; i < list.Count; i++) if (civ.KnownTechs.Contains(list[i])) known++;
                techs = MathUtil.Clamp01(known / (float)next.RequiredTechsFromPreviousAge);
            }
            return MathUtil.Clamp01(MathUtil.Sanitize(Math.Min(research, Math.Min(pop, techs))));
        }
    }

    /// <summary>Per-civ, per-run cache for the age advisor (recomputed each run; never persisted).</summary>
    public sealed class AgesCivSnapshot
    {
        public long Stamp = -1;
        public bool Valid;
        public float Curiosity, Industriousness, Piety, Aggression, Mercantilism;
        /// <summary>0..1 adoption-wave intensity (0 when outside the wave).</summary>
        public float Wave;
        /// <summary>0..1 next-age readiness.</summary>
        public float Progress;
        /// <summary>0..1 pre-transition preparation strength.</summary>
        public float Prep;
        /// <summary>Estimated ticks until the next research threshold (capped at 1e6).</summary>
        public float EtaTicks = 1e6f;
        /// <summary>Leading settlement id (capital, or largest if the capital fell).</summary>
        public int LeadId = -1;
        /// <summary>Building (and queued) counts of the leading settlement, by BuildingType.</summary>
        public readonly int[] LeadCounts = new int[Counts.Buildings];
    }
}
