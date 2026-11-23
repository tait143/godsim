using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Buildings;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Buildings
{
    /// <summary>Invariant and balance tests for building catalog part 1 (Dawn/Bronze/Iron, BuildingType 0..27).</summary>
    [TestFixture]
    public sealed class BuildingsEarlyTests
    {
        private const int First = 0;
        private const int Last = 27;
        private const float Eps = 1e-5f;
        private const float FoodPerPerson = 0.05f;

        /// <summary>Placement flags that are hard terrain requirements (the rest are soft preferences).</summary>
        private const PlacementRule HardPlacement =
            PlacementRule.NearWater | PlacementRule.Coastal | PlacementRule.OnHills | PlacementRule.NearMountain |
            PlacementRule.NearForest | PlacementRule.NearOre | PlacementRule.NearStone | PlacementRule.NearGold |
            PlacementRule.NearCoal | PlacementRule.NearOil | PlacementRule.Fertile | PlacementRule.NearRiver;

        private GameDatabase _db;

        [SetUp]
        public void SetUp()
        {
            _db = new GameDatabase();
            BuildingsEarly.Register(_db);
            TestDb.FillMissing(_db);
            _db.Freeze();
        }

        // ================================================================== registration

        [Test]
        public void Register_ThenFillMissing_ValidatesAndFreezes()
        {
            var db = new GameDatabase();
            BuildingsEarly.Register(db);
            TestDb.FillMissing(db);
            Assert.That(db.Validate(), Is.Empty);
            db.Freeze();
            Assert.IsTrue(db.IsFrozen);
        }

        [Test]
        public void Register_NullDatabase_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => BuildingsEarly.Register(null));
        }

        [Test]
        public void Register_FrozenDatabase_ThrowsAndChangesNothing()
        {
            var db = TestDb.CreateStub(true);
            var before = db.Buildings[(int)BuildingType.House];
            Assert.Throws<InvalidOperationException>(() => BuildingsEarly.Register(db));
            Assert.AreSame(before, db.Buildings[(int)BuildingType.House]);
        }

        [Test]
        public void Register_FillsExactlySlots0To27()
        {
            Assert.IsTrue(Counts.Buildings > Last + 1, "enum must extend past this catalog part");
            var db = new GameDatabase();
            BuildingsEarly.Register(db);
            for (int i = 0; i < Counts.Buildings; i++)
            {
                var d = db.Buildings[i];
                if (i <= Last)
                {
                    Assert.IsNotNull(d, "missing " + (BuildingType)i);
                    Assert.AreEqual((BuildingType)i, d.Type, "slot " + i + " holds the wrong type");
                    Assert.IsFalse(string.IsNullOrEmpty(d.Name), (BuildingType)i + " has no Name");
                    Assert.IsFalse(string.IsNullOrEmpty(d.Description), (BuildingType)i + " has no Description");
                }
                else
                {
                    Assert.IsNull(d, "BuildingsEarly must not register " + (BuildingType)i + " (owned by another catalog part)");
                }
            }

            // After FillMissing, slots 0..27 still hold our definitions while 28+ are stubs.
            TestDb.FillMissing(db);
            for (int i = 0; i < Counts.Buildings; i++)
            {
                var t = (BuildingType)i;
                var d = db.Buildings[i];
                if (i <= Last)
                {
                    Assert.AreNotEqual("", d.Description, t + " was replaced by a stub");
                    if (IsMultiWord(t.ToString()))
                        Assert.AreNotEqual(t.ToString(), d.Name, t + " should have a display name, not the enum identifier");
                }
                else
                {
                    Assert.AreEqual(t.ToString(), d.Name, t + " should still be the FillMissing stub");
                    Assert.AreEqual("", d.Description, t + " should still be the FillMissing stub");
                }
            }
        }

        [Test]
        public void Covers_And_TypeCount_MatchTypes0To27()
        {
            Assert.AreEqual(28, BuildingsEarly.TypeCount);
            Assert.AreEqual(BuildingType.TownCenter, BuildingsEarly.FirstType);
            Assert.AreEqual(BuildingType.Scriptorium, BuildingsEarly.LastType);
            foreach (var t in EnumUtil.AllBuildings)
                Assert.AreEqual((int)t <= Last, BuildingsEarly.Covers(t), t.ToString());

            var defs = BuildingsEarly.CreateDefinitions();
            Assert.AreEqual(28, defs.Length);
            for (int i = 0; i < defs.Length; i++) Assert.AreEqual((BuildingType)i, defs[i].Type);
        }

        [Test]
        public void CreateDefinitions_ReturnsFreshInstancesEachCall()
        {
            var a = BuildingsEarly.CreateDefinitions();
            var b = BuildingsEarly.CreateDefinitions();
            for (int i = 0; i < a.Length; i++)
            {
                Assert.AreNotSame(a[i], b[i], a[i].Type + " definition is shared");
                if (a[i].Cost.Length > 0) Assert.AreNotSame(a[i].Cost, b[i].Cost, a[i].Type + " Cost array is shared");
                if (a[i].InputsPerWorker.Length > 0) Assert.AreNotSame(a[i].InputsPerWorker, b[i].InputsPerWorker, a[i].Type + " inputs shared");
                if (a[i].OutputsPerWorker.Length > 0) Assert.AreNotSame(a[i].OutputsPerWorker, b[i].OutputsPerWorker, a[i].Type + " outputs shared");
                if (a[i].Services.Length > 0) Assert.AreNotSame(a[i].Services, b[i].Services, a[i].Type + " services shared");
            }
            a[(int)BuildingType.House].Cost[0].Amount = 999f;
            Assert.AreEqual(7f, b[(int)BuildingType.House].Cost[0].Amount, Eps); // 7 Wood + 5 Stone since the 2026-11-19 balance pass (was 12 Wood)

            var db2 = new GameDatabase();
            BuildingsEarly.Register(db2);
            Assert.AreNotSame(_db.Buildings[(int)BuildingType.Farm], db2.Buildings[(int)BuildingType.Farm]);
        }

        [Test]
        public void AllDefinitions_AreOrdinaryBuildingsForAllRaces_WithUniqueNames()
        {
            var names = new List<string>();
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                Assert.AreEqual(RaceMask.All, d.AllowedRaces, d.Type + " must be open to every race");
                Assert.IsFalse(d.IsWonder, d.Type + " is not a wonder");
                Assert.IsFalse(d.UniquePerCivilization, d.Type + " is not civ-unique");
                Assert.IsFalse(names.Contains(d.Name), "duplicate name " + d.Name);
                names.Add(d.Name);
            }
        }

        // ================================================================== housing

        [Test]
        public void House_Holds3To5_AndIsTheOnlyDwelling()
        {
            var house = _db.Building(BuildingType.House);
            Assert.AreEqual(3, house.HousingMin);
            Assert.AreEqual(5, house.HousingMax);
            Assert.IsTrue(house.IsDwelling);
            Assert.AreEqual(BuildingCategory.Housing, house.Category);
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                if (d.Type == BuildingType.House) continue;
                Assert.IsFalse(d.IsDwelling, d.Type + " must not be a dwelling");
                Assert.AreEqual(0, d.HousingMin, d.Type + " HousingMin");
                Assert.AreEqual(0, d.HousingMax, d.Type + " HousingMax");
                Assert.AreNotEqual(BuildingCategory.Housing, d.Category, d.Type + " category");
            }
        }

        [Test]
        public void House_PlacedInWorld_RollsCapacity3To5_TownCenterHousesNobody()
        {
            var ctx = TestWorlds.CreateContext(_db, 64);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 4, out _);
            var tc = ctx.State.Buildings.Get(s.TownCenterId);
            Assert.IsNotNull(tc, "FoundSettlement should place the Town Center");
            Assert.AreEqual(2, tc.Width);
            Assert.AreEqual(2, tc.Height);
            Assert.AreEqual(0, tc.HousingCapacity);
            for (int k = 0; k < 40; k++)
            {
                var site = ctx.Ops.FindFreeSite(s.Center, 1, 1, 20);
                Assert.IsTrue(site.IsValid);
                var house = ctx.Ops.CreateBuilding(s, BuildingType.House, site, 0, true);
                Assert.IsNotNull(house);
                Assert.That(house.HousingCapacity, Is.InRange(3, 5));
            }
        }

        // ================================================================== jobs & production chain

        [Test]
        public void JobBuildings_HaveSlotsAndOutputsOrServices()
        {
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                if (d.Job != JobType.None)
                {
                    Assert.IsTrue(d.JobSlots > 0, d.Type + " has a job but no slots");
                    Assert.IsTrue(d.OutputsPerWorker.Length > 0 || d.Services.Length > 0, d.Type + " employs workers who produce nothing");
                }
                else
                {
                    Assert.AreEqual(0, d.JobSlots, d.Type + " has slots but no job");
                }
                if (d.OutputsPerWorker.Length > 0 || d.InputsPerWorker.Length > 0)
                    Assert.IsTrue(d.HasJobs, d.Type + " has per-worker IO but no workers");
                if (d.InputsPerWorker.Length > 0)
                    Assert.IsTrue(d.OutputsPerWorker.Length > 0, d.Type + " consumes inputs but outputs nothing");
            }
        }

        [Test]
        public void ProductionInputs_AreProducedInEveryAgeTheConsumerExists()
        {
            for (int c = First; c <= Last; c++)
            {
                var consumer = _db.Buildings[c];
                foreach (var input in consumer.InputsPerWorker)
                    for (var age = consumer.MinAge; age <= consumer.MaxAge; age++)
                        Assert.IsTrue(HasProducer(input.Type, age, -1),
                            consumer.Type + " consumes " + input.Type + " but nothing in 0..27 produces it in " + age);
            }
        }

        [Test]
        public void Costs_AreProducibleAtMinAge_AndNeverOnlyByTheBuildingItself()
        {
            var founding = FoundingStock();
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                foreach (var cost in d.Cost)
                {
                    Assert.IsTrue(HasProducer(cost.Type, d.MinAge, -1),
                        d.Type + " costs " + cost.Type + " which no building produces by " + d.MinAge);
                    if (founding[(int)cost.Type] <= 0f)
                        Assert.IsTrue(HasProducer(cost.Type, d.MinAge, i),
                            d.Type + " costs " + cost.Type + " which only it produces (deadlock)");
                }
            }
        }

        [Test]
        public void Economy_BootstrapsFromFoundingStock_InEveryAge()
        {
            var founding = FoundingStock();
            Assert.IsTrue(founding[(int)ResourceType.Wood] > 0f && founding[(int)ResourceType.Stone] > 0f, "founding stock assumption");

            foreach (var age in EnumUtil.AllAges)
            {
                var known = new bool[Counts.Resources];
                var renewable = new bool[Counts.Resources];
                var buildable = new bool[Last + 1];
                for (int r = 0; r < Counts.Resources; r++) known[r] = founding[r] > 0f;

                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int i = First; i <= Last; i++)
                    {
                        var d = _db.Buildings[i];
                        if (!AvailableIn(d, age)) continue;
                        if (!buildable[i] && AllIn(d.Cost, known)) { buildable[i] = true; changed = true; }
                        if (!buildable[i] || !AllIn(d.InputsPerWorker, renewable)) continue;
                        foreach (var o in d.OutputsPerWorker)
                            if (!renewable[(int)o.Type]) { renewable[(int)o.Type] = true; known[(int)o.Type] = true; changed = true; }
                    }
                }

                for (int i = First; i <= Last; i++)
                {
                    var d = _db.Buildings[i];
                    if (!AvailableIn(d, age)) continue;
                    Assert.IsTrue(buildable[i], d.Type + " can never be afforded in " + age);
                    Assert.IsTrue(AllIn(d.InputsPerWorker, renewable), d.Type + " can never be supplied in " + age);
                }

                Assert.IsTrue(renewable[(int)ResourceType.Food], "no renewable Food in " + age);
                Assert.IsTrue(renewable[(int)ResourceType.Wood], "no renewable Wood in " + age);
                Assert.IsTrue(renewable[(int)ResourceType.Stone], "no renewable Stone in " + age);
                if (age >= AgeId.Bronze)
                {
                    Assert.IsTrue(renewable[(int)ResourceType.Ore], "no Ore in " + age);
                    Assert.IsTrue(renewable[(int)ResourceType.Metal], "no Metal in " + age);
                    Assert.IsTrue(renewable[(int)ResourceType.Cloth], "no Cloth in " + age);
                }
                if (age >= AgeId.Iron)
                {
                    Assert.IsTrue(renewable[(int)ResourceType.Tools], "no Tools in " + age);
                    Assert.IsTrue(renewable[(int)ResourceType.Weapons], "no Weapons in " + age);
                    Assert.IsTrue(renewable[(int)ResourceType.Gold], "no Gold in " + age);
                }
            }
        }

        // ================================================================== Dawn bootstrap

        [TestCase(BuildingType.GathererHut, JobType.Gatherer, 0.09f)]
        [TestCase(BuildingType.HuntingLodge, JobType.Hunter, 0.12f)]
        [TestCase(BuildingType.FishingHut, JobType.Fisher, 0.15f)]
        public void Dawn_FoodProducers_FeedMoreThanTheirWorkers(BuildingType type, JobType job, float food)
        {
            var d = _db.Building(type);
            Assert.AreEqual(AgeId.Dawn, d.MinAge);
            Assert.AreEqual(job, d.Job);
            Assert.AreEqual(BuildingCategory.Food, d.Category);
            Assert.AreEqual(food, AmountOf(d.OutputsPerWorker, ResourceType.Food), Eps);
            Assert.IsTrue(food > FoodPerPerson, type + " worker must out-produce own consumption");
            AssertCampBuildable(d);
            AssertCostsOnly(d, ResourceType.Wood, ResourceType.Stone);
        }

        [TestCase(BuildingType.WoodcutterCamp, JobType.Woodcutter, ResourceType.Wood, 0.12f)]
        [TestCase(BuildingType.StoneQuarry, JobType.Quarrier, ResourceType.Stone, 0.08f)]
        public void Dawn_MaterialProducers(BuildingType type, JobType job, ResourceType res, float rate)
        {
            var d = _db.Building(type);
            Assert.AreEqual(AgeId.Dawn, d.MinAge);
            Assert.AreEqual(job, d.Job);
            Assert.AreEqual(rate, AmountOf(d.OutputsPerWorker, res), Eps);
            Assert.AreEqual(0, d.InputsPerWorker.Length);
            AssertCampBuildable(d);
            AssertCostsOnly(d, ResourceType.Wood, ResourceType.Stone);
        }

        [Test]
        public void Dawn_AllCostsAreWoodAndStoneOnly_HouseCosts7Wood5Stone()
        {
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                if (d.MinAge == AgeId.Dawn) AssertCostsOnly(d, ResourceType.Wood, ResourceType.Stone);
            }
            var house = _db.Building(BuildingType.House);
            // Balance pass 2026-11-19: was 12 Wood only, which left treeless (Dwarf) sites unable to house growth.
            Assert.AreEqual(2, house.Cost.Length);
            Assert.AreEqual(ResourceType.Wood, house.Cost[0].Type);
            Assert.AreEqual(7f, house.Cost[0].Amount, Eps);
            Assert.AreEqual(ResourceType.Stone, house.Cost[1].Type);
            Assert.AreEqual(5f, house.Cost[1].Amount, Eps);
        }

        [Test]
        public void Dawn_GathererWoodcutterQuarry_FitInFoundingStock()
        {
            var founding = FoundingStock();
            var need = new float[Counts.Resources];
            AddCost(need, _db.Building(BuildingType.GathererHut));
            AddCost(need, _db.Building(BuildingType.WoodcutterCamp));
            AddCost(need, _db.Building(BuildingType.StoneQuarry));
            for (int r = 0; r < Counts.Resources; r++)
                Assert.IsTrue(need[r] <= founding[r] + Eps, "founding stock cannot pay the bootstrap " + (ResourceType)r + ": " + need[r] + " > " + founding[r]);
            Assert.AreEqual(PlacementRule.None, _db.Building(BuildingType.GathererHut).Placement & HardPlacement,
                "the gatherer hut is the food fallback and must fit any terrain");
        }

        [Test]
        public void FoodProducers_AllOutProduceTheirWorkersConsumption()
        {
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                float food = AmountOf(d.OutputsPerWorker, ResourceType.Food);
                if (food > 0f) Assert.IsTrue(food > FoodPerPerson, d.Type + " produces less food than a worker eats");
            }
        }

        // ================================================================== numbers

        [Test]
        public void AllNumbers_FiniteAndNonNegative_ArraysNonNull()
        {
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                string n = d.Type.ToString();
                AssertAmounts(d.Cost, n + ".Cost");
                AssertAmounts(d.InputsPerWorker, n + ".InputsPerWorker");
                AssertAmounts(d.OutputsPerWorker, n + ".OutputsPerWorker");
                Assert.IsNotNull(d.Services, n + ".Services");
                var seen = new bool[Counts.Services];
                foreach (var s in d.Services)
                {
                    Assert.IsTrue(IsFinite(s.Amount) && s.Amount > 0f, n + " service " + s.Type + " = " + s.Amount);
                    Assert.IsFalse(seen[(int)s.Type], n + " lists service " + s.Type + " twice");
                    seen[(int)s.Type] = true;
                }

                Assert.IsTrue(d.Cost.Length > 0, n + " must cost something");
                Assert.IsTrue(IsFinite(d.BuildWork) && d.BuildWork > 0f, n + ".BuildWork");
                Assert.IsTrue(IsFinite(d.MaxCondition) && d.MaxCondition > 0f, n + ".MaxCondition");
                Assert.IsTrue(IsFinite(d.StorageCapacity) && d.StorageCapacity >= 0f, n + ".StorageCapacity");
                Assert.IsTrue(IsFinite(d.UpkeepGold) && d.UpkeepGold >= 0f, n + ".UpkeepGold");
                Assert.IsTrue(IsFinite(d.VisualHeight) && d.VisualHeight > 0f, n + ".VisualHeight");
                Assert.IsTrue(IsFinite(d.Desirability) && d.Desirability >= -1f && d.Desirability <= 1f, n + ".Desirability");
                Assert.IsTrue(d.MinPopulation >= 0, n + ".MinPopulation");
                Assert.IsTrue(d.JobSlots >= 0, n + ".JobSlots");
                Assert.IsTrue(d.ServiceRadius > 0, n + ".ServiceRadius");
                Assert.IsTrue(d.MaxPerSettlement == -1 || d.MaxPerSettlement >= 1, n + ".MaxPerSettlement");
                Assert.IsTrue(d.Width >= 1 && d.Width <= 8 && d.Height >= 1 && d.Height <= 8, n + " footprint");
                Assert.IsTrue(d.HousingMin >= 0 && d.HousingMax >= d.HousingMin, n + " housing");
            }
        }

        // ================================================================== ages & availability

        [Test]
        public void MinAge_FollowsEnumBands_AndNeverExceedsMaxAge()
        {
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                AgeId expected = i <= (int)BuildingType.Campfire ? AgeId.Dawn
                    : i <= (int)BuildingType.Palisade ? AgeId.Bronze : AgeId.Iron;
                Assert.AreEqual(expected, d.MinAge, d.Type + " MinAge");
                Assert.IsTrue(d.MinAge <= d.MaxAge, d.Type + " MinAge > MaxAge");
            }
        }

        [TestCase(BuildingType.GathererHut, AgeId.Iron)]
        [TestCase(BuildingType.HuntingLodge, AgeId.Medieval)]
        [TestCase(BuildingType.Campfire, AgeId.Classical)]
        [TestCase(BuildingType.SpiritTotem, AgeId.Iron)]
        [TestCase(BuildingType.Palisade, AgeId.Medieval)]
        [TestCase(BuildingType.Watchtower, AgeId.Industrial)]
        public void PrimitiveBuildings_Retire(BuildingType type, AgeId maxAge)
        {
            Assert.AreEqual(maxAge, _db.Building(type).MaxAge);
        }

        [Test]
        public void NonPrimitiveBuildings_NeverRetire()
        {
            var primitive = new[]
            {
                BuildingType.GathererHut, BuildingType.HuntingLodge, BuildingType.Campfire,
                BuildingType.SpiritTotem, BuildingType.Palisade, BuildingType.Watchtower,
            };
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                if (Array.IndexOf(primitive, d.Type) >= 0) continue;
                Assert.AreEqual(AgeId.Ascension, d.MaxAge, d.Type + " should stay available");
            }
        }

        [TestCase(BuildingType.TownCenter, AgeId.Dawn, true)]
        [TestCase(BuildingType.House, AgeId.Ascension, true)]
        [TestCase(BuildingType.GathererHut, AgeId.Dawn, true)]
        [TestCase(BuildingType.GathererHut, AgeId.Iron, true)]
        [TestCase(BuildingType.GathererHut, AgeId.Classical, false)]
        [TestCase(BuildingType.HuntingLodge, AgeId.Medieval, true)]
        [TestCase(BuildingType.HuntingLodge, AgeId.Renaissance, false)]
        [TestCase(BuildingType.Campfire, AgeId.Classical, true)]
        [TestCase(BuildingType.Campfire, AgeId.Medieval, false)]
        [TestCase(BuildingType.SpiritTotem, AgeId.Iron, true)]
        [TestCase(BuildingType.SpiritTotem, AgeId.Classical, false)]
        [TestCase(BuildingType.Farm, AgeId.Dawn, false)]
        [TestCase(BuildingType.Farm, AgeId.Bronze, true)]
        [TestCase(BuildingType.Palisade, AgeId.Dawn, false)]
        [TestCase(BuildingType.Palisade, AgeId.Medieval, true)]
        [TestCase(BuildingType.Palisade, AgeId.Renaissance, false)]
        [TestCase(BuildingType.Blacksmith, AgeId.Bronze, false)]
        [TestCase(BuildingType.Blacksmith, AgeId.Iron, true)]
        [TestCase(BuildingType.Watchtower, AgeId.Bronze, false)]
        [TestCase(BuildingType.Watchtower, AgeId.Industrial, true)]
        [TestCase(BuildingType.Watchtower, AgeId.Modern, false)]
        [TestCase(BuildingType.Scriptorium, AgeId.Ascension, true)]
        public void IsBuildingAvailable_AtBoundaryAges(BuildingType type, AgeId age, bool expected)
        {
            foreach (var race in EnumUtil.AllRaces)
            {
                var civ = new Civilization { Race = race, Age = age };
                Assert.AreEqual(expected, _db.IsBuildingAvailable(civ, type), type + " for " + race + " in " + age);
            }
        }

        [Test]
        public void TechUnlock_AllowsEarlyBuild_ButNotPastMaxAge()
        {
            var early = new Civilization { Race = RaceId.Human, Age = AgeId.Dawn };
            Assert.IsFalse(_db.IsBuildingAvailable(early, BuildingType.Farm));
            early.UnlockedBuildings[(int)BuildingType.Farm] = true;
            Assert.IsTrue(_db.IsBuildingAvailable(early, BuildingType.Farm));

            var late = new Civilization { Race = RaceId.Elf, Age = AgeId.Classical };
            late.UnlockedBuildings[(int)BuildingType.GathererHut] = true;
            Assert.IsFalse(_db.IsBuildingAvailable(late, BuildingType.GathererHut));
        }

        // ================================================================== balance table

        [TestCase(BuildingType.GathererHut, JobType.Gatherer, ResourceType.Food, 0.09f)]
        [TestCase(BuildingType.HuntingLodge, JobType.Hunter, ResourceType.Food, 0.12f)]
        [TestCase(BuildingType.FishingHut, JobType.Fisher, ResourceType.Food, 0.15f)]
        [TestCase(BuildingType.Farm, JobType.Farmer, ResourceType.Food, 0.22f)]
        [TestCase(BuildingType.Pasture, JobType.Herder, ResourceType.Food, 0.10f)]
        [TestCase(BuildingType.Pasture, JobType.Herder, ResourceType.Cloth, 0.04f)]
        [TestCase(BuildingType.WoodcutterCamp, JobType.Woodcutter, ResourceType.Wood, 0.12f)]
        [TestCase(BuildingType.StoneQuarry, JobType.Quarrier, ResourceType.Stone, 0.08f)]
        [TestCase(BuildingType.Mine, JobType.Miner, ResourceType.Ore, 0.07f)]
        [TestCase(BuildingType.GoldMine, JobType.Miner, ResourceType.Gold, 0.03f)]
        [TestCase(BuildingType.Smelter, JobType.Smith, ResourceType.Metal, 0.05f)]
        [TestCase(BuildingType.Blacksmith, JobType.Smith, ResourceType.Tools, 0.04f)]
        [TestCase(BuildingType.Armory, JobType.Smith, ResourceType.Weapons, 0.03f)]
        [TestCase(BuildingType.Weaver, JobType.Weaver, ResourceType.Cloth, 0.05f)]
        public void Balance_OutputPerWorker(BuildingType type, JobType job, ResourceType res, float perWorker)
        {
            var d = _db.Building(type);
            Assert.AreEqual(job, d.Job);
            Assert.IsTrue(d.JobSlots > 0);
            Assert.AreEqual(perWorker, AmountOf(d.OutputsPerWorker, res), Eps);
        }

        [TestCase(BuildingType.Smelter, ResourceType.Ore, 0.06f, ResourceType.Ore, 0.06f)]
        [TestCase(BuildingType.Blacksmith, ResourceType.Metal, 0.04f, ResourceType.Wood, 0.02f)]
        [TestCase(BuildingType.Armory, ResourceType.Metal, 0.04f, ResourceType.Wood, 0.02f)]
        public void Balance_InputPerWorker(BuildingType type, ResourceType a, float aAmount, ResourceType b, float bAmount)
        {
            var d = _db.Building(type);
            Assert.AreEqual(a == b ? 1 : 2, d.InputsPerWorker.Length);
            Assert.AreEqual(aAmount, AmountOf(d.InputsPerWorker, a), Eps);
            Assert.AreEqual(bAmount, AmountOf(d.InputsPerWorker, b), Eps);
        }

        [TestCase(BuildingType.Farm, 4)]
        [TestCase(BuildingType.Pasture, 2)]
        [TestCase(BuildingType.Shrine, 1)]
        [TestCase(BuildingType.Temple, 2)]
        [TestCase(BuildingType.Herbalist, 1)]
        [TestCase(BuildingType.Market, 2)]
        [TestCase(BuildingType.Scriptorium, 2)]
        [TestCase(BuildingType.Watchtower, 1)]
        [TestCase(BuildingType.Barracks, 6)]
        public void Balance_JobSlots(BuildingType type, int slots)
        {
            Assert.AreEqual(slots, _db.Building(type).JobSlots);
        }

        [TestCase(BuildingType.Shrine, ServiceType.Faith, 1.0f, JobType.Priest)]
        [TestCase(BuildingType.SpiritTotem, ServiceType.Faith, 0.5f, JobType.None)]
        [TestCase(BuildingType.Temple, ServiceType.Faith, 3.0f, JobType.Priest)]
        [TestCase(BuildingType.Herbalist, ServiceType.Health, 1.5f, JobType.Healer)]
        [TestCase(BuildingType.Well, ServiceType.Water, 3.0f, JobType.None)]
        [TestCase(BuildingType.Campfire, ServiceType.Entertainment, 0.8f, JobType.None)]
        [TestCase(BuildingType.Market, ServiceType.Commerce, 2.0f, JobType.Merchant)]
        [TestCase(BuildingType.Scriptorium, ServiceType.Research, 1.2f, JobType.Scholar)]
        [TestCase(BuildingType.Palisade, ServiceType.Defense, 2.0f, JobType.None)]
        [TestCase(BuildingType.Watchtower, ServiceType.Defense, 3.0f, JobType.Soldier)]
        [TestCase(BuildingType.Barracks, ServiceType.Defense, 2.0f, JobType.Soldier)]
        public void Balance_Services(BuildingType type, ServiceType service, float amount, JobType job)
        {
            var d = _db.Building(type);
            Assert.AreEqual(1, d.Services.Length, type + " should provide exactly one service");
            Assert.AreEqual(amount, ServiceOf(d.Services, service), Eps);
            Assert.AreEqual(job, d.Job);
            if (job == JobType.None) Assert.AreEqual(0, d.JobSlots);
        }

        [Test]
        public void TownCenter_AdministersDefendsAndStores()
        {
            var d = _db.Building(BuildingType.TownCenter);
            Assert.AreEqual(BuildingCategory.Civic, d.Category);
            Assert.AreEqual(4, d.Services.Length);
            Assert.AreEqual(2f, ServiceOf(d.Services, ServiceType.Administration), Eps);
            Assert.AreEqual(1f, ServiceOf(d.Services, ServiceType.Defense), Eps);
            Assert.AreEqual(0.2f, ServiceOf(d.Services, ServiceType.Research), Eps);
            Assert.AreEqual(0.2f, ServiceOf(d.Services, ServiceType.Faith), Eps);
            Assert.AreEqual(100f, d.StorageCapacity, Eps);
            Assert.AreEqual(JobType.None, d.Job);
            Assert.AreEqual(AgeId.Dawn, d.MinAge);
            Assert.AreEqual(AgeId.Ascension, d.MaxAge);
            Assert.AreEqual(SettlementTier.Camp, d.MinTier);
            Assert.AreEqual(0, d.MinPopulation);
            Assert.AreEqual(PlacementRule.None, d.Placement & HardPlacement, "a settlement must be foundable anywhere");
        }

        [Test]
        public void StorageAndUpkeep_OnlyWhereTheTableSaysSo()
        {
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                float storage = d.Type == BuildingType.TownCenter ? 100f
                    : d.Type == BuildingType.Storehouse ? 120f
                    : d.Type == BuildingType.Granary ? 150f : 0f;
                float upkeep = d.Type == BuildingType.Market || d.Type == BuildingType.Barracks ? 0.01f : 0f;
                Assert.AreEqual(storage, d.StorageCapacity, Eps, d.Type + ".StorageCapacity");
                Assert.AreEqual(upkeep, d.UpkeepGold, Eps, d.Type + ".UpkeepGold");
            }
        }

        [TestCase(BuildingType.House, 1, 1)]
        [TestCase(BuildingType.TownCenter, 2, 2)]
        [TestCase(BuildingType.Farm, 3, 3)]
        [TestCase(BuildingType.Pasture, 3, 3)]
        [TestCase(BuildingType.Temple, 2, 2)]
        [TestCase(BuildingType.Market, 2, 2)]
        [TestCase(BuildingType.Barracks, 2, 2)]
        public void Footprints(BuildingType type, int width, int height)
        {
            var d = _db.Building(type);
            Assert.AreEqual(width, d.Width);
            Assert.AreEqual(height, d.Height);
        }

        [TestCase(BuildingType.TownCenter, 1)]
        [TestCase(BuildingType.Granary, 3)]
        [TestCase(BuildingType.Barracks, 2)]
        [TestCase(BuildingType.Market, 2)]
        [TestCase(BuildingType.Temple, 2)]
        [TestCase(BuildingType.House, -1)]
        [TestCase(BuildingType.Farm, -1)]
        public void MaxPerSettlement(BuildingType type, int max)
        {
            Assert.AreEqual(max, _db.Building(type).MaxPerSettlement);
        }

        [TestCase(BuildingType.House, 18f)]
        [TestCase(BuildingType.TownCenter, 60f)]
        [TestCase(BuildingType.Farm, 25f)]
        [TestCase(BuildingType.Temple, 80f)]
        public void BuildWork(BuildingType type, float work)
        {
            Assert.AreEqual(work, _db.Building(type).BuildWork, Eps);
        }

        [Test]
        public void BuildWork_GrowsWithAge()
        {
            float dawn = AverageWork(AgeId.Dawn, true), bronze = AverageWork(AgeId.Bronze, false), iron = AverageWork(AgeId.Iron, false);
            Assert.IsTrue(dawn < bronze && bronze < iron, "average BuildWork Dawn " + dawn + ", Bronze " + bronze + ", Iron " + iron);
        }

        [TestCase(BuildingType.Market, SettlementTier.Village, 40)]
        [TestCase(BuildingType.Temple, SettlementTier.Hamlet, 25)]
        public void TierAndPopulationGates(BuildingType type, SettlementTier tier, int population)
        {
            var d = _db.Building(type);
            Assert.AreEqual(tier, d.MinTier);
            Assert.AreEqual(population, d.MinPopulation);
        }

        [Test]
        public void CostTiers_BronzeAddsLittleMetal_IronAddsMetalAndTools()
        {
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                if (d.MinAge == AgeId.Bronze)
                {
                    AssertCostsOnly(d, ResourceType.Wood, ResourceType.Stone, ResourceType.Metal);
                    Assert.IsTrue(AmountOf(d.Cost, ResourceType.Metal) <= 5f, d.Type + " Bronze Metal cost must stay small");
                }
                else if (d.MinAge == AgeId.Iron)
                {
                    AssertCostsOnly(d, ResourceType.Wood, ResourceType.Stone, ResourceType.Metal, ResourceType.Tools);
                }
            }
            Assert.AreEqual(0f, AmountOf(_db.Building(BuildingType.Smelter).Cost, ResourceType.Metal), Eps, "Smelter is the Metal source");
            Assert.AreEqual(0f, AmountOf(_db.Building(BuildingType.Blacksmith).Cost, ResourceType.Tools), Eps, "Blacksmith is the Tools source");
        }

        // ================================================================== placement & desirability

        [TestCase(BuildingType.Farm, PlacementRule.Fertile)]
        [TestCase(BuildingType.Pasture, PlacementRule.Outskirts)]
        [TestCase(BuildingType.FishingHut, PlacementRule.NearWater)]
        [TestCase(BuildingType.WoodcutterCamp, PlacementRule.NearForest | PlacementRule.Outskirts)]
        [TestCase(BuildingType.HuntingLodge, PlacementRule.NearForest)]
        [TestCase(BuildingType.StoneQuarry, PlacementRule.NearStone)]
        [TestCase(BuildingType.Mine, PlacementRule.NearOre)]
        [TestCase(BuildingType.GoldMine, PlacementRule.NearGold)]
        [TestCase(BuildingType.Smelter, PlacementRule.Isolated)]
        [TestCase(BuildingType.Blacksmith, PlacementRule.Isolated)]
        [TestCase(BuildingType.Armory, PlacementRule.Isolated)]
        [TestCase(BuildingType.Temple, PlacementRule.Central)]
        [TestCase(BuildingType.Shrine, PlacementRule.Central)]
        [TestCase(BuildingType.Market, PlacementRule.Central)]
        [TestCase(BuildingType.Palisade, PlacementRule.Outskirts)]
        [TestCase(BuildingType.Watchtower, PlacementRule.Outskirts)]
        public void Placement_HasRequiredFlags(BuildingType type, PlacementRule flags)
        {
            Assert.AreEqual(flags, _db.Building(type).Placement & flags, type + " placement " + _db.Building(type).Placement);
        }

        [TestCase(BuildingType.Pasture)]
        [TestCase(BuildingType.House)]
        [TestCase(BuildingType.TownCenter)]
        [TestCase(BuildingType.GathererHut)]
        [TestCase(BuildingType.Well)]
        public void Placement_NoHardTerrainRequirement(BuildingType type)
        {
            Assert.AreEqual(PlacementRule.None, _db.Building(type).Placement & HardPlacement);
        }

        [Test]
        public void Placement_ResourceBuildingsNeedTheirTerrain_AndPrefsNeverContradict()
        {
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                bool central = (d.Placement & PlacementRule.Central) != 0;
                bool outskirts = (d.Placement & PlacementRule.Outskirts) != 0;
                Assert.IsFalse(central && outskirts, d.Type + " cannot prefer both Central and Outskirts");
            }
            Assert.AreEqual(PlacementRule.NearWater, _db.Building(BuildingType.FishingHut).Placement & HardPlacement,
                "FishingHut needs exactly 'near water' (coast or river), not both Coastal and NearRiver");
        }

        [Test]
        public void Desirability_IndustryNegative_ReligionAndWellsPositive()
        {
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                if (d.Category == BuildingCategory.Industry) Assert.IsTrue(d.Desirability < 0f, d.Type + " is industry");
                if (d.Category == BuildingCategory.Religion) Assert.IsTrue(d.Desirability > 0f, d.Type + " is religious");
            }
            Assert.IsTrue(_db.Building(BuildingType.Well).Desirability > 0f);
            Assert.IsTrue(_db.Building(BuildingType.StoneQuarry).Desirability < 0f);
            Assert.IsTrue(_db.Building(BuildingType.Mine).Desirability < 0f);
            Assert.IsTrue(_db.Building(BuildingType.GoldMine).Desirability < 0f);
            Assert.IsTrue(_db.Building(BuildingType.Temple).Desirability > _db.Building(BuildingType.Shrine).Desirability);
        }

        // ================================================================== helpers

        private static bool IsMultiWord(string identifier)
        {
            for (int c = 1; c < identifier.Length; c++) if (char.IsUpper(identifier[c])) return true;
            return false;
        }

        private static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static bool AvailableIn(BuildingDefinition d, AgeId age) => d.MinAge <= age && age <= d.MaxAge;

        /// <summary>True if some building in 0..27 (other than <paramref name="excludeIndex"/>) outputs <paramref name="res"/> in <paramref name="age"/>.</summary>
        private bool HasProducer(ResourceType res, AgeId age, int excludeIndex)
        {
            for (int p = First; p <= Last; p++)
            {
                if (p == excludeIndex) continue;
                var d = _db.Buildings[p];
                if (AvailableIn(d, age) && AmountOf(d.OutputsPerWorker, res) > 0f) return true;
            }
            return false;
        }

        private static bool AllIn(ResourceAmount[] amounts, bool[] set)
        {
            for (int i = 0; i < amounts.Length; i++) if (!set[(int)amounts[i].Type]) return false;
            return true;
        }

        private static float AmountOf(ResourceAmount[] amounts, ResourceType type)
        {
            float sum = 0f;
            for (int i = 0; i < amounts.Length; i++) if (amounts[i].Type == type) sum += amounts[i].Amount;
            return sum;
        }

        private static float ServiceOf(ServiceAmount[] services, ServiceType type)
        {
            float sum = 0f;
            for (int i = 0; i < services.Length; i++) if (services[i].Type == type) sum += services[i].Amount;
            return sum;
        }

        private static void AddCost(float[] need, BuildingDefinition d)
        {
            foreach (var c in d.Cost) need[(int)c.Type] += c.Amount;
        }

        private static void AssertAmounts(ResourceAmount[] amounts, string what)
        {
            Assert.IsNotNull(amounts, what + " is null");
            var seen = new bool[Counts.Resources];
            foreach (var a in amounts)
            {
                Assert.IsTrue(IsFinite(a.Amount) && a.Amount > 0f, what + " " + a.Type + " = " + a.Amount);
                Assert.IsFalse(seen[(int)a.Type], what + " lists " + a.Type + " twice");
                seen[(int)a.Type] = true;
            }
        }

        private static void AssertCostsOnly(BuildingDefinition d, params ResourceType[] allowed)
        {
            foreach (var c in d.Cost)
                Assert.IsTrue(Array.IndexOf(allowed, c.Type) >= 0, d.Type + " (" + d.MinAge + ") must not cost " + c.Type);
        }

        private static void AssertCampBuildable(BuildingDefinition d)
        {
            Assert.AreEqual(SettlementTier.Camp, d.MinTier, d.Type + " must be buildable by a fresh camp");
            Assert.AreEqual(0, d.MinPopulation, d.Type + " must be buildable by a fresh camp");
        }

        private float AverageWork(AgeId age, bool excludeTownCenter)
        {
            float sum = 0f;
            int n = 0;
            for (int i = First; i <= Last; i++)
            {
                var d = _db.Buildings[i];
                if (d.MinAge != age || (excludeTownCenter && d.Type == BuildingType.TownCenter)) continue;
                sum += d.BuildWork;
                n++;
            }
            return n > 0 ? sum / n : 0f;
        }

        /// <summary>The stockpile a settlement starts with, read from the real WorldOps.FoundSettlement.</summary>
        private float[] FoundingStock()
        {
            var ctx = TestWorlds.CreateContext(_db, 64);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 0, out _);
            var stock = new float[Counts.Resources];
            for (int r = 0; r < Counts.Resources; r++) stock[r] = s.Stockpile.Get((ResourceType)r);
            return stock;
        }
    }
}
