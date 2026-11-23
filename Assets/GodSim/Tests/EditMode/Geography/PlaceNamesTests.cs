using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Geography;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Geography
{
    public class PlaceNamesTests
    {
        private static void AssertValidAndUnique(GeographyIndex idx)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < idx.Regions.Count; i++)
            {
                var r = idx.Regions[i];
                Assert.IsFalse(string.IsNullOrEmpty(r.Name), "empty name for " + r.Kind);
                Assert.IsFalse(string.IsNullOrEmpty(r.Phrase));
                Assert.IsTrue(r.Name.Length <= PlaceNames.MaxLength && r.Phrase.Length <= PlaceNames.MaxLength, r.Phrase);
                Assert.AreEqual(r.Name, PlaceNames.StripArticle(r.Phrase));
                Assert.IsTrue(seen.Add(r.Name), "duplicate name '" + r.Name + "'");
            }
        }

        [Test]
        public void Names_AreDeterministicForSameSeed()
        {
            var a = GeographyIndex.Build(GeoTestMaps.Standard(), 42UL);
            var b = GeographyIndex.Build(GeoTestMaps.Standard(), 42UL);
            for (int i = 0; i < a.Regions.Count; i++)
            {
                Assert.AreEqual(a.Regions[i].Phrase, b.Regions[i].Phrase);
                Assert.AreEqual(a.Regions[i].NameSeed, b.Regions[i].NameSeed);
            }
        }

        [Test]
        public void Names_DifferForAnotherSeed()
        {
            var a = GeographyIndex.Build(GeoTestMaps.Standard(), 42UL);
            var b = GeographyIndex.Build(GeoTestMaps.Standard(), 4242UL);
            int differ = 0;
            for (int i = 0; i < a.Regions.Count; i++) if (a.Regions[i].Name != b.Regions[i].Name) differ++;
            Assert.IsTrue(differ >= a.Regions.Count / 2, "only " + differ + " names changed");
        }

        [Test]
        public void Names_AreUniqueNonEmptyAndShort([Values(1UL, 2UL, 3UL, 77UL, 123456789UL)] ulong seed)
        {
            AssertValidAndUnique(GeographyIndex.Build(GeoTestMaps.Standard(), seed));
            AssertValidAndUnique(GeographyIndex.Build(NoiseWorld.Create(128, seed), seed));
        }

        [Test]
        public void Compose_IsPureAndRespectsKind()
        {
            for (int k = 0; k < RegionKindUtil.Count; k++)
            {
                var kind = (RegionKind)k;
                for (int s = 0; s < 40; s++)
                {
                    string a = PlaceNames.Compose(kind, 10 + s * 13, s / 40f, Biome.Grassland, new Rng((ulong)s), 1, -1);
                    string b = PlaceNames.Compose(kind, 10 + s * 13, s / 40f, Biome.Grassland, new Rng((ulong)s), 1, -1);
                    Assert.AreEqual(a, b);
                    Assert.IsTrue(a.Length > 0 && a.Length <= PlaceNames.MaxLength, a);
                }
            }
            StringAssert.Contains("Lake", ComposeMany(RegionKind.Lake, 60));
            StringAssert.Contains("River", ComposeMany(RegionKind.River, 40));
        }

        private static string ComposeMany(RegionKind kind, int size)
        {
            string all = "";
            for (int s = 0; s < 30; s++) all += PlaceNames.Compose(kind, size, 0.5f, Biome.Grassland, new Rng((ulong)s)) + "|";
            return all;
        }

        [Test]
        public void RegionSeed_DependsOnSeedCentroidAndKind()
        {
            var c = new Vec2i(10, 20);
            ulong s = PlaceNames.RegionSeed(1UL, c, RegionKind.Lake);
            Assert.AreEqual(s, PlaceNames.RegionSeed(1UL, c, RegionKind.Lake));
            Assert.AreNotEqual(s, PlaceNames.RegionSeed(2UL, c, RegionKind.Lake));
            Assert.AreNotEqual(s, PlaceNames.RegionSeed(1UL, new Vec2i(20, 10), RegionKind.Lake));
            Assert.AreNotEqual(s, PlaceNames.RegionSeed(1UL, c, RegionKind.Sea));
        }

        [Test]
        public void Rename_UsesRaceSyllablesAndStaysUnique()
        {
            var db = TestDb.CreateStub();
            var race = db.Races[(int)RaceId.Dwarf];
            var idx = GeographyIndex.Build(GeoTestMaps.Standard(), 42UL);
            var mtn = idx.RegionAt(GeoTestMaps.MountainTile);
            string baseName = mtn.Name;

            string native = PlaceNames.Rename(idx, mtn.Id, race, new Rng(5UL));
            Assert.IsFalse(string.IsNullOrEmpty(native));
            Assert.AreEqual(native, mtn.Name);
            Assert.AreEqual(baseName, mtn.BaseName);
            Assert.IsTrue(mtn.IsRenamed);
            Assert.AreEqual((int)RaceId.Dwarf, mtn.NativeRace);
            Assert.AreSame(mtn, idx.Find(native));
            Assert.IsNull(idx.Find(baseName), "old name is released");
            bool usesSyllable = native.Contains("Oak") || native.Contains("Stone") || native.Contains("Al") || native.Contains("Bo") || native.Contains("Ca");
            Assert.IsTrue(usesSyllable, native);

            // Renaming every region with a tiny syllable table forces the dedupe path.
            for (int i = 0; i < idx.Regions.Count; i++) PlaceNames.Rename(idx, i, race, new Rng(9UL));
            AssertValidAndUnique(idx);
            Assert.IsNull(PlaceNames.Rename(idx, 999, race, null));
            Assert.IsNull(PlaceNames.Rename(null, 0, race, null));
        }

        [Test]
        public void Rename_WithNullRng_IsReproducible()
        {
            var db = TestDb.CreateStub();
            var race = db.Races[(int)RaceId.Elf];
            var a = GeographyIndex.Build(GeoTestMaps.Standard(), 42UL);
            var b = GeographyIndex.Build(GeoTestMaps.Standard(), 42UL);
            int id = a.RegionAt(GeoTestMaps.ForestTile).Id;
            Assert.AreEqual(PlaceNames.Rename(a, id, race, null), PlaceNames.Rename(b, id, race, null));
            Assert.IsNotNull(PlaceNames.Rename(a, a.RegionAt(GeoTestMaps.LakeTile).Id, null, null), "null race uses generic syllables");
        }
    }
}
