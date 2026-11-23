using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Diplomacy;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Diplomacy.Military
{
    public class ArmySystemTests
    {
        [Test]
        public void Systems_HaveContractShape()
        {
            Assert.AreEqual(6, new MilitarySystem().TickInterval);
            Assert.AreEqual(1, new ArmySystem().TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(new ArmySystem().Name));
        }

        [Test]
        public void NoArmy_WithoutWar_ArmyRaisedOnceWarStarts()
        {
            var b = MilitaryTestKit.Create(14, 10, 12, 0, war: false);
            b.Run(60);
            Assert.AreEqual(0, b.LivingArmies().Count, "no war, no army");

            b.War = MilitaryTestKit.DeclareWar(b.Ctx, b.Attacker, b.Defender);
            Assert.IsTrue(b.RunUntil(() => b.FirstArmy(b.Attacker.Id) != null, 24));
            var a = b.FirstArmy(b.Attacker.Id);
            Assert.AreEqual(b.War.Id, a.WarId);
            Assert.AreEqual(b.D.Id, a.TargetSettlementId);
            Assert.AreEqual(ArmyState.Marching, a.State);
            Assert.AreEqual(7, a.SoldierIds.Count, "70% of 10 soldiers");
            Assert.AreEqual(a.Id, (int)b.A.Mem(MilitaryMath.KeyArmyId, -1f));
            Assert.AreEqual(a.HomeSettlementId, b.A.Id);
            Assert.IsTrue(a.Strength > 6.9f && a.Strength < 7.1f, "strength = 7 soldiers * 1 * morale 1, got " + a.Strength);
            foreach (int id in a.SoldierIds)
            {
                var p = b.State.People.Get(id);
                Assert.AreEqual(a.Id, p.ArmyId);
                Assert.AreEqual(JobType.Soldier, p.Job);
            }
            Assert.AreEqual(0, b.LivingArmies(b.Defender.Id).Count, "defender without soldiers cannot counter-attack");
        }

        [Test]
        public void Army_PathReachesTarget_AndAdvancesEveryTick()
        {
            var b = MilitaryTestKit.Create(14, 10, 12, 1, defService: 2f);
            Assert.IsTrue(b.RunUntil(() => b.FirstArmy(b.Attacker.Id) != null, 24));
            var a = b.FirstArmy(b.Attacker.Id);
            Assert.AreEqual(b.A.Center, a.Path[0]);
            Assert.AreEqual(b.D.Center, a.Path[a.Path.Count - 1]);
            for (int i = 1; i < a.Path.Count; i++) Assert.AreEqual(1, a.Path[i].ChebyshevTo(a.Path[i - 1]));

            int lastDist = a.Position.ChebyshevTo(b.D.Center);
            float lastProgress = a.PathIndex + a.MoveProgress;
            int ticks = 0;
            while (a.State == ArmyState.Marching && ticks < 400)
            {
                b.Step();
                ticks++;
                if (!a.IsAlive || a.State != ArmyState.Marching) break;
                float progress = a.PathIndex + a.MoveProgress;
                Assert.IsTrue(progress > lastProgress, "progress advances every tick");
                int dist = a.Position.ChebyshevTo(b.D.Center);
                Assert.IsTrue(dist <= lastDist, "never moves away from the target");
                lastProgress = progress;
                lastDist = dist;
                foreach (int id in a.SoldierIds) Assert.AreEqual(a.Position, b.State.People.Get(id).Position);
            }
            Assert.AreEqual(ArmyState.Besieging, a.State);
            Assert.IsTrue(a.Position.ChebyshevTo(b.D.Center) <= ArmySystem.SiegeRange);
            Assert.IsTrue(ticks >= 100 && ticks <= 140, "~28 tiles at 0.25 tiles/tick, took " + ticks);
        }

        [Test]
        public void Siege_KillsSoldiers_AndPublishesBattleEvent()
        {
            var b = MilitaryTestKit.Create(30, 20, 30, 3, defService: 5f);
            Assert.IsTrue(b.RunUntil(() => b.Battles.Count > 0, 400), "a siege round happens");
            var e = b.Battles[0];
            Assert.AreEqual(b.D.Id, e.SettlementId);
            Assert.AreEqual(b.Attacker.Id, e.AttackerCivId);
            Assert.AreEqual(b.Defender.Id, e.DefenderCivId);
            Assert.IsTrue(e.AttackerLosses + e.DefenderLosses > 0);
            Assert.AreEqual(e.AttackerLosses + e.DefenderLosses, MilitaryTestKit.ViolentDeaths(b.Ctx));
            Assert.AreEqual(1, b.War.Battles);
            Assert.AreEqual(1, b.State.Stats.BattlesFought);
            Assert.AreEqual(e.AttackerLosses, b.War.AttackerLosses);
            Assert.AreEqual(e.DefenderLosses, b.War.DefenderLosses);
            Assert.IsTrue(System.Math.Abs(b.War.WarScore) >= 5f && System.Math.Abs(b.War.WarScore) <= 15f);
        }

        [Test]
        public void StrongAttacker_CapturesWeakSettlement_ThenGoesHome()
        {
            var b = MilitaryTestKit.Create(40, 30, 12, 2);
            var d2 = MilitaryTestKit.AddTown(b.Ctx, b.Defender, new Vec2i(100, 100), 6, 0);
            Assert.IsTrue(b.RunUntil(() => b.D.CivilizationId == b.Attacker.Id, 800), "settlement captured");
            Assert.IsTrue(b.D.IsAlive);
            Assert.AreEqual(1, b.War.SettlementsCaptured);
            Assert.IsTrue(b.War.WarScore >= 25f, "capture favours the attacker: " + b.War.WarScore);
            Assert.IsTrue(b.War.IsActive, "defender still holds " + d2.Name);
            Assert.IsTrue(b.Attacker.SettlementIds.Contains(b.D.Id));
            var a = b.FirstArmy(b.Attacker.Id);
            Assert.IsNotNull(a);
            Assert.AreEqual(-1, a.TargetSettlementId, "the army heads home after the capture");
            Assert.IsTrue(b.RunUntil(() => b.FirstArmy(b.Attacker.Id) == null || b.FirstArmy(b.Attacker.Id).Id != a.Id, 300));
            Assert.IsFalse(a.IsAlive);
        }

        [Test]
        public void WeakAttacker_Retreats_AndDisbandsAtHome()
        {
            var b = MilitaryTestKit.Create(14, 10, 30, 3, defService: 15f);
            Assert.IsTrue(b.RunUntil(() => b.FirstArmy(b.Attacker.Id) != null, 24));
            var a = b.FirstArmy(b.Attacker.Id);
            Assert.IsTrue(b.RunUntil(() => a.State == ArmyState.Retreating, 600), "losses never annihilate before the retreat");
            Assert.IsTrue(a.IsAlive && a.SoldierIds.Count > 0);
            Assert.IsTrue(b.Battles.Count > 0);
            Assert.IsTrue(b.RunUntil(() => !a.IsAlive, 400), "retreating army disbands on arrival");
            Assert.AreEqual(b.Defender.Id, b.D.CivilizationId);
            Assert.IsTrue(b.A.Mem(MilitaryMath.KeyRestUntil) > 0f, "home regroups after the defeat");
            Assert.IsFalse(b.A.Memory.ContainsKey(MilitaryMath.KeyArmyId));
            foreach (int id in b.A.ResidentIds)
            {
                var p = b.State.People.Get(id);
                if (p.ArmyId == a.Id) Assert.Fail("soldier still attached to a disbanded army");
            }
        }

        [Test]
        public void WarEnd_SendsArmiesHome_AndDisbandsThem()
        {
            var b = MilitaryTestKit.Create(14, 10, 12, 0);
            Assert.IsTrue(b.RunUntil(() => b.FirstArmy(b.Attacker.Id) != null, 24));
            var a = b.FirstArmy(b.Attacker.Id);
            var soldiers = new List<int>(a.SoldierIds);
            b.Run(40);
            MilitaryTestKit.EndWar(b.Ctx, b.War);
            Assert.IsTrue(b.RunUntil(() => b.LivingArmies().Count == 0, 300));
            foreach (int id in soldiers)
            {
                var p = b.State.People.Get(id);
                Assert.AreEqual(-1, p.ArmyId);
                Assert.AreEqual(JobType.Soldier, p.Job, "soldiers keep their jobs");
            }
            Assert.IsFalse(b.A.Memory.ContainsKey(MilitaryMath.KeyArmyId));
            b.Run(100);
            Assert.AreEqual(0, b.LivingArmies().Count, "no new armies in peace");
        }

        [Test]
        public void MaxArmies_IsRespected()
        {
            var b = MilitaryTestKit.Create(14, 8, 12, 0);
            MilitaryTestKit.AddTown(b.Ctx, b.Attacker, new Vec2i(40, 30), 12, 8);
            MilitaryTestKit.AddTown(b.Ctx, b.Attacker, new Vec2i(40, 82), 12, 8);
            b.Ctx.Config.MaxArmies = 2;
            int peak = 0;
            for (int t = 0; t < 120; t++)
            {
                b.Step();
                int n = b.LivingArmies().Count;
                if (n > peak) peak = n;
                Assert.IsTrue(n <= 2, "tick " + b.State.Tick + ": " + n + " armies");
            }
            Assert.AreEqual(2, peak);

            var c = MilitaryTestKit.Create(14, 8, 12, 0);
            c.Ctx.Config.MaxArmies = 0;
            c.Run(60);
            Assert.AreEqual(0, c.LivingArmies().Count);
        }

        [Test]
        public void WaterBlockedTarget_NoArmyStaysInTheField()
        {
            var b = MilitaryTestKit.Create(14, 10, 12, 0);
            MilitaryTestKit.WaterChannel(b.State.Map, 55, 57);
            b.Run(120);
            Assert.AreEqual(0, b.LivingArmies().Count);
            Assert.IsTrue(b.Armies.LastPathExpansions <= ArmySystem.MaxPathExpansions + 1);
            foreach (int id in b.A.ResidentIds) Assert.AreEqual(-1, b.State.People.Get(id).ArmyId);
            Assert.AreEqual(0, b.Battles.Count);
        }

        [Test]
        public void ArmyCutOffMidMarch_RepathsHome_AndDisbands()
        {
            var b = MilitaryTestKit.Create(14, 10, 12, 0);
            Assert.IsTrue(b.RunUntil(() => b.FirstArmy(b.Attacker.Id) != null, 24));
            var a = b.FirstArmy(b.Attacker.Id);
            Assert.IsTrue(b.RunUntil(() => a.PathIndex >= 4, 60));
            MilitaryTestKit.WaterChannel(b.State.Map, 60, 62);
            Assert.IsTrue(b.RunUntil(() => !a.IsAlive, 400), "army gives up and disbands");
            Assert.AreEqual(b.Defender.Id, b.D.CivilizationId);
            Assert.AreEqual(0, b.Battles.Count);
        }

        [Test]
        public void HomeCaptured_ArmyDisbands()
        {
            var b = MilitaryTestKit.Create(14, 10, 12, 0);
            MilitaryTestKit.AddTown(b.Ctx, b.Attacker, new Vec2i(10, 10), 4, 0);
            Assert.IsTrue(b.RunUntil(() => b.FirstArmy(b.Attacker.Id) != null, 24));
            var a = b.FirstArmy(b.Attacker.Id);
            b.Run(10);
            b.Ctx.Ops.TransferSettlement(b.A, b.Defender);
            b.Run(2);
            Assert.IsFalse(a.IsAlive);
        }

        [Test]
        public void TargetTransferredMidMarch_ArmyRetargets()
        {
            var b = MilitaryTestKit.Create(14, 10, 12, 0);
            var d2 = MilitaryTestKit.AddTown(b.Ctx, b.Defender, new Vec2i(70, 90), 6, 0);
            Assert.IsTrue(b.RunUntil(() => b.FirstArmy(b.Attacker.Id) != null, 24));
            var a = b.FirstArmy(b.Attacker.Id);
            Assert.AreEqual(b.D.Id, a.TargetSettlementId);
            b.Run(10);
            TestWorlds.AddSettlement(b.Ctx, RaceId.Dwarf, new Vec2i(100, 10), 3, out var third);
            b.Ctx.Ops.TransferSettlement(b.D, third);
            b.Step();
            Assert.IsTrue(a.IsAlive);
            Assert.AreEqual(d2.Id, a.TargetSettlementId);
            Assert.AreEqual(ArmyState.Marching, a.State);
            Assert.AreEqual(d2.Center, a.Path[a.Path.Count - 1]);
        }

        [Test]
        public void SoldiersKilledElsewhere_EmptyArmyDisbands_StrengthTracksLivingSoldiers()
        {
            var b = MilitaryTestKit.Create(14, 10, 12, 0);
            Assert.IsTrue(b.RunUntil(() => b.FirstArmy(b.Attacker.Id) != null, 24));
            var a = b.FirstArmy(b.Attacker.Id);
            var ids = new List<int>(a.SoldierIds);
            b.Ctx.Ops.KillPerson(b.State.People.Get(ids[0]), DeathCause.Disaster);
            b.Run(ArmySystem.DecisionInterval);
            Assert.AreEqual(ids.Count - 1, a.SoldierIds.Count);
            Assert.IsTrue(a.Strength <= ids.Count - 1 + 1e-3f, "strength recomputed from living soldiers: " + a.Strength);
            foreach (int id in ids) b.Ctx.Ops.KillPerson(b.State.People.Get(id), DeathCause.Disease);
            b.Step();
            Assert.IsFalse(a.IsAlive);
            Assert.IsFalse(b.A.Memory.ContainsKey(MilitaryMath.KeyArmyId));
        }

        [Test]
        public void WarfareDisabled_NoArmies_AndFieldArmiesGoHome()
        {
            var b = MilitaryTestKit.Create(14, 10, 12, 0);
            Assert.IsTrue(b.RunUntil(() => b.FirstArmy(b.Attacker.Id) != null, 24));
            b.Ctx.Config.EnableWarfare = false;
            Assert.IsTrue(b.RunUntil(() => b.LivingArmies().Count == 0, 300));
            b.Run(150);
            Assert.AreEqual(0, b.LivingArmies().Count);
            Assert.AreEqual(0, b.Battles.Count);
        }

        [Test]
        public void CivilizationCollapse_RemovesItsArmies()
        {
            var b = MilitaryTestKit.Create(14, 10, 12, 0);
            Assert.IsTrue(b.RunUntil(() => b.FirstArmy(b.Attacker.Id) != null, 24));
            b.Ctx.Ops.CollapseCivilization(b.Attacker, -1);
            b.Run(2);
            Assert.AreEqual(0, b.LivingArmies().Count);
        }

        [Test]
        public void StrongerDefender_CounterAttacks_AndArmiesMeetInTheField()
        {
            var b = MilitaryTestKit.Create(16, 12, 26, 20);
            Assert.IsTrue(b.RunUntil(() => b.FirstArmy(b.Defender.Id) != null && b.FirstArmy(b.Attacker.Id) != null, 30));
            Assert.AreEqual(b.A.Id, b.FirstArmy(b.Defender.Id).TargetSettlementId);
            Assert.IsTrue(b.RunUntil(() => b.Battles.Exists(e => e.SettlementId < 0), 200), "field battle between the two armies");
            var fb = b.Battles.Find(e => e.SettlementId < 0);
            Assert.AreEqual(b.Attacker.Id, fb.AttackerCivId);
            Assert.AreEqual(b.Defender.Id, fb.DefenderCivId);
            Assert.IsTrue(b.War.Battles >= 1);
        }

        [Test]
        public void AggressiveRace_RazesCamp_ButNeverTheLastSettlement()
        {
            var db = TestDb.CreateStub(false);
            db.Race(RaceId.Orc).Aggression = 0.9f;
            db.Freeze();
            var b = MilitaryTestKit.Create(40, 30, 10, 1, db: db, atkRace: RaceId.Orc);
            MilitaryTestKit.AddTown(b.Ctx, b.Defender, new Vec2i(100, 100), 6, 0);
            Assert.AreEqual(SettlementTier.Camp, b.D.Tier);
            Assert.IsTrue(b.RunUntil(() => !b.D.IsAlive || b.D.CivilizationId != b.Defender.Id, 800));
            Assert.IsFalse(b.D.IsAlive, "camp razed");
            Assert.AreEqual(1, b.War.SettlementsCaptured);
            Assert.IsTrue(b.War.WarScore > 0f);

            var c = MilitaryTestKit.Create(40, 30, 10, 1, db: db, atkRace: RaceId.Orc);
            Assert.IsTrue(c.RunUntil(() => !c.D.IsAlive || c.D.CivilizationId != c.Defender.Id, 800));
            Assert.IsTrue(c.D.IsAlive, "last settlement is captured, not razed");
            Assert.AreEqual(c.Attacker.Id, c.D.CivilizationId);
            Assert.IsFalse(c.Defender.IsAlive);
            Assert.IsFalse(c.War.IsActive);
        }

        [Test]
        public void SiegeRounds_NeverWipeOutASettlementInOneTick()
        {
            var b = MilitaryTestKit.Create(80, 60, 200, 30, defService: 10f);
            int worst = 0, before = b.D.Population;
            for (int t = 0; t < 900; t++)
            {
                int pop = b.D.Population;
                b.Step();
                if (!b.D.IsAlive || b.D.CivilizationId != b.Defender.Id) break;
                int lost = pop - b.D.Population;
                if (lost > worst) worst = lost;
            }
            Assert.IsTrue(b.Battles.Count > 0);
            Assert.IsTrue(worst <= 0.25f * 30 + 0.02f * before + 2, "worst single-tick loss " + worst);
        }

        [Test]
        public void Soak_ThreeCivsTwoWars_StateStaysSane()
        {
            var b = MilitaryTestKit.Create(40, 18, 40, 14, defService: 4f, seed: 99);
            TestWorlds.AddSettlement(b.Ctx, RaceId.Orc, new Vec2i(56, 20), 40, out var orcs);
            var orcTown = b.State.Settlements.Get(orcs.CapitalId);
            MilitaryTestKit.MakeSoldiers(b.Ctx, orcTown, 20);
            MilitaryTestKit.AddTown(b.Ctx, b.Defender, new Vec2i(90, 90), 30, 10);
            MilitaryTestKit.DeclareWar(b.Ctx, orcs, b.Attacker);
            for (int t = 0; t < 1500; t++)
            {
                b.Step();
                foreach (var a in b.LivingArmies())
                {
                    Assert.IsTrue(b.State.Map.InBounds(a.Position));
                    Assert.IsFalse(float.IsNaN(a.Strength) || float.IsInfinity(a.Strength));
                    Assert.IsTrue(a.Morale >= 0f && a.Morale <= 1f);
                }
            }
            foreach (var w in b.State.Diplomacy.Wars) Assert.IsTrue(w.WarScore >= -100f && w.WarScore <= 100f);
            Assert.IsTrue(b.State.Stats.BattlesFought > 0);
            foreach (var c in b.State.Civilizations.Items)
                Assert.IsFalse(float.IsNaN(c.MilitaryPower) || float.IsInfinity(c.MilitaryPower));
        }

        [Test]
        public void Simulation_IsDeterministic()
        {
            Assert.AreEqual(Fingerprint(11), Fingerprint(11));
        }

        private static string Fingerprint(ulong seed)
        {
            var b = MilitaryTestKit.Create(30, 18, 30, 8, defService: 6f, seed: seed);
            MilitaryTestKit.AddTown(b.Ctx, b.Defender, new Vec2i(90, 30), 20, 6);
            var sb = new StringBuilder();
            for (int t = 0; t < 500; t++)
            {
                b.Step();
                if (t % 25 != 0) continue;
                foreach (var a in b.LivingArmies())
                    sb.Append(a.Id).Append(':').Append(a.Position).Append(a.State).Append(a.Strength.ToString("R")).Append(';');
                sb.Append('|');
            }
            sb.Append(b.State.LivingPopulation).Append('/').Append(b.War.WarScore.ToString("R")).Append('/').Append(b.War.Battles)
              .Append('/').Append(b.D.CivilizationId).Append('/').Append(b.State.Rng.State);
            return sb.ToString();
        }
    }
}
