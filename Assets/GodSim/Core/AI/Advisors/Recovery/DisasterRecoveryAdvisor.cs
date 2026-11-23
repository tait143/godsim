using System;
using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The Recovery advisor: makes civilizations resilient. It listens for catastrophes (buildings lost to fire,
    /// disaster, war or divine power; disasters; famines), keeps a decaying per-settlement loss ledger, and votes a
    /// prioritized rebuild plan (Town Center, then dwellings while people are homeless, food, storage, the rest),
    /// fading over ~3 years and paced by what the settlement can actually rebuild. It also learns: fires teach wells
    /// and forest-edge caution, floods teach dry stores on high ground, war teaches walls, famine teaches granaries
    /// and food diversity, plague teaches healers, divine wrath teaches piety. Lessons also enter the civilization's
    /// slower cultural memory, so its other towns prepare before disaster strikes them.
    /// Votes are soft (AdvisorBoard), gated by availability, tier, population and per-settlement limits; strengths
    /// below are per council cycle (the base class scales each 6-tick vote by its share of the 24-tick cycle).
    /// </summary>
    public sealed class DisasterRecoveryAdvisor : AdvisorSystemBase
    {
        public const string Id = "recovery";

        /// <summary>Rebuild votes fade linearly to zero over this window after the latest catastrophe.</summary>
        public const int RecoveryWindowTicks = SimTime.TicksPerYear * 3;
        public const float LedgerHalfLifeTicks = SimTime.TicksPerYear * 2f;
        public const float ScarHalfLifeTicks = SimTime.TicksPerYear * 1.5f;
        public const float CivScarHalfLifeTicks = SimTime.TicksPerYear * 12f;
        /// <summary>Minimum ticks between reason-logging passes for one settlement (unless a new catastrophe struck).</summary>
        public const int LogIntervalTicks = SimTime.TicksPerSeason;
        /// <summary>How strongly the civ's cultural memory drives preventive votes relative to a local scar.</summary>
        public const float CivMemoryFactor = 0.35f;

        public override string AdvisorId => Id;
        public override int TickInterval => 6;

        private readonly Action<BuildingDestroyedEvent> _onDestroyed;
        private readonly Action<BuildingCompletedEvent> _onCompleted;
        private readonly Action<DisasterEvent> _onDisaster;
        private readonly Action<FamineEvent> _onFamine;
        private EventBus _bus;
        private WorldState _state;
        private RecoveryCatalog _catalog;
        private readonly string _urgencyKey = AdvisorBoard.UrgencyKey(Id);

        // per-settlement survey (reused, no per-tick allocation)
        private readonly int[] _complete = new int[Counts.Buildings];
        private readonly int[] _building = new int[Counts.Buildings];
        private readonly int[] _queued = new int[Counts.Buildings];
        private readonly float[] _need = new float[Counts.Resources];
        private readonly List<int> _lostTypes = new List<int>(Counts.Buildings);
        private readonly StringBuilder _sb = new StringBuilder(160);
        private int _features;
        private bool _featuresReady;

        // per-tick decay factors
        private long _factorTick = long.MinValue;
        private float _lostDecay = 1f, _scarDecay = 1f, _civDecay = 1f;

        public DisasterRecoveryAdvisor()
        {
            _onDestroyed = OnBuildingDestroyed;
            _onCompleted = OnBuildingCompleted;
            _onDisaster = OnDisaster;
            _onFamine = OnFamine;
        }

        public override void Initialize(SimContext ctx)
        {
            if (ctx == null) return;
            _state = ctx.State;
            if (!ReferenceEquals(_bus, ctx.Events))
            {
                if (_bus != null)
                {
                    _bus.Unsubscribe(_onDestroyed);
                    _bus.Unsubscribe(_onCompleted);
                    _bus.Unsubscribe(_onDisaster);
                    _bus.Unsubscribe(_onFamine);
                }
                _bus = ctx.Events;
                if (_bus != null)
                {
                    _bus.Subscribe(_onDestroyed);
                    _bus.Subscribe(_onCompleted);
                    _bus.Subscribe(_onDisaster);
                    _bus.Subscribe(_onFamine);
                }
            }
            if (ctx.Db != null) _catalog = new RecoveryCatalog(ctx.Db);
        }

        // =================================================================== events -> ledger
        private void OnBuildingDestroyed(BuildingDestroyedEvent e)
        {
            RecoveryCatastrophe kind;
            switch (e.Cause)
            {
                case DestroyCause.Fire: kind = RecoveryCatastrophe.Fire; break;
                case DestroyCause.Disaster: kind = RecoveryCatastrophe.Disaster; break;
                case DestroyCause.War: kind = RecoveryCatastrophe.War; break;
                case DestroyCause.DivinePower: kind = RecoveryCatastrophe.DivineWrath; break;
                default: return; // demolition, decay and abandonment are not catastrophes
            }
            var state = _state;
            if (state == null || (int)e.Type < 0 || (int)e.Type >= Counts.Buildings) return;
            var s = state.Settlements.Get(e.SettlementId);
            if (s == null || !s.IsAlive) return; // fatigue: a dead settlement plans nothing
            var civ = state.Civilizations.Get(s.CivilizationId);
            long tick = state.Tick;
            RecoveryLedger.AddLoss(s, e.Type, 1f);
            switch (e.Cause)
            {
                case DestroyCause.Fire:
                    RecoveryLedger.AddScar(s, civ, RecoveryScar.Fire, 0.5f);
                    if (state.Map != null && state.Map.InBounds(e.Origin) && state.Map.CountFlagInRadius(e.Origin, 2, TileFlags.Trees) > 0)
                        s.Memory[RecoveryKeys.ForestEdge] = MathUtil.Clamp(s.Mem(RecoveryKeys.ForestEdge) + 1f, 0f, RecoveryLedger.MaxScar);
                    RecoveryLedger.MarkKind(s, kind, tick, true);
                    break;
                case DestroyCause.War:
                    RecoveryLedger.AddScar(s, civ, RecoveryScar.War, 0.5f);
                    RecoveryLedger.MarkKind(s, kind, tick, false);
                    break;
                case DestroyCause.DivinePower:
                    RecoveryLedger.AddScar(s, civ, RecoveryScar.Divine, 0.5f);
                    RecoveryLedger.MarkKind(s, kind, tick, false);
                    break;
                default:
                    RecoveryLedger.MarkKind(s, kind, tick, true);
                    break;
            }
            RecoveryLedger.Touch(s, tick);
        }

        private void OnBuildingCompleted(BuildingCompletedEvent e)
        {
            var state = _state;
            if (state == null || (int)e.Type < 0 || (int)e.Type >= Counts.Buildings) return;
            var s = state.Settlements.Get(e.SettlementId);
            if (s == null || !s.IsAlive || !RecoveryLedger.IsRecovering(s)) return;
            RecoveryLedger.CreditRebuild(s, e.Type);
        }

        private void OnDisaster(DisasterEvent e)
        {
            var state = _state;
            if (state == null) return;
            float sev = MathUtil.Clamp(MathUtil.Sanitize(e.Severity, 0.5f), 0f, 2f);
            float amount = MathUtil.Clamp(0.5f + 0.5f * sev, 0.25f, 1.5f);
            int radius = MathUtil.Clamp(e.Radius, 0, 64);
            var scar = RecoveryLedger.ScarOf(e.Type);
            var kind = RecoveryLedger.FromDisaster(e.Type);
            var items = state.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (!s.IsAlive) continue;
                int reach = radius + Math.Max(1, s.TerritoryRadius);
                if (s.Center.DistanceSqTo(e.Position) > reach * reach) continue;
                var civ = state.Civilizations.Get(s.CivilizationId);
                RecoveryLedger.AddScar(s, civ, scar, amount);
                RecoveryLedger.MarkKind(s, kind, state.Tick, false);
                RecoveryLedger.Touch(s, state.Tick);
            }
        }

        private void OnFamine(FamineEvent e)
        {
            var state = _state;
            if (state == null) return;
            var s = state.Settlements.Get(e.SettlementId);
            if (s == null || !s.IsAlive) return;
            long tick = state.Tick;
            var civ = state.Civilizations.Get(s.CivilizationId);
            // Famines are reported repeatedly while they last: one full scar per season, a trickle otherwise.
            bool fresh = tick - (long)s.Mem(RecoveryKeys.FamineTick, -100000f) >= SimTime.TicksPerSeason;
            RecoveryLedger.AddScar(s, civ, RecoveryScar.Famine, fresh ? 0.75f : 0.05f);
            if (fresh) s.Memory[RecoveryKeys.FamineTick] = tick;
            RecoveryLedger.MarkKind(s, RecoveryCatastrophe.Famine, tick, true);
            RecoveryLedger.Touch(s, tick);
        }

        // =================================================================== advising
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            if (db == null) return;
            if (_catalog == null || !ReferenceEquals(_catalog.Db, db)) _catalog = new RecoveryCatalog(db);
            if (_state == null) _state = ctx.State;
            UpdateFactors(ctx);
            var mem = s.Memory;
            long tick = ctx.Tick;
            bool local = mem.TryGetValue(RecoveryKeys.LastTick, out float lastTickF);
            bool civMemory = RecoveryLedger.HasCivMemory(civ);
            if ((!local && !civMemory) || s.Population <= 0)
            {
                mem.Remove(_urgencyKey);
                return;
            }

            float age = 0f;
            bool hasLosses = false, hasScars = false;
            if (local)
            {
                age = MathUtil.Sanitize(tick - lastTickF);
                if (age < 0f) age = 0f;
                if (age > RecoveryWindowTicks) RecoveryLedger.ClearLosses(s); // the rebuild plan is over
                RecoveryLedger.Decay(s, _lostDecay, _scarDecay, out hasLosses, out hasScars);
                if (!hasLosses && !hasScars)
                {
                    RecoveryLedger.ClearAll(s);
                    local = false;
                    if (!civMemory) { mem.Remove(_urgencyKey); return; }
                }
            }

            Survey(ctx.State, s);
            _featuresReady = false;
            float weight = AdvisorBoard.Weight(civ, Id);
            bool log = local && ShouldLog(s, tick, lastTickF);
            var kind = RecoveryLedger.Kind(s);
            string disaster = RecoveryLedger.Name(kind);

            float urgency = 0f, acuteTc = 0f, spent = 0f;
            if (hasLosses)
            {
                float fade = MathUtil.Clamp01(1f - age / RecoveryWindowTicks);
                if (fade > 0f) urgency = PlanRebuild(ctx, db, civ, s, fade, weight, log, disaster, out acuteTc, out spent);
            }
            float mitigation = PlanMitigation(ctx, db, civ, s, local, weight, log, kind, acuteTc);
            urgency = Math.Max(urgency, mitigation * 0.6f);

            if (spent > 0.05f) VoteJob(s, civ, JobType.Builder, MathUtil.Clamp(0.2f + 0.6f * urgency, 0f, 0.8f));
            if (urgency > 0.005f) Urgency(s, urgency); else mem.Remove(_urgencyKey);
        }

        protected override void AfterRun(SimContext ctx)
        {
            UpdateFactors(ctx);
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
                if (civs[i].IsAlive) RecoveryLedger.DecayCiv(civs[i], _civDecay);
        }

        // ------------------------------------------------------------------ rebuild plan
        private float PlanRebuild(SimContext ctx, GameDatabase db, Civilization civ, Settlement s, float fade, float weight, bool log,
            string disaster, out float acuteTc, out float spent)
        {
            acuteTc = 0f;
            spent = 0f;
            var mem = s.Memory;
            var lostKeys = RecoveryKeys.Lost;
            var tiers = _catalog.Tier;

            // Collect losses, ordered by rebuild tier then type (deterministic insertion sort, no allocation).
            _lostTypes.Clear();
            for (int i = 0; i < lostKeys.Length; i++)
            {
                if (!mem.TryGetValue(lostKeys[i], out float v) || v < RecoveryLedger.PruneBelow) continue;
                int j = _lostTypes.Count;
                _lostTypes.Add(i);
                while (j > 0 && tiers[_lostTypes[j - 1]] > tiers[i]) { _lostTypes[j] = _lostTypes[j - 1]; j--; }
                _lostTypes[j] = i;
            }
            if (_lostTypes.Count == 0) return 0f;

            Array.Clear(_need, 0, _need.Length);
            float budget = Budget(ctx.Db, civ, s);
            int homeless = Math.Max(0, s.Homeless);
            float foodDays = MathUtil.Sanitize(s.Mem("econ.foodDays", -1f), -1f);
            bool forestEdge = s.Mem(RecoveryKeys.ForestEdge) > 0.25f;
            float urgency = 0f;

            for (int n = 0; n < _lostTypes.Count; n++)
            {
                int t = _lostTypes[n];
                var def = db.Buildings[t];
                if (def == null || def.IsWonder || def.UniquePerCivilization) continue; // wonders are not rebuilt on impulse
                var tier = tiers[t];
                float lost = mem[lostKeys[t]];
                if (tier == RecoveryTier.TownCenter && _complete[t] > 0) { mem.Remove(lostKeys[t]); continue; } // already rebuilt

                int target = t;
                if (!CanBuildHere(db, civ, s, t))
                {
                    target = FindSubstitute(db, civ, s, t);
                    if (target < 0) continue;
                }
                float outstanding = lost - _building[target] - _queued[target];
                if (outstanding < 0.3f) continue;

                float baseStrength, tierUrgency, sat;
                switch (tier)
                {
                    case RecoveryTier.TownCenter:
                        baseStrength = 1f; tierUrgency = 1f; break;
                    case RecoveryTier.Dwelling:
                        if (homeless > 0) { baseStrength = 0.95f * (0.5f + 0.5f * MathUtil.Clamp01(homeless / 4f)); tierUrgency = 0.85f; }
                        else { baseStrength = 0.3f; tierUrgency = 0.3f; }
                        break;
                    case RecoveryTier.Food:
                        baseStrength = foodDays >= 0f && foodDays < 30f ? 0.75f + 0.2f * (1f - foodDays / 30f) : 0.75f;
                        tierUrgency = baseStrength * 0.85f;
                        break;
                    case RecoveryTier.Storage:
                        baseStrength = 0.6f; tierUrgency = 0.5f; break;
                    default:
                        baseStrength = def.HasJobs || (def.Services != null && def.Services.Length > 0) ? 0.45f : 0.3f; tierUrgency = 0.35f; break;
                }
                float strength;
                if (tier == RecoveryTier.TownCenter)
                {
                    sat = 1f;
                    strength = 0.6f + 0.4f * fade; // the heart of the town stays the top priority
                    acuteTc = 1f;
                }
                else
                {
                    sat = 1f - (float)Math.Exp(-outstanding / 1.5f);
                    strength = baseStrength * sat * fade;
                    // Pace the plan: a small settlement cannot raise everything at once, so later tiers wait their turn.
                    if (spent >= budget) strength *= 0.2f;
                    else if (spent + strength > budget) strength = (budget - spent) + (spent + strength - budget) * 0.2f;
                }
                spent += strength;
                urgency = Math.Max(urgency, tierUrgency * sat * (tier == RecoveryTier.TownCenter ? 1f : fade));
                if (strength < 0.02f) continue;

                string reason = null;
                if (log && strength * weight >= 0.5f)
                {
                    _sb.Length = 0;
                    _sb.Append("Rebuilding after ").Append(disaster).Append(": ");
                    _sb.Append(RecoveryLedger.CountedName(def, (BuildingType)t, Math.Max(1, MathUtil.RoundToInt(lost)))).Append(" lost");
                    if (tier == RecoveryTier.TownCenter) _sb.Append(" (top priority)");
                    if (tier == RecoveryTier.Dwelling && homeless > 0) _sb.Append(", ").Append(homeless).Append(" homeless");
                    if (target != t) _sb.Append(", rebuilt as ").Append(db.Buildings[target].Name);
                    if (forestEdge && tier <= RecoveryTier.Dwelling) _sb.Append("; away from the forest edge");
                    reason = _sb.ToString();
                }
                VoteBuild(s, civ, (BuildingType)target, strength, reason);

                if (strength > 0.1f)
                {
                    var cost = db.Buildings[target].Cost;
                    float count = Math.Min(outstanding, 3f);
                    if (cost != null) for (int k = 0; k < cost.Length; k++) _need[(int)cost[k].Type] += MathUtil.Sanitize(cost[k].Amount) * count;
                }
            }

            PlanMaterials(ctx, db, civ, s, fade, weight, log);
            return urgency;
        }

        /// <summary>Votes for the producer of the resource the rebuild is most short of.</summary>
        private void PlanMaterials(SimContext ctx, GameDatabase db, Civilization civ, Settlement s, float fade, float weight, bool log)
        {
            int worst = -1;
            float worstShort = 0f, worstMissing = 0f;
            for (int r = 0; r < _need.Length; r++)
            {
                float need = _need[r];
                if (need < 5f) continue;
                float missing = need - MathUtil.Sanitize(s.Stockpile.Get((ResourceType)r));
                float shortRatio = MathUtil.Clamp01(missing / need);
                if (shortRatio > worstShort) { worstShort = shortRatio; worst = r; worstMissing = missing; }
            }
            if (worst < 0 || worstShort < 0.25f) return;
            int prod = PickBest(db, civ, s, _catalog.Producers[worst], null, (ResourceType)worst, true);
            if (prod < 0) return;
            bool has = _complete[prod] + _building[prod] + _queued[prod] > 0;
            float strength = 0.55f * worstShort * fade * (has ? 0.4f : 1f);
            string reason = null;
            if (log && strength * weight >= 0.5f)
                reason = "Materials for rebuilding: short " + MathUtil.RoundToInt(worstMissing) + " " + ((ResourceType)worst).ToString().ToLowerInvariant();
            VoteBuild(s, civ, (BuildingType)prod, strength, reason);
            var job = db.Buildings[prod].Job;
            if (job != JobType.None) VoteJob(s, civ, job, 0.5f * worstShort * fade);
        }

        // ------------------------------------------------------------------ mitigation learning
        private float PlanMitigation(SimContext ctx, GameDatabase db, Civilization civ, Settlement s, bool local, float weight, bool log,
            RecoveryCatastrophe kind, float acuteTc)
        {
            float pop = s.Population;
            float mitScale = 1f - 0.3f * acuteTc; // shelter and the town heart first, then the lessons
            float best = 0f;
            var cov = s.ServiceCoverage;

            // Fire -> wells (bucket chains) and forest-edge caution.
            float fire = Risk(s, civ, RecoveryScar.Fire, local);
            float forestEdge = local ? s.Mem(RecoveryKeys.ForestEdge) : 0f;
            SetHint(s, RecoveryKeys.HintAvoidForest, fire * (forestEdge > 0.25f ? 1f : 0.6f));
            if (fire > 0.03f)
            {
                float need = Need(RoleCount(_catalog.Water), 1f + pop / 20f, Cov(cov, ServiceType.Water));
                float st = 0.9f * fire * need * mitScale;
                string reason = log && st * weight >= 0.5f
                    ? "Lessons of " + Lesson(kind, RecoveryCatastrophe.Fire, RecoveryCatastrophe.Volcano, "past fires") + ": wells for bucket chains" + (forestEdge > 0.25f ? "; keep new homes off the forest edge" : "")
                    : null;
                best = Math.Max(best, VoteRole(db, civ, s, _catalog.Water, _catalog.WaterAmount, st, reason, true));
            }

            // Flood / storm -> dry stores on higher ground.
            float flood = Risk(s, civ, RecoveryScar.Flood, local);
            SetHint(s, RecoveryKeys.HintHighGround, flood);
            if (flood > 0.03f)
            {
                float need = Need(RoleCount(_catalog.Storage), 1f + pop / 30f, 0f);
                float st = 0.75f * flood * need * mitScale;
                string reason = log && st * weight >= 0.5f ? "After " + Lesson(kind, RecoveryCatastrophe.Flood, RecoveryCatastrophe.Storm, "past floods") + ": dry stores on higher ground" : null;
                best = Math.Max(best, VoteRole(db, civ, s, _catalog.Storage, _catalog.StorageAmount, st, reason, false));
            }

            // War -> walls (aggressive cultures answer harder) and soldiers.
            float war = Risk(s, civ, RecoveryScar.War, local);
            if (war > 0.03f)
            {
                float aggression = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
                float desired = MathUtil.Clamp(2f + 2f * RecoveryLedger.Scar(s, RecoveryScar.War), 2f, 10f);
                float need = Need(RoleCount(_catalog.Walls), desired, Cov(cov, ServiceType.Defense));
                float st = 0.9f * war * need * (0.7f + 0.6f * aggression) * mitScale;
                string reason = log && st * weight >= 0.5f ? "Walls after " + Lesson(kind, RecoveryCatastrophe.War, RecoveryCatastrophe.War, "past raids") + ": never again undefended" : null;
                best = Math.Max(best, VoteRole(db, civ, s, _catalog.Walls, _catalog.DefenseAmount, st, reason, true));
                VoteJob(s, civ, JobType.Soldier, 0.35f * war * (0.6f + 0.8f * aggression));
            }

            // Famine -> granaries and food diversification.
            float famine = Risk(s, civ, RecoveryScar.Famine, local);
            float drought = Risk(s, civ, RecoveryScar.Drought, local);
            float hunger = Math.Max(famine, drought * 0.7f);
            if (hunger > 0.03f)
            {
                int granary = CanBuildHere(db, civ, s, (int)BuildingType.Granary) && _catalog.StorageAmount[(int)BuildingType.Granary] > 0f
                    ? (int)BuildingType.Granary : PickBest(db, civ, s, _catalog.Storage, _catalog.StorageAmount, ResourceType.Food, false);
                if (granary >= 0)
                {
                    float need = Need(_complete[granary] + _building[granary] + _queued[granary], 1f + pop / 40f, 0f);
                    float st = 0.9f * hunger * need * mitScale;
                    string reason = log && st * weight >= 0.5f ? "Never hungry again: " + db.Buildings[granary].Name.ToLowerInvariant() + " after " + Lesson(kind, RecoveryCatastrophe.Famine, RecoveryCatastrophe.Drought, "past famines") : null;
                    VoteBuild(s, civ, (BuildingType)granary, st, reason);
                    best = Math.Max(best, st);
                }
                if (famine > 0.03f) best = Math.Max(best, Diversify(db, civ, s, famine * mitScale, weight, log));
            }

            // Drought -> water.
            if (drought > 0.03f)
            {
                float need = Need(RoleCount(_catalog.Water), 1f + pop / 20f, Cov(cov, ServiceType.Water));
                best = Math.Max(best, VoteRole(db, civ, s, _catalog.Water, _catalog.WaterAmount, 0.6f * drought * need * mitScale, null, false));
            }

            // Plague (and the injured of earthquakes) -> healers and clean water.
            float plague = Risk(s, civ, RecoveryScar.Plague, local);
            float quake = Risk(s, civ, RecoveryScar.Quake, local);
            float sick = Math.Max(plague, quake * 0.5f);
            if (sick > 0.03f)
            {
                float need = Need(RoleCount(_catalog.Health), 1f + pop / 40f, Cov(cov, ServiceType.Health));
                float st = 0.85f * sick * need * mitScale;
                string reason = log && st * weight >= 0.5f ? "After " + Lesson(kind, RecoveryCatastrophe.Plague, RecoveryCatastrophe.Earthquake, "past plagues") + ": healers for the sick and injured" : null;
                best = Math.Max(best, VoteRole(db, civ, s, _catalog.Health, _catalog.HealthAmount, st, reason, false));
                if (plague > 0.03f)
                {
                    float wneed = Need(RoleCount(_catalog.Water), 1f + pop / 20f, Cov(cov, ServiceType.Water));
                    VoteRole(db, civ, s, _catalog.Water, _catalog.WaterAmount, 0.4f * plague * wneed * mitScale, null, false);
                }
            }

            // Divine wrath -> appease the god (pious or fearful cultures much more so).
            float divine = Risk(s, civ, RecoveryScar.Divine, local);
            if (divine > 0.03f)
            {
                float piety = AdvisorBoard.Trait(civ, db, AITrait.Piety);
                float fear = AdvisorBoard.GodFear(civ);
                float need = Need(RoleCount(_catalog.Faith), 1f + pop / 30f, Cov(cov, ServiceType.Faith));
                float st = MathUtil.Clamp01(0.8f * divine * need * (0.4f + 0.5f * piety + 0.5f * fear)) * mitScale;
                string reason = log && st * weight >= 0.5f ? "Appease the angry gods after " + Lesson(kind, RecoveryCatastrophe.DivineWrath, RecoveryCatastrophe.DivineWrath, "their wrath") : null;
                best = Math.Max(best, VoteRole(db, civ, s, _catalog.Faith, _catalog.FaithAmount, st, reason, false));
            }
            return best;
        }

        /// <summary>After famine: votes food producers the settlement does not have yet (terrain permitting).</summary>
        private float Diversify(GameDatabase db, Civilization civ, Settlement s, float famine, float weight, bool log)
        {
            var food = _catalog.Food;
            int present = 0;
            for (int i = 0; i < food.Length; i++) if (_complete[food[i]] + _building[food[i]] + _queued[food[i]] > 0) present++;
            float st = 0.6f * famine * (present == 0 ? 1f : present == 1 ? 0.8f : 0.4f);
            if (st < 0.02f) return 0f;
            int voted = 0;
            for (int i = 0; i < food.Length && voted < 2; i++)
            {
                int t = food[i];
                if (_complete[t] + _building[t] + _queued[t] > 0) continue;
                if (!CanBuildHere(db, civ, s, t) || !RecoveryCatalog.PlacementViable(db.Buildings[t], Features(s))) continue;
                string reason = log && voted == 0 && st * weight >= 0.5f ? "Never hungry again: diversify food with a " + db.Buildings[t].Name.ToLowerInvariant() : null;
                VoteBuild(s, civ, (BuildingType)t, st, reason);
                voted++;
            }
            return voted > 0 ? st : 0f;
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>
        /// Casts a vote through the base helper (which normalizes it per 24-tick council cycle) and logs the reason
        /// when the per-cycle vote is strong, using the board's log format. The board cannot log for us because the
        /// cycle-scaled single vote of a 6-tick advisor never reaches its 0.5 logging threshold.
        /// </summary>
        private new void VoteBuild(Settlement s, Civilization civ, BuildingType t, float strength, string reason = null)
        {
            base.VoteBuild(s, civ, t, strength);
            if (reason == null || !s.IsAlive) return;
            float v = MathUtil.Clamp(MathUtil.Sanitize(strength), -1f, 1f) * AdvisorBoard.Weight(civ, Id);
            if (Math.Abs(v) < 0.5f) return;
            int c = MathUtil.RoundToInt(Math.Abs(v) * 100f);
            int frac = c % 100;
            s.LogAi("[" + Id + "] " + (v > 0f ? "+" : "-") + (c / 100) + "." + (frac < 10 ? "0" : "") + frac + " " + t + ": " + reason);
        }

        /// <summary>Names the lesson after the current catastrophe when it taught it, else after older ones.</summary>
        private static string Lesson(RecoveryCatastrophe kind, RecoveryCatastrophe a, RecoveryCatastrophe b, string fallback)
            => kind == a || kind == b ? RecoveryLedger.Name(kind) : fallback;

        private float Risk(Settlement s, Civilization civ, RecoveryScar k, bool local)
        {
            float x = (local ? RecoveryLedger.Scar(s, k) : 0f) + CivMemoryFactor * RecoveryLedger.CivScar(civ, k);
            x = MathUtil.Sanitize(x);
            return x <= 0f ? 0f : x / (x + 1f);
        }

        private static float Need(int have, float desired, float coverage)
        {
            float byCount = desired <= 0f ? 0f : MathUtil.Clamp01(1f - have / desired);
            float byCoverage = MathUtil.Clamp01(1.25f - coverage);
            return Math.Min(byCount, byCoverage);
        }

        private static float Cov(float[] cov, ServiceType t) => cov == null ? 0f : MathUtil.Sanitize(cov[(int)t]);

        private static void SetHint(Settlement s, string key, float v)
        {
            v = MathUtil.Clamp01(MathUtil.Sanitize(v));
            if (v < 0.02f) s.Memory.Remove(key); else s.Memory[key] = v;
        }

        private int RoleCount(int[] role)
        {
            int c = 0;
            for (int i = 0; i < role.Length; i++) { int t = role[i]; c += _complete[t] + _building[t] + _queued[t]; }
            return c;
        }

        /// <summary>Votes the best buildable type of a role (by amount) and, when alsoCheapest, its cheapest alternative at 60%.</summary>
        private float VoteRole(GameDatabase db, Civilization civ, Settlement s, int[] role, float[] amount, float strength, string reason, bool alsoCheapest)
        {
            if (strength < 0.02f) return 0f;
            int bestT = PickBest(db, civ, s, role, amount, ResourceType.Food, false);
            if (bestT < 0) return 0f;
            VoteBuild(s, civ, (BuildingType)bestT, strength, reason);
            if (alsoCheapest)
            {
                int cheap = -1;
                float cheapCost = float.MaxValue;
                for (int i = 0; i < role.Length; i++)
                {
                    int t = role[i];
                    if (t == bestT || !CanBuildHere(db, civ, s, t)) continue;
                    if (_catalog.CostTotal[t] < cheapCost) { cheapCost = _catalog.CostTotal[t]; cheap = t; }
                }
                if (cheap >= 0 && cheapCost < _catalog.CostTotal[bestT]) VoteBuild(s, civ, (BuildingType)cheap, strength * 0.6f, null);
            }
            return strength;
        }

        /// <summary>Best buildable type in a role: highest amount (or output of resource when byOutput), cheaper on ties.</summary>
        private int PickBest(GameDatabase db, Civilization civ, Settlement s, int[] role, float[] amount, ResourceType res, bool byOutput)
        {
            int best = -1;
            float bestScore = 0f, bestCost = float.MaxValue;
            for (int i = 0; i < role.Length; i++)
            {
                int t = role[i];
                if (!CanBuildHere(db, civ, s, t)) continue;
                var d = db.Buildings[t];
                if (!RecoveryCatalog.PlacementViable(d, Features(s))) continue;
                float score = byOutput ? RecoveryCatalog.OutputAmount(d, res) : amount[t];
                float cost = _catalog.CostTotal[t];
                if (score > bestScore + 1e-4f || (Math.Abs(score - bestScore) <= 1e-4f && cost < bestCost))
                {
                    best = t; bestScore = score; bestCost = cost;
                }
            }
            return best;
        }

        /// <summary>An obsolete or no-longer-allowed loss is rebuilt as the newest buildable type of the same category and role.</summary>
        private int FindSubstitute(GameDatabase db, Civilization civ, Settlement s, int lostType)
        {
            var lost = db.Buildings[lostType];
            if (lost == null || lost.Type == BuildingType.TownCenter) return -1;
            int best = -1, bestScore = int.MinValue;
            for (int i = 0; i < Counts.Buildings; i++)
            {
                if (i == lostType) continue;
                var d = db.Buildings[i];
                if (d == null || d.Category != lost.Category || !RecoveryCatalog.SameRole(lost, d)) continue;
                if (!CanBuildHere(db, civ, s, i) || !RecoveryCatalog.PlacementViable(d, Features(s))) continue;
                int score = (int)d.MinAge;
                if (score > bestScore) { bestScore = score; best = i; }
            }
            return best;
        }

        /// <summary>Same gates the governor applies: availability, tier, population and per-settlement limit.</summary>
        private bool CanBuildHere(GameDatabase db, Civilization civ, Settlement s, int t)
        {
            if (!db.TryGetBuilding((BuildingType)t, out var def)) return false;
            if (def.IsWonder || def.UniquePerCivilization) return false;
            if (!db.IsBuildingAvailable(civ, (BuildingType)t)) return false;
            if (s.Tier < def.MinTier || s.Population < def.MinPopulation) return false;
            if (def.MaxPerSettlement >= 0 && _complete[t] + _building[t] + _queued[t] >= def.MaxPerSettlement) return false;
            return true;
        }

        /// <summary>How much rebuilding the settlement can push per run (industrious cultures push harder).</summary>
        private static float Budget(GameDatabase db, Civilization civ, Settlement s)
        {
            float industrious = AdvisorBoard.Trait(civ, db, AITrait.Industriousness);
            float b = MathUtil.Clamp(0.6f + s.Population / 10f, 0.8f, 3.5f);
            return b * (0.8f + 0.4f * industrious);
        }

        private int Features(Settlement s)
        {
            if (_featuresReady) return _features;
            _featuresReady = true;
            long tick = _state != null ? _state.Tick : 0;
            if (s.Memory.TryGetValue(RecoveryKeys.Features, out float f) && tick - (long)s.Mem(RecoveryKeys.FeaturesTick, -100000f) < SimTime.TicksPerYear)
            {
                _features = (int)f;
                return _features;
            }
            _features = _state != null ? RecoveryCatalog.ScanFeatures(_state.Map, s.Center, s.TerritoryRadius) : 0;
            s.Memory[RecoveryKeys.Features] = _features;
            s.Memory[RecoveryKeys.FeaturesTick] = tick;
            return _features;
        }

        private void Survey(WorldState state, Settlement s)
        {
            Array.Clear(_complete, 0, _complete.Length);
            Array.Clear(_building, 0, _building.Length);
            Array.Clear(_queued, 0, _queued.Length);
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= Counts.Buildings) continue;
                if (b.IsComplete) _complete[t]++; else _building[t]++;
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                var o = q[i];
                if (o == null || (int)o.Type >= Counts.Buildings) continue;
                _queued[(int)o.Type]++;
            }
        }

        private static bool ShouldLog(Settlement s, long tick, float lastCatastropheTick)
        {
            float lastLog = s.Mem(RecoveryKeys.LogTick, -100000f);
            if (tick - lastLog < LogIntervalTicks && lastLog >= lastCatastropheTick) return false;
            s.Memory[RecoveryKeys.LogTick] = tick;
            return true;
        }

        private void UpdateFactors(SimContext ctx)
        {
            if (ctx.Tick == _factorTick) return;
            _factorTick = ctx.Tick;
            float dt = Math.Max(1, ctx.DeltaTicks);
            _lostDecay = HalfLifeFactor(dt, LedgerHalfLifeTicks);
            _scarDecay = HalfLifeFactor(dt, ScarHalfLifeTicks);
            _civDecay = HalfLifeFactor(dt, CivScarHalfLifeTicks);
        }

        private static float HalfLifeFactor(float dt, float halfLife) => MathUtil.Clamp01((float)Math.Exp(-0.693147f * dt / halfLife));
    }
}
