using System;
using System.Collections.Generic;

namespace GodSim.Core.Diplomacy
{
    public sealed partial class ArmySystem
    {
        // =============================================================== civilization decisions
        private void DecideCivilizations(SimContext ctx, long tick)
        {
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (!civ.IsAlive || !Due(tick, civ.Id, DecisionInterval)) continue;
                DecideCivilization(ctx, civ);
            }
        }

        /// <summary>Raises at most one army per decision, for the oldest active war that offers a viable source and target.</summary>
        private void DecideCivilization(SimContext ctx, Civilization civ)
        {
            int maxArmies = ctx.Config != null ? ctx.Config.MaxArmies : 256;
            var wars = ctx.State.Diplomacy.Wars;
            for (int w = 0; w < wars.Count; w++)
            {
                if (_aliveArmies >= maxArmies) return;
                var war = wars[w];
                if (war == null || !war.IsActive) continue;
                bool isAttacker = war.AttackerId == civ.Id;
                if (!isAttacker && war.DefenderId != civ.Id) continue;
                var enemy = ctx.State.Civilizations.Get(isAttacker ? war.DefenderId : war.AttackerId);
                if (enemy == null || !enemy.IsAlive || enemy.Id == civ.Id) continue;
                if (TryRaise(ctx, civ, enemy, war, isAttacker)) return;
            }
        }

        private bool TryRaise(SimContext ctx, Civilization civ, Civilization enemy, War war, bool isAttacker)
        {
            var state = ctx.State;
            long tick = state.Tick;

            // Source: own living settlement nearest to the enemy with enough free soldiers and no army out.
            Settlement source = null;
            int bestDist = int.MaxValue;
            var own = civ.SettlementIds;
            for (int i = 0; i < own.Count; i++)
            {
                var s = state.Settlements.Get(own[i]);
                if (s == null || !s.IsAlive || s.CivilizationId != civ.Id) continue;
                if (HasActiveArmy(state, s)) continue;
                if (s.Mem(MilitaryMath.KeyRestUntil, -1f) > tick) continue;
                if (CountFreeSoldiers(state, s) < MilitaryMath.MinSoldiersToRaise) continue;
                int d = NearestEnemyDistSq(state, enemy, s.Center);
                if (d < bestDist) { bestDist = d; source = s; }
            }
            if (source == null) return false;

            MilitaryMath.SplitResidents(state, source, _raise, null);
            int n = MilitaryMath.RaiseCount(_raise.Count);
            if (n <= 0) return false;
            float per = MilitaryMath.SoldierStrength(ctx.Db, civ, source.Stockpile, Math.Max(source.Soldiers, _raise.Count));
            float projected = n * per;
            float minOdds = isAttacker ? MilitaryMath.MinAttackOdds(RaceAggression(ctx.Db, civ.Race)) : -1f;

            var target = ChooseTarget(state, enemy, source.Center, projected, source, minOdds, _pathBuffer);
            if (target == null) return false;

            if (_raise.Count > n) _raise.RemoveRange(n, _raise.Count - n);
            var army = ctx.Ops.CreateArmy(civ, source, _raise);
            _raise.Clear();
            if (army == null) return false;
            _aliveArmies++;
            army.WarId = war.Id;
            army.TargetSettlementId = target.Id;
            army.Destination = target.Center;
            army.State = ArmyState.Marching;
            army.Morale = 1f;
            army.MoveProgress = 0f;
            army.Path.Clear();
            army.Path.AddRange(_pathBuffer);
            army.PathIndex = 0;
            if (army.Path.Count > 0) army.Position = army.Path[0];
            source.Memory[MilitaryMath.KeyArmyId] = army.Id;
            source.Memory[MilitaryMath.KeyArmyInitialSoldiers] = army.SoldierIds.Count;
            if (army.SoldierIds.Count == 0) { Disband(ctx, army); return false; }
            RecomputeStrength(ctx, army, civ);
            SyncSoldiers(state, army);
            source.LogAi("mil: raised army #" + army.Id + " (" + army.SoldierIds.Count + " soldiers) against " + target.Name);
            return true;
        }

        /// <summary>
        /// Scores the enemy's settlements (nearer and weaker is better) and returns the best one that is
        /// reachable, writing its path. <paramref name="minOdds"/> &gt;= 0 is the attacker's minimum
        /// projected/defense ratio; a negative value means "counter-attack only when stronger".
        /// A null <paramref name="source"/> (field re-targeting) disables the unreachable caches.
        /// </summary>
        private Settlement ChooseTarget(WorldState state, Civilization enemy, Vec2i from, float projected, Settlement source, float minOdds, List<Vec2i> outPath)
        {
            long tick = state.Tick;
            int noPathTarget = -1;
            if (source != null && source.Mem(MilitaryMath.KeyNoPathUntil, -1f) > tick) noPathTarget = (int)MathUtil.Sanitize(source.Mem(MilitaryMath.KeyNoPathTarget, -1f), -1f);
            outPath.Clear();
            _candidates.Clear();
            _candidateScores.Clear();
            var ids = enemy.SettlementIds;
            var settlements = state.Settlements;
            for (int i = 0; i < ids.Count; i++)
            {
                var t = settlements.Get(ids[i]);
                if (t == null || !t.IsAlive || t.CivilizationId != enemy.Id || t.Id == noPathTarget) continue;
                float def = Math.Max(0f, MathUtil.Sanitize(t.DefenseStrength));
                if (minOdds >= 0f) { if (projected < minOdds * def) continue; }
                else if (projected <= def) continue;
                _candidates.Add(t);
                _candidateScores.Add(MilitaryMath.TargetScore(projected, def, from.DistanceTo(t.Center)));
            }
            for (int attempt = 0; attempt < MaxTargetAttempts; attempt++)
            {
                int best = -1;
                float bestScore = -1f;
                for (int i = 0; i < _candidateScores.Count; i++)
                    if (_candidateScores[i] > bestScore) { bestScore = _candidateScores[i]; best = i; }
                if (best < 0) break;
                _candidateScores[best] = -2f;
                var t = _candidates[best];
                bool ok = source != null ? FindPathCached(source, t, tick, outPath) : FindPath(from, t.Center, outPath);
                if (ok) { _candidates.Clear(); return t; }
            }
            _candidates.Clear();
            outPath.Clear();
            return null;
        }

        /// <summary>Field re-targeting after the target was lost (captured by someone else, destroyed, transferred).</summary>
        private bool Retarget(SimContext ctx, Army a, Civilization civ, War war)
        {
            var enemy = ctx.State.Civilizations.Get(EnemyOf(war, civ.Id));
            if (enemy == null || !enemy.IsAlive) return false;
            var t = ChooseTarget(ctx.State, enemy, a.Position, Math.Max(0.01f, a.Strength), null, 0f, _pathBuffer);
            if (t == null) return false;
            a.TargetSettlementId = t.Id;
            a.Destination = t.Center;
            a.State = ArmyState.Marching;
            a.Path.Clear();
            a.Path.AddRange(_pathBuffer);
            a.PathIndex = 0;
            if (a.Position.ChebyshevTo(t.Center) <= SiegeRange) { a.State = ArmyState.Besieging; a.Path.Clear(); }
            return true;
        }

        /// <summary>True when the settlement's blackboard names a living army raised there; clears stale entries.</summary>
        private static bool HasActiveArmy(WorldState state, Settlement s)
        {
            if (!s.Memory.TryGetValue(MilitaryMath.KeyArmyId, out float v)) return false;
            if (!float.IsNaN(v) && !float.IsInfinity(v))
            {
                var a = state.Diplomacy.Armies.Get((int)v);
                if (a != null && a.IsAlive && a.HomeSettlementId == s.Id) return true;
            }
            s.Memory.Remove(MilitaryMath.KeyArmyId);
            s.Memory.Remove(MilitaryMath.KeyArmyInitialSoldiers);
            return false;
        }

        private static int CountFreeSoldiers(WorldState state, Settlement s)
        {
            int n = 0;
            var ids = s.ResidentIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = state.People.Get(ids[i]);
                if (p != null && p.IsAlive && p.Job == JobType.Soldier && p.ArmyId < 0) n++;
            }
            return n;
        }

        private static int NearestEnemyDistSq(WorldState state, Civilization enemy, Vec2i from)
        {
            int best = int.MaxValue;
            var ids = enemy.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var t = state.Settlements.Get(ids[i]);
                if (t == null || !t.IsAlive || t.CivilizationId != enemy.Id) continue;
                int d = t.Center.DistanceSqTo(from);
                if (d < best) best = d;
            }
            return best;
        }

        private static float RaceAggression(GameDatabase db, RaceId race)
        {
            if (db == null) return 0.5f;
            int i = (int)race;
            var races = db.Races;
            return i >= 0 && i < races.Count && races[i] != null ? MathUtil.Clamp01(MathUtil.Sanitize(races[i].Aggression, 0.5f)) : 0.5f;
        }
    }
}
