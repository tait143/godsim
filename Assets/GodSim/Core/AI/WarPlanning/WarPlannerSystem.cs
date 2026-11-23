using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Strategic war planner advisor ("war"). Each run (12 ticks) it first plans every civilization once
    /// (phase machine stored in <see cref="WarPlanKeys"/>), then votes on each settlement's buildings and jobs:
    /// <list type="bullet">
    /// <item><b>Peace</b>: no votes. Tension toward a rival comes from a Hostile stance, low and falling opinion,
    /// and aggressive, strong neighbours near our borders.</item>
    /// <item><b>Tension</b>: preparation ramps with time: Barracks/Armory/walls, the weapons chain (Mine, Smelter,
    /// Smith jobs), Soldier jobs and grain/storage stockpiling. Aggression shortens the time to mobilization.</item>
    /// <item><b>Mobilizing</b>: full preparation, an objective is chosen.</item>
    /// <item><b>At war</b>: war economy: Soldier votes high at the frontier nearest the enemy and moderate inside,
    /// weapons production, repairs and walls at threatened settlements; objective and weariness are tracked.</item>
    /// <item><b>Demobilizing</b> (~5 years after peace): negative Soldier and military-building votes and
    /// rebuilding votes ("Swords to ploughshares").</item>
    /// </list>
    /// Deterministic (civs/settlements iterated by index, dictionaries only looked up), allocation-free per run
    /// after warm-up (strings are built only on phase changes or for new rivals).
    /// </summary>
    public sealed class WarPlannerSystem : AdvisorSystemBase
    {
        public const string Id = "war";
        private const string DemobLine = "[war] Swords to ploughshares: standing the army down and rebuilding";

        private static readonly BuildingType[] s_militaryBuildings =
        {
            BuildingType.Barracks, BuildingType.Armory, BuildingType.Watchtower, BuildingType.Palisade,
            BuildingType.StoneWall, BuildingType.Castle, BuildingType.MilitaryBase,
        };

        private sealed class CivPlan
        {
            public long PlannedTick = long.MinValue;
            public WarPhase Phase;
            public int TargetId = -1;
            public float Tension;
            public float Progress;
            public float Weariness;
            public float DemobRemaining;
            public float NearestDist = float.MaxValue;
            public bool Announce;
            public int ReasonTargetId = int.MinValue;
            public string StockpileReason = "[war] Stockpiling grain for the coming war";
            public string WarReason = "[war] War economy";
            public int Runs;
        }

        private readonly Dictionary<int, CivPlan> _plans = new Dictionary<int, CivPlan>();
        private readonly Dictionary<int, string> _opinionKeys = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _trendKeys = new Dictionary<int, string>();
        private readonly int[] _counts = new int[Counts.Buildings];

        /// <inheritdoc />
        public override string AdvisorId => Id;

        /// <inheritdoc />
        public override int TickInterval => 12;

        // ================================================================== settlement advice

        /// <inheritdoc />
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            if (ctx == null || ctx.State == null) return;
            var plan = PlanFor(ctx, civ);
            if (plan.Phase == WarPhase.Peace) { Urgency(s, 0f); return; }

            var state = ctx.State;
            CountBuildings(state, s);
            var target = state.Civilizations.Get(plan.TargetId);
            float dist = target != null && target.IsAlive ? NearestDistance(state, target, s.Center) : float.MaxValue;
            float frontier = dist < float.MaxValue ? WarPlanMath.Frontier01(dist, plan.NearestDist) : 0.5f;
            float soldier = WarPlanMath.SoldierVote(plan.Phase, frontier, plan.Progress, plan.Weariness, plan.DemobRemaining);
            VoteJob(s, civ, JobType.Soldier, soldier);

            switch (plan.Phase)
            {
                case WarPhase.Tension: Prepare(ctx, civ, s, plan, frontier, 0.6f + 0.4f * plan.Progress); break;
                case WarPhase.Mobilizing: Prepare(ctx, civ, s, plan, frontier, 1.2f); break;
                case WarPhase.AtWar: WarEconomy(ctx, civ, s, plan, frontier); break;
                case WarPhase.Demobilizing: Demobilize(ctx, civ, s, plan); break;
            }
            Urgency(s, plan.Phase == WarPhase.Demobilizing ? 0.2f * plan.DemobRemaining : plan.Tension * MathUtil.Lerp(0.5f, 1f, frontier));
        }

        private void Prepare(SimContext ctx, Civilization civ, Settlement s, CivPlan plan, float frontier, float k)
        {
            bool front = frontier >= 0.5f || s.IsCapital;
            if (front) Vote(ctx, civ, s, BuildingType.Barracks, 0.5f * k, 1, null);
            Vote(ctx, civ, s, BuildingType.Armory, 0.4f * k, 1, null);
            if (frontier >= 0.5f) VoteWalls(ctx, civ, s, 0.45f * k);
            // weapons chain
            Vote(ctx, civ, s, BuildingType.Mine, 0.2f * k, 2, null);
            Vote(ctx, civ, s, BuildingType.Smelter, 0.2f * k, 1, null);
            VoteJob(s, civ, JobType.Smith, 0.3f * k);
            VoteJob(s, civ, JobType.Miner, 0.15f * k);
            // food & storage stockpiling for the war
            bool stock = Vote(ctx, civ, s, BuildingType.Granary, 0.6f, 2, null) || Vote(ctx, civ, s, BuildingType.Storehouse, 0.6f, 2, null);
            if (stock && plan.Announce) s.LogAi(plan.StockpileReason);
            Vote(ctx, civ, s, BuildingType.Farm, 0.25f * k, 99, null);
            VoteJob(s, civ, JobType.Farmer, 0.2f * k);
        }

        private void WarEconomy(SimContext ctx, Civilization civ, Settlement s, CivPlan plan, float frontier)
        {
            bool threatened = frontier >= 0.99f || IsThreatened(ctx.State, civ, s);
            // weapons production (stronger when soldiers are unarmed)
            bool shortWeapons = s.Stockpile.Get(ResourceType.Weapons) < 0.5f * Math.Max(1, s.Soldiers);
            if (Vote(ctx, civ, s, BuildingType.Armory, shortWeapons ? 0.8f : 0.5f, 1, null) && plan.Announce) s.LogAi(plan.WarReason);
            Vote(ctx, civ, s, BuildingType.Blacksmith, 0.25f, 1, null);
            Vote(ctx, civ, s, BuildingType.Smelter, 0.25f, 1, null);
            VoteJob(s, civ, JobType.Smith, shortWeapons ? 0.7f : 0.45f);
            VoteJob(s, civ, JobType.Miner, 0.25f);
            if (frontier >= 0.5f) Vote(ctx, civ, s, BuildingType.Barracks, 0.6f, s.Population >= 60 ? 2 : 1, null);
            if (threatened)
            {
                VoteWalls(ctx, civ, s, 0.8f);
                Vote(ctx, civ, s, BuildingType.Watchtower, 0.5f, 2, null);
                int damaged = DamagedBuildings(ctx.State, s);
                if (damaged > 0) VoteJob(s, civ, JobType.Builder, MathUtil.Clamp(0.3f + 0.1f * damaged, 0f, 0.9f));
            }
            // keep the granaries full
            Vote(ctx, civ, s, BuildingType.Granary, 0.3f, 2, null);
            VoteJob(s, civ, JobType.Farmer, 0.3f);
        }

        private void Demobilize(SimContext ctx, Civilization civ, Settlement s, CivPlan plan)
        {
            float r = MathUtil.Lerp(0.3f, 1f, plan.DemobRemaining);
            for (int i = 0; i < s_militaryBuildings.Length; i++)
                VoteBuild(s, civ, s_militaryBuildings[i], -0.5f * r);
            VoteJob(s, civ, JobType.Smith, -0.3f * r);
            if (plan.Announce) s.LogAi(DemobLine);
            Vote(ctx, civ, s, BuildingType.Farm, 0.5f * r, 99, null);
            Vote(ctx, civ, s, BuildingType.House, 0.4f * r, 999, null);
            Vote(ctx, civ, s, BuildingType.Market, 0.3f * r, 1, null);
            Vote(ctx, civ, s, BuildingType.Workshop, 0.3f * r, 2, null);
            VoteJob(s, civ, JobType.Farmer, 0.3f * r);
            VoteJob(s, civ, JobType.Builder, 0.3f * r);
        }

        /// <summary>Votes for a building if available and the settlement has fewer than maxCount (built + queued).</summary>
        private bool Vote(SimContext ctx, Civilization civ, Settlement s, BuildingType type, float strength, int maxCount, string reason)
        {
            if (ctx.Db == null || !ctx.Db.IsBuildingAvailable(civ, type)) return false;
            if (_counts[(int)type] >= maxCount) return false;
            VoteBuild(s, civ, type, strength, reason);
            return true;
        }

        private void VoteWalls(SimContext ctx, Civilization civ, Settlement s, float strength)
        {
            if (!Vote(ctx, civ, s, BuildingType.StoneWall, strength, 1, null)) Vote(ctx, civ, s, BuildingType.Palisade, strength, 1, null);
            if (s.Tier >= SettlementTier.Town) Vote(ctx, civ, s, BuildingType.Castle, strength * 0.6f, 1, null);
        }

        private void CountBuildings(WorldState state, Settlement s)
        {
            Array.Clear(_counts, 0, _counts.Length);
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t >= 0 && t < _counts.Length) _counts[t]++;
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                if (q[i] == null) continue;
                int t = (int)q[i].Type;
                if (t >= 0 && t < _counts.Length) _counts[t]++;
            }
        }

        private static int DamagedBuildings(WorldState state, Settlement s)
        {
            int n = 0;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b != null && !b.IsDestroyed && b.IsComplete && MathUtil.Sanitize(b.Condition, 1f) < 0.8f) n++;
            }
            return n;
        }

        /// <summary>Under siege damage or targeted by an enemy army.</summary>
        private static bool IsThreatened(WorldState state, Civilization civ, Settlement s)
        {
            if (MathUtil.Sanitize(s.Mem("mil.siegeDamage")) > 0f) return true;
            var armies = state.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                if (a == null || !a.IsAlive || a.CivilizationId == civ.Id || a.TargetSettlementId != s.Id) continue;
                if (state.Diplomacy.AreAtWar(a.CivilizationId, civ.Id)) return true;
            }
            return false;
        }

        // ================================================================== civilization planning

        private CivPlan PlanFor(SimContext ctx, Civilization civ)
        {
            if (!_plans.TryGetValue(civ.Id, out var plan))
            {
                plan = new CivPlan();
                _plans[civ.Id] = plan;
            }
            if (plan.PlannedTick != ctx.State.Tick)
            {
                plan.PlannedTick = ctx.State.Tick;
                PlanCivilization(ctx, civ, plan);
            }
            return plan;
        }

        private void PlanCivilization(SimContext ctx, Civilization civ, CivPlan plan)
        {
            var state = ctx.State;
            long tick = state.Tick;
            float dtYears = Math.Max(1, ctx.DeltaTicks) / (float)SimTime.TicksPerYear;
            float aggression = AdvisorBoard.Trait(civ, ctx.Db, AITrait.Aggression);
            float diplomacy = AdvisorBoard.Trait(civ, ctx.Db, AITrait.Diplomacy);
            plan.Runs++;

            WarPhase prev = WarPlanKeys.PhaseOf(civ);
            int prevTarget = WarPlanKeys.TargetOf(civ);

            // ---- active wars
            int enemyId = -1, ownLosses = 0;
            long warStart = long.MaxValue;
            float bestEnemyDist = float.MaxValue, enemyScore = 0f;
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null || !w.IsActive) continue;
                bool att = w.AttackerId == civ.Id;
                if (!att && w.DefenderId != civ.Id) continue;
                var enemy = state.Civilizations.Get(att ? w.DefenderId : w.AttackerId);
                if (enemy == null || !enemy.IsAlive || enemy.Id == civ.Id) continue;
                ownLosses += Math.Max(0, att ? w.AttackerLosses : w.DefenderLosses);
                if (w.StartTick < warStart) warStart = w.StartTick;
                float d = CivDistance(state, civ, enemy);
                if (enemyId < 0 || d < bestEnemyDist)
                {
                    bestEnemyDist = d; enemyId = enemy.Id;
                    enemyScore = MathUtil.Clamp(MathUtil.Sanitize(w.WarScore), -100f, 100f) * (att ? 1f : -1f);
                }
            }

            // ---- tension toward rivals (also refreshes opinion trends)
            int rivalId = -1;
            float rivalTension = 0f, rivalDist = float.MaxValue;
            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var o = civs[i];
                if (o == null || !o.IsAlive || o.Id == civ.Id) continue;
                if (!state.Diplomacy.TryGet(civ.Id, o.Id, out var r)) continue;
                float opinion = MathUtil.Clamp(MathUtil.Sanitize(r.Opinion), -100f, 100f);
                string opKey = CachedKey(_opinionKeys, o.Id, WarPlanKeys.OpinionPrefix);
                string trKey = CachedKey(_trendKeys, o.Id, WarPlanKeys.TrendPrefix);
                float trend = MathUtil.Sanitize(civ.Mem(trKey));
                if (civ.Memory.TryGetValue(opKey, out float last))
                {
                    float inst = (opinion - MathUtil.Sanitize(last, opinion)) / dtYears;
                    trend = MathUtil.Clamp(MathUtil.Lerp(trend, inst, 0.3f), -200f, 200f);
                }
                civ.Memory[opKey] = opinion;
                civ.Memory[trKey] = MathUtil.Sanitize(trend);
                if (!r.HasContact && !r.AtWar) continue;

                float dist = CivDistance(state, civ, o);
                float ratio = (Math.Max(0f, MathUtil.Sanitize(o.MilitaryPower)) + 1f) / (Math.Max(0f, MathUtil.Sanitize(civ.MilitaryPower)) + 1f);
                float t = WarPlanMath.Tension(r.Stance, opinion, trend, AdvisorBoard.Trait(o, ctx.Db, AITrait.Aggression),
                                              WarPlanMath.Proximity(dist), ratio, r.TruceUntilTick > tick);
                if (t > rivalTension) { rivalTension = t; rivalId = o.Id; rivalDist = dist; }
            }

            // ---- phase machine
            WarPhase next = prev;
            int target = prevTarget;
            if (enemyId >= 0)
            {
                next = WarPhase.AtWar;
                target = enemyId;
            }
            else
            {
                switch (prev)
                {
                    case WarPhase.Peace:
                        if (rivalTension >= WarPlanMath.TensionEnter) { next = WarPhase.Tension; target = rivalId; }
                        else target = -1;
                        break;
                    case WarPhase.Tension:
                        if (rivalTension < WarPlanMath.TensionLeave) { next = WarPhase.Peace; target = -1; }
                        else
                        {
                            target = rivalId;
                            float years = SimTime.TicksToYears(tick - PhaseTick(civ, tick));
                            if (rivalTension >= WarPlanMath.MobilizeTension || years >= WarPlanMath.MobilizeDelayYears(aggression))
                                next = WarPhase.Mobilizing;
                        }
                        break;
                    case WarPhase.Mobilizing:
                        if (rivalTension < WarPlanMath.TensionLeave) { next = WarPhase.Demobilizing; StartDemob(civ, tick, WarPlanMath.StandDownYears); }
                        else target = rivalId;
                        break;
                    case WarPhase.AtWar:
                        next = WarPhase.Demobilizing;
                        StartDemob(civ, tick, WarPlanMath.DemobilizeYears);
                        break;
                    case WarPhase.Demobilizing:
                        if (rivalTension >= WarPlanMath.RearmTension) { next = WarPhase.Tension; target = rivalId; }
                        else if (tick >= (long)MathUtil.Sanitize(civ.Mem(WarPlanKeys.DemobUntil))) { next = WarPhase.Peace; target = -1; }
                        break;
                }
            }

            bool changed = next != prev;
            if (changed)
            {
                civ.Memory[WarPlanKeys.PhaseTick] = tick;
                if (next == WarPhase.AtWar)
                {
                    civ.Memory[WarPlanKeys.WarStart] = warStart != long.MaxValue ? warStart : tick;
                    civ.Memory[WarPlanKeys.PopulationAtStart] = Math.Max(1, CivPopulation(state, civ) + ownLosses);
                }
            }
            civ.Memory[WarPlanKeys.Phase] = (float)(int)next;
            civ.Memory[WarPlanKeys.Target] = target;

            // ---- tension, progress, weariness, demob
            plan.Phase = next;
            plan.TargetId = target;
            plan.Tension = next == WarPhase.AtWar ? 1f : (target == rivalId ? rivalTension : 0f);
            civ.Memory[WarPlanKeys.Tension] = plan.Tension;
            plan.Progress = next == WarPhase.Tension
                ? MathUtil.Clamp01(SimTime.TicksToYears(tick - PhaseTick(civ, tick)) / WarPlanMath.MobilizeDelayYears(aggression))
                : (next == WarPhase.Mobilizing || next == WarPhase.AtWar ? 1f : 0f);

            float weariness = WarPlanKeys.WearinessOf(civ);
            if (next == WarPhase.AtWar)
            {
                int pop0 = (int)Math.Max(1f, MathUtil.Sanitize(civ.Mem(WarPlanKeys.PopulationAtStart, 1f), 1f));
                long start = warStart != long.MaxValue ? warStart : (long)MathUtil.Sanitize(civ.Mem(WarPlanKeys.WarStart, tick), tick);
                weariness = WarPlanMath.Weariness(ownLosses, pop0, SimTime.TicksToYears(Math.Max(0L, tick - start)), enemyScore, aggression, diplomacy);
            }
            else weariness = Math.Max(0f, weariness - WarPlanMath.WearinessDecayPerYear * dtYears);
            plan.Weariness = MathUtil.Clamp01(MathUtil.Sanitize(weariness));
            civ.Memory[WarPlanKeys.Weariness] = plan.Weariness;

            if (next == WarPhase.Demobilizing)
            {
                float len = Math.Max(1f, MathUtil.Sanitize(civ.Mem(WarPlanKeys.DemobLength, 1f), 1f));
                float left = MathUtil.Sanitize(civ.Mem(WarPlanKeys.DemobUntil)) - tick;
                plan.DemobRemaining = MathUtil.Clamp01(left / len);
            }
            else plan.DemobRemaining = 0f;

            // ---- frontier reference and objective
            var targetCiv = state.Civilizations.Get(target);
            plan.NearestDist = targetCiv != null && targetCiv.IsAlive ? (target == enemyId ? bestEnemyDist : (target == rivalId ? rivalDist : CivDistance(state, civ, targetCiv))) : float.MaxValue;
            int objective = -1;
            if ((next == WarPhase.AtWar || next == WarPhase.Mobilizing) && targetCiv != null && targetCiv.IsAlive)
                objective = ChooseObjective(state, civ, targetCiv);
            civ.Memory[WarPlanKeys.Objective] = objective;

            // ---- narration (strings only on change)
            if (targetCiv != null && plan.ReasonTargetId != targetCiv.Id)
            {
                string enemyName = EnemyName(ctx.Db, targetCiv);
                plan.StockpileReason = "[war] Stockpiling grain for the coming war with the " + enemyName;
                plan.WarReason = "[war] War economy: forging weapons for the war with the " + enemyName;
                plan.ReasonTargetId = targetCiv.Id;
            }
            plan.Announce = changed || plan.Runs % 8 == 1;
            if (changed) Narrate(ctx, civ, next, targetCiv);
        }

        private static long PhaseTick(Civilization civ, long fallback) => (long)MathUtil.Sanitize(civ.Mem(WarPlanKeys.PhaseTick, fallback), fallback);

        private static void StartDemob(Civilization civ, long tick, float years)
        {
            long len = Math.Max(1L, SimTime.YearsToTicks(years));
            civ.Memory[WarPlanKeys.DemobUntil] = tick + len;
            civ.Memory[WarPlanKeys.DemobLength] = len;
        }

        /// <summary>Nearest weak enemy settlement: distance from our nearest settlement, inflated by its defense.</summary>
        private static int ChooseObjective(WorldState state, Civilization civ, Civilization enemy)
        {
            int best = -1;
            float bestScore = float.MaxValue;
            float power = Math.Max(1f, MathUtil.Sanitize(civ.MilitaryPower));
            var ids = enemy.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var t = state.Settlements.Get(ids[i]);
                if (t == null || !t.IsAlive || t.CivilizationId != enemy.Id) continue;
                float d = NearestDistance(state, civ, t.Center);
                if (d >= float.MaxValue) continue;
                float score = WarPlanMath.ObjectiveScore(d, t.DefenseStrength, power);
                if (score < bestScore || (score == bestScore && t.Id < best)) { bestScore = score; best = t.Id; }
            }
            return best;
        }

        private static void Narrate(SimContext ctx, Civilization civ, WarPhase phase, Civilization target)
        {
            var cap = ctx.State.Settlements.Get(civ.CapitalId);
            string enemy = target != null ? "the " + EnemyName(ctx.Db, target) : "their rivals";
            string text;
            switch (phase)
            {
                case WarPhase.Tension: text = "war: tension with " + enemy + "; preparing for war"; break;
                case WarPhase.Mobilizing: text = "war: mobilizing against " + enemy; break;
                case WarPhase.AtWar: text = "war: at war with " + enemy; break;
                case WarPhase.Demobilizing: text = "war: demobilizing (swords to ploughshares)"; break;
                default: text = "war: peace"; break;
            }
            if (cap != null && cap.IsAlive) cap.LogAi(text);
            if (ctx.Ops != null && phase == WarPhase.Mobilizing)
                ctx.Ops.AddChronicle(ChronicleCategory.War, "The " + civ.Name + " are mobilizing for war against " + enemy + ".", civ.Id, cap != null ? cap.Id : -1, cap != null ? cap.Center : Vec2i.Invalid, 1);
        }

        private static string EnemyName(GameDatabase db, Civilization c)
        {
            if (db != null && (int)c.Race < db.Races.Count)
            {
                var r = db.Races[(int)c.Race];
                if (r != null && !string.IsNullOrEmpty(r.PluralName)) return r.PluralName;
            }
            return string.IsNullOrEmpty(c.Name) ? "enemy" : c.Name;
        }

        // ================================================================== geometry

        private static float NearestDistance(WorldState state, Civilization c, Vec2i from)
        {
            int best = int.MaxValue;
            var ids = c.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s == null || !s.IsAlive || s.CivilizationId != c.Id) continue;
                int d = s.Center.DistanceSqTo(from);
                if (d < best) best = d;
            }
            return best == int.MaxValue ? float.MaxValue : (float)Math.Sqrt(best);
        }

        private static float CivDistance(WorldState state, Civilization a, Civilization b)
        {
            float best = float.MaxValue;
            var ids = a.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s == null || !s.IsAlive || s.CivilizationId != a.Id) continue;
                float d = NearestDistance(state, b, s.Center);
                if (d < best) best = d;
            }
            return best;
        }

        private static int CivPopulation(WorldState state, Civilization c)
        {
            int n = 0;
            var ids = c.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s != null && s.IsAlive) n += s.Population;
            }
            return n;
        }

        private static string CachedKey(Dictionary<int, string> cache, int id, string prefix)
        {
            if (cache.TryGetValue(id, out var k)) return k;
            if (cache.Count >= 4096) cache.Clear();
            k = prefix + id;
            cache[id] = k;
            return k;
        }
    }
}
