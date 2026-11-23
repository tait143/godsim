using GodSim.Core;
using GodSim.Core.Settlements;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Settlements
{
    public class RoadNetworkTests
    {
        [Test]
        public void ConnectsNearestSameCivSettlements_Once()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 31);
            var map = ctx.State.Map;
            var civ = ctx.Ops.CreateCivilization(RaceId.Human);
            var a = ctx.Ops.FoundSettlement(civ, new Vec2i(10, 30));
            var b = ctx.Ops.FoundSettlement(civ, new Vec2i(46, 34));
            a.Tier = SettlementTier.Hamlet;
            b.Tier = SettlementTier.Hamlet;
            int events = 0;
            ctx.Events.Subscribe<TerrainChangedEvent>(e => events++);

            var system = new RoadNetworkSystem();
            TestWorlds.Run(system, ctx, 24);
            Assert.AreEqual(1f, a.Mem(RoadNetworkSystem.RoadMemoryKey(b.Id)));
            Assert.AreEqual(1f, b.Mem(RoadNetworkSystem.RoadMemoryKey(a.Id)));
            Assert.IsTrue(events >= 1);
            var tcA = ctx.State.Buildings.Get(a.TownCenterId);
            var tcB = ctx.State.Buildings.Get(b.TownCenterId);
            Assert.IsTrue(PlanningTestKit.RoadConnects(map, tcA, tcB));

            int roads = PlanningTestKit.CountFlag(map, TileFlags.Road);
            int version = map.TerrainVersion;
            TestWorlds.Run(system, ctx, 72, false);
            Assert.AreEqual(roads, PlanningTestKit.CountFlag(map, TileFlags.Road), "connected once, never rebuilt");
            Assert.AreEqual(version, map.TerrainVersion);
            for (int i = 0; i < map.TileCount; i++)
                if (map.Has(i, TileFlags.Road)) Assert.IsTrue(map.BuildingAt[i] < 0, "roads avoid buildings");
        }

        [Test]
        public void RoadDetoursAroundWater()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 32);
            var map = ctx.State.Map;
            PlanningTestKit.PaintBiome(map, 27, 10, 4, 44, Biome.Lake);
            var civ = ctx.Ops.CreateCivilization(RaceId.Elf);
            var a = ctx.Ops.FoundSettlement(civ, new Vec2i(12, 32));
            var b = ctx.Ops.FoundSettlement(civ, new Vec2i(46, 32));
            a.Tier = SettlementTier.Village;
            TestWorlds.Run(new RoadNetworkSystem(), ctx, 24);
            Assert.AreEqual(1f, a.Mem(RoadNetworkSystem.RoadMemoryKey(b.Id)));
            Assert.IsTrue(PlanningTestKit.RoadConnects(map, ctx.State.Buildings.Get(a.TownCenterId), ctx.State.Buildings.Get(b.TownCenterId)));
            for (int i = 0; i < map.TileCount; i++)
                if (map.Has(i, TileFlags.Road)) Assert.IsFalse(map.IsWater(i));
        }

        [Test]
        public void CampTier_DistantAndForeignSettlements_AreNotConnected()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 96, 33);
            var map = ctx.State.Map;
            var civ = ctx.Ops.CreateCivilization(RaceId.Human);
            var camp1 = ctx.Ops.FoundSettlement(civ, new Vec2i(10, 10));
            var camp2 = ctx.Ops.FoundSettlement(civ, new Vec2i(30, 10));
            var far = ctx.Ops.FoundSettlement(civ, new Vec2i(85, 85));
            far.Tier = SettlementTier.Town;
            var other = ctx.Ops.CreateCivilization(RaceId.Orc);
            var foreign = ctx.Ops.FoundSettlement(other, new Vec2i(70, 60));
            foreign.Tier = SettlementTier.Town;
            TestWorlds.Run(new RoadNetworkSystem(), ctx, 48);
            Assert.AreEqual(0, PlanningTestKit.CountFlag(map, TileFlags.Road));
            Assert.AreEqual(0f, camp1.Mem(RoadNetworkSystem.RoadMemoryKey(camp2.Id)));
            Assert.AreEqual(0f, far.Mem(RoadNetworkSystem.RoadMemoryKey(foreign.Id)));
        }

        private static Settlement Hamlet(SimContext ctx, Civilization civ, Vec2i at)
        {
            var s = ctx.Ops.FoundSettlement(civ, at);
            s.Tier = SettlementTier.Hamlet;
            return s;
        }

        [Test]
        public void HopelessLinks_DoNotStarveLaterSettlements()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 160, 35);
            var map = ctx.State.Map;
            var civ = ctx.Ops.CreateCivilization(RaceId.Human);
            for (int k = 0; k < 8; k++)
            {
                var c = new Vec2i(16 + 16 * k, 20);
                PlanningTestKit.PaintRing(map, c, 5, Biome.Lake);
                Hamlet(ctx, civ, c);
            }
            var p1 = Hamlet(ctx, civ, new Vec2i(40, 120));
            var p2 = Hamlet(ctx, civ, new Vec2i(70, 120));
            var system = new RoadNetworkSystem();
            TestWorlds.Run(system, ctx, 24 * 12);
            Assert.AreEqual(RoadNetworkSystem.Connected, p1.Mem(RoadNetworkSystem.RoadMemoryKey(p2.Id)));
            Assert.AreEqual(RoadNetworkSystem.Connected, p2.Mem(RoadNetworkSystem.RoadMemoryKey(p1.Id)));
            Assert.IsTrue(PlanningTestKit.RoadConnects(map, ctx.State.Buildings.Get(p1.TownCenterId), ctx.State.Buildings.Get(p2.TownCenterId)));

            // Eventually every hopeless link is abandoned and the system goes quiet.
            TestWorlds.Run(system, ctx, 24 * 240, false);
            TestWorlds.Run(system, ctx, 24, false);
            Assert.AreEqual(0, system.LastRunSearches, "impossible links stop costing searches");
        }

        [Test]
        public void FailedLink_IsSearchedOnce_RetriedWithBackoff_ThenAbandoned()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 36);
            var map = ctx.State.Map;
            var civ = ctx.Ops.CreateCivilization(RaceId.Human);
            var island = new Vec2i(16, 32);
            PlanningTestKit.PaintRing(map, island, 5, Biome.Lake);
            var a = Hamlet(ctx, civ, island);
            var b = Hamlet(ctx, civ, new Vec2i(46, 32));
            string failAB = RoadNetworkSystem.FailMemoryKey(b.Id), failBA = RoadNetworkSystem.FailMemoryKey(a.Id);
            var system = new RoadNetworkSystem();

            TestWorlds.Run(system, ctx, 24); // tick 24: first attempt
            Assert.AreEqual(1, system.LastRunSearches, "the two ends never repeat the same search");
            Assert.AreEqual(1f, a.Mem(failAB));
            Assert.AreEqual(1f, b.Mem(failBA));
            Assert.AreEqual(24f, a.Mem(RoadNetworkSystem.RetryMemoryKey(b.Id)));
            Assert.AreEqual(24f, b.Mem(RoadNetworkSystem.RetryMemoryKey(a.Id)));

            TestWorlds.Run(system, ctx, 72, false); // ticks 48..96: still backing off
            Assert.AreEqual(1f, a.Mem(failAB), "not retried before 96 ticks");
            Assert.AreEqual(0, system.LastRunSearches);

            TestWorlds.Run(system, ctx, 24, false); // tick 120 = 24 + 96
            Assert.AreEqual(2f, a.Mem(failAB), "retried after 96 ticks");

            TestWorlds.Run(system, ctx, 168, false); // tick 288 < 120 + 192
            Assert.AreEqual(2f, a.Mem(failAB), "second retry waits twice as long");
            TestWorlds.Run(system, ctx, 24, false); // tick 312
            Assert.AreEqual(RoadNetworkSystem.GaveUp, a.Mem(RoadNetworkSystem.RoadMemoryKey(b.Id)));
            Assert.AreEqual(RoadNetworkSystem.GaveUp, b.Mem(RoadNetworkSystem.RoadMemoryKey(a.Id)));
            Assert.IsFalse(a.Memory.ContainsKey(failAB));
            Assert.IsFalse(b.Memory.ContainsKey(RoadNetworkSystem.RetryMemoryKey(a.Id)));

            TestWorlds.Run(system, ctx, 24 * 20, false);
            Assert.AreEqual(0, system.LastRunSearches);
            Assert.AreEqual(0, PlanningTestKit.CountFlag(map, TileFlags.Road) - PlazaTiles(ctx, a) - PlazaTiles(ctx, b), "no partial road was laid");
        }

        [Test]
        public void AbandonedLink_FallsBackToNextNearestSettlement()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 96, 37);
            var map = ctx.State.Map;
            var civ = ctx.Ops.CreateCivilization(RaceId.Human);
            var island = new Vec2i(30, 20);
            PlanningTestKit.PaintRing(map, island, 5, Biome.Lake);
            var a = Hamlet(ctx, civ, new Vec2i(16, 20));
            var b = Hamlet(ctx, civ, island);
            var c = Hamlet(ctx, civ, new Vec2i(16, 60));
            var d = Hamlet(ctx, civ, new Vec2i(16, 72));
            TestWorlds.Run(new RoadNetworkSystem(), ctx, 24 * 20);
            Assert.AreEqual(RoadNetworkSystem.GaveUp, a.Mem(RoadNetworkSystem.RoadMemoryKey(b.Id)));
            Assert.AreEqual(RoadNetworkSystem.Connected, c.Mem(RoadNetworkSystem.RoadMemoryKey(d.Id)), "C and D are each other's nearest");
            Assert.AreEqual(RoadNetworkSystem.Connected, a.Mem(RoadNetworkSystem.RoadMemoryKey(c.Id)), "A links to its nearest reachable settlement instead");
        }

        private static int PlazaTiles(SimContext ctx, Settlement s)
        {
            var map = ctx.State.Map;
            var tc = ctx.State.Buildings.Get(s.TownCenterId);
            int n = 0;
            for (int y = tc.Origin.Y - 1; y <= tc.Origin.Y + tc.Height; y++)
                for (int x = tc.Origin.X - 1; x <= tc.Origin.X + tc.Width; x++)
                    if (map.InBounds(x, y) && map.Has(map.Index(x, y), TileFlags.Road)) n++;
            return n;
        }

        [Test]
        public void PlannerAndNetwork_RunTogether()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 34);
            var civ = ctx.Ops.CreateCivilization(RaceId.Dwarf);
            var a = ctx.Ops.FoundSettlement(civ, new Vec2i(14, 20));
            var b = ctx.Ops.FoundSettlement(civ, new Vec2i(44, 44));
            for (int i = 0; i < 14; i++) ctx.Ops.CreatePerson(a, RaceId.Dwarf, i % 2 == 0 ? Sex.Female : Sex.Male, 25f);
            a.Tier = SettlementTier.Hamlet;
            for (int i = 0; i < 4; i++) PlanningTestKit.Enqueue(a, BuildingType.House, 1f, i);
            var systems = new ISimSystem[] { new TownPlannerSystem(), new RoadNetworkSystem() };
            Assert.DoesNotThrow(() => TestWorlds.RunAll(systems, ctx, 48));
            Assert.IsTrue(PlanningTestKit.BuildingsOf(ctx, a, BuildingType.House).Count >= 2);
            Assert.AreEqual(1f, a.Mem(RoadNetworkSystem.RoadMemoryKey(b.Id)));
        }
    }
}
