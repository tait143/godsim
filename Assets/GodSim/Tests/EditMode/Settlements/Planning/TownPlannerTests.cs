using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Settlements;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Settlements
{
    public class TownPlannerTests
    {
        private static Settlement NewTown(SimContext ctx, int adults, out Civilization civ, RaceId race = RaceId.Human)
        {
            return TestWorlds.AddSettlement(ctx, race, new Vec2i(32, 32), adults, out civ);
        }

        [Test]
        public void SystemShape_MatchesContract()
        {
            var planner = new TownPlannerSystem();
            var roads = new RoadNetworkSystem();
            Assert.AreEqual(2, planner.TickInterval);
            Assert.AreEqual(24, roads.TickInterval);
            Assert.IsFalse(string.IsNullOrEmpty(planner.Name));
            Assert.IsFalse(string.IsNullOrEmpty(roads.Name));
        }

        [TestCase(0, 1)]
        [TestCase(11, 1)]
        [TestCase(12, 2)]
        [TestCase(30, 3)]
        [TestCase(500, 6)]
        public void SiteCap_FollowsPopulation(int population, int expected)
        {
            Assert.AreEqual(expected, TownPlannerSystem.SiteCapFor(population));
        }

        [Test]
        public void HouseOrder_PlacedInsideTerritoryNearCenter()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 11);
            var s = NewTown(ctx, 6, out _);
            PlanningTestKit.Enqueue(s, BuildingType.House);
            TestWorlds.Run(new TownPlannerSystem(), ctx, 2);

            var houses = PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House);
            Assert.AreEqual(1, houses.Count);
            var h = houses[0];
            Assert.IsFalse(h.IsComplete, "planner places construction sites, not finished buildings");
            Assert.IsTrue(h.Center.DistanceTo(s.Center) <= s.TerritoryRadius + 2);
            Assert.AreEqual(s.Id, ctx.State.Map.Owner[ctx.State.Map.Index(h.Origin)]);
            Assert.AreEqual(0, s.BuildQueue.Count);
            Assert.IsTrue(s.AiLog.Exists(l => l.StartsWith("Placed House at (")), "AI log should record the placement");
        }

        [Test]
        public void PlacedHouse_IsConnectedByRoadToTownCenter()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 5);
            var s = NewTown(ctx, 6, out _);
            PlanningTestKit.Enqueue(s, BuildingType.House);
            TestWorlds.Run(new TownPlannerSystem(), ctx, 2);

            var map = ctx.State.Map;
            var house = PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House)[0];
            var tc = ctx.State.Buildings.Get(s.TownCenterId);
            Assert.IsTrue(PlanningTestKit.HasAdjacentRoad(map, house));
            Assert.IsTrue(PlanningTestKit.RoadConnects(map, house, tc), "access road must link the house to the plaza");
            Assert.IsTrue(map.TerrainVersion > 0);
        }

        [Test]
        public void FishingHut_NotPlacedWithoutWater()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 3);
            var s = NewTown(ctx, 6, out _);
            var order = PlanningTestKit.Enqueue(s, BuildingType.FishingHut);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 2);
            Assert.AreEqual(1, order.Attempts);
            Assert.AreEqual(1, s.BuildQueue.Count);

            TestWorlds.Run(planner, ctx, 10, false);
            Assert.AreEqual(0, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.FishingHut).Count);
            Assert.AreEqual(0, s.BuildQueue.Count, "order dropped after 4 failures");
            Assert.AreEqual(6, s.TerritoryRadius, "a missing requirement is not a lack of space");
            Assert.IsTrue(s.AiLog.Exists(l => l.StartsWith("Gave up on FishingHut")));
        }

        [Test]
        public void FishingHut_PlacedNearLake()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 3);
            var map = ctx.State.Map;
            PlanningTestKit.PaintBiome(map, 39, 29, 4, 7, Biome.Lake);
            var s = NewTown(ctx, 6, out _);
            PlanningTestKit.Enqueue(s, BuildingType.FishingHut);
            TestWorlds.Run(new TownPlannerSystem(), ctx, 2);

            var huts = PlanningTestKit.BuildingsOf(ctx, s, BuildingType.FishingHut);
            Assert.AreEqual(1, huts.Count);
            Assert.IsFalse(map.IsWater(huts[0].Origin));
            Assert.IsTrue(PlanningTestKit.DistanceTo(map, huts[0], i => map.IsWater(i)) <= 3);
        }

        [Test]
        public void Mine_PlacedNextToOre()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 9);
            var map = ctx.State.Map;
            PlanningTestKit.PaintFlag(map, 39, 36, 2, 2, TileFlags.OreDeposit);
            var s = NewTown(ctx, 6, out _);
            PlanningTestKit.Enqueue(s, BuildingType.Mine);
            TestWorlds.Run(new TownPlannerSystem(), ctx, 2);

            var mines = PlanningTestKit.BuildingsOf(ctx, s, BuildingType.Mine);
            Assert.AreEqual(1, mines.Count);
            Assert.IsTrue(PlanningTestKit.DistanceTo(map, mines[0], i => map.Has(i, TileFlags.OreDeposit)) <= 3);
        }

        [Test]
        public void UnavailableBuilding_IsDropped_ThenAllowedAfterAgeAdvance()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 2);
            var s = NewTown(ctx, 6, out var civ);
            PlanningTestKit.Enqueue(s, BuildingType.Temple);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 2);
            Assert.AreEqual(0, s.BuildQueue.Count);
            Assert.AreEqual(0, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.Temple).Count);
            Assert.IsTrue(s.AiLog.Exists(l => l.StartsWith("Dropped Temple")));

            civ.Age = AgeId.Iron;
            PlanningTestKit.Enqueue(s, BuildingType.Temple);
            TestWorlds.Run(planner, ctx, 2, false);
            var temples = PlanningTestKit.BuildingsOf(ctx, s, BuildingType.Temple);
            Assert.AreEqual(1, temples.Count);
            Assert.IsTrue(temples[0].Center.DistanceTo(s.Center) <= 5f, "Central buildings hug the town center");
        }

        [Test]
        public void MaxPerSettlement_IsRespected()
        {
            var db = PlanningTestKit.CreateDb(d => d.Register(new BuildingDefinition { Type = BuildingType.Well, Name = "Well", Category = BuildingCategory.Infrastructure, MaxPerSettlement = 1 }));
            var ctx = TestWorlds.CreateContext(db, 64, 4);
            var s = NewTown(ctx, 30, out _);
            PlanningTestKit.Enqueue(s, BuildingType.Well);
            PlanningTestKit.Enqueue(s, BuildingType.Well);
            TestWorlds.Run(new TownPlannerSystem(), ctx, 6);
            Assert.AreEqual(1, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.Well).Count);
            Assert.AreEqual(0, s.BuildQueue.Count);
        }

        [Test]
        public void UniquePerCivilization_IsRespectedAcrossSettlements()
        {
            var db = PlanningTestKit.CreateDb(d => d.Register(new BuildingDefinition { Type = BuildingType.GreatLibrary, Name = "Great Library", Category = BuildingCategory.Wonder, UniquePerCivilization = true, IsWonder = true }));
            var ctx = TestWorlds.CreateContext(db, 64, 4);
            var a = NewTown(ctx, 6, out var civ);
            var b = ctx.Ops.FoundSettlement(civ, new Vec2i(10, 10));
            PlanningTestKit.Enqueue(a, BuildingType.GreatLibrary);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 2);
            Assert.AreEqual(1, PlanningTestKit.BuildingsOf(ctx, a, BuildingType.GreatLibrary).Count);
            PlanningTestKit.Enqueue(b, BuildingType.GreatLibrary);
            TestWorlds.Run(planner, ctx, 2, false);
            Assert.AreEqual(0, PlanningTestKit.BuildingsOf(ctx, b, BuildingType.GreatLibrary).Count);
            Assert.AreEqual(0, b.BuildQueue.Count);
        }

        [Test]
        public void ConstructionSiteCap_IsRespected_HousesMayExceedWhileHomeless()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 6);
            var s = NewTown(ctx, 6, out _);
            for (int i = 0; i < 3; i++) PlanningTestKit.Enqueue(s, BuildingType.Well);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 8);
            Assert.AreEqual(1, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.Well).Count);
            Assert.AreEqual(1, TownPlannerSystem.CountConstructionSites(ctx.State, s));
            Assert.AreEqual(2, s.BuildQueue.Count);
            Assert.AreEqual(0, s.BuildQueue[0].Attempts, "waiting for the cap is not a failure");

            s.Homeless = 3;
            PlanningTestKit.Enqueue(s, BuildingType.House, 5f);
            TestWorlds.Run(planner, ctx, 2, false);
            Assert.AreEqual(1, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House).Count);

            PlanningTestKit.Enqueue(s, BuildingType.House, 5f);
            TestWorlds.Run(planner, ctx, 2, false);
            Assert.AreEqual(1, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House).Count, "only one extra site for houses");
            Assert.AreEqual(2, TownPlannerSystem.CountConstructionSites(ctx.State, s));

            PlanningTestKit.CompleteAll(ctx, s);
            TestWorlds.Run(planner, ctx, 2, false);
            Assert.AreEqual(2, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House).Count, "highest priority first once a site frees up");
        }

        [Test]
        public void Queue_IsProcessedByPriorityThenRequestTick()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 8);
            var s = NewTown(ctx, 6, out _);
            PlanningTestKit.Enqueue(s, BuildingType.Well, 1f, 0);
            PlanningTestKit.Enqueue(s, BuildingType.House, 5f, 10);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 2);
            Assert.AreEqual(1, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House).Count);
            Assert.AreEqual(0, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.Well).Count);

            PlanningTestKit.CompleteAll(ctx, s);
            s.BuildQueue.Clear();
            PlanningTestKit.Enqueue(s, BuildingType.House, 1f, 5);
            PlanningTestKit.Enqueue(s, BuildingType.Well, 1f, 2);
            TestWorlds.Run(planner, ctx, 2, false);
            Assert.AreEqual(1, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.Well).Count, "equal priority: earlier request first");
            Assert.AreEqual(BuildingType.House, s.BuildQueue[0].Type);
        }

        [Test]
        public void Farm_MarksFarmland_AndOtherBuildingsAvoidIt()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 12);
            var map = ctx.State.Map;
            var s = NewTown(ctx, 6, out _);
            PlanningTestKit.Enqueue(s, BuildingType.Farm);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 2);

            var farms = PlanningTestKit.BuildingsOf(ctx, s, BuildingType.Farm);
            Assert.AreEqual(1, farms.Count);
            var farm = farms[0];
            int fields = PlanningTestKit.CountFlag(map, TileFlags.Farmland);
            Assert.IsTrue(fields >= 1 && fields <= 8, "fields: " + fields);
            for (int i = 0; i < map.TileCount; i++)
            {
                if (!map.Has(i, TileFlags.Farmland)) continue;
                var p = map.PosOf(i);
                Assert.IsTrue(map.BuildingAt[i] < 0);
                Assert.IsFalse(map.Has(i, TileFlags.Road));
                int dx = p.X < farm.Origin.X ? farm.Origin.X - p.X : (p.X >= farm.Origin.X + 3 ? p.X - farm.Origin.X - 2 : 0);
                int dy = p.Y < farm.Origin.Y ? farm.Origin.Y - p.Y : (p.Y >= farm.Origin.Y + 3 ? p.Y - farm.Origin.Y - 2 : 0);
                Assert.AreEqual(1, System.Math.Max(dx, dy), "fields are adjacent to the farm");
            }

            PlanningTestKit.CompleteAll(ctx, s);
            for (int k = 0; k < 14; k++)
            {
                PlanningTestKit.Enqueue(s, BuildingType.House);
                TestWorlds.Run(planner, ctx, 2, false);
                PlanningTestKit.CompleteAll(ctx, s);
            }
            Assert.IsTrue(PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House).Count >= 10);
            Assert.AreEqual(fields, PlanningTestKit.CountFlag(map, TileFlags.Farmland));
            for (int i = 0; i < map.TileCount; i++)
                if (map.Has(i, TileFlags.Farmland))
                {
                    Assert.IsTrue(map.BuildingAt[i] < 0, "nothing is built on fields");
                    Assert.IsFalse(map.Has(i, TileFlags.Road), "roads never cross fields");
                }
        }

        [Test]
        public void SpaceFailure_ExpandsTerritory()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 13, Biome.Mountain);
            var map = ctx.State.Map;
            PlanningTestKit.PaintBiome(map, 29, 29, 8, 8, Biome.Grassland);
            var s = NewTown(ctx, 6, out _);
            Assert.AreEqual(6, s.TerritoryRadius);
            PlanningTestKit.Enqueue(s, BuildingType.Farm);
            TestWorlds.Run(new TownPlannerSystem(), ctx, 8);
            Assert.AreEqual(0, s.BuildQueue.Count);
            Assert.AreEqual(0, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.Farm).Count);
            Assert.AreEqual(7, s.TerritoryRadius, "no space -> claim one more ring");
        }

        [Test]
        public void SameSeed_GivesSamePlacements()
        {
            var a = Simulate(77);
            var b = Simulate(77);
            Assert.IsTrue(a.Count >= 6, "placed " + a.Count);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i]);
        }

        private static List<Vec2i> Simulate(ulong seed)
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, seed);
            PlanningTestKit.PaintBiome(ctx.State.Map, 40, 24, 5, 5, Biome.Lake);
            var s = NewTown(ctx, 36, out _);
            PlanningTestKit.Enqueue(s, BuildingType.Farm, 2f);
            PlanningTestKit.Enqueue(s, BuildingType.FishingHut, 1.5f);
            for (int i = 0; i < 6; i++) PlanningTestKit.Enqueue(s, BuildingType.House, 1f, i);
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 2);
            for (int k = 0; k < 6; k++) { PlanningTestKit.CompleteAll(ctx, s); TestWorlds.Run(planner, ctx, 2, false); }
            var origins = new List<Vec2i>();
            var items = ctx.State.Buildings.Items;
            for (int i = 0; i < items.Count; i++) origins.Add(items[i].Origin);
            return origins;
        }

        [Test]
        public void NothingIsEverPlacedOnWaterOrOutOfBounds()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 21);
            var map = ctx.State.Map;
            PlanningTestKit.PaintBiome(map, 0, 0, 30, 64, Biome.DeepOcean);
            PlanningTestKit.PaintBiome(map, 30, 40, 3, 6, Biome.Lake);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(34, 59), 72, out _);
            s.Homeless = 10;
            var planner = new TownPlannerSystem();
            TestWorlds.Run(planner, ctx, 0);
            for (int k = 0; k < 40; k++)
            {
                PlanningTestKit.Enqueue(s, k % 7 == 3 ? BuildingType.Farm : (k % 5 == 1 ? BuildingType.FishingHut : BuildingType.House));
                TestWorlds.Run(planner, ctx, 2, false);
                PlanningTestKit.CompleteAll(ctx, s);
            }
            int placed = 0;
            var items = ctx.State.Buildings.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                placed++;
                for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                    for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                    {
                        Assert.IsTrue(map.InBounds(x, y), b.Type + " out of bounds at " + b.Origin);
                        Assert.IsFalse(map.IsWater(map.Index(x, y)), b.Type + " on water at " + b.Origin);
                    }
                if (b.Type != BuildingType.TownCenter) Assert.IsTrue(PlanningTestKit.HasAdjacentRoad(map, b), b.Type + " has no access road");
            }
            Assert.IsTrue(placed >= 20, "placed " + placed);
            for (int i = 0; i < map.TileCount; i++)
                if (map.Has(i, TileFlags.Road) || map.Has(i, TileFlags.Farmland)) Assert.IsFalse(map.IsWater(i));
        }

        [Test]
        public void BadData_DoesNotThrow()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 1);
            var s = NewTown(ctx, 6, out _);
            s.BuildQueue.Add(null);
            s.BuildQueue.Add(new BuildOrder { Type = (BuildingType)250, Priority = float.NaN });
            PlanningTestKit.Enqueue(s, BuildingType.House, float.PositiveInfinity);
            var dead = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(10, 50), 2, out var deadCiv);
            PlanningTestKit.Enqueue(dead, BuildingType.House);
            deadCiv.IsAlive = false;
            Assert.DoesNotThrow(() => TestWorlds.Run(new TownPlannerSystem(), ctx, 4));
            Assert.AreEqual(1, PlanningTestKit.BuildingsOf(ctx, s, BuildingType.House).Count);
            Assert.AreEqual(0, PlanningTestKit.BuildingsOf(ctx, dead, BuildingType.House).Count);
            Assert.AreEqual(0, s.BuildQueue.Count);
        }

        [Test]
        public void FormatScore_IsCultureIndependent()
        {
            Assert.AreEqual("0.83", TownPlannerSystem.FormatScore(0.834f));
            Assert.AreEqual("1.05", TownPlannerSystem.FormatScore(1.05f));
            Assert.AreEqual("-0.20", TownPlannerSystem.FormatScore(-0.2f));
            Assert.AreEqual("0.00", TownPlannerSystem.FormatScore(float.NaN));
        }
    }
}
