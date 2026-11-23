using GodSim.Core;
using GodSim.Core.Geography;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Geography
{
    public class GeographyCacheTests
    {
        [SetUp]
        public void SetUp() { GeographyCache.Clear(); }

        [TearDown]
        public void TearDown() { GeographyCache.Clear(); }

        [Test]
        public void Get_CachesPerMapInstance()
        {
            var s = new WorldState(42UL, GeoTestMaps.Standard());
            var a = GeographyCache.Get(s);
            Assert.IsNotNull(a);
            Assert.AreSame(a, GeographyCache.Get(s));
            var other = new WorldState(42UL, GeoTestMaps.Standard());
            var b = GeographyCache.Get(other);
            Assert.AreNotSame(a, b);
            Assert.AreEqual(a.Landmass(GeoTestMaps.PlainsTile).Name, b.Landmass(GeoTestMaps.PlainsTile).Name, "a reloaded map yields the same names");
            Assert.IsNull(GeographyCache.Get(null));
        }

        [Test]
        public void TerrainChange_RebuildsAtMostOncePerInterval()
        {
            var s = new WorldState(42UL, GeoTestMaps.Standard());
            s.Tick = 10;
            var first = GeographyCache.Get(s);
            Assert.AreEqual(RegionKind.Forest, first.RegionAt(GeoTestMaps.ForestTile).Kind);

            GeoTestMaps.Rect(s.Map, 26, 6, 32, 12, Biome.Desert);
            s.Map.MarkTerrainChanged();
            s.Tick = 10 + GeographyCache.MinRebuildIntervalTicks - 1;
            Assert.AreSame(first, GeographyCache.Get(s), "throttled: stale index returned");

            s.Tick = 10 + GeographyCache.MinRebuildIntervalTicks;
            var second = GeographyCache.Get(s);
            Assert.AreNotSame(first, second);
            Assert.AreEqual(RegionKind.Desert, second.RegionAt(GeoTestMaps.ForestTile).Kind);
            Assert.AreEqual(s.Map.TerrainVersion, second.TerrainVersion);
            Assert.AreSame(second, GeographyCache.Get(s), "no change, no rebuild");
        }

        [Test]
        public void Invalidate_ForcesImmediateRebuild_AndKeepsRenames()
        {
            var db = TestDb.CreateStub();
            var s = new WorldState(42UL, GeoTestMaps.Standard());
            var first = GeographyCache.Get(s);
            int mtnId = first.RegionAt(GeoTestMaps.MountainTile).Id;
            string native = PlaceNames.Rename(first, mtnId, db.Races[(int)RaceId.Dwarf], null);

            GeoTestMaps.Rect(s.Map, 26, 6, 32, 12, Biome.Swamp);
            s.Map.MarkTerrainChanged();
            s.Tick += 1;
            GeographyCache.Invalidate(s);
            int builds = GeographyCache.BuildCount;
            var second = GeographyCache.Get(s);
            Assert.AreEqual(builds + 1, GeographyCache.BuildCount);
            Assert.AreNotSame(first, second);
            Assert.AreEqual(RegionKind.Swamp, second.RegionAt(GeoTestMaps.ForestTile).Kind);
            var mtn = second.RegionAt(GeoTestMaps.MountainTile);
            Assert.AreEqual(native, mtn.Name, "race rename carried over the rebuild");
            Assert.IsTrue(mtn.IsRenamed);
        }

        [Test]
        public void SeedChange_Rebuilds()
        {
            var s = new WorldState(42UL, GeoTestMaps.Standard());
            var a = GeographyCache.Get(s);
            s.Seed = 43UL;
            var b = GeographyCache.Get(s);
            Assert.AreNotSame(a, b);
            Assert.AreEqual(43UL, b.Seed);
        }
    }
}
