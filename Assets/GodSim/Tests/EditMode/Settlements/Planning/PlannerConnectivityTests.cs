using GodSim.Core;
using GodSim.Core.Settlements;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Settlements
{
    /// <summary>Regression tests: every placed building must be reachable by road from its Town Center.</summary>
    public class PlannerConnectivityTests
    {
        [Test]
        public void LongGrowth_EveryBuildingIsRoadConnected_AndRoadsFormOneNetwork()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 41);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 60, out _);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 0);
            for (int k = 0; k < 400; k++)
            {
                var type = k % 5 == 2 ? BuildingType.Farm : (k % 7 == 3 ? BuildingType.Well : BuildingType.House);
                PlanningTestKit.Enqueue(s, type, 1f, k);
                TestWorlds.Run(planner, ctx, 2, false);
                PlanningTestKit.CompleteAll(ctx, s);
            }
            int checkedCount = PlanningTestKit.AssertAllRoadConnected(ctx, s);
            Assert.IsTrue(checkedCount >= 150, "placed " + checkedCount);
            Assert.AreEqual(1, PlanningTestKit.CountRoadComponents(ctx.State.Map), "no road islands or dead stubs");
        }

        [Test]
        public void WaterChannel_NoHouseIsCutOffFromTheTown()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 42);
            var map = ctx.State.Map;
            PlanningTestKit.PaintBiome(map, 0, 37, 64, 1, Biome.Lake);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 60, out _);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 0);
            for (int k = 0; k < 60; k++)
            {
                PlanningTestKit.Enqueue(s, BuildingType.House, 1f, k);
                TestWorlds.Run(planner, ctx, 2, false);
                PlanningTestKit.CompleteAll(ctx, s);
            }
            int houses = PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House).Count;
            Assert.IsTrue(houses >= 40, "houses " + houses);
            PlanningTestKit.AssertAllRoadConnected(ctx, s);
            foreach (var h in PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House))
                Assert.IsTrue(h.Origin.Y < 37, "house across the channel at " + h.Origin);
            Assert.AreEqual(1, PlanningTestKit.CountRoadComponents(map));
        }

        private static SimContext LakeShoreWorld(out Settlement s, out Building tc)
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 96, 43);
            var map = ctx.State.Map;
            PlanningTestKit.PaintBiome(map, 58, 6, 2, 84, Biome.Lake);
            PlanningTestKit.PaintFlag(map, 61, 6, 1, 84, TileFlags.Road);
            s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(48, 48), 6, out _);
            tc = ctx.State.Buildings.Get(s.TownCenterId);
            var dirty = default(TerrainDirtyRect);
            new PlannerRoads().EnsurePlaza(ctx.State, s, ref dirty);
            return ctx;
        }

        [Test]
        public void ConnectAccess_UsesLandRoute_WhenNearestRoadIsAcrossWater()
        {
            var ctx = LakeShoreWorld(out var s, out var tc);
            var map = ctx.State.Map;
            var hut = ctx.Ops.CreateBuilding(s, BuildingType.FishingHut, new Vec2i(56, 48));
            Assert.IsNotNull(hut);
            var access = PlacementRules.FindAccessTile(map, hut.Origin, 1, 1, s.Center, out _);
            var dirty = default(TerrainDirtyRect);
            Assert.IsTrue(new PlannerRoads().ConnectAccess(ctx.State, s, access, ref dirty));
            Assert.IsTrue(dirty.Any);
            Assert.IsTrue(PlanningTestKit.RoadConnects(map, hut, tc), "a land route exists even though the nearest road is across the lake");
            for (int i = 0; i < map.TileCount; i++)
                if (map.Has(i, TileFlags.Road)) Assert.IsFalse(map.IsWater(i));
        }

        [Test]
        public void PlannedFishingHut_OnLakeShore_IsLinkedToTheTown()
        {
            var ctx = LakeShoreWorld(out var s, out var tc);
            s.TerritoryRadius = 10;
            PlanningTestKit.Enqueue(s, BuildingType.FishingHut);
            TestWorlds.Run(new TownPlannerSystem(), ctx, 2);
            var huts = PlanningTestKit.BuildingsOf(ctx, s, BuildingType.FishingHut);
            Assert.AreEqual(1, huts.Count);
            Assert.IsTrue(PlanningTestKit.RoadConnects(ctx.State.Map, huts[0], tc));
        }

        [Test]
        public void ConnectAccess_FromSealedPocket_FailsWithoutLayingAStub()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 44);
            var map = ctx.State.Map;
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(20, 20), 6, out _);
            var dirty = default(TerrainDirtyRect);
            var roads = new PlannerRoads();
            roads.EnsurePlaza(ctx.State, s, ref dirty);
            PlanningTestKit.PaintRing(map, new Vec2i(30, 30), 2, Biome.Lake);
            int before = PlanningTestKit.CountFlag(map, TileFlags.Road);
            dirty = default(TerrainDirtyRect);
            Assert.IsFalse(roads.ConnectAccess(ctx.State, s, new Vec2i(30, 30), ref dirty));
            Assert.IsFalse(dirty.Any);
            Assert.AreEqual(before, PlanningTestKit.CountFlag(map, TileFlags.Road), "no dead-end stub");
        }

        [Test]
        public void CutOffIsland_IsNeverBuiltOn_AndCountsAsNoSpace()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 45);
            var map = ctx.State.Map;
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 6, out _);
            var dirty = default(TerrainDirtyRect);
            new PlannerRoads().EnsurePlaza(ctx.State, s, ref dirty);
            // Flood everything around the plaza, then raise a 3x3 island two tiles east of it: the only land a
            // house could stand on, with valid land entrances, but no road could ever leave it.
            for (int y = 20; y <= 45; y++)
                for (int x = 20; x <= 45; x++)
                {
                    int i = map.Index(x, y);
                    if (map.BuildingAt[i] < 0 && !map.Has(i, TileFlags.Road)) map.Biomes[i] = Biome.Lake;
                }
            PlanningTestKit.PaintBiome(map, 37, 32, 3, 3, Biome.Grassland);
            var order = PlanningTestKit.Enqueue(s, BuildingType.House);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 2);
            Assert.AreEqual(0, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House).Count, "the island has no road access");
            Assert.AreEqual(1, order.Attempts);
            TestWorlds.Run(planner, ctx, 6, false);
            Assert.AreEqual(0, s.BuildQueue.Count);
            Assert.AreEqual(7, s.TerritoryRadius, "unreachable land is no usable space: the territory grows");
            for (int y = 32; y < 35; y++)
                for (int x = 37; x < 40; x++)
                    Assert.IsFalse(map.Has(map.Index(x, y), TileFlags.Road), "no stub on the island");
        }
    }
}
