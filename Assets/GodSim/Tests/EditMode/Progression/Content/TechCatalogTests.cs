using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Progression;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Progression.Content
{
    public class TechCatalogTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _db = new GameDatabase();
            AgeCatalog.Register(_db);
            TechCatalog.Register(_db);
            TestDb.FillMissing(_db);
            _db.Freeze();
        }

        private static AgeId IntendedMinAge(BuildingType b) => DesignEconomy.IntendedMinAge(b);
        private static RaceMask IntendedRaces(BuildingType b) => DesignEconomy.IntendedRaces(b);

        private static bool IsRacial(TechDefinition t) => t.AllowedRaces != RaceMask.All;

        // ------------------------------------------------------------------ structure & validation

        [Test]
        public void FreezeSucceedsAndValidationIsClean()
        {
            Assert.IsTrue(_db.IsFrozen);
            Assert.AreEqual(0, _db.Validate().Count, string.Join("\n", _db.Validate()));
            foreach (var t in _db.Techs)
                Assert.AreEqual(t.PrerequisiteKeys.Length, t.Prerequisites.Length, t.Key);
        }

        [Test]
        public void TechCountsPerAgeAndTotalAreInRange()
        {
            int total = _db.Techs.Count;
            Assert.That(total >= 80 && total <= 95, "total " + total);
            foreach (var age in EnumUtil.AllAges)
            {
                int n = _db.TechsOfAge(age).Count;
                Assert.That(n >= 8 && n <= 10, age + " has " + n);
                Assert.That(TechCatalog.GeneralTechCount(age) >= 8, age + " general");
            }
        }

        [Test]
        public void KeysAreUniqueWellFormedAndPrefixedByAge()
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in _db.Techs)
            {
                Assert.IsTrue(seen.Add(t.Key), "duplicate " + t.Key);
                string prefix = TechCatalog.AgePrefix(t.Age) + ".";
                Assert.IsTrue(t.Key.StartsWith(prefix, StringComparison.Ordinal), t.Key);
                string rest = t.Key.Substring(prefix.Length);
                Assert.IsTrue(rest.Length > 1 && rest[0] >= 'a' && rest[0] <= 'z', t.Key);
                foreach (char c in rest)
                    Assert.IsTrue((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_', t.Key);
                Assert.AreSame(t, _db.TechByKey(t.Key));
            }
        }

        [Test]
        public void NamesAndOneSentenceDescriptionsArePresent()
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in _db.Techs)
            {
                Assert.IsFalse(string.IsNullOrEmpty(t.Name), t.Key);
                Assert.IsTrue(names.Add(t.Name), "duplicate name " + t.Name);
                Assert.IsTrue(t.Description.Length > 20, t.Key);
                Assert.IsTrue(t.Description.EndsWith(".", StringComparison.Ordinal), t.Key);
                Assert.IsFalse(t.Description.Contains(". "), "single sentence: " + t.Key);
            }
        }

        [Test]
        public void PrerequisitesNeverPointToLaterAgesOrThemselves()
        {
            foreach (var t in _db.Techs)
                foreach (int p in t.Prerequisites)
                {
                    Assert.AreNotEqual(t.Id, p, t.Key);
                    Assert.IsTrue(_db.Tech(p).Age <= t.Age, t.Key + " -> " + _db.Tech(p).Key);
                }
        }

        [Test]
        public void DawnHasTwoToThreeRootsWithoutPrerequisites()
        {
            int roots = 0;
            foreach (var t in _db.Techs)
            {
                if (t.Age != AgeId.Dawn || t.Prerequisites.Length > 0) continue;
                Assert.IsFalse(IsRacial(t), "racial root " + t.Key);
                roots++;
            }
            Assert.That(roots >= 2 && roots <= 3, "dawn roots " + roots);
        }

        [Test]
        public void EveryLaterAgeOpensWithTwoToThreeRootsFromThePreviousAge()
        {
            for (int i = 1; i < Counts.Ages; i++)
            {
                var age = (AgeId)i;
                int roots = 0;
                foreach (var t in _db.Techs)
                {
                    if (t.Age != age || !TechTreeReport.IsRoot(_db, t)) continue;
                    roots++;
                    bool fromPrevious = false;
                    foreach (int p in t.Prerequisites) if (_db.Tech(p).Age == age - 1) fromPrevious = true;
                    Assert.IsTrue(fromPrevious, t.Key + " should depend on a " + (age - 1) + " tech");
                }
                Assert.That(roots >= 2 && roots <= 3, age + " roots " + roots);
            }
        }

        [Test]
        public void NonRootTechsHaveOneOrTwoPrerequisitesIncludingSameAge()
        {
            foreach (var t in _db.Techs)
            {
                bool dawnRoot = t.Age == AgeId.Dawn && t.Prerequisites.Length == 0;
                if (dawnRoot) continue;
                Assert.That(t.Prerequisites.Length >= 1 && t.Prerequisites.Length <= 2, t.Key);
                if (TechTreeReport.IsRoot(_db, t)) continue;
                bool sameAge = false;
                foreach (int p in t.Prerequisites) if (_db.Tech(p).Age == t.Age) sameAge = true;
                Assert.IsTrue(sameAge, t.Key);
            }
        }

        [Test]
        public void EveryTechIsReachableFromDawnRootsForEveryAllowedRace()
        {
            foreach (var race in EnumUtil.AllRaces)
            {
                var known = new bool[_db.Techs.Count];
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    foreach (var t in _db.Techs)
                    {
                        if (known[t.Id] || !t.AllowedRaces.Allows(race)) continue;
                        bool ok = true;
                        foreach (int p in t.Prerequisites) if (!known[p]) { ok = false; break; }
                        if (ok) { known[t.Id] = true; changed = true; }
                    }
                }
                foreach (var t in _db.Techs)
                    if (t.AllowedRaces.Allows(race)) Assert.IsTrue(known[t.Id], race + " cannot reach " + t.Key);
            }
        }

        [Test]
        public void GeneralTechsNeverDependOnRacialTechs()
        {
            foreach (var t in _db.Techs)
                foreach (int p in t.Prerequisites)
                {
                    var pre = _db.Tech(p);
                    if (!IsRacial(pre)) continue;
                    Assert.IsTrue(IsRacial(t) && (t.AllowedRaces & ~pre.AllowedRaces) == 0, t.Key + " depends on racial " + pre.Key);
                }
        }

        [Test]
        public void EachRaceHasExactlyTwoSingleRaceTechs()
        {
            var perRace = new int[Counts.Races];
            foreach (var t in _db.Techs)
            {
                if (!IsRacial(t)) continue;
                int mask = (int)t.AllowedRaces;
                Assert.IsTrue(mask != 0 && (mask & (mask - 1)) == 0, t.Key + " must be single-race");
                for (int r = 0; r < Counts.Races; r++) if (t.AllowedRaces.Allows((RaceId)r)) perRace[r]++;
            }
            for (int r = 0; r < Counts.Races; r++) Assert.AreEqual(2, perRace[r], ((RaceId)r).ToString());
        }

        [TestCase("dawn.sylvan_lore", false)]
        [TestCase("bronze.sylvan_lore", true)]
        public void RacialTechKeysExistAsDesigned(string key, bool exists)
        {
            Assert.AreEqual(exists, _db.TechByKey(key) != null);
        }

        [TestCase("bronze.sylvan_lore", RaceId.Elf)]
        [TestCase("classical.grove_wardens", RaceId.Elf)]
        [TestCase("dawn.deep_delving", RaceId.Dwarf)]
        [TestCase("medieval.runesmithing", RaceId.Dwarf)]
        [TestCase("dawn.warbands", RaceId.Orc)]
        [TestCase("iron.blood_rites", RaceId.Orc)]
        [TestCase("bronze.star_charts", RaceId.Saurian)]
        [TestCase("iron.clutch_rites", RaceId.Saurian)]
        [TestCase("classical.chivalric_orders", RaceId.Human)]
        [TestCase("medieval.great_guilds", RaceId.Human)]
        public void RacialTechsAreThemedToTheirRace(string key, RaceId race)
        {
            var t = _db.TechByKey(key);
            Assert.IsNotNull(t, key);
            Assert.AreEqual(race.ToMask(), t.AllowedRaces);
        }

        // ------------------------------------------------------------------ costs

        [Test]
        public void CostsArePositiveAndRiseWithEachAge()
        {
            var rows = TechTreeReport.Summarize(_db);
            float prevAverage = 0f, prevMin = 0f;
            foreach (var r in rows)
            {
                Assert.IsTrue(r.MinCost > 0f, r.Age + " min cost");
                float avg = r.TotalCost / r.TechCount;
                Assert.IsTrue(avg > prevAverage, r.Age + " average " + avg);
                Assert.IsTrue(r.MinCost > prevMin, r.Age + " min " + r.MinCost);
                prevAverage = avg;
                prevMin = r.MinCost;
            }
        }

        [Test]
        public void PerAgeTotalCostFitsTheResearchWindow()
        {
            for (int i = 0; i < Counts.Ages; i++)
            {
                var age = (AgeId)i;
                float window = TechTreeReport.ResearchWindow(_db, age);
                float total = TechTreeReport.TotalCost(_db, age);
                Assert.IsTrue(window > 0f, age + " window");
                Assert.IsTrue(total <= window * 0.8f, age + " cost " + total + " > 80% of " + window);
                Assert.IsTrue(total >= window * 0.4f, age + " cost " + total + " < 40% of " + window);
                if (i + 1 < Counts.Ages)
                    Assert.AreEqual(_db.Age((AgeId)(i + 1)).ResearchThreshold - _db.Age(age).ResearchThreshold, window, 1e-3f);
                else // the final age is held to the last real gap (Information -> Ascension), not an invented window
                    Assert.AreEqual(_db.Age(age).ResearchThreshold - _db.Age((AgeId)(i - 1)).ResearchThreshold, window, 1e-3f);
            }
            // Balance pass 2026-11-19: the Information -> Ascension gap went 14,000 -> 392,000 (x28).
            Assert.AreEqual(392000f, TechTreeReport.ResearchWindow(_db, AgeId.Ascension), 1e-3f);
        }

        // ------------------------------------------------------------------ effects

        [Test]
        public void EveryTechHasSaneEffects()
        {
            foreach (var t in _db.Techs)
            {
                Assert.IsTrue(t.Effects.Length >= 1 && t.Effects.Length <= 3, t.Key);
                foreach (var e in t.Effects)
                {
                    Assert.IsTrue(Enum.IsDefined(typeof(TechEffectKind), e.Kind), t.Key);
                    switch (e.Kind)
                    {
                        case TechEffectKind.UnlockBuilding:
                            Assert.IsTrue(Enum.IsDefined(typeof(BuildingType), e.Building), t.Key);
                            break;
                        case TechEffectKind.LifespanBonus:
                            Assert.IsTrue(e.Value > 0f && e.Value <= 10f, t.Key + " lifespan " + e.Value);
                            break;
                        case TechEffectKind.HealthBonus:
                        case TechEffectKind.HappinessBonus:
                            Assert.IsTrue(e.Value > 0f && e.Value <= 0.1f, t.Key + " bonus " + e.Value);
                            break;
                        default:
                            Assert.IsTrue(e.Value > 0f && e.Value <= 0.3f + 1e-6f, t.Key + " multiplier " + e.Value);
                            if (e.Kind == TechEffectKind.ProductionMultiplier) Assert.IsTrue(Enum.IsDefined(typeof(ResourceType), e.Resource), t.Key);
                            if (e.Kind == TechEffectKind.ServiceMultiplier) Assert.IsTrue(Enum.IsDefined(typeof(ServiceType), e.Service), t.Key);
                            break;
                    }
                    Assert.IsFalse(float.IsNaN(e.Value) || float.IsInfinity(e.Value), t.Key);
                }
            }
        }

        [Test]
        public void EveryEffectKindIsUsed()
        {
            var used = new bool[Enum.GetValues(typeof(TechEffectKind)).Length];
            foreach (var t in _db.Techs) foreach (var e in t.Effects) used[(int)e.Kind] = true;
            for (int k = 0; k < used.Length; k++) Assert.IsTrue(used[k], ((TechEffectKind)k).ToString());
        }

        [Test]
        public void UnlocksTargetBuildingsExactlyOneAgeEarlyAndForTheRightRace()
        {
            var unlocked = new HashSet<BuildingType>();
            foreach (var t in _db.Techs)
                foreach (var e in t.Effects)
                {
                    if (e.Kind != TechEffectKind.UnlockBuilding) continue;
                    Assert.IsTrue(Enum.IsDefined(typeof(BuildingType), e.Building), t.Key);
                    Assert.IsTrue(_db.TryGetBuilding(e.Building, out _), t.Key + " -> " + e.Building);
                    Assert.AreEqual(IntendedMinAge(e.Building), t.Age + 1, t.Key + " -> " + e.Building);
                    Assert.IsTrue(unlocked.Add(e.Building), "unlocked twice: " + e.Building);
                    var races = IntendedRaces(e.Building);
                    if (races != RaceMask.All) Assert.AreEqual(races, t.AllowedRaces, t.Key + " unlocks a racial building");
                }
            Assert.IsTrue(unlocked.Count >= 10, "unlocks " + unlocked.Count);
        }

        [TestCase("dawn.agriculture", BuildingType.Farm)]
        [TestCase("dawn.deep_delving", BuildingType.Mine)]
        [TestCase("bronze.iron_working", BuildingType.Blacksmith)]
        [TestCase("iron.philosophy", BuildingType.Library)]
        [TestCase("renaissance.steam_power", BuildingType.CoalMine)]
        [TestCase("modern.computing", BuildingType.ElectronicsPlant)]
        public void KeyTechsUnlockTheirBuilding(string key, BuildingType building)
        {
            var t = _db.TechByKey(key);
            Assert.IsNotNull(t, key);
            bool found = false;
            foreach (var e in t.Effects) if (e.Kind == TechEffectKind.UnlockBuilding && e.Building == building) found = true;
            Assert.IsTrue(found, key + " should unlock " + building);
        }

        // Unlocks removed in review because their target could not be paid for or run in the unlock age.
        [TestCase("dawn.deep_delving", BuildingType.DeepForge)]          // forge smelts Ore; no Ore before Bronze
        [TestCase("bronze.priesthood", BuildingType.Temple)]             // costs Tools; Blacksmith is not an ancestor
        [TestCase("renaissance.steam_power", BuildingType.Factory)]      // burns Fuel; no Fuel before the CoalMine
        [TestCase("modern.computing", BuildingType.DataCenter)]          // needs Electronics; no Electronics in Modern
        public void UnsuppliedUnlocksStayRemoved(string key, BuildingType building)
        {
            foreach (var e in _db.TechByKey(key).Effects)
                Assert.IsFalse(e.Kind == TechEffectKind.UnlockBuilding && e.Building == building, key + " must not unlock " + building);
        }

        // ------------------------------------------------------------------ unlock supply chains

        private static GameDatabase DesignEconomyDb(bool withCatalog)
        {
            var db = new GameDatabase();
            AgeCatalog.Register(db);
            if (withCatalog) TechCatalog.Register(db);
            DesignEconomy.Register(db);
            return db;
        }

        [TestCase(ResourceType.Food, AgeId.Dawn)]
        [TestCase(ResourceType.Wood, AgeId.Dawn)]
        [TestCase(ResourceType.Stone, AgeId.Dawn)]
        [TestCase(ResourceType.Ore, AgeId.Bronze)]
        [TestCase(ResourceType.Metal, AgeId.Bronze)]
        [TestCase(ResourceType.Cloth, AgeId.Bronze)]
        [TestCase(ResourceType.Gold, AgeId.Iron)]
        [TestCase(ResourceType.Tools, AgeId.Iron)]
        [TestCase(ResourceType.Weapons, AgeId.Iron)]
        [TestCase(ResourceType.Goods, AgeId.Classical)]
        [TestCase(ResourceType.Fuel, AgeId.Industrial)]
        [TestCase(ResourceType.Machinery, AgeId.Industrial)]
        [TestCase(ResourceType.Electronics, AgeId.Information)]
        public void ResourcesFirstAppearInTheAgeTheEnumCommentsState(ResourceType resource, AgeId expected)
        {
            // First age in which a building every race may build (by MinAge alone) outputs the resource.
            AgeId first = AgeId.Ascension;
            bool any = false;
            foreach (var b in EnumUtil.AllBuildings)
            {
                if (IntendedRaces(b) != RaceMask.All || Array.IndexOf(DesignEconomy.OutputsOf(b), resource) < 0) continue;
                if (!any || IntendedMinAge(b) < first) first = IntendedMinAge(b);
                any = true;
            }
            Assert.IsTrue(any, resource + " has no producer");
            Assert.AreEqual(expected, first, resource.ToString());
        }

        [Test]
        public void EveryEarlyUnlockCanBePaidForAndRunInItsAge()
        {
            var db = DesignEconomyDb(true);
            TestDb.FillMissing(db);
            db.Freeze();
            foreach (var t in db.Techs)
                foreach (var e in t.Effects)
                    if (e.Kind == TechEffectKind.UnlockBuilding)
                        Assert.IsTrue(DesignEconomy.Describes(e.Building), "add " + e.Building + " (its cost materials and inputs) to DesignEconomy before unlocking it early");
            var problems = TechTreeReport.FindUnsuppliedUnlocks(db);
            Assert.AreEqual(0, problems.Count, string.Join("\n", problems));
        }

        [Test]
        public void SupplyCheckFlagsUnlocksWhoseNeedsArriveLater()
        {
            var db = DesignEconomyDb(false);
            // The unlocks the catalog had before review: each needs a resource first produced an age later.
            AddTech(db, "renaissance.old_steam", AgeId.Renaissance, RaceMask.All, BuildingType.Factory);   // Fuel input
            AddTech(db, "modern.old_computing", AgeId.Modern, RaceMask.All, BuildingType.DataCenter);      // Electronics cost + input
            AddTech(db, "bronze.old_priesthood", AgeId.Bronze, RaceMask.All, BuildingType.Temple);         // Tools cost
            AddTech(db, "dawn.old_delving", AgeId.Dawn, RaceMask.Dwarf, BuildingType.DeepForge);           // Ore input
            AddTech(db, "iron.bad_target", AgeId.Iron, RaceMask.All, (BuildingType)250);                   // not a building
            // Supplied through an ancestor's unlock, and through a producer that only the tech's own race may build.
            AddTech(db, "renaissance.pumps", AgeId.Renaissance, RaceMask.All, BuildingType.CoalMine);
            AddTech(db, "renaissance.engines", AgeId.Renaissance, RaceMask.All, BuildingType.Factory, "renaissance.pumps");
            AddTech(db, "bronze.dwarf_shrine", AgeId.Bronze, RaceMask.Dwarf, BuildingType.Temple);         // Tools from DeepForge
            TestDb.FillMissing(db);

            var problems = TechTreeReport.FindUnsuppliedUnlocks(db);
            string all = string.Join("\n", problems);
            Assert.AreEqual(6, problems.Count, all);
            AssertProblems(problems, "renaissance.old_steam", 1, "Fuel (input)");
            AssertProblems(problems, "modern.old_computing", 2, "Electronics (cost)");
            AssertProblems(problems, "modern.old_computing", 2, "Electronics (input)");
            AssertProblems(problems, "bronze.old_priesthood", 1, "Tools (cost)");
            AssertProblems(problems, "dawn.old_delving", 1, "Ore (input)");
            AssertProblems(problems, "iron.bad_target", 1, "not registered");
            AssertProblems(problems, "renaissance.pumps", 0, "");
            AssertProblems(problems, "renaissance.engines", 0, "");
            AssertProblems(problems, "bronze.dwarf_shrine", 0, "");

            db.Freeze(); // the check reads prerequisite keys, so freezing must not change the answer
            Assert.AreEqual(all, string.Join("\n", TechTreeReport.FindUnsuppliedUnlocks(db)));
        }

        private static void AddTech(GameDatabase db, string key, AgeId age, RaceMask races, BuildingType unlock, params string[] prereqs)
        {
            db.Register(new TechDefinition
            {
                Key = key, Name = key, Age = age, Cost = 10f, AllowedRaces = races,
                PrerequisiteKeys = prereqs, Effects = new[] { TechEffect.Unlock(unlock) }
            });
        }

        private static void AssertProblems(List<string> problems, string techKey, int expected, string mention)
        {
            int n = 0;
            bool mentioned = false;
            foreach (var p in problems)
            {
                if (!p.StartsWith(techKey + " ", StringComparison.Ordinal)) continue;
                n++;
                if (p.Contains(mention)) mentioned = true;
            }
            Assert.AreEqual(expected, n, techKey + ": " + string.Join("\n", problems));
            if (expected > 0) Assert.IsTrue(mentioned, techKey + " should mention '" + mention + "'");
        }

        [Test]
        public void CapstoneIsTheCrownOfTheTree()
        {
            var cap = _db.TechByKey(TechCatalog.CapstoneKey);
            Assert.IsNotNull(cap);
            Assert.AreEqual(AgeId.Ascension, cap.Age);
            Assert.AreEqual(RaceMask.All, cap.AllowedRaces);
            foreach (var t in _db.Techs)
            {
                if (t != cap) Assert.IsTrue(t.Cost < cap.Cost, t.Key + " costs more than the capstone");
                foreach (int p in t.Prerequisites) Assert.AreNotEqual(cap.Id, p, "nothing may depend on the capstone");
            }
            // The capstone's ancestry spans every age.
            var ages = new bool[Counts.Ages];
            var stack = new Stack<int>();
            var seen = new HashSet<int>();
            stack.Push(cap.Id);
            while (stack.Count > 0)
            {
                var t = _db.Tech(stack.Pop());
                ages[(int)t.Age] = true;
                foreach (int p in t.Prerequisites) if (seen.Add(p)) stack.Push(p);
            }
            for (int i = 0; i < ages.Length; i++) Assert.IsTrue(ages[i], "capstone ancestry misses " + (AgeId)i);
        }

        // ------------------------------------------------------------------ play-through

        [Test]
        public void EveryRaceCanAdvanceAgeByAgeWithCheapestResearch()
        {
            // Model: in each age the civ researches the cheapest tech it may research (current or earlier age,
            // so skipped prerequisites get back-filled) until it knows enough techs of that age to advance.
            foreach (var race in EnumUtil.AllRaces)
            {
                var civ = new Civilization { Race = race, Age = AgeId.Dawn };
                for (int i = 0; i < Counts.Ages - 1; i++)
                {
                    var age = (AgeId)i;
                    int required = _db.Age(age + 1).RequiredTechsFromPreviousAge;
                    float spent = 0f;
                    while (CountKnown(civ, age) < required)
                    {
                        var next = CheapestResearchable(civ, age);
                        Assert.IsNotNull(next, race + " stuck in " + age);
                        civ.KnownTechs.Add(next.Id);
                        spent += next.Cost;
                    }
                    Assert.IsTrue(spent <= TechTreeReport.ResearchWindow(_db, age), race + " " + age + " requirement cost " + spent);
                    civ.Age = age + 1;
                }
                // At Ascension the civ can eventually learn everything its race allows.
                TechDefinition t;
                while ((t = AnyResearchable(civ)) != null) civ.KnownTechs.Add(t.Id);
                foreach (var def in _db.Techs)
                    Assert.AreEqual(def.AllowedRaces.Allows(race), civ.KnownTechs.Contains(def.Id), race + " " + def.Key);
                Assert.IsTrue(civ.KnownTechs.Contains(_db.TechIdOf(TechCatalog.CapstoneKey)));
            }
        }

        [Test]
        public void CanResearchRespectsRaceAgeAndPrerequisites()
        {
            var elf = new Civilization { Race = RaceId.Elf, Age = AgeId.Bronze };
            var sylvan = _db.TechByKey("bronze.sylvan_lore");
            var stars = _db.TechByKey("bronze.star_charts");
            Assert.IsFalse(_db.CanResearch(elf, sylvan), "needs writing first");
            foreach (var key in new[] { "dawn.foraging", "dawn.stone_tools", "dawn.agriculture", "dawn.fire", "dawn.animism", "dawn.oral_tradition", "bronze.pottery", "bronze.writing" })
                elf.KnownTechs.Add(_db.TechIdOf(key));
            Assert.IsTrue(_db.CanResearch(elf, sylvan));
            Assert.IsFalse(_db.CanResearch(elf, stars), "saurian-only tech");
            Assert.IsFalse(_db.CanResearch(elf, _db.TechByKey("iron.philosophy")), "later age");
            var fresh = new Civilization { Race = RaceId.Human, Age = AgeId.Dawn };
            Assert.IsTrue(_db.CanResearch(fresh, _db.TechByKey("dawn.fire")));
            Assert.IsFalse(_db.CanResearch(fresh, _db.TechByKey("dawn.agriculture")));
        }

        private int CountKnown(Civilization civ, AgeId age)
        {
            int n = 0;
            foreach (int id in civ.KnownTechs) if (_db.Tech(id).Age == age) n++;
            return n;
        }

        private TechDefinition CheapestResearchable(Civilization civ, AgeId age)
        {
            TechDefinition best = null;
            foreach (var t in _db.Techs)
                if (t.Age <= age && _db.CanResearch(civ, t) && (best == null || t.Cost < best.Cost)) best = t;
            return best;
        }

        private TechDefinition AnyResearchable(Civilization civ)
        {
            foreach (var t in _db.Techs) if (_db.CanResearch(civ, t)) return t;
            return null;
        }

        // ------------------------------------------------------------------ catalog mechanics & report

        [Test]
        public void CatalogIsDeterministicAndReturnsFreshInstances()
        {
            var a = TechCatalog.CreateDefinitions();
            var b = TechCatalog.CreateDefinitions();
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++)
            {
                Assert.AreEqual(a[i].Key, b[i].Key);
                Assert.AreEqual(a[i].Cost, b[i].Cost);
                Assert.IsFalse(ReferenceEquals(a[i], b[i]));
                Assert.IsFalse(ReferenceEquals(a[i].PrerequisiteKeys, b[i].PrerequisiteKeys));
                Assert.IsFalse(ReferenceEquals(a[i].Effects, b[i].Effects));
            }
            var other = new GameDatabase();
            TechCatalog.Register(other);
            Assert.IsFalse(ReferenceEquals(other.TechByKey("dawn.fire"), _db.TechByKey("dawn.fire")));
            Assert.AreEqual(_db.Techs.Count, other.Techs.Count);
        }

        // FNV-1a 64 of every tech key in registration order, each followed by '|'. Tech ids are registration
        // indices and saves store them, so changing this value is a save-format change: bump the save version
        // (or add a migration) first, then update the golden value.
        private const ulong RegistrationOrderHash = 0x8B8A2ABA8867D0E7UL;

        [Test]
        public void RegistrationOrderIsStableAndTopological()
        {
            var sb = new StringBuilder();
            foreach (var t in _db.Techs) sb.Append(t.Key).Append('|');
            Assert.AreEqual(RegistrationOrderHash, Hashing.Fnv1a64(sb.ToString()),
                "Tech registration order changed; this re-maps saved tech ids (see TechCatalog docs). Actual: 0x"
                + Hashing.Fnv1a64(sb.ToString()).ToString("X16"));
            Assert.AreEqual(0, _db.TechIdOf("dawn.fire"));
            Assert.AreEqual(_db.Techs.Count - 1, _db.TechIdOf(TechCatalog.CapstoneKey));
            var fresh = TechCatalog.CreateDefinitions();
            for (int i = 0; i < _db.Techs.Count; i++)
            {
                var t = _db.Techs[i];
                Assert.AreEqual(i, t.Id, t.Key);
                Assert.AreEqual(fresh[i].Key, t.Key, "CreateDefinitions order");
                if (i > 0) Assert.IsTrue(t.Age >= _db.Techs[i - 1].Age, t.Key + " is registered after a later-age tech");
                foreach (int p in t.Prerequisites) Assert.IsTrue(p < t.Id, t.Key + " is registered before its prerequisite " + _db.Tech(p).Key);
            }
        }

        [Test]
        public void RegisterRejectsDuplicatesNullAndFrozenDatabase()
        {
            var db = new GameDatabase();
            TechCatalog.Register(db);
            Assert.Throws<ArgumentException>(() => TechCatalog.Register(db));
            Assert.Throws<ArgumentNullException>(() => TechCatalog.Register(null));
            Assert.Throws<InvalidOperationException>(() => TechCatalog.Register(_db));
        }

        [Test]
        public void ReportMatchesDatabase()
        {
            var rows = TechTreeReport.Summarize(_db);
            Assert.AreEqual(Counts.Ages, rows.Length);
            int count = 0, racial = 0;
            float total = 0f;
            foreach (var r in rows)
            {
                // Recompute every aggregate independently from the frozen database (resolved prerequisite ids).
                int n = 0, general = 0, roots = 0;
                float sum = 0f, generalSum = 0f, min = float.MaxValue, max = 0f;
                foreach (int id in _db.TechsOfAge(r.Age))
                {
                    var t = _db.Tech(id);
                    n++;
                    sum += t.Cost;
                    min = Math.Min(min, t.Cost);
                    max = Math.Max(max, t.Cost);
                    if (IsRacial(t)) continue;
                    general++;
                    generalSum += t.Cost;
                    bool onlyEarlier = true;
                    foreach (int p in t.Prerequisites) if (_db.Tech(p).Age >= t.Age) onlyEarlier = false;
                    if (onlyEarlier) roots++;
                }
                Assert.AreEqual(r.Age, rows[(int)r.Age].Age);
                Assert.AreEqual(n, r.TechCount, r.Age + " count");
                Assert.AreEqual(TechTreeReport.CountTechs(_db, r.Age), r.TechCount);
                Assert.AreEqual(general, r.GeneralTechCount, r.Age + " general");
                Assert.AreEqual(n - general, r.RacialTechCount, r.Age + " racial");
                Assert.AreEqual(TechCatalog.GeneralTechCount(r.Age), r.GeneralTechCount);
                Assert.AreEqual(sum, r.TotalCost, 1e-3f, r.Age + " total");
                Assert.AreEqual(TechTreeReport.TotalCost(_db, r.Age), r.TotalCost, 1e-3f);
                Assert.AreEqual(generalSum, r.GeneralCost, 1e-3f, r.Age + " general cost");
                Assert.AreEqual(min, r.MinCost, r.Age + " min");
                Assert.AreEqual(max, r.MaxCost, r.Age + " max");
                Assert.AreEqual(roots, r.RootCount, r.Age + " roots");
                Assert.AreEqual(3, r.RootCount, r.Age + " roots");
                Assert.AreEqual(TechTreeReport.ResearchWindow(_db, r.Age), r.ResearchWindow, r.Age + " window");
                Assert.AreEqual(r.TotalCost / r.ResearchWindow, r.CostToWindowRatio, 1e-5f);
                count += r.TechCount;
                racial += r.RacialTechCount;
                total += r.TotalCost;
            }
            Assert.AreEqual(_db.Techs.Count, count);
            Assert.AreEqual(2 * Counts.Races, racial);
            Assert.AreEqual(TechTreeReport.GrandTotalCost(_db), total, 1e-2f);
            // Fixed spot checks against the shipped catalog. Listed costs x TechCatalog.CostScale (balance pass
            // 2026-11-19: Dawn x2, Ascension x28; was 14 / 30 / 2,400 / 14,000).
            Assert.AreEqual(28f, rows[(int)AgeId.Dawn].MinCost);
            Assert.AreEqual(60f, rows[(int)AgeId.Dawn].MaxCost);
            Assert.AreEqual(_db.TechByKey(TechCatalog.CapstoneKey).Cost, rows[(int)AgeId.Ascension].MaxCost);
            Assert.AreEqual(67200f, rows[(int)AgeId.Ascension].MaxCost);
            Assert.AreEqual(392000f, rows[(int)AgeId.Ascension].ResearchWindow, 1e-3f);

            var catalogRows = TechTreeReport.SummarizeCatalog();
            for (int i = 0; i < rows.Length; i++)
            {
                Assert.AreEqual(rows[i].TechCount, catalogRows[i].TechCount);
                Assert.AreEqual(rows[i].RootCount, catalogRows[i].RootCount);
            }

            string text = TechTreeReport.Format(_db);
            foreach (var age in EnumUtil.AllAges) Assert.IsTrue(text.Contains(_db.Age(age).Name), age.ToString());
            Assert.IsTrue(text.Contains("Total: " + _db.Techs.Count + " techs"), text);
        }

        [Test]
        public void ReportHandlesNullAndEmptyDatabases()
        {
            Assert.AreEqual(0, TechTreeReport.CountTechs(null, AgeId.Dawn));
            Assert.AreEqual(0f, TechTreeReport.ResearchWindow(null, AgeId.Dawn));
            Assert.AreEqual(0f, TechTreeReport.ResearchWindow(new GameDatabase(), AgeId.Iron));
            var rows = TechTreeReport.Summarize(new GameDatabase());
            foreach (var r in rows) { Assert.AreEqual(0, r.TechCount); Assert.AreEqual(0f, r.CostToWindowRatio); }
            Assert.IsFalse(string.IsNullOrEmpty(TechTreeReport.Format(null)));
            Assert.AreEqual(0, TechTreeReport.FindUnsuppliedUnlocks(null).Count);
            Assert.AreEqual(0, TechTreeReport.FindUnsuppliedUnlocks(new GameDatabase()).Count);
        }

        [Test]
        public void IsRootHandlesEdgeCases()
        {
            var db = new GameDatabase();
            var dawnA = Reg(db, "dawn.a", AgeId.Dawn, RaceMask.All);
            var dawnB = Reg(db, "dawn.b", AgeId.Dawn, RaceMask.All, "dawn.a");
            var racial = Reg(db, "bronze.racial", AgeId.Bronze, RaceMask.Elf, "dawn.a");
            var general = Reg(db, "bronze.general", AgeId.Bronze, RaceMask.All, "dawn.a");
            var sameAge = Reg(db, "bronze.same_age", AgeId.Bronze, RaceMask.All, "bronze.general");
            var mixed = Reg(db, "bronze.mixed", AgeId.Bronze, RaceMask.All, "dawn.b", "bronze.general");
            var unknown = Reg(db, "bronze.unknown", AgeId.Bronze, RaceMask.All, "nowhere.missing");
            var later = Reg(db, "bronze.later", AgeId.Bronze, RaceMask.All, "iron.x");
            Reg(db, "iron.x", AgeId.Iron, RaceMask.All);
            var noKeys = new TechDefinition { Key = "iron.no_keys", Age = AgeId.Iron, PrerequisiteKeys = null };

            Assert.IsTrue(TechTreeReport.IsRoot(db, dawnA), "Dawn tech without prerequisites");
            Assert.IsFalse(TechTreeReport.IsRoot(db, dawnB), "Dawn tech with a Dawn prerequisite");
            Assert.IsFalse(TechTreeReport.IsRoot(db, racial), "(a) racial tech with only a previous-age prerequisite");
            Assert.IsTrue(TechTreeReport.IsRoot(db, general), "(b) general tech with only a previous-age prerequisite");
            Assert.IsFalse(TechTreeReport.IsRoot(db, sameAge), "(c) same-age prerequisite");
            Assert.IsFalse(TechTreeReport.IsRoot(db, mixed), "earlier and same-age prerequisites");
            Assert.IsTrue(TechTreeReport.IsRoot(db, unknown), "(d) unknown prerequisite keys are ignored");
            Assert.IsFalse(TechTreeReport.IsRoot(db, later), "later-age prerequisite");
            Assert.IsTrue(TechTreeReport.IsRoot(db, noKeys), "null prerequisite keys");
            Assert.IsFalse(TechTreeReport.IsRoot(null, general), "(e) null database");
            Assert.IsFalse(TechTreeReport.IsRoot(db, null), "(e) null tech");

            var bronze = TechTreeReport.Summarize(db, AgeId.Bronze);
            Assert.AreEqual(6, bronze.TechCount);
            Assert.AreEqual(1, bronze.RacialTechCount);
            Assert.AreEqual(2, bronze.RootCount, "general + unknown");
            Assert.AreEqual(1, TechTreeReport.Summarize(db, AgeId.Dawn).RootCount);
        }

        private static TechDefinition Reg(GameDatabase db, string key, AgeId age, RaceMask races, params string[] prereqs)
        {
            var t = new TechDefinition { Key = key, Name = key, Age = age, Cost = 10f, AllowedRaces = races, PrerequisiteKeys = prereqs };
            db.Register(t);
            return t;
        }
    }
}
