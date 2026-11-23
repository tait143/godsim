using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Logistics advisor: keeps construction flowing so towns never stall.
    ///
    /// Per settlement, every 12 ticks:
    /// <list type="number">
    /// <item><b>Ledger</b>: material demand of placed unpaid sites (def.Cost where !CostPaid) plus queued
    /// <see cref="BuildOrder"/>s (weighted <see cref="QueuedDemandWeight"/>, so problems are anticipated before the
    /// site exists), versus stock and income (<see cref="Stockpile.Net"/>) per resource.</item>
    /// <item><b>Bottlenecks</b>: per resource, deficit and time-to-afford (deficit / net income, "never" without income);
    /// severity rises from <see cref="EtaComfortDays"/> to <see cref="EtaPanicDays"/>, boosted by blocked sites and by
    /// how close a blocked site is to being abandoned.</item>
    /// <item><b>Producers</b>: votes the best buildable producer of each bottleneck material, found purely from
    /// definition data (OutputsPerWorker, Placement vs territory, MinAge via <c>db.IsBuildingAvailable</c>, tier,
    /// population, MaxPerSettlement). It staffs idle producers first (job votes), discounts producers already under
    /// construction, and follows input chains (Tools -> Blacksmith needs Metal -> Smelter needs Ore -> Mine).</item>
    /// <item><b>Storage</b>: a single cost that can never fit in the stockpile is a hard stall and gets a strong
    /// storage vote ("Castle needs 300 stone, storage holds 150"); demand that cannot be stockpiled at once, or a full
    /// stockpile with rising income, gets a mild one.</item>
    /// <item><b>Restraint</b>: while more than <see cref="StalledSitesLimit"/> sites are stalled, votes against
    /// queuing more big projects ("3 sites waiting for stone"), hardest on those that need the bottleneck material.</item>
    /// <item><b>Builders</b>: Builder job votes proportional to paid sites (and remaining work); a negative vote when
    /// every site is waiting for materials, so idle builders go and produce them.</item>
    /// </list>
    /// Personality: Industriousness scales producer investment; Expansionism softens the restraint on big projects.
    /// Fully deterministic (no randomness, index iteration) and allocation-free except for reason strings on strong votes.
    /// </summary>
    public sealed class LogisticsAdvisor : AdvisorSystemBase
    {
        public const string Id = "logistics";

        /// <summary>Queued orders count this much of their cost as demand (they are not placed yet).</summary>
        public const float QueuedDemandWeight = 0.7f;
        /// <summary>Materials affordable within this many days need no new producers.</summary>
        public const float EtaComfortDays = 24f;
        /// <summary>Materials that take this long (or never arrive) are a full-severity bottleneck.</summary>
        public const float EtaPanicDays = 240f;
        /// <summary>Time-to-afford reported when there is no income.</summary>
        public const float NeverEtaDays = 9999f;
        /// <summary>Minimum bottleneck severity that triggers producer votes.</summary>
        public const float VoteThreshold = 0.15f;
        /// <summary>More stalled sites than this blocks new big projects.</summary>
        public const int StalledSitesLimit = 2;
        /// <summary>Total cost at which a project counts as big.</summary>
        public const float BigProjectCost = 120f;
        /// <summary>Deficits below this are noise.</summary>
        public const float MinDeficit = 0.5f;
        /// <summary>How many input-chain levels upstream producers are voted.</summary>
        public const int ChainDepth = 2;
        /// <summary>Upstream vote strength factor per chain level.</summary>
        public const float ChainFactor = 0.6f;
        /// <summary>An input with less stock than a producer needs for this many days (and no net income) is starved.</summary>
        public const float InputBufferDays = 24f;
        /// <summary>Maximum logged reasons for restraint votes per run (keeps the AI log readable).</summary>
        public const int MaxRestraintReasons = 2;
        /// <summary>Explanations of strong votes are logged at most this often per settlement.</summary>
        public const int ExplainEveryTicks = 48;
        /// <summary>Maximum explained votes per explanation round.</summary>
        public const int MaxExplanations = 3;

        private readonly LogisticsLedger _ledger = new LogisticsLedger();
        private readonly LogisticsCatalog _catalog = new LogisticsCatalog();
        private readonly LogisticsTerritory _territory = new LogisticsTerritory();
        private readonly float[] _severity = new float[Counts.Resources];
        private readonly float[] _eta = new float[Counts.Resources];
        private readonly float[] _plan = new float[Counts.Buildings];
        private readonly string[] _planReason = new string[Counts.Buildings];
        private readonly bool[] _protected = new bool[Counts.Buildings];

        public override string AdvisorId => Id;
        public override int TickInterval => 12;

        /// <summary>Ledger of the most recently advised settlement (debug/tests).</summary>
        public LogisticsLedger LastLedger => _ledger;

        public override void Initialize(SimContext ctx)
        {
            _territory.Clear();
            ResetPlan();
        }

        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            if (db == null || ctx.State == null || !_catalog.Ensure(db)) return;
            var L = _ledger;
            L.Gather(ctx.State, db, s);
            int totalSites = L.PaidSites + L.UnpaidSites;
            if (totalSites == 0 && L.QueuedOrders == 0) { GoIdle(s); return; }

            var sp = s.Stockpile;
            float cap = Math.Max(1f, MathUtil.Sanitize(sp.Capacity, Stockpile.DefaultCapacity));
            float drive = 0.75f + 0.5f * AdvisorBoard.Trait(civ, db, AITrait.Industriousness);
            float expansion = AdvisorBoard.Trait(civ, db, AITrait.Expansionism);

            int stalled = L.UnaffordableSites;
            if (s.Memory.TryGetValue(LogisticsKeys.SitesWaiting, out float waitingMem))
                stalled = MathUtil.Clamp((int)MathUtil.Sanitize(waitingMem), 0, 9999);

            // ---- 1. bottlenecks: deficit, time-to-afford, severity
            int neck = -1;
            float maxSev = 0f;
            for (int r = 0; r < Counts.Resources; r++)
            {
                _severity[r] = 0f;
                _eta[r] = 0f;
                float demand = L.SiteDemand[r] + QueuedDemandWeight * L.QueueDemand[r];
                if (demand <= 0f) continue;
                var rt = (ResourceType)r;
                float deficit = demand - sp.Get(rt);
                if (deficit <= MinDeficit) continue;
                float net = MathUtil.Sanitize(sp.Net(rt));
                float eta = net > 0.001f ? Math.Min(NeverEtaDays, deficit / net) : NeverEtaDays;
                float sev = MathUtil.SmoothStep(EtaComfortDays, EtaPanicDays, eta) * (L.BlockedSites[r] > 0 ? 1f : 0.6f);
                sev = MathUtil.Clamp01(sev + 0.35f * L.MaxWaitFrac[r] + 0.1f * Math.Min(L.BlockedSites[r], 3));
                if (eta <= EtaComfortDays) sev *= 0.5f; // materials are arriving soon anyway
                _severity[r] = sev;
                _eta[r] = eta;
                if (sev > maxSev) { maxSev = sev; neck = r; }
            }

            PlacementRule features = _territory.Features(ctx, s);

            // ---- 2. producers of bottleneck materials (and their input chains)
            for (int r = 0; r < Counts.Resources; r++)
                if (_severity[r] >= VoteThreshold)
                    PlanProducers(ctx, civ, s, (ResourceType)r, _severity[r] * drive, 0, features, -1, ResourceType.Food);

            // ---- 3. storage (hard stall when a single cost can never fit)
            bool hard = PlanStorage(ctx, civ, s, cap, features);

            // ---- 4. restraint on big projects while sites are stalled
            if (stalled > StalledSitesLimit) PlanRestraint(db, civ, s, stalled, neck, cap, expansion);

            // ---- 5. builders proportional to paid sites
            PlanBuilders(civ, s, stalled);

            // ---- 6. publish votes
            ApplyPlan(ctx, s, civ);

            // ---- 7. urgency & diagnostics
            float u = Math.Max(maxSev * 0.8f, Math.Min(1f, stalled / 4f));
            if (hard) u = Math.Max(u, 0.9f);
            u = MathUtil.Clamp01(u + 0.2f * L.MaxWaitFracAll + (totalSites > 0 ? 0.1f * Math.Min(1f, stalled / (float)totalSites) : 0f));
            Urgency(s, u);
            s.Memory[LogisticsKeys.StallRisk] = u;
            s.Memory[LogisticsKeys.EtaDays] = neck >= 0 ? MathUtil.Clamp(_eta[neck], 0f, NeverEtaDays) : 0f;
            s.Memory[LogisticsKeys.Bottleneck] = neck;
            s.Memory[LogisticsKeys.HardStall] = hard ? 1f : 0f;

            bool nowStalled = stalled > StalledSitesLimit || hard;
            bool wasStalled = s.Mem(LogisticsKeys.Stalled) > 0.5f;
            if (nowStalled && !wasStalled)
            {
                string what = neck >= 0 ? LogisticsKeys.ResourceName((ResourceType)neck) : "materials";
                s.LogAi("[" + Id + "] construction stalled: " + stalled + " site(s) waiting, bottleneck " + what + EtaText(neck >= 0 ? _eta[neck] : 0f));
            }
            else if (!nowStalled && wasStalled) s.LogAi("[" + Id + "] construction flowing again");
            s.Memory[LogisticsKeys.Stalled] = nowStalled ? 1f : 0f;
        }

        // ================================================================== producers
        private void PlanProducers(SimContext ctx, Civilization civ, Settlement s, ResourceType res, float strength, int depth,
            PlacementRule features, int parentType, ResourceType parentRes)
        {
            if (strength <= 0.01f) return;
            var db = ctx.Db;
            var L = _ledger;
            var sp = s.Stockpile;
            int r = (int)res;
            var list = _catalog.ProducersOf(res);
            int best = -1, second = -1, freest = -1;
            float bestV = 0f, secondV = 0f;
            int free = 0, pending = 0, existing = 0;
            float prodMod = MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Production[r], 1f), 0.05f, 20f);
            for (int i = 0; i < list.Count; i++)
            {
                int t = (int)list[i];
                _protected[t] = true; // never vote against the cure
                existing += L.CompleteCount[t];
                pending += L.PendingCount[t];
                if (L.FreeSlots[t] > 0)
                {
                    free += L.FreeSlots[t];
                    if (freest < 0 || L.FreeSlots[t] > L.FreeSlots[freest]) freest = t;
                }
                var def = db.Building(list[i]);
                if (!CanBuildHere(db, civ, s, def, features)) continue;
                float v = (LogisticsCatalog.OutputOf(def, res) - LogisticsCatalog.InputOf(def, res)) * Math.Max(1, def.JobSlots) * prodMod;
                if (LogisticsCatalog.CostOf(def, res) > sp.Get(res)) v *= 0.5f;     // would itself wait for the bottleneck
                if (!sp.CanAfford(def.Cost)) v *= 0.8f;
                if (InputsStarved(sp, def)) v *= 0.6f;
                v /= 1f + _catalog.CostTotal(t) / 60f;
                if (v > bestV) { second = best; secondV = bestV; best = t; bestV = v; }
                else if (v > secondV) { second = t; secondV = v; }
            }

            // Staff what already exists before building more.
            if (freest >= 0)
            {
                var fdef = db.Building((BuildingType)freest);
                if (fdef.Job != JobType.None) VoteJob(s, civ, fdef.Job, MathUtil.Clamp01(strength * 0.8f));
            }
            else if (existing > 0 && best >= 0)
            {
                var bdef = db.Building((BuildingType)best);
                if (bdef.Job != JobType.None) VoteJob(s, civ, bdef.Job, MathUtil.Clamp01(strength * 0.3f));
            }

            if (best < 0) return;
            float build = strength;
            if (free > 0) build *= 0.35f;
            if (pending > 0) build /= 1f + pending;
            build = MathUtil.Clamp01(build);

            string reason = null;
            if (build >= 0.5f) reason = depth == 0 ? BottleneckReason(res, sp) : ChainReason(db, parentType, res, parentRes);
            Plan(best, build, reason);
            if (second >= 0) Plan(second, build * 0.4f, null);

            if (depth >= ChainDepth) return;
            var bestDef = db.Building((BuildingType)best);
            var inputs = bestDef.InputsPerWorker;
            if (inputs == null) return;
            for (int i = 0; i < inputs.Length; i++)
            {
                float a = LogisticsCatalog.Amount(inputs[i]);
                if (a <= 0f || inputs[i].Type == res) continue;
                var inRes = inputs[i].Type;
                if (sp.Get(inRes) >= a * Math.Max(1, bestDef.JobSlots) * InputBufferDays) continue;
                if (MathUtil.Sanitize(sp.Net(inRes)) > 0.0001f) continue;
                PlanProducers(ctx, civ, s, inRes, strength * ChainFactor, depth + 1, features, best, res);
            }
        }

        private static bool InputsStarved(Stockpile sp, BuildingDefinition def)
        {
            var inputs = def.InputsPerWorker;
            if (inputs == null) return false;
            for (int i = 0; i < inputs.Length; i++)
            {
                if (LogisticsCatalog.Amount(inputs[i]) <= 0f) continue;
                if (sp.Get(inputs[i].Type) < 1f && MathUtil.Sanitize(sp.Net(inputs[i].Type)) <= 0f) return true;
            }
            return false;
        }

        /// <summary>Availability, tier, population, per-settlement limit and hard placement requirements.</summary>
        private bool CanBuildHere(GameDatabase db, Civilization civ, Settlement s, BuildingDefinition def, PlacementRule features)
        {
            if (def == null || def.IsWonder || def.UniquePerCivilization) return false;
            if (!db.IsBuildingAvailable(civ, def.Type)) return false;
            if (s.Tier < def.MinTier || s.Population < def.MinPopulation) return false;
            int t = (int)def.Type;
            if (def.MaxPerSettlement >= 0 && _ledger.CompleteCount[t] + _ledger.PendingCount[t] >= def.MaxPerSettlement) return false;
            return LogisticsTerritory.Satisfies(def.Placement, features);
        }

        // ================================================================== storage
        private bool PlanStorage(SimContext ctx, Civilization civ, Settlement s, float cap, PlacementRule features)
        {
            var L = _ledger;
            var sp = s.Stockpile;
            int worst = -1;
            float worstAmt = 0f;
            for (int r = 0; r < Counts.Resources; r++)
                if (L.MaxSingle[r] > cap + 0.01f && L.MaxSingle[r] > worstAmt) { worst = r; worstAmt = L.MaxSingle[r]; }

            float soft = 0f;
            if (worst < 0)
                for (int r = 0; r < Counts.Resources; r++)
                {
                    float total = L.SiteDemand[r] + L.QueueDemand[r];
                    if (total <= 0f) continue;
                    var rt = (ResourceType)r;
                    if (total > cap * 0.9f) soft = Math.Max(soft, 0.4f);
                    else if (sp.Get(rt) >= cap * 0.95f && MathUtil.Sanitize(sp.Net(rt)) > 0f) soft = Math.Max(soft, 0.25f);
                }
            if (worst < 0 && soft <= 0f) return false;

            float storageMod = MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Storage, 1f), 0.1f, 20f);
            float pendingCap;
            int best = BestStorage(ctx.Db, civ, s, features, _catalog.Storage, storageMod, out pendingCap);
            if (best < 0) best = BestStorage(ctx.Db, civ, s, features, _catalog.StorageFallback, storageMod, out pendingCap);
            if (best < 0) return worst >= 0;
            _protected[best] = true;

            if (worst >= 0)
            {
                float need = worstAmt - cap;
                float str = pendingCap + 0.01f >= need ? 0.35f : 1f; // more storage already on its way?
                string reason = null;
                if (str >= 0.5f)
                    reason = LogisticsKeys.BuildingName(ctx.Db, L.MaxSingleType[worst]) + " needs " + (int)Math.Ceiling(worstAmt) + " "
                        + LogisticsKeys.ResourceName((ResourceType)worst) + ", storage holds " + (int)Math.Floor(cap);
                Plan(best, str, reason);
                return true;
            }
            Plan(best, soft, null);
            return false;
        }

        private int BestStorage(GameDatabase db, Civilization civ, Settlement s, PlacementRule features,
            System.Collections.Generic.IReadOnlyList<BuildingType> list, float storageMod, out float pendingCap)
        {
            pendingCap = 0f;
            int best = -1;
            float bestV = 0f;
            var sp = s.Stockpile;
            for (int i = 0; i < list.Count; i++)
            {
                int t = (int)list[i];
                var def = db.Building(list[i]);
                float capT = Math.Max(0f, MathUtil.Sanitize(def.StorageCapacity)) * storageMod;
                pendingCap += _ledger.PendingCount[t] * capT;
                if (!CanBuildHere(db, civ, s, def, features)) continue;
                float v = capT / (1f + _catalog.CostTotal(t) / 40f);
                if (!sp.CanAfford(def.Cost)) v *= 0.7f;
                if (v > bestV) { bestV = v; best = t; }
            }
            return best;
        }

        // ================================================================== restraint
        private void PlanRestraint(GameDatabase db, Civilization civ, Settlement s, int stalled, int neck, float cap, float expansion)
        {
            float restraint = MathUtil.Clamp(0.55f + 0.2f * (stalled - StalledSitesLimit - 1), 0.55f, 1f) * (1.15f - 0.3f * expansion);
            restraint = MathUtil.Clamp(restraint, 0.3f, 1f);
            string reason = null;
            int logged = 0;
            for (int t = 0; t < Counts.Buildings; t++)
            {
                if (_plan[t] != 0f || _protected[t]) continue;
                if (!db.TryGetBuilding((BuildingType)t, out var def)) continue;
                if (!_catalog.IsBigProject(def, BigProjectCost, cap)) continue;
                if (!db.IsBuildingAvailable(civ, def.Type)) continue;
                if (def.MaxPerSettlement >= 0 && _ledger.CompleteCount[t] >= def.MaxPerSettlement) continue;
                bool usesNeck = neck >= 0 && LogisticsCatalog.CostOf(def, (ResourceType)neck) > 0f;
                float v = def.IsWonder || usesNeck ? restraint : restraint * 0.5f;
                string why = null;
                if (v >= 0.5f && logged < MaxRestraintReasons)
                {
                    if (reason == null)
                        reason = stalled + " sites waiting for " + (neck >= 0 ? LogisticsKeys.ResourceName((ResourceType)neck) : "materials");
                    why = reason;
                    logged++;
                }
                _plan[t] = -v;
                _planReason[t] = why;
            }
        }

        // ================================================================== builders
        private void PlanBuilders(Civilization civ, Settlement s, int stalled)
        {
            var L = _ledger;
            if (L.PaidSites > 0)
            {
                int desired = 2 * L.PaidSites + 1;
                float str = Math.Min(1f, 0.25f * L.PaidSites);
                str = Math.Min(1f, str * (1f + 0.5f * MathUtil.Clamp01(L.PaidWorkRemaining / 1000f)));
                if (L.Builders >= desired) str *= 0.25f;
                VoteJob(s, civ, JobType.Builder, str);
            }
            else if (L.UnpaidSites > 0 && stalled > 0 && L.Builders > 0)
            {
                VoteJob(s, civ, JobType.Builder, -0.4f); // nothing to build until materials arrive
            }
        }

        // ================================================================== plan helpers
        private void Plan(int type, float strength, string reason)
        {
            if (type < 0 || type >= _plan.Length) return;
            if (strength > _plan[type])
            {
                _plan[type] = strength;
                if (reason != null) _planReason[type] = reason;
            }
        }

        /// <summary>
        /// Publishes the planned votes. Explanations are rate-limited (at most <see cref="MaxExplanations"/> of the
        /// strongest reasoned votes every <see cref="ExplainEveryTicks"/> ticks) so the AI log stays readable; reasons the
        /// board would not log (scaled vote below 0.5) are logged here in the board's format.
        /// </summary>
        private void ApplyPlan(SimContext ctx, Settlement s, Civilization civ)
        {
            bool explain = ctx.Tick - (long)s.Mem(LogisticsKeys.ExplainTick, -1e6f) >= ExplainEveryTicks;
            int e0 = -1, e1 = -1, e2 = -1;
            if (explain)
            {
                e0 = StrongestReasoned(-1, -1);
                e1 = e0 >= 0 ? StrongestReasoned(e0, -1) : -1;
                e2 = e1 >= 0 ? StrongestReasoned(e0, e1) : -1;
            }
            float scale = CycleScale * AdvisorBoard.Weight(civ, Id);
            for (int t = 0; t < _plan.Length; t++)
            {
                float v = _plan[t];
                if (v == 0f) continue;
                string why = null;
                if (t == e0 || t == e1 || t == e2)
                {
                    float scaled = MathUtil.Clamp(v, -1f, 1f) * scale;
                    if (Math.Abs(scaled) >= 0.5f) why = _planReason[t];
                    else if (scaled != 0f) s.LogAi("[" + Id + "] " + (scaled > 0f ? "+" : "") + scaled.ToString("0.00") + " " + (BuildingType)t + ": " + _planReason[t]);
                }
                VoteBuild(s, civ, (BuildingType)t, v, why);
            }
            if (e0 >= 0) s.Memory[LogisticsKeys.ExplainTick] = ctx.Tick;
            ResetPlan();
        }

        private int StrongestReasoned(int skipA, int skipB)
        {
            int best = -1;
            float bestAbs = 0f;
            for (int t = 0; t < _plan.Length; t++)
            {
                if (t == skipA || t == skipB || _planReason[t] == null) continue;
                float a = Math.Abs(_plan[t]);
                if (a >= 0.5f && a > bestAbs) { bestAbs = a; best = t; }
            }
            return best;
        }

        private void ResetPlan()
        {
            Array.Clear(_plan, 0, _plan.Length);
            Array.Clear(_planReason, 0, _planReason.Length);
            Array.Clear(_protected, 0, _protected.Length);
        }

        private void GoIdle(Settlement s)
        {
            Urgency(s, 0f);
            if (s.Mem(LogisticsKeys.Stalled) > 0.5f) s.LogAi("[" + Id + "] construction flowing again");
            s.Memory.Remove(LogisticsKeys.Stalled);
            s.Memory.Remove(LogisticsKeys.StallRisk);
            s.Memory.Remove(LogisticsKeys.EtaDays);
            s.Memory.Remove(LogisticsKeys.Bottleneck);
            s.Memory.Remove(LogisticsKeys.HardStall);
            ResetPlan();
        }

        // ================================================================== explanations
        private string BottleneckReason(ResourceType res, Stockpile sp)
        {
            var L = _ledger;
            int r = (int)res;
            int n = L.BlockedSites[r];
            float need = L.SiteDemand[r] + QueuedDemandWeight * L.QueueDemand[r];
            string head = n > 0
                ? n + (n == 1 ? " site waiting for " : " sites waiting for ") + LogisticsKeys.ResourceName(res)
                : "queued builds need " + LogisticsKeys.ResourceName(res);
            return head + ": need " + (int)Math.Ceiling(need) + ", have " + (int)Math.Floor(sp.Get(res)) + EtaText(_eta[r]);
        }

        private static string ChainReason(GameDatabase db, int parentType, ResourceType res, ResourceType parentRes)
        {
            return LogisticsKeys.BuildingName(db, parentType) + " needs " + LogisticsKeys.ResourceName(res) + " to make " + LogisticsKeys.ResourceName(parentRes);
        }

        private static string EtaText(float eta)
        {
            if (eta <= 0f) return "";
            if (eta >= NeverEtaDays) return " (no income)";
            return " (~" + (int)Math.Ceiling(eta) + " days)";
        }
    }
}
