using System;

namespace GodSim.Core.AI
{
    /// <summary>Per-settlement threat assessment produced by <see cref="DefenseAdvisorThreatModel"/> (value type, reused).</summary>
    public struct DefenseAdvisorThreat
    {
        /// <summary>Overall threat 0..1 (soft-or of every component).</summary>
        public float Threat;
        /// <summary>How soon the danger arrives 0..1 (army closing in, siege, fresh battle).</summary>
        public float Imminence;
        /// <summary>Our defense / force we expect to face (0..5).</summary>
        public float DefenseRatio;
        /// <summary>Defense strength we expect to need.</summary>
        public float Required;
        /// <summary>Our current (plus planned) defense estimate.</summary>
        public float OurDefense;
        /// <summary>0 deep interior .. 1 borders another civilization.</summary>
        public float Frontier;

        /// <summary>Threat from hostile / warring / aggressive neighbour settlements.</summary>
        public float SettlementThreat;
        /// <summary>Threat from hostile armies within <see cref="DefenseAdvisorKeys.ArmyRadius"/>.</summary>
        public float ArmyThreat;
        /// <summary>Threat from recent battles at this settlement.</summary>
        public float BattleThreat;
        /// <summary>Civ-wide shock after losing a settlement.</summary>
        public float ShockThreat;
        /// <summary>Extra threat because a hostile neighbour is arming fast.</summary>
        public float ArmsRaceThreat;

        /// <summary>Hostile neighbour power projected onto this settlement.</summary>
        public float EnemyPower;
        /// <summary>Summed hostility-weighted strength of hostile armies nearby.</summary>
        public float ArmyPowerNear;

        /// <summary>Our civilization is in an active war.</summary>
        public bool AtWar;
        /// <summary>A hostile army is besieging this settlement (or siege damage is fresh).</summary>
        public bool Besieged;
        /// <summary>The nearest hostile army is heading here or closing in.</summary>
        public bool ArmyApproaching;

        /// <summary>Distance to the nearest hostile army, -1 if none.</summary>
        public int NearestArmyDist;
        /// <summary>Civilization of that army, -1 if none.</summary>
        public int NearestArmyCivId;
        /// <summary>Estimated ticks until it arrives, -1 if unknown / not approaching.</summary>
        public int ArmyEtaTicks;

        /// <summary>Distance to the most threatening neighbour settlement, -1 if none.</summary>
        public int HostileDist;
        /// <summary>Civilization of that settlement, -1 if none.</summary>
        public int HostileCivId;
        /// <summary>We are at war with that civilization.</summary>
        public bool HostileAtWar;

        /// <summary>1 right after a battle here, fading to 0 over <see cref="DefenseAdvisorKeys.BattleMemoryTicks"/>.</summary>
        public float BattleRecency;
        /// <summary>Recency-weighted soldiers lost here.</summary>
        public float BattleLosses;
        /// <summary>Latest battle here was lost.</summary>
        public bool BattleLost;
        /// <summary>Ticks since the latest battle here, -1 if none remembered.</summary>
        public int TicksSinceBattle;
    }

    /// <summary>
    /// The defense advisor's world model: for one settlement it measures hostile neighbours (war, Hostile stance,
    /// aggressive races, bad opinion), hostile armies within 30 tiles and their approach trend, recent battles,
    /// civ-wide shocks, frontier exposure and the balance of our defense against the force we expect to face.
    /// Deterministic (index iteration only), allocation-free; it updates only its own "adv.def.*" trend keys.
    /// </summary>
    public sealed class DefenseAdvisorThreatModel
    {
        /// <summary>
        /// How hostile <paramref name="them"/> is toward <paramref name="us"/>, 0..1: 1 at war, ~0.7 Hostile stance,
        /// otherwise driven by their aggression, our xenophobia, opinion, friendship and truces.
        /// </summary>
        public static float Hostility(WorldState state, GameDatabase db, Civilization us, Civilization them, float ourXenophobia)
        {
            if (state == null || us == null || them == null || !them.IsAlive || them.Id == us.Id) return 0f;
            var dip = state.Diplomacy;
            if (dip.AreAtWar(us.Id, them.Id)) return 1f;
            float aggression = AdvisorBoard.Trait(them, db, AITrait.Aggression);
            float h = 0.32f * MathUtil.SmoothStep(0.35f, 0.9f, aggression);
            h += (them.Race != us.Race ? 0.15f : 0.05f) * MathUtil.Clamp01(ourXenophobia);
            if (dip.TryGet(us.Id, them.Id, out var rel) && rel != null)
            {
                h += 0.25f * MathUtil.Clamp01(-MathUtil.Sanitize(rel.Opinion) / 100f);
                switch (rel.Stance)
                {
                    case DiplomaticStance.War: return 1f;
                    case DiplomaticStance.Hostile: h = Math.Max(h, 0.6f) + 0.1f; break;
                    case DiplomaticStance.Friendly: h *= 0.35f; break;
                    case DiplomaticStance.Allied: h = 0f; break;
                }
                if (rel.TruceUntilTick > state.Tick) h *= 0.5f;
            }
            return MathUtil.Clamp01(MathUtil.Sanitize(h));
        }

        /// <summary>True if the civilization takes part in any active war.</summary>
        public static bool CivAtWar(WorldState state, int civId)
        {
            if (state == null) return false;
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w != null && w.IsActive && (w.AttackerId == civId || w.DefenderId == civId)) return true;
            }
            return false;
        }

        /// <summary>
        /// Assesses one settlement. <paramref name="builtDefense"/> is the defense of completed buildings (used when
        /// the military module has not computed DefenseStrength yet); <paramref name="plannedDefense"/> is the value
        /// of queued / unfinished defensive buildings (counted at 60% so the AI does not over-order walls).
        /// When <paramref name="commit"/> is true the approach-trend and arms-race keys are updated.
        /// </summary>
        public void Assess(SimContext ctx, Civilization civ, Settlement s, float xenophobia, float builtDefense, float plannedDefense, bool commit, out DefenseAdvisorThreat t)
        {
            t = default(DefenseAdvisorThreat);
            t.NearestArmyDist = -1; t.NearestArmyCivId = -1; t.ArmyEtaTicks = -1;
            t.HostileDist = -1; t.HostileCivId = -1; t.TicksSinceBattle = -1;
            if (ctx == null || ctx.State == null || civ == null || s == null) return;
            var state = ctx.State;
            var db = ctx.Db;
            long tick = state.Tick;
            int dt = Math.Max(1, ctx.DeltaTicks);
            int pop = s.Population;
            var mem = s.Memory;

            // ---------------------------------------------------------------- our defense
            float siege = MathUtil.Clamp(MathUtil.Sanitize(s.Mem(DefenseAdvisorKeys.ExternalSiegeDamage)), 0f, 0.9f);
            float svc = Math.Max(0f, MathUtil.Sanitize(s.ServiceOutput[(int)ServiceType.Defense]));
            float measured = Math.Max(Math.Max(0f, MathUtil.Sanitize(s.DefenseStrength)), svc * (1f - siege) + Math.Max(0, s.Soldiers));
            float ourDef = Math.Max(measured, Math.Max(0f, MathUtil.Sanitize(builtDefense)) * (1f - siege)) + 0.6f * Math.Max(0f, MathUtil.Sanitize(plannedDefense));
            t.OurDefense = MathUtil.Clamp(ourDef, 0f, 1e9f);
            t.AtWar = CivAtWar(state, civ.Id);

            // ---------------------------------------------------------------- neighbour settlements
            var civs = state.Civilizations;
            var settlements = state.Settlements.Items;
            float nearestForeign = 1e6f;
            float setThreat = 0f, enemyPow = 0f;
            float reach = DefenseAdvisorKeys.SettlementReach, near = DefenseAdvisorKeys.SettlementNear;
            for (int i = 0; i < settlements.Count; i++)
            {
                var o = settlements[i];
                if (o == null || !o.IsAlive || o.CivilizationId == civ.Id) continue;
                float d = s.Center.DistanceTo(o.Center);
                if (d < nearestForeign) nearestForeign = d;
                if (d > reach) continue;
                var oc = civs.Get(o.CivilizationId);
                if (oc == null || !oc.IsAlive) continue;
                float h = Hostility(state, db, civ, oc, xenophobia);
                if (h < 0.01f) continue;
                float prox = MathUtil.Clamp01((reach - d) / (reach - near));
                float th = h * prox;
                if (th > setThreat)
                {
                    setThreat = th;
                    t.HostileCivId = oc.Id;
                    t.HostileDist = (int)d;
                    t.HostileAtWar = h >= 0.999f;
                }
                float pw = th * Math.Max(0f, MathUtil.Sanitize(oc.MilitaryPower)) * DefenseAdvisorKeys.ProjectionFraction;
                if (pw > enemyPow) enemyPow = pw;
            }
            t.SettlementThreat = MathUtil.Clamp01(setThreat);
            t.EnemyPower = MathUtil.Clamp(enemyPow, 0f, 1e9f);

            // ---------------------------------------------------------------- frontier
            int r = Math.Max(1, s.TerritoryRadius);
            float frontier = 1f - MathUtil.SmoothStep(r + 10f, r + 40f, nearestForeign);
            if (frontier < 1f && BorderTouchesForeign(state, civ, s)) frontier = 1f;
            t.Frontier = MathUtil.Clamp01(frontier);

            // ---------------------------------------------------------------- armies
            float armyRadius = DefenseAdvisorKeys.ArmyRadius;
            float armyThreat = 0f, armyPow = 0f;
            float nearestD = 1e6f, nearestH = 0f, nearestProx = 0f;
            bool nearestHeading = false;
            var armies = state.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                if (a == null || !a.IsAlive || a.State == ArmyState.Disbanding || a.CivilizationId == civ.Id) continue;
                float d = a.Position.DistanceTo(s.Center);
                if (d > armyRadius) continue;
                bool targetsUs = a.TargetSettlementId == s.Id;
                var ac = civs.Get(a.CivilizationId);
                float h = ac != null ? Hostility(state, db, civ, ac, xenophobia) : 0f;
                if (targetsUs) h = Math.Max(h, 0.9f);
                if (h < 0.05f) continue;
                bool besieging = a.State == ArmyState.Besieging && (targetsUs || d <= 4f);
                bool heading = targetsUs || (a.State == ArmyState.Marching && a.Destination.DistanceTo(s.Center) <= 6f);
                float prox = MathUtil.Clamp01((armyRadius - d) / (armyRadius - 4f));
                float th = h * (0.35f + 0.65f * prox);
                if (a.State == ArmyState.Retreating) th *= 0.3f;
                else if (!heading) th *= 0.6f;
                if (besieging) { th = 1f; t.Besieged = true; }
                armyPow += h * Math.Max(0f, MathUtil.Sanitize(a.Strength));
                if (th > armyThreat) armyThreat = th;
                if (d < nearestD)
                {
                    nearestD = d; nearestH = h; nearestProx = prox; nearestHeading = heading || besieging;
                    t.NearestArmyCivId = a.CivilizationId;
                }
            }
            if (siege > 0.05f && armyThreat > 0f) t.Besieged = true;
            t.ArmyPowerNear = MathUtil.Clamp(armyPow, 0f, 1e9f);

            // approach trend (distance memory) -> anticipation
            if (t.NearestArmyCivId >= 0)
            {
                t.NearestArmyDist = (int)nearestD;
                float prev = s.Mem(DefenseAdvisorKeys.ArmyDist, -1f);
                float closing = 0f;
                if (prev >= 0f)
                {
                    float raw = MathUtil.Clamp((prev - nearestD) / dt, -10f, 10f);
                    closing = 0.5f * MathUtil.Sanitize(s.Mem(DefenseAdvisorKeys.ArmyClosing)) + 0.5f * raw;
                }
                t.ArmyApproaching = nearestHeading || closing > 0.02f;
                if (closing > 0.02f)
                {
                    t.ArmyEtaTicks = (int)MathUtil.Clamp(nearestD / closing, 0f, 100000f);
                    // the trend says it is coming even if its orders do not: count it with full weight
                    armyThreat = Math.Max(armyThreat, nearestH * (0.35f + 0.65f * nearestProx));
                    if (t.ArmyEtaTicks <= 36) armyThreat = Math.Max(armyThreat, 0.85f * nearestH);
                }
                if (commit)
                {
                    mem[DefenseAdvisorKeys.ArmyDist] = MathUtil.Clamp(nearestD, 0f, 1e5f);
                    if (Math.Abs(closing) > 0.001f) mem[DefenseAdvisorKeys.ArmyClosing] = MathUtil.Clamp(MathUtil.Sanitize(closing), -10f, 10f);
                    else mem.Remove(DefenseAdvisorKeys.ArmyClosing);
                }
            }
            else if (commit)
            {
                mem.Remove(DefenseAdvisorKeys.ArmyDist);
                mem.Remove(DefenseAdvisorKeys.ArmyClosing);
            }
            t.ArmyThreat = MathUtil.Clamp01(armyThreat);

            // ---------------------------------------------------------------- recent battles here
            float bt = s.Mem(DefenseAdvisorKeys.BattleTick, -1f);
            if (bt >= 0f)
            {
                float age = Math.Max(0f, tick - bt);
                float rec = MathUtil.Clamp01(1f - age / DefenseAdvisorKeys.BattleMemoryTicks);
                if (rec <= 0f)
                {
                    if (commit)
                    {
                        mem.Remove(DefenseAdvisorKeys.BattleTick);
                        mem.Remove(DefenseAdvisorKeys.BattleLosses);
                        mem.Remove(DefenseAdvisorKeys.BattleLost);
                    }
                }
                else
                {
                    t.BattleRecency = rec;
                    t.TicksSinceBattle = (int)age;
                    t.BattleLosses = Math.Max(0f, MathUtil.Sanitize(s.Mem(DefenseAdvisorKeys.BattleLosses)));
                    t.BattleLost = s.Mem(DefenseAdvisorKeys.BattleLost) > 0.5f;
                    float lossF = MathUtil.Clamp01(t.BattleLosses / Math.Max(4f, pop * 0.15f));
                    t.BattleThreat = MathUtil.Clamp01(rec * (0.4f + 0.35f * lossF + (t.BattleLost ? 0.2f : 0f)));
                }
            }

            // ---------------------------------------------------------------- civ-wide shock (lost a settlement)
            float shockTick = civ.Mem(DefenseAdvisorKeys.CivShockTick, -1f);
            if (shockTick >= 0f)
            {
                float rec = MathUtil.Clamp01(1f - Math.Max(0f, tick - shockTick) / DefenseAdvisorKeys.ShockMemoryTicks);
                t.ShockThreat = 0.3f * rec * (0.5f + 0.5f * t.Frontier);
            }

            // ---------------------------------------------------------------- arms race (anticipation)
            float prevPow = s.Mem(DefenseAdvisorKeys.EnemyPower, -1f);
            if (prevPow > 0.5f && enemyPow > prevPow * 1.1f && setThreat > 0.05f)
                t.ArmsRaceThreat = 0.12f * MathUtil.Clamp01((enemyPow - prevPow) / prevPow);
            if (commit)
            {
                if (enemyPow > 0.01f) mem[DefenseAdvisorKeys.EnemyPower] = MathUtil.Clamp(prevPow > 0f ? 0.7f * prevPow + 0.3f * enemyPow : enemyPow, 0f, 1e9f);
                else mem.Remove(DefenseAdvisorKeys.EnemyPower);
            }

            // ---------------------------------------------------------------- combine
            float vigilance = t.Frontier * (0.12f + 0.18f * MathUtil.Clamp01(xenophobia));
            float warFloor = t.AtWar ? 0.12f + 0.2f * t.Frontier : 0f;
            float calm = (1f - t.SettlementThreat) * (1f - t.ArmyThreat) * (1f - t.BattleThreat) * (1f - t.ShockThreat)
                         * (1f - t.ArmsRaceThreat) * (1f - vigilance) * (1f - warFloor);
            float threat = 1f - MathUtil.Clamp01(calm);
            if (t.Besieged) threat = Math.Max(threat, 0.95f);
            t.Threat = MathUtil.Clamp01(MathUtil.Sanitize(threat));

            float imm = t.ArmyApproaching || t.Besieged ? t.ArmyThreat : 0.6f * t.ArmyThreat;
            if (t.BattleRecency > 0f) imm = Math.Max(imm, 0.8f * t.BattleThreat);
            if (t.HostileAtWar && t.HostileDist >= 0 && t.HostileDist <= 20) imm = Math.Max(imm, 0.45f);
            if (t.Besieged) imm = 1f;
            t.Imminence = MathUtil.Clamp01(MathUtil.Sanitize(imm));

            // ---------------------------------------------------------------- force balance
            float baseline = t.Threat * (3f + 0.12f * pop);
            float required = Math.Max(baseline, Math.Max(armyPow * 1.15f, enemyPow));
            t.Required = MathUtil.Clamp(MathUtil.Sanitize(required), 0f, 1e9f);
            t.DefenseRatio = MathUtil.Clamp(MathUtil.Sanitize(t.OurDefense / Math.Max(0.5f, t.Required)), 0f, 5f);
        }

        /// <summary>Samples a ring just outside the territory; true if any tile belongs to another civilization.</summary>
        private static bool BorderTouchesForeign(WorldState state, Civilization civ, Settlement s)
        {
            var map = state.Map;
            if (map == null || map.Owner == null) return false;
            int rad = MathUtil.Clamp(s.TerritoryRadius + 2, 2, 64);
            int cx = s.Center.X, cy = s.Center.Y;
            int lastOwner = int.MinValue;
            bool lastForeign = false;
            for (int k = -rad; k <= rad; k += 2)
            {
                if (IsForeign(state, map, civ.Id, cx + k, cy - rad, ref lastOwner, ref lastForeign)) return true;
                if (IsForeign(state, map, civ.Id, cx + k, cy + rad, ref lastOwner, ref lastForeign)) return true;
                if (IsForeign(state, map, civ.Id, cx - rad, cy + k, ref lastOwner, ref lastForeign)) return true;
                if (IsForeign(state, map, civ.Id, cx + rad, cy + k, ref lastOwner, ref lastForeign)) return true;
            }
            return false;
        }

        private static bool IsForeign(WorldState state, WorldMap map, int civId, int x, int y, ref int lastOwner, ref bool lastForeign)
        {
            if (!map.InBounds(x, y)) return false;
            int owner = map.Owner[map.Index(x, y)];
            if (owner < 0) return false;
            if (owner == lastOwner) return lastForeign;
            lastOwner = owner;
            var o = state.Settlements.Get(owner);
            lastForeign = o != null && o.IsAlive && o.CivilizationId != civId && o.CivilizationId >= 0;
            return lastForeign;
        }
    }
}
