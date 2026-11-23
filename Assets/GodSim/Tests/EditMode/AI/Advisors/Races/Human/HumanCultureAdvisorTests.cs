using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Core.Races;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class HumanCultureAdvisorTests
    {
        // ------------------------------------------------------------------ fixtures
        private static AgeId IntendedAge(BuildingType t)
        {
            int i = (int)t;
            if (t == BuildingType.GrandCathedral || t == BuildingType.GrandBazaar) return AgeId.Medieval;
            if (t == BuildingType.WorldTree || t == BuildingType.SunPyramid) return AgeId.Iron;
            if (t == BuildingType.DeepForge || t == BuildingType.WarIdol) return AgeId.Bronze;
            if (t == BuildingType.GreatLibrary || t == BuildingType.Colossus) return AgeId.Classical;
            if (i <= 10) return AgeId.Dawn;
            if (i <= 19) return AgeId.Bronze;
            if (i <= 27) return AgeId.Iron;
            if (i <= 36) return AgeId.Classical;
            if (i <= 42) return AgeId.Medieval;
            if (i <= 47) return AgeId.Renaissance;
            if (i <= 52) return AgeId.Industrial;
            if (i <= 58) return AgeId.Modern;
            if (i <= 62) return AgeId.Information;
            return AgeId.Ascension;
        }

        private static BuildingDefinition Def(BuildingType t, BuildingCategory cat, JobType job = JobType.None, ResourceType? output = null)
        {
            var d = new BuildingDefinition { Type = t, Name = t.ToString(), Category = cat, MinAge = IntendedAge(t), BuildWork = 10f };
            if (job != JobType.None) { d.Job = job; d.JobSlots = 2; }
            if (output.HasValue) d.OutputsPerWorker = new[] { new ResourceAmount(output.Value, 0.1f) };
            return d;
        }

        /// <summary>A miniature, age-correct catalog of the buildings the advisor reasons about.</summary>
        private static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            RaceCatalog.Register(db);
            db.Register(new BuildingDefinition { Type = BuildingType.House, Name = "House", Category = BuildingCategory.Housing, HousingMin = 3, HousingMax = 5, BuildWork = 10f });
            db.Register(new BuildingDefinition { Type = BuildingType.TownCenter, Name = "TownCenter", Category = BuildingCategory.Civic, MaxPerSettlement = 1, Width = 2, Height = 2, BuildWork = 10f });
            db.Register(new BuildingDefinition { Type = BuildingType.GathererHut, Name = "GathererHut", Category = BuildingCategory.Food, Job = JobType.Gatherer, JobSlots = 2, MaxAge = AgeId.Iron, BuildWork = 10f, OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Food, 0.1f) } });
            db.Register(Def(BuildingType.HuntingLodge, BuildingCategory.Food, JobType.Hunter, ResourceType.Food));
            db.Register(Def(BuildingType.WoodcutterCamp, BuildingCategory.Resource, JobType.Woodcutter, ResourceType.Wood));
            db.Register(Def(BuildingType.StoneQuarry, BuildingCategory.Resource, JobType.Quarrier, ResourceType.Stone));
            db.Register(Def(BuildingType.Storehouse, BuildingCategory.Storage));
            db.Register(Def(BuildingType.SpiritTotem, BuildingCategory.Religion));
            db.Register(Def(BuildingType.Farm, BuildingCategory.Food, JobType.Farmer, ResourceType.Food));
            db.Register(Def(BuildingType.Granary, BuildingCategory.Storage));
            db.Register(Def(BuildingType.Shrine, BuildingCategory.Religion));
            db.Register(new BuildingDefinition { Type = BuildingType.Palisade, Name = "Palisade", Category = BuildingCategory.Military, Shape = BuildingShape.Wall, MinAge = AgeId.Bronze, BuildWork = 10f });
            db.Register(Def(BuildingType.Blacksmith, BuildingCategory.Industry, JobType.Smith, ResourceType.Tools));
            db.Register(Def(BuildingType.Temple, BuildingCategory.Religion, JobType.Priest));
            db.Register(Def(BuildingType.Barracks, BuildingCategory.Military, JobType.Soldier));
            db.Register(new BuildingDefinition { Type = BuildingType.Market, Name = "Market", Category = BuildingCategory.Commerce, MinAge = AgeId.Iron, MinTier = SettlementTier.Village, MaxPerSettlement = 2, Job = JobType.Merchant, JobSlots = 2, BuildWork = 10f });
            db.Register(Def(BuildingType.GoldMine, BuildingCategory.Resource, JobType.Miner, ResourceType.Gold));
            db.Register(Def(BuildingType.Workshop, BuildingCategory.Industry, JobType.Craftsman, ResourceType.Goods));
            db.Register(new BuildingDefinition { Type = BuildingType.StoneWall, Name = "StoneWall", Category = BuildingCategory.Military, Shape = BuildingShape.Wall, MinAge = AgeId.Classical, BuildWork = 10f });
            db.Register(new BuildingDefinition { Type = BuildingType.Castle, Name = "Castle", Category = BuildingCategory.Military, MinAge = AgeId.Medieval, MaxPerSettlement = 1, BuildWork = 10f });
            db.Register(new BuildingDefinition { Type = BuildingType.Cathedral, Name = "Cathedral", Category = BuildingCategory.Religion, MinAge = AgeId.Medieval, MinTier = SettlementTier.Town, BuildWork = 10f });
            db.Register(Def(BuildingType.Mill, BuildingCategory.Food));
            db.Register(Def(BuildingType.Guildhall, BuildingCategory.Commerce, JobType.Merchant));
            db.Register(Def(BuildingType.Bank, BuildingCategory.Commerce, JobType.Merchant));
            db.Register(Def(BuildingType.Factory, BuildingCategory.Industry, JobType.Laborer, ResourceType.Machinery));
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.GrandCathedral, Name = "GrandCathedral", Category = BuildingCategory.Wonder, IsWonder = true, UniquePerCivilization = true,
                AllowedRaces = RaceMask.Human, MinAge = AgeId.Medieval, MinTier = SettlementTier.Town, BuildWork = 700f,
                Cost = new[] { new ResourceAmount(ResourceType.Wood, 150f), new ResourceAmount(ResourceType.Stone, 400f), new ResourceAmount(ResourceType.Gold, 120f) },
            });
            // Everything else: late-age civic stubs, so they never interfere with early-age tests.
            for (int i = 0; i < Counts.Buildings; i++)
                if (db.Buildings[i] == null)
                    db.Register(new BuildingDefinition { Type = (BuildingType)i, Name = ((BuildingType)i).ToString(), Category = BuildingCategory.Civic, MinAge = AgeId.Ascension, BuildWork = 10f });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static SimContext NewWorld(RaceId race, AgeId age, SettlementTier tier, out Civilization civ, out Settlement s, int adults = 30)
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            s = TestWorlds.AddSettlement(ctx, race, new Vec2i(32, 32), adults, out civ);
            civ.Age = age;
            s.Tier = tier;
            return ctx;
        }

        private static void RunOnce(SimContext ctx) => TestWorlds.Run(new HumanCultureAdvisor(), ctx, 24);

        private static float Bias(Settlement s, BuildingType t) => AdvisorBoard.BuildBias(s, t);

        // ------------------------------------------------------------------ tests
        [Test]
        public void Identity()
        {
            var a = new HumanCultureAdvisor();
            Assert.AreEqual("race_human", a.AdvisorId);
            Assert.AreEqual(24, a.TickInterval);
        }

        [Test]
        public void HumanVillage_VotesMarketFirst()
        {
            var ctx = NewWorld(RaceId.Human, AgeId.Iron, SettlementTier.Village, out _, out var s);
            RunOnce(ctx);
            float market = Bias(s, BuildingType.Market);
            Assert.That(market, Is.GreaterThan(0.9f));
            for (int i = 0; i < Counts.Buildings; i++)
                if ((BuildingType)i != BuildingType.Market)
                    Assert.That(Bias(s, (BuildingType)i), Is.LessThan(market), "Market must lead, but " + (BuildingType)i + " was higher");
            Assert.That(AdvisorBoard.JobBias(s, JobType.Merchant), Is.GreaterThan(0f));
            Assert.That(AdvisorBoard.JobBias(s, JobType.Farmer), Is.GreaterThan(0f));
            Assert.That(AdvisorBoard.JobBias(s, JobType.Builder), Is.GreaterThan(0f));
        }

        [Test]
        public void HumanHamlet_DoesNotVoteMarketBeforeVillageTier()
        {
            var ctx = NewWorld(RaceId.Human, AgeId.Iron, SettlementTier.Hamlet, out _, out var s);
            RunOnce(ctx);
            Assert.AreEqual(0f, Bias(s, BuildingType.Market));
        }

        [Test]
        public void FarmsPreferredOverGatherers_OnceAvailable()
        {
            var ctx = NewWorld(RaceId.Human, AgeId.Bronze, SettlementTier.Hamlet, out _, out var s);
            RunOnce(ctx);
            Assert.That(Bias(s, BuildingType.Farm), Is.GreaterThan(0.5f));
            Assert.That(Bias(s, BuildingType.GathererHut), Is.LessThan(0f));
            Assert.That(Bias(s, BuildingType.Farm), Is.GreaterThan(Bias(s, BuildingType.GathererHut)));
        }

        [Test]
        public void DawnAge_GatherersNotSuppressedWithoutFarms()
        {
            var ctx = NewWorld(RaceId.Human, AgeId.Dawn, SettlementTier.Camp, out _, out var s);
            RunOnce(ctx);
            Assert.AreEqual(0f, Bias(s, BuildingType.GathererHut));
            Assert.AreEqual(0f, Bias(s, BuildingType.Farm));
        }

        [Test]
        public void MedievalTown_CathedralCastleMillAmbition_AndGrandCathedralStockpile()
        {
            var ctx = NewWorld(RaceId.Human, AgeId.Medieval, SettlementTier.Town, out var civ, out var s);
            s.IsCapital = true; civ.CapitalId = s.Id;
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Farm });
            RunOnce(ctx);
            Assert.That(Bias(s, BuildingType.Cathedral), Is.GreaterThan(0.5f));
            Assert.That(Bias(s, BuildingType.Castle), Is.GreaterThan(0.5f));
            Assert.That(Bias(s, BuildingType.Mill), Is.GreaterThan(0.5f));
            Assert.That(Bias(s, BuildingType.StoneQuarry), Is.GreaterThan(0.5f), "capital stockpiles stone for the Grand Cathedral");
            Assert.That(Bias(s, BuildingType.GoldMine), Is.GreaterThan(0.5f));
            Assert.That(Bias(s, BuildingType.Storehouse), Is.GreaterThan(0f), "400 stone does not fit the default stockpile");
        }

        [Test]
        public void GrandCathedralStockpile_SkippedWhenStrategyAlreadyQueuedIt()
        {
            var ctx = NewWorld(RaceId.Human, AgeId.Medieval, SettlementTier.Town, out var civ, out var s);
            s.IsCapital = true; civ.CapitalId = s.Id;
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.GrandCathedral, Priority = 6f, Reason = "wonder: Grand Cathedral" });
            RunOnce(ctx);
            Assert.AreEqual(0f, Bias(s, BuildingType.StoneQuarry));
            Assert.AreEqual(0f, Bias(s, BuildingType.GoldMine));
        }

        [Test]
        public void IndustrialAge_FactoriesAdoptedEarly()
        {
            var ctx = NewWorld(RaceId.Human, AgeId.Industrial, SettlementTier.Town, out _, out var s);
            RunOnce(ctx);
            Assert.That(Bias(s, BuildingType.Factory), Is.GreaterThan(1f));
        }

        [Test]
        public void AfterWar_WallsAndBarracksResolutely()
        {
            var ctx = NewWorld(RaceId.Human, AgeId.Classical, SettlementTier.Village, out var civ, out var s);
            ctx.State.Tick = 0;
            ctx.State.Diplomacy.Wars.Add(new War { Id = 1, AttackerId = civ.Id + 100, DefenderId = civ.Id, StartTick = 0, EndTick = 10, IsActive = false });
            RunOnce(ctx);
            Assert.That(Bias(s, BuildingType.StoneWall), Is.GreaterThan(0.6f));
            Assert.That(Bias(s, BuildingType.Barracks), Is.GreaterThan(0.6f));
            Assert.That(AdvisorBoard.JobBias(s, JobType.Soldier), Is.GreaterThan(0f));
        }

        [Test]
        public void Famine_FaithRisesAlongsideFarms()
        {
            var calm = NewWorld(RaceId.Human, AgeId.Iron, SettlementTier.Hamlet, out _, out var s0);
            RunOnce(calm);
            var hungry = NewWorld(RaceId.Human, AgeId.Iron, SettlementTier.Hamlet, out _, out var s1);
            s1.Memory[HumanCultureAdvisor.FoodDaysKey] = 3f;
            RunOnce(hungry);
            Assert.That(Bias(s1, BuildingType.Temple), Is.GreaterThan(Bias(s0, BuildingType.Temple) + 0.3f));
            Assert.That(Bias(s1, BuildingType.Farm), Is.GreaterThan(Bias(s0, BuildingType.Farm)));
            Assert.That(AdvisorBoard.JobBias(s1, JobType.Priest), Is.GreaterThan(0f));
        }

        [Test]
        public void PunishedByGod_DefiantAtTheAltar()
        {
            var ctx = NewWorld(RaceId.Human, AgeId.Iron, SettlementTier.Hamlet, out var civ, out var s);
            civ.GodFavor = -0.9f;
            RunOnce(ctx);
            Assert.That(Bias(s, BuildingType.Temple), Is.LessThan(0f));
            Assert.That(AdvisorBoard.JobBias(s, JobType.Priest), Is.LessThanOrEqualTo(0f));
        }

        [TestCase(RaceId.Elf)]
        [TestCase(RaceId.Dwarf)]
        [TestCase(RaceId.Orc)]
        [TestCase(RaceId.Saurian)]
        public void NonHumanSettlements_AreNeverTouched(RaceId race)
        {
            var ctx = NewWorld(race, AgeId.Medieval, SettlementTier.Town, out var civ, out var s);
            s.Memory[HumanCultureAdvisor.FoodDaysKey] = 1f;
            civ.GodFavor = 0.9f;
            var before = new Dictionary<string, float>(s.Memory);
            int log = s.AiLog.Count;
            RunOnce(ctx);
            Assert.AreEqual(before.Count, s.Memory.Count);
            foreach (var kv in before) Assert.AreEqual(kv.Value, s.Memory[kv.Key]);
            Assert.AreEqual(log, s.AiLog.Count);
        }

        [Test]
        public void Deterministic_SameInputsSameVotes()
        {
            string a = Snapshot(), b = Snapshot();
            Assert.AreEqual(a, b);
            Assert.That(a.Length, Is.GreaterThan(0));
        }

        private static string Snapshot()
        {
            var ctx = NewWorld(RaceId.Human, AgeId.Medieval, SettlementTier.Town, out var civ, out var s);
            s.Memory[HumanCultureAdvisor.FoodDaysKey] = 12f;
            civ.GodFavor = 0.5f;
            TestWorlds.Run(new HumanCultureAdvisor(), ctx, 24 * 5);
            var keys = new List<string>(s.Memory.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new System.Text.StringBuilder();
            foreach (var k in keys)
            {
                float v = s.Memory[k];
                Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), k);
                sb.Append(k).Append('=').Append(v.ToString("R")).Append(';');
            }
            return sb.ToString();
        }
    }
}
