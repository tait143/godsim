using GodSim.Core;
using GodSim.Core.Settlements;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Settlements
{
    public class PlacementRulesTests
    {
        private WorldMap _map;

        [SetUp]
        public void SetUp()
        {
            _map = TestWorlds.CreateContext(TestDb.CreateStub(), 64, 1).State.Map;
        }

        [Test]
        public void NearWater_RequiresRiverOrWaterWithinThree()
        {
            var o = new Vec2i(20, 20);
            Assert.IsFalse(PlacementRules.MeetsRequirements(_map, PlacementRule.NearWater, o, 1, 1));
            _map.Set(_map.Index(24, 20), TileFlags.River);
            Assert.IsFalse(PlacementRules.MeetsRequirements(_map, PlacementRule.NearWater, o, 1, 1), "4 tiles away is too far");
            _map.Set(_map.Index(23, 21), TileFlags.River);
            Assert.IsTrue(PlacementRules.MeetsRequirements(_map, PlacementRule.NearWater, o, 1, 1));
            Assert.IsTrue(PlacementRules.MeetsRequirements(_map, PlacementRule.NearRiver, o, 1, 1));
        }

        [Test]
        public void Coastal_RequiresAdjacentWater()
        {
            PlanningTestKit.PaintBiome(_map, 30, 10, 3, 3, Biome.Lake);
            Assert.IsTrue(PlacementRules.MeetsRequirements(_map, PlacementRule.Coastal, new Vec2i(28, 10), 2, 2));
            Assert.IsFalse(PlacementRules.MeetsRequirements(_map, PlacementRule.Coastal, new Vec2i(27, 10), 2, 2));
            Assert.IsTrue(PlacementRules.MeetsRequirements(_map, PlacementRule.NearWater, new Vec2i(27, 10), 2, 2));
        }

        [Test]
        public void Fertile_UsesAverageFertilityOrFertileSoil()
        {
            var o = new Vec2i(10, 40);
            Assert.IsTrue(PlacementRules.MeetsRequirements(_map, PlacementRule.Fertile, o, 3, 3), "default fertility 0.6");
            for (int y = 40; y < 43; y++) for (int x = 10; x < 13; x++) _map.Fertility[_map.Index(x, y)] = 0.2f;
            Assert.IsFalse(PlacementRules.MeetsRequirements(_map, PlacementRule.Fertile, o, 3, 3));
            _map.Set(_map.Index(11, 41), TileFlags.FertileSoil);
            Assert.IsTrue(PlacementRules.MeetsRequirements(_map, PlacementRule.Fertile, o, 3, 3));
        }

        [Test]
        public void NearForest_NeedsThreeTreeTilesWithinFour()
        {
            var o = new Vec2i(40, 40);
            _map.Set(_map.Index(44, 40), TileFlags.Trees);
            _map.Set(_map.Index(44, 41), TileFlags.Trees);
            Assert.IsFalse(PlacementRules.MeetsRequirements(_map, PlacementRule.NearForest, o, 1, 1));
            _map.Set(_map.Index(36, 36), TileFlags.Trees);
            Assert.IsTrue(PlacementRules.MeetsRequirements(_map, PlacementRule.NearForest, o, 1, 1));
        }

        [Test]
        public void OnHills_NeedsMostlyHills_DepositsAndMountainNeedProximity()
        {
            var o = new Vec2i(50, 10);
            PlanningTestKit.PaintBiome(_map, 50, 10, 2, 1, Biome.Hills);
            Assert.IsFalse(PlacementRules.MeetsRequirements(_map, PlacementRule.OnHills, o, 2, 2), "half is not mostly");
            _map.Biomes[_map.Index(50, 11)] = Biome.Hills;
            Assert.IsTrue(PlacementRules.MeetsRequirements(_map, PlacementRule.OnHills, o, 2, 2));

            Assert.IsFalse(PlacementRules.MeetsRequirements(_map, PlacementRule.NearGold | PlacementRule.NearMountain, o, 2, 2));
            _map.Set(_map.Index(54, 12), TileFlags.GoldDeposit);
            _map.Biomes[_map.Index(48, 8)] = Biome.Mountain;
            Assert.IsTrue(PlacementRules.MeetsRequirements(_map, PlacementRule.NearGold | PlacementRule.NearMountain, o, 2, 2));
            Assert.IsTrue(PlacementRules.MeetsRequirements(_map, PlacementRule.Central | PlacementRule.Outskirts | PlacementRule.Isolated, o, 2, 2), "soft rules never fail");
        }

        [Test]
        public void FindAccessTile_PrefersRoadAndReportsFacing()
        {
            var o = new Vec2i(20, 50);
            Assert.IsTrue(PlacementRules.FindAccessTile(_map, o, 1, 1, new Vec2i(20, 40), out _).IsValid);
            foreach (var d in Vec2i.Dirs4) _map.BuildingAt[_map.Index(o + d)] = 99;
            Assert.IsFalse(PlacementRules.FindAccessTile(_map, o, 1, 1, new Vec2i(20, 40), out _).IsValid);
            var east = o + new Vec2i(1, 0);
            _map.BuildingAt[_map.Index(east)] = -1;
            _map.Set(_map.Index(east), TileFlags.Road);
            var north = o + new Vec2i(0, 1);
            _map.BuildingAt[_map.Index(north)] = -1;
            var access = PlacementRules.FindAccessTile(_map, o, 1, 1, new Vec2i(20, 60), out byte rot);
            Assert.AreEqual(east, access, "road beats closer free land");
            Assert.AreEqual(1, rot);
            _map.Set(_map.Index(north), TileFlags.Farmland);
            _map.Clear(_map.Index(east), TileFlags.Road);
            _map.BuildingAt[_map.Index(east)] = 99;
            Assert.IsFalse(PlacementRules.FindAccessTile(_map, o, 1, 1, new Vec2i(20, 60), out _).IsValid, "fields are not entrances");
        }

        [Test]
        public void FootprintClaimable_RejectsForeignOwnerAndFarmland()
        {
            var o = new Vec2i(5, 5);
            Assert.IsTrue(PlacementRules.FootprintClaimable(_map, 1, o, 2, 2));
            _map.Owner[_map.Index(6, 6)] = 2;
            Assert.IsFalse(PlacementRules.FootprintClaimable(_map, 1, o, 2, 2));
            Assert.IsTrue(PlacementRules.FootprintClaimable(_map, 2, o, 2, 2));
            _map.Set(_map.Index(5, 5), TileFlags.Farmland);
            Assert.IsFalse(PlacementRules.FootprintClaimable(_map, 2, o, 2, 2));
            Assert.IsFalse(PlacementRules.FootprintClaimable(_map, 2, new Vec2i(63, 63), 2, 2), "out of bounds");
        }

        [Test]
        public void RingOffset_CoversEachRingTileOnce()
        {
            for (int r = 0; r <= 5; r++)
            {
                var seen = new System.Collections.Generic.HashSet<Vec2i>();
                int n = PlacementRules.RingCount(r);
                for (int t = 0; t < n; t++)
                {
                    PlacementRules.RingOffset(r, t, out int dx, out int dy);
                    Assert.AreEqual(r, System.Math.Max(System.Math.Abs(dx), System.Math.Abs(dy)));
                    Assert.IsTrue(seen.Add(new Vec2i(dx, dy)));
                }
                Assert.AreEqual(n, seen.Count);
            }
        }

        [Test]
        public void UsesRugged_OnlyForDwarvenHeavyBuildings()
        {
            Assert.IsTrue(PlacementRules.UsesRugged(RaceId.Dwarf, BuildingType.Mine));
            Assert.IsTrue(PlacementRules.UsesRugged(RaceId.Dwarf, BuildingType.House));
            Assert.IsFalse(PlacementRules.UsesRugged(RaceId.Dwarf, BuildingType.Temple));
            Assert.IsFalse(PlacementRules.UsesRugged(RaceId.Human, BuildingType.Mine));
        }

        [Test]
        public void TileCost_MatchesRoadRules()
        {
            int i = _map.Index(30, 30);
            Assert.AreEqual(1f, PlannerRoads.TileCost(_map, i));
            _map.Set(i, TileFlags.Trees);
            Assert.AreEqual(1.5f, PlannerRoads.TileCost(_map, i));
            _map.Clear(i, TileFlags.Trees);
            _map.Biomes[i] = Biome.Hills;
            Assert.AreEqual(1.5f, PlannerRoads.TileCost(_map, i));
            _map.Set(i, TileFlags.Road);
            Assert.AreEqual(PlannerRoads.RoadCost, PlannerRoads.TileCost(_map, i));
            _map.Biomes[i] = Biome.Mountain;
            Assert.IsTrue(float.IsPositiveInfinity(PlannerRoads.TileCost(_map, i)));
            _map.Biomes[i] = Biome.Lake;
            Assert.IsTrue(float.IsPositiveInfinity(PlannerRoads.TileCost(_map, i)));
            _map.Biomes[i] = Biome.Grassland;
            _map.BuildingAt[i] = 7;
            Assert.IsTrue(float.IsPositiveInfinity(PlannerRoads.TileCost(_map, i)));
        }

        [Test]
        public void SiteScorer_CentralPrefersCenter_OutskirtsPrefersEdge()
        {
            var ctx = TestWorlds.CreateContext(PlanningTestKit.CreateDb(), 64, 2);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(32, 32), 4, out _);
            var race = ctx.Db.Race(RaceId.Orc);
            var central = new BuildingDefinition { Type = BuildingType.Temple, Placement = PlacementRule.Central };
            var outskirts = new BuildingDefinition { Type = BuildingType.Palisade, Placement = PlacementRule.Outskirts };
            var probe = default(SiteProbe);
            var near = new Vec2i(s.Center.X + 2, s.Center.Y);
            var edge = new Vec2i(s.Center.X + 6, s.Center.Y);
            Assert.IsTrue(SiteScorer.Score(ctx.State.Map, s, race, central, near, in probe, null, 8, 0f)
                        > SiteScorer.Score(ctx.State.Map, s, race, central, edge, in probe, null, 8, 0f));
            Assert.IsTrue(SiteScorer.Score(ctx.State.Map, s, race, outskirts, edge, in probe, null, 8, 0f)
                        > SiteScorer.Score(ctx.State.Map, s, race, outskirts, near, in probe, null, 8, 0f));
        }
    }
}
