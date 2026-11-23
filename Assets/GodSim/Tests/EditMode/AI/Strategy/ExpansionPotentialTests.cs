using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Strategy
{
    /// <summary>Upgrade (2): site potential, army/rival avoidance, war/famine timing, balanced settlers, remembered sites.</summary>
    public class ExpansionPotentialTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp() { _db = StrategyTestSupport.CreateDb(); }

        private static Settlement Colony(SimContext ctx, Civilization civ, Settlement parent)
        {
            foreach (var s in StrategyTestSupport.SettlementsOf(ctx, civ)) if (s.Id != parent.Id) return s;
            return null;
        }

        [Test]
        public void HumansHoldExpansion_DuringWar_ButKeepScouting()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 40, out var civ, out var parent);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(10, 10), 6, out var elf);
            var war = StrategyTestSupport.StartWar(ctx, elf, civ);
            var sys = new ExpansionSystem();
            TestWorlds.Run(sys, ctx, 48 * 3);
            Assert.AreEqual(1, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
            Assert.AreEqual(ExpansionSystem.HoldWar, (int)civ.Mem(CivStrategyKeys.ExpansionHold));
            Assert.That((int)civ.Mem(CivStrategyKeys.SiteCount), Is.InRange(1, 3), "scouted sites are remembered");
            Assert.AreEqual(1, parent.AiLog.FindAll(l => l.StartsWith("Expansion: on hold (at war x1); scouted ")).Count);

            war.IsActive = false;
            TestWorlds.Run(sys, ctx, 48, initialize: false);
            Assert.AreEqual(2, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
            Assert.IsTrue(parent.AiLog.Exists(l => l.StartsWith("Expansion: resumed")));
        }

        [Test]
        public void ExpansionDrivenCivs_ExpandDespiteWar()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 40, out var civ, out var parent);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(10, 10), 6, out var elf);
            StrategyTestSupport.StartWar(ctx, elf, civ);
            AdvisorBoard.SetTrait(civ, AITrait.Expansionism, 0.9f);
            StrategyTestSupport.RunOnce(new ExpansionSystem(), ctx);
            Assert.AreEqual(2, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
            Assert.IsTrue(parent.AiLog.Exists(l => l.StartsWith("Expansion: founded ") && l.Contains("expansion-driven despite war")));
        }

        [Test]
        public void RecentFamine_HoldsExpansion()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 40, out var civ, out var parent);
            civ.Memory[CivStrategyKeys.FamineTick] = 40f; // reported just before the run at tick 48
            StrategyTestSupport.RunOnce(new ExpansionSystem(), ctx);
            Assert.AreEqual(1, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
            Assert.AreEqual(ExpansionSystem.HoldFamine, (int)civ.Mem(CivStrategyKeys.ExpansionHold));
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(3UL)]
        public void PrefersSites_WithResourcesTheCivLacks(ulong seed)
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 40, out var civ, out var parent, seed);
            var map = ctx.State.Map;
            for (int y = 62; y <= 66; y++)
                for (int x = 90; x <= 94; x++) map.Set(map.Index(x, y), TileFlags.OreDeposit);
            StrategyTestSupport.RunOnce(new ExpansionSystem(), ctx);
            var colony = Colony(ctx, civ, parent);
            Assert.IsNotNull(colony);
            Assert.LessOrEqual(colony.Center.ChebyshevTo(new Vec2i(92, 64)), ExpansionSystem.BuildableRadius + 3, "settles next to the ore");
            Assert.IsTrue(parent.AiLog.Exists(l => l.StartsWith("Expansion: founded ") && l.Contains("+ore")));
        }

        [TestCase(1UL)]
        [TestCase(2UL)]
        [TestCase(3UL)]
        public void NeverSettlesNearOrPastHostileArmies(ulong seed)
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 40, out var civ, out var parent, seed);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(120, 120), 6, out var orc);
            var rel = ctx.State.Diplomacy.Get(civ.Id, orc.Id);
            rel.HasContact = true;
            rel.Stance = DiplomaticStance.Hostile; // hostile but not at war: expansion is allowed, armies are avoided
            var armyAt = new Vec2i(90, 64);
            ctx.State.Diplomacy.Armies.Add(new Army { Id = ctx.State.Ids.Army(), CivilizationId = orc.Id, Position = armyAt, State = ArmyState.Marching });
            StrategyTestSupport.RunOnce(new ExpansionSystem(), ctx);
            var colony = Colony(ctx, civ, parent);
            Assert.IsNotNull(colony);
            Assert.Greater(colony.Center.DistanceTo(armyAt), ExpansionSystem.ArmyAvoidRadius - 2f);
            Assert.IsTrue(parent.AiLog.Exists(l => l.Contains("near armies")));
        }

        [Test]
        public void HostileNeighbours_PushSitesAway()
        {
            float near = 0f, far = 0f;
            for (ulong seed = 1; seed <= 3; seed++)
            {
                var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 40, out var civ, out var parent, seed);
                TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(64, 100), 6, out var orc);
                var rel = ctx.State.Diplomacy.Get(civ.Id, orc.Id);
                rel.HasContact = true;
                rel.Stance = DiplomaticStance.Hostile;
                StrategyTestSupport.RunOnce(new ExpansionSystem(), ctx);
                var colony = Colony(ctx, civ, parent);
                near += colony.Center.DistanceTo(new Vec2i(64, 100));
                far += 1f;
            }
            Assert.Greater(near / far, 30f, "colonies are planted away from the hostile town");
        }

        [Test]
        public void RemembersTopSites_Distinct_AndReusesThem()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 70, out var civ, out var parent);
            var sys = new ExpansionSystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            int n = (int)civ.Mem(CivStrategyKeys.SiteCount);
            Assert.That(n, Is.InRange(1, CivStrategyKeys.MaxRememberedSites));
            int spacing = _db.Race(RaceId.Human).MinSettlementSpacing;
            var colony = Colony(ctx, civ, parent);
            for (int i = 0; i < n; i++)
            {
                var a = new Vec2i((int)civ.Mem(CivStrategyKeys.SiteX(i)), (int)civ.Mem(CivStrategyKeys.SiteY(i)));
                Assert.IsTrue(ctx.State.Map.InBounds(a));
                Assert.GreaterOrEqual(a.ChebyshevTo(colony.Center), spacing / 2, "the settled site is not remembered");
                for (int j = i + 1; j < n; j++)
                    Assert.GreaterOrEqual(a.ChebyshevTo(new Vec2i((int)civ.Mem(CivStrategyKeys.SiteX(j)), (int)civ.Mem(CivStrategyKeys.SiteY(j)))), spacing);
            }
        }

        [Test]
        public void Settlers_AreBalanced_AndNeverTheOnlySpecialist()
        {
            var ctx = StrategyTestSupport.CreateExpansionWorld(_db, RaceId.Human, 40, out var civ, out var parent);
            Person smith = null;
            var former = new System.Collections.Generic.Dictionary<int, JobType>();
            for (int i = 0; i < parent.ResidentIds.Count; i++)
            {
                var p = ctx.State.People.Get(parent.ResidentIds[i]);
                if (i == 7) { p.Job = JobType.Smith; smith = p; }
                else if (i % 8 == 0) p.Job = JobType.Builder;
                else if (i % 8 == 1) p.Job = JobType.Woodcutter;
                else p.Job = JobType.Farmer;
                former[p.Id] = p.Job;
            }
            StrategyTestSupport.RunOnce(new ExpansionSystem(), ctx);
            var colony = Colony(ctx, civ, parent);
            Assert.IsNotNull(colony);
            // Migration clears jobs (WorldOps), so count by the job each settler held at home.
            int builders = 0, woodcutters = 0, farmers = 0;
            foreach (int id in colony.ResidentIds)
            {
                if (former[id] == JobType.Builder) builders++;
                if (former[id] == JobType.Woodcutter) woodcutters++;
                if (former[id] == JobType.Farmer) farmers++;
            }
            Assert.GreaterOrEqual(builders, 1);
            Assert.GreaterOrEqual(woodcutters, 1);
            Assert.GreaterOrEqual(farmers, 2);
            Assert.AreEqual(parent.Id, smith.SettlementId, "the only smith stays home");
            Assert.IsTrue(parent.AiLog.Exists(l => l.StartsWith("Expansion: founded ") && l.Contains("[food ") && l.Contains("skill ")));
        }

        [Test]
        public void ChokepointSites_GetABonus()
        {
            // An isthmus: water everywhere except a horizontal land bridge (rows 59..69) - sites on the bridge are chokepoints.
            var ctx = TestWorlds.CreateContext(_db, 128, 3, Biome.ShallowWater);
            var map = ctx.State.Map;
            for (int y = 59; y <= 69; y++)
                for (int x = 0; x < 128; x++) { map.Biomes[map.Index(x, y)] = Biome.Grassland; map.Elevation[map.Index(x, y)] = 0.2f; }
            var parent = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(64, 64), 40, out var civ);
            StrategyTestSupport.MakeHealthy(parent);
            StrategyTestSupport.RunOnce(new ExpansionSystem(), ctx);
            Assert.AreEqual(2, StrategyTestSupport.SettlementsOf(ctx, civ).Count);
            Assert.IsTrue(parent.AiLog.Exists(l => l.StartsWith("Expansion: founded ") && l.Contains("chokepoint")), string.Join("\n", parent.AiLog));
        }
    }
}
