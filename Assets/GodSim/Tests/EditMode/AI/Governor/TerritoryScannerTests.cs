using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Governor
{
    public class TerritoryScannerTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp() { _db = GovernorTestKit.CreateDb(); }

        [Test]
        public void FlatGrassland_IsFertileButHasNoDeposits()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 4, out var s, out _);
            var f = TerritoryScanner.Scan(ctx.State.Map, s);
            Assert.IsTrue(f.Has(TerritoryFeature.Fertile));
            Assert.IsFalse(f.HasAny(TerritoryFeature.Stone | TerritoryFeature.Ore | TerritoryFeature.Trees | TerritoryFeature.Water | TerritoryFeature.Coast));
            Assert.IsTrue(f.FreeTiles > 50 && f.FreeTiles < f.LandTiles, "town center occupies tiles: " + f);
        }

        [Test]
        public void DetectsDepositsTreesWaterAndHills()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 4, out var s, out _);
            var map = ctx.State.Map;
            var c = s.Center;
            map.Set(map.Index(c.X + 5, c.Y), TileFlags.StoneDeposit);
            map.Set(map.Index(c.X - 4, c.Y + 2), TileFlags.OreDeposit);
            map.Set(map.Index(c.X, c.Y + 9), TileFlags.Trees);           // radius 6 + 4
            map.Biomes[map.Index(c.X + 8, c.Y + 1)] = Biome.Lake;          // radius 6 + 3 (approx.)
            map.Biomes[map.Index(c.X - 2, c.Y - 2)] = Biome.Hills;
            var f = TerritoryScanner.Scan(map, s);
            Assert.IsTrue(f.Has(TerritoryFeature.Stone | TerritoryFeature.Ore | TerritoryFeature.Trees | TerritoryFeature.Water | TerritoryFeature.Hills));
            Assert.IsFalse(f.HasAny(TerritoryFeature.Gold | TerritoryFeature.Coal | TerritoryFeature.Oil | TerritoryFeature.Mountain));
        }

        [Test]
        public void DistantDeposit_IsOutOfReach()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 4, out var s, out _);
            var map = ctx.State.Map;
            map.Set(map.Index(s.Center.X + 15, s.Center.Y), TileFlags.StoneDeposit);
            Assert.IsFalse(TerritoryScanner.Scan(map, s).Has(TerritoryFeature.Stone));
        }

        [Test]
        public void CoastDetectedFromAdjacentWater()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 4, out var s, out _);
            var map = ctx.State.Map;
            for (int y = 0; y < map.Height; y++) map.Biomes[map.Index(s.Center.X + 4, y)] = Biome.ShallowWater;
            var f = TerritoryScanner.Scan(map, s);
            Assert.IsTrue(f.Has(TerritoryFeature.Coast | TerritoryFeature.Water));
            Assert.IsTrue(TerritoryScanner.IsPlacementFeasible(PlacementRule.Coastal | PlacementRule.NearWater, f));
        }

        [Test]
        public void WaterWorld_HasNoLandAndNoFreeTiles()
        {
            var ctx = TestWorlds.CreateContext(_db, 32, 3, Biome.ShallowWater);
            var civ = ctx.Ops.CreateCivilization(RaceId.Human);
            var s = ctx.Ops.CreateSettlement(civ, new Vec2i(16, 16));
            var f = TerritoryScanner.Scan(ctx.State.Map, s);
            Assert.AreEqual(0, f.FreeTiles);
            Assert.AreEqual(0, f.LandTiles);
            Assert.IsFalse(f.HasAny(TerritoryFeature.Fertile | TerritoryFeature.Coast));
        }

        [Test]
        public void Feasibility_RequiresAllHardFlags_IgnoresSoftOnes()
        {
            var f = new TerritoryFeatures(TerritoryFeature.Stone | TerritoryFeature.River, 10, 20);
            Assert.IsTrue(TerritoryScanner.IsPlacementFeasible(PlacementRule.NearStone | PlacementRule.Outskirts | PlacementRule.Central | PlacementRule.Isolated, f));
            Assert.IsTrue(TerritoryScanner.IsPlacementFeasible(PlacementRule.NearWater, f), "a river satisfies NearWater");
            Assert.IsFalse(TerritoryScanner.IsPlacementFeasible(PlacementRule.NearStone | PlacementRule.NearOre, f));
            Assert.IsFalse(TerritoryScanner.IsPlacementFeasible(PlacementRule.Fertile, f));
            Assert.IsTrue(TerritoryScanner.IsPlacementFeasible(PlacementRule.None, default(TerritoryFeatures)));
        }

        [Test]
        public void Cache_WritesMemoryKeys_AndRefreshesYearlyOrOnRadiusChange()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 4, out var s, out _);
            var map = ctx.State.Map;
            TerritoryScanner.GetOrScan(map, s, 10, 96);
            Assert.AreEqual(0f, s.Mem("ai.terr.stone", -1f));
            Assert.AreEqual(1f, s.Mem("ai.terr.fertile", -1f));
            Assert.AreEqual(10f, s.Mem(TerritoryScanner.KeyTick, -1f));
            foreach (var key in TerritoryScanner.FeatureKeys) Assert.IsTrue(s.Memory.ContainsKey(key), key);

            map.Set(map.Index(s.Center.X + 2, s.Center.Y + 2), TileFlags.StoneDeposit);
            Assert.IsFalse(TerritoryScanner.GetOrScan(map, s, 50, 96).Has(TerritoryFeature.Stone), "cached until refresh");
            Assert.IsTrue(TerritoryScanner.GetOrScan(map, s, 106, 96).Has(TerritoryFeature.Stone), "refreshed after 96 ticks");

            map.Set(map.Index(s.Center.X + 11, s.Center.Y), TileFlags.OreDeposit);
            Assert.IsFalse(TerritoryScanner.GetOrScan(map, s, 110, 96).Has(TerritoryFeature.Ore));
            ctx.Ops.ClaimTerritory(s, 9);
            Assert.IsTrue(TerritoryScanner.GetOrScan(map, s, 111, 96).Has(TerritoryFeature.Ore), "radius change forces a rescan");
            Assert.IsTrue(TerritoryScanner.NeedsRefresh(s, 5, 96), "time went backwards (loaded save)");
        }

        [Test]
        public void NullInputs_AreSafe()
        {
            Assert.AreEqual(TerritoryFeature.None, TerritoryScanner.Scan(null, null).Flags);
            Assert.AreEqual(TerritoryFeature.None, TerritoryScanner.GetOrScan(null, null, 0).Flags);
            Assert.AreEqual(TerritoryFeature.None, TerritoryScanner.Load(new Settlement()).Flags);
        }
    }
}
