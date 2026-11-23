using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Advisors.Happiness
{
    public class HappinessAdvisorTests
    {
        private static ResourceAmount R(ResourceType t, float a) => new ResourceAmount(t, a);
        private static ServiceAmount S(ServiceType t, float a) => new ServiceAmount(t, a);

        private static readonly BuildingType[] CultureTypes = { BuildingType.Campfire, BuildingType.Theater, BuildingType.Arena, BuildingType.Tavern, BuildingType.Stadium };

        private static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.TownCenter, Name = "Town Center", MaxPerSettlement = 1, Width = 2, Height = 2, Services = new[] { S(ServiceType.Administration, 2f) } });
            db.Register(new BuildingDefinition { Type = BuildingType.House, Name = "House", Category = BuildingCategory.Housing, HousingMin = 3, HousingMax = 5, Cost = new[] { R(ResourceType.Wood, 10f) }, BuildWork = 18f });
            db.Register(new BuildingDefinition { Type = BuildingType.Campfire, Name = "Campfire", Category = BuildingCategory.Culture, MaxAge = AgeId.Classical, Cost = new[] { R(ResourceType.Wood, 6f) }, BuildWork = 6f, Services = new[] { S(ServiceType.Entertainment, 0.8f) }, Placement = PlacementRule.Central, Desirability = 0.3f });
            db.Register(new BuildingDefinition { Type = BuildingType.Theater, Name = "Theater", Category = BuildingCategory.Culture, MinAge = AgeId.Classical, MinTier = SettlementTier.Village, MinPopulation = 25, Width = 2, Height = 2, Cost = new[] { R(ResourceType.Wood, 25f), R(ResourceType.Stone, 35f), R(ResourceType.Cloth, 8f) }, BuildWork = 130f, Job = JobType.Entertainer, JobSlots = 2, Services = new[] { S(ServiceType.Entertainment, 4f) }, Placement = PlacementRule.Central, Desirability = 0.4f });
            db.Register(new BuildingDefinition { Type = BuildingType.Arena, Name = "Arena", Category = BuildingCategory.Culture, MinAge = AgeId.Classical, MinTier = SettlementTier.Village, MinPopulation = 45, Width = 3, Height = 3, Cost = new[] { R(ResourceType.Stone, 90f), R(ResourceType.Wood, 30f), R(ResourceType.Tools, 8f), R(ResourceType.Gold, 20f) }, BuildWork = 260f, Job = JobType.Entertainer, JobSlots = 3, Services = new[] { S(ServiceType.Entertainment, 6f) }, Placement = PlacementRule.Outskirts, Desirability = 0.1f });
            db.Register(new BuildingDefinition { Type = BuildingType.Tavern, Name = "Tavern", Category = BuildingCategory.Culture, MinAge = AgeId.Medieval, MinTier = SettlementTier.Hamlet, MinPopulation = 10, Width = 2, Height = 2, Cost = new[] { R(ResourceType.Wood, 35f), R(ResourceType.Stone, 10f), R(ResourceType.Goods, 4f) }, BuildWork = 80f, Job = JobType.Merchant, JobSlots = 1, Services = new[] { S(ServiceType.Entertainment, 3f), S(ServiceType.Commerce, 1f) }, Desirability = 0.1f });
            db.Register(new BuildingDefinition { Type = BuildingType.Stadium, Name = "Stadium", Category = BuildingCategory.Culture, MinAge = AgeId.Modern, MinTier = SettlementTier.Town, MinPopulation = 100, MaxPerSettlement = 1, Width = 3, Height = 3, Cost = new[] { R(ResourceType.Stone, 200f), R(ResourceType.Metal, 80f) }, BuildWork = 600f, Job = JobType.Entertainer, JobSlots = 4, Services = new[] { S(ServiceType.Entertainment, 15f) }, Placement = PlacementRule.Outskirts, Desirability = 0.2f });
            db.Register(new BuildingDefinition { Type = BuildingType.GathererHut, Name = "Gatherer's Hut", Category = BuildingCategory.Food, MaxAge = AgeId.Iron, Job = JobType.Gatherer, JobSlots = 3, OutputsPerWorker = new[] { R(ResourceType.Food, 0.09f) }, Cost = new[] { R(ResourceType.Wood, 8f) } });
            db.Register(new BuildingDefinition { Type = BuildingType.Farm, Name = "Farm", Category = BuildingCategory.Food, MinAge = AgeId.Bronze, Width = 2, Height = 2, Job = JobType.Farmer, JobSlots = 4, OutputsPerWorker = new[] { R(ResourceType.Food, 0.2f) }, Cost = new[] { R(ResourceType.Wood, 15f) }, Placement = PlacementRule.Fertile });
            db.Register(new BuildingDefinition { Type = BuildingType.SpiritTotem, Name = "Spirit Totem", Category = BuildingCategory.Religion, MaxAge = AgeId.Iron, Services = new[] { S(ServiceType.Faith, 0.6f) }, Cost = new[] { R(ResourceType.Wood, 5f) } });
            db.Register(new BuildingDefinition { Type = BuildingType.Shrine, Name = "Shrine", Category = BuildingCategory.Religion, MinAge = AgeId.Bronze, Job = JobType.Priest, JobSlots = 1, Services = new[] { S(ServiceType.Faith, 2f) }, Cost = new[] { R(ResourceType.Stone, 15f) } });
            db.Register(new BuildingDefinition { Type = BuildingType.Forum, Name = "Forum", Category = BuildingCategory.Civic, MinAge = AgeId.Classical, Services = new[] { S(ServiceType.Administration, 6f) }, Cost = new[] { R(ResourceType.Stone, 60f) } });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Settlement Make(SimContext ctx, RaceId race, AgeId age, int adults, Vec2i at, out Civilization civ)
        {
            var s = TestWorlds.AddSettlement(ctx, race, at, adults, out civ);
            civ.Age = age;
            s.Tier = SettlementTier.Village;
            s.Stockpile.SetRaw(ResourceType.Food, 140f);
            return s;
        }

        private static void Set(Settlement s, float ent, float faith, float happiness, float unrest, int homeless = 0)
        {
            int pop = s.Population;
            s.ServiceCoverage[(int)ServiceType.Entertainment] = ent;
            s.ServiceOutput[(int)ServiceType.Entertainment] = ent * pop * 0.05f;
            s.ServiceCoverage[(int)ServiceType.Faith] = faith;
            s.ServiceOutput[(int)ServiceType.Faith] = faith * pop * 0.05f;
            s.Happiness = happiness;
            s.Unrest = unrest;
            s.Homeless = homeless;
        }

        private static void RunOnce(HappinessAdvisor adv, SimContext ctx, bool init) => TestWorlds.Run(adv, ctx, 24, init);

        private static float CultureBias(Settlement s)
        {
            float v = 0f;
            for (int i = 0; i < CultureTypes.Length; i++) v += AdvisorBoard.BuildBias(s, CultureTypes[i]);
            return v;
        }

        private static string Log(Settlement s) => string.Join("\n", s.AiLog);

        [Test]
        public void RisingUnrest_GivesUrgentCultureAndEntertainerVotes()
        {
            var db = CreateDb();
            var ctxA = TestWorlds.CreateContext(db);
            var rising = Make(ctxA, RaceId.Human, AgeId.Classical, 30, new Vec2i(20, 20), out _);
            var advA = new HappinessAdvisor();
            Set(rising, 0.5f, 1f, 0.45f, 0.05f);
            RunOnce(advA, ctxA, true);
            Set(rising, 0.5f, 1f, 0.3f, 0.3f);
            RunOnce(advA, ctxA, false);

            var ctxB = TestWorlds.CreateContext(db);
            var stable = Make(ctxB, RaceId.Human, AgeId.Classical, 30, new Vec2i(20, 20), out _);
            var advB = new HappinessAdvisor();
            Set(stable, 0.5f, 1f, 0.45f, 0.05f);
            RunOnce(advB, ctxB, true);
            RunOnce(advB, ctxB, false);

            Assert.Greater(CultureBias(rising), CultureBias(stable), "rising unrest must push culture harder");
            Assert.Greater(AdvisorBoard.BuildBias(rising, BuildingType.Theater), 0.8f, "humans answer unrest with a theater");
            Assert.Greater(AdvisorBoard.JobBias(rising, JobType.Entertainer), 0f, "entertainers are called for");
            Assert.AreEqual(0f, AdvisorBoard.JobBias(stable, JobType.Entertainer));
            Assert.Greater(AdvisorBoard.Urgency(rising, HappinessAdvisor.Id), 0.8f);
            Assert.Greater(rising.Mem(HappinessAdvisorKeys.UnrestTrend), HappinessAdvisor.RisingTrend);
            StringAssert.Contains("rising", Log(rising));
            Assert.Greater(CultureBias(stable), 0f, "coverage 50% still earns a calm culture vote");
        }

        [Test]
        public void HousingCause_VotesHouseOverMoreTheaters()
        {
            var db = CreateDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = Make(ctx, RaceId.Human, AgeId.Classical, 20, new Vec2i(20, 20), out _);
            Set(s, 0.6f, 1.2f, 0.4f, 0.1f, homeless: 6);
            RunOnce(new HappinessAdvisor(), ctx, true);

            float house = AdvisorBoard.BuildBias(s, BuildingType.House);
            Assert.Greater(house, 0.5f);
            Assert.Greater(house, CultureBias(s), "the cause is housing, not a lack of theaters");
            Assert.AreEqual((float)(int)HappinessCause.Housing, s.Mem(HappinessAdvisorKeys.Cause));
            StringAssert.Contains("Unrest from homelessness: 6 without a roof", Log(s));
        }

        [Test]
        public void HousingAlreadyUnderConstruction_DoesNotVoteMoreHouses()
        {
            var db = CreateDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = Make(ctx, RaceId.Human, AgeId.Classical, 20, new Vec2i(20, 20), out _);
            Set(s, 1.5f, 1.5f, 0.45f, 0.05f, homeless: 3);
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.House, Priority = 1f });
            RunOnce(new HappinessAdvisor(), ctx, true);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.House), "a queued house already covers 3 homeless");
            Assert.AreEqual(1, s.BuildQueue.Count, "advisor never edits the build queue");
        }

        [Test]
        public void FoodCause_VotesFood()
        {
            var db = CreateDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = Make(ctx, RaceId.Human, AgeId.Bronze, 20, new Vec2i(20, 20), out _);
            Set(s, 1.5f, 1.5f, 0.4f, 0.1f);
            s.Stockpile.SetRaw(ResourceType.Food, 5f);
            RunOnce(new HappinessAdvisor(), ctx, true);
            float food = AdvisorBoard.BuildBias(s, BuildingType.GathererHut) + AdvisorBoard.BuildBias(s, BuildingType.Farm);
            Assert.Greater(food, 0.4f);
            Assert.AreEqual((float)(int)HappinessCause.Food, s.Mem(HappinessAdvisorKeys.Cause));
        }

        [Test]
        public void RemoteSettlement_IsMoreRestlessThanCentral()
        {
            var db = CreateDb();
            var ctx = TestWorlds.CreateContext(db);
            var capital = Make(ctx, RaceId.Human, AgeId.Classical, 30, new Vec2i(8, 8), out var civ);
            var remote = ctx.Ops.FoundSettlement(civ, new Vec2i(55, 55));
            for (int i = 0; i < 30; i++) ctx.Ops.CreatePerson(remote, RaceId.Human, i % 2 == 0 ? Sex.Female : Sex.Male, 20f + (i % 10));
            remote.Tier = SettlementTier.Village;
            remote.Stockpile.SetRaw(ResourceType.Food, 140f);
            Set(capital, 0.4f, 1f, 0.45f, 0.1f);
            Set(remote, 0.4f, 1f, 0.45f, 0.1f);
            RunOnce(new HappinessAdvisor(), ctx, true);

            Assert.IsTrue(capital.IsCapital);
            Assert.Greater(remote.Mem(HappinessAdvisorKeys.Remote), 0.5f);
            Assert.AreEqual(0f, capital.Mem(HappinessAdvisorKeys.Remote));
            Assert.Greater(CultureBias(remote), CultureBias(capital));
            Assert.Greater(AdvisorBoard.Urgency(remote, HappinessAdvisor.Id), AdvisorBoard.Urgency(capital, HappinessAdvisor.Id));
            Assert.Greater(remote.Mem(HappinessAdvisorKeys.Risk), capital.Mem(HappinessAdvisorKeys.Risk));
        }

        [Test]
        public void FullCoverage_GivesNoVotes()
        {
            var db = CreateDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = Make(ctx, RaceId.Human, AgeId.Classical, 30, new Vec2i(20, 20), out _);
            Set(s, 1.5f, 1.5f, 0.75f, 0f);
            var adv = new HappinessAdvisor();
            RunOnce(adv, ctx, true);
            RunOnce(adv, ctx, false);
            RunOnce(adv, ctx, false);
            for (int i = 0; i < Counts.Buildings; i++) Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, (BuildingType)i), ((BuildingType)i).ToString());
            for (int j = 0; j < Counts.Jobs; j++) Assert.AreEqual(0f, AdvisorBoard.JobBias(s, (JobType)j), ((JobType)j).ToString());
            Assert.Less(AdvisorBoard.Urgency(s, HappinessAdvisor.Id), 0.05f);
            Assert.AreEqual((float)(int)HappinessCause.None, s.Mem(HappinessAdvisorKeys.Cause));
        }

        [Test]
        public void HighUnrestAtFullCoverage_DoesNotBuildMoreTheaters()
        {
            var db = CreateDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = Make(ctx, RaceId.Human, AgeId.Classical, 30, new Vec2i(20, 20), out _);
            Set(s, 1.6f, 1.5f, 0.3f, 0.5f);
            RunOnce(new HappinessAdvisor(), ctx, true);
            Assert.AreEqual(0f, CultureBias(s), "diminishing returns: venues are already full");
            Assert.Greater(AdvisorBoard.Urgency(s, HappinessAdvisor.Id), 0.8f, "but the danger is still reported");
        }

        [Test]
        public void RaceFlavour_OrcsArenaHumansAndElvesTheater()
        {
            var db = CreateDb();
            float Bias(RaceId race, BuildingType t)
            {
                var ctx = TestWorlds.CreateContext(db);
                var s = Make(ctx, race, AgeId.Classical, 50, new Vec2i(20, 20), out _);
                Set(s, 0.3f, 1f, 0.45f, 0.1f);
                RunOnce(new HappinessAdvisor(), ctx, true);
                return AdvisorBoard.BuildBias(s, t);
            }
            Assert.Greater(Bias(RaceId.Orc, BuildingType.Arena), Bias(RaceId.Orc, BuildingType.Theater));
            Assert.Greater(Bias(RaceId.Human, BuildingType.Theater), Bias(RaceId.Human, BuildingType.Arena));
            Assert.Greater(Bias(RaceId.Elf, BuildingType.Theater), Bias(RaceId.Elf, BuildingType.Arena));
        }

        [Test]
        public void DawnAge_VotesCampfireOnly()
        {
            var db = CreateDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = Make(ctx, RaceId.Human, AgeId.Dawn, 12, new Vec2i(20, 20), out _);
            Set(s, 0.2f, 1f, 0.45f, 0.1f);
            RunOnce(new HappinessAdvisor(), ctx, true);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Campfire), 0.3f);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Theater), "Theater is not available in the Dawn age");
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Tavern));
        }

        [Test]
        public void IdleVenue_IsStaffedBeforeBuildingAnother()
        {
            var db = CreateDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = Make(ctx, RaceId.Human, AgeId.Classical, 30, new Vec2i(20, 20), out _);
            var site = ctx.Ops.FindFreeSite(new Vec2i(26, 26), 2, 2, 8);
            Assert.IsNotNull(ctx.Ops.CreateBuilding(s, BuildingType.Theater, site, 0, true));
            Set(s, 0.3f, 1f, 0.45f, 0.1f);
            RunOnce(new HappinessAdvisor(), ctx, true);
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Entertainer), 0.2f);
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Theater), AdvisorBoard.JobBias(s, JobType.Entertainer) + 0.5f);
        }

        [Test]
        public void RecentDeaths_TriggerFaithAndGathering()
        {
            var db = CreateDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = Make(ctx, RaceId.Human, AgeId.Classical, 30, new Vec2i(20, 20), out _);
            var adv = new HappinessAdvisor();
            adv.Initialize(ctx);
            for (int i = 0; i < 6; i++) ctx.Ops.KillPerson(ctx.State.People.Get(s.ResidentIds[0]), DeathCause.Disaster);
            ctx.Ops.FlushRemovals();
            Set(s, 1.0f, 0.4f, 0.5f, 0.05f);
            RunOnce(adv, ctx, false);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Shrine), 0.3f);
            Assert.Greater(CultureBias(s), 0f, "a place to gather and mourn");
            Assert.Greater(adv.LastDiagnosis.DeathShare, 0.1f);
        }

        [Test]
        public void Deterministic_AndNoNaN()
        {
            string a = Scenario(7), b = Scenario(7);
            Assert.AreEqual(a, b);
            StringAssert.DoesNotContain("NaN", a);
            StringAssert.DoesNotContain("Infinity", a);
        }

        private static string Scenario(ulong seed)
        {
            var db = CreateDb();
            var ctx = TestWorlds.CreateContext(db, 64, seed);
            var capital = Make(ctx, RaceId.Orc, AgeId.Classical, 40, new Vec2i(10, 10), out var civ);
            var colony = ctx.Ops.FoundSettlement(civ, new Vec2i(52, 50));
            for (int i = 0; i < 25; i++) ctx.Ops.CreatePerson(colony, RaceId.Orc, i % 2 == 0 ? Sex.Female : Sex.Male, 18f + i % 7);
            colony.Tier = SettlementTier.Hamlet;
            var adv = new HappinessAdvisor();
            for (int run = 0; run < 6; run++)
            {
                float t = run / 5f;
                Set(capital, 0.2f + 0.2f * t, 0.5f, 0.5f - 0.2f * t, 0.05f + 0.3f * t, run);
                Set(colony, float.NaN, 0.1f, 0.4f, 0.1f + 0.1f * run, 2);
                if (run == 2) ctx.Ops.KillPerson(ctx.State.People.Get(colony.ResidentIds[0]), DeathCause.Violence);
                RunOnce(adv, ctx, run == 0);
            }
            var sb = new StringBuilder();
            Dump(sb, capital);
            Dump(sb, colony);
            sb.Append(adv.LastDiagnosis.Describe());
            return sb.ToString();
        }

        private static void Dump(StringBuilder sb, Settlement s)
        {
            var keys = new List<string>(s.Memory.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            for (int i = 0; i < keys.Count; i++) sb.Append(keys[i]).Append('=').Append(s.Memory[keys[i]].ToString("R")).Append(';');
            sb.Append(string.Join("|", s.AiLog)).Append('\n');
        }
    }
}
