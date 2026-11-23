using System;
using GodSim.Core;
using GodSim.Core.World;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.World
{
    public class StartLocationFinderTests
    {
        private static int BuildableAround(WorldMap map, Vec2i p, int r)
        {
            int c = 0;
            for (int y = p.Y - r; y <= p.Y + r; y++)
                for (int x = p.X - r; x <= p.X + r; x++)
                    if (map.InBounds(x, y) && map.IsFreeBuildable(map.Index(x, y))) c++;
            return c;
        }

        private static float MinPairwise(Vec2i[] pts)
        {
            float min = float.PositiveInfinity;
            for (int a = 0; a < pts.Length; a++)
                for (int b = a + 1; b < pts.Length; b++)
                    min = Math.Min(min, pts[a].DistanceTo(pts[b]));
            return min;
        }

        [TestCase(WorldPreset.Continents, 2026UL)]
        [TestCase(WorldPreset.Continents, 7UL)]
        [TestCase(WorldPreset.Pangaea, 7UL)]
        [TestCase(WorldPreset.Highlands, 7UL)]
        [TestCase(WorldPreset.Archipelago, 7UL)]
        [TestCase(WorldPreset.Archipelago, 2026UL)]
        public void StartLocations_AreDistinctLandBuildableAndSpaced(WorldPreset preset, ulong seed)
        {
            var map = WorldGeneratorTests.Gen(preset, 192, seed);
            var db = TestDb.CreateStub();
            var starts = StartLocationFinder.Find(map, db, EnumUtil.AllRaces, new Rng(seed));
            Assert.AreEqual(EnumUtil.AllRaces.Length, starts.Length);
            foreach (var p in starts)
            {
                Assert.IsTrue(map.InBounds(p), p.ToString());
                int i = map.Index(p);
                Assert.IsTrue(map.Biomes[i].IsLand(), "not land: " + p);
                Assert.IsTrue(map.IsFreeBuildable(i), "not buildable: " + p);
                Assert.That(BuildableAround(map, p, StartLocationFinder.Radius), Is.GreaterThanOrEqualTo(StartLocationFinder.MinBuildableTiles));
                AssertTownCenterFootprintClear(map, p);
            }
            Assert.That(MinPairwise(starts), Is.GreaterThan(25f), string.Join(" ", starts));
        }

        /// <summary>FoundSettlement puts the 2x2 TownCenter with its origin on the start tile: it must not bury a deposit.</summary>
        private static void AssertTownCenterFootprintClear(WorldMap map, Vec2i p)
        {
            for (int dy = 0; dy < StartLocationFinder.TownCenterSize; dy++)
                for (int dx = 0; dx < StartLocationFinder.TownCenterSize; dx++)
                {
                    var q = new Vec2i(p.X + dx, p.Y + dy);
                    Assert.IsTrue(map.InBounds(q) && map.IsFreeBuildable(map.Index(q)), "footprint not buildable at " + q);
                    Assert.AreEqual(TileFlags.None, map.Flags[map.Index(q)] & (Reserved | TileFlags.River), "footprint covers " + map.Flags[map.Index(q)] + " at " + q);
                }
        }

        private const TileFlags Reserved = TileFlags.OreDeposit | TileFlags.StoneDeposit | TileFlags.GoldDeposit
            | TileFlags.CoalDeposit | TileFlags.OilDeposit | TileFlags.Sacred;

        [Test]
        public void SixteenRaces_AllGetDistinctBuildableStarts()
        {
            var map = WorldGeneratorTests.Gen(WorldPreset.Continents, 192, 4);
            var races = new RaceId[16]; // SimConfig.Sanitize allows up to 16 starting races
            for (int i = 0; i < races.Length; i++) races[i] = (RaceId)(i % Counts.Races);
            var starts = StartLocationFinder.Find(map, TestDb.CreateStub(), races, new Rng(4));
            Assert.AreEqual(16, starts.Length);
            for (int a = 0; a < starts.Length; a++)
            {
                Assert.IsTrue(map.InBounds(starts[a]) && map.IsFreeBuildable(map.Index(starts[a])), "not buildable: " + starts[a]);
                Assert.That(BuildableAround(map, starts[a], StartLocationFinder.Radius), Is.GreaterThanOrEqualTo(StartLocationFinder.MinBuildableTiles), starts[a].ToString());
                for (int b = a + 1; b < starts.Length; b++) Assert.AreNotEqual(starts[a], starts[b]);
            }
        }

        // Habitability tables shaped like the shipped races (indexed by Biome), so the test exercises
        // biome-specific preferences instead of the stub's uniform 0.6. Test data only: no Races dependency.
        private static readonly float[][] RaceTables =
        {
            new[] { 0f, 0f, 0f, 0.6f, 1.0f, 0.6f, 0.8f, 0.35f, 0.25f, 0.25f, 0.45f, 0.2f, 0.7f, 0.2f, 0.05f, 0.05f },   // grassland/forest/hills
            new[] { 0f, 0f, 0f, 0.3f, 0.7f, 0.3f, 1.0f, 0.8f, 0.05f, 0.4f, 0.6f, 0.15f, 0.45f, 0.15f, 0.05f, 0.02f },  // forest/jungle
            new[] { 0f, 0f, 0f, 0.15f, 0.45f, 0.3f, 0.4f, 0.1f, 0.3f, 0.05f, 0.7f, 0.5f, 1.0f, 0.9f, 0.35f, 0.45f },   // hills/mountain
            new[] { 0f, 0f, 0f, 0.3f, 0.65f, 1.0f, 0.4f, 0.3f, 0.6f, 0.35f, 0.5f, 0.7f, 0.8f, 0.35f, 0.2f, 0.4f },     // savanna/tundra/desert
            new[] { 0f, 0f, 0f, 0.8f, 0.45f, 0.5f, 0.5f, 0.9f, 0.35f, 1.0f, 0.05f, 0.02f, 0.3f, 0.1f, 0f, 0.3f },     // swamp/jungle/beach
        };

        private static GameDatabase RaceSpecificDb()
        {
            var db = TestDb.CreateStub(false);
            for (int r = 0; r < Counts.Races; r++)
            {
                var h = db.Race((RaceId)r).BiomeHabitability;
                for (int b = 0; b < h.Length; b++) h[b] = RaceTables[r][b];
            }
            db.Freeze();
            return db;
        }

        /// <summary>
        /// Stone (StoneQuarry needs a deposit) and wood (WoodcutterCamp needs trees) must be in reach of a
        /// capital, also for races whose favourite biomes lack them: forest races (no stone in forests)
        /// and hill races (no trees on hills).
        /// </summary>
        [TestCase(WorldPreset.Continents, 2026UL)]
        [TestCase(WorldPreset.Continents, 7UL)]
        [TestCase(WorldPreset.Pangaea, 3UL)]
        public void ForestAndHillRaces_StartWithStoneAndTreesInReach(WorldPreset preset, ulong seed)
        {
            var map = WorldGeneratorTests.Gen(preset, 192, seed);
            var db = TestDb.CreateStub(false);
            var forest = db.Race(RaceId.Elf).BiomeHabitability;
            var hills = db.Race(RaceId.Dwarf).BiomeHabitability;
            for (int b = 0; b < Counts.Biomes; b++)
            {
                bool land = ((Biome)b).IsLand();
                forest[b] = land ? 0.3f : 0f;
                hills[b] = land ? 0.3f : 0f;
            }
            forest[(int)Biome.Forest] = 1f;
            hills[(int)Biome.Hills] = 1f;
            db.Freeze();
            var starts = StartLocationFinder.Find(map, db, new[] { RaceId.Elf, RaceId.Dwarf }, new Rng(seed));
            foreach (var p in starts)
            {
                Assert.That(map.CountFlagInRadius(p, StartLocationFinder.StoneRadius, TileFlags.StoneDeposit), Is.GreaterThanOrEqualTo(1), "no stone near " + p);
                Assert.That(map.CountFlagInRadius(p, StartLocationFinder.Radius, TileFlags.Trees), Is.GreaterThanOrEqualTo(3), "no wood near " + p);
            }
            Assert.That(map.CountBiomeInRadius(starts[0], StartLocationFinder.Radius, Biome.Forest), Is.GreaterThan(0), "forest race not in forest: " + starts[0]);
            Assert.That(map.CountBiomeInRadius(starts[1], StartLocationFinder.Radius, Biome.Hills), Is.GreaterThan(0), "hill race not in hills: " + starts[1]);
        }

        [Test]
        public void RaceSpecificTables_MostStartsHaveStoneAndWood_AndKeepTheirHabitat()
        {
            var db = RaceSpecificDb();
            int starts = 0, stocked = 0;
            foreach (WorldPreset preset in Enum.GetValues(typeof(WorldPreset)))
                foreach (ulong seed in new[] { 7UL, 2026UL })
                {
                    var map = WorldGeneratorTests.Gen(preset, 192, seed);
                    var found = StartLocationFinder.Find(map, db, EnumUtil.AllRaces, new Rng(seed));
                    for (int r = 0; r < found.Length; r++)
                    {
                        var p = found[r];
                        AssertTownCenterFootprintClear(map, p);
                        starts++;
                        if (map.CountFlagInRadius(p, StartLocationFinder.StoneRadius, TileFlags.StoneDeposit) > 0
                            && map.CountFlagInRadius(p, StartLocationFinder.Radius, TileFlags.Trees) >= 3) stocked++;
                        double hab = 0;
                        int cells = 0;
                        for (int y = p.Y - StartLocationFinder.Radius; y <= p.Y + StartLocationFinder.Radius; y++)
                            for (int x = p.X - StartLocationFinder.Radius; x <= p.X + StartLocationFinder.Radius; x++)
                                if (map.InBounds(x, y)) { hab += RaceTables[r][(int)map.Biomes[map.Index(x, y)]]; cells++; }
                        Assert.That(hab / cells, Is.GreaterThan(0.3), preset + " seed " + seed + " " + (RaceId)r + " starts in a hostile biome at " + p);
                    }
                }
            // Hot swamp/jungle regions can lack stone entirely; everyone else should be stocked.
            Assert.That(stocked, Is.GreaterThanOrEqualTo(starts * 85 / 100), stocked + " of " + starts + " starts have stone and wood in reach");
        }

        [Test]
        public void Starts_NeverSitOnDepositsOrSacredSites()
        {
            var map = WorldGeneratorTests.Gen(WorldPreset.Highlands, 192, 7);
            var db = TestDb.CreateStub();
            for (ulong seed = 1; seed <= 4; seed++)
                foreach (var p in StartLocationFinder.Find(map, db, EnumUtil.AllRaces, new Rng(seed)))
                    AssertTownCenterFootprintClear(map, p);
        }

        [Test]
        public void Archipelago_StillReturnsValidDistinctTiles()
        {
            var map = WorldGeneratorTests.Gen(WorldPreset.Archipelago, 128, 11);
            var starts = StartLocationFinder.Find(map, TestDb.CreateStub(), EnumUtil.AllRaces, new Rng(3));
            Assert.AreEqual(5, starts.Length);
            for (int a = 0; a < starts.Length; a++)
            {
                Assert.IsTrue(map.IsFreeBuildable(map.Index(starts[a])));
                for (int b = a + 1; b < starts.Length; b++) Assert.AreNotEqual(starts[a], starts[b]);
            }
        }

        [Test]
        public void Find_IsDeterministic()
        {
            var map = WorldGeneratorTests.Gen(WorldPreset.Continents, 192, 2026);
            var db = TestDb.CreateStub();
            var a = StartLocationFinder.Find(map, db, EnumUtil.AllRaces, new Rng(99));
            var b = StartLocationFinder.Find(map, db, EnumUtil.AllRaces, new Rng(99));
            CollectionAssert.AreEqual(a, b);
        }

        [Test]
        public void Find_PrefersHabitableBiomes()
        {
            var map = new WorldMap(64, 64);
            for (int i = 0; i < map.TileCount; i++)
            {
                var p = map.PosOf(i);
                bool edge = p.X < 2 || p.Y < 2 || p.X > 61 || p.Y > 61;
                map.Biomes[i] = edge ? Biome.DeepOcean : (p.X < 32 ? Biome.Desert : Biome.Forest);
                map.Elevation[i] = edge ? -0.5f : 0.2f;
            }
            var db = TestDb.CreateStub(false);
            var elf = db.Race(RaceId.Elf).BiomeHabitability;
            for (int b = 0; b < elf.Length; b++) elf[b] = 0.05f;
            elf[(int)Biome.Forest] = 1f;
            db.Freeze();
            var starts = StartLocationFinder.Find(map, db, new[] { RaceId.Elf }, new Rng(1));
            Assert.AreEqual(1, starts.Length);
            Assert.That(starts[0].X, Is.GreaterThanOrEqualTo(32), starts[0].ToString());
        }

        [Test]
        public void Find_SpreadsRacesOnUniformMap()
        {
            var map = new WorldMap(96, 96);
            for (int i = 0; i < map.TileCount; i++) { map.Biomes[i] = Biome.Grassland; map.Elevation[i] = 0.2f; }
            var starts = StartLocationFinder.Find(map, TestDb.CreateStub(), EnumUtil.AllRaces, new Rng(2));
            Assert.AreEqual(5, starts.Length);
            Assert.That(MinPairwise(starts), Is.GreaterThan(20f), string.Join(" ", starts));
        }

        [Test]
        public void Find_FallsBackOnHostileMaps()
        {
            var water = new WorldMap(32, 32);
            for (int i = 0; i < water.TileCount; i++) { water.Biomes[i] = Biome.DeepOcean; water.Elevation[i] = -0.5f; }
            var starts = StartLocationFinder.Find(water, TestDb.CreateStub(), new[] { RaceId.Human, RaceId.Orc }, new Rng(1));
            Assert.AreEqual(2, starts.Length);
            Assert.AreEqual(new Vec2i(16, 16), starts[0]);

            // A single small island: too few tiles for the 60-tile rule, but every race still gets a land tile.
            var island = new WorldMap(32, 32);
            for (int i = 0; i < island.TileCount; i++)
            {
                var p = island.PosOf(i);
                bool land = p.X >= 10 && p.X < 14 && p.Y >= 10 && p.Y < 14;
                island.Biomes[i] = land ? Biome.Grassland : Biome.ShallowWater;
                island.Elevation[i] = land ? 0.1f : -0.1f;
            }
            var s2 = StartLocationFinder.Find(island, null, new[] { RaceId.Human, RaceId.Elf, RaceId.Dwarf }, null);
            Assert.AreEqual(3, s2.Length);
            for (int a = 0; a < s2.Length; a++)
            {
                Assert.IsTrue(island.Biomes[island.Index(s2[a])].IsBuildable(), s2[a].ToString());
                for (int b = a + 1; b < s2.Length; b++) Assert.AreNotEqual(s2[a], s2[b]);
            }
        }

        [Test]
        public void Find_HandlesEmptyInput()
        {
            var map = new WorldMap(32, 32);
            Assert.AreEqual(0, StartLocationFinder.Find(map, null, new RaceId[0], new Rng(1)).Length);
            Assert.AreEqual(0, StartLocationFinder.Find(map, null, null, new Rng(1)).Length);
            Assert.Throws<ArgumentNullException>(() => StartLocationFinder.Find(null, null, EnumUtil.AllRaces, new Rng(1)));
        }
    }
}
