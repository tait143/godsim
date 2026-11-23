using System;
using System.Collections.Generic;

namespace GodSim.Core.Diplomacy
{
    public sealed partial class ArmySystem
    {
        /// <summary>Orders armies by (X, Id) for the engagement sweep (deterministic total order, allocation-free).</summary>
        private sealed class ArmyXComparer : IComparer<Army>
        {
            public static readonly ArmyXComparer Instance = new ArmyXComparer();
            public int Compare(Army x, Army y)
            {
                int c = x.Position.X.CompareTo(y.Position.X);
                return c != 0 ? c : x.Id.CompareTo(y.Id);
            }
        }

        // =============================================================== engagements & field battles
        /// <summary>
        /// Sweep over armies sorted by X: hostile pairs within <see cref="EngageRange"/> engage. Engaged
        /// (non-retreating) armies hold position; a pair fights a round every <see cref="CombatInterval"/> ticks.
        /// </summary>
        private void DetectEngagements(SimContext ctx, long tick)
        {
            _engaged.Clear();
            _fought.Clear();
            _pairA.Clear();
            _pairB.Clear();
            _sorted.Clear();
            if (!_warfare) return;
            for (int i = 0; i < _armies.Count; i++)
            {
                var a = _armies[i];
                if (a.IsAlive && a.State != ArmyState.Disbanding) _sorted.Add(a);
            }
            if (_sorted.Count < 2) return;
            _sorted.Sort(ArmyXComparer.Instance);
            var dip = ctx.State.Diplomacy;
            for (int i = 0; i < _sorted.Count; i++)
            {
                var a = _sorted[i];
                for (int j = i + 1; j < _sorted.Count; j++)
                {
                    var b = _sorted[j];
                    if (b.Position.X - a.Position.X > EngageRange) break;
                    if (Math.Abs(b.Position.Y - a.Position.Y) > EngageRange) continue;
                    if (a.CivilizationId == b.CivilizationId) continue;
                    bool aRet = a.State == ArmyState.Retreating, bRet = b.State == ArmyState.Retreating;
                    if (aRet && bRet) continue;
                    if (!dip.AreAtWar(a.CivilizationId, b.CivilizationId)) continue;
                    if (!aRet) _engaged.Add(a.Id);
                    if (!bRet) _engaged.Add(b.Id);
                    if (Due(tick, Math.Min(a.Id, b.Id), CombatInterval)) { _pairA.Add(a); _pairB.Add(b); }
                }
            }
        }

        private void ResolveFieldBattles(SimContext ctx)
        {
            var dip = ctx.State.Diplomacy;
            for (int k = 0; k < _pairA.Count; k++)
            {
                var a = _pairA[k];
                var b = _pairB[k];
                if (!a.IsAlive || !b.IsAlive || _fought.Contains(a.Id) || _fought.Contains(b.Id)) continue;
                if (a.SoldierIds.Count == 0 || b.SoldierIds.Count == 0) continue;
                if (!dip.TryGet(a.CivilizationId, b.CivilizationId, out var rel)) continue;
                var war = ActiveWar(rel.WarId);
                if (war == null) continue;
                _fought.Add(a.Id);
                _fought.Add(b.Id);
                FieldBattle(ctx, war, a, b);
            }
        }

        private void FieldBattle(SimContext ctx, War war, Army a, Army b)
        {
            var state = ctx.State;
            var rng = ctx.Rng;
            Army atk = a, def = b;
            if (b.CivilizationId == war.AttackerId) { atk = b; def = a; }
            var civA = state.Civilizations.Get(atk.CivilizationId);
            var civD = state.Civilizations.Get(def.CivilizationId);
            if (civA == null || civD == null) return;
            int nA = RecomputeStrength(ctx, atk, civA);
            int nD = RecomputeStrength(ctx, def, civD);
            if (nA == 0 || nD == 0) return;

            float pa = atk.Strength * MilitaryMath.CombatRoll(rng);
            float pd = def.Strength * MilitaryMath.CombatRoll(rng);
            bool atkWon = pa > pd;
            int la = KillArmySoldiers(ctx, atk, MilitaryMath.Casualties(rng, nA, !atkWon));
            int ld = KillArmySoldiers(ctx, def, MilitaryMath.Casualties(rng, nD, atkWon));
            atk.Morale = MilitaryMath.MoraleAfter(atk.Morale, atkWon);
            def.Morale = MilitaryMath.MoraleAfter(def.Morale, !atkWon);

            var pos = new Vec2i((atk.Position.X + def.Position.X) / 2, (atk.Position.Y + def.Position.Y) / 2);
            RecordBattle(ctx, war, civA, civD, la, ld, atkWon, pos, null, false, false);
            AfterCombat(ctx, atk, civA);
            AfterCombat(ctx, def, civD);
        }

        /// <summary>Post-battle bookkeeping: strength refresh, annihilation, retreat check.</summary>
        private void AfterCombat(SimContext ctx, Army a, Civilization civ)
        {
            if (!a.IsAlive || civ == null) return;
            if (RecomputeStrength(ctx, a, civ) == 0) { Disband(ctx, a); return; }
            if (a.TargetSettlementId >= 0 && a.State != ArmyState.Retreating && ShouldRetreat(ctx.State, a, HomeOf(ctx.State, a)))
                SendHome(ctx, a, civ, true);
        }

        // =============================================================== sieges
        private void ResolveSieges(SimContext ctx, long tick)
        {
            for (int i = 0; i < _armies.Count; i++)
            {
                var a = _armies[i];
                if (!a.IsAlive || a.State != ArmyState.Besieging || a.TargetSettlementId < 0) continue;
                if (!Due(tick, a.Id, CombatInterval) || _engaged.Contains(a.Id) || _fought.Contains(a.Id)) continue;
                SiegeRound(ctx, a);
            }
        }

        private void SiegeRound(SimContext ctx, Army a)
        {
            var state = ctx.State;
            var rng = ctx.Rng;
            var civ = state.Civilizations.Get(a.CivilizationId);
            if (civ == null || !civ.IsAlive) return;
            var war = ActiveWar(a.WarId);
            if (war == null) return;
            var target = ValidTarget(state, a, war);
            if (target == null || a.Position.ChebyshevTo(target.Center) > SiegeRange) return;
            var defCiv = state.Civilizations.Get(target.CivilizationId);
            int living = RecomputeStrength(ctx, a, civ);
            if (living == 0) return;
            _fought.Add(a.Id);

            // ---- defenders: garrison (s.Soldiers minus soldiers away) + friendly field armies near the center
            MilitaryMath.SplitResidents(state, target, _garrison, _civilians);
            int away = MilitaryMath.AwaySoldiers(state, target.Id);
            int garrison = MilitaryMath.GarrisonCount(target, away);
            float defense = MilitaryMath.UpdateDefense(ctx.Db, state, target, away);
            _defenders.Clear();
            float armyDefense = 0f;
            int armySoldiers = 0;
            for (int i = 0; i < _armies.Count; i++)
            {
                var d = _armies[i];
                if (!d.IsAlive || d.CivilizationId != target.CivilizationId || d.State == ArmyState.Retreating || d.State == ArmyState.Disbanding) continue;
                if (_fought.Contains(d.Id) || d.Position.ChebyshevTo(target.Center) > SiegeRange || defCiv == null) continue;
                int dn = RecomputeStrength(ctx, d, defCiv);
                if (dn == 0) continue;
                _defenders.Add(d);
                _fought.Add(d.Id);
                armyDefense += d.Strength;
                armySoldiers += dn;
            }

            // ---- combat round
            float atkPower = a.Strength * MilitaryMath.CombatRoll(rng);
            float defPower = (defense + armyDefense) * MilitaryMath.CombatRoll(rng);
            bool atkWon = atkPower > defPower;

            int la = KillArmySoldiers(ctx, a, MilitaryMath.Casualties(rng, living, !atkWon));
            int defCasualties = MilitaryMath.Casualties(rng, garrison + armySoldiers, atkWon);
            int fromGarrison = Math.Min(defCasualties, garrison);
            int killedGarrison = KillFromList(ctx, _garrison, fromGarrison);
            if (fromGarrison > killedGarrison) target.Soldiers = Math.Max(0, target.Soldiers - (fromGarrison - killedGarrison));
            int ld = killedGarrison;
            int remaining = defCasualties - fromGarrison;
            for (int i = 0; i < _defenders.Count && remaining > 0; i++)
            {
                int want = Math.Min(remaining, _defenders[i].SoldierIds.Count);
                remaining -= want;
                ld += KillArmySoldiers(ctx, _defenders[i], want);
            }
            if (atkWon)
            {
                ld += KillFromList(ctx, _civilians, MilitaryMath.CivilianCasualties(rng, _civilians.Count));
                float dmg = MathUtil.Sanitize(target.Mem(MilitaryMath.KeySiegeDamage)) + rng.Range(0.08f, 0.15f);
                target.Memory[MilitaryMath.KeySiegeDamage] = MathUtil.Clamp(dmg, 0f, MilitaryMath.MaxSiegeDamage);
            }
            a.Morale = MilitaryMath.MoraleAfter(a.Morale, atkWon);
            for (int i = 0; i < _defenders.Count; i++) _defenders[i].Morale = MilitaryMath.MoraleAfter(_defenders[i].Morale, !atkWon);

            // ---- aftermath
            int awayAfter = MilitaryMath.AwaySoldiers(state, target.Id);
            int garrisonAfter = MilitaryMath.GarrisonCount(target, awayAfter);
            float defenseAfter = MilitaryMath.UpdateDefense(ctx.Db, state, target, awayAfter);
            float armyDefenseAfter = 0f;
            for (int i = 0; i < _defenders.Count; i++)
            {
                var d = _defenders[i];
                if (d.IsAlive && defCiv != null && RecomputeStrength(ctx, d, defCiv) > 0) armyDefenseAfter += d.Strength;
            }
            living = a.IsAlive ? RecomputeStrength(ctx, a, civ) : 0;
            bool capture = living > 0 && MilitaryMath.ShouldCapture(defenseAfter + armyDefenseAfter, garrisonAfter, a.Strength);
            bool raze = capture && ShouldRaze(ctx, civ, target);

            RecordBattle(ctx, war, civ, defCiv, la, ld, atkWon, target.Center, target, capture && !raze, raze);
            for (int i = 0; i < _defenders.Count; i++) AfterCombat(ctx, _defenders[i], defCiv);

            if (!a.IsAlive) return;
            if (living == 0) { Disband(ctx, a); return; }
            if (capture) { Conquer(ctx, a, civ, target, war, raze); return; }
            if (ShouldRetreat(state, a, HomeOf(state, a))) SendHome(ctx, a, civ, true);
        }

        /// <summary>Raze instead of capture: camps taken by aggressive races (&gt; 0.7), or when the conqueror is at its settlement cap. Never the defender's last settlement.</summary>
        private static bool ShouldRaze(SimContext ctx, Civilization conqueror, Settlement target)
        {
            var state = ctx.State;
            var owner = state.Civilizations.Get(target.CivilizationId);
            if (owner == null) return false;
            int alive = 0;
            var ids = owner.SettlementIds;
            for (int i = 0; i < ids.Count; i++) { var s = state.Settlements.Get(ids[i]); if (s != null && s.IsAlive) alive++; }
            if (alive <= 1) return false; // never wipe out a people in one blow: the last settlement is always captured
            if (ctx.Config != null && conqueror.SettlementIds.Count >= ctx.Config.MaxSettlementsPerCiv) return true;
            if (target.Tier >= SettlementTier.Hamlet) return false;
            return RaceAggression(ctx.Db, conqueror.Race) > MilitaryMath.RazeAggression;
        }

        private void Conquer(SimContext ctx, Army a, Civilization civ, Settlement target, War war, bool raze)
        {
            war.SettlementsCaptured++;
            war.WarScore = MilitaryMath.ApplyWarScore(war.WarScore, MilitaryMath.FavourSign(war, civ.Id) * MilitaryMath.CaptureWarScore);
            target.Memory.Remove(MilitaryMath.KeySiegeDamage);
            target.Memory.Remove(MilitaryMath.KeyArmyId);
            target.Memory.Remove(MilitaryMath.KeyArmyInitialSoldiers);
            target.Memory.Remove(MilitaryMath.KeyRestUntil);
            if (raze) ctx.Ops.DestroySettlement(target, DestroyCause.War);
            else
            {
                ctx.Ops.TransferSettlement(target, civ);
                MilitaryMath.UpdateDefense(ctx.Db, ctx.State, target, MilitaryMath.AwaySoldiers(ctx.State, target.Id));
            }
            if (a.IsAlive) SendHome(ctx, a, civ, false);
        }

        // =============================================================== casualties & records
        private int KillArmySoldiers(SimContext ctx, Army a, int count)
        {
            if (count <= 0 || a == null || a.SoldierIds.Count == 0) return 0;
            var state = ctx.State;
            var rng = ctx.Rng;
            _idBuffer.Clear();
            _idBuffer.AddRange(a.SoldierIds);
            int killed = 0;
            while (killed < count && _idBuffer.Count > 0)
            {
                int idx = rng.Range(0, _idBuffer.Count);
                int last = _idBuffer.Count - 1;
                int id = _idBuffer[idx];
                _idBuffer[idx] = _idBuffer[last];
                _idBuffer.RemoveAt(last);
                var p = state.People.Get(id);
                if (p == null || !p.IsAlive) continue;
                KillCombatant(ctx, p);
                killed++;
            }
            _idBuffer.Clear();
            return killed;
        }

        private static int KillFromList(SimContext ctx, List<Person> pool, int count)
        {
            if (count <= 0 || pool.Count == 0) return 0;
            var rng = ctx.Rng;
            int killed = 0;
            while (killed < count && pool.Count > 0)
            {
                int idx = rng.Range(0, pool.Count);
                int last = pool.Count - 1;
                var p = pool[idx];
                pool[idx] = pool[last];
                pool.RemoveAt(last);
                if (p == null || !p.IsAlive) continue;
                KillCombatant(ctx, p);
                killed++;
            }
            return killed;
        }

        /// <summary>Kills through WorldOps (Violence) and keeps the settlement's cached soldier count in step until job assignment recounts.</summary>
        private static void KillCombatant(SimContext ctx, Person p)
        {
            if (p.Job == JobType.Soldier)
            {
                var s = ctx.State.Settlements.Get(p.SettlementId);
                if (s != null && s.Soldiers > 0) s.Soldiers--;
            }
            ctx.Ops.KillPerson(p, DeathCause.Violence);
        }

        /// <summary>Updates war + world statistics, war score, publishes <see cref="BattleEvent"/> and chronicles major battles.</summary>
        private static void RecordBattle(SimContext ctx, War war, Civilization atkCiv, Civilization defCiv, int atkLosses, int defLosses,
                                         bool atkWon, Vec2i pos, Settlement site, bool captured, bool razed)
        {
            var state = ctx.State;
            int atkId = atkCiv != null ? atkCiv.Id : -1;
            int defId = defCiv != null ? defCiv.Id : -1;
            state.Stats.BattlesFought++;
            war.Battles++;
            if (atkId == war.AttackerId) { war.AttackerLosses += atkLosses; war.DefenderLosses += defLosses; }
            else { war.AttackerLosses += defLosses; war.DefenderLosses += atkLosses; }
            int winner = atkWon ? atkId : defId;
            war.WarScore = MilitaryMath.ApplyWarScore(war.WarScore, MilitaryMath.FavourSign(war, winner) * MilitaryMath.BattleWarScore(ctx.Rng));

            ctx.Events?.Publish(new BattleEvent
            {
                Position = pos,
                AttackerCivId = atkId,
                DefenderCivId = defId,
                SettlementId = site != null ? site.Id : -1,
                AttackerLosses = atkLosses,
                DefenderLosses = defLosses,
                AttackerWon = atkWon,
            });

            if (atkLosses + defLosses <= 20 && !captured && !razed) return;
            if (!captured && !razed && atkCiv != null)
            {
                // Rate-limit chronicling of long sieges/skirmishes so history stays readable.
                float last = atkCiv.Mem(MilitaryMath.KeyLastBattleChronicle, -1e9f);
                if (state.Tick - last < BattleChronicleCooldown) return;
                atkCiv.Memory[MilitaryMath.KeyLastBattleChronicle] = state.Tick;
            }
            string atkName = atkCiv != null ? atkCiv.Name : "invaders";
            string defName = defCiv != null ? defCiv.Name : "defenders";
            string text;
            if (razed) text = "The " + atkName + " stormed " + site.Name + " and put it to the torch.";
            else if (captured) text = "The " + atkName + " stormed the defences of " + site.Name + ", wresting it from the " + defName + ".";
            else if (site != null)
                text = "At the siege of " + site.Name + " the " + (atkWon ? atkName : defName) + " prevailed; " + (atkLosses + defLosses) + " fell.";
            else
                text = "The armies of the " + atkName + " and the " + defName + " clashed in the field; the " + (atkWon ? atkName : defName) + " prevailed and " + (atkLosses + defLosses) + " fell.";
            ctx.Ops.AddChronicle(ChronicleCategory.War, text, atkId, site != null ? site.Id : -1, pos, 2);
        }
    }
}
