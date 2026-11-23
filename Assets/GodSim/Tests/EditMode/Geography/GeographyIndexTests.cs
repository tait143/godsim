using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Geography;
using NUnit.Framework;

namespace GodSim.Tests.Geography
{
    public class GeographyIndexTests
    {
        private GeographyIndex _idx;

        [SetUp]
        public void SetUp()
        {
            _idx = GeographyIndex.Build(GeoTestMaps.Standard(), 42UL);
        }

        [Test]
        public void StandardMap_HasExpectedRegionKindsAndCounts()
        {
            Assert.AreEqual(1, _idx.CountKind(RegionKind.Continent));
            Assert.AreEqual(1, _idx.CountKind(RegionKind.Island));
            Assert.AreEqual(1, _idx.CountKind(RegionKind.Ocean));
            Assert.AreEqual(1, _idx.CountKind(RegionKind.Lake));
            Assert.AreEqual(1, _idx.CountKind(RegionKind.MountainRange), "5-tile blob must not form a range");
            Assert.AreEqual(1, _idx.CountKind(RegionKind.Forest));
            Assert.AreEqual(1, _idx.CountKind(RegionKind.Plains));
            Assert.AreEqual(1, _idx.CountKind(RegionKind.River));
            Assert.AreEqual(0, _idx.CountKind(RegionKind.Bay));
            Assert.AreEqual(8, _idx.Regions.Count);
            for (int i = 0; i < _idx.Regions.Count; i++) Assert.AreEqual(i, _idx.Regions[i].Id);
        }

        [Test]
        public void RegionStats_AreCorrect()
        {
            var continent = _idx.Landmass(GeoTestMaps.PlainsTile);
            Assert.AreEqual(RegionKind.Continent, continent.Kind);
            Assert.AreEqual(1024 - 25, continent.TileCount, "lake tiles belong to the lake body");
            Assert.AreEqual(4, continent.Bounds.MinX);
            Assert.AreEqual(35, continent.Bounds.MaxY);
            Assert.AreEqual(-1, continent.ParentId);

            var mtn = _idx.RegionAt(GeoTestMaps.MountainTile);
            Assert.AreEqual(12, mtn.TileCount);
            Assert.AreEqual(new Vec2i(22, 21), mtn.Centroid);
            Assert.AreEqual(Biome.Mountain, mtn.DominantBiome);
            Assert.AreEqual(continent.Id, mtn.ParentId);
            Assert.IsTrue(mtn.Bounds.Contains(mtn.Anchor));
            Assert.AreEqual(RegionLayer.Feature, mtn.Layer);

            var forest = _idx.RegionAt(GeoTestMaps.ForestTile);
            Assert.AreEqual(RegionKind.Forest, forest.Kind);
            Assert.AreEqual(49, forest.TileCount);
            Assert.AreEqual(new Vec2i(29, 9), forest.Centroid);
            Assert.AreEqual(continent.Id, forest.ParentId);
        }

        [Test]
        public void Lake_IsDistinguishedFromOcean()
        {
            var lake = _idx.RegionAt(GeoTestMaps.LakeTile);
            var ocean = _idx.RegionAt(GeoTestMaps.OceanTile);
            Assert.AreEqual(RegionKind.Lake, lake.Kind);
            Assert.AreEqual(25, lake.TileCount);
            Assert.IsFalse(lake.TouchesMapEdge);
            Assert.AreEqual(_idx.Landmass(GeoTestMaps.PlainsTile).Id, lake.ParentId, "enclosed lake sits in its continent");
            Assert.AreEqual(RegionKind.Ocean, ocean.Kind);
            Assert.IsTrue(ocean.TouchesMapEdge);
            Assert.AreNotEqual(lake.Id, ocean.Id);
            Assert.IsTrue(lake.IsWater && ocean.IsWater);
        }

        [Test]
        public void WaterBodies_SplitBySizeAndEnclosure()
        {
            var map = GeoTestMaps.Filled(64, Biome.Grassland);
            GeoTestMaps.Rect(map, 0, 0, 9, 9, Biome.DeepOcean);        // 100 tiles at the edge -> Sea
            GeoTestMaps.Rect(map, 30, 30, 41, 41, Biome.ShallowWater); // 144 enclosed -> inland Sea
            GeoTestMaps.Rect(map, 20, 50, 22, 52, Biome.ShallowWater); // 9 enclosed -> Lake
            GeoTestMaps.Rect(map, 63, 30, 63, 33, Biome.Lake);         // lake biome at the edge -> Lake
            var idx = GeographyIndex.Build(map, 7UL);
            Assert.AreEqual(RegionKind.Sea, idx.RegionAt(new Vec2i(2, 2)).Kind);
            Assert.AreEqual(RegionKind.Sea, idx.RegionAt(new Vec2i(35, 35)).Kind);
            Assert.AreEqual(RegionKind.Lake, idx.RegionAt(new Vec2i(21, 51)).Kind);
            Assert.AreEqual(RegionKind.Lake, idx.RegionAt(new Vec2i(63, 31)).Kind);
            Assert.AreEqual(0, idx.CountKind(RegionKind.Ocean));
        }

        [Test]
        public void UShapedCoast_FormsBayInsideOcean()
        {
            var map = GeoTestMaps.Filled(64, Biome.DeepOcean);
            GeoTestMaps.Rect(map, 10, 10, 50, 40, Biome.Grassland);
            GeoTestMaps.Rect(map, 25, 22, 35, 40, Biome.ShallowWater); // notch open to the north
            var idx = GeographyIndex.Build(map, 3UL);
            var bay = idx.RegionAt(new Vec2i(30, 28));
            Assert.AreEqual(RegionKind.Bay, bay.Kind);
            Assert.AreEqual(RegionLayer.Feature, bay.Layer);
            var ocean = idx.Landmass(new Vec2i(30, 28));
            Assert.AreEqual(RegionKind.Ocean, ocean.Kind);
            Assert.AreEqual(ocean.Id, bay.ParentId);
            Assert.AreEqual(RegionKind.Ocean, idx.RegionAt(new Vec2i(2, 60)).Kind, "open ocean is not a bay");
            StringAssert.Contains(ocean.Phrase, idx.Describe(new Vec2i(30, 28)));
        }

        [Test]
        public void MountainAndForest_AreMostSpecificRegions()
        {
            Assert.AreEqual(RegionKind.MountainRange, _idx.RegionAt(GeoTestMaps.MountainTile).Kind);
            Assert.AreEqual(RegionKind.Forest, _idx.RegionAt(GeoTestMaps.ForestTile).Kind);
            Assert.AreEqual(RegionKind.Plains, _idx.RegionAt(GeoTestMaps.PlainsTile).Kind);
            Assert.AreEqual(RegionKind.Continent, _idx.RegionAt(GeoTestMaps.SmallMountainTile).Kind, "small blob falls back to the landmass");
            Assert.AreEqual(RegionKind.Island, _idx.RegionAt(GeoTestMaps.IslandTile).Kind, "beach island has no feature");
            Assert.IsNull(_idx.RegionAt(new Vec2i(-1, 3)));
            Assert.IsNull(_idx.RegionAt(new Vec2i(64, 3)));
            Assert.IsNull(_idx.Landmass(new Vec2i(3, 99)));
        }

        [Test]
        public void River_IsOneNamedSystem()
        {
            var a = _idx.RegionAt(GeoTestMaps.RiverTileA);
            var b = _idx.RegionAt(GeoTestMaps.RiverTileB);
            Assert.AreEqual(RegionKind.River, a.Kind);
            Assert.AreSame(a, b);
            Assert.AreEqual(23, a.TileCount);
            Assert.AreEqual(_idx.Landmass(GeoTestMaps.RiverTileA).Id, a.ParentId);
            Assert.AreEqual(RegionKind.Plains, _idx.Feature(GeoTestMaps.RiverTileA).Kind, "river overlays the biome feature");
        }

        [Test]
        public void Describe_ComposesFeatureAndLandmass()
        {
            var continent = _idx.Landmass(GeoTestMaps.PlainsTile);
            var mtn = _idx.RegionAt(GeoTestMaps.MountainTile);
            Assert.AreEqual(mtn.Phrase + " of " + continent.Phrase, _idx.Describe(GeoTestMaps.MountainTile));

            var river = _idx.RegionAt(GeoTestMaps.RiverTileA);
            var plains = _idx.Feature(GeoTestMaps.RiverTileA);
            Assert.AreEqual(river.Phrase + " in " + plains.Phrase + " of " + continent.Phrase, _idx.Describe(GeoTestMaps.RiverTileA));

            var lake = _idx.RegionAt(GeoTestMaps.LakeTile);
            Assert.AreEqual(lake.Phrase + " in " + continent.Phrase, _idx.Describe(GeoTestMaps.LakeTile));

            Assert.AreEqual(_idx.RegionAt(GeoTestMaps.OceanTile).Phrase, _idx.Describe(GeoTestMaps.OceanTile));
            string corner = _idx.Describe(GeoTestMaps.SmallMountainTile);
            StringAssert.Contains("southwestern", corner);
            StringAssert.Contains(PlaceNames.StripArticle(continent.Phrase), corner);
            Assert.IsFalse(string.IsNullOrEmpty(_idx.Describe(new Vec2i(-5, -5))));
        }

        [Test]
        public void NearbyFeature_FindsLandmarks()
        {
            var forest = _idx.RegionAt(GeoTestMaps.ForestTile);
            Assert.AreSame(forest, _idx.NearbyFeature(GeoTestMaps.NextToForest, 3), "specific landmark beats the plains underfoot");
            Assert.AreSame(forest, _idx.NearbyFeature(GeoTestMaps.ForestTile, 0));
            Assert.AreEqual(RegionKind.Plains, _idx.NearbyFeature(new Vec2i(24, 26), 1).Kind, "plains are the fallback");
            Assert.AreEqual(RegionKind.Island, _idx.NearbyFeature(GeoTestMaps.IslandTile, 2).Kind);
            Assert.IsNull(_idx.NearbyFeature(GeoTestMaps.OceanTile, 3), "oceans/continents are never landmarks");
            Assert.AreEqual(RegionKind.Lake, _idx.NearbyFeature(new Vec2i(12, 17), 3).Kind);
            Assert.IsNull(_idx.NearbyFeature(new Vec2i(500, 500), 5));
        }

        [Test]
        public void Find_AndLargestLandmasses()
        {
            for (int i = 0; i < _idx.Regions.Count; i++)
            {
                var r = _idx.Regions[i];
                Assert.AreSame(r, _idx.Find(r.Name));
                Assert.AreSame(r, _idx.Find(r.Phrase));
                Assert.AreSame(r, _idx.Find("  The " + r.Name.ToUpperInvariant() + " "));
            }
            Assert.IsNull(_idx.Find("Nowhere In Particular"));
            Assert.IsNull(_idx.Find(null));

            var largest = _idx.LargestLandmasses(5);
            Assert.AreEqual(2, largest.Count);
            Assert.AreEqual(RegionKind.Continent, largest[0].Kind);
            Assert.AreEqual(RegionKind.Island, largest[1].Kind);
            Assert.AreEqual(1, _idx.LargestLandmasses(1).Count);
            Assert.AreEqual(0, _idx.LargestLandmasses(-3).Count);
        }

        [Test]
        public void Rebuild_AfterTerrainChange_ReflectsNewTerrainAndKeepsStableNames()
        {
            var map = GeoTestMaps.Standard();
            var before = GeographyIndex.Build(map, 42UL);
            string continentName = before.Landmass(GeoTestMaps.PlainsTile).Name;
            string lakeName = before.RegionAt(GeoTestMaps.LakeTile).Name;

            GeoTestMaps.Rect(map, 26, 6, 32, 12, Biome.Desert);
            map.MarkTerrainChanged();
            var after = GeographyIndex.Build(map, 42UL);

            Assert.AreEqual(map.TerrainVersion, after.TerrainVersion);
            Assert.AreEqual(0, after.CountKind(RegionKind.Forest));
            Assert.AreEqual(1, after.CountKind(RegionKind.Desert));
            Assert.AreEqual(RegionKind.Desert, after.RegionAt(GeoTestMaps.ForestTile).Kind);
            Assert.AreEqual(continentName, after.Landmass(GeoTestMaps.PlainsTile).Name, "unchanged regions keep their names");
            Assert.AreEqual(lakeName, after.RegionAt(GeoTestMaps.LakeTile).Name);
        }

        [Test]
        public void Build_IsDeterministic_OnNoiseWorld()
        {
            var a = GeographyIndex.Build(NoiseWorld.Create(128, 99UL), 5UL);
            var b = GeographyIndex.Build(NoiseWorld.Create(128, 99UL), 5UL);
            Assert.AreEqual(a.Regions.Count, b.Regions.Count);
            for (int i = 0; i < a.Regions.Count; i++)
            {
                Assert.AreEqual(a.Regions[i].Kind, b.Regions[i].Kind);
                Assert.AreEqual(a.Regions[i].Phrase, b.Regions[i].Phrase);
                Assert.AreEqual(a.Regions[i].Anchor, b.Regions[i].Anchor);
            }
            for (int y = 0; y < 128; y += 7)
                for (int x = 0; x < 128; x += 7)
                    Assert.AreEqual(a.Describe(new Vec2i(x, y)), b.Describe(new Vec2i(x, y)));
        }

        [Test]
        public void Build_NullMap_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => GeographyIndex.Build(null, 1UL));
        }

        [Test, Category("Slow")]
        public void Build_512_IsFastAndNamesAreUnique()
        {
            GeographyIndex.Build(NoiseWorld.Create(64, 1UL), 1UL); // JIT warm-up
            var map = NoiseWorld.Create(512, 2026UL);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var idx = GeographyIndex.Build(map, 2026UL);
            sw.Stop();
            long ms = sw.ElapsedMilliseconds;
            Console.WriteLine("GeographyIndex.Build 512x512: " + ms + " ms, " + idx.Regions.Count + " regions");
            Assert.IsTrue(ms < 1000, "Build took " + ms + " ms");
            Assert.IsTrue(idx.Regions.Count > 20, "noise world should have many regions, got " + idx.Regions.Count);
            Assert.IsTrue(idx.CountKind(RegionKind.River) > 0);
            Assert.IsTrue(idx.CountKind(RegionKind.MountainRange) > 0);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < idx.Regions.Count; i++)
            {
                var r = idx.Regions[i];
                Assert.IsTrue(seen.Add(r.Name), "duplicate name " + r.Name);
                Assert.IsTrue(r.Phrase.Length <= PlaceNames.MaxLength, r.Phrase);
                Assert.IsTrue(r.Name.Length > 0);
            }
        }
    }
}
