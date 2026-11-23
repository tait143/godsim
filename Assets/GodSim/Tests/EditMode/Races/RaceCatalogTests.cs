using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Races;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Races
{
    /// <summary>Registration, structural validity and contract-table values of the five race definitions.</summary>
    public class RaceCatalogTests
    {
        private const float Eps = 1e-5f;

        // ------------------------------------------------------------------ registration

        [Test]
        public void Register_FillsAllFiveRaces_WithRealData_BeforeAnyStubbing()
        {
            var db = new GameDatabase();
            RaceCatalog.Register(db);

            for (int i = 0; i < Counts.Races; i++)
            {
                var id = (RaceId)i;
                Assert.IsNotNull(db.Races[i], "slot " + id + " must be filled by RaceCatalog itself, not by TestDb.FillMissing");
                Assert.AreEqual(id, db.Race(id).Id);
                Assert.IsTrue(db.Races[i].Names.PersonPrefixes.Length >= 20, id + " has stub-sized name tables");
                Assert.AreEqual(RaceLore.Summary(id), db.Races[i].Description);
                Assert.IsTrue(db.Races[i].Description.Length > 0);
            }
            Assert.AreEqual(350f, db.Race(RaceId.Elf).LifeExpectancy, Eps);
            Assert.AreEqual(0x3B6FD8FFu, db.Race(RaceId.Human).BannerColor);
            Assert.AreEqual(BuildingType.SunPyramid, db.Race(RaceId.Saurian).UniqueBuildings[0]);
        }

        [Test]
        public void Register_ThenFillMissingAndFreeze_Succeeds_AndKeepsRealRaces()
        {
            var db = new GameDatabase();
            RaceCatalog.Register(db);
            TestDb.FillMissing(db);

            for (int i = 0; i < Counts.Races; i++)
                Assert.AreEqual(RaceLore.Summary((RaceId)i), db.Races[i].Description, "FillMissing replaced " + (RaceId)i);

            var errors = db.Validate();
            Assert.AreEqual(0, errors.Count, string.Join("; ", errors));
            Assert.DoesNotThrow(() => db.Freeze());
            Assert.IsTrue(db.IsFrozen);
        }

        [Test]
        public void Register_OverStubs_ReplacesThem()
        {
            var db = new GameDatabase();
            TestDb.FillMissing(db);
            Assert.AreEqual("", db.Race(RaceId.Orc).Description);
            RaceCatalog.Register(db);
            Assert.AreEqual(50f, db.Race(RaceId.Orc).LifeExpectancy, Eps);
            Assert.AreEqual(RaceLore.Summary(RaceId.Orc), db.Race(RaceId.Orc).Description);
            Assert.DoesNotThrow(() => db.Freeze());
        }

        [Test]
        public void Register_NullDatabase_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => RaceCatalog.Register(null));
        }

        [Test]
        public void Register_FrozenDatabase_Throws_AndLeavesItUnchanged()
        {
            var db = TestDb.CreateStub();
            Assert.Throws<InvalidOperationException>(() => RaceCatalog.Register(db));
            Assert.AreEqual("", db.Race(RaceId.Human).Description);
        }

        [Test]
        public void CreateAll_IsIndexedByRaceId_AndReturnsFreshInstances()
        {
            var a = RaceCatalog.CreateAll();
            var b = RaceCatalog.CreateAll();
            Assert.AreEqual(Counts.Races, a.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual((RaceId)i, a[i].Id);
                Assert.AreNotSame(a[i], b[i]);
                Assert.AreNotSame(a[i].BiomeHabitability, b[i].BiomeHabitability);
                Assert.AreNotSame(a[i].ProductionMultiplier, b[i].ProductionMultiplier);
                Assert.AreNotSame(a[i].RaceAffinity, b[i].RaceAffinity);
                Assert.AreNotSame(a[i].Architecture, b[i].Architecture);
                Assert.AreNotSame(a[i].Names, b[i].Names);
                Assert.AreNotSame(a[i].Names.PersonPrefixes, b[i].Names.PersonPrefixes);
                Assert.AreNotSame(a[i].UniqueBuildings, b[i].UniqueBuildings);
                Assert.AreNotSame(a[i].FavoredBuildings, b[i].FavoredBuildings);
            }
        }

        [Test]
        public void Create_IsDeterministic()
        {
            for (int i = 0; i < Counts.Races; i++)
                Assert.AreEqual(Fingerprint(RaceCatalog.Create((RaceId)i)), Fingerprint(RaceCatalog.Create((RaceId)i)));
        }

        [Test]
        public void Create_UnknownRace_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => RaceCatalog.Create((RaceId)200));
        }

        [Test]
        public void PerRaceFactories_MatchTheCatalog()
        {
            Assert.AreEqual(Fingerprint(HumanRace.Create()), Fingerprint(RaceCatalog.Create(RaceId.Human)));
            Assert.AreEqual(Fingerprint(ElfRace.Create()), Fingerprint(RaceCatalog.Create(RaceId.Elf)));
            Assert.AreEqual(Fingerprint(DwarfRace.Create()), Fingerprint(RaceCatalog.Create(RaceId.Dwarf)));
            Assert.AreEqual(Fingerprint(OrcRace.Create()), Fingerprint(RaceCatalog.Create(RaceId.Orc)));
            Assert.AreEqual(Fingerprint(SaurianRace.Create()), Fingerprint(RaceCatalog.Create(RaceId.Saurian)));
        }

        [Test]
        public void Races_AreMutuallyDistinct()
        {
            var all = RaceCatalog.CreateAll();
            for (int i = 0; i < all.Length; i++)
                for (int j = i + 1; j < all.Length; j++)
                    Assert.AreNotEqual(Fingerprint(all[i]), Fingerprint(all[j]));
        }

        // ------------------------------------------------------------------ per-race structure

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void Arrays_HaveContractLengths_AndFiniteValues(RaceId id)
        {
            var r = RaceCatalog.Create(id);
            Assert.AreEqual(Counts.Biomes, r.BiomeHabitability.Length);
            Assert.AreEqual(Counts.Resources, r.ProductionMultiplier.Length);
            Assert.AreEqual(Counts.Races, r.RaceAffinity.Length);
            AssertFinite(r.BiomeHabitability, id + " habitability");
            AssertFinite(r.ProductionMultiplier, id + " production");
            AssertFinite(r.RaceAffinity, id + " affinity");
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void Habitability_IsZeroOnWater_AndInUnitRange(RaceId id)
        {
            var r = RaceCatalog.Create(id);
            for (int b = 0; b < Counts.Biomes; b++)
            {
                float v = r.BiomeHabitability[b];
                Assert.IsTrue(v >= 0f && v <= 1f, id + " " + (Biome)b + " = " + v);
                if (((Biome)b).IsWater()) Assert.AreEqual(0f, v, id + " must not live on " + (Biome)b);
            }
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void AtLeastThreeLandBiomes_AreGoodHomes(RaceId id)
        {
            var r = RaceCatalog.Create(id);
            int good = 0;
            for (int b = 0; b < Counts.Biomes; b++)
                if (((Biome)b).IsLand() && r.BiomeHabitability[b] >= 0.7f) good++;
            Assert.IsTrue(good >= 3, id + " has only " + good + " biomes >= 0.7");
        }

        [TestCase(RaceId.Human, Biome.Grassland, 1.0f)]
        [TestCase(RaceId.Human, Biome.Forest, 0.8f)]
        [TestCase(RaceId.Human, Biome.Hills, 0.7f)]
        [TestCase(RaceId.Elf, Biome.Forest, 1.0f)]
        [TestCase(RaceId.Elf, Biome.Jungle, 0.8f)]
        [TestCase(RaceId.Elf, Biome.Taiga, 0.6f)]
        [TestCase(RaceId.Elf, Biome.Grassland, 0.7f)]
        [TestCase(RaceId.Dwarf, Biome.Hills, 1.0f)]
        [TestCase(RaceId.Dwarf, Biome.Mountain, 0.9f)]
        [TestCase(RaceId.Dwarf, Biome.Taiga, 0.7f)]
        [TestCase(RaceId.Orc, Biome.Savanna, 1.0f)]
        [TestCase(RaceId.Orc, Biome.Hills, 0.8f)]
        [TestCase(RaceId.Orc, Biome.Tundra, 0.7f)]
        [TestCase(RaceId.Orc, Biome.Desert, 0.6f)]
        [TestCase(RaceId.Saurian, Biome.Swamp, 1.0f)]
        [TestCase(RaceId.Saurian, Biome.Jungle, 0.9f)]
        [TestCase(RaceId.Saurian, Biome.Beach, 0.8f)]
        [TestCase(RaceId.Saurian, Biome.Snow, 0f)]
        public void Habitability_MatchesBrief(RaceId id, Biome biome, float expected)
        {
            Assert.AreEqual(expected, RaceCatalog.Create(id).BiomeHabitability[(int)biome], Eps);
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void ProductionMultipliers_ArePositiveAndBounded(RaceId id)
        {
            var p = RaceCatalog.Create(id).ProductionMultiplier;
            for (int i = 0; i < p.Length; i++)
                Assert.IsTrue(p[i] >= 0.5f && p[i] <= 2f, id + " " + (ResourceType)i + " = " + p[i]);
        }

        [TestCase(RaceId.Dwarf, ResourceType.Wood, 0.8f)]
        [TestCase(RaceId.Elf, ResourceType.Wood, 1.3f)]
        [TestCase(RaceId.Elf, ResourceType.Food, 1.1f)]
        [TestCase(RaceId.Elf, ResourceType.Ore, 0.7f)]
        [TestCase(RaceId.Orc, ResourceType.Weapons, 1.4f)]
        [TestCase(RaceId.Orc, ResourceType.Food, 0.9f)]
        [TestCase(RaceId.Saurian, ResourceType.Food, 1.2f)]
        [TestCase(RaceId.Saurian, ResourceType.Gold, 1.2f)]
        [TestCase(RaceId.Human, ResourceType.Goods, 1.15f)]
        [TestCase(RaceId.Human, ResourceType.Gold, 1.15f)]
        public void ProductionMultiplier_MatchesBrief(RaceId id, ResourceType res, float expected)
        {
            Assert.AreEqual(expected, RaceCatalog.Create(id).ProductionMultiplier[(int)res], Eps);
        }

        [TestCase(ResourceType.Ore)]
        [TestCase(ResourceType.Metal)]
        [TestCase(ResourceType.Tools)]
        [TestCase(ResourceType.Stone)]
        public void Dwarves_AreMasterMinersAndSmiths(ResourceType res)
        {
            float v = RaceCatalog.Create(RaceId.Dwarf).ProductionMultiplier[(int)res];
            Assert.IsTrue(v >= 1.3f && v <= 1.5f, res + " = " + v);
            Assert.AreEqual(RaceId.Dwarf, ArgMax(r => r.ProductionMultiplier[(int)res]), "Dwarves must lead " + res);
        }

        // Fertility, MaxChildren and elf adulthood re-tuned by the 2026-11-19 balance pass (was Human 0.30/6,
        // Elf adult 40 0.05/3, Dwarf 0.10/4, Orc 0.45/8, Saurian 0.32/8). See docs/BALANCE_PASS.md.
        [TestCase(RaceId.Human, 16f, 55f, 70f, 0.21f, 4)]
        [TestCase(RaceId.Elf, 30f, 280f, 350f, 0.085f, 5)]
        [TestCase(RaceId.Dwarf, 30f, 150f, 200f, 0.14f, 6)]
        [TestCase(RaceId.Orc, 12f, 38f, 50f, 0.22f, 5)]
        [TestCase(RaceId.Saurian, 10f, 70f, 90f, 0.09f, 4)]
        public void LifeCycle_MatchesContractTable(RaceId id, float adult, float elder, float life, float fertility, int maxChildren)
        {
            var r = RaceCatalog.Create(id);
            Assert.AreEqual(adult, r.AdulthoodAge, Eps);
            Assert.AreEqual(elder, r.ElderAge, Eps);
            Assert.AreEqual(life, r.LifeExpectancy, Eps);
            Assert.AreEqual(fertility, r.FertilityPerYear, Eps);
            Assert.AreEqual(maxChildren, r.MaxChildren);
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void LifeCycle_IsInternallyConsistent(RaceId id)
        {
            var r = RaceCatalog.Create(id);
            Assert.IsTrue(r.AdulthoodAge > 0f && r.AdulthoodAge < r.ElderAge, id + " adulthood < elder");
            Assert.IsTrue(r.ElderAge < r.LifeExpectancy, id + " elder < life expectancy");
            Assert.IsTrue(r.FertilityPerYear > 0f && r.FertilityPerYear < 1f, id + " fertility in (0,1)");
            Assert.IsTrue(r.MaxChildren >= 1, id + " max children");
            Assert.AreEqual(LifeStage.Child, WorldOps.StageFor(r, r.AdulthoodAge - 0.01f));
            Assert.AreEqual(LifeStage.Adult, WorldOps.StageFor(r, r.AdulthoodAge));
            Assert.AreEqual(LifeStage.Elder, WorldOps.StageFor(r, r.ElderAge));
        }

        [Test]
        public void Lifespans_AreOrderedAsInTheContractTable()
        {
            float human = Life(RaceId.Human), elf = Life(RaceId.Elf), dwarf = Life(RaceId.Dwarf), orc = Life(RaceId.Orc), saurian = Life(RaceId.Saurian);
            Assert.IsTrue(elf > dwarf, "Elf > Dwarf");
            Assert.IsTrue(dwarf > saurian, "Dwarf > Saurian");
            Assert.IsTrue(saurian > human, "Saurian > Human");
            Assert.IsTrue(human > orc, "Human > Orc");
        }

        [Test]
        public void Fertility_OrcsHighest_ElvesLowest()
        {
            Assert.AreEqual(RaceId.Orc, ArgMax(r => r.FertilityPerYear));
            Assert.AreEqual(RaceId.Elf, ArgMax(r => -r.FertilityPerYear));
        }

        [TestCase(RaceId.Human, 0.05f)]
        [TestCase(RaceId.Elf, 0.04f)]
        [TestCase(RaceId.Dwarf, 0.05f)]
        [TestCase(RaceId.Orc, 0.06f)]
        [TestCase(RaceId.Saurian, 0.045f)]
        public void FoodPerTick_MatchesBrief(RaceId id, float expected)
        {
            Assert.AreEqual(expected, RaceCatalog.Create(id).FoodPerTick, Eps);
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void Aptitudes_ArePositiveAndBounded(RaceId id)
        {
            var r = RaceCatalog.Create(id);
            float[] apt = { r.BuildSpeed, r.ResearchRate, r.FaithRate, r.MilitaryStrength, r.TradeAptitude, r.MoveSpeed, r.DiseaseResistance, r.BaseHealth };
            AssertFinite(apt, id + " aptitudes");
            for (int i = 0; i < apt.Length; i++) Assert.IsTrue(apt[i] >= 0.5f && apt[i] <= 2f, id + " aptitude #" + i + " = " + apt[i]);
        }

        [Test]
        public void Aptitudes_ReflectContractStrengths()
        {
            Assert.AreEqual(RaceId.Elf, ArgMax(r => r.ResearchRate), "Elves are the best researchers");
            Assert.AreEqual(RaceId.Elf, ArgMax(r => r.DiseaseResistance), "Elves are the healthiest");
            Assert.AreEqual(RaceId.Orc, ArgMax(r => r.MilitaryStrength), "Orcs are the strongest fighters");
            Assert.AreEqual(RaceId.Orc, ArgMax(r => r.BaseHealth), "Orcs are the hardiest");
            Assert.AreEqual(RaceId.Human, ArgMax(r => r.TradeAptitude), "Humans are the best traders");
            Assert.AreEqual(RaceId.Dwarf, ArgMax(r => r.BuildSpeed), "Dwarves are the best builders");
            Assert.AreEqual(RaceId.Saurian, ArgMax(r => r.FaithRate), "Saurians are the most devout");
            Assert.IsTrue(RaceCatalog.Create(RaceId.Elf).FaithRate > 1.1f, "Elves are devout too");
            Assert.IsTrue(RaceCatalog.Create(RaceId.Dwarf).MilitaryStrength > 1f, "Dwarves are strong defenders");
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void Personality_IsInUnitRange(RaceId id)
        {
            var r = RaceCatalog.Create(id);
            float[] traits = { r.Aggression, r.Expansionism, r.Piety, r.Industriousness, r.Curiosity, r.Diplomacy, r.Xenophobia, r.Mercantilism };
            AssertFinite(traits, id + " personality");
            for (int i = 0; i < traits.Length; i++) Assert.IsTrue(traits[i] >= 0f && traits[i] <= 1f, id + " trait #" + i + " = " + traits[i]);
        }

        [Test]
        public void Personality_ReflectsContractTable()
        {
            Assert.AreEqual(RaceId.Orc, ArgMax(r => r.Aggression), "Orcs are aggressive");
            Assert.AreEqual(RaceId.Orc, ArgMax(r => r.Expansionism), "Orcs are expansionist");
            Assert.AreEqual(RaceId.Elf, ArgMax(r => r.Xenophobia), "Elves are xenophobic");
            Assert.AreEqual(RaceId.Elf, ArgMax(r => -r.Aggression), "Elves are peaceful");
            Assert.AreEqual(RaceId.Elf, ArgMax(r => r.Curiosity), "Elves are scholars");
            Assert.AreEqual(RaceId.Dwarf, ArgMax(r => r.Industriousness), "Dwarves are industrious");
            Assert.AreEqual(RaceId.Saurian, ArgMax(r => r.Piety), "Saurians are pious");
            Assert.AreEqual(RaceId.Human, ArgMax(r => r.Mercantilism), "Humans are traders");
            Assert.AreEqual(RaceId.Human, ArgMax(r => r.Diplomacy), "Humans are diplomats");
        }

        [TestCase(RaceId.Human, 0.8f)]
        [TestCase(RaceId.Elf, 0.1f)]
        [TestCase(RaceId.Dwarf, 0.6f)]
        [TestCase(RaceId.Orc, 0.2f)]
        [TestCase(RaceId.Saurian, 0.5f)]
        public void Layout_IsWithinRanges_AndRoadAffinityMatchesBrief(RaceId id, float roads)
        {
            var r = RaceCatalog.Create(id);
            Assert.AreEqual(roads, r.RoadAffinity, Eps);
            Assert.IsTrue(r.MinSettlementSpacing >= 12 && r.MinSettlementSpacing <= 20, id + " spacing " + r.MinSettlementSpacing);
            Assert.IsTrue(r.Compactness >= 0f && r.Compactness <= 1f, id + " compactness");
        }

        [Test]
        public void Layout_DwarvesDensest_OrcsSprawling()
        {
            Assert.AreEqual(RaceId.Dwarf, ArgMax(r => r.Compactness));
            Assert.AreEqual(RaceId.Orc, ArgMax(r => -r.Compactness));
        }

        [TestCase(RaceId.Human, RoofStyle.Gable, false, false, false)]
        [TestCase(RaceId.Elf, RoofStyle.Leaf, false, true, false)]
        [TestCase(RaceId.Dwarf, RoofStyle.Flat, true, false, false)]
        [TestCase(RaceId.Orc, RoofStyle.Cone, false, false, false)]
        [TestCase(RaceId.Saurian, RoofStyle.Pyramid, false, false, true)]
        public void Architecture_MatchesContractTable(RaceId id, RoofStyle roof, bool underground, bool trees, bool stilts)
        {
            var a = RaceCatalog.Create(id).Architecture;
            Assert.IsNotNull(a);
            Assert.AreEqual(roof, a.Roof);
            Assert.AreEqual(underground, a.BuildsUnderground, "BuildsUnderground");
            Assert.AreEqual(trees, a.BuildsInTrees, "BuildsInTrees");
            Assert.AreEqual(stilts, a.BuildsOnStilts, "BuildsOnStilts");
            Assert.IsFalse(string.IsNullOrEmpty(a.StyleTag));
            Assert.IsTrue(a.HeightScale >= 0.5f && a.HeightScale <= 2f, "HeightScale");
            Assert.IsTrue(a.WidthScale >= 0.5f && a.WidthScale <= 2f, "WidthScale");
            Assert.IsTrue(a.Roundness >= 0f && a.Roundness <= 1f, "Roundness");
            uint[] colors = { a.WallColor, a.RoofColor, a.TrimColor, a.AccentColor };
            for (int i = 0; i < colors.Length; i++) Assert.AreEqual(0xFFu, colors[i] & 0xFFu, "color #" + i + " must be opaque");
            Assert.AreNotEqual(a.WallColor, a.RoofColor);
        }

        [Test]
        public void Architecture_StyleTagsAndRoofsAreDistinctPerRace()
        {
            var all = RaceCatalog.CreateAll();
            for (int i = 0; i < all.Length; i++)
                for (int j = i + 1; j < all.Length; j++)
                {
                    Assert.AreNotEqual(all[i].Architecture.StyleTag, all[j].Architecture.StyleTag);
                    Assert.AreNotEqual(all[i].Architecture.Roof, all[j].Architecture.Roof);
                }
        }

        [TestCase(RaceId.Human, 0x3B6FD8FFu)]
        [TestCase(RaceId.Elf, 0x2FA84FFFu)]
        [TestCase(RaceId.Dwarf, 0xC8741EFFu)]
        [TestCase(RaceId.Orc, 0xB3261EFFu)]
        [TestCase(RaceId.Saurian, 0x8E44ADFFu)]
        public void BannerColor_MatchesBrief(RaceId id, uint expected)
        {
            var r = RaceCatalog.Create(id);
            Assert.AreEqual(expected, r.BannerColor);
            Assert.AreEqual(0xFFu, r.SkinColor & 0xFFu, "skin must be opaque");
        }

        [Test]
        public void BannerAndSkinColors_AreDistinguishable()
        {
            var all = RaceCatalog.CreateAll();
            for (int i = 0; i < all.Length; i++)
                for (int j = i + 1; j < all.Length; j++)
                {
                    Assert.IsTrue(RgbDistance(all[i].BannerColor, all[j].BannerColor) >= 70.0,
                        all[i].Id + "/" + all[j].Id + " banners too similar");
                    Assert.IsTrue(RgbDistance(all[i].SkinColor, all[j].SkinColor) >= 40.0,
                        all[i].Id + "/" + all[j].Id + " skins too similar");
                }
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void Identity_TextIsFilled(RaceId id)
        {
            var r = RaceCatalog.Create(id);
            Assert.AreEqual(id.ToString(), r.Name);
            Assert.IsFalse(string.IsNullOrEmpty(r.PluralName));
            Assert.IsFalse(string.IsNullOrEmpty(r.Adjective));
            Assert.AreNotEqual(r.Name, r.PluralName);
            Assert.AreEqual(RaceLore.Summary(id), r.Description);
            Assert.IsTrue(r.Description.Length >= 80);
        }

        // ------------------------------------------------------------------ buildings

        [TestCase(RaceId.Human, BuildingType.GrandCathedral)]
        [TestCase(RaceId.Elf, BuildingType.WorldTree)]
        [TestCase(RaceId.Dwarf, BuildingType.DeepForge)]
        [TestCase(RaceId.Orc, BuildingType.WarIdol)]
        [TestCase(RaceId.Saurian, BuildingType.SunPyramid)]
        public void UniqueBuilding_MatchesBrief(RaceId id, BuildingType expected)
        {
            var u = RaceCatalog.Create(id).UniqueBuildings;
            Assert.AreEqual(1, u.Length);
            Assert.AreEqual(expected, u[0]);
        }

        [Test]
        public void UniqueBuildings_ArePairwiseDistinct()
        {
            var seen = new HashSet<BuildingType>();
            foreach (var r in RaceCatalog.CreateAll())
                foreach (var b in r.UniqueBuildings)
                    Assert.IsTrue(seen.Add(b), b + " is unique to more than one race");
            Assert.AreEqual(Counts.Races, seen.Count);
        }

        [TestCase(RaceId.Human)]
        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void FavoredBuildings_AreThreeToFive_Valid_AndNotRacialUniques(RaceId id)
        {
            var f = RaceCatalog.Create(id).FavoredBuildings;
            Assert.IsTrue(f.Length >= 3 && f.Length <= 5, id + " favors " + f.Length);
            var allUniques = new HashSet<BuildingType>();
            foreach (var r in RaceCatalog.CreateAll()) foreach (var b in r.UniqueBuildings) allUniques.Add(b);
            var seen = new HashSet<BuildingType>();
            for (int i = 0; i < f.Length; i++)
            {
                Assert.IsTrue(Enum.IsDefined(typeof(BuildingType), f[i]), f[i] + " undefined");
                Assert.IsTrue(seen.Add(f[i]), id + " favors " + f[i] + " twice");
                Assert.IsFalse(allUniques.Contains(f[i]), id + " favors racial unique " + f[i]);
                Assert.AreNotEqual(BuildingType.TownCenter, f[i]);
                Assert.AreNotEqual(BuildingType.House, f[i]);
            }
        }

        // ------------------------------------------------------------------ affinity table

        [Test]
        public void Affinity_SelfIsOne_AllInRange_AndNearlySymmetric()
        {
            var all = RaceCatalog.CreateAll();
            for (int a = 0; a < all.Length; a++)
            {
                Assert.AreEqual(1f, all[a].RaceAffinity[a], Eps, (RaceId)a + " self affinity");
                for (int b = 0; b < all.Length; b++)
                {
                    float ab = all[a].RaceAffinity[b], ba = all[b].RaceAffinity[a];
                    Assert.IsTrue(ab >= -1f && ab <= 1f, (RaceId)a + "->" + (RaceId)b + " = " + ab);
                    Assert.IsTrue(Math.Abs(ab - ba) <= 0.3f + Eps, (RaceId)a + "<->" + (RaceId)b + " asymmetry " + Math.Abs(ab - ba));
                    if (a != b) Assert.IsTrue(ab < 1f, "only self may be full kinship");
                }
            }
        }

        // ------------------------------------------------------------------ helpers

        private static float Life(RaceId id) => RaceCatalog.Create(id).LifeExpectancy;

        /// <summary>Race with the strictly highest value of the selector (asserts there is no tie).</summary>
        private static RaceId ArgMax(Func<RaceDefinition, float> selector)
        {
            var all = RaceCatalog.CreateAll();
            int best = 0;
            for (int i = 1; i < all.Length; i++) if (selector(all[i]) > selector(all[best])) best = i;
            for (int i = 0; i < all.Length; i++)
                if (i != best) Assert.IsTrue(selector(all[i]) < selector(all[best]), "tie between " + (RaceId)best + " and " + (RaceId)i);
            return (RaceId)best;
        }

        private static void AssertFinite(float[] values, string what)
        {
            for (int i = 0; i < values.Length; i++)
                Assert.IsTrue(!float.IsNaN(values[i]) && !float.IsInfinity(values[i]), what + "[" + i + "] is not finite");
        }

        private static double RgbDistance(uint a, uint b)
        {
            double dr = (int)((a >> 24) & 0xFF) - (int)((b >> 24) & 0xFF);
            double dg = (int)((a >> 16) & 0xFF) - (int)((b >> 16) & 0xFF);
            double db = (int)((a >> 8) & 0xFF) - (int)((b >> 8) & 0xFF);
            return Math.Sqrt(dr * dr + dg * dg + db * db);
        }

        /// <summary>Stable text dump of every field, used to compare definitions by value.</summary>
        internal static string Fingerprint(RaceDefinition r)
        {
            var sb = new StringBuilder();
            sb.Append(r.Id).Append('|').Append(r.Name).Append('|').Append(r.PluralName).Append('|').Append(r.Adjective).Append('|').Append(r.Description);
            sb.Append('|').Append(r.BannerColor).Append('|').Append(r.SkinColor);
            float[] scalars =
            {
                r.AdulthoodAge, r.ElderAge, r.LifeExpectancy, r.FertilityPerYear, r.FoodPerTick, r.BaseHealth, r.DiseaseResistance,
                r.BuildSpeed, r.ResearchRate, r.FaithRate, r.MilitaryStrength, r.TradeAptitude, r.MoveSpeed,
                r.Aggression, r.Expansionism, r.Piety, r.Industriousness, r.Curiosity, r.Diplomacy, r.Xenophobia, r.Mercantilism,
                r.Compactness, r.RoadAffinity,
            };
            AppendAll(sb, scalars);
            sb.Append('|').Append(r.MaxChildren).Append('|').Append(r.MinSettlementSpacing);
            AppendAll(sb, r.BiomeHabitability);
            AppendAll(sb, r.ProductionMultiplier);
            AppendAll(sb, r.RaceAffinity);
            var a = r.Architecture;
            sb.Append('|').Append(a.Roof).Append(a.HeightScale).Append(a.WidthScale).Append(a.Roundness).Append(a.BuildsUnderground)
              .Append(a.BuildsInTrees).Append(a.BuildsOnStilts).Append(a.WallColor).Append(a.RoofColor).Append(a.TrimColor).Append(a.AccentColor).Append(a.StyleTag);
            var n = r.Names;
            foreach (var arr in new[] { n.PersonPrefixes, n.MaleSuffixes, n.FemaleSuffixes, n.FamilyNames, n.SettlementPrefixes, n.SettlementSuffixes, n.CivilizationPatterns })
                sb.Append('|').Append(string.Join(",", arr));
            foreach (var b in r.UniqueBuildings) sb.Append('|').Append(b);
            foreach (var b in r.FavoredBuildings) sb.Append('|').Append(b);
            return sb.ToString();
        }

        private static void AppendAll(StringBuilder sb, float[] values)
        {
            sb.Append('|');
            for (int i = 0; i < values.Length; i++) sb.Append(values[i].ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(',');
        }
    }
}
