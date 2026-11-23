using GodSim.Core;
using GodSim.Core.Settlements;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Settlements
{
    /// <summary>Farm fields: tilled on placement, released when the farm is gone, reusable by new farms only.</summary>
    public class FarmlandTests
    {
        [Test]
        public void DestroyedFarm_ReleasesItsFields_AndPublishesTheChange()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 51);
            var map = ctx.State.Map;
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 6, out _);
            PlanningTestKit.Enqueue(s, BuildingType.Farm);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 2);
            var farm = PlanningTestKit.BuildingsOf(ctx, s, BuildingType.Farm)[0];
            ctx.Ops.CompleteBuilding(farm);
            Assert.AreEqual(PlannerRoads.MaxFieldTiles, PlanningTestKit.CountFlag(map, TileFlags.Farmland));

            TerrainChangedEvent last = default(TerrainChangedEvent);
            int events = 0;
            ctx.Events.Subscribe<TerrainChangedEvent>(e => { events++; last = e; });
            ctx.Ops.DestroyBuilding(farm, DestroyCause.Fire);
            ctx.Ops.FlushRemovals();
            Assert.AreEqual(0, PlanningTestKit.CountFlag(map, TileFlags.Farmland), "orphaned fields are cleared");

            TestWorlds.Run(planner, ctx, 2, false);
            Assert.AreEqual(1, events, "renderers are told about the cleared fields");
            Assert.IsTrue(last.MinX >= farm.Origin.X - 1 && last.MaxX <= farm.Origin.X + 3);
            Assert.IsTrue(last.MinY >= farm.Origin.Y - 1 && last.MaxY <= farm.Origin.Y + 3);
        }

        [Test]
        public void SharedFields_StayWhileANeighbouringFarmLives()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 52);
            var map = ctx.State.Map;
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(10, 10), 6, out _);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 0);
            var a = ctx.Ops.CreateBuilding(s, BuildingType.Farm, new Vec2i(30, 30), 0, true);
            var b = ctx.Ops.CreateBuilding(s, BuildingType.Farm, new Vec2i(34, 30), 0, true);
            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            // Fields fill both rings: x 29..37, y 29..33, minus the two 3x3 footprints. Column 33 is shared.
            PlanningTestKit.PaintFlag(map, 29, 29, 9, 5, TileFlags.Farmland);
            for (int y = 29; y < 34; y++)
                for (int x = 29; x < 38; x++)
                    if (map.BuildingAt[map.Index(x, y)] >= 0) map.Clear(map.Index(x, y), TileFlags.Farmland);
            Assert.AreEqual(45 - 18, PlanningTestKit.CountFlag(map, TileFlags.Farmland));

            ctx.Ops.DestroyBuilding(a, DestroyCause.War);
            for (int y = 29; y < 34; y++)
                for (int x = 29; x < 38; x++)
                {
                    int i = map.Index(x, y);
                    if (map.BuildingAt[i] >= 0) continue;
                    bool nearB = x >= 33;
                    Assert.AreEqual(nearB, map.Has(i, TileFlags.Farmland), "tile (" + x + "," + y + ")");
                }
        }

        [Test]
        public void NewFarm_MayReuseFields_OtherBuildingsMayNot()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 53);
            var map = ctx.State.Map;
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 6, out _);
            var dirty = default(TerrainDirtyRect);
            new PlannerRoads().EnsurePlaza(ctx.State, s, ref dirty);
            for (int i = 0; i < map.TileCount; i++)
                if (map.IsFreeBuildable(i)) map.Set(i, TileFlags.Farmland);

            var house = PlanningTestKit.Enqueue(s, BuildingType.House);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 2);
            Assert.AreEqual(0, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House).Count, "houses never go on fields");
            Assert.AreEqual(1, house.Attempts);

            s.BuildQueue.Clear();
            PlanningTestKit.Enqueue(s, BuildingType.Farm);
            TestWorlds.Run(planner, ctx, 2, false);
            var farms = PlanningTestKit.BuildingsOf(ctx, s, BuildingType.Farm);
            Assert.AreEqual(1, farms.Count, "a farm may take over fields");
            var f = farms[0];
            for (int y = f.Origin.Y; y < f.Origin.Y + f.Height; y++)
                for (int x = f.Origin.X; x < f.Origin.X + f.Width; x++)
                    Assert.IsFalse(map.Has(map.Index(x, y), TileFlags.Farmland), "no field flag under the farm itself");
            Assert.IsTrue(PlanningTestKit.HasAdjacentRoad(map, f));
        }

        [Test]
        public void FootprintClaimable_AllowsFarmlandOnlyWhenAsked()
        {
            var map = TestWorlds.CreateContext(TestDb.CreateStub(), 64, 1).State.Map;
            map.Set(map.Index(5, 5), TileFlags.Farmland);
            Assert.IsFalse(PlacementRules.FootprintClaimable(map, 1, new Vec2i(4, 4), 3, 3));
            Assert.IsTrue(PlacementRules.FootprintClaimable(map, 1, new Vec2i(4, 4), 3, 3, true));
        }
    }
}
