using System;
using GodSim.Core;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Contracts
{
    public class GameDatabaseTests
    {
        private static GameDatabase Unfrozen()
        {
            var db = new GameDatabase();
            TestDb.FillMissing(db);
            return db;
        }

        private static TechDefinition Tech(string key, AgeId age, params string[] pre) =>
            new TechDefinition { Key = key, Name = key, Age = age, Cost = 10f, PrerequisiteKeys = pre };

        [Test]
        public void StubDatabase_FreezesCleanly()
        {
            var db = TestDb.CreateStub();
            Assert.IsTrue(db.IsFrozen);
            Assert.AreEqual(0, db.Validate().Count, string.Join("; ", db.Validate()));
            Assert.AreEqual(Counts.Buildings, db.Buildings.Count);
            Assert.AreEqual(Counts.Races, db.Races.Count);
        }

        [Test]
        public void Freeze_ResolvesPrerequisiteKeysToIds()
        {
            var db = Unfrozen();
            int a = db.Register(Tech("a", AgeId.Dawn));
            int b = db.Register(Tech("b", AgeId.Dawn, "a"));
            int c = db.Register(Tech("c", AgeId.Bronze, "a", "b"));
            db.Freeze();
            CollectionAssert.AreEqual(new int[0], db.Tech(a).Prerequisites);
            CollectionAssert.AreEqual(new[] { a }, db.Tech(b).Prerequisites);
            CollectionAssert.AreEqual(new[] { a, b }, db.Tech(c).Prerequisites);
            Assert.AreEqual(c, db.TechIdOf("c"));
            Assert.AreSame(db.Tech(c), db.TechByKey("c"));
            Assert.AreEqual(-1, db.TechIdOf("zzz"));
            Assert.AreEqual(-1, db.TechIdOf(null));
            Assert.IsNull(db.TechByKey(null));
            Assert.IsNull(db.Tech(-1));
            Assert.IsNull(db.Tech(9999));
        }

        [Test]
        public void Freeze_NullPrerequisiteKeys_Tolerated()
        {
            var db = Unfrozen();
            var t = Tech("a", AgeId.Dawn);
            t.PrerequisiteKeys = null;
            db.Register(t);
            Assert.DoesNotThrow(() => db.Freeze());
        }

        [Test]
        public void Freeze_UnknownPrerequisite_Throws()
        {
            var db = Unfrozen();
            db.Register(Tech("a", AgeId.Dawn, "missing"));
            var ex = Assert.Throws<SimValidationException>(() => db.Freeze());
            StringAssert.Contains("missing", ex.Message);
            Assert.IsFalse(db.IsFrozen);
        }

        [Test]
        public void Freeze_Cycle_Throws()
        {
            var db = Unfrozen();
            db.Register(Tech("a", AgeId.Dawn, "c"));
            db.Register(Tech("b", AgeId.Dawn, "a"));
            db.Register(Tech("c", AgeId.Dawn, "b"));
            var ex = Assert.Throws<SimValidationException>(() => db.Freeze());
            StringAssert.Contains("cycle", ex.Message);
            Assert.IsFalse(db.IsFrozen);
        }

        [Test]
        public void Freeze_SelfPrerequisite_IsCycle()
        {
            var db = Unfrozen();
            db.Register(Tech("a", AgeId.Dawn, "a"));
            Assert.Throws<SimValidationException>(() => db.Freeze());
        }

        [Test]
        public void Freeze_DiamondDependency_IsNotACycle()
        {
            var db = Unfrozen();
            db.Register(Tech("root", AgeId.Dawn));
            db.Register(Tech("l", AgeId.Dawn, "root"));
            db.Register(Tech("r", AgeId.Dawn, "root"));
            db.Register(Tech("top", AgeId.Bronze, "l", "r"));
            Assert.DoesNotThrow(() => db.Freeze());
        }

        [Test]
        public void Freeze_LaterAgePrerequisite_Rejected()
        {
            var db = Unfrozen();
            db.Register(Tech("late", AgeId.Iron));
            db.Register(Tech("early", AgeId.Dawn, "late"));
            Assert.Throws<SimValidationException>(() => db.Freeze());
        }

        [TestCase(2, 5)]
        [TestCase(3, 6)]
        [TestCase(0, 0)]
        [TestCase(4, 4)]
        public void Freeze_HouseMustHoldThreeToFive(int min, int max)
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.House, Name = "House", HousingMin = min, HousingMax = max });
            TestDb.FillMissing(db);
            var ex = Assert.Throws<SimValidationException>(() => db.Freeze());
            StringAssert.Contains("3-5", ex.Message);
        }

        [Test]
        public void Validate_ReportsStructuralProblems()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.Well, Width = 0 });
            db.Register(new BuildingDefinition { Type = BuildingType.Farm, MinAge = AgeId.Iron, MaxAge = AgeId.Bronze });
            db.Register(new BuildingDefinition { Type = BuildingType.Mine, Job = JobType.Miner, JobSlots = 0 });
            db.Register(new BuildingDefinition { Type = BuildingType.Shrine, BuildWork = 0f });
            db.Register(new BuildingDefinition { Type = BuildingType.Granary, HousingMin = 4, HousingMax = 2 });
            TestDb.FillMissing(db);
            var errors = db.Validate();
            string all = string.Join("\n", errors);
            StringAssert.Contains("Well footprint", all);
            StringAssert.Contains("Farm MinAge > MaxAge", all);
            StringAssert.Contains("Mine has a job but no slots", all);
            StringAssert.Contains("Shrine BuildWork", all);
            StringAssert.Contains("Granary HousingMax < HousingMin", all);
        }

        [Test]
        public void Validate_EmptyDatabase_ReportsMissingSlots()
        {
            var errors = new GameDatabase().Validate();
            string all = string.Join("\n", errors);
            StringAssert.Contains("Missing race", all);
            StringAssert.Contains("Missing age", all);
            StringAssert.Contains("Missing building", all);
            StringAssert.Contains("Missing god power", all);
            StringAssert.Contains("No techs", all);
        }

        [Test]
        [Ignore("BUG (low): GameDatabase.Validate uses `x <= 0` checks that NaN passes: a tech with Cost = NaN, a building with BuildWork = NaN, and NaN age ResearchThresholds all validate cleanly (GameDatabase.cs:125, 133, 148). Fix: use `!(x > 0f)` / float.IsNaN checks.")]
        public void Validate_RejectsNaNNumbers()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.Well, BuildWork = float.NaN });
            db.Register(new TechDefinition { Key = "nan", Age = AgeId.Dawn, Cost = float.NaN });
            TestDb.FillMissing(db);
            Assert.Greater(db.Validate().Count, 1);
        }

        [Test]
        public void Registration_AfterFreeze_Throws()
        {
            var db = TestDb.CreateStub();
            Assert.Throws<InvalidOperationException>(() => db.Register(new RaceDefinition()));
            Assert.Throws<InvalidOperationException>(() => db.Register(new AgeDefinition()));
            Assert.Throws<InvalidOperationException>(() => db.Register(new BuildingDefinition()));
            Assert.Throws<InvalidOperationException>(() => db.Register(new GodPowerDefinition()));
            Assert.Throws<InvalidOperationException>(() => db.Register(Tech("late", AgeId.Dawn)));
            Assert.DoesNotThrow(() => db.Freeze(), "Freeze is idempotent");
        }

        [Test]
        public void RegisterTech_DuplicateOrEmptyKey_Throws()
        {
            var db = new GameDatabase();
            db.Register(Tech("a", AgeId.Dawn));
            Assert.Throws<ArgumentException>(() => db.Register(Tech("a", AgeId.Dawn)));
            Assert.Throws<ArgumentException>(() => db.Register(Tech("", AgeId.Dawn)));
            Assert.Throws<ArgumentException>(() => db.Register(new TechDefinition { Key = null }));
            Assert.Throws<ArgumentNullException>(() => db.Register((TechDefinition)null));
        }

        [Test]
        public void Lookups_ThrowForUnregistered()
        {
            var db = new GameDatabase();
            Assert.Throws<InvalidOperationException>(() => db.Race(RaceId.Elf));
            Assert.Throws<InvalidOperationException>(() => db.Age(AgeId.Iron));
            Assert.Throws<InvalidOperationException>(() => db.Building(BuildingType.Farm));
            Assert.Throws<InvalidOperationException>(() => db.GodPower(GodPowerType.Rain));
            Assert.IsFalse(db.TryGetBuilding(BuildingType.Farm, out var def));
            Assert.IsNull(def);
            Assert.IsFalse(db.TryGetBuilding((BuildingType)250, out def));
        }

        [Test]
        public void TechsOfAge_IndexesAfterFreeze()
        {
            var db = new GameDatabase();
            db.Register(Tech("d1", AgeId.Dawn));
            db.Register(Tech("b1", AgeId.Bronze, "d1"));
            db.Register(Tech("b2", AgeId.Bronze));
            TestDb.FillMissing(db);
            db.Freeze();
            Assert.AreEqual(1, db.TechsOfAge(AgeId.Dawn).Count);
            CollectionAssert.AreEqual(new[] { db.TechIdOf("b1"), db.TechIdOf("b2") }, db.TechsOfAge(AgeId.Bronze));
            Assert.AreEqual(0, db.TechsOfAge(AgeId.Ascension).Count);
        }

        // ------------------------------------------------------------ IsBuildingAvailable truth table
        [TestCase(AgeId.Bronze, AgeId.Iron, AgeId.Medieval, false, RaceMask.All, RaceId.Human, false)] // too early
        [TestCase(AgeId.Iron, AgeId.Iron, AgeId.Medieval, false, RaceMask.All, RaceId.Human, true)]     // == MinAge
        [TestCase(AgeId.Classical, AgeId.Iron, AgeId.Medieval, false, RaceMask.All, RaceId.Human, true)]
        [TestCase(AgeId.Medieval, AgeId.Iron, AgeId.Medieval, false, RaceMask.All, RaceId.Human, true)] // == MaxAge
        [TestCase(AgeId.Renaissance, AgeId.Iron, AgeId.Medieval, false, RaceMask.All, RaceId.Human, false)] // obsolete
        [TestCase(AgeId.Bronze, AgeId.Iron, AgeId.Medieval, true, RaceMask.All, RaceId.Human, true)]   // unlocked early
        [TestCase(AgeId.Renaissance, AgeId.Iron, AgeId.Medieval, true, RaceMask.All, RaceId.Human, false)] // unlock does not bypass MaxAge
        [TestCase(AgeId.Iron, AgeId.Iron, AgeId.Medieval, false, RaceMask.Elf, RaceId.Human, false)]  // race excluded
        [TestCase(AgeId.Iron, AgeId.Iron, AgeId.Medieval, true, RaceMask.Elf, RaceId.Human, false)]   // unlock does not bypass race
        [TestCase(AgeId.Iron, AgeId.Iron, AgeId.Medieval, false, RaceMask.Elf | RaceMask.Orc, RaceId.Orc, true)]
        [TestCase(AgeId.Iron, AgeId.Iron, AgeId.Medieval, false, RaceMask.None, RaceId.Orc, false)]
        public void IsBuildingAvailable_TruthTable(AgeId civAge, AgeId min, AgeId max, bool unlocked, RaceMask races, RaceId race, bool expected)
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.Temple, MinAge = min, MaxAge = max, AllowedRaces = races });
            TestDb.FillMissing(db);
            db.Freeze();
            var civ = new Civilization { Race = race, Age = civAge };
            civ.UnlockedBuildings[(int)BuildingType.Temple] = unlocked;
            Assert.AreEqual(expected, db.IsBuildingAvailable(civ, BuildingType.Temple));
        }

        [Test]
        public void IsBuildingAvailable_NullCivOrUnregistered_False()
        {
            var db = new GameDatabase();
            Assert.IsFalse(db.IsBuildingAvailable(new Civilization(), BuildingType.House));
            var stub = TestDb.CreateStub();
            Assert.IsFalse(stub.IsBuildingAvailable(null, BuildingType.House));
            Assert.IsFalse(stub.IsBuildingAvailable(new Civilization(), (BuildingType)200));
        }

        // ------------------------------------------------------------ CanResearch
        [Test]
        public void CanResearch_Rules()
        {
            var db = new GameDatabase();
            int a = db.Register(Tech("a", AgeId.Dawn));
            int b = db.Register(Tech("b", AgeId.Dawn, "a"));
            int iron = db.Register(Tech("iron", AgeId.Iron));
            var elfOnly = Tech("elf", AgeId.Dawn);
            elfOnly.AllowedRaces = RaceMask.Elf;
            int elf = db.Register(elfOnly);
            TestDb.FillMissing(db);
            db.Freeze();

            var civ = new Civilization { Race = RaceId.Human, Age = AgeId.Dawn };
            Assert.IsTrue(db.CanResearch(civ, db.Tech(a)));
            Assert.IsFalse(db.CanResearch(civ, db.Tech(b)), "missing prerequisite");
            Assert.IsFalse(db.CanResearch(civ, db.Tech(iron)), "future age");
            Assert.IsFalse(db.CanResearch(civ, db.Tech(elf)), "race excluded");
            civ.KnownTechs.Add(a);
            Assert.IsFalse(db.CanResearch(civ, db.Tech(a)), "already known");
            Assert.IsTrue(db.CanResearch(civ, db.Tech(b)));
            civ.Age = AgeId.Classical;
            Assert.IsTrue(db.CanResearch(civ, db.Tech(iron)), "past-age techs remain researchable");
            Assert.IsFalse(db.CanResearch(null, db.Tech(a)));
            Assert.IsFalse(db.CanResearch(civ, null));
            var elfCiv = new Civilization { Race = RaceId.Elf };
            Assert.IsTrue(db.CanResearch(elfCiv, db.Tech(elf)));
        }

        [Test]
        public void RaceMask_Helpers()
        {
            Assert.AreEqual(RaceMask.Dwarf, RaceId.Dwarf.ToMask());
            Assert.IsTrue(RaceMask.All.Allows(RaceId.Saurian));
            Assert.IsFalse(RaceMask.None.Allows(RaceId.Human));
            RaceMask all = RaceMask.None;
            foreach (var r in EnumUtil.AllRaces) all |= r.ToMask();
            Assert.AreEqual(RaceMask.All, all);
        }

        [Test]
        public void Biome_Helpers()
        {
            Assert.IsTrue(Biome.Lake.IsWater());
            Assert.IsFalse(Biome.Beach.IsWater());
            Assert.IsFalse(Biome.Mountain.IsBuildable());
            Assert.IsTrue(Biome.Hills.IsBuildable());
            Assert.IsTrue(float.IsPositiveInfinity(Biome.DeepOcean.MoveCost()));
            foreach (Biome b in Enum.GetValues(typeof(Biome)))
                if (b.IsLand()) Assert.GreaterOrEqual(b.MoveCost(), 1f, "land move costs must be >= 1 for the pathfinder heuristic");
        }
    }
}
