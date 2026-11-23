using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The Prospector: "know the land". Every two years (every half year while a deposit runs low) it surveys each
    /// settlement's territory with <see cref="ProspectorSurveyor"/> and then advises the council:
    /// <list type="bullet">
    /// <item>Rich, untapped deposits get an extractor vote (the best available building whose Placement carries the
    /// matching Near* rule and that yields the resource), scaled by richness, by how much the settlement actually
    /// needs the resource (deficit, current-age building costs, starving consumers, glut) and by personality
    /// (industriousness, racial production affinity, mercantilism for gold).</item>
    /// <item>Exploited deposits under 10% of their peak (or below an absolute floor) get a NEGATIVE vote on more of
    /// that extractor plus a positive vote for an alternative: a sibling deposit with the same yield (coal vs oil),
    /// a deposit-free producer, relocating the work to a sister settlement with a rich untapped deposit, or trade.</item>
    /// <item>Deposits forecast to run out within a few years (smoothed depletion rate) get early, softer versions of
    /// the same advice: anticipation instead of a late reaction.</item>
    /// <item>Elves practice forest stewardship: when trees are scarce they vote against woodcutting and its staffing.</item>
    /// <item>Coal only matters from the Industrial age and oil from the Modern age.</item>
    /// </list>
    /// Votes are soft influence through <see cref="AdvisorBoard"/>; the advisor never touches the build queue (it only
    /// reads it, to avoid voting for what is already ordered). Deterministic (no randomness, index iteration) and
    /// allocation-free per run except for the short reason strings attached to strong votes.
    /// </summary>
    public sealed class ProspectorAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in memory keys and logs.</summary>
        public const string Id = "prospect";

        /// <summary>An exploited deposit below this fraction of its peak is nearly exhausted.</summary>
        public const float ExhaustedFraction = 0.10f;
        /// <summary>An exploited deposit below this fraction of its peak is running low.</summary>
        public const float DepletingFraction = 0.25f;
        /// <summary>Forecast horizon: an exploited deposit with fewer years left than this is running low.</summary>
        public const float ForecastYears = 6f;
        /// <summary>Elves consider their forest scarce below this much standing wood...</summary>
        public const float StewardAmount = 900f;
        /// <summary>...or when trees cover less than this share of their land.</summary>
        public const float StewardCoverage = 0.15f;

        private const float ExhaustedVote = -0.8f;
        private const float DepletingVote = -0.35f;
        private const float StrongVote = 0.45f;

        private const byte StNone = 0, StUntapped = 1, StHealthy = 2, StDepleting = 3, StExhausted = 4, StScarce = 5;
        private const byte WhyDepleting = 0, WhyExhausted = 1, WhySteward = 2;

        public override string AdvisorId => Id;
        public override int TickInterval => 48;

        private readonly ProspectorSurveyor _surveyor = new ProspectorSurveyor();

        // ---- catalog derived from BuildingDefinition data (rebuilt when the database changes)
        private GameDatabase _catalogDb;
        private readonly List<BuildingType>[] _extractors = NewLists(ProspectorKinds.Count);
        private readonly List<BuildingType>[] _producers = NewLists(Counts.Resources);
        private readonly List<BuildingType> _commerce = new List<BuildingType>();
        private readonly List<BuildingType> _fertileFood = new List<BuildingType>();
        private readonly List<BuildingType> _waterFood = new List<BuildingType>();
        private readonly int[] _kindMask = new int[Counts.Buildings];
        private readonly byte[] _foodSite = new byte[Counts.Buildings]; // 1 = fertile-land food, 2 = water food

        // ---- per-settlement scratch (reused)
        private readonly int[] _typeCount = new int[Counts.Buildings];
        private readonly int[] _ext = new int[ProspectorKinds.Count];
        private readonly byte[] _status = new byte[ProspectorKinds.Count];
        private readonly bool[] _consumes = new bool[Counts.Resources];
        private readonly bool[] _produces = new bool[Counts.Resources];
        private readonly float[] _need = new float[Counts.Resources];
        private readonly float[] _costShare = new float[Counts.Resources];
        private readonly int[] _costHits = new int[Counts.Resources];
        private bool _hasFertileFood, _hasWaterFood;
        private int _shareCivId = -1;
        private AgeId _shareAge;
        private long _shareTick = -1;

        // ------------------------------------------------------------------ public read API (UI, other advisors)
        /// <summary>Remaining amount of a deposit kind in a settlement's territory at the last survey.</summary>
        public static float Remaining(Settlement s, ProspectorDeposit kind) => s == null ? 0f : s.Mem(ProspectorKinds.AmountKey[(int)kind]);

        /// <summary>Peak remaining amount seen for the current territory (baseline for exhaustion).</summary>
        public static float Peak(Settlement s, ProspectorDeposit kind) => s == null ? 0f : s.Mem(ProspectorKinds.PeakKey[(int)kind]);

        /// <summary>Forecast years until the deposit runs out at the observed depletion rate (float.MaxValue if stable).</summary>
        public static float YearsLeft(Settlement s, ProspectorDeposit kind)
        {
            if (s == null) return 0f;
            float rate = s.Mem(ProspectorKinds.RateKey[(int)kind]);
            return rate > 1e-3f ? s.Mem(ProspectorKinds.AmountKey[(int)kind]) / rate : float.MaxValue;
        }

        // ------------------------------------------------------------------ advising
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            var map = ctx.State.Map;
            if (db == null || map == null || !s.Center.IsValid) return;
            EnsureCatalog(db);
            if (_surveyor.NeedsRefresh(s, ctx.Tick)) _surveyor.Run(map, s, ctx.Tick);

            Inventory(ctx, db, s);
            ComputeCostShare(db, civ, ctx.Tick);
            float merc = AdvisorBoard.Trait(civ, db, AITrait.Mercantilism);
            ComputeNeeds(s, merc);
            float ind = AdvisorBoard.Trait(civ, db, AITrait.Industriousness);
            RaceDefinition race = (int)civ.Race < db.Races.Count ? db.Races[(int)civ.Race] : null;
            bool steward = civ.Race == RaceId.Elf;

            for (int k = 0; k < ProspectorKinds.Count; k++) _status[k] = Classify(s, k, civ.Age, steward);

            float urgency = 0f;
            bool watch = false;
            for (int k = 0; k < ProspectorKinds.Count; k++)
            {
                byte st = _status[k];
                if (st == StNone) continue;
                float need = _need[(int)ProspectorKinds.Yield[k]];
                float amt = s.Mem(ProspectorKinds.AmountKey[k]);
                switch (st)
                {
                    case StUntapped:
                    {
                        float rich = ProspectorKinds.Richness(k, amt);
                        if (rich < 0.2f) break;
                        int best = BestOf(_extractors[k], (int)ProspectorKinds.Yield[k], civ, s, db, true);
                        if (best < 0) break;
                        float str = rich * (0.35f + 0.65f * need) * Personality(k, race, ind, merc);
                        if (steward && k == ProspectorKinds.Forest) str = Math.Min(str, 0.6f) * 0.8f; // elves cut sparingly
                        VoteBuild(s, civ, (BuildingType)best, str, str >= StrongVote ? UntappedReason(k, amt, (BuildingType)best) : null);
                        urgency = Math.Max(urgency, rich * need * 0.75f);
                        break;
                    }
                    case StHealthy:
                    {
                        JobType job = PresentJob(db, k);
                        if (need >= 0.5f) VoteJob(s, civ, job, 0.25f * need);
                        if (need >= 0.7f && !(steward && k == ProspectorKinds.Forest) && amt / Math.Max(1, _ext[k]) >= ProspectorKinds.RichAmount[k] * 1.5f)
                        {
                            int best = BestOf(_extractors[k], (int)ProspectorKinds.Yield[k], civ, s, db, true);
                            if (best >= 0) VoteBuild(s, civ, (BuildingType)best, 0.3f * ProspectorKinds.Richness(k, amt) * Personality(k, race, ind, merc));
                            urgency = Math.Max(urgency, 0.3f * need);
                        }
                        break;
                    }
                    case StDepleting:
                        watch = true;
                        VoteAgainstExtractors(civ, s, db, k, DepletingVote, WhyDepleting);
                        if (need >= 0.4f) Alternatives(ctx, civ, s, db, k, 0.25f + 0.25f * need, WhyDepleting);
                        urgency = Math.Max(urgency, 0.3f + 0.35f * need);
                        break;
                    case StExhausted:
                        watch = true;
                        VoteAgainstExtractors(civ, s, db, k, ExhaustedVote, WhyExhausted);
                        JobType exJob = PresentJob(db, k);
                        if (exJob != JobType.None && !JobSharedByHealthy(db, k, exJob)) VoteJob(s, civ, exJob, -0.3f);
                        Alternatives(ctx, civ, s, db, k, 0.5f + 0.4f * need, WhyExhausted);
                        urgency = Math.Max(urgency, 0.5f + 0.45f * need);
                        break;
                    case StScarce:
                    {
                        float sev = StewardSeverity(s);
                        VoteAgainstExtractors(civ, s, db, k, -(0.5f + 0.4f * sev), WhySteward);
                        JobType wj = PresentJob(db, k);
                        if (wj != JobType.None) VoteJob(s, civ, wj, -0.3f * sev);
                        if (need >= 0.5f) Alternatives(ctx, civ, s, db, k, 0.4f, WhySteward);
                        urgency = Math.Max(urgency, 0.3f + 0.4f * sev);
                        break;
                    }
                }
            }

            AdviseLandFit(civ, s, db);

            if (watch) s.Memory[ProspectorKinds.KeyWatch] = 1f; else s.Memory.Remove(ProspectorKinds.KeyWatch);
            Urgency(s, urgency);
        }

        // ------------------------------------------------------------------ classification
        private byte Classify(Settlement s, int k, AgeId age, bool steward)
        {
            if (!ProspectorKinds.IsRelevant(k, age)) return StNone;
            float amt = s.Mem(ProspectorKinds.AmountKey[k]);
            float peak = Math.Max(amt, s.Mem(ProspectorKinds.PeakKey[k]));
            float low = ProspectorKinds.LowAmount[k];
            int ext = _ext[k];
            int tiles = (int)s.Mem(ProspectorKinds.TilesKey[k]);
            if (steward && k == ProspectorKinds.Forest && (tiles > 0 || ext > 0) && IsForestScarce(s)) return StScarce;
            if (ext > 0)
            {
                bool hasBaseline = peak >= 2f * low;
                if (amt < low || (hasBaseline && amt < peak * ExhaustedFraction)) return StExhausted;
                float rate = s.Mem(ProspectorKinds.RateKey[k]);
                float years = rate > 1e-3f ? amt / rate : float.MaxValue;
                if ((hasBaseline && amt < peak * DepletingFraction) || years < ForecastYears) return StDepleting;
                return StHealthy;
            }
            return amt >= 2f * low && tiles > 0 ? StUntapped : StNone;
        }

        private static bool IsForestScarce(Settlement s)
        {
            float amt = s.Mem(ProspectorKinds.AmountKey[ProspectorKinds.Forest]);
            float tiles = s.Mem(ProspectorKinds.TilesKey[ProspectorKinds.Forest]);
            float land = Math.Max(1f, s.Mem(ProspectorKinds.KeyLand));
            return amt < StewardAmount || tiles / land < StewardCoverage;
        }

        private static float StewardSeverity(Settlement s)
        {
            float amt = s.Mem(ProspectorKinds.AmountKey[ProspectorKinds.Forest]);
            float tiles = s.Mem(ProspectorKinds.TilesKey[ProspectorKinds.Forest]);
            float land = Math.Max(1f, s.Mem(ProspectorKinds.KeyLand));
            float byAmount = 1f - amt / StewardAmount;
            float byCover = 1f - (tiles / land) / StewardCoverage;
            return MathUtil.Clamp(Math.Max(byAmount, byCover), 0.3f, 1f);
        }

        // ------------------------------------------------------------------ actions
        private void VoteAgainstExtractors(Civilization civ, Settlement s, GameDatabase db, int k, float strength, byte why)
        {
            var list = _extractors[k];
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (!db.IsBuildingAvailable(civ, t)) continue;
                string reason = null;
                if (-strength >= StrongVote)
                {
                    if (why == WhySteward)
                    {
                        reason = "Forest stewardship: only " + ProspectorKinds.Thousands(s.Mem(ProspectorKinds.AmountKey[k])) + " wood in " +
                                 ((int)s.Mem(ProspectorKinds.TilesKey[k])).ToString() + " groves, no more " + t;
                    }
                    else
                    {
                        float amt = s.Mem(ProspectorKinds.AmountKey[k]);
                        float peak = Math.Max(amt, s.Mem(ProspectorKinds.PeakKey[k]));
                        reason = amt <= 0f
                            ? ProspectorKinds.Title[k] + " exhausted: no more " + t
                            : ProspectorKinds.Title[k] + " nearly exhausted (" + ProspectorKinds.Thousands(amt) + " left, " + ProspectorKinds.Percent(peak > 0f ? amt / peak : 0f) + "): no more " + t;
                    }
                }
                VoteBuild(s, civ, t, strength, reason);
            }
        }

        /// <summary>Positive votes for a way to keep the resource flowing once deposit k fails.</summary>
        private void Alternatives(SimContext ctx, Civilization civ, Settlement s, GameDatabase db, int k, float strength, byte why)
        {
            var res = ProspectorKinds.Yield[k];
            bool found = false;

            // (a) a sibling deposit with the same yield (coal <-> oil)
            int bestK = -1;
            float bestAmt = 0f;
            for (int k2 = 0; k2 < ProspectorKinds.Count; k2++)
            {
                if (k2 == k || ProspectorKinds.Yield[k2] != res) continue;
                byte st = _status[k2];
                if (st != StUntapped && st != StHealthy) continue;
                float a2 = s.Mem(ProspectorKinds.AmountKey[k2]);
                if (a2 > bestAmt) { bestAmt = a2; bestK = k2; }
            }
            if (bestK >= 0)
            {
                int b = BestOf(_extractors[bestK], (int)res, civ, s, db, true);
                if (b >= 0)
                {
                    float str = strength * (0.5f + 0.5f * ProspectorKinds.Richness(bestK, bestAmt)) * (_ext[bestK] > 0 ? 0.5f : 1f);
                    VoteBuild(s, civ, (BuildingType)b, str, str >= StrongVote
                        ? WhyText(k, why) + ": " + (BuildingType)b + " on " + ProspectorKinds.Label[bestK] + " (" + ProspectorKinds.Thousands(bestAmt) + ") instead"
                        : null);
                    found = true;
                }
            }

            // (b) a producer of the same resource that needs no deposit at all
            int p = BestOf(_producers[(int)res], (int)res, civ, s, db, true);
            if (p >= 0)
            {
                float str = strength * 0.8f;
                VoteBuild(s, civ, (BuildingType)p, str, str >= StrongVote ? WhyText(k, why) + ": " + (BuildingType)p + " makes " + res + " without a deposit" : null);
                found = true;
            }

            // (c) move the work to a sister settlement sitting on a rich, untapped deposit
            if (RelocateToSister(ctx, civ, s, db, k, strength * 0.8f)) found = true;

            // (d) nothing on our land: trade for it
            if (!found)
            {
                int m = BestOf(_commerce, -1, civ, s, db, true);
                if (m >= 0 && _typeCount[m] == 0)
                {
                    float str = strength * 0.6f;
                    VoteBuild(s, civ, (BuildingType)m, str, str >= StrongVote ? WhyText(k, why) + ": import " + res + " via " + (BuildingType)m : null);
                }
            }
        }

        private bool RelocateToSister(SimContext ctx, Civilization civ, Settlement s, GameDatabase db, int k, float strength)
        {
            var ids = civ.SettlementIds;
            Settlement target = null;
            float bestAmt = ProspectorKinds.RichAmount[k] * 0.5f;
            bool steward = civ.Race == RaceId.Elf && k == ProspectorKinds.Forest;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == s.Id) continue;
                var o = ctx.State.Settlements.Get(ids[i]);
                if (o == null || !o.IsAlive || !o.Center.IsValid) continue;
                if (!ProspectorSurveyor.HasSurvey(o)) _surveyor.Run(ctx.State.Map, o, ctx.Tick);
                float a = o.Mem(ProspectorKinds.AmountKey[k]);
                if (a <= bestAmt) continue;
                if (steward && IsForestScarce(o)) continue;
                if (CountKind(ctx, o, k) > 0) continue;
                target = o;
                bestAmt = a;
            }
            if (target == null) return false;
            int b = BestOf(_extractors[k], (int)ProspectorKinds.Yield[k], civ, target, db, false);
            if (b < 0) return false;
            VoteBuild(target, civ, (BuildingType)b, strength, strength >= StrongVote
                ? "Relocate " + ProspectorKinds.Yield[k] + " work here: rich " + ProspectorKinds.Label[k] + " (" + ProspectorKinds.Thousands(bestAmt) + ") untapped; " + s.Name + "'s is failing: " + (BuildingType)b
                : null);
            return true;
        }

        /// <summary>Light "know the land" food advice: untapped fertile land or water while food runs short.</summary>
        private void AdviseLandFit(Civilization civ, Settlement s, GameDatabase db)
        {
            if (_need[(int)ResourceType.Food] < 0.75f) return;
            float fertile = s.Mem(ProspectorKinds.KeyFertile);
            if (!_hasFertileFood && fertile >= 6f)
            {
                int b = BestOf(_fertileFood, (int)ResourceType.Food, civ, s, db, true);
                if (b >= 0) VoteBuild(s, civ, (BuildingType)b, 0.3f * MathUtil.Clamp01(fertile / 24f));
            }
            float water = s.Mem(ProspectorKinds.KeyWater) + s.Mem(ProspectorKinds.KeyCoast);
            if (!_hasWaterFood && water >= 3f)
            {
                int b = BestOf(_waterFood, (int)ResourceType.Food, civ, s, db, true);
                if (b >= 0) VoteBuild(s, civ, (BuildingType)b, 0.25f);
            }
        }

        private static string WhyText(int k, byte why) =>
            why == WhySteward ? "Forest stewardship" : ProspectorKinds.Title[k] + (why == WhyExhausted ? " exhausted" : " running out");

        private static string UntappedReason(int k, float amt, BuildingType t) =>
            (amt >= ProspectorKinds.RichAmount[k] * 0.8f ? "Rich " : "Workable ") + ProspectorKinds.Label[k] + " (" + ProspectorKinds.Thousands(amt) + ") untapped: " + t;

        // ------------------------------------------------------------------ needs & personality
        private void ComputeNeeds(Settlement s, float mercantilism)
        {
            var sp = s.Stockpile;
            float cap = Math.Max(1f, MathUtil.Sanitize(sp.Capacity, Stockpile.DefaultCapacity));
            for (int r = 0; r < Counts.Resources; r++)
            {
                var rt = (ResourceType)r;
                float net = MathUtil.Sanitize(sp.Net(rt));
                float stock = MathUtil.Sanitize(sp.Get(rt));
                float share = _costShare[r];
                float need = 0.15f;
                if (share > 0f) need = Math.Max(need, 0.4f + 0.4f * share);
                if (net < -0.0005f) need = Math.Max(need, 0.75f + 0.25f * MathUtil.Clamp01(-net / 0.3f));
                if (_consumes[r]) need = Math.Max(need, _produces[r] ? 0.6f : 0.9f);
                if (share > 0f && stock < 0.1f * cap) need = Math.Max(need, 0.8f);
                if (rt == ResourceType.Gold) need += 0.2f * mercantilism;
                if (stock >= 0.9f * cap && net >= 0f) need *= 0.35f; // glut: no point digging more
                _need[r] = MathUtil.Clamp01(need);
            }
        }

        /// <summary>Share of the civ's current-age buildings whose cost includes each resource (cached per civ per run).</summary>
        private void ComputeCostShare(GameDatabase db, Civilization civ, long tick)
        {
            if (civ.Id == _shareCivId && civ.Age == _shareAge && tick == _shareTick) return;
            _shareCivId = civ.Id;
            _shareAge = civ.Age;
            _shareTick = tick;
            Array.Clear(_costHits, 0, _costHits.Length);
            int total = 0;
            for (int i = 0; i < Counts.Buildings; i++)
            {
                var t = (BuildingType)i;
                if (!db.TryGetBuilding(t, out var def)) continue;
                bool current = def.MinAge == civ.Age || (def.MinAge > civ.Age && civ.UnlockedBuildings[i]);
                if (!current || !db.IsBuildingAvailable(civ, t)) continue;
                total++;
                var cost = def.Cost;
                if (cost == null) continue;
                for (int c = 0; c < cost.Length; c++)
                {
                    int r = (int)cost[c].Type;
                    if (cost[c].Amount > 0f && r >= 0 && r < Counts.Resources) _costHits[r]++;
                }
            }
            for (int r = 0; r < Counts.Resources; r++) _costShare[r] = total > 0 ? MathUtil.Clamp01(_costHits[r] / (float)total) : 0f;
        }

        private static float Personality(int k, RaceDefinition race, float industriousness, float mercantilism)
        {
            int r = (int)ProspectorKinds.Yield[k];
            float mult = race != null && race.ProductionMultiplier != null && r < race.ProductionMultiplier.Length ? race.ProductionMultiplier[r] : 1f;
            float pf = (0.85f + 0.3f * industriousness) * (0.8f + 0.2f * MathUtil.Clamp(MathUtil.Sanitize(mult, 1f), 0.5f, 2f));
            if (ProspectorKinds.Yield[k] == ResourceType.Gold) pf *= 0.9f + 0.2f * mercantilism;
            return MathUtil.Clamp(pf, 0.5f, 1.5f);
        }

        // ------------------------------------------------------------------ inventory
        private void Inventory(SimContext ctx, GameDatabase db, Settlement s)
        {
            Array.Clear(_typeCount, 0, _typeCount.Length);
            Array.Clear(_ext, 0, _ext.Length);
            Array.Clear(_consumes, 0, _consumes.Length);
            Array.Clear(_produces, 0, _produces.Length);
            _hasFertileFood = _hasWaterFood = false;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = ctx.State.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= Counts.Buildings) continue;
                Count(t);
                if (!db.TryGetBuilding(b.Type, out var def)) continue;
                Mark(def.InputsPerWorker, _consumes);
                Mark(def.OutputsPerWorker, _produces);
            }
            var queue = s.BuildQueue;
            for (int i = 0; i < queue.Count; i++)
            {
                var o = queue[i];
                if (o == null) continue;
                int t = (int)o.Type;
                if (t >= 0 && t < Counts.Buildings) Count(t);
            }
        }

        private void Count(int t)
        {
            _typeCount[t]++;
            int mask = _kindMask[t];
            if (mask != 0) for (int k = 0; k < ProspectorKinds.Count; k++) if ((mask & (1 << k)) != 0) _ext[k]++;
            if (_foodSite[t] == 1) _hasFertileFood = true;
            else if (_foodSite[t] == 2) _hasWaterFood = true;
        }

        private static void Mark(ResourceAmount[] flows, bool[] into)
        {
            if (flows == null) return;
            for (int i = 0; i < flows.Length; i++)
            {
                int r = (int)flows[i].Type;
                if (flows[i].Amount > 0f && r >= 0 && r < into.Length) into[r] = true;
            }
        }

        private int CountKind(SimContext ctx, Settlement o, int k)
        {
            int n = 0, bit = 1 << k;
            var ids = o.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = ctx.State.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t >= 0 && t < Counts.Buildings && (_kindMask[t] & bit) != 0) n++;
            }
            var queue = o.BuildQueue;
            for (int i = 0; i < queue.Count; i++)
            {
                var q = queue[i];
                if (q == null) continue;
                int t = (int)q.Type;
                if (t >= 0 && t < Counts.Buildings && (_kindMask[t] & bit) != 0) n++;
            }
            return n;
        }

        /// <summary>Job of the extractors of kind k present in the settlement (only the kind's canonical extraction job).</summary>
        private JobType PresentJob(GameDatabase db, int k)
        {
            var list = _extractors[k];
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (_typeCount[(int)t] == 0 || !db.TryGetBuilding(t, out var def)) continue;
                if (def.Job == ProspectorKinds.Job[k]) return def.Job;
            }
            return JobType.None;
        }

        private bool JobSharedByHealthy(GameDatabase db, int k, JobType job)
        {
            for (int k2 = 0; k2 < ProspectorKinds.Count; k2++)
                if (k2 != k && _status[k2] == StHealthy && PresentJob(db, k2) == job) return true;
            return false;
        }

        // ------------------------------------------------------------------ choosing buildings (data-driven)
        /// <summary>
        /// Best buildable entry of a candidate list for a target settlement: available to the civ, tier/population met,
        /// under MaxPerSettlement (when counts are known) and placeable given the surveyed land. Scored by throughput of
        /// the resource (or Commerce service when scoreResource &lt; 0). Returns -1 when none qualifies.
        /// </summary>
        private int BestOf(List<BuildingType> list, int scoreResource, Civilization civ, Settlement target, GameDatabase db, bool useCounts)
        {
            int best = -1;
            float bestScore = -1f;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (!db.IsBuildingAvailable(civ, t) || !db.TryGetBuilding(t, out var def)) continue;
                if (target.Tier < def.MinTier || target.Population < def.MinPopulation) continue;
                if (def.MaxPerSettlement == 0) continue;
                if (useCounts && def.MaxPerSettlement > 0 && _typeCount[(int)t] >= def.MaxPerSettlement) continue;
                if (!PlacementFeasible(target, def.Placement)) continue;
                float score = scoreResource >= 0
                    ? Amount(def.OutputsPerWorker, scoreResource) * Math.Max(1, def.JobSlots)
                    : ServiceAmount(def.Services, ServiceType.Commerce);
                score += 0.001f * (int)def.MinAge; // ties: the newer technique
                if (score > bestScore) { bestScore = score; best = (int)t; }
            }
            return best;
        }

        private static bool PlacementFeasible(Settlement s, PlacementRule rule)
        {
            if (rule == PlacementRule.None) return true;
            if ((rule & PlacementRule.NearWater) != 0 && s.Mem(ProspectorKinds.KeyWater) + s.Mem(ProspectorKinds.KeyCoast) <= 0f) return false;
            if ((rule & PlacementRule.Coastal) != 0 && s.Mem(ProspectorKinds.KeyCoast) <= 0f) return false;
            if ((rule & PlacementRule.NearRiver) != 0 && s.Mem(ProspectorKinds.KeyRiver) <= 0f) return false;
            if ((rule & PlacementRule.OnHills) != 0 && s.Mem(ProspectorKinds.KeyHills) <= 0f) return false;
            if ((rule & PlacementRule.NearMountain) != 0 && s.Mem(ProspectorKinds.KeyMountain) <= 0f) return false;
            if ((rule & PlacementRule.Fertile) != 0 && s.Mem(ProspectorKinds.KeyFertile) <= 0f) return false;
            for (int k = 0; k < ProspectorKinds.Count; k++)
                if ((rule & ProspectorKinds.NearRule[k]) != 0 && s.Mem(ProspectorKinds.TilesKey[k]) <= 0f) return false;
            return true;
        }

        private void EnsureCatalog(GameDatabase db)
        {
            if (ReferenceEquals(db, _catalogDb)) return;
            _catalogDb = db;
            for (int k = 0; k < _extractors.Length; k++) _extractors[k].Clear();
            for (int r = 0; r < _producers.Length; r++) _producers[r].Clear();
            _commerce.Clear();
            _fertileFood.Clear();
            _waterFood.Clear();
            Array.Clear(_kindMask, 0, _kindMask.Length);
            Array.Clear(_foodSite, 0, _foodSite.Length);
            for (int i = 0; i < Counts.Buildings; i++)
            {
                var t = (BuildingType)i;
                if (!db.TryGetBuilding(t, out var def) || def.IsWonder || def.UniquePerCivilization || def.IsDwelling) continue;
                var outs = def.OutputsPerWorker;
                bool noOutputs = outs == null || outs.Length == 0;
                for (int k = 0; k < ProspectorKinds.Count; k++)
                {
                    if ((def.Placement & ProspectorKinds.NearRule[k]) == 0) continue;
                    int res = (int)ProspectorKinds.Yield[k];
                    if (Amount(outs, res) > 0f || (noOutputs && def.Job == ProspectorKinds.Job[k]))
                    {
                        _extractors[k].Add(t);
                        _kindMask[i] |= 1 << k;
                    }
                }
                if ((def.Placement & ProspectorKinds.AnyNearDeposit) == 0 && outs != null)
                {
                    for (int o = 0; o < outs.Length; o++)
                    {
                        int r = (int)outs[o].Type;
                        if (outs[o].Amount <= 0f || r < 0 || r >= Counts.Resources) continue;
                        if (Amount(def.InputsPerWorker, r) > 0f || _producers[r].Contains(t)) continue;
                        _producers[r].Add(t);
                    }
                }
                if (ServiceAmount(def.Services, ServiceType.Commerce) > 0f) _commerce.Add(t);
                if (Amount(outs, (int)ResourceType.Food) > 0f)
                {
                    if ((def.Placement & PlacementRule.Fertile) != 0) { _fertileFood.Add(t); _foodSite[i] = 1; }
                    else if ((def.Placement & (PlacementRule.NearWater | PlacementRule.Coastal)) != 0) { _waterFood.Add(t); _foodSite[i] = 2; }
                }
            }
        }

        private static float Amount(ResourceAmount[] flows, int resource)
        {
            if (flows == null) return 0f;
            float a = 0f;
            for (int i = 0; i < flows.Length; i++) if ((int)flows[i].Type == resource) a += MathUtil.Sanitize(flows[i].Amount);
            return a;
        }

        private static float ServiceAmount(ServiceAmount[] services, ServiceType type)
        {
            if (services == null) return 0f;
            float a = 0f;
            for (int i = 0; i < services.Length; i++) if (services[i].Type == type) a += MathUtil.Sanitize(services[i].Amount);
            return a;
        }

        private static List<BuildingType>[] NewLists(int n)
        {
            var a = new List<BuildingType>[n];
            for (int i = 0; i < n; i++) a[i] = new List<BuildingType>();
            return a;
        }
    }
}
