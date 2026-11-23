using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Security;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Security
{
    public class ContentValidatorTests
    {
        private static readonly BuildingType[] Uniques = { BuildingType.GrandCathedral, BuildingType.WorldTree, BuildingType.DeepForge, BuildingType.WarIdol, BuildingType.SunPyramid };
        private static readonly RaceId[] Owners = { RaceId.Human, RaceId.Elf, RaceId.Dwarf, RaceId.Orc, RaceId.Saurian };

        private static ResourceAmount[] R(ResourceType t, float a) => new[] { new ResourceAmount(t, a) };

        private static BuildingDefinition B(BuildingType t, AgeId age, JobType job, ResourceAmount[] inputs, ResourceAmount[] outputs)
        {
            return new BuildingDefinition
            {
                Type = t, Name = t.ToString(), MinAge = age, Category = BuildingCategory.Resource, BuildWork = 20f,
                Job = job, JobSlots = job == JobType.None ? 0 : 2,
                InputsPerWorker = inputs ?? ResourceAmount.None, OutputsPerWorker = outputs ?? ResourceAmount.None,
            };
        }

        /// <summary>A small but "real" economy: Dawn food/wood/stone, Bronze mine -> smelter, Iron blacksmith.</summary>
        private static GameDatabase EconomyDb(bool foodAtDawn = true, bool includeMine = true, System.Action<GameDatabase> extra = null)
        {
            var db = new GameDatabase();
            db.Register(B(BuildingType.GathererHut, foodAtDawn ? AgeId.Dawn : AgeId.Bronze, JobType.Gatherer, null, R(ResourceType.Food, 0.5f)));
            db.Register(B(BuildingType.WoodcutterCamp, AgeId.Dawn, JobType.Woodcutter, null, R(ResourceType.Wood, 0.5f)));
            db.Register(B(BuildingType.StoneQuarry, AgeId.Dawn, JobType.Quarrier, null, R(ResourceType.Stone, 0.4f)));
            if (includeMine) db.Register(B(BuildingType.Mine, AgeId.Bronze, JobType.Miner, null, R(ResourceType.Ore, 0.4f)));
            db.Register(B(BuildingType.Smelter, AgeId.Bronze, JobType.Smith, R(ResourceType.Ore, 0.5f), R(ResourceType.Metal, 0.3f)));
            db.Register(B(BuildingType.Blacksmith, AgeId.Iron, JobType.Smith,
                new[] { new ResourceAmount(ResourceType.Metal, 0.2f), new ResourceAmount(ResourceType.Wood, 0.1f) }, R(ResourceType.Tools, 0.2f)));
            for (int k = 0; k < Uniques.Length; k++)
                db.Register(new BuildingDefinition { Type = Uniques[k], Name = Uniques[k].ToString(), Category = BuildingCategory.Wonder, AllowedRaces = Owners[k].ToMask(), BuildWork = 500f });
            extra?.Invoke(db);
            TestDb.FillMissing(db);
            return db;
        }

        private static bool Has(List<string> messages, params string[] fragments)
        {
            foreach (var m in messages)
            {
                bool all = true;
                foreach (var f in fragments) if (!m.Contains(f)) { all = false; break; }
                if (all) return true;
            }
            return false;
        }

        [Test]
        public void TestDbStub_PassesWithoutErrors()
        {
            var db = TestDb.CreateStub();
            var report = ContentValidator.Analyze(db);
            Assert.AreEqual(0, report.ErrorCount, report.ToString());
            Assert.IsTrue(report.IsValid);
            Assert.AreEqual(0, ContentValidator.Validate(db).Count);
            Assert.IsTrue(Has(report.Warnings, "placeholder"), "stub content is recognized and reported as a warning");
        }

        [Test]
        public void CompleteSyntheticEconomy_PassesWithoutErrors()
        {
            var report = ContentValidator.Analyze(EconomyDb());
            Assert.AreEqual(0, report.ErrorCount, report.ToString());
        }

        [Test]
        public void FrozenAndUnfrozenDatabases_GiveTheSameErrors()
        {
            var a = EconomyDb();
            var b = EconomyDb();
            b.Freeze();
            Assert.AreEqual(ContentValidator.Validate(a).Count, ContentValidator.Validate(b).Count);
        }

        [Test]
        public void BrokenProductionChain_IsAnError()
        {
            var errors = ContentValidator.Validate(EconomyDb(includeMine: false));
            Assert.IsTrue(Has(errors, "Smelter", "Ore"), string.Join("\n", errors));
            // Transitive closure: Metal cannot be made without Ore, so the Blacksmith chain is broken too.
            Assert.IsTrue(Has(errors, "Blacksmith", "Metal"), string.Join("\n", errors));
        }

        [Test]
        public void ProducerUnlockedEarlyByTech_ClosesTheChain()
        {
            var db = EconomyDb(includeMine: false, extra: d =>
            {
                d.Register(B(BuildingType.Mine, AgeId.Iron, JobType.Miner, null, R(ResourceType.Ore, 0.4f)));
                d.Register(new TechDefinition { Key = "bronze.mining", Name = "Mining", Age = AgeId.Bronze, Cost = 100f, Effects = new[] { TechEffect.Unlock(BuildingType.Mine) } });
            });
            var errors = ContentValidator.Validate(db);
            Assert.IsFalse(Has(errors, "Smelter", "Ore"), string.Join("\n", errors));
        }

        [Test]
        public void MissingDawnFoodSource_IsAnError_ForEveryRace()
        {
            var errors = ContentValidator.Validate(EconomyDb(foodAtDawn: false));
            foreach (var race in EnumUtil.AllRaces)
                Assert.IsTrue(Has(errors, "Race " + race, "Dawn-age producer of Food"), string.Join("\n", errors));
            Assert.IsFalse(Has(errors, "producer of Wood"));
        }

        [Test]
        public void MissingDawnDwelling_IsAnError()
        {
            var db = EconomyDb(extra: d => d.Register(new BuildingDefinition { Type = BuildingType.House, Name = "House", HousingMin = 3, HousingMax = 5, MinAge = AgeId.Bronze, BuildWork = 10f }));
            Assert.IsTrue(Has(ContentValidator.Validate(db), "no dwelling"));
        }

        [Test]
        public void RaceRestrictedProducer_LeavesOtherRacesWithoutFood()
        {
            var db = EconomyDb(extra: d =>
            {
                var hut = B(BuildingType.GathererHut, AgeId.Dawn, JobType.Gatherer, null, R(ResourceType.Food, 0.5f));
                hut.AllowedRaces = RaceMask.Elf;
                d.Register(hut);
            });
            var errors = ContentValidator.Validate(db);
            Assert.IsTrue(Has(errors, "Race Human", "Food"));
            Assert.IsFalse(Has(errors, "Race Elf", "Food"));
        }

        [Test]
        public void NonFiniteAndOutOfBoundsNumbers_AreErrors()
        {
            var db = EconomyDb(extra: d =>
            {
                d.Register(new BuildingDefinition { Type = BuildingType.Well, Name = "Well", BuildWork = float.NaN });
                d.Register(new BuildingDefinition { Type = BuildingType.Storehouse, Name = "Storehouse", BuildWork = 200000f, Cost = R(ResourceType.Wood, 200000f), StorageCapacity = float.PositiveInfinity });
                d.Register(B(BuildingType.FishingHut, AgeId.Dawn, JobType.Fisher, null, R(ResourceType.Food, 50f)));
                d.Register(new BuildingDefinition { Type = BuildingType.Campfire, Name = "Campfire", Cost = R(ResourceType.Wood, -5f) });
            });
            var errors = ContentValidator.Validate(db);
            Assert.IsTrue(Has(errors, "Building Well", "BuildWork", "finite"), string.Join("\n", errors));
            Assert.IsTrue(Has(errors, "Building Storehouse", "BuildWork"));
            Assert.IsTrue(Has(errors, "Building Storehouse", "Cost[0]"));
            Assert.IsTrue(Has(errors, "Building Storehouse", "StorageCapacity"));
            Assert.IsTrue(Has(errors, "Building FishingHut", "OutputsPerWorker"));
            Assert.IsTrue(Has(errors, "Building Campfire", "Cost[0]"));
        }

        [Test]
        public void UndefinedEnumsAndNullArrays_AreErrors_AndNeverThrow()
        {
            var db = EconomyDb(extra: d =>
            {
                d.Register(new BuildingDefinition { Type = BuildingType.Granary, Name = "Granary", OutputsPerWorker = R((ResourceType)200, 1f), Job = JobType.Farmer, JobSlots = 1 });
                d.Register(new BuildingDefinition { Type = BuildingType.Shrine, Name = "Shrine", Cost = null, Services = null, Category = (BuildingCategory)99 });
                d.Register(new TechDefinition { Key = "bad.tech", Name = "Bad", Cost = 10f, Effects = null, PrerequisiteKeys = new[] { "missing.key" } });
            });
            ContentReport report = null;
            report = ContentValidator.Analyze(db);
            Assert.IsTrue(Has(report.Errors, "Granary", "ResourceType"), report.ToString());
            Assert.IsTrue(Has(report.Errors, "Shrine", "Cost is null"));
            Assert.IsTrue(Has(report.Errors, "Shrine", "Services is null"));
            Assert.IsTrue(Has(report.Errors, "Shrine", "Category"));
            Assert.IsTrue(Has(report.Errors, "bad.tech", "Effects is null"));
            Assert.IsTrue(Has(report.Errors, "bad.tech", "PrerequisiteKeys"));
        }

        [Test]
        public void TextBounds_AndInjection_AreErrors()
        {
            var db = EconomyDb(extra: d =>
            {
                d.Register(new BuildingDefinition { Type = BuildingType.Well, Name = new string('W', 65) });
                d.Register(new BuildingDefinition { Type = BuildingType.Storehouse, Name = "Store", Description = new string('d', 1001) });
                d.Register(new BuildingDefinition { Type = BuildingType.Campfire, Name = "Camp‮fire" });
                d.Register(new BuildingDefinition { Type = BuildingType.SpiritTotem, Name = null });
            });
            var errors = ContentValidator.Validate(db);
            Assert.IsTrue(Has(errors, "Building Well", "Name", "too long"));
            Assert.IsTrue(Has(errors, "Building Storehouse", "Description", "too long"));
            Assert.IsTrue(Has(errors, "Building Campfire", "bidi"));
            Assert.IsTrue(Has(errors, "Building SpiritTotem", "Name is null"));
            foreach (var e in errors) Assert.IsFalse(e.Contains("‮") || e.Contains("WWWWWWWW"), "messages never echo content text");
        }

        [Test]
        public void DangerousCivilizationPatterns_AreErrors()
        {
            Assert.IsTrue(ContentValidator.IsSafeFormatPattern("The {0} Realm", out int n) && n == 1);
            Assert.IsTrue(ContentValidator.IsSafeFormatPattern("{{literal}} {0}", out n) && n == 1);
            Assert.IsFalse(ContentValidator.IsSafeFormatPattern("{0,99999999}", out _));
            Assert.IsFalse(ContentValidator.IsSafeFormatPattern("{1}", out _));
            Assert.IsFalse(ContentValidator.IsSafeFormatPattern("{0:N}", out _));
            Assert.IsFalse(ContentValidator.IsSafeFormatPattern("oops }", out _));

            var db = EconomyDb(extra: d =>
            {
                var race = new RaceDefinition { Id = RaceId.Orc, Name = "Orc", PluralName = "Orcs", Adjective = "Orcish" };
                race.Names.CivilizationPatterns = new[] { "{0,99999999}" };
                d.Register(race);
            });
            Assert.IsTrue(Has(ContentValidator.Validate(db), "Race Orc", "CivilizationPatterns"));
        }

        [Test]
        public void RacialUnique_MustBeExactlyOneRace()
        {
            var db = EconomyDb(extra: d =>
            {
                d.Register(new BuildingDefinition { Type = BuildingType.GrandCathedral, Name = "Grand Cathedral", AllowedRaces = RaceMask.All, BuildWork = 500f });
                d.Register(new BuildingDefinition { Type = BuildingType.WorldTree, Name = "World Tree", AllowedRaces = RaceMask.Elf | RaceMask.Human, BuildWork = 500f });
                var dwarf = new RaceDefinition { Id = RaceId.Dwarf, Name = "Dwarf", PluralName = "Dwarves", Adjective = "Dwarven" };
                dwarf.UniqueBuildings = new[] { BuildingType.DeepForge, BuildingType.Library };
                d.Register(dwarf);
            });
            var errors = ContentValidator.Validate(db);
            Assert.IsTrue(Has(errors, "GrandCathedral", "exactly one race"), string.Join("\n", errors));
            Assert.IsTrue(Has(errors, "WorldTree", "exactly one race"));
            Assert.IsTrue(Has(errors, "Library", "exactly one race"), "a race-listed unique must be single-race too");
            Assert.IsFalse(Has(errors, "DeepForge"));
        }

        [Test]
        public void FreeWrathPower_IsAnError()
        {
            var db = EconomyDb(extra: d => d.Register(new GodPowerDefinition { Type = GodPowerType.Meteor, Name = "Meteor", Category = GodPowerCategory.Wrath, ManaCost = 0f, MaxRadius = 4 }));
            Assert.IsTrue(Has(ContentValidator.Validate(db), "Meteor", "wrath"));
        }

        [Test]
        public void UnreachableBuilding_IsAnError()
        {
            var db = EconomyDb(extra: d => d.Register(new BuildingDefinition { Type = BuildingType.Tavern, Name = "Tavern", AllowedRaces = RaceMask.None }));
            Assert.IsTrue(Has(ContentValidator.Validate(db), "Tavern", "unreachable"));
        }

        [Test]
        public void DecreasingTechCosts_AreAWarning()
        {
            var db = EconomyDb(extra: d =>
            {
                d.Register(new TechDefinition { Key = "dawn.a", Name = "A", Age = AgeId.Dawn, Cost = 500f });
                d.Register(new TechDefinition { Key = "bronze.b", Name = "B", Age = AgeId.Bronze, Cost = 100f });
            });
            var report = ContentValidator.Analyze(db);
            Assert.IsTrue(Has(report.Warnings, "Average tech cost decreases"), report.ToString());
            Assert.IsFalse(Has(report.Errors, "Average tech cost"));
        }

        [Test]
        public void HugeTechList_IsRejected_WithoutRecursingIntoStructuralValidation()
        {
            var db = new GameDatabase();
            for (int i = 0; i <= ContentValidator.MaxTechCount; i++)
            {
                var t = new TechDefinition { Key = "t" + i, Name = "T", Cost = 10f };
                if (i > 0) t.Prerequisites = new[] { i - 1 }; // one long chain
                db.Register(t);
            }
            TestDb.FillMissing(db);
            var report = ContentValidator.Analyze(db);
            Assert.IsTrue(Has(report.Errors, "Too many techs"), report.ToString());
        }

        [Test]
        public void NullDatabase_IsReportedNotThrown()
        {
            var report = ContentValidator.Analyze(null);
            Assert.IsFalse(report.IsValid);
            Assert.AreEqual(1, ContentValidator.Validate(null).Count);
        }

        [Test]
        public void Report_IsBounded()
        {
            var r = new ContentReport();
            for (int i = 0; i < ContentReport.MaxEntriesPerList + 500; i++) { r.AddError("e"); r.AddWarning("w"); }
            Assert.AreEqual(ContentReport.MaxEntriesPerList, r.Errors.Count);
            Assert.AreEqual(ContentReport.MaxEntriesPerList + 500, r.ErrorCount);
            Assert.IsTrue(r.ToString().Contains("suppressed"));
        }
    }
}
