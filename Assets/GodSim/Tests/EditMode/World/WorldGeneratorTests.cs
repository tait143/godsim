using System;
using System.Collections.Generic;
using System.Diagnostics;
using GodSim.Core;
using GodSim.Core.World;
using NUnit.Framework;

namespace GodSim.Tests.World
{
    public class WorldGeneratorTests
    {
        private const TileFlags Deposits = TileFlags.OreDeposit | TileFlags.StoneDeposit | TileFlags.GoldDeposit | TileFlags.CoalDeposit | TileFlags.OilDeposit;

        private static readonly Dictionary<string, WorldMap> Cache = new Dictionary<string, WorldMap>();

        internal static WorldMap Gen(WorldPreset preset, int size, ulong seed)
        {
            string key = preset + "/" + size + "/" + seed;
            if (Cache.TryGetValue(key, out var m)) return m;
            m = WorldGenerator.Generate(WorldGenSettings.ForPreset(preset, size, size), new Rng(seed));
            Cache[key] = m;
            return m;
        }

        // Cheap checks for per-tile loops (NUnit asserts are comparatively slow); fail with the first message.
        private static void Check(bool condition, string message = "check failed") { if (!condition) Assert.Fail(message); }
        private static void CheckEqual<T>(T expected, T actual, string message = "") { if (!EqualityComparer<T>.Default.Equals(expected, actual)) Assert.Fail("Expected " + expected + " but was " + actual + ". " + message); }
        private static void CheckRange(float v, float min, float max) { if (!(v >= min && v <= max)) Assert.Fail(v + " not in [" + min + ", " + max + "]"); }

        private static IEnumerable<WorldMap> AllPresetMaps()
        {
            foreach (WorldPreset p in Enum.GetValues(typeof(WorldPreset))) yield return Gen(p, 128, 11);
        }

        [Test]
        public void SameSeed_ProducesIdenticalHash()
        {
            var a = WorldGenerator.Generate(WorldGenSettings.ForPreset(WorldPreset.Continents, 96, 80), new Rng(42));
            var b = WorldGenerator.Generate(WorldGenSettings.ForPreset(WorldPreset.Continents, 96, 80), new Rng(42));
            Assert.AreEqual(WorldHash.Compute(a), WorldHash.Compute(b));
        }

        [Test]
        public void DifferentSeed_ProducesDifferentHash()
        {
            var a = WorldGenerator.Generate(WorldGenSettings.ForPreset(WorldPreset.Continents, 96, 96), new Rng(1));
            var b = WorldGenerator.Generate(WorldGenSettings.ForPreset(WorldPreset.Continents, 96, 96), new Rng(2));
            Assert.AreNotEqual(WorldHash.Compute(a), WorldHash.Compute(b));
        }

        [Test]
        public void WorldHash_DetectsSingleTileChange()
        {
            var map = WorldGenerator.Generate(WorldGenSettings.ForPreset(WorldPreset.Pangaea, 64, 64), new Rng(5));
            ulong before = WorldHash.Compute(map);
            map.Flags[map.Index(32, 32)] ^= TileFlags.Road;
            Assert.AreNotEqual(before, WorldHash.Compute(map));
        }

        [TestCase(WorldPreset.Continents)]
        [TestCase(WorldPreset.Pangaea)]
        [TestCase(WorldPreset.Archipelago)]
        [TestCase(WorldPreset.Highlands)]
        public void OceanRatio_IsNearTarget(WorldPreset preset)
        {
            var settings = WorldGenSettings.ForPreset(preset, 160, 160);
            for (ulong seed = 3; seed <= 4; seed++)
            {
                var stats = MapStatistics.Compute(Gen(preset, 160, seed));
                Assert.That(Math.Abs(stats.OceanRatio - settings.TargetOceanRatio), Is.LessThanOrEqualTo(0.1f),
                    preset + " seed " + seed + ": ocean " + stats.OceanRatio + " target " + settings.TargetOceanRatio);
            }
        }

        /// <summary>
        /// 64x64 is the smallest size SimConfig allows; there an enclosed basin turned Lake can be ~10% of
        /// the map (Archipelago 64 seed 19 used to give ocean 0.517 vs 0.62), which the generator must
        /// compensate for.
        /// </summary>
        [TestCase(WorldPreset.Continents)]
        [TestCase(WorldPreset.Pangaea)]
        [TestCase(WorldPreset.Archipelago)]
        [TestCase(WorldPreset.Highlands)]
        public void OceanRatio_IsNearTarget_OnSmallMapsAcrossManySeeds(WorldPreset preset)
        {
            var settings = WorldGenSettings.ForPreset(preset, 64, 64);
            for (ulong seed = 1; seed <= 40; seed++)
            {
                var stats = MapStatistics.Compute(WorldGenerator.Generate(settings, new Rng(seed)));
                Check(Math.Abs(stats.OceanRatio - settings.TargetOceanRatio) <= 0.1f,
                    preset + " 64x64 seed " + seed + ": ocean " + stats.OceanRatio + " target " + settings.TargetOceanRatio);
            }
            var regression = MapStatistics.Compute(WorldGenerator.Generate(WorldGenSettings.ForPreset(WorldPreset.Archipelago, 64, 64), new Rng(19)));
            Assert.That(Math.Abs(regression.OceanRatio - 0.62f), Is.LessThanOrEqualTo(0.1f), regression.ToString());
        }

        /// <summary>
        /// Golden hash: pins the exact output for one seed so that a runtime whose arithmetic or sort order
        /// differs (Unity Mono/IL2CPP vs CoreCLR, a future .NET) is caught. Replays and shared seeds rely on
        /// this. Update the constant ONLY for an intentional change to world generation.
        /// </summary>
        [Test]
        public void GoldenHash_IsStableAcrossRuntimes()
        {
            var map = WorldGenerator.Generate(WorldGenSettings.ForPreset(WorldPreset.Continents, 96, 80), new Rng(42));
            Assert.AreEqual(GoldenHashContinents96x80Seed42, WorldHash.Compute(map));
        }

        // Verified identical with a different sort algorithm (heap sort) substituted for Array.Sort, and in
        // Debug and Release builds.
        private const ulong GoldenHashContinents96x80Seed42 = 4170816232404881344UL;

        [Test]
        public void Continents192_HasAtLeastTenDistinctBiomes()
        {
            var stats = MapStatistics.Compute(Gen(WorldPreset.Continents, 192, 2026));
            Assert.That(stats.DistinctBiomes, Is.GreaterThanOrEqualTo(10), stats.ToString());
        }

        [Test]
        public void AllSixteenBiomes_AreReachable()
        {
            var seen = new bool[Counts.Biomes];
            foreach (var map in AllPresetMaps())
                for (int i = 0; i < map.TileCount; i++) seen[(int)map.Biomes[i]] = true;
            for (int b = 0; b < seen.Length; b++)
                Assert.IsTrue(seen[b], "Biome never generated: " + (Biome)b);
        }

        [Test]
        public void EdgeTiles_AreWater()
        {
            foreach (var map in AllPresetMaps())
            {
                for (int x = 0; x < map.Width; x++)
                {
                    Check(map.IsWater(new Vec2i(x, 0)));
                    Check(map.IsWater(new Vec2i(x, map.Height - 1)));
                }
                for (int y = 0; y < map.Height; y++)
                {
                    Check(map.IsWater(new Vec2i(0, y)));
                    Check(map.IsWater(new Vec2i(map.Width - 1, y)));
                }
            }
        }

        [Test]
        public void Layers_AreFiniteAndConsistentWithSeaLevel()
        {
            foreach (var map in AllPresetMaps())
            {
                CheckEqual(0f, map.SeaLevel);
                for (int i = 0; i < map.TileCount; i++)
                {
                    float e = map.Elevation[i];
                    Check(!(float.IsNaN(e) || float.IsInfinity(e)));
                    CheckRange(e, -1f, 1f);
                    CheckEqual(map.Biomes[i].IsWater(), e < map.SeaLevel, "tile " + i + " biome " + map.Biomes[i] + " elev " + e);
                    CheckRange(map.Temperature[i], 0f, 1f);
                    CheckRange(map.Moisture[i], 0f, 1f);
                    CheckRange(map.Fertility[i], 0f, 1f);
                    if (map.Biomes[i].IsWater()) CheckEqual(0f, map.Fertility[i]);
                    CheckEqual(-1, map.Owner[i]);
                    CheckEqual(-1, map.BuildingAt[i]);
                }
            }
        }

        [Test]
        public void Lakes_AreNotConnectedToOcean_AndOceanIsConnectedToBorder()
        {
            foreach (var map in AllPresetMaps())
            {
                var reached = new bool[map.TileCount];
                var queue = new Queue<int>();
                for (int i = 0; i < map.TileCount; i++)
                {
                    var p = map.PosOf(i);
                    bool border = p.X == 0 || p.Y == 0 || p.X == map.Width - 1 || p.Y == map.Height - 1;
                    if (border && map.IsWater(i)) { reached[i] = true; queue.Enqueue(i); }
                }
                while (queue.Count > 0)
                {
                    var p = map.PosOf(queue.Dequeue());
                    foreach (var d in Vec2i.Dirs4)
                    {
                        var np = p + d;
                        if (!map.InBounds(np)) continue;
                        int ni = map.Index(np);
                        if (reached[ni] || !map.IsWater(ni)) continue;
                        reached[ni] = true;
                        queue.Enqueue(ni);
                    }
                }
                for (int i = 0; i < map.TileCount; i++)
                {
                    if (map.Biomes[i] == Biome.Lake) Check(!(reached[i]), "Lake connected to ocean at " + map.PosOf(i));
                    if (map.Biomes[i] == Biome.DeepOcean || map.Biomes[i] == Biome.ShallowWater) Check(reached[i], "Ocean not connected at " + map.PosOf(i));
                }
            }
        }

        [Test]
        public void Rivers_AreOnLand_AndEveryRiverSystemReachesWater()
        {
            int systems = 0;
            foreach (var map in AllPresetMaps())
            {
                var seen = new bool[map.TileCount];
                var comp = new List<int>();
                for (int i = 0; i < map.TileCount; i++)
                {
                    if (!map.Has(i, TileFlags.River)) continue;
                    Check(map.Biomes[i].IsLand(), "River on water at " + map.PosOf(i));
                    if (seen[i]) continue;
                    systems++;
                    comp.Clear();
                    comp.Add(i);
                    seen[i] = true;
                    bool reachesWater = false;
                    for (int h = 0; h < comp.Count; h++)
                    {
                        var p = map.PosOf(comp[h]);
                        foreach (var d in Vec2i.Dirs4)
                        {
                            var np = p + d;
                            if (!map.InBounds(np)) continue;
                            int ni = map.Index(np);
                            if (map.IsWater(ni)) reachesWater = true;
                            if (!seen[ni] && map.Has(ni, TileFlags.River)) { seen[ni] = true; comp.Add(ni); }
                        }
                    }
                    Check(reachesWater, "River system at " + map.PosOf(i) + " (" + comp.Count + " tiles) never reaches water");
                }
            }
            Assert.That(systems, Is.GreaterThan(8), "expected several river systems across presets");
        }

        private static IEnumerable<WorldMap> RiverMaps()
        {
            foreach (var m in AllPresetMaps()) yield return m;
            yield return Gen(WorldPreset.Continents, 192, 2026);
        }

        [Test]
        public void RiverHeads_StartOnHighland()
        {
            int heads = 0;
            foreach (var map in RiverMaps())
            {
                for (int i = 0; i < map.TileCount; i++)
                {
                    if (!map.Has(i, TileFlags.River)) continue;
                    var p = map.PosOf(i);
                    int riverNeighbours = 0;
                    bool water = false;
                    foreach (var d in Vec2i.Dirs4)
                    {
                        var q = p + d;
                        if (!map.InBounds(q)) continue;
                        if (map.Has(q, TileFlags.River)) riverNeighbours++;
                        if (map.IsWater(q)) water = true;
                    }
                    if (riverNeighbours > 1 || water) continue;
                    heads++;
                    Biome b = map.Biomes[i];
                    Check(b == Biome.Hills || b == Biome.Mountain || b == Biome.Snow || b == Biome.Volcanic,
                        "river head on " + b + " at " + p);
                }
            }
            Assert.That(heads, Is.GreaterThan(8));
        }

        [Test]
        public void RiverSystems_AreNotStubs_AndNeverCrossLava()
        {
            foreach (var map in RiverMaps())
            {
                var seen = new bool[map.TileCount];
                var comp = new List<int>();
                for (int i = 0; i < map.TileCount; i++)
                {
                    if (!map.Has(i, TileFlags.River)) continue;
                    Check(map.Biomes[i] != Biome.Volcanic, "river through volcanic tile at " + map.PosOf(i));
                    if (seen[i]) continue;
                    comp.Clear();
                    comp.Add(i);
                    seen[i] = true;
                    for (int h = 0; h < comp.Count; h++)
                    {
                        var p = map.PosOf(comp[h]);
                        foreach (var d in Vec2i.Dirs4)
                        {
                            var np = p + d;
                            if (!map.InBounds(np)) continue;
                            int ni = map.Index(np);
                            if (!seen[ni] && map.Has(ni, TileFlags.River)) { seen[ni] = true; comp.Add(ni); }
                        }
                    }
                    // Sources keep >= 4 tiles (Chebyshev) from every water body, pit lakes included.
                    Check(comp.Count >= 3, "stub river (" + comp.Count + " tiles) at " + map.PosOf(i));
                }
            }
        }

        [Test]
        public void RiverCountZero_ProducesNoRivers()
        {
            var s = WorldGenSettings.ForPreset(WorldPreset.Continents, 96, 96);
            s.RiverCount = 0;
            var stats = MapStatistics.Compute(WorldGenerator.Generate(s, new Rng(9)));
            Assert.AreEqual(0, stats.RiverTiles);
        }

        [Test]
        public void TreesAndDeposits_FollowPlacementRules()
        {
            foreach (var map in AllPresetMaps())
            {
                for (int i = 0; i < map.TileCount; i++)
                {
                    TileFlags f = map.Flags[i];
                    Biome b = map.Biomes[i];
                    if (b.IsWater())
                    {
                        CheckEqual(TileFlags.None, f, "flags on water at " + map.PosOf(i));
                        CheckEqual(0f, map.Resource[i]);
                        continue;
                    }
                    TileFlags dep = f & Deposits;
                    if ((f & TileFlags.Trees) != 0)
                    {
                        CheckEqual(TileFlags.None, dep, "deposit on a Trees tile at " + map.PosOf(i));
                        CheckRange(map.Resource[i], 60f, 160f);
                    }
                    if (dep != TileFlags.None)
                    {
                        Assert.IsTrue(dep == TileFlags.OreDeposit || dep == TileFlags.StoneDeposit || dep == TileFlags.GoldDeposit
                            || dep == TileFlags.CoalDeposit || dep == TileFlags.OilDeposit, "multiple deposits on one tile");
                        CheckRange(map.Resource[i], 250f, 1200f);
                    }
                    if (dep == TileFlags.OreDeposit) Check(b == Biome.Hills || b == Biome.Mountain);
                    if (dep == TileFlags.StoneDeposit) Check(b == Biome.Hills || b == Biome.Mountain || b == Biome.Desert || b == Biome.Tundra);
                    if (dep == TileFlags.CoalDeposit) Check(b == Biome.Hills || b == Biome.Forest || b == Biome.Swamp);
                    if (dep == TileFlags.OilDeposit) Check(b == Biome.Desert || b == Biome.Swamp || b == Biome.Tundra);
                    if (dep == TileFlags.GoldDeposit) Check(b == Biome.Hills || b == Biome.Mountain || ((f & TileFlags.River) != 0 && b.IsBuildable()),
                        "gold on " + b + " at " + map.PosOf(i));
                    if ((f & (TileFlags.Trees | Deposits)) == 0) CheckEqual(0f, map.Resource[i]);
                }
            }
        }

        [Test]
        public void Features_ArePresentOnStandardMap()
        {
            var stats = MapStatistics.Compute(Gen(WorldPreset.Continents, 192, 2026));
            Assert.That(stats.TreeTiles, Is.GreaterThan(500));
            Assert.That(stats.RiverTiles, Is.GreaterThan(50));
            Assert.That(stats.CoastalTiles, Is.GreaterThan(100));
            Assert.That(stats.FertileTiles, Is.GreaterThan(20));
            Assert.That(stats.SacredTiles, Is.GreaterThan(0));
            Assert.That(stats.RuinTiles, Is.GreaterThan(0));
            Assert.That(stats.OreDeposits, Is.GreaterThan(0));
            Assert.That(stats.StoneDeposits, Is.GreaterThan(0));
            Assert.That(stats.GoldDeposits, Is.GreaterThan(0));
            Assert.That(stats.CoalDeposits, Is.GreaterThan(0));
            Assert.That(stats.OilDeposits, Is.GreaterThan(0));
            Assert.That(stats.Count(Biome.Volcanic), Is.GreaterThan(0));
        }

        [Test]
        public void CoastalFlag_MarksLandNextToWater()
        {
            var map = Gen(WorldPreset.Archipelago, 128, 11);
            for (int i = 0; i < map.TileCount; i++)
            {
                if (map.IsWater(i)) continue;
                var p = map.PosOf(i);
                bool adj = false;
                foreach (var d in Vec2i.Dirs4) if (map.IsWater(p + d)) adj = true;
                CheckEqual(adj, map.Has(i, TileFlags.Coastal), "Coastal mismatch at " + p);
            }
        }

        [Test]
        public void EveryBeachTile_IsCoastal()
        {
            int beaches = 0;
            foreach (var map in RiverMaps())
                for (int i = 0; i < map.TileCount; i++)
                {
                    if (map.Biomes[i] != Biome.Beach) continue;
                    beaches++;
                    Check(map.Has(i, TileFlags.Coastal), "Beach without Coastal at " + map.PosOf(i));
                }
            Assert.That(beaches, Is.GreaterThan(0));
        }

        [Test]
        public void FertileSoil_AlsoOccursOnWetGrasslandAwayFromRivers()
        {
            var map = Gen(WorldPreset.Continents, 192, 2026);
            int fertile = 0, awayFromRiver = 0;
            for (int i = 0; i < map.TileCount; i++)
            {
                if (!map.Has(i, TileFlags.FertileSoil)) continue;
                fertile++;
                Check(map.Biomes[i].IsBuildable(), "FertileSoil on " + map.Biomes[i]);
                var p = map.PosOf(i);
                bool nearRiver = false;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (map.Has(new Vec2i(p.X + dx, p.Y + dy), TileFlags.River)) nearRiver = true;
                if (!nearRiver)
                {
                    awayFromRiver++;
                    CheckEqual(Biome.Grassland, map.Biomes[i], "FertileSoil away from rivers must be wet grassland at " + p);
                }
            }
            Assert.That(awayFromRiver, Is.GreaterThan(0), "fertile " + fertile);
            Assert.That(fertile - awayFromRiver, Is.GreaterThan(0), "river floodplains should be fertile too");
        }

        [Test]
        public void DepositDensityZero_PlacesNoDeposits()
        {
            var s = WorldGenSettings.ForPreset(WorldPreset.Highlands, 96, 96);
            s.DepositDensity = 0f;
            var stats = MapStatistics.Compute(WorldGenerator.Generate(s, new Rng(17)));
            Assert.AreEqual(0, stats.TotalDeposits);
        }

        [Test]
        public void Highlands_HaveMoreMountainsThanArchipelago()
        {
            var hi = MapStatistics.Compute(Gen(WorldPreset.Highlands, 128, 11));
            var ar = MapStatistics.Compute(Gen(WorldPreset.Archipelago, 128, 11));
            float hiShare = (hi.Count(Biome.Mountain) + hi.Count(Biome.Snow) + hi.Count(Biome.Hills)) / (float)hi.LandTiles;
            float arShare = (ar.Count(Biome.Mountain) + ar.Count(Biome.Snow) + ar.Count(Biome.Hills)) / (float)ar.LandTiles;
            Assert.That(hiShare, Is.GreaterThan(arShare));
            Assert.That(hi.MeanLandElevation, Is.GreaterThan(ar.MeanLandElevation));
        }

        [Test]
        public void TemperatureBias_ShiftsClimate()
        {
            var cold = WorldGenSettings.ForPreset(WorldPreset.Continents, 96, 96);
            cold.TemperatureBias = -0.3f;
            var warm = WorldGenSettings.ForPreset(WorldPreset.Continents, 96, 96);
            warm.TemperatureBias = 0.3f;
            var a = WorldGenerator.Generate(cold, new Rng(8));
            var b = WorldGenerator.Generate(warm, new Rng(8));
            double ta = 0, tb = 0;
            for (int i = 0; i < a.TileCount; i++) { ta += a.Temperature[i]; tb += b.Temperature[i]; }
            Assert.That(tb, Is.GreaterThan(ta));
        }

        [Test]
        public void Temperature_FollowsLatitude_EquatorHotPolesCold()
        {
            var map = Gen(WorldPreset.Continents, 192, 2026);
            int h = map.Height, w = map.Width;
            float mid = (h - 1) * 0.5f, band = h * 0.05f;
            double eq = 0, north = 0, south = 0;
            int ne = 0, nn = 0, ns = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    float t = map.Temperature[map.Index(x, y)];
                    if (Math.Abs(y - mid) <= band) { eq += t; ne++; }
                    else if (y < band) { north += t; nn++; }
                    else if (y > h - 1 - band) { south += t; ns++; }
                }
            eq /= ne; north /= nn; south /= ns;
            Assert.That(eq - north, Is.GreaterThanOrEqualTo(0.4), "equator " + eq + " north pole " + north);
            Assert.That(eq - south, Is.GreaterThanOrEqualTo(0.4), "equator " + eq + " south pole " + south);
        }

        [Test]
        public void Moisture_HasRainShadowEastOfMountains()
        {
            // Prevailing wind blows west -> east, so lowland just east of a range (lee) is drier than lowland
            // just west of one (windward).
            var map = Gen(WorldPreset.Continents, 192, 2026);
            int w = map.Width, h = map.Height;
            const float MountainElevation = 0.6f, LowlandBelow = 0.35f;
            double lee = 0, windward = 0;
            int nl = 0, nw = 0;
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    int i = map.Index(x, y);
                    if (map.IsWater(i) || map.Has(i, TileFlags.River) || map.Elevation[i] >= LowlandBelow) continue;
                    bool west = false, east = false;
                    for (int d = 1; d <= 8; d++)
                    {
                        if (x - d >= 0 && map.Elevation[map.Index(x - d, y)] >= MountainElevation) west = true;
                        if (x + d < w && map.Elevation[map.Index(x + d, y)] >= MountainElevation) east = true;
                    }
                    if (west && !east) { lee += map.Moisture[i]; nl++; }
                    else if (east && !west) { windward += map.Moisture[i]; nw++; }
                }
            Assert.That(nl, Is.GreaterThan(20));
            Assert.That(nw, Is.GreaterThan(20));
            Assert.That(lee / nl, Is.LessThan(windward / nw), "lee " + lee / nl + " windward " + windward / nw);
        }

        [Test]
        public void SmallAndRectangularMaps_Generate()
        {
            var tiny = WorldGenerator.Generate(new WorldGenSettings { Width = 16, Height = 16 }, new Rng(1));
            Assert.AreEqual(16, tiny.Width);
            Assert.IsTrue(tiny.IsWater(new Vec2i(0, 0)));
            var rect = WorldGenerator.Generate(WorldGenSettings.ForPreset(WorldPreset.Archipelago, 120, 64), new Rng(3));
            Assert.AreEqual(120, rect.Width);
            Assert.AreEqual(64, rect.Height);
            Assert.That(MapStatistics.Compute(rect).LandTiles, Is.GreaterThan(0));
        }

        [Test]
        public void Generate_NullSettingsUsesDefaults_AndNullRngThrows()
        {
            var map = WorldGenerator.Generate(null, new Rng(4));
            Assert.AreEqual(192, map.Width);
            Assert.Throws<ArgumentNullException>(() => WorldGenerator.Generate(new WorldGenSettings(), null));
        }

        [Test]
        public void Generate_DoesNotMutateSettings()
        {
            var s = new WorldGenSettings { Width = 5000, Height = 64, TargetOceanRatio = float.NaN };
            WorldGenerator.Generate(s, new Rng(1));
            Assert.AreEqual(5000, s.Width);
            Assert.IsTrue(float.IsNaN(s.TargetOceanRatio));
        }

        [Test]
        public void Settings_SanitizeClampsUntrustedValues()
        {
            var s = new WorldGenSettings
            {
                Width = 3, Height = 99999, Preset = (WorldPreset)77, TargetOceanRatio = float.PositiveInfinity,
                MountainIntensity = -5f, RiverCount = -3, MoistureScale = float.NaN, TemperatureBias = 9f,
                DepositDensity = float.NegativeInfinity, SacredSiteCount = -1, RuinCount = 100000
            }.Sanitize();
            Assert.AreEqual(WorldMap.MinSize, s.Width);
            Assert.AreEqual(WorldMap.MaxSize, s.Height);
            Assert.AreEqual(WorldPreset.Continents, s.Preset);
            Assert.That(s.TargetOceanRatio, Is.InRange(0.1f, 0.9f));
            Assert.AreEqual(0f, s.MountainIntensity);
            Assert.AreEqual(0, s.RiverCount);
            Assert.AreEqual(1f, s.MoistureScale);
            Assert.AreEqual(0.5f, s.TemperatureBias);
            Assert.That(s.DepositDensity, Is.InRange(0f, 4f));
            Assert.AreEqual(0, s.SacredSiteCount);
            Assert.AreEqual(512, s.RuinCount);
        }

        [Test]
        public void Settings_FromConfigUsesPresetDefaults()
        {
            var c = new SimConfig { MapWidth = 256, MapHeight = 200, Preset = WorldPreset.Archipelago };
            var s = WorldGenSettings.FromConfig(c);
            Assert.AreEqual(256, s.Width);
            Assert.AreEqual(200, s.Height);
            Assert.AreEqual(WorldPreset.Archipelago, s.Preset);
            Assert.AreEqual(0.62f, s.TargetOceanRatio, 1e-4f);
            Assert.AreEqual(0.45f, WorldGenSettings.FromConfig(new SimConfig()).TargetOceanRatio, 1e-4f);
            Assert.AreEqual(0.30f, WorldGenSettings.ForPreset(WorldPreset.Pangaea, 128, 128).TargetOceanRatio, 1e-4f);
            Assert.AreEqual(0.35f, WorldGenSettings.ForPreset(WorldPreset.Highlands, 128, 128).TargetOceanRatio, 1e-4f);
            Assert.That(WorldGenSettings.ForPreset(WorldPreset.Continents, 512, 512).RiverCount,
                Is.GreaterThan(WorldGenSettings.ForPreset(WorldPreset.Continents, 128, 128).RiverCount));
            Assert.AreEqual(192, WorldGenSettings.FromConfig(null).Width);
        }

        [Test]
        public void MapStatistics_HistogramSumsToTileCount()
        {
            var map = Gen(WorldPreset.Pangaea, 128, 11);
            var stats = MapStatistics.Compute(map);
            int sum = 0;
            for (int b = 0; b < Counts.Biomes; b++) sum += stats.BiomeCounts[b];
            Assert.AreEqual(map.TileCount, sum);
            Assert.AreEqual(map.TileCount, stats.OceanTiles + stats.LakeTiles + stats.LandTiles);
            Assert.IsFalse(string.IsNullOrEmpty(stats.ToString()));
        }

        [Test]
        public void Generate256_IsFast()
        {
            WorldGenerator.Generate(WorldGenSettings.ForPreset(WorldPreset.Continents, 64, 64), new Rng(1)); // JIT warm-up
            var sw = Stopwatch.StartNew();
            WorldGenerator.Generate(WorldGenSettings.ForPreset(WorldPreset.Continents, 256, 256), new Rng(77));
            sw.Stop();
            TestContext.WriteLine("256x256 generation: " + sw.ElapsedMilliseconds + " ms");
            // Budget is 1.5 s on one core (measured ~0.1 s); allow a little headroom for loaded CI machines.
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(2000));
        }

        [TestCase(WorldPreset.Continents)]
        [TestCase(WorldPreset.Pangaea)]
        [TestCase(WorldPreset.Archipelago)]
        [TestCase(WorldPreset.Highlands)]
        public void Generate512_IsFast(WorldPreset preset)
        {
            WorldGenerator.Generate(WorldGenSettings.ForPreset(preset, 64, 64), new Rng(1)); // JIT warm-up
            var sw = Stopwatch.StartNew();
            var map = WorldGenerator.Generate(WorldGenSettings.ForPreset(preset, 512, 512), new Rng(77));
            sw.Stop();
            TestContext.WriteLine(preset + " 512x512 generation: " + sw.ElapsedMilliseconds + " ms");
            Assert.AreEqual(512, map.Width);
            Assert.That(sw.ElapsedMilliseconds, Is.LessThan(5000)); // brief: 512x512 < 5 s on one core
        }
    }

    public class NoiseTests
    {
        [Test]
        public void GradientNoise_IsDeterministicAndBounded()
        {
            var a = new GradientNoise(new Rng(5));
            var b = new GradientNoise(new Rng(5));
            var c = new GradientNoise(new Rng(6));
            bool differs = false;
            for (int i = 0; i < 2000; i++)
            {
                float x = i * 0.137f - 40f, y = i * 0.071f + 3f;
                float va = a.Sample(x, y);
                Assert.AreEqual(va, b.Sample(x, y));
                Assert.That(va, Is.InRange(-1f, 1f));
                if (Math.Abs(va - c.Sample(x, y)) > 1e-4f) differs = true;
            }
            Assert.IsTrue(differs);
            Assert.AreEqual(0f, a.Sample(3f, 7f), 1e-6f);
        }

        [Test]
        public void FractalNoise_FbmAndRidgedStayInRange()
        {
            var f = new FractalNoise(new Rng(9), 6);
            float min = 1f, max = -1f;
            for (int i = 0; i < 5000; i++)
            {
                float x = (i % 100) * 0.05f, y = (i / 100) * 0.05f;
                float v = f.Fbm(x, y);
                float r = f.Ridged(x, y);
                Assert.That(v, Is.InRange(-1f, 1f));
                Assert.That(r, Is.InRange(0f, 1f));
                if (v < min) min = v;
                if (v > max) max = v;
            }
            Assert.That(max - min, Is.GreaterThan(0.3f), "fBm should vary");
            Assert.AreEqual(-1, GradientNoise.FastFloor(-0.5f));
            Assert.AreEqual(-2, GradientNoise.FastFloor(-2f));
            Assert.AreEqual(1, GradientNoise.FastFloor(1.9f));
        }
    }
}
