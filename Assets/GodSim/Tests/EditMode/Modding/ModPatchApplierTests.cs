using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Modding;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Modding
{
    public class ModPatchApplierTests
    {
        private const string Manifest = "{\"id\":\"test_mod\",\"name\":\"Test Mod\",\"version\":\"1.0.0\",\"gameVersionRange\":\">=0.1.0 <1.0.0\"}";

        private static string J(string s) => s.Replace('`', '\\');
        private static string C(int code) => ((char)code).ToString();
        private static bool Has(string hay, string needle) => hay != null && hay.IndexOf(needle, StringComparison.Ordinal) >= 0;

        private static ModPack Pack(string json, string manifest = Manifest) =>
            ModPack.FromJson(manifest, new KeyValuePair<string, string>("patch.json", json));

        private static GameDatabase Db()
        {
            var db = TestDb.CreateStub(freeze: false);
            db.Register(new TechDefinition { Key = "bronze.casting", Name = "Bronze Casting", Age = AgeId.Bronze, Cost = 200f });
            var smelter = db.Building(BuildingType.Smelter);
            smelter.Job = JobType.Smith;
            smelter.JobSlots = 4;
            return db;
        }

        private static bool AnyError(ModApplyReport r, string needle)
        {
            foreach (var e in r.Errors) if (Has(e.ToString(), needle)) return true;
            return false;
        }

        private static bool AnyWarning(ModApplyReport r, string needle)
        {
            foreach (var e in r.Warnings) if (Has(e.ToString(), needle)) return true;
            return false;
        }

        /// <summary>Deterministic fingerprint of everything a mod can touch (to prove transactional behavior).</summary>
        private static string Snapshot(GameDatabase db)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var b in db.Buildings)
            {
                sb.Append(b.Name).Append('|').Append(b.Description).Append('|').Append(b.BuildWork).Append('|').Append(b.JobSlots).Append('|')
                  .Append(b.StorageCapacity).Append('|').Append(b.UpkeepGold).Append('|').Append(b.Desirability).Append('|').Append(b.VisualHeight).Append('|')
                  .Append(b.MinPopulation).Append('|').Append(b.HousingMin).Append('|').Append(b.HousingMax).Append('|').Append(b.Cost.Length).Append(';');
            }
            foreach (var r in db.Races)
                sb.Append(r.Description).Append('|').Append(r.LifeExpectancy).Append('|').Append(r.Aggression).Append('|').Append(r.BannerColor).Append('|')
                  .Append(string.Join(",", r.Names.PersonPrefixes)).Append(';');
            foreach (var a in db.Ages) sb.Append(a.ResearchThreshold).Append('|').Append(a.MinPopulation).Append(';');
            foreach (var p in db.GodPowers) sb.Append(p.ManaCost).Append('|').Append(p.CooldownTicks).Append('|').Append(p.MaxRadius).Append(';');
            foreach (var t in db.Techs) sb.Append(t.Key).Append('|').Append(t.Name).Append('|').Append(t.Cost).Append(';');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ happy path
        [Test]
        public void PatchesHouseBuildWorkAndTechCostThenFreezeSucceeds()
        {
            var db = Db();
            var report = ModPatchApplier.Apply(db, Pack(
                "{\"formatVersion\":1,\"buildings\":{\"House\":{\"buildWork\":25,\"cost\":{\"Wood\":8,\"Stone\":2}}},\"techs\":{\"stub.tech\":{\"cost\":75}}}"),
                new ModApplyOptions());
            Assert.IsTrue(report.Applied, report.ToString());
            Assert.AreEqual(0, report.Errors.Count);
            Assert.AreEqual(3, report.Changes.Count);
            var house = db.Building(BuildingType.House);
            Assert.AreEqual(25f, house.BuildWork);
            Assert.AreEqual(2, house.Cost.Length);
            Assert.AreEqual(ResourceType.Wood, house.Cost[0].Type);
            Assert.AreEqual(8f, house.Cost[0].Amount);
            Assert.AreEqual(75f, db.TechByKey("stub.tech").Cost);
            Assert.AreEqual(3, house.HousingMin);
            Assert.AreEqual(5, house.HousingMax);
            Assert.DoesNotThrow(() => db.Freeze());
            Assert.IsTrue(db.IsFrozen);
        }

        [Test]
        public void ReportListsOldAndNewValuesWithPositions()
        {
            var db = Db();
            var r = ModPatchApplier.Apply(db, Pack("{\n\"buildings\": {\"House\": {\"buildWork\": 30}}}"), null);
            Assert.IsTrue(r.Applied);
            var c = r.Changes[0];
            Assert.AreEqual("buildings.House.buildWork", c.Path);
            Assert.AreEqual("10", c.OldValue);
            Assert.AreEqual("30", c.NewValue);
            Assert.AreEqual(2, c.Line);
            Assert.IsTrue(Has(c.Source, "patch.json"));
        }

        [Test]
        public void PatchesRaceAgeAndGodPowerWhitelistedFields()
        {
            var db = Db();
            var r = ModPatchApplier.Apply(db, Pack(
                "{\"races\":{\"Elf\":{\"description\":\"Old folk\",\"lifeExpectancy\":400,\"fertilityPerYear\":0.04,\"researchRate\":1.4," +
                "\"productionMultiplier\":{\"Wood\":1.5},\"aggression\":0.1,\"bannerColor\":\"#33CC66\",\"wallColor\":\"#11223344\"," +
                "\"names\":{\"personPrefixes\":[\"Ae\",\"Ela\"],\"civilizationPatterns\":[\"The {0} Court\"]}}}," +
                "\"ages\":{\"Bronze\":{\"researchThreshold\":1500,\"minPopulation\":30}}," +
                "\"godPowers\":{\"Lightning\":{\"manaCost\":20,\"cooldownTicks\":48,\"maxRadius\":12,\"description\":\"Zap\"}}}"), null);
            Assert.IsTrue(r.Applied, r.ToString());
            var elf = db.Race(RaceId.Elf);
            Assert.AreEqual(400f, elf.LifeExpectancy);
            Assert.AreEqual(1.4f, elf.ResearchRate);
            Assert.AreEqual(1.5f, elf.ProductionMultiplier[(int)ResourceType.Wood]);
            Assert.AreEqual(1f, elf.ProductionMultiplier[(int)ResourceType.Stone]);
            Assert.AreEqual(1f, db.Race(RaceId.Human).ProductionMultiplier[(int)ResourceType.Wood]); // no aliasing across races
            Assert.AreEqual(0x33CC66FFu, elf.BannerColor);
            Assert.AreEqual(0x11223344u, elf.Architecture.WallColor);
            Assert.AreEqual(new[] { "Ae", "Ela" }, elf.Names.PersonPrefixes);
            Assert.AreEqual("The {0} Court", elf.Names.CivilizationPatterns[0]);
            Assert.AreEqual(1500f, db.Age(AgeId.Bronze).ResearchThreshold);
            Assert.AreEqual(12, db.GodPower(GodPowerType.Lightning).MaxRadius);
            Assert.DoesNotThrow(() => db.Freeze());
        }

        [Test]
        public void DoesNotMutateSharedStaticArrays()
        {
            var db = Db();
            var before = ResourceAmount.None.Length;
            var r = ModPatchApplier.Apply(db, Pack("{\"buildings\":{\"Well\":{\"cost\":{\"Stone\":3}}}}"), null);
            Assert.IsTrue(r.Applied);
            Assert.AreEqual(before, ResourceAmount.None.Length);
            Assert.AreEqual(0, db.Building(BuildingType.Storehouse).Cost.Length);
        }

        // ------------------------------------------------------------------ transactional rejection
        [TestCase("{\"buildings\":{\"House\":{\"buildWork\":1e9}}}", "outside the allowed range", TestName = "BuildWorkTooHigh")]
        [TestCase("{\"buildings\":{\"House\":{\"buildWork\":0}}}", "outside the allowed range", TestName = "BuildWorkZero")]
        [TestCase("{\"buildings\":{\"House\":{\"cost\":{\"Wood\":-1}}}}", "outside the allowed range", TestName = "NegativeCost")]
        [TestCase("{\"buildings\":{\"House\":{\"cost\":{\"Wood\":100001}}}}", "outside the allowed range", TestName = "CostAboveCap")]
        [TestCase("{\"buildings\":{\"House\":{\"jobSlots\":2.5}}}", "whole number", TestName = "FractionalInt")]
        [TestCase("{\"buildings\":{\"House\":{\"jobSlots\":1e12}}}", "outside the allowed range", TestName = "IntOverflow")]
        [TestCase("{\"buildings\":{\"House\":{\"outputsPerWorker\":{\"Food\":11}}}}", "outside the allowed range", TestName = "RateAboveCap")]
        [TestCase("{\"buildings\":{\"House\":{\"desirability\":2}}}", "outside the allowed range", TestName = "DesirabilityAboveOne")]
        [TestCase("{\"buildings\":{\"House\":{\"buildWork\":\"25\"}}}", "Expected a number", TestName = "WrongType")]
        [TestCase("{\"buildings\":{\"House\":{\"cost\":{\"Mana\":1}}}}", "Unknown resource", TestName = "UnknownResource")]
        [TestCase("{\"buildings\":{\"Houze\":{\"buildWork\":5}}}", "Unknown building", TestName = "UnknownBuilding")]
        [TestCase("{\"buildings\":{\"1\":{\"buildWork\":5}}}", "Unknown building", TestName = "NumericEnumName")]
        [TestCase("{\"buildings\":{\"House, Well\":{\"buildWork\":5}}}", "Unknown building", TestName = "FlagsEnumName")]
        [TestCase("{\"races\":{\"Elf\":{\"lifeExpectancy\":2001}}}", "outside the allowed range", TestName = "LifeAboveCap")]
        [TestCase("{\"races\":{\"Elf\":{\"lifeExpectancy\":10}}}", "adulthood", TestName = "LifeBelowAdulthood")]
        [TestCase("{\"races\":{\"Elf\":{\"aggression\":1.5}}}", "outside the allowed range", TestName = "PersonalityAboveOne")]
        [TestCase("{\"races\":{\"Elf\":{\"bannerColor\":\"red\"}}}", "color", TestName = "BadColor")]
        [TestCase("{\"races\":{\"Elf\":{\"names\":{\"civilizationPatterns\":[\"{1} Realm\"]}}}}", "exactly one {0}", TestName = "FormatInjectionPattern")]
        [TestCase("{\"races\":{\"Elf\":{\"names\":{\"civilizationPatterns\":[\"No placeholder\"]}}}}", "exactly one {0}", TestName = "PatternWithoutPlaceholder")]
        [TestCase("{\"races\":{\"Elf\":{\"names\":{\"personPrefixes\":[]}}}}", "non-empty", TestName = "EmptyNameList")]
        [TestCase("{\"races\":{\"Elf\":{\"names\":{\"personPrefixes\":[\"123\"]}}}}", "empty after sanitization", TestName = "SyllableWithoutLetters")]
        [TestCase("{\"ages\":{\"Iron\":{\"researchThreshold\":500}}}", "non-decreasing", TestName = "AgeThresholdOrder")]
        [TestCase("{\"godPowers\":{\"Meteor\":{\"maxRadius\":33}}}", "outside the allowed range", TestName = "RadiusAboveCap")]
        [TestCase("{\"godPowers\":{\"Meteor\":{\"cooldownTicks\":-1}}}", "outside the allowed range", TestName = "NegativeCooldown")]
        [TestCase("{\"techs\":{\"stub.tech\":{\"cost\":0}}}", "outside the allowed range", TestName = "TechCostZero")]
        [TestCase("{\"techs\":{\"nope.tech\":{\"cost\":10}}}", "Unknown tech", TestName = "UnknownTech")]
        [TestCase("{\"buildings\":{\"Smelter\":{\"jobSlots\":0}}}", "jobSlots must be at least 1", TestName = "JobWithoutSlots")]
        [TestCase("{\"buildings\":{\"House\":{\"name\":\"" + "\\u0007\\u202e" + "\"}}}", "empty after sanitization", TestName = "NameOnlyControlChars")]
        [TestCase("{\"buildings\":[1,2]}", "Section must be an object", TestName = "SectionNotObject")]
        [TestCase("{\"formatVersion\":2}", "formatVersion", TestName = "WrongFormatVersion")]
        public void InvalidPatchIsRejectedAndDatabaseUnchanged(string json, string expected)
        {
            var db = Db();
            string before = Snapshot(db);
            // a valid change in the same pack must NOT be applied either (all-or-nothing)
            string combined = json.Substring(0, json.Length - 1) + ",\"techs_valid_marker\":0}";
            var r1 = ModPatchApplier.Apply(db, Pack(json), new ModApplyOptions());
            Assert.IsFalse(r1.Applied);
            Assert.IsTrue(AnyError(r1, expected), "Expected error containing '" + expected + "' but got:\n" + r1);
            Assert.AreEqual(0, r1.Changes.Count);
            Assert.AreEqual(before, Snapshot(db));
            var r2 = ModPatchApplier.Apply(db, Pack(combined), new ModApplyOptions { StrictUnknownFields = false });
            Assert.IsFalse(r2.Applied);
            Assert.AreEqual(before, Snapshot(db));
            Assert.DoesNotThrow(() => db.Freeze());
        }

        [Test]
        public void OneBadValueRollsBackEveryOtherChangeInThePack()
        {
            var db = Db();
            string before = Snapshot(db);
            var pack = ModPack.FromJson(Manifest,
                new KeyValuePair<string, string>("a.json", "{\"buildings\":{\"House\":{\"buildWork\":20,\"name\":\"Hut\"}},\"techs\":{\"stub.tech\":{\"cost\":60}}}"),
                new KeyValuePair<string, string>("b.json", "{\"newTechs\":[{\"key\":\"test_mod.ok\",\"name\":\"Ok\",\"age\":\"Dawn\",\"cost\":10}],\"godPowers\":{\"Bless\":{\"manaCost\":-5}}}"));
            var r = ModPatchApplier.Apply(db, pack, null);
            Assert.IsFalse(r.Applied);
            Assert.AreEqual(1, r.Errors.Count, r.ToString());
            Assert.IsTrue(Has(r.Errors[0].Source, "b.json"));
            Assert.AreEqual(before, Snapshot(db));
            Assert.IsNull(db.TechByKey("test_mod.ok"));
            Assert.AreEqual(0, r.AddedTechs.Count);
        }

        // ------------------------------------------------------------------ whitelist
        [TestCase("housingMin")]
        [TestCase("housingMax")]
        [TestCase("HousingMin")]
        [TestCase("width")]
        [TestCase("minAge")]
        [TestCase("allowedRaces")]
        [TestCase("job")]
        public void ProtectedBuildingFieldsAreAlwaysRejected(string field)
        {
            var db = Db();
            string before = Snapshot(db);
            foreach (bool strict in new[] { true, false })
            {
                var r = ModPatchApplier.Apply(db, Pack("{\"buildings\":{\"House\":{\"" + field + "\":1}}}"), new ModApplyOptions { StrictUnknownFields = strict });
                Assert.IsFalse(r.Applied);
                Assert.IsTrue(AnyError(r, "protected"), r.ToString());
            }
            Assert.AreEqual(before, Snapshot(db));
            Assert.AreEqual(3, db.Building(BuildingType.House).HousingMin);
            Assert.AreEqual(5, db.Building(BuildingType.House).HousingMax);
        }

        [Test]
        public void ProtectedFieldsOfOtherTypesRejected()
        {
            var db = Db();
            Assert.IsTrue(AnyError(ModPatchApplier.Apply(db, Pack("{\"races\":{\"Orc\":{\"adulthoodAge\":1}}}"), null), "protected"));
            Assert.IsTrue(AnyError(ModPatchApplier.Apply(db, Pack("{\"races\":{\"Orc\":{\"name\":\"Goblin\"}}}"), null), "protected"));
            Assert.IsTrue(AnyError(ModPatchApplier.Apply(db, Pack("{\"techs\":{\"stub.tech\":{\"prerequisites\":[]}}}"), null), "protected"));
            Assert.IsTrue(AnyError(ModPatchApplier.Apply(db, Pack("{\"ages\":{\"Iron\":{\"requiredTechsFromPreviousAge\":0}}}"), null), "protected"));
            Assert.IsTrue(AnyError(ModPatchApplier.Apply(db, Pack("{\"godPowers\":{\"Heal\":{\"minRadius\":0}}}"), null), "protected"));
        }

        [Test]
        public void UnknownFieldsAreErrorsWhenStrictAndWarningsOtherwise()
        {
            var db = Db();
            string json = "{\"buildings\":{\"House\":{\"BuildWork\":20,\"buildWork\":21}},\"weather\":{}}";
            var strict = ModPatchApplier.Apply(db, Pack(json), new ModApplyOptions { StrictUnknownFields = true });
            Assert.IsFalse(strict.Applied);
            Assert.IsTrue(AnyError(strict, "Did you mean 'buildWork'"), strict.ToString());
            Assert.IsTrue(AnyError(strict, "weather"));
            Assert.AreEqual(10f, db.Building(BuildingType.House).BuildWork);

            var lax = ModPatchApplier.Apply(db, Pack(json), new ModApplyOptions { StrictUnknownFields = false });
            Assert.IsTrue(lax.Applied, lax.ToString());
            Assert.IsTrue(AnyWarning(lax, "BuildWork"));
            Assert.AreEqual(21f, db.Building(BuildingType.House).BuildWork);
        }

        [Test]
        public void FrozenDatabaseThrows()
        {
            var db = Db();
            db.Freeze();
            Assert.Throws<InvalidOperationException>(() => ModPatchApplier.Apply(db, Pack("{\"buildings\":{\"House\":{\"buildWork\":20}}}"), null));
            Assert.Throws<InvalidOperationException>(() => ModPatchApplier.ApplyAll(db, new List<ModPack>(), null));
        }

        [Test]
        public void GameVersionMismatchRejected()
        {
            var db = Db();
            var m = "{\"id\":\"future\",\"name\":\"Future\",\"version\":\"1.0.0\",\"gameVersionRange\":\">=2.0.0\"}";
            var r = ModPatchApplier.Apply(db, Pack("{\"buildings\":{\"House\":{\"buildWork\":20}}}", m), null);
            Assert.IsFalse(r.Applied);
            Assert.IsTrue(AnyError(r, "game version"));
        }

        // ------------------------------------------------------------------ sanitization
        [Test]
        public void StringsAreSanitizedAgainstInjection()
        {
            var db = Db();
            string evil = J("Evil`u202e`u0000<color=red>`u200bHut</color>`n`tX");
            var r = ModPatchApplier.Apply(db, Pack("{\"buildings\":{\"House\":{\"name\":\"" + evil + "\",\"description\":\"" + new string('d', 3000) + "\"}}," +
                "\"races\":{\"Elf\":{\"names\":{\"personPrefixes\":[\"Ae{0}\",\"" + J("El`u202ea") + "\",\"<b>Ka</b>\"],\"familyNames\":[\"Moon Song\"]}}}}"), null);
            Assert.IsTrue(r.Applied, r.ToString());
            string name = db.Building(BuildingType.House).Name;
            Assert.AreEqual("Evilcolor=redHut/color X", name);
            Assert.AreEqual(ModHardCaps.MaxDescriptionLength, db.Building(BuildingType.House).Description.Length);
            Assert.AreEqual(new[] { "Ae", "Ela", "bKab" }, db.Race(RaceId.Elf).Names.PersonPrefixes);
            Assert.AreEqual("Moon Song", db.Race(RaceId.Elf).Names.FamilyNames[0]);
            Assert.IsTrue(AnyWarning(r, "sanitized"));
            foreach (char c in name) Assert.IsTrue(c >= 0x20 && c != 0x202E && c != 0x200B && c != '<' && c != '>');
        }

        [Test]
        public void SanitizerHandlesEdgeCases()
        {
            Assert.AreEqual("", ModText.Sanitize(null, 10));
            Assert.AreEqual("a b", ModText.Sanitize("  a \t\n b  ", 10));
            Assert.AreEqual("a\n\nb", ModText.Sanitize("a\n\n\n\nb", 10, true));
            Assert.AreEqual("ab", ModText.Sanitize("a" + C(0xD800) + "b" + C(0xDC00), 10));
            // never splits a surrogate pair when truncating
            string s = ModText.Sanitize("ab" + C(0xD83D) + C(0xDE00), 3);
            Assert.AreEqual("ab", s);
            Assert.IsTrue(ModText.IsValidCivilizationPattern("{0} Empire"));
            Assert.IsFalse(ModText.IsValidCivilizationPattern("{0} {0}"));
            Assert.IsFalse(ModText.IsValidCivilizationPattern("{0:N2}"));
            Assert.IsFalse(ModText.IsValidCivilizationPattern("{0}}"));
            Assert.IsFalse(ModText.IsValidCivilizationPattern("{{0}"));
            Assert.IsFalse(ModText.IsValidCivilizationPattern("{0,999999999}"));
            // Zalgo: at most two combining marks per base character
            string zalgo = "Z" + new string((char)0x0301, 50) + "a" + new string((char)0x0300, 3);
            Assert.AreEqual(6, ModText.SanitizeSyllable(zalgo, 100).Length);
            Assert.AreEqual(6, ModText.Sanitize(zalgo, 100).Length);
        }

        [Test]
        public void TruncationNeverLeavesTrailingWhitespaceAndDeviceNamesDetected()
        {
            Assert.AreEqual("ab", ModText.Sanitize("ab cd", 3));
            Assert.AreEqual("ab", ModText.Sanitize("ab\n\ncd", 4, true));
            Assert.IsTrue(ModText.IsReservedDeviceName("CON.json"));
            Assert.IsTrue(ModText.IsReservedDeviceName("com1"));
            Assert.IsFalse(ModText.IsReservedDeviceName("console.json"));
            Assert.IsFalse(ModManifest.IsValidDataFileName("nul.json"));
            Assert.IsFalse(ModLoader.IsValidFolderName("LPT3"));
            Assert.IsFalse(ModLoader.IsValidFolderName("trailing."));
        }

        [Test]
        public void DiagnosticsAreCappedForHostilePacks()
        {
            var sb = new System.Text.StringBuilder("{\"buildings\":{");
            for (int i = 0; i < 900; i++) { if (i > 0) sb.Append(','); sb.Append("\"Fake").Append(i).Append("\":{}"); }
            sb.Append("}}");
            var r = ModPatchApplier.Apply(Db(), Pack(sb.ToString()), null);
            Assert.IsFalse(r.Applied);
            Assert.AreEqual(ModApplyReport.MaxDiagnostics + 1, r.Errors.Count);
        }

        [Test]
        public void ManyDocumentsPatchingTheSameFieldStayFast()
        {
            var docs = new List<KeyValuePair<string, string>>();
            for (int i = 0; i < 3000; i++) docs.Add(new KeyValuePair<string, string>("d" + i + ".json", "{\"buildings\":{\"House\":{\"buildWork\":" + (i + 1) + "}}}"));
            var pack = ModPack.FromJson(Manifest, docs.ToArray());
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var db = Db();
            var r = ModPatchApplier.Apply(db, pack, null);
            Assert.IsTrue(r.Applied);
            Assert.AreEqual(1, r.Changes.Count);
            Assert.AreEqual(3000f, db.Building(BuildingType.House).BuildWork);
            Assert.IsTrue(sw.ElapsedMilliseconds < 3000, "took " + sw.ElapsedMilliseconds + " ms");
        }

        // ------------------------------------------------------------------ new techs
        [Test]
        public void NewTechsAddedWithPrefixedKeysAndValidPrerequisites()
        {
            var db = Db();
            var r = ModPatchApplier.Apply(db, Pack(
                "{\"newTechs\":[" +
                "{\"key\":\"test_mod.alchemy\",\"name\":\"Alchemy\",\"age\":\"Bronze\",\"cost\":300,\"prerequisites\":[\"bronze.casting\",\"stub.tech\"]," +
                " \"effects\":[{\"kind\":\"ProductionMultiplier\",\"resource\":\"Gold\",\"value\":0.1},{\"kind\":\"UnlockBuilding\",\"building\":\"Library\"}]}," +
                "{\"key\":\"test_mod.transmutation\",\"name\":\"Transmutation\",\"age\":\"Iron\",\"cost\":500,\"prerequisites\":[\"test_mod.alchemy\"],\"allowedRaces\":[\"Dwarf\",\"Elf\"]," +
                " \"effects\":[{\"kind\":\"ServiceMultiplier\",\"service\":\"Research\",\"value\":0.25},{\"kind\":\"LifespanBonus\",\"value\":5}]}" +
                "]}"), null);
            Assert.IsTrue(r.Applied, r.ToString());
            Assert.AreEqual(new List<string> { "test_mod.alchemy", "test_mod.transmutation" }, r.AddedTechs);
            var alchemy = db.TechByKey("test_mod.alchemy");
            Assert.IsNotNull(alchemy);
            Assert.AreEqual(AgeId.Bronze, alchemy.Age);
            Assert.AreEqual(2, alchemy.Effects.Length);
            Assert.AreEqual(BuildingType.Library, alchemy.Effects[1].Building);
            var trans = db.TechByKey("test_mod.transmutation");
            Assert.AreEqual(RaceMask.Dwarf | RaceMask.Elf, trans.AllowedRaces);
            db.Freeze();
            Assert.AreEqual(new[] { alchemy.Id }, trans.Prerequisites);
            Assert.AreEqual(2, alchemy.Prerequisites.Length);
        }

        [TestCase("{\"newTechs\":[{\"key\":\"alchemy\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5}]}", "must be \"test_mod.", TestName = "UnprefixedKey")]
        [TestCase("{\"newTechs\":[{\"key\":\"other_mod.alchemy\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5}]}", "must be \"test_mod.", TestName = "ForeignPrefix")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.a.b\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5}]}", "must be \"test_mod.", TestName = "NestedDots")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.A\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5}]}", "must be \"test_mod.", TestName = "UppercaseSuffix")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5},{\"key\":\"test_mod.x\",\"name\":\"B\",\"age\":\"Dawn\",\"cost\":5}]}", "already exists", TestName = "DuplicateNewKey")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5,\"prerequisites\":[\"missing.tech\"]}]}", "Unknown prerequisite", TestName = "UnknownPrereq")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5,\"prerequisites\":[\"bronze.casting\"]}]}", "later age", TestName = "LaterAgePrereq")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5,\"prerequisites\":[\"test_mod.y\"]},{\"key\":\"test_mod.y\",\"name\":\"B\",\"age\":\"Dawn\",\"cost\":5,\"prerequisites\":[\"test_mod.x\"]}]}", "Unknown prerequisite", TestName = "ForwardReferenceCycle")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5,\"prerequisites\":[\"test_mod.x\"]}]}", "Unknown prerequisite", TestName = "SelfPrerequisite")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Dawn\"}]}", "requires \"cost\"", TestName = "MissingCost")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Future\",\"cost\":5}]}", "age name", TestName = "UnknownAge")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5,\"effects\":[{\"kind\":\"ProductionMultiplier\",\"resource\":\"Food\",\"value\":50}]}]}", "outside the allowed range", TestName = "EffectValueTooBig")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5,\"effects\":[{\"kind\":\"Teleport\",\"value\":1}]}]}", "valid \"kind\"", TestName = "UnknownEffectKind")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5,\"effects\":[{\"kind\":\"ProductionMultiplier\",\"value\":0.1}]}]}", "requires \"resource\"", TestName = "EffectMissingResource")]
        [TestCase("{\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5,\"allowedRaces\":[]}]}", "non-empty", TestName = "EmptyRaces")]
        [TestCase("{\"newTechs\":[{\"key\":\"stub.tech\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5}]}", "must be \"test_mod.", TestName = "OverrideExistingTech")]
        [TestCase("{\"techs\":{\"test_mod.x\":{\"cost\":5}},\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5}]}", "Unknown tech", TestName = "PatchOwnNewTechInTechs")]
        public void InvalidNewTechsRejected(string json, string expected)
        {
            var db = Db();
            int techs = db.Techs.Count;
            var r = ModPatchApplier.Apply(db, Pack(json), null);
            Assert.IsFalse(r.Applied);
            Assert.IsTrue(AnyError(r, expected), "Expected '" + expected + "' in:\n" + r);
            Assert.AreEqual(techs, db.Techs.Count);
            Assert.DoesNotThrow(() => db.Freeze());
        }

        [Test]
        public void NewTechsCanBeDisabledByOptions()
        {
            var db = Db();
            var r = ModPatchApplier.Apply(db, Pack("{\"newTechs\":[{\"key\":\"test_mod.x\",\"name\":\"A\",\"age\":\"Dawn\",\"cost\":5}]}"), new ModApplyOptions { AllowNewTechs = false });
            Assert.IsFalse(r.Applied);
            Assert.IsTrue(AnyError(r, "disabled"));
        }

        // ------------------------------------------------------------------ multiple packs
        [Test]
        public void ApplyAllLaterModWinsWithConflictWarning()
        {
            var db = Db();
            var a = ModPack.FromJson("{\"id\":\"mod_a\",\"name\":\"A\",\"version\":\"1.0.0\"}", new KeyValuePair<string, string>("p.json", "{\"buildings\":{\"House\":{\"buildWork\":20}}}"));
            var b = ModPack.FromJson("{\"id\":\"mod_b\",\"name\":\"B\",\"version\":\"1.0.0\",\"dependencies\":[\"mod_a\"]}", new KeyValuePair<string, string>("p.json", "{\"buildings\":{\"House\":{\"buildWork\":30}}}"));
            var reports = ModPatchApplier.ApplyAll(db, new List<ModPack> { a, b }, null);
            Assert.IsTrue(reports[0].Applied && reports[1].Applied);
            Assert.AreEqual(30f, db.Building(BuildingType.House).BuildWork);
            Assert.IsTrue(AnyWarning(reports[1], "mod_a"));
        }

        [Test]
        public void ApplyAllSkipsPacksWhoseDependencyFailed()
        {
            var db = Db();
            var a = ModPack.FromJson("{\"id\":\"mod_a\",\"name\":\"A\",\"version\":\"1.0.0\"}", new KeyValuePair<string, string>("p.json", "{\"buildings\":{\"House\":{\"buildWork\":-1}}}"));
            var b = ModPack.FromJson("{\"id\":\"mod_b\",\"name\":\"B\",\"version\":\"1.0.0\",\"dependencies\":[{\"id\":\"mod_a\",\"version\":\"^1.0.0\"}]}", new KeyValuePair<string, string>("p.json", "{\"buildings\":{\"Well\":{\"buildWork\":30}}}"));
            var reports = ModPatchApplier.ApplyAll(db, new List<ModPack> { a, b }, null);
            Assert.IsFalse(reports[0].Applied);
            Assert.IsFalse(reports[1].Applied);
            Assert.IsTrue(AnyError(reports[1], "dependency"));
            Assert.AreEqual(10f, db.Building(BuildingType.Well).BuildWork);
        }

        [Test]
        public void SameFieldTwiceInOnePackWarnsAndLaterWins()
        {
            var db = Db();
            var pack = ModPack.FromJson(Manifest,
                new KeyValuePair<string, string>("a.json", "{\"buildings\":{\"House\":{\"buildWork\":20}}}"),
                new KeyValuePair<string, string>("b.json", "{\"buildings\":{\"House\":{\"buildWork\":40}}}"));
            var r = ModPatchApplier.Apply(db, pack, null);
            Assert.IsTrue(r.Applied);
            Assert.AreEqual(40f, db.Building(BuildingType.House).BuildWork);
            Assert.AreEqual(1, r.Changes.Count);
            Assert.IsTrue(AnyWarning(r, "more than once"));
        }

        [Test]
        public void CollectTargetPathsMatchesChangePaths()
        {
            var pack = Pack("{\"buildings\":{\"House\":{\"buildWork\":20}},\"races\":{\"Elf\":{\"names\":{\"personPrefixes\":[\"Ae\"]},\"productionMultiplier\":{\"Wood\":2}}}}");
            var paths = ModPatchApplier.CollectTargetPaths(pack);
            Assert.AreEqual(new List<string> { "buildings.House.buildWork", "races.Elf.names.personPrefixes", "races.Elf.productionMultiplier.Wood" }, paths);
            var r = ModPatchApplier.Apply(Db(), pack, null);
            var changePaths = new List<string>();
            foreach (var c in r.Changes) changePaths.Add(c.Path);
            Assert.AreEqual(paths, changePaths);
        }
    }
}
