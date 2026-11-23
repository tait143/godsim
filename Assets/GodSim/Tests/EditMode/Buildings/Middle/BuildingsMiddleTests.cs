using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Buildings;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Buildings
{
    /// <summary>Data/balance tests for the BuildingsMiddle catalog part (BuildingType 28..47 and 72..74).</summary>
    public class BuildingsMiddleTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _db = new GameDatabase();
            BuildingsMiddle.Register(_db);
        }

        private static readonly BuildingType[] Wonders = { BuildingType.GreatLibrary, BuildingType.Colossus, BuildingType.GrandBazaar };

        private static bool IsWonderType(BuildingType t) => Array.IndexOf(Wonders, t) >= 0;

        private static float ServiceOf(BuildingDefinition d, ServiceType s)
        {
            float sum = 0f;
            foreach (var a in d.Services) if (a.Type == s) sum += a.Amount;
            return sum;
        }

        private static float AmountOf(ResourceAmount[] list, ResourceType r)
        {
            float sum = 0f;
            foreach (var a in list) if (a.Type == r) sum += a.Amount;
            return sum;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        // ------------------------------------------------------------------ registration

        [Test]
        public void Register_ThenFillMissingAndFreeze_Succeeds()
        {
            TestDb.FillMissing(_db);
            var errors = _db.Validate();
            Assert.AreEqual(0, errors.Count, string.Join("\n", errors));
            Assert.DoesNotThrow(() => _db.Freeze());
            Assert.IsTrue(_db.IsFrozen);
            Assert.AreEqual("Great Library", _db.Building(BuildingType.GreatLibrary).Name);
        }

        [Test]
        public void Register_RegistersExactlyOwnedTypes()
        {
            int registered = 0;
            for (int i = 0; i < Counts.Buildings; i++)
            {
                var t = (BuildingType)i;
                bool expected = (i >= 28 && i <= 47) || (i >= 72 && i <= 74);
                Assert.AreEqual(expected, _db.Buildings[i] != null, "slot " + t);
                Assert.AreEqual(expected, BuildingsMiddle.Owns(t), "Owns " + t);
                if (_db.Buildings[i] == null) continue;
                registered++;
                Assert.AreEqual(t, _db.Buildings[i].Type, "Type field matches slot");
            }
            Assert.AreEqual(23, registered);
            Assert.AreEqual(23, BuildingsMiddle.Count);
            Assert.AreEqual(23, BuildingsMiddle.GetTypes().Length);
        }

        [Test]
        public void Register_LeavesForeignSlotsUntouched()
        {
            var db = new GameDatabase();
            var house = new BuildingDefinition { Type = BuildingType.House, Name = "Custom House", HousingMin = 3, HousingMax = 5 };
            db.Register(house);
            BuildingsMiddle.Register(db);
            Assert.AreSame(house, db.Building(BuildingType.House));
            Assert.IsFalse(db.TryGetBuilding(BuildingType.Factory, out _));
            Assert.IsFalse(db.TryGetBuilding(BuildingType.Scriptorium, out _));
        }

        [Test]
        public void Register_NullOrFrozenDatabase_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => BuildingsMiddle.Register(null));
            var frozen = TestDb.CreateStub(true);
            Assert.Throws<InvalidOperationException>(() => BuildingsMiddle.Register(frozen));
        }

        [Test]
        public void Register_Twice_ReplacesWithEqualData()
        {
            var first = _db.Building(BuildingType.Castle);
            BuildingsMiddle.Register(_db);
            var second = _db.Building(BuildingType.Castle);
            Assert.AreNotSame(first, second);
            Assert.AreEqual(first.Name, second.Name);
            Assert.AreEqual(first.BuildWork, second.BuildWork);
        }

        [Test]
        public void Create_ReturnsIndependentInstances_AndRejectsForeignTypes()
        {
            var a = BuildingsMiddle.Create(BuildingType.Workshop);
            a.Name = "Mutated";
            a.OutputsPerWorker[0] = new ResourceAmount(ResourceType.Goods, 99f);
            var b = BuildingsMiddle.Create(BuildingType.Workshop);
            Assert.AreEqual("Workshop", b.Name);
            Assert.AreEqual(0.04f, AmountOf(b.OutputsPerWorker, ResourceType.Goods), 1e-6f);
            Assert.Throws<ArgumentOutOfRangeException>(() => BuildingsMiddle.Create(BuildingType.House));
            Assert.Throws<ArgumentOutOfRangeException>(() => BuildingsMiddle.Create(BuildingType.Factory));
        }

        [Test]
        public void Create_EveryType_SharesNoMutableState()
        {
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                var a = BuildingsMiddle.Create(t);
                var b = BuildingsMiddle.Create(t);
                Assert.AreNotSame(a, b, t + " definition");
                AssertNotShared(t + " Cost", a.Cost, b.Cost);
                AssertNotShared(t + " InputsPerWorker", a.InputsPerWorker, b.InputsPerWorker);
                AssertNotShared(t + " OutputsPerWorker", a.OutputsPerWorker, b.OutputsPerWorker);
                if (a.Services.Length > 0) Assert.AreNotSame(a.Services, b.Services, t + " Services");

                float costBefore = b.Cost[0].Amount;
                a.Cost[0] = new ResourceAmount(a.Cost[0].Type, 9999f);
                Assert.AreEqual(costBefore, b.Cost[0].Amount, t + " Cost leaked between Create calls");
                Assert.AreEqual(costBefore, BuildingsMiddle.Create(t).Cost[0].Amount, t + " Cost leaked into later Create calls");
                if (a.Services.Length > 0)
                {
                    float svcBefore = b.Services[0].Amount;
                    a.Services[0] = new ServiceAmount(a.Services[0].Type, 9999f);
                    Assert.AreEqual(svcBefore, b.Services[0].Amount, t + " Services leaked between Create calls");
                }
            }
        }

        [Test]
        public void Register_TwoDatabases_ShareNoMutableState()
        {
            var other = new GameDatabase();
            BuildingsMiddle.Register(other);
            var a = _db.Building(BuildingType.GreatLibrary);
            float before = other.Building(BuildingType.GreatLibrary).Cost[0].Amount;
            a.Cost[0] = new ResourceAmount(a.Cost[0].Type, 1f);
            Assert.AreEqual(before, other.Building(BuildingType.GreatLibrary).Cost[0].Amount);
            var third = new GameDatabase();
            BuildingsMiddle.Register(third);
            Assert.AreEqual(before, third.Building(BuildingType.GreatLibrary).Cost[0].Amount);
        }

        private static void AssertNotShared(string label, ResourceAmount[] a, ResourceAmount[] b)
        {
            Assert.AreEqual(a.Length, b.Length, label + " length");
            if (a.Length > 0) Assert.AreNotSame(a, b, label + " array is shared");
        }

        [TestCase(0)]
        [TestCase(27)]
        [TestCase(48)]
        [TestCase(71)]
        [TestCase(75)]
        [TestCase(200)]
        [TestCase(255)]
        public void OutOfRangeTypes_AreNotOwned_AndCreateThrows(int raw)
        {
            var t = (BuildingType)raw;
            Assert.IsFalse(BuildingsMiddle.Owns(t), "Owns " + raw);
            Assert.Throws<ArgumentOutOfRangeException>(() => BuildingsMiddle.Create(t));
        }

        [Test]
        public void Owns_IsTrueForEveryListedType_AndCreateMatches()
        {
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                Assert.IsTrue(BuildingsMiddle.Owns(t), "Owns " + t);
                Assert.AreEqual(t, BuildingsMiddle.Create(t).Type, "Create " + t);
            }
        }

        [Test]
        public void GetTypes_ReturnsSortedDefensiveCopy()
        {
            var types = BuildingsMiddle.GetTypes();
            for (int i = 1; i < types.Length; i++) Assert.IsTrue(types[i] > types[i - 1]);
            types[0] = BuildingType.House;
            Assert.AreEqual(BuildingType.Library, BuildingsMiddle.GetTypes()[0]);
        }

        // ------------------------------------------------------------------ names & content

        [TestCase(BuildingType.Library, "Library")]
        [TestCase(BuildingType.StoneWall, "Stone Wall")]
        [TestCase(BuildingType.Guildhall, "Guildhall")]
        [TestCase(BuildingType.Printworks, "Printworks")]
        [TestCase(BuildingType.GreatLibrary, "Great Library")]
        [TestCase(BuildingType.Colossus, "Colossus")]
        [TestCase(BuildingType.GrandBazaar, "Grand Bazaar")]
        public void Names_AreHumanReadable(BuildingType t, string name)
        {
            Assert.AreEqual(name, _db.Building(t).Name);
        }

        [Test]
        public void AllDefinitions_HaveRealNamesAndDescriptions_AndAreUnique()
        {
            var names = new List<string>();
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                var d = _db.Building(t);
                Assert.IsFalse(string.IsNullOrEmpty(d.Name), t + " name");
                Assert.IsTrue(d.Description != null && d.Description.Length >= 40, t + " description");
                Assert.IsFalse(names.Contains(d.Name), "duplicate name " + d.Name);
                names.Add(d.Name);
            }
        }

        [Test]
        public void NoDwellings_InThisCatalogPart()
        {
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                var d = _db.Building(t);
                Assert.IsFalse(d.IsDwelling, t + " must not house residents");
                Assert.AreEqual(0, d.HousingMin, t + " HousingMin");
                Assert.AreEqual(0, d.HousingMax, t + " HousingMax");
                Assert.AreNotEqual(BuildingCategory.Housing, d.Category, t + " category");
            }
        }

        // ------------------------------------------------------------------ ages & availability

        [TestCase(BuildingType.Library, AgeId.Classical)]
        [TestCase(BuildingType.Aqueduct, AgeId.Classical)]
        [TestCase(BuildingType.Bathhouse, AgeId.Classical)]
        [TestCase(BuildingType.Theater, AgeId.Classical)]
        [TestCase(BuildingType.Harbor, AgeId.Classical)]
        [TestCase(BuildingType.Workshop, AgeId.Classical)]
        [TestCase(BuildingType.Forum, AgeId.Classical)]
        [TestCase(BuildingType.Arena, AgeId.Classical)]
        [TestCase(BuildingType.StoneWall, AgeId.Classical)]
        [TestCase(BuildingType.Castle, AgeId.Medieval)]
        [TestCase(BuildingType.Cathedral, AgeId.Medieval)]
        [TestCase(BuildingType.Monastery, AgeId.Medieval)]
        [TestCase(BuildingType.Mill, AgeId.Medieval)]
        [TestCase(BuildingType.Guildhall, AgeId.Medieval)]
        [TestCase(BuildingType.Tavern, AgeId.Medieval)]
        [TestCase(BuildingType.University, AgeId.Renaissance)]
        [TestCase(BuildingType.Bank, AgeId.Renaissance)]
        [TestCase(BuildingType.Observatory, AgeId.Renaissance)]
        [TestCase(BuildingType.Hospital, AgeId.Renaissance)]
        [TestCase(BuildingType.Printworks, AgeId.Renaissance)]
        [TestCase(BuildingType.GreatLibrary, AgeId.Classical)]
        [TestCase(BuildingType.Colossus, AgeId.Classical)]
        [TestCase(BuildingType.GrandBazaar, AgeId.Medieval)]
        public void MinAge_MatchesEnumComment(BuildingType t, AgeId age)
        {
            var d = _db.Building(t);
            Assert.AreEqual(age, d.MinAge);
            Assert.IsTrue(d.MaxAge >= d.MinAge, t + " MaxAge >= MinAge");
            Assert.AreEqual(RaceMask.All, d.AllowedRaces, t + " is shared by all races");
        }

        [Test]
        public void Availability_FollowsAgeWindow()
        {
            TestDb.FillMissing(_db);
            _db.Freeze();
            var civ = new Civilization { Race = RaceId.Dwarf, Age = AgeId.Iron };
            Assert.IsFalse(_db.IsBuildingAvailable(civ, BuildingType.Library));
            civ.Age = AgeId.Classical;
            Assert.IsTrue(_db.IsBuildingAvailable(civ, BuildingType.Library));
            Assert.IsTrue(_db.IsBuildingAvailable(civ, BuildingType.GreatLibrary));
            Assert.IsFalse(_db.IsBuildingAvailable(civ, BuildingType.Castle));
            Assert.IsFalse(_db.IsBuildingAvailable(civ, BuildingType.University));
            civ.Age = AgeId.Medieval;
            Assert.IsTrue(_db.IsBuildingAvailable(civ, BuildingType.Castle));
            Assert.IsTrue(_db.IsBuildingAvailable(civ, BuildingType.GrandBazaar));
            civ.Age = AgeId.Renaissance;
            Assert.IsTrue(_db.IsBuildingAvailable(civ, BuildingType.University));
            civ.Age = AgeId.Industrial;
            Assert.IsFalse(_db.IsBuildingAvailable(civ, BuildingType.Castle), "castles are obsolete after the Renaissance");
            Assert.IsTrue(_db.IsBuildingAvailable(civ, BuildingType.Aqueduct), "aqueducts never become obsolete");
            civ.Age = AgeId.Modern;
            Assert.IsFalse(_db.IsBuildingAvailable(civ, BuildingType.StoneWall));
            Assert.IsFalse(_db.IsBuildingAvailable(civ, BuildingType.Mill));
        }

        [TestCase(BuildingType.Castle, AgeId.Renaissance)]
        [TestCase(BuildingType.StoneWall, AgeId.Industrial)]
        [TestCase(BuildingType.Mill, AgeId.Industrial)]
        [TestCase(BuildingType.Workshop, AgeId.Modern)]
        public void ObsoleteBuildings_HaveExactMaxAge(BuildingType t, AgeId max)
        {
            Assert.AreEqual(max, _db.Building(t).MaxAge);
        }

        [Test]
        public void NonObsoleteBuildings_StayAvailableUntilAscension()
        {
            var obsolete = new[] { BuildingType.Castle, BuildingType.StoneWall, BuildingType.Mill, BuildingType.Workshop };
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                if (Array.IndexOf(obsolete, t) >= 0) continue;
                Assert.AreEqual(AgeId.Ascension, _db.Building(t).MaxAge, t + " MaxAge");
            }
        }

        [Test]
        public void Availability_MatchesAgeWindow_ForEveryTypeAgeAndRace()
        {
            TestDb.FillMissing(_db);
            _db.Freeze();
            foreach (var race in EnumUtil.AllRaces)
            {
                var civ = new Civilization { Race = race };
                foreach (var t in BuildingsMiddle.GetTypes())
                {
                    var d = _db.Building(t);
                    foreach (var age in EnumUtil.AllAges)
                    {
                        civ.Age = age;
                        bool expected = age >= d.MinAge && age <= d.MaxAge;
                        Assert.AreEqual(expected, _db.IsBuildingAvailable(civ, t), race + " " + t + " in " + age);
                    }
                }
            }
        }

        // ------------------------------------------------------------------ wonders

        [Test]
        public void Wonders_AreUniqueLargeAndVeryExpensive()
        {
            float maxRegularCost = 0f;
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                var d = _db.Building(t);
                float total = 0f;
                foreach (var c in d.Cost) total += c.Amount;
                if (IsWonderType(t))
                {
                    Assert.IsTrue(d.IsWonder, t + " IsWonder");
                    Assert.IsTrue(d.UniquePerCivilization, t + " UniquePerCivilization");
                    Assert.AreEqual(BuildingCategory.Wonder, d.Category, t + " category");
                    Assert.AreEqual(3, d.Width, t + " width");
                    Assert.AreEqual(3, d.Height, t + " height");
                    Assert.IsTrue(d.BuildWork >= 600f && d.BuildWork <= 900f, t + " BuildWork " + d.BuildWork);
                    Assert.AreEqual(SettlementTier.Town, d.MinTier, t + " MinTier");
                    Assert.AreEqual(1, d.MaxPerSettlement, t + " MaxPerSettlement");
                }
                else
                {
                    Assert.IsFalse(d.IsWonder, t + " is not a wonder");
                    Assert.IsFalse(d.UniquePerCivilization, t + " not unique");
                    Assert.AreNotEqual(BuildingCategory.Wonder, d.Category, t + " category");
                    Assert.IsTrue(d.BuildWork >= 60f && d.BuildWork <= 400f, t + " BuildWork " + d.BuildWork);
                    if (total > maxRegularCost) maxRegularCost = total;
                }
            }
            foreach (var w in Wonders)
            {
                float total = 0f;
                foreach (var c in _db.Building(w).Cost) total += c.Amount;
                Assert.IsTrue(total > maxRegularCost * 2f, w + " should cost far more than any regular building");
            }
        }

        // ------------------------------------------------------------------ jobs & balance table

        [TestCase(BuildingType.Library, JobType.Scholar, 3)]
        [TestCase(BuildingType.Bathhouse, JobType.Healer, 1)]
        [TestCase(BuildingType.Theater, JobType.Entertainer, 2)]
        [TestCase(BuildingType.Harbor, JobType.Merchant, 3)]
        [TestCase(BuildingType.Workshop, JobType.Craftsman, 3)]
        [TestCase(BuildingType.Forum, JobType.Administrator, 2)]
        [TestCase(BuildingType.Arena, JobType.Entertainer, 3)]
        [TestCase(BuildingType.Castle, JobType.Soldier, 10)]
        [TestCase(BuildingType.Cathedral, JobType.Priest, 4)]
        [TestCase(BuildingType.Monastery, JobType.Priest, 3)]
        [TestCase(BuildingType.Mill, JobType.Laborer, 2)]
        [TestCase(BuildingType.Guildhall, JobType.Merchant, 2)]
        [TestCase(BuildingType.Tavern, JobType.Merchant, 1)]
        [TestCase(BuildingType.University, JobType.Scholar, 5)]
        [TestCase(BuildingType.Bank, JobType.Merchant, 2)]
        [TestCase(BuildingType.Observatory, JobType.Scholar, 2)]
        [TestCase(BuildingType.Hospital, JobType.Healer, 4)]
        [TestCase(BuildingType.Printworks, JobType.Craftsman, 2)]
        [TestCase(BuildingType.Aqueduct, JobType.None, 0)]
        [TestCase(BuildingType.StoneWall, JobType.None, 0)]
        [TestCase(BuildingType.Colossus, JobType.None, 0)]
        [TestCase(BuildingType.GreatLibrary, JobType.Scholar, 4)]
        [TestCase(BuildingType.GrandBazaar, JobType.Merchant, 4)]
        public void Jobs_MatchBalanceTable(BuildingType t, JobType job, int slots)
        {
            var d = _db.Building(t);
            Assert.AreEqual(job, d.Job);
            Assert.AreEqual(slots, d.JobSlots);
            Assert.AreEqual(job != JobType.None, d.HasJobs);
        }

        [Test]
        public void JobBuildings_HaveSlots_AndJoblessBuildingsHaveNoIO()
        {
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                var d = _db.Building(t);
                if (d.Job != JobType.None) Assert.IsTrue(d.JobSlots > 0, t + " job without slots");
                else
                {
                    Assert.AreEqual(0, d.JobSlots, t + " slots without job");
                    Assert.AreEqual(0, d.InputsPerWorker.Length, t + " inputs need workers");
                    Assert.AreEqual(0, d.OutputsPerWorker.Length, t + " outputs need workers");
                }
                Assert.IsTrue(d.Services.Length > 0 || d.OutputsPerWorker.Length > 0, t + " must provide something");
            }
        }

        [TestCase(BuildingType.Library, ServiceType.Research, 3f)]
        [TestCase(BuildingType.University, ServiceType.Research, 7f)]
        [TestCase(BuildingType.Observatory, ServiceType.Research, 4f)]
        [TestCase(BuildingType.Printworks, ServiceType.Research, 1.5f)]
        [TestCase(BuildingType.Monastery, ServiceType.Faith, 3f)]
        [TestCase(BuildingType.Monastery, ServiceType.Research, 1.5f)]
        [TestCase(BuildingType.Cathedral, ServiceType.Faith, 8f)]
        [TestCase(BuildingType.Bathhouse, ServiceType.Health, 3f)]
        [TestCase(BuildingType.Bathhouse, ServiceType.Water, 2f)]
        [TestCase(BuildingType.Hospital, ServiceType.Health, 8f)]
        [TestCase(BuildingType.Aqueduct, ServiceType.Water, 10f)]
        [TestCase(BuildingType.Theater, ServiceType.Entertainment, 4f)]
        [TestCase(BuildingType.Arena, ServiceType.Entertainment, 6f)]
        [TestCase(BuildingType.Tavern, ServiceType.Entertainment, 3f)]
        [TestCase(BuildingType.Tavern, ServiceType.Commerce, 1f)]
        [TestCase(BuildingType.Forum, ServiceType.Administration, 6f)]
        [TestCase(BuildingType.StoneWall, ServiceType.Defense, 4f)]
        [TestCase(BuildingType.Castle, ServiceType.Defense, 20f)]
        [TestCase(BuildingType.Castle, ServiceType.Administration, 3f)]
        [TestCase(BuildingType.Harbor, ServiceType.Commerce, 3f)]
        [TestCase(BuildingType.Guildhall, ServiceType.Commerce, 3f)]
        [TestCase(BuildingType.Bank, ServiceType.Commerce, 5f)]
        [TestCase(BuildingType.GreatLibrary, ServiceType.Research, 25f)]
        [TestCase(BuildingType.Colossus, ServiceType.Commerce, 20f)]
        [TestCase(BuildingType.GrandBazaar, ServiceType.Commerce, 25f)]
        public void Services_MatchBalanceTable(BuildingType t, ServiceType s, float amount)
        {
            Assert.AreEqual(amount, ServiceOf(_db.Building(t), s), 1e-5f);
        }

        [Test]
        public void ProductionChains_MatchBalanceTable()
        {
            var workshop = _db.Building(BuildingType.Workshop);
            Assert.AreEqual(0.03f, AmountOf(workshop.InputsPerWorker, ResourceType.Wood), 1e-6f);
            Assert.AreEqual(0.02f, AmountOf(workshop.InputsPerWorker, ResourceType.Cloth), 1e-6f);
            Assert.AreEqual(0.04f, AmountOf(workshop.OutputsPerWorker, ResourceType.Goods), 1e-6f);

            var print = _db.Building(BuildingType.Printworks);
            Assert.AreEqual(0.02f, AmountOf(print.InputsPerWorker, ResourceType.Wood), 1e-6f);
            Assert.AreEqual(0.02f, AmountOf(print.OutputsPerWorker, ResourceType.Goods), 1e-6f);

            Assert.AreEqual(0.12f, AmountOf(_db.Building(BuildingType.Mill).OutputsPerWorker, ResourceType.Food), 1e-6f);
            Assert.AreEqual(0.08f, AmountOf(_db.Building(BuildingType.Harbor).OutputsPerWorker, ResourceType.Food), 1e-6f);
            Assert.AreEqual(0.04f, AmountOf(_db.Building(BuildingType.Bank).OutputsPerWorker, ResourceType.Gold), 1e-6f);
            Assert.AreEqual(0.06f, AmountOf(_db.Building(BuildingType.GrandBazaar).OutputsPerWorker, ResourceType.Gold), 1e-6f);
        }

        [Test]
        public void JobBuildings_EmployAtMostTwelve()
        {
            // Wonders and regular buildings alike: no single workplace may drain a town's adults (Castle's 10 is the largest).
            foreach (var t in BuildingsMiddle.GetTypes())
                Assert.IsTrue(_db.Building(t).JobSlots <= 12, t + " JobSlots " + _db.Building(t).JobSlots);
        }

        [Test]
        public void FoodBuildings_FeedMorePeopleThanTheyEmploy()
        {
            const float foodPerPerson = 0.05f;
            foreach (var t in new[] { BuildingType.Mill, BuildingType.Harbor })
            {
                var d = _db.Building(t);
                float perWorker = AmountOf(d.OutputsPerWorker, ResourceType.Food);
                Assert.IsTrue(perWorker > foodPerPerson, t + " worker must feed more than themselves");
            }
        }

        [Test]
        public void GoodsAndGoldProduction_ExistInThisSet()
        {
            bool goods = false, gold = false;
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                var d = _db.Building(t);
                if (AmountOf(d.OutputsPerWorker, ResourceType.Goods) > 0f) goods = true;
                if (AmountOf(d.OutputsPerWorker, ResourceType.Gold) > 0f) gold = true;
            }
            Assert.IsTrue(goods, "some building must produce Goods");
            Assert.IsTrue(gold, "some building must produce Gold");
        }

        [Test]
        public void ProductionBuildings_AreNetPositive()
        {
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                var d = _db.Building(t);
                float inSum = 0f, outSum = 0f;
                foreach (var a in d.InputsPerWorker) inSum += a.Amount;
                foreach (var a in d.OutputsPerWorker) outSum += a.Amount;
                if (inSum > 0f) Assert.IsTrue(outSum > 0f, t + " consumes without producing");
            }
        }

        // ------------------------------------------------------------------ placement & limits

        [Test]
        public void Placement_MatchesBrief()
        {
            Assert.IsTrue((_db.Building(BuildingType.Harbor).Placement & PlacementRule.Coastal) != 0);
            Assert.IsTrue((_db.Building(BuildingType.Colossus).Placement & PlacementRule.Coastal) != 0);
            Assert.AreEqual(PlacementRule.Fertile | PlacementRule.NearRiver, _db.Building(BuildingType.Mill).Placement);
            Assert.IsTrue((_db.Building(BuildingType.Workshop).Placement & PlacementRule.Isolated) != 0);
            Assert.IsTrue((_db.Building(BuildingType.Forum).Placement & PlacementRule.Central) != 0);
            Assert.IsTrue((_db.Building(BuildingType.Cathedral).Placement & PlacementRule.Central) != 0);
            Assert.IsTrue((_db.Building(BuildingType.StoneWall).Placement & PlacementRule.Outskirts) != 0);
            // The brief says the Observatory only *prefers* hills; OnHills is a hard rule in the contract.
            Assert.AreEqual(PlacementRule.None, _db.Building(BuildingType.Observatory).Placement & PlacementRule.OnHills);
            Assert.AreEqual(PlacementRule.None, _db.Building(BuildingType.Observatory).Placement & HardPlacement);
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                var p = _db.Building(t).Placement;
                Assert.IsFalse((p & PlacementRule.Central) != 0 && (p & PlacementRule.Outskirts) != 0, t + " cannot be both central and outskirts");
            }
        }

        /// <summary>Placement flags the contract treats as HARD requirements (a site failing one is rejected).</summary>
        private const PlacementRule HardPlacement =
            PlacementRule.NearWater | PlacementRule.Coastal | PlacementRule.OnHills | PlacementRule.NearMountain
            | PlacementRule.NearForest | PlacementRule.NearOre | PlacementRule.NearStone | PlacementRule.NearGold
            | PlacementRule.NearCoal | PlacementRule.NearOil | PlacementRule.Fertile | PlacementRule.NearRiver;

        [Test]
        public void HardPlacement_OnlyWhereTheBriefDemandsIt()
        {
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                PlacementRule expected;
                switch (t)
                {
                    case BuildingType.Harbor: case BuildingType.Colossus: expected = PlacementRule.Coastal; break;
                    case BuildingType.Mill: expected = PlacementRule.Fertile | PlacementRule.NearRiver; break;
                    default: expected = PlacementRule.None; break;
                }
                Assert.AreEqual(expected, _db.Building(t).Placement & HardPlacement, t + " hard placement flags");
            }
        }

        [TestCase(BuildingType.Library, PlacementRule.Central)]
        [TestCase(BuildingType.Aqueduct, PlacementRule.None)]
        [TestCase(BuildingType.Bathhouse, PlacementRule.Central)]
        [TestCase(BuildingType.Theater, PlacementRule.Central)]
        [TestCase(BuildingType.Harbor, PlacementRule.Coastal)]
        [TestCase(BuildingType.Workshop, PlacementRule.Isolated)]
        [TestCase(BuildingType.Forum, PlacementRule.Central)]
        [TestCase(BuildingType.Arena, PlacementRule.Outskirts)]
        [TestCase(BuildingType.StoneWall, PlacementRule.Outskirts)]
        [TestCase(BuildingType.Castle, PlacementRule.Central)]
        [TestCase(BuildingType.Cathedral, PlacementRule.Central)]
        [TestCase(BuildingType.Monastery, PlacementRule.Outskirts)]
        [TestCase(BuildingType.Mill, PlacementRule.Fertile | PlacementRule.NearRiver)]
        [TestCase(BuildingType.Guildhall, PlacementRule.Central)]
        [TestCase(BuildingType.Tavern, PlacementRule.None)]
        [TestCase(BuildingType.University, PlacementRule.Central)]
        [TestCase(BuildingType.Bank, PlacementRule.Central)]
        [TestCase(BuildingType.Observatory, PlacementRule.Outskirts)]
        [TestCase(BuildingType.Hospital, PlacementRule.Central)]
        [TestCase(BuildingType.Printworks, PlacementRule.None)]
        [TestCase(BuildingType.GreatLibrary, PlacementRule.Central)]
        [TestCase(BuildingType.Colossus, PlacementRule.Coastal)]
        [TestCase(BuildingType.GrandBazaar, PlacementRule.Central)]
        public void Placement_IsExact(BuildingType t, PlacementRule expected)
        {
            Assert.AreEqual(expected, _db.Building(t).Placement);
        }

        [TestCase(BuildingType.Castle, 1)]
        [TestCase(BuildingType.Forum, 1)]
        [TestCase(BuildingType.Cathedral, 1)]
        [TestCase(BuildingType.University, 1)]
        [TestCase(BuildingType.Harbor, 2)]
        [TestCase(BuildingType.GreatLibrary, 1)]
        [TestCase(BuildingType.Colossus, 1)]
        [TestCase(BuildingType.GrandBazaar, 1)]
        [TestCase(BuildingType.Library, -1)]
        [TestCase(BuildingType.StoneWall, 24)]
        public void MaxPerSettlement_MatchesBrief(BuildingType t, int max)
        {
            Assert.AreEqual(max, _db.Building(t).MaxPerSettlement);
        }

        [Test]
        public void StoneWall_DefensePerCost_IsAtMostTwiceTheCastle()
        {
            var wall = _db.Building(BuildingType.StoneWall);
            var castle = _db.Building(BuildingType.Castle);
            float wallRatio = ServiceOf(wall, ServiceType.Defense) / TotalCost(wall);
            float castleRatio = ServiceOf(castle, ServiceType.Defense) / TotalCost(castle);
            Assert.IsTrue(wallRatio <= castleRatio * 2f, "wall " + wallRatio + " vs castle " + castleRatio);
            Assert.IsTrue(wall.BuildWork >= 90f, "wall segments must take real labor: " + wall.BuildWork);

            // A full allowance of segments must stay bounded in both Defense and upkeep.
            Assert.IsTrue(wall.MaxPerSettlement > 0, "wall segments must be capped");
            float maxDefense = wall.MaxPerSettlement * ServiceOf(wall, ServiceType.Defense);
            float maxUpkeep = wall.MaxPerSettlement * wall.UpkeepGold;
            Assert.IsTrue(maxDefense <= ServiceOf(castle, ServiceType.Defense) * 5f, "max wall defense " + maxDefense);
            Assert.IsTrue(maxUpkeep <= 0.25f + 1e-5f, "max wall upkeep " + maxUpkeep);
        }

        private static float TotalCost(BuildingDefinition d)
        {
            float total = 0f;
            foreach (var c in d.Cost) total += c.Amount;
            return total;
        }

        [TestCase(BuildingType.Library, SettlementTier.Village)]
        [TestCase(BuildingType.Aqueduct, SettlementTier.Village)]
        [TestCase(BuildingType.Bathhouse, SettlementTier.Village)]
        [TestCase(BuildingType.Theater, SettlementTier.Village)]
        [TestCase(BuildingType.Harbor, SettlementTier.Village)]
        [TestCase(BuildingType.Workshop, SettlementTier.Hamlet)]
        [TestCase(BuildingType.Forum, SettlementTier.Village)]
        [TestCase(BuildingType.Arena, SettlementTier.Village)]
        [TestCase(BuildingType.StoneWall, SettlementTier.Hamlet)]
        [TestCase(BuildingType.Castle, SettlementTier.Village)]
        [TestCase(BuildingType.Cathedral, SettlementTier.Town)]
        [TestCase(BuildingType.Monastery, SettlementTier.Hamlet)]
        [TestCase(BuildingType.Mill, SettlementTier.Hamlet)]
        [TestCase(BuildingType.Guildhall, SettlementTier.Village)]
        [TestCase(BuildingType.Tavern, SettlementTier.Hamlet)]
        [TestCase(BuildingType.University, SettlementTier.Town)]
        [TestCase(BuildingType.Bank, SettlementTier.Town)]
        [TestCase(BuildingType.Observatory, SettlementTier.Village)]
        [TestCase(BuildingType.Hospital, SettlementTier.Village)]
        [TestCase(BuildingType.Printworks, SettlementTier.Village)]
        [TestCase(BuildingType.GreatLibrary, SettlementTier.Town)]
        [TestCase(BuildingType.Colossus, SettlementTier.Town)]
        [TestCase(BuildingType.GrandBazaar, SettlementTier.Town)]
        public void MinTier_IsExact(BuildingType t, SettlementTier tier)
        {
            Assert.AreEqual(tier, _db.Building(t).MinTier);
        }

        [Test]
        public void MinTier_TownOnlyForBriefedBuildings_AndPopulationReachable()
        {
            var townLevel = new[]
            {
                BuildingType.Cathedral, BuildingType.University, BuildingType.Bank,
                BuildingType.GreatLibrary, BuildingType.Colossus, BuildingType.GrandBazaar
            };
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                var d = _db.Building(t);
                if (Array.IndexOf(townLevel, t) >= 0) Assert.AreEqual(SettlementTier.Town, d.MinTier, t + " MinTier");
                else Assert.IsTrue(d.MinTier <= SettlementTier.Village, t + " MinTier " + d.MinTier);
                Assert.IsTrue(d.MinPopulation > 0 && d.MinPopulation <= 100, t + " MinPopulation " + d.MinPopulation);
            }
        }

        [TestCase(BuildingType.Cathedral)]
        [TestCase(BuildingType.University)]
        [TestCase(BuildingType.Bank)]
        public void TownLevelBuildings_RequireTownTier(BuildingType t)
        {
            var d = _db.Building(t);
            Assert.AreEqual(SettlementTier.Town, d.MinTier);
            Assert.IsTrue(d.MinPopulation > 0);
        }

        [TestCase(BuildingType.Library, 2, 2)]
        [TestCase(BuildingType.Aqueduct, 1, 3)]
        [TestCase(BuildingType.Bathhouse, 2, 2)]
        [TestCase(BuildingType.Theater, 2, 2)]
        [TestCase(BuildingType.Harbor, 2, 2)]
        [TestCase(BuildingType.Workshop, 2, 2)]
        [TestCase(BuildingType.Forum, 3, 2)]
        [TestCase(BuildingType.Arena, 3, 3)]
        [TestCase(BuildingType.StoneWall, 1, 1)]
        [TestCase(BuildingType.Castle, 3, 3)]
        [TestCase(BuildingType.Cathedral, 3, 3)]
        [TestCase(BuildingType.Monastery, 2, 2)]
        [TestCase(BuildingType.Mill, 2, 2)]
        [TestCase(BuildingType.Guildhall, 2, 2)]
        [TestCase(BuildingType.Tavern, 2, 2)]
        [TestCase(BuildingType.University, 3, 2)]
        [TestCase(BuildingType.Bank, 2, 2)]
        [TestCase(BuildingType.Observatory, 2, 2)]
        [TestCase(BuildingType.Hospital, 2, 2)]
        [TestCase(BuildingType.Printworks, 2, 2)]
        [TestCase(BuildingType.GreatLibrary, 3, 3)]
        [TestCase(BuildingType.Colossus, 3, 3)]
        [TestCase(BuildingType.GrandBazaar, 3, 3)]
        public void Footprints_MatchBrief(BuildingType t, int w, int h)
        {
            var d = _db.Building(t);
            Assert.AreEqual(w, d.Width, t + " width");
            Assert.AreEqual(h, d.Height, t + " height");
        }

        // ------------------------------------------------------------------ numeric hygiene

        [Test]
        public void AllNumbers_AreFiniteAndInRange()
        {
            foreach (var t in BuildingsMiddle.GetTypes())
            {
                var d = _db.Building(t);
                Assert.IsTrue(d.Width >= 1 && d.Width <= 8 && d.Height >= 1 && d.Height <= 8, t + " footprint");
                Assert.IsTrue(Finite(d.BuildWork) && d.BuildWork > 0f, t + " BuildWork");
                Assert.IsTrue(Finite(d.MaxCondition) && d.MaxCondition > 0f, t + " MaxCondition");
                Assert.IsTrue(Finite(d.StorageCapacity) && d.StorageCapacity >= 0f, t + " storage");
                Assert.IsTrue(Finite(d.UpkeepGold) && d.UpkeepGold >= 0.01f - 1e-6f && d.UpkeepGold <= 0.05f + 1e-6f, t + " upkeep " + d.UpkeepGold);
                Assert.IsTrue(Finite(d.Desirability) && d.Desirability >= -1f && d.Desirability <= 1f, t + " desirability");
                Assert.IsTrue(Finite(d.VisualHeight) && d.VisualHeight > 0f, t + " visual height");
                Assert.IsTrue(d.ServiceRadius > 0, t + " service radius");
                Assert.IsTrue(d.MinPopulation >= 0, t + " MinPopulation");
                Assert.IsTrue(d.MaxPerSettlement == -1 || d.MaxPerSettlement >= 1, t + " MaxPerSettlement");
                Assert.IsTrue(d.Cost.Length >= 2, t + " should cost more than one resource");
                AssertAmounts(t + " cost", d.Cost, true);
                AssertAmounts(t + " inputs", d.InputsPerWorker, true);
                AssertAmounts(t + " outputs", d.OutputsPerWorker, true);
                var seenServices = new List<ServiceType>();
                foreach (var s in d.Services)
                {
                    Assert.IsTrue(Finite(s.Amount) && s.Amount > 0f, t + " service " + s.Type);
                    Assert.IsFalse(seenServices.Contains(s.Type), t + " duplicate service " + s.Type);
                    seenServices.Add(s.Type);
                }
                foreach (var a in d.InputsPerWorker) Assert.IsTrue(a.Amount < 1f, t + " per-worker input is per tick and must be small");
                foreach (var a in d.OutputsPerWorker) Assert.IsTrue(a.Amount < 1f, t + " per-worker output is per tick and must be small");
            }
        }

        private static void AssertAmounts(string label, ResourceAmount[] list, bool positive)
        {
            Assert.IsNotNull(list, label);
            var seen = new List<ResourceType>();
            foreach (var a in list)
            {
                Assert.IsTrue(Finite(a.Amount), label + " finite");
                Assert.IsTrue(positive ? a.Amount > 0f : a.Amount >= 0f, label + " " + a.Type + " = " + a.Amount);
                Assert.IsFalse(seen.Contains(a.Type), label + " duplicate " + a.Type);
                seen.Add(a.Type);
            }
        }

        [Test]
        public void Costs_UseOnlyPreIndustrialMaterials()
        {
            foreach (var t in BuildingsMiddle.GetTypes())
                foreach (var c in _db.Building(t).Cost)
                    Assert.IsTrue(c.Type == ResourceType.Wood || c.Type == ResourceType.Stone || c.Type == ResourceType.Metal
                        || c.Type == ResourceType.Tools || c.Type == ResourceType.Gold || c.Type == ResourceType.Cloth
                        || c.Type == ResourceType.Goods, t + " uses " + c.Type);
        }

        [Test]
        public void LaterAges_CostMoreOnAverage()
        {
            float classical = AverageCost(28, 36), medieval = AverageCost(37, 42), renaissance = AverageCost(43, 47);
            Assert.IsTrue(renaissance > classical, "renaissance " + renaissance + " vs classical " + classical);
            Assert.IsTrue(medieval > classical, "medieval " + medieval + " vs classical " + classical);
        }

        private float AverageCost(int from, int to)
        {
            float sum = 0f;
            for (int i = from; i <= to; i++) foreach (var c in _db.Building((BuildingType)i).Cost) sum += c.Amount;
            return sum / (to - from + 1);
        }

        // ------------------------------------------------------------------ integration with shared WorldOps

        [Test]
        public void WorldOps_PlacesWonderWithFootprint_AndChroniclesCompletion()
        {
            TestDb.FillMissing(_db);
            _db.Freeze();
            var ctx = TestWorlds.CreateContext(_db, 64, 7);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 6, out var civ);
            civ.Age = AgeId.Classical;
            Assert.IsTrue(_db.IsBuildingAvailable(civ, BuildingType.GreatLibrary));

            var def = _db.Building(BuildingType.GreatLibrary);
            var site = ctx.Ops.FindFreeSite(new Vec2i(38, 38), def.Width, def.Height, 10);
            Assert.AreNotEqual(Vec2i.Invalid, site);
            var b = ctx.Ops.CreateBuilding(s, BuildingType.GreatLibrary, site, 0, true);
            Assert.IsNotNull(b);
            Assert.AreEqual(3, b.Width);
            Assert.AreEqual(3, b.Height);
            Assert.AreEqual(0, b.HousingCapacity);
            Assert.IsTrue(b.IsComplete);
            Assert.IsTrue(b.Occupies(new Vec2i(site.X + 2, site.Y + 2)));

            bool chronicled = false;
            foreach (var e in ctx.State.Chronicle.Entries)
                if (e.Text != null && e.Text.Contains("Great Library")) chronicled = true;
            Assert.IsTrue(chronicled, "wonder completion should be chronicled by name");
        }

        [Test]
        public void WorldOps_RespectsJobSlotsOfCastle()
        {
            TestDb.FillMissing(_db);
            _db.Freeze();
            var ctx = TestWorlds.CreateContext(_db, 64, 3);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(20, 20), 12, out _);
            var site = ctx.Ops.FindFreeSite(new Vec2i(26, 26), 3, 3, 10);
            var castle = ctx.Ops.CreateBuilding(s, BuildingType.Castle, site, 0, true);
            Assert.IsNotNull(castle);
            int hired = 0;
            var people = ctx.State.People.Items;
            for (int i = 0; i < people.Count; i++)
                if (ctx.Ops.AssignJob(people[i], castle, JobType.Soldier)) hired++;
            Assert.AreEqual(_db.Building(BuildingType.Castle).JobSlots, hired);
            Assert.AreEqual(10, castle.Workers.Count);
        }
    }
}
