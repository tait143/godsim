using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Buildings;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Buildings
{
    /// <summary>Tests for the Industrial..Ascension + racial wonder catalog part (BuildingType 48..71).</summary>
    public class BuildingsLateTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _db = new GameDatabase();
            BuildingsLate.Register(_db);
            TestDb.FillMissing(_db);
            _db.Freeze();
        }

        private static IEnumerable<BuildingDefinition> Own(GameDatabase db)
        {
            for (int i = (int)BuildingsLate.FirstType; i <= (int)BuildingsLate.LastType; i++) yield return db.Building((BuildingType)i);
        }

        private static float TotalCost(BuildingDefinition d)
        {
            float sum = 0f;
            foreach (var c in d.Cost) sum += c.Amount;
            return sum;
        }

        private static bool Has(ResourceAmount[] list, ResourceType type, out float amount)
        {
            amount = 0f;
            foreach (var r in list) if (r.Type == type) { amount = r.Amount; return true; }
            return false;
        }

        private static float ServiceOf(BuildingDefinition d, ServiceType type)
        {
            foreach (var s in d.Services) if (s.Type == type) return s.Amount;
            return 0f;
        }

        // ------------------------------------------------------------------ registration

        [Test]
        public void Register_FreshDb_FillMissing_Freeze_Succeeds()
        {
            Assert.IsTrue(_db.IsFrozen);
            Assert.AreEqual(0, _db.Validate().Count, string.Join("\n", _db.Validate()));
        }

        [Test]
        public void Register_DefinesExactlyTypes48To71()
        {
            var db = new GameDatabase();
            BuildingsLate.Register(db);
            Assert.AreEqual(24, BuildingsLate.Count);
            for (int i = 0; i < Counts.Buildings; i++)
            {
                var t = (BuildingType)i;
                bool own = i >= 48 && i <= 71;
                Assert.AreEqual(own, BuildingsLate.Owns(t), t.ToString());
                if (own)
                {
                    Assert.IsNotNull(db.Buildings[i], t + " missing");
                    Assert.AreEqual(t, db.Buildings[i].Type);
                }
                else Assert.IsNull(db.Buildings[i], t + " must not be registered by BuildingsLate");
            }
            var types = BuildingsLate.GetTypes();
            Assert.AreEqual(24, types.Length);
            Assert.AreEqual(BuildingType.Factory, types[0]);
            Assert.AreEqual(BuildingType.SunPyramid, types[23]);
        }

        [Test]
        public void Register_DoesNotOverwriteOtherSlots()
        {
            var db = new GameDatabase();
            var house = new BuildingDefinition { Type = BuildingType.House, Name = "Mine", HousingMin = 3, HousingMax = 5 };
            var bazaar = new BuildingDefinition { Type = BuildingType.GrandBazaar, Name = "Other team" };
            db.Register(house);
            db.Register(bazaar);
            BuildingsLate.Register(db);
            Assert.AreSame(house, db.Buildings[(int)BuildingType.House]);
            Assert.AreSame(bazaar, db.Buildings[(int)BuildingType.GrandBazaar]);
        }

        [Test]
        public void Register_NullOrFrozenDb_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => BuildingsLate.Register(null));
            Assert.Throws<InvalidOperationException>(() => BuildingsLate.Register(_db));
        }

        [Test]
        public void Create_OutOfRange_Throws_AndReturnsFreshInstances()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => BuildingsLate.Create(BuildingType.House));
            Assert.Throws<ArgumentOutOfRangeException>(() => BuildingsLate.Create(BuildingType.GreatLibrary));
            var a = BuildingsLate.Create(BuildingType.Factory);
            var b = BuildingsLate.Create(BuildingType.Factory);
            Assert.AreNotSame(a, b);
            Assert.AreNotSame(a.Cost, b.Cost);
            a.Cost[0] = new ResourceAmount(ResourceType.Gold, 9999f);
            Assert.AreNotEqual(9999f, BuildingsLate.Create(BuildingType.Factory).Cost[0].Amount);
        }

        [Test]
        public void CreateAll_IsDeterministicAndSharesNothing()
        {
            var a = BuildingsLate.CreateAll();
            var b = BuildingsLate.CreateAll();
            Assert.AreEqual(BuildingsLate.Count, a.Length);
            Assert.AreEqual(BuildingsLate.Count, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                string n = a[i].Type.ToString();
                Assert.AreEqual((BuildingType)((int)BuildingsLate.FirstType + i), a[i].Type, n);
                Assert.AreNotSame(a[i], b[i], n);
                AssertSameData(a[i], b[i], n);
                AssertSameData(a[i], BuildingsLate.Create(a[i].Type), n + " (Create)");
                AssertNotShared(a[i].Cost, b[i].Cost, n + " Cost");
                AssertNotShared(a[i].InputsPerWorker, b[i].InputsPerWorker, n + " InputsPerWorker");
                AssertNotShared(a[i].OutputsPerWorker, b[i].OutputsPerWorker, n + " OutputsPerWorker");
                if (a[i].Services.Length > 0) Assert.AreNotSame(a[i].Services, b[i].Services, n + " Services");
            }

            // Two databases never share a definition: mutating one cannot leak into the other.
            var db1 = new GameDatabase();
            var db2 = new GameDatabase();
            BuildingsLate.Register(db1);
            BuildingsLate.Register(db2);
            foreach (var t in BuildingsLate.GetTypes())
            {
                Assert.AreNotSame(db1.Building(t), db2.Building(t), t.ToString());
                AssertSameData(db1.Building(t), db2.Building(t), t + " (db)");
            }
        }

        [Test]
        public void Names_AreRealUniqueAndDescribed()
        {
            var names = new HashSet<string>();
            foreach (var d in Own(_db))
            {
                Assert.IsFalse(string.IsNullOrWhiteSpace(d.Name), d.Type + " name");
                Assert.IsTrue(names.Add(d.Name), "duplicate name " + d.Name);
                Assert.IsTrue(d.Description != null && d.Description.Length >= 60, d.Type + " needs a flavorful description");
                Assert.AreEqual(d.Name.Trim(), d.Name);
            }
            Assert.AreEqual("Coal Mine", _db.Building(BuildingType.CoalMine).Name);
            Assert.AreEqual("Ascension Gate", _db.Building(BuildingType.AscensionGate).Name);
            Assert.AreEqual("World Tree", _db.Building(BuildingType.WorldTree).Name);
        }

        // ------------------------------------------------------------------ ages & races

        [TestCase(BuildingType.Factory, AgeId.Industrial)]
        [TestCase(BuildingType.CoalMine, AgeId.Industrial)]
        [TestCase(BuildingType.Sawmill, AgeId.Industrial)]
        [TestCase(BuildingType.RailStation, AgeId.Industrial)]
        [TestCase(BuildingType.Foundry, AgeId.Industrial)]
        [TestCase(BuildingType.PowerPlant, AgeId.Modern)]
        [TestCase(BuildingType.OilWell, AgeId.Modern)]
        [TestCase(BuildingType.IndustrialFarm, AgeId.Modern)]
        [TestCase(BuildingType.ResearchLab, AgeId.Modern)]
        [TestCase(BuildingType.Stadium, AgeId.Modern)]
        [TestCase(BuildingType.MilitaryBase, AgeId.Modern)]
        [TestCase(BuildingType.DataCenter, AgeId.Information)]
        [TestCase(BuildingType.ElectronicsPlant, AgeId.Information)]
        [TestCase(BuildingType.Airport, AgeId.Information)]
        [TestCase(BuildingType.MedicalCenter, AgeId.Information)]
        [TestCase(BuildingType.HydroponicsTower, AgeId.Ascension)]
        [TestCase(BuildingType.FusionReactor, AgeId.Ascension)]
        [TestCase(BuildingType.Arcology, AgeId.Ascension)]
        [TestCase(BuildingType.AscensionGate, AgeId.Ascension)]
        [TestCase(BuildingType.GrandCathedral, AgeId.Medieval)]
        [TestCase(BuildingType.WorldTree, AgeId.Iron)]
        [TestCase(BuildingType.DeepForge, AgeId.Bronze)]
        [TestCase(BuildingType.WarIdol, AgeId.Bronze)]
        [TestCase(BuildingType.SunPyramid, AgeId.Iron)]
        public void MinAge_MatchesEnumComments(BuildingType type, AgeId age)
        {
            var d = _db.Building(type);
            Assert.AreEqual(age, d.MinAge);
            Assert.AreEqual(AgeId.Ascension, d.MaxAge);
        }

        [TestCase(BuildingType.GrandCathedral, RaceId.Human)]
        [TestCase(BuildingType.WorldTree, RaceId.Elf)]
        [TestCase(BuildingType.DeepForge, RaceId.Dwarf)]
        [TestCase(BuildingType.WarIdol, RaceId.Orc)]
        [TestCase(BuildingType.SunPyramid, RaceId.Saurian)]
        public void RacialUniques_RestrictedToExactlyOneRace(BuildingType type, RaceId race)
        {
            var d = _db.Building(type);
            Assert.AreEqual(race.ToMask(), d.AllowedRaces);
            Assert.IsTrue(d.IsWonder);
            Assert.IsTrue(d.UniquePerCivilization);
            Assert.AreEqual(1, d.MaxPerSettlement);
            Assert.AreEqual(BuildingCategory.Wonder, d.Category);
            foreach (var r in EnumUtil.AllRaces)
            {
                var civ = new Civilization { Race = r, Age = AgeId.Ascension };
                Assert.AreEqual(r == race, _db.IsBuildingAvailable(civ, type), type + " for " + r);
            }
        }

        [Test]
        public void NonRacialBuildings_AreOpenToAllRaces()
        {
            foreach (var d in Own(_db))
            {
                if (d.Type >= BuildingType.GrandCathedral) continue;
                Assert.AreEqual(RaceMask.All, d.AllowedRaces, d.Type.ToString());
            }
        }

        [Test]
        public void Availability_FollowsAge()
        {
            var civ = new Civilization { Race = RaceId.Human, Age = AgeId.Renaissance };
            Assert.IsFalse(_db.IsBuildingAvailable(civ, BuildingType.Factory));
            civ.Age = AgeId.Industrial;
            Assert.IsTrue(_db.IsBuildingAvailable(civ, BuildingType.Factory));
            Assert.IsFalse(_db.IsBuildingAvailable(civ, BuildingType.PowerPlant));
            var dwarves = new Civilization { Race = RaceId.Dwarf, Age = AgeId.Bronze };
            Assert.IsTrue(_db.IsBuildingAvailable(dwarves, BuildingType.DeepForge));
            dwarves.Age = AgeId.Dawn;
            Assert.IsFalse(_db.IsBuildingAvailable(dwarves, BuildingType.DeepForge));
        }

        // ------------------------------------------------------------------ structure

        [Test]
        public void NoDwellings_AndWondersAreConsistent()
        {
            foreach (var d in Own(_db))
            {
                Assert.IsFalse(d.IsDwelling, d.Type + " must not be a dwelling");
                Assert.AreEqual(0, d.HousingMin, d.Type.ToString());
                if (d.IsWonder)
                {
                    Assert.IsTrue(d.UniquePerCivilization, d.Type.ToString());
                    Assert.AreEqual(BuildingCategory.Wonder, d.Category, d.Type.ToString());
                    Assert.AreEqual(1, d.MaxPerSettlement, d.Type.ToString());
                }
                else Assert.IsFalse(d.UniquePerCivilization, d.Type.ToString());
                Assert.IsTrue(d.MaxPerSettlement == -1 || d.MaxPerSettlement >= 1, d.Type.ToString());
            }
        }

        [Test]
        public void Footprints_AreSquare2x2Or3x3_ExceptGate4x4()
        {
            foreach (var d in Own(_db))
            {
                Assert.AreEqual(d.Width, d.Height, d.Type.ToString());
                if (d.Type == BuildingType.AscensionGate) Assert.AreEqual(4, d.Width);
                else Assert.IsTrue(d.Width == 2 || d.Width == 3, d.Type + " width " + d.Width);
            }
        }

        [Test]
        public void Numbers_AreFiniteNonNegativeAndInBalanceBands()
        {
            foreach (var d in Own(_db))
            {
                string n = d.Type.ToString();
                Assert.IsTrue(d.Cost.Length > 0, n + " needs a cost");
                AssertFiniteNonNegative(d.Cost, n + " cost");
                AssertFiniteNonNegative(d.InputsPerWorker, n + " inputs");
                AssertFiniteNonNegative(d.OutputsPerWorker, n + " outputs");
                foreach (var s in d.Services) Assert.IsTrue(IsFinite(s.Amount) && s.Amount > 0f, n + " service " + s.Type);
                Assert.IsTrue(IsFinite(d.UpkeepGold) && d.UpkeepGold >= 0.03f - 1e-6f && d.UpkeepGold <= 0.2f + 1e-6f, n + " upkeep " + d.UpkeepGold);
                Assert.IsTrue(IsFinite(d.Desirability) && d.Desirability >= -1f && d.Desirability <= 1f, n + " desirability");
                Assert.IsTrue(IsFinite(d.VisualHeight) && d.VisualHeight > 0f && d.VisualHeight <= 4f, n + " height");
                Assert.IsTrue(IsFinite(d.StorageCapacity) && d.StorageCapacity >= 0f, n + " storage");
                Assert.IsTrue(IsFinite(d.MaxCondition) && d.MaxCondition > 0f, n + " condition");
                Assert.IsTrue(d.ServiceRadius > 0 && d.MinPopulation >= 0, n);
                if (d.Type == BuildingType.AscensionGate) Assert.AreEqual(3000f, d.BuildWork);
                else Assert.IsTrue(d.BuildWork >= 150f && d.BuildWork <= 800f, n + " buildwork " + d.BuildWork);
                var seen = new HashSet<ResourceType>();
                foreach (var c in d.Cost) Assert.IsTrue(seen.Add(c.Type), n + " duplicate cost " + c.Type);
            }
        }

        [Test]
        public void Jobs_AreConsistentWithProduction()
        {
            foreach (var d in Own(_db))
            {
                string n = d.Type.ToString();
                Assert.AreEqual(d.Job != JobType.None, d.JobSlots > 0, n);
                if (d.InputsPerWorker.Length > 0 || d.OutputsPerWorker.Length > 0) Assert.IsTrue(d.HasJobs, n + " produces without workers");
                if (d.InputsPerWorker.Length > 0) Assert.IsTrue(d.OutputsPerWorker.Length > 0 || d.Services.Length > 0, n + " consumes for nothing");
                Assert.IsTrue(d.HasJobs || d.Services.Length > 0, n + " does nothing");
            }
        }

        // ------------------------------------------------------------------ balance table

        [TestCase(BuildingType.Factory, JobType.Laborer, 6)]
        [TestCase(BuildingType.Foundry, JobType.Laborer, 4)]
        [TestCase(BuildingType.CoalMine, JobType.Miner, 4)]
        [TestCase(BuildingType.OilWell, JobType.Engineer, 2)]
        [TestCase(BuildingType.Sawmill, JobType.Laborer, 3)]
        [TestCase(BuildingType.IndustrialFarm, JobType.Farmer, 4)]
        [TestCase(BuildingType.HydroponicsTower, JobType.Engineer, 3)]
        [TestCase(BuildingType.ElectronicsPlant, JobType.Engineer, 4)]
        [TestCase(BuildingType.PowerPlant, JobType.Engineer, 3)]
        [TestCase(BuildingType.FusionReactor, JobType.Engineer, 4)]
        [TestCase(BuildingType.ResearchLab, JobType.Scholar, 6)]
        [TestCase(BuildingType.DataCenter, JobType.Engineer, 4)]
        [TestCase(BuildingType.MedicalCenter, JobType.Healer, 6)]
        [TestCase(BuildingType.Stadium, JobType.Entertainer, 4)]
        [TestCase(BuildingType.RailStation, JobType.Merchant, 3)]
        [TestCase(BuildingType.Airport, JobType.Merchant, 6)]
        [TestCase(BuildingType.MilitaryBase, JobType.Soldier, 20)]
        [TestCase(BuildingType.DeepForge, JobType.Smith, 6)]
        public void Jobs_MatchBalanceTable(BuildingType type, JobType job, int slots)
        {
            var d = _db.Building(type);
            Assert.AreEqual(job, d.Job);
            Assert.AreEqual(slots, d.JobSlots);
        }

        [TestCase(BuildingType.Factory, "Metal:0.05,Fuel:0.04", "Machinery:0.05,Goods:0.04")]
        [TestCase(BuildingType.Foundry, "Ore:0.06,Fuel:0.03", "Metal:0.09")]
        [TestCase(BuildingType.CoalMine, "", "Fuel:0.10")]
        [TestCase(BuildingType.OilWell, "", "Fuel:0.18")]
        [TestCase(BuildingType.Sawmill, "", "Wood:0.25")]
        [TestCase(BuildingType.IndustrialFarm, "", "Food:0.6")]
        [TestCase(BuildingType.HydroponicsTower, "", "Food:1.2")]
        [TestCase(BuildingType.ElectronicsPlant, "Metal:0.04,Gold:0.01", "Electronics:0.04")]
        [TestCase(BuildingType.PowerPlant, "Fuel:0.05", "")]
        [TestCase(BuildingType.FusionReactor, "", "")]
        [TestCase(BuildingType.DataCenter, "Electronics:0.01", "")]
        [TestCase(BuildingType.DeepForge, "Ore:0.05", "Metal:0.08,Tools:0.03")]
        public void ProductionFlows_MatchBalanceTable(BuildingType type, string inputs, string outputs)
        {
            var d = _db.Building(type);
            AssertFlows(inputs, d.InputsPerWorker, type + " inputs");
            AssertFlows(outputs, d.OutputsPerWorker, type + " outputs");
        }

        [TestCase(BuildingType.PowerPlant, ServiceType.Power, 20f)]
        [TestCase(BuildingType.FusionReactor, ServiceType.Power, 80f)]
        [TestCase(BuildingType.ResearchLab, ServiceType.Research, 15f)]
        [TestCase(BuildingType.DataCenter, ServiceType.Research, 30f)]
        [TestCase(BuildingType.MedicalCenter, ServiceType.Health, 25f)]
        [TestCase(BuildingType.Stadium, ServiceType.Entertainment, 15f)]
        [TestCase(BuildingType.RailStation, ServiceType.Commerce, 8f)]
        [TestCase(BuildingType.Airport, ServiceType.Commerce, 20f)]
        [TestCase(BuildingType.MilitaryBase, ServiceType.Defense, 60f)]
        [TestCase(BuildingType.Arcology, ServiceType.Administration, 20f)]
        [TestCase(BuildingType.Arcology, ServiceType.Entertainment, 10f)]
        [TestCase(BuildingType.Arcology, ServiceType.Water, 20f)]
        [TestCase(BuildingType.AscensionGate, ServiceType.Faith, 50f)]
        [TestCase(BuildingType.AscensionGate, ServiceType.Research, 50f)]
        [TestCase(BuildingType.GrandCathedral, ServiceType.Faith, 20f)]
        [TestCase(BuildingType.GrandCathedral, ServiceType.Entertainment, 5f)]
        [TestCase(BuildingType.WorldTree, ServiceType.Faith, 8f)]
        [TestCase(BuildingType.WorldTree, ServiceType.Research, 6f)]
        [TestCase(BuildingType.WorldTree, ServiceType.Health, 6f)]
        [TestCase(BuildingType.DeepForge, ServiceType.Defense, 6f)]
        [TestCase(BuildingType.WarIdol, ServiceType.Defense, 10f)]
        [TestCase(BuildingType.WarIdol, ServiceType.Faith, 4f)]
        [TestCase(BuildingType.SunPyramid, ServiceType.Faith, 10f)]
        [TestCase(BuildingType.SunPyramid, ServiceType.Research, 5f)]
        public void Services_MatchBalanceTable(BuildingType type, ServiceType service, float amount)
        {
            Assert.AreEqual(amount, ServiceOf(_db.Building(type), service), 1e-5f);
        }

        [Test]
        public void ServiceTypes_AreUniquePerBuilding()
        {
            foreach (var d in Own(_db))
            {
                var seen = new HashSet<ServiceType>();
                foreach (var s in d.Services) Assert.IsTrue(seen.Add(s.Type), d.Type + " duplicate service " + s.Type);
            }
        }

        [Test]
        public void IndustrialFarms_FeedManyPeople()
        {
            // a person eats ~0.05 Food/tick: one industrial-farm worker feeds 12, a hydroponics engineer 24
            Has(_db.Building(BuildingType.IndustrialFarm).OutputsPerWorker, ResourceType.Food, out float farm);
            Has(_db.Building(BuildingType.HydroponicsTower).OutputsPerWorker, ResourceType.Food, out float hydro);
            Assert.AreEqual(12f, farm / 0.05f, 1e-3f);
            Assert.AreEqual(24f, hydro / 0.05f, 1e-3f);
        }

        // ------------------------------------------------------------------ placement, tiers, shapes

        [Test]
        public void Placement_RulesMatchBrief()
        {
            Assert.IsTrue(Flag(BuildingType.CoalMine, PlacementRule.NearCoal));
            Assert.IsTrue(Flag(BuildingType.OilWell, PlacementRule.NearOil));
            Assert.IsTrue(Flag(BuildingType.Sawmill, PlacementRule.NearForest));
            Assert.IsTrue(Flag(BuildingType.IndustrialFarm, PlacementRule.Fertile));
            Assert.IsTrue(Flag(BuildingType.Airport, PlacementRule.Outskirts));
            Assert.IsTrue(Flag(BuildingType.ResearchLab, PlacementRule.Central));
            Assert.AreEqual(PlacementRule.None, _db.Building(BuildingType.HydroponicsTower).Placement);
            // Brief: "NearMountain or OnHills". Hard flags are ANDed by the planner, so a single flag is used (see Racial.cs).
            Assert.AreEqual(PlacementRule.NearMountain, _db.Building(BuildingType.DeepForge).Placement);
            foreach (var t in new[] { BuildingType.Factory, BuildingType.Foundry, BuildingType.ElectronicsPlant, BuildingType.PowerPlant })
            {
                Assert.IsTrue(Flag(t, PlacementRule.Isolated | PlacementRule.Outskirts), t.ToString());
                Assert.IsTrue(_db.Building(t).Desirability <= -0.6f, t + " should be undesirable heavy industry");
            }
            Assert.AreEqual(-0.8f, _db.Building(BuildingType.Factory).Desirability, 1e-5f);
            Assert.AreEqual(-0.8f, _db.Building(BuildingType.Foundry).Desirability, 1e-5f);
        }

        [Test]
        public void Placement_AtMostOneHardRequirement()
        {
            // Every hard flag (Near*/OnHills/Coastal/Fertile/NearRiver) must hold at once, so two of them would demand both
            // terrains together. Soft preferences (Central/Outskirts/Isolated) never fail placement and may be combined.
            const PlacementRule hard = PlacementRule.NearWater | PlacementRule.Coastal | PlacementRule.OnHills |
                PlacementRule.NearMountain | PlacementRule.NearForest | PlacementRule.NearOre | PlacementRule.NearStone |
                PlacementRule.NearGold | PlacementRule.NearCoal | PlacementRule.NearOil | PlacementRule.Fertile | PlacementRule.NearRiver;
            foreach (var d in Own(_db))
            {
                int bits = 0;
                for (int v = (int)(d.Placement & hard); v != 0; v &= v - 1) bits++;
                Assert.IsTrue(bits <= 1, d.Type + " combines hard placement flags " + (d.Placement & hard));
            }
        }

        [Test]
        public void MinTier_CityForAirportAndArcology()
        {
            Assert.AreEqual(SettlementTier.City, _db.Building(BuildingType.Airport).MinTier);
            Assert.AreEqual(SettlementTier.City, _db.Building(BuildingType.Arcology).MinTier);
            foreach (var d in Own(_db))
                if (d.Type != BuildingType.Airport && d.Type != BuildingType.Arcology)
                    Assert.IsTrue(d.MinTier < SettlementTier.City, d.Type + " tier");
        }

        [TestCase(BuildingType.WorldTree, BuildingShape.Tree)]
        [TestCase(BuildingType.SunPyramid, BuildingShape.Pyramid)]
        [TestCase(BuildingType.DeepForge, BuildingShape.Cave)]
        [TestCase(BuildingType.AscensionGate, BuildingShape.Monument)]
        [TestCase(BuildingType.Factory, BuildingShape.Factory)]
        [TestCase(BuildingType.Arcology, BuildingShape.Skyscraper)]
        public void Shapes_MatchArchetypes(BuildingType type, BuildingShape shape)
        {
            Assert.AreEqual(shape, _db.Building(type).Shape);
        }

        [TestCase(BuildingType.Factory, BuildingCategory.Industry)]
        [TestCase(BuildingType.CoalMine, BuildingCategory.Resource)]
        [TestCase(BuildingType.RailStation, BuildingCategory.Commerce)]
        [TestCase(BuildingType.PowerPlant, BuildingCategory.Infrastructure)]
        [TestCase(BuildingType.IndustrialFarm, BuildingCategory.Food)]
        [TestCase(BuildingType.ResearchLab, BuildingCategory.Education)]
        [TestCase(BuildingType.Stadium, BuildingCategory.Culture)]
        [TestCase(BuildingType.MilitaryBase, BuildingCategory.Military)]
        [TestCase(BuildingType.MedicalCenter, BuildingCategory.Health)]
        [TestCase(BuildingType.Arcology, BuildingCategory.Civic)]
        [TestCase(BuildingType.AscensionGate, BuildingCategory.Wonder)]
        public void Categories_MatchEnumComments(BuildingType type, BuildingCategory category)
        {
            Assert.AreEqual(category, _db.Building(type).Category);
        }

        // ------------------------------------------------------------------ economy chains

        // Transitive bootstrap: start from what earlier catalogs produce by `age`, then repeatedly build every own
        // non-racial building that is available (MinAge <= age, MinTier <= tier, no `missing` deposit flag) and whose
        // whole Cost is in stock, run it once its inputs exist, and add its outputs. Every such building must end up both
        // built and running. Stockpiles are per settlement, so the tier filter models one settlement of that tier.
        // (Without coal there is no Fuel before the Modern-age OilWell, so Industrial+NearCoal is not expected to work.)
        [TestCase(AgeId.Industrial, SettlementTier.Village, PlacementRule.None)]
        [TestCase(AgeId.Industrial, SettlementTier.Town, PlacementRule.None)]
        [TestCase(AgeId.Industrial, SettlementTier.City, PlacementRule.NearOil)]
        [TestCase(AgeId.Modern, SettlementTier.Village, PlacementRule.None)]
        [TestCase(AgeId.Modern, SettlementTier.Village, PlacementRule.NearCoal)]
        [TestCase(AgeId.Modern, SettlementTier.Town, PlacementRule.NearCoal)]
        [TestCase(AgeId.Modern, SettlementTier.City, PlacementRule.NearOil)]
        [TestCase(AgeId.Information, SettlementTier.Village, PlacementRule.None)]
        [TestCase(AgeId.Information, SettlementTier.Town, PlacementRule.NearCoal)]
        [TestCase(AgeId.Information, SettlementTier.City, PlacementRule.NearOil)]
        [TestCase(AgeId.Ascension, SettlementTier.Village, PlacementRule.NearCoal)]
        [TestCase(AgeId.Ascension, SettlementTier.Town, PlacementRule.None)]
        [TestCase(AgeId.Ascension, SettlementTier.Town, PlacementRule.NearCoal)]
        [TestCase(AgeId.Ascension, SettlementTier.Town, PlacementRule.NearOil)]
        [TestCase(AgeId.Ascension, SettlementTier.City, PlacementRule.None)]
        [TestCase(AgeId.Ascension, SettlementTier.City, PlacementRule.NearCoal)]
        [TestCase(AgeId.Ascension, SettlementTier.City, PlacementRule.NearOil)]
        public void LateChains_BootstrapPerAgeTierAndMissingDeposit(AgeId age, SettlementTier tier, PlacementRule missing)
        {
            var run = RunChains(age, tier, missing);
            Assert.IsTrue(run.Considered.Count > 0);
            foreach (var d in run.Considered)
            {
                Assert.IsTrue(run.Built.Contains(d.Type), d.Type + " can never be paid at " + age + "/" + tier + " without " + missing);
                Assert.IsTrue(run.Running.Contains(d.Type), d.Type + " can never get its inputs at " + age + "/" + tier + " without " + missing);
            }
        }

        [TestCase(PlacementRule.None)]
        [TestCase(PlacementRule.NearCoal)]
        [TestCase(PlacementRule.NearOil)]
        public void LateChains_ReachFuelMachineryAndElectronics_WithoutCoalOrWithoutOil(PlacementRule missing)
        {
            var run = RunChains(AgeId.Ascension, SettlementTier.City, missing);
            foreach (var r in new[] { ResourceType.Fuel, ResourceType.Machinery, ResourceType.Electronics })
                Assert.IsTrue(run.Available.Contains(r), r + " unreachable without " + missing);
            Assert.IsTrue(run.Built.Contains(BuildingType.AscensionGate), "Ascension Gate unreachable without " + missing);
            // the old deadlock: an OilWell must never need Machinery (only the Fuel-burning Factory makes it)
            Assert.IsFalse(Has(_db.Building(BuildingType.OilWell).Cost, ResourceType.Machinery, out _));
            Assert.IsFalse(Has(_db.Building(BuildingType.OilWell).Cost, ResourceType.Fuel, out _));
        }

        [Test]
        public void LateResourceProducers_HaveTierNoHigherThanConsumers()
        {
            var late = new[] { ResourceType.Fuel, ResourceType.Machinery, ResourceType.Electronics };
            foreach (var d in Own(_db))
                foreach (var r in late)
                {
                    if (!Has(d.Cost, r, out _) && !Has(d.InputsPerWorker, r, out _)) continue;
                    bool found = false;
                    foreach (var p in Own(_db))
                        if (p.Type != d.Type && p.AllowedRaces == RaceMask.All && p.MinAge <= d.MinAge && p.MinTier <= d.MinTier && Has(p.OutputsPerWorker, r, out _))
                            found = true;
                    Assert.IsTrue(found, d.Type + " (" + d.MinTier + ") needs " + r + " but no producer is available at that tier/age");
                }
        }

        [Test]
        public void Costs_FollowAgeMaterialRules()
        {
            // Brief: Industrial costs use Metal/Tools/Stone/Wood/Fuel; Modern adds Machinery; Information/Ascension add Electronics, Gold.
            var industrial = new HashSet<ResourceType> { ResourceType.Metal, ResourceType.Tools, ResourceType.Stone, ResourceType.Wood, ResourceType.Fuel };
            var modern = new HashSet<ResourceType>(industrial) { ResourceType.Machinery };
            var information = new HashSet<ResourceType>(modern) { ResourceType.Electronics, ResourceType.Gold };
            foreach (var d in Own(_db))
            {
                if (d.AllowedRaces != RaceMask.All) continue; // racial wonders: see Costs_PayableAtEarliestUnlockAge
                Assert.IsTrue(d.MinAge >= AgeId.Industrial, d.Type.ToString());
                var allowed = d.MinAge == AgeId.Industrial ? industrial : d.MinAge == AgeId.Modern ? modern : information;
                foreach (var c in d.Cost) Assert.IsTrue(allowed.Contains(c.Type), d.Type + " (" + d.MinAge + ") must not cost " + c.Type);
                bool hasMachinery = Has(d.Cost, ResourceType.Machinery, out _);
                bool hasElectronics = Has(d.Cost, ResourceType.Electronics, out _);
                // Exceptions: the OilWell (Fuel without coal) and the ElectronicsPlant (sole Electronics source) must not
                // need the product of a chain they start.
                if (d.MinAge >= AgeId.Modern && d.Type != BuildingType.OilWell) Assert.IsTrue(hasMachinery, d.Type + " should need machinery");
                if (d.MinAge >= AgeId.Information)
                {
                    Assert.IsTrue(Has(d.Cost, ResourceType.Gold, out _), d.Type + " should need gold");
                    if (d.Type != BuildingType.ElectronicsPlant) Assert.IsTrue(hasElectronics, d.Type + " should need electronics");
                }
            }
        }

        [Test]
        public void Costs_StayWithinStorageBudget()
        {
            Assert.IsTrue(BuildingsLate.MaxSingleCostPreIndustrial > Stockpile.DefaultCapacity && BuildingsLate.MaxSingleCostPreIndustrial < BuildingsLate.MaxSingleCost);
            foreach (var d in Own(_db))
            {
                float cap = d.MinAge < AgeId.Industrial ? BuildingsLate.MaxSingleCostPreIndustrial : BuildingsLate.MaxSingleCost;
                foreach (var c in d.Cost)
                    Assert.IsTrue(c.Amount <= cap + 1e-3f, d.Type + " costs " + c.Amount + " " + c.Type + ", more than a settlement can store (" + cap + ")");
            }
        }

        // Techs (TechEffectKind.UnlockBuilding) may make a building available one age before its MinAge; the tech tree
        // currently does so for these. An unlocked but unpayable building would stall the build queue, so each cost
        // must be payable at that age. DataCenter is deliberately absent: it needs Electronics to build and run, which
        // only exists from the Information age (ElectronicsPlant), so it cannot be made payable earlier.
        [TestCase(BuildingType.Factory, AgeId.Renaissance)]
        [TestCase(BuildingType.PowerPlant, AgeId.Industrial)]
        [TestCase(BuildingType.HydroponicsTower, AgeId.Information)]
        [TestCase(BuildingType.FusionReactor, AgeId.Information)]
        [TestCase(BuildingType.GrandCathedral, AgeId.Classical)]
        [TestCase(BuildingType.WorldTree, AgeId.Bronze)]
        [TestCase(BuildingType.SunPyramid, AgeId.Bronze)]
        [TestCase(BuildingType.DeepForge, AgeId.Dawn)]
        [TestCase(BuildingType.WarIdol, AgeId.Dawn)]
        public void Costs_PayableAtEarliestUnlockAge(BuildingType type, AgeId age)
        {
            var d = _db.Building(type);
            Assert.IsTrue(age <= d.MinAge);
            var available = RunChains(age, SettlementTier.Megalopolis, PlacementRule.None).Available;
            foreach (var c in d.Cost) Assert.IsTrue(available.Contains(c.Type), type + " unlocked in " + age + " but costs " + c.Type + " that does not exist yet");
        }

        [Test]
        public void RacialWonders_CostsAndInputsExistOneAgeBeforeMinAge()
        {
            foreach (var d in Own(_db))
            {
                if (d.AllowedRaces == RaceMask.All) continue;
                var early = EarlierCatalogResources(d.MinAge == AgeId.Dawn ? AgeId.Dawn : d.MinAge - 1);
                foreach (var c in d.Cost) Assert.IsTrue(early.Contains(c.Type), d.Type + " cost " + c.Type);
                var atMinAge = EarlierCatalogResources(d.MinAge);
                foreach (var c in d.InputsPerWorker) Assert.IsTrue(atMinAge.Contains(c.Type), d.Type + " input " + c.Type);
            }
        }

        // ------------------------------------------------------------------ the endgame wonder

        [Test]
        public void AscensionGate_IsUniqueColossalEndgameWonder()
        {
            var g = _db.Building(BuildingType.AscensionGate);
            Assert.IsTrue(g.IsWonder);
            Assert.IsTrue(g.UniquePerCivilization);
            Assert.AreEqual(4, g.Width);
            Assert.AreEqual(4, g.Height);
            Assert.AreEqual(3000f, g.BuildWork);
            Assert.AreEqual(RaceMask.All, g.AllowedRaces);
            Assert.IsTrue(Has(g.Cost, ResourceType.Electronics, out float e) && e >= 500f);
            Assert.IsTrue(Has(g.Cost, ResourceType.Machinery, out float m) && m >= 500f);
            Assert.IsTrue(Has(g.Cost, ResourceType.Gold, out float au) && au >= 500f);
            Assert.IsTrue(g.MinPopulation >= 250, "gate needs a large population");
            // Colossal through breadth and total, while each entry stays storable (Costs_StayWithinStorageBudget).
            Assert.IsTrue(g.Cost.Length >= 8, "gate should drain many resource types");
            foreach (var c in g.Cost) Assert.IsTrue(c.Amount <= BuildingsLate.MaxSingleCost + 1e-3f, "gate " + c.Type);
            float gateTotal = TotalCost(g);
            Assert.IsTrue(gateTotal >= 4000f, "gate total " + gateTotal);
            foreach (var d in Own(_db))
                if (d.Type != BuildingType.AscensionGate)
                {
                    Assert.Greater(gateTotal, TotalCost(d) * 3f, "gate must dwarf " + d.Type);
                    Assert.Greater(g.BuildWork, d.BuildWork);
                }
        }

        // ------------------------------------------------------------------ helpers

        private bool Flag(BuildingType t, PlacementRule rule) => (_db.Building(t).Placement & rule) == rule;

        /// <summary>
        /// Resources produced by the earlier catalog parts (BuildingType 0..47) by <paramref name="age"/>, per the
        /// BuildingType enum comments: Dawn gatherers/woodcutters/quarries; Bronze Mine (Ore), Smelter (Metal),
        /// Pasture/Weaver (Cloth); Iron GoldMine, Blacksmith (Tools), Armory (Weapons); Classical Workshop (Goods).
        /// </summary>
        private static HashSet<ResourceType> EarlierCatalogResources(AgeId age)
        {
            var set = new HashSet<ResourceType> { ResourceType.Food, ResourceType.Wood, ResourceType.Stone };
            if (age >= AgeId.Bronze) { set.Add(ResourceType.Ore); set.Add(ResourceType.Metal); set.Add(ResourceType.Cloth); }
            if (age >= AgeId.Iron) { set.Add(ResourceType.Gold); set.Add(ResourceType.Tools); set.Add(ResourceType.Weapons); }
            if (age >= AgeId.Classical) set.Add(ResourceType.Goods);
            return set;
        }

        private sealed class ChainRun
        {
            public HashSet<ResourceType> Available;
            public readonly List<BuildingDefinition> Considered = new List<BuildingDefinition>();
            public readonly HashSet<BuildingType> Built = new HashSet<BuildingType>();
            public readonly HashSet<BuildingType> Running = new HashSet<BuildingType>();
        }

        /// <summary>Fixed-point reachability over own non-racial buildings (see LateChains_BootstrapPerAgeTierAndMissingDeposit).</summary>
        private ChainRun RunChains(AgeId age, SettlementTier tier, PlacementRule missing)
        {
            var run = new ChainRun { Available = EarlierCatalogResources(age) };
            foreach (var d in Own(_db))
                if (d.AllowedRaces == RaceMask.All && d.MinAge <= age && d.MinTier <= tier && (d.Placement & missing) == 0)
                    run.Considered.Add(d);
            bool changed = true;
            while (changed)
            {
                changed = false;
                foreach (var d in run.Considered)
                {
                    if (!run.Built.Contains(d.Type) && AllIn(d.Cost, run.Available)) { run.Built.Add(d.Type); changed = true; }
                    if (run.Built.Contains(d.Type) && !run.Running.Contains(d.Type) && AllIn(d.InputsPerWorker, run.Available))
                    {
                        run.Running.Add(d.Type);
                        foreach (var o in d.OutputsPerWorker) run.Available.Add(o.Type);
                        changed = true;
                    }
                }
            }
            return run;
        }

        private static bool AllIn(ResourceAmount[] list, HashSet<ResourceType> available)
        {
            foreach (var r in list) if (!available.Contains(r.Type)) return false;
            return true;
        }

        private static void AssertNotShared(ResourceAmount[] a, ResourceAmount[] b, string what)
        {
            if (a.Length > 0) Assert.AreNotSame(a, b, what + " array is shared between calls");
        }

        private static void AssertSameData(BuildingDefinition a, BuildingDefinition b, string n)
        {
            Assert.AreEqual(a.Type, b.Type, n);
            Assert.AreEqual(a.Name, b.Name, n);
            Assert.AreEqual(a.Description, b.Description, n);
            Assert.AreEqual(a.Category, b.Category, n);
            Assert.AreEqual(a.Shape, b.Shape, n);
            Assert.AreEqual(a.MinAge, b.MinAge, n);
            Assert.AreEqual(a.MaxAge, b.MaxAge, n);
            Assert.AreEqual(a.AllowedRaces, b.AllowedRaces, n);
            Assert.AreEqual(a.MinTier, b.MinTier, n);
            Assert.AreEqual(a.MinPopulation, b.MinPopulation, n);
            Assert.AreEqual(a.MaxPerSettlement, b.MaxPerSettlement, n);
            Assert.AreEqual(a.UniquePerCivilization, b.UniquePerCivilization, n);
            Assert.AreEqual(a.IsWonder, b.IsWonder, n);
            Assert.AreEqual(a.Width, b.Width, n);
            Assert.AreEqual(a.Height, b.Height, n);
            AssertSameAmounts(a.Cost, b.Cost, n + " Cost");
            Assert.AreEqual(a.BuildWork, b.BuildWork, n);
            Assert.AreEqual(a.MaxCondition, b.MaxCondition, n);
            Assert.AreEqual(a.HousingMin, b.HousingMin, n);
            Assert.AreEqual(a.HousingMax, b.HousingMax, n);
            Assert.AreEqual(a.Job, b.Job, n);
            Assert.AreEqual(a.JobSlots, b.JobSlots, n);
            AssertSameAmounts(a.InputsPerWorker, b.InputsPerWorker, n + " InputsPerWorker");
            AssertSameAmounts(a.OutputsPerWorker, b.OutputsPerWorker, n + " OutputsPerWorker");
            Assert.AreEqual(a.Services.Length, b.Services.Length, n + " Services");
            for (int i = 0; i < a.Services.Length; i++)
            {
                Assert.AreEqual(a.Services[i].Type, b.Services[i].Type, n + " Services");
                Assert.AreEqual(a.Services[i].Amount, b.Services[i].Amount, n + " Services");
            }
            Assert.AreEqual(a.ServiceRadius, b.ServiceRadius, n);
            Assert.AreEqual(a.StorageCapacity, b.StorageCapacity, n);
            Assert.AreEqual(a.UpkeepGold, b.UpkeepGold, n);
            Assert.AreEqual(a.Placement, b.Placement, n);
            Assert.AreEqual(a.Desirability, b.Desirability, n);
            Assert.AreEqual(a.VisualHeight, b.VisualHeight, n);
            Assert.AreEqual(a.TintColor, b.TintColor, n);
        }

        private static void AssertSameAmounts(ResourceAmount[] a, ResourceAmount[] b, string what)
        {
            Assert.AreEqual(a.Length, b.Length, what);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreEqual(a[i].Type, b[i].Type, what);
                Assert.AreEqual(a[i].Amount, b[i].Amount, what);
            }
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static void AssertFiniteNonNegative(ResourceAmount[] list, string what)
        {
            Assert.IsNotNull(list, what);
            foreach (var r in list) Assert.IsTrue(IsFinite(r.Amount) && r.Amount > 0f, what + " " + r.Type + "=" + r.Amount);
        }

        private static void AssertFlows(string spec, ResourceAmount[] actual, string what)
        {
            var parts = spec.Length == 0 ? new string[0] : spec.Split(',');
            Assert.AreEqual(parts.Length, actual.Length, what + " count");
            foreach (var part in parts)
            {
                var kv = part.Split(':');
                var type = (ResourceType)Enum.Parse(typeof(ResourceType), kv[0]);
                float expected = float.Parse(kv[1], System.Globalization.CultureInfo.InvariantCulture);
                Assert.IsTrue(Has(actual, type, out float got), what + " missing " + type);
                Assert.AreEqual(expected, got, 1e-5f, what + " " + type);
            }
        }
    }
}
