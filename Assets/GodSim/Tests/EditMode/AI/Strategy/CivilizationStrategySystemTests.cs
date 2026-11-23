using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Strategy
{
    public class CivilizationStrategySystemTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp() { _db = StrategyTestSupport.CreateDb(); }

        private static int Posture(Civilization c) => (int)c.Mem(CivStrategyKeys.Posture, -1f);
        private static int Focus(Civilization c) => (int)c.Mem(CivStrategyKeys.Focus, -1f);

        private static int CountWonderOrders(Settlement s, BuildingType type)
        {
            int n = 0;
            foreach (var o in s.BuildQueue) if (o.Type == type) n++;
            return n;
        }

        private static int CountWonderOrders(SimContext ctx, Civilization civ)
        {
            int n = 0;
            foreach (int id in civ.SettlementIds)
            {
                var s = ctx.State.Settlements.Get(id);
                foreach (var o in s.BuildQueue) if (o.Reason.StartsWith("wonder: ")) n++;
            }
            return n;
        }

        [Test]
        public void SystemMetadata()
        {
            var sys = new CivilizationStrategySystem();
            Assert.AreEqual(24, sys.TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(sys.Name));
        }

        [Test]
        public void AggressiveRaceAtWar_BecomesMilitarist()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(30, 30), 10, out var orc);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(100, 100), 10, out var elf);
            StrategyTestSupport.StartWar(ctx, elf, orc); // the orcs even defend
            StrategyTestSupport.RunOnce(new CivilizationStrategySystem(), ctx);
            Assert.AreEqual(CivStrategyKeys.PostureMilitarist, Posture(orc));
            Assert.AreEqual(1f, orc.Mem(CivStrategyKeys.ActiveWars));
            Assert.Greater(orc.Mem(CivStrategyKeys.Threat), 0f);
        }

        [Test]
        public void PeacefulRaceAtWar_BecomesDefensive_AndPeacefulWhenAlone()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(30, 30), 10, out var elf);
            StrategyTestSupport.RunOnce(new CivilizationStrategySystem(), ctx);
            Assert.AreEqual(CivStrategyKeys.PosturePeaceful, Posture(elf));

            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(100, 100), 10, out var orc);
            StrategyTestSupport.StartWar(ctx, orc, elf);
            StrategyTestSupport.RunOnce(new CivilizationStrategySystem(), ctx);
            Assert.AreEqual(CivStrategyKeys.PostureDefensive, Posture(elf));
        }

        [Test]
        public void HostileNeighbour_MakesPeacefulRaceDefensive()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(30, 30), 10, out var elf);
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(52, 30), 10, out var human);
            var rel = ctx.State.Diplomacy.Get(elf.Id, human.Id);
            rel.HasContact = true;
            rel.Stance = DiplomaticStance.Hostile;
            StrategyTestSupport.RunOnce(new CivilizationStrategySystem(), ctx);
            Assert.AreEqual(CivStrategyKeys.PostureDefensive, Posture(elf));
            Assert.AreEqual(1f, elf.Mem(CivStrategyKeys.HostileNeighbours));
        }

        [Test]
        public void PostureAndFocus_AlwaysInRange_ForEveryRace()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            var civs = new Civilization[Counts.Races];
            for (int r = 0; r < Counts.Races; r++)
                TestWorlds.AddSettlement(ctx, (RaceId)r, new Vec2i(12 + r * 22, 64), 8, out civs[r]);
            StrategyTestSupport.StartWar(ctx, civs[0], civs[1]);
            TestWorlds.Run(new CivilizationStrategySystem(), ctx, 24 * 6);
            foreach (var c in civs)
            {
                Assert.That(Posture(c), Is.InRange(0, 2));
                Assert.That(Focus(c), Is.InRange(0, 4));
            }
        }

        [Test]
        public void Focus_FollowsPersonality()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(30, 30), 10, out var saurian);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(90, 90), 10, out var elf);
            StrategyTestSupport.RunOnce(new CivilizationStrategySystem(), ctx);
            Assert.AreEqual(CivStrategyKeys.FocusFaith, Focus(saurian));
            Assert.AreEqual(CivStrategyKeys.FocusResearch, Focus(elf));
        }

        [Test]
        public void Focus_FoodCrisis_SwitchesToGrowth()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 40, out var human);
            s.Memory["econ.foodDays"] = 5f;
            StrategyTestSupport.RunOnce(new CivilizationStrategySystem(), ctx);
            Assert.AreEqual(CivStrategyKeys.FocusGrowth, Focus(human));
        }

        [Test]
        public void Wonder_QueuedAtCapital_WhenAvailableAndAffordable_AndNeverTwice()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(40, 40), 20, out var human);
            cap.Tier = SettlementTier.Town;
            cap.Stockpile.SetRaw(ResourceType.Stone, 60f); // >= 50% of 100

            var sys = new CivilizationStrategySystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(1, CountWonderOrders(cap, BuildingType.GreatLibrary));
            var order = cap.BuildQueue.Find(o => o.Type == BuildingType.GreatLibrary);
            Assert.AreEqual(6f, order.Priority);
            Assert.AreEqual("wonder: Great Library", order.Reason);
            Assert.IsTrue(cap.AiLog.Exists(l => l.Contains("Great Library")));

            // Still queued: never a second copy, and no second wonder project meanwhile.
            TestWorlds.Run(sys, ctx, 24 * 10, initialize: false);
            Assert.AreEqual(1, CountWonderOrders(ctx, human));

            // The planner places and completes it: never ordered again (even after the retry period).
            cap.BuildQueue.Clear();
            var site = ctx.Ops.FindFreeSite(new Vec2i(46, 46), 2, 2, 10);
            var b = ctx.Ops.CreateBuilding(cap, BuildingType.GreatLibrary, site, 0, true);
            Assert.IsNotNull(b);
            TestWorlds.Run(sys, ctx, CivilizationStrategySystem.WonderRetryTicks + 48, initialize: false);
            Assert.AreEqual(0, CountWonderOrders(cap, BuildingType.GreatLibrary));
        }

        [Test]
        public void Wonder_OnlyOneProjectAtATime_ThenNextFitsFocus()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(40, 40), 20, out var human);
            cap.Tier = SettlementTier.Town;
            cap.Stockpile.SetRaw(ResourceType.Stone, 120f);
            var sys = new CivilizationStrategySystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(1, CountWonderOrders(ctx, human));
            var first = cap.BuildQueue[0].Type;

            // First one completes; the other shared wonder follows.
            cap.BuildQueue.Clear();
            ctx.Ops.CreateBuilding(cap, first, ctx.Ops.FindFreeSite(new Vec2i(46, 46), 2, 2, 10), 0, true);
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(1, CountWonderOrders(ctx, human));
            Assert.AreNotEqual(first, cap.BuildQueue[0].Type);
            Assert.IsTrue(cap.BuildQueue[0].Type == BuildingType.GreatLibrary || cap.BuildQueue[0].Type == BuildingType.GrandBazaar);
        }

        [Test]
        public void Wonder_NotQueued_WhenUnaffordable_TooSmall_OrPlacementImpossible()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(40, 40), 20, out var human);
            cap.Tier = SettlementTier.Town;
            cap.Stockpile.SetRaw(ResourceType.Stone, 20f);   // < 50%
            cap.Stockpile.SetRaw(ResourceType.Wood, 100f);   // Colossus affordable but no coast
            var sys = new CivilizationStrategySystem();
            TestWorlds.Run(sys, ctx, 24 * 3);
            Assert.AreEqual(0, CountWonderOrders(ctx, human));

            cap.Stockpile.SetRaw(ResourceType.Stone, 80f);
            cap.Tier = SettlementTier.Village;               // shared wonders need a Town
            TestWorlds.Run(sys, ctx, 24 * 3, initialize: false);
            Assert.AreEqual(0, CountWonderOrders(ctx, human));

            cap.Tier = SettlementTier.Town;
            TestWorlds.Run(sys, ctx, 24, initialize: false);
            Assert.AreEqual(1, CountWonderOrders(ctx, human));
            Assert.AreEqual(0, CountWonderOrders(cap, BuildingType.Colossus));
        }

        [Test]
        public void Wonder_RacialUniquePreferred_AndAllowedAtVillage()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(40, 40), 20, out var orc);
            orc.Age = AgeId.Bronze;
            cap.Tier = SettlementTier.Village;
            cap.Stockpile.SetRaw(ResourceType.Wood, 45f);
            cap.Stockpile.SetRaw(ResourceType.Stone, 100f);
            var sys = new CivilizationStrategySystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(1, CountWonderOrders(cap, BuildingType.WarIdol));
            Assert.AreEqual(1, CountWonderOrders(ctx, orc));

            // A Town with everything affordable still prefers the racial unique.
            var ctx2 = TestWorlds.CreateContext(_db, 128);
            var cap2 = TestWorlds.AddSettlement(ctx2, RaceId.Orc, new Vec2i(40, 40), 20, out var orc2);
            orc2.Age = AgeId.Bronze;
            cap2.Tier = SettlementTier.Town;
            cap2.Stockpile.SetRaw(ResourceType.Wood, 100f);
            cap2.Stockpile.SetRaw(ResourceType.Stone, 100f);
            StrategyTestSupport.RunOnce(new CivilizationStrategySystem(), ctx2);
            Assert.AreEqual(1, CountWonderOrders(ctx2, orc2));
            Assert.AreEqual(BuildingType.WarIdol, cap2.BuildQueue[0].Type);
        }

        [Test]
        public void Wonder_NotQueued_WhenNotAvailable()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(40, 40), 20, out var orc);
            cap.Tier = SettlementTier.Village; // Dawn: War Idol needs Bronze, shared wonders need a Town
            cap.Stockpile.SetRaw(ResourceType.Wood, 100f);
            cap.Stockpile.SetRaw(ResourceType.Stone, 100f);
            TestWorlds.Run(new CivilizationStrategySystem(), ctx, 24 * 4);
            Assert.AreEqual(0, CountWonderOrders(ctx, orc));
        }

        [Test]
        public void Wonder_AscensionGateFirst_InAscensionAge()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(40, 40), 20, out var human);
            human.Age = AgeId.Ascension;
            cap.Tier = SettlementTier.Metropolis;
            cap.Stockpile.SetRaw(ResourceType.Stone, 100f);
            StrategyTestSupport.RunOnce(new CivilizationStrategySystem(), ctx);
            Assert.AreEqual(1, CountWonderOrders(ctx, human));
            Assert.AreEqual(BuildingType.AscensionGate, cap.BuildQueue[0].Type);
        }

        [Test]
        public void DeadCivsAndCivsWithoutSettlements_AreIgnored()
        {
            var ctx = TestWorlds.CreateContext(_db, 64);
            var dead = ctx.Ops.CreateCivilization(RaceId.Human);
            dead.IsAlive = false;
            var empty = ctx.Ops.CreateCivilization(RaceId.Elf);
            empty.CapitalId = 999;
            Assert.DoesNotThrow(() => TestWorlds.Run(new CivilizationStrategySystem(), ctx, 24 * 2));
            Assert.IsFalse(dead.Memory.ContainsKey(CivStrategyKeys.Posture));
            Assert.IsFalse(empty.Memory.ContainsKey(CivStrategyKeys.Posture));
        }

        [Test]
        public void Deterministic_AcrossIdenticalWorlds()
        {
            string Run()
            {
                var ctx = TestWorlds.CreateContext(_db, 128, 99);
                var a = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(30, 30), 12, out var orc);
                TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(60, 30), 12, out var human);
                a.Tier = SettlementTier.Town;
                a.Stockpile.SetRaw(ResourceType.Stone, 100f);
                orc.Age = AgeId.Bronze;
                StrategyTestSupport.StartWar(ctx, orc, human);
                TestWorlds.Run(new CivilizationStrategySystem(), ctx, 24 * 8);
                return Posture(orc) + "/" + Focus(orc) + "/" + Posture(human) + "/" + Focus(human) + "/" + string.Join("|", a.AiLog);
            }
            Assert.AreEqual(Run(), Run());
        }
    }
}
