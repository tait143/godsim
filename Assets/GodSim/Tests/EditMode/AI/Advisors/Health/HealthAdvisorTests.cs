using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class HealthAdvisorTests
    {
        private static readonly BuildingType[] WaterLadder = { BuildingType.Well, BuildingType.Aqueduct };
        private static readonly BuildingType[] HealthLadder = { BuildingType.Herbalist, BuildingType.Bathhouse, BuildingType.Hospital, BuildingType.MedicalCenter };

        private static BuildingDefinition Def(BuildingType t, BuildingCategory cat, AgeId age, JobType job, int slots, params ServiceAmount[] services)
        {
            return new BuildingDefinition { Type = t, Name = t.ToString(), Category = cat, MinAge = age, Job = job, JobSlots = slots, Services = services, BuildWork = 10f };
        }

        private static GameDatabase MakeDb()
        {
            var db = new GameDatabase();
            db.Register(Def(BuildingType.Well, BuildingCategory.Infrastructure, AgeId.Dawn, JobType.None, 0, new ServiceAmount(ServiceType.Water, 3f)));
            db.Register(Def(BuildingType.Aqueduct, BuildingCategory.Infrastructure, AgeId.Classical, JobType.None, 0, new ServiceAmount(ServiceType.Water, 10f)));
            db.Register(Def(BuildingType.Herbalist, BuildingCategory.Health, AgeId.Bronze, JobType.Healer, 1, new ServiceAmount(ServiceType.Health, 1.5f)));
            db.Register(Def(BuildingType.Bathhouse, BuildingCategory.Health, AgeId.Classical, JobType.Healer, 1, new ServiceAmount(ServiceType.Health, 3f), new ServiceAmount(ServiceType.Water, 2f)));
            db.Register(Def(BuildingType.Hospital, BuildingCategory.Health, AgeId.Renaissance, JobType.Healer, 4, new ServiceAmount(ServiceType.Health, 8f)));
            db.Register(Def(BuildingType.MedicalCenter, BuildingCategory.Health, AgeId.Information, JobType.Healer, 6, new ServiceAmount(ServiceType.Health, 25f)));
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Settlement Setup(GameDatabase db, AgeId age, int adults, float water, float health, out SimContext ctx, out Civilization civ, ulong seed = 1)
        {
            ctx = TestWorlds.CreateContext(db, 64, seed);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), adults, out civ);
            civ.Age = age;
            s.ServiceCoverage[(int)ServiceType.Water] = water;
            s.ServiceCoverage[(int)ServiceType.Health] = health;
            return s;
        }

        private static void KillByDisease(SimContext ctx, Settlement s, int count)
        {
            for (int i = 0; i < count && s.ResidentIds.Count > 0; i++)
                ctx.Ops.KillPerson(ctx.State.People.Get(s.ResidentIds[0]), DeathCause.Disease);
            ctx.Ops.FlushRemovals();
        }

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains(text)) return true;
            return false;
        }

        [Test]
        public void HighDensityLowCoverage_VotesWaterFirstThenHealers()
        {
            var db = MakeDb();
            var s = Setup(db, AgeId.Bronze, 30, 0.1f, 0.1f, out var ctx, out var civ);
            var adv = new HealthAdvisor();
            TestWorlds.Run(adv, ctx, 24);

            float well = AdvisorBoard.BuildBias(s, BuildingType.Well);
            float herb = AdvisorBoard.BuildBias(s, BuildingType.Herbalist);
            Assert.IsTrue(well > 0.3f, "well bias " + well);
            Assert.IsTrue(herb > 0.1f, "herbalist bias " + herb);
            Assert.IsTrue(well > herb, "water first");
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Aqueduct), "aqueduct not available in Bronze");
            Assert.IsTrue(AdvisorBoard.Urgency(s, HealthAdvisor.Id) > 0.2f);
            Assert.IsTrue(LogContains(s, "[health]") && LogContains(s, "crowded"), "strong vote carries a reason");
        }

        [Test]
        public void RecentDiseaseDeaths_GiveUrgentSpikeThatDecays()
        {
            var db = MakeDb();
            var s = Setup(db, AgeId.Bronze, 40, 1.2f, 1.2f, out var ctx, out var civ);
            var adv = new HealthAdvisor();
            ctx.DeltaTicks = adv.TickInterval;
            adv.Initialize(ctx);
            KillByDisease(ctx, s, 4);
            Assert.AreEqual(4f, s.Mem(HealthAdvisorKeys.PendingDeaths));

            TestWorlds.Run(adv, ctx, 24, false);
            float u1 = AdvisorBoard.Urgency(s, HealthAdvisor.Id);
            TestWorlds.Run(adv, ctx, 24, false);
            float u2 = AdvisorBoard.Urgency(s, HealthAdvisor.Id);
            TestWorlds.Run(adv, ctx, 24, false);
            float u3 = AdvisorBoard.Urgency(s, HealthAdvisor.Id);

            Assert.IsTrue(u1 >= 0.8f, "urgent spike " + u1);
            Assert.IsTrue(u2 < u1 && u3 < u2, "decays: " + u1 + " " + u2 + " " + u3);
            Assert.AreEqual(0f, s.Mem(HealthAdvisorKeys.PendingDeaths), "pending consumed");
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Herbalist), "full coverage: no redundant healers");
        }

        [Test]
        public void Outbreak_WithLowCoverage_ExplainsDeathsAndPushesHealers()
        {
            var db = MakeDb();
            var s = Setup(db, AgeId.Bronze, 30, 1.2f, 0.2f, out var ctx, out var civ);
            var adv = new HealthAdvisor();
            ctx.DeltaTicks = adv.TickInterval;
            adv.Initialize(ctx);
            KillByDisease(ctx, s, 3);
            TestWorlds.Run(adv, ctx, 24, false);

            Assert.IsTrue(AdvisorBoard.BuildBias(s, BuildingType.Herbalist) >= 0.6f);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Well), "water already covered");
            Assert.IsTrue(LogContains(s, "3 died of fever last season"), string.Join(" | ", s.AiLog));
        }

        [Test]
        public void UnstaffedClinic_GetsHealerJobVote()
        {
            var db = MakeDb();
            var s = Setup(db, AgeId.Bronze, 30, 1.2f, 0.1f, out var ctx, out var civ);
            var site = ctx.Ops.FindFreeSite(new Vec2i(s.Center.X + 4, s.Center.Y), 1, 1, 10);
            var clinic = ctx.Ops.CreateBuilding(s, BuildingType.Herbalist, site, 0, true);
            Assert.IsNotNull(clinic);
            TestWorlds.Run(new HealthAdvisor(), ctx, 24);
            Assert.IsTrue(AdvisorBoard.JobBias(s, JobType.Healer) > 0.2f);
        }

        [Test]
        public void FullCoverage_GivesNoVotes()
        {
            var db = MakeDb();
            var s = Setup(db, AgeId.Information, 30, 1.3f, 1.3f, out var ctx, out var civ);
            TestWorlds.Run(new HealthAdvisor(), ctx, 24);
            for (int i = 0; i < WaterLadder.Length; i++) Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, WaterLadder[i]));
            for (int i = 0; i < HealthLadder.Length; i++) Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, HealthLadder[i]));
            Assert.AreEqual(0f, AdvisorBoard.JobBias(s, JobType.Healer));
            Assert.IsTrue(AdvisorBoard.Urgency(s, HealthAdvisor.Id) < 0.01f);
        }

        [TestCase(AgeId.Bronze, BuildingType.Well, BuildingType.Herbalist)]
        [TestCase(AgeId.Classical, BuildingType.Aqueduct, BuildingType.Bathhouse)]
        [TestCase(AgeId.Renaissance, BuildingType.Aqueduct, BuildingType.Hospital)]
        [TestCase(AgeId.Information, BuildingType.Aqueduct, BuildingType.MedicalCenter)]
        public void AgeEscalation_PicksBestAvailableBuilding(AgeId age, BuildingType water, BuildingType health)
        {
            var db = MakeDb();
            var s = Setup(db, age, 30, 0f, 0f, out var ctx, out var civ);
            TestWorlds.Run(new HealthAdvisor(), ctx, 24);
            float w = AdvisorBoard.BuildBias(s, water), h = AdvisorBoard.BuildBias(s, health);
            Assert.IsTrue(w > 0f && h > 0f, age + ": " + w + " " + h);
            for (int i = 0; i < WaterLadder.Length; i++) if (WaterLadder[i] != water) Assert.IsTrue(AdvisorBoard.BuildBias(s, WaterLadder[i]) < w, age + " water " + WaterLadder[i]);
            for (int i = 0; i < HealthLadder.Length; i++) if (HealthLadder[i] != health) Assert.IsTrue(AdvisorBoard.BuildBias(s, HealthLadder[i]) < h, age + " health " + HealthLadder[i]);
            for (int i = 0; i < Counts.Buildings; i++)
                if (AdvisorBoard.BuildBias(s, (BuildingType)i) != 0f) Assert.IsTrue(db.IsBuildingAvailable(civ, (BuildingType)i), "voted unavailable " + (BuildingType)i);
        }

        [Test]
        public void DawnAge_OnlyWell()
        {
            var db = MakeDb();
            var s = Setup(db, AgeId.Dawn, 30, 0f, 0f, out var ctx, out var civ);
            TestWorlds.Run(new HealthAdvisor(), ctx, 24);
            Assert.IsTrue(AdvisorBoard.BuildBias(s, BuildingType.Well) > 0f);
            for (int i = 0; i < HealthLadder.Length; i++) Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, HealthLadder[i]));
        }

        [Test]
        public void QueuedProvider_SoftensVote()
        {
            var db = MakeDb();
            var a = Setup(db, AgeId.Bronze, 30, 0.1f, 0.1f, out var ctxA, out var civA);
            var b = Setup(db, AgeId.Bronze, 30, 0.1f, 0.1f, out var ctxB, out var civB);
            b.BuildQueue.Add(new BuildOrder { Type = BuildingType.Well, Priority = 1f });
            TestWorlds.Run(new HealthAdvisor(), ctxA, 24);
            TestWorlds.Run(new HealthAdvisor(), ctxB, 24);
            Assert.IsTrue(AdvisorBoard.BuildBias(b, BuildingType.Well) < AdvisorBoard.BuildBias(a, BuildingType.Well) * 0.5f);
            Assert.AreEqual(1, b.BuildQueue.Count, "never edits the queue");
        }

        [Test]
        public void PlagueNearby_RaisesUrgency_FarTownUnaffected()
        {
            var db = MakeDb();
            var s = Setup(db, AgeId.Bronze, 30, 1.2f, 1.2f, out var ctx, out var civ);
            var far = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(6, 6), 20, out var farCiv);
            far.ServiceCoverage[(int)ServiceType.Water] = 1.2f;
            far.ServiceCoverage[(int)ServiceType.Health] = 1.2f;
            var adv = new HealthAdvisor();
            ctx.DeltaTicks = adv.TickInterval;
            adv.Initialize(ctx);
            ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Plague, Position = s.Center, Radius = 3, Severity = 1f });
            TestWorlds.Run(adv, ctx, 24, false);
            Assert.IsTrue(AdvisorBoard.Urgency(s, HealthAdvisor.Id) >= 0.7f);
            Assert.IsTrue(AdvisorBoard.Urgency(far, HealthAdvisor.Id) < 0.05f);
            Assert.IsTrue(HealthAdvisor.Describe(s).Contains("plague"));
        }

        [Test]
        public void OutbreakInNeighbor_RaisesRiskNextDoor()
        {
            var db = MakeDb();
            var s = Setup(db, AgeId.Bronze, 30, 0.6f, 0.6f, out var ctx, out var civ);
            var next = ctx.Ops.FoundSettlement(civ, new Vec2i(46, 32));
            for (int i = 0; i < 30; i++) ctx.Ops.CreatePerson(next, RaceId.Human, i % 2 == 0 ? Sex.Female : Sex.Male, 25f);
            next.ServiceCoverage[(int)ServiceType.Water] = 0.6f;
            next.ServiceCoverage[(int)ServiceType.Health] = 0.6f;
            var adv = new HealthAdvisor();
            ctx.DeltaTicks = adv.TickInterval;
            adv.Initialize(ctx);
            KillByDisease(ctx, s, 5);
            TestWorlds.Run(adv, ctx, 24, false);
            Assert.IsTrue(AdvisorBoard.Urgency(next, HealthAdvisor.Id) >= 0.3f, "neighbor alarm " + AdvisorBoard.Urgency(next, HealthAdvisor.Id));
        }

        [Test]
        public void Deterministic_AcrossIdenticalRuns()
        {
            var a = RunScenario(7);
            var b = RunScenario(7);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i]);
        }

        private static List<string> RunScenario(ulong seed)
        {
            var db = MakeDb();
            var s = Setup(db, AgeId.Classical, 36, 0.3f, 0.2f, out var ctx, out var civ, seed);
            var adv = new HealthAdvisor();
            ctx.DeltaTicks = adv.TickInterval;
            adv.Initialize(ctx);
            KillByDisease(ctx, s, 3);
            ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Flood, Position = s.Center, Radius = 4, Severity = 0.8f });
            TestWorlds.Run(adv, ctx, 96, false);
            var keys = new List<string>(s.Memory.Keys);
            keys.Sort(string.CompareOrdinal);
            var outList = new List<string>();
            for (int i = 0; i < keys.Count; i++) outList.Add(keys[i] + "=" + s.Memory[keys[i]].ToString("R"));
            return outList;
        }

        [Test]
        public void BadData_NeverProducesNaN()
        {
            var db = MakeDb();
            var s = Setup(db, AgeId.Renaissance, 12, float.NaN, float.PositiveInfinity, out var ctx, out var civ);
            s.Health = float.NaN;
            s.Memory[HealthAdvisorKeys.Spike] = float.NaN;
            s.Memory[HealthAdvisorKeys.DeathEma] = float.PositiveInfinity;
            TestWorlds.Run(new HealthAdvisor(), ctx, 48);
            foreach (var kv in s.Memory)
                Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
        }

        [Test]
        public void Model_GrowthAnticipatesCoverageDilution()
        {
            var steady = HealthRiskModel.Evaluate(new HealthRiskInputs { Population = 40, BuildingCount = 10, AvgHealth = 1f, DiseaseResistance = 1f, WaterCoverage = 1.05f, HealthCoverage = 1.05f });
            var growing = HealthRiskModel.Evaluate(new HealthRiskInputs { Population = 40, BuildingCount = 10, AvgHealth = 1f, DiseaseResistance = 1f, WaterCoverage = 1.05f, HealthCoverage = 1.05f, GrowthPerRun = 8f });
            Assert.AreEqual(0f, steady.WaterNeed);
            Assert.IsTrue(growing.WaterNeed > 0f && growing.Growing);
            var frail = HealthRiskModel.Evaluate(new HealthRiskInputs { Population = 40, BuildingCount = 10, AvgHealth = 1f, DiseaseResistance = 0.5f });
            var hardy = HealthRiskModel.Evaluate(new HealthRiskInputs { Population = 40, BuildingCount = 10, AvgHealth = 1f, DiseaseResistance = 2f });
            Assert.IsTrue(frail.Risk > hardy.Risk);
        }
    }
}
