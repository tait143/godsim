using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class ScienceAdvisorTests
    {
        private sealed class Scenario
        {
            public SimContext Ctx;
            public Civilization Civ;
            public Settlement Town;
            public Settlement Hamlet;
            public ScienceAdvisor Advisor = new ScienceAdvisor();
        }

        private static BuildingDefinition Research(BuildingType t, AgeId minAge, SettlementTier tier, int minPop, float research, float work, bool wonder)
        {
            return new BuildingDefinition
            {
                Type = t, Name = t.ToString(),
                Category = wonder ? BuildingCategory.Wonder : BuildingCategory.Education,
                MinAge = minAge, MinTier = tier, MinPopulation = minPop,
                MaxPerSettlement = wonder ? 1 : -1, UniquePerCivilization = wonder, IsWonder = wonder,
                Width = 2, Height = 1, BuildWork = work,
                Job = JobType.Scholar, JobSlots = 2,
                Services = new[] { new ServiceAmount(ServiceType.Research, research) },
            };
        }

        private static GameDatabase BuildDb()
        {
            var db = new GameDatabase();
            db.Register(new AgeDefinition { Id = AgeId.Medieval, Name = "Medieval", ResearchThreshold = 5000f, RequiredTechsFromPreviousAge = 2, MinPopulation = 100, MinSettlements = 2 });
            db.Register(Research(BuildingType.Scriptorium, AgeId.Iron, SettlementTier.Hamlet, 30, 1.2f, 50f, false));
            db.Register(Research(BuildingType.Library, AgeId.Classical, SettlementTier.Village, 25, 3f, 140f, false));
            db.Register(Research(BuildingType.University, AgeId.Renaissance, SettlementTier.Town, 50, 7f, 300f, false));
            db.Register(Research(BuildingType.GreatLibrary, AgeId.Classical, SettlementTier.Town, 80, 25f, 750f, true));
            for (int i = 0; i < 3; i++)
                db.Register(new TechDefinition { Key = "classical.t" + i, Name = "Classical Tech " + i, Age = AgeId.Classical, Cost = 300f + i * 50f });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Scenario Build(int townPop = 60, AgeId age = AgeId.Classical, float totalResearch = 0f)
        {
            var sc = new Scenario();
            var db = BuildDb();
            sc.Ctx = TestWorlds.CreateContext(db);
            sc.Town = TestWorlds.AddSettlement(sc.Ctx, RaceId.Human, new Vec2i(16, 16), townPop, out sc.Civ);
            sc.Hamlet = sc.Ctx.Ops.FoundSettlement(sc.Civ, new Vec2i(46, 46));
            for (int i = 0; i < 12; i++) sc.Ctx.Ops.CreatePerson(sc.Hamlet, RaceId.Human, i % 2 == 0 ? Sex.Female : Sex.Male, 25f);
            sc.Civ.Age = age;
            sc.Civ.AgeEnteredTick = 0;
            sc.Civ.TotalResearch = totalResearch;
            sc.Town.Tier = SettlementTier.Town;
            sc.Hamlet.Tier = SettlementTier.Hamlet;
            var site = sc.Ctx.Ops.FindFreeSite(sc.Town.Center, 2, 1, 10);
            Assert.IsNotNull(sc.Ctx.Ops.CreateBuilding(sc.Town, BuildingType.Scriptorium, site, 0, true), "scriptorium placed");
            return sc;
        }

        private static void RunOnce(Scenario sc) => TestWorlds.Run(sc.Advisor, sc.Ctx, sc.Advisor.TickInterval);

        private static float B(Settlement s, BuildingType t) => AdvisorBoard.BuildBias(s, t);
        private static float J(Settlement s, JobType j) => AdvisorBoard.JobBias(s, j);

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].IndexOf(text, StringComparison.Ordinal) >= 0) return true;
            return false;
        }

        [Test]
        public void BehindPace_VotesLibraryAndScholarsAtBestSettlement()
        {
            var sc = Build();
            RunOnce(sc);
            var plan = sc.Advisor.PlanFor(sc.Ctx, sc.Civ);
            Assert.AreEqual(sc.Town.Id, plan.BestSettlementId);
            Assert.AreEqual(SciencePacePlan.BandBehind, plan.Band, plan.Reason);
            Assert.AreEqual(2, plan.TechsMissing);

            Assert.Greater(B(sc.Town, BuildingType.Library), 0.3f, "library pushed at the town");
            Assert.Greater(B(sc.Town, BuildingType.Library), B(sc.Town, BuildingType.Scriptorium), "library preferred over another scriptorium");
            Assert.AreEqual(0f, B(sc.Hamlet, BuildingType.Library), "hamlet too small for a library");
            Assert.Greater(J(sc.Town, JobType.Scholar), 0.3f, "scholars staffed");
            Assert.Greater(AdvisorBoard.Urgency(sc.Town, ScienceAdvisor.Id), 0.3f);
            Assert.IsTrue(LogContains(sc.Town, "Medieval Age needs 2 more techs"), "explainable reason logged");
            Assert.IsTrue(LogContains(sc.Town, "at current pace"));
        }

        [Test]
        public void AheadOfPace_GivesNoPositiveVotes()
        {
            var sc = Build(60, AgeId.Classical, 4990f);
            var techs = sc.Ctx.Db.TechsOfAge(AgeId.Classical);
            sc.Civ.KnownTechs.Add(techs[0]);
            sc.Civ.KnownTechs.Add(techs[1]);
            sc.Town.ServiceOutput[(int)ServiceType.Research] = 5f;
            RunOnce(sc);
            Assert.AreEqual(SciencePacePlan.BandAhead, sc.Advisor.PlanFor(sc.Ctx, sc.Civ).Band);
            Assert.Less(B(sc.Town, BuildingType.Library), 0f, "eases off research buildings");
            Assert.LessOrEqual(J(sc.Town, JobType.Scholar), 0f);
            Assert.LessOrEqual(B(sc.Town, BuildingType.GreatLibrary), 0f);
            Assert.LessOrEqual(B(sc.Hamlet, BuildingType.Scriptorium), 0f);
        }

        [Test]
        public void CrisisUrgency_SuppressesVotes()
        {
            var sc = Build();
            AdvisorBoard.ReportUrgency(sc.Town, "food", 1f);
            AdvisorBoard.ReportUrgency(sc.Hamlet, "defense", 0.9f);
            RunOnce(sc);
            for (int t = 0; t < Counts.Buildings; t++)
            {
                Assert.LessOrEqual(B(sc.Town, (BuildingType)t), 0f, "town " + (BuildingType)t);
                Assert.LessOrEqual(B(sc.Hamlet, (BuildingType)t), 0f, "hamlet " + (BuildingType)t);
            }
            Assert.LessOrEqual(J(sc.Town, JobType.Scholar), 0f);
            Assert.AreEqual(0f, AdvisorBoard.Urgency(sc.Town, ScienceAdvisor.Id));
            Assert.IsTrue(LogContains(sc.Town, "food crisis"));
        }

        [Test]
        public void ModerateCrisis_DampsVotes()
        {
            var calm = Build();
            RunOnce(calm);
            var tense = Build();
            AdvisorBoard.ReportUrgency(tense.Town, "food", 0.45f);
            RunOnce(tense);
            Assert.Less(B(tense.Town, BuildingType.Library), B(calm.Town, BuildingType.Library));
        }

        [Test]
        public void UnavailableBuildings_AreNeverVoted()
        {
            var sc = Build(60, AgeId.Iron);
            RunOnce(sc);
            Assert.AreEqual(0f, B(sc.Town, BuildingType.Library), "library needs Classical");
            Assert.AreEqual(0f, B(sc.Town, BuildingType.University));
            Assert.AreEqual(0f, B(sc.Town, BuildingType.GreatLibrary));
            Assert.Greater(B(sc.Town, BuildingType.Scriptorium), 0f, "the available research building is pushed");

            var classical = Build(60);
            RunOnce(classical);
            Assert.AreEqual(0f, B(classical.Town, BuildingType.University), "university needs Renaissance");
        }

        [Test]
        public void TechUnlockedBuilding_IsConsidered()
        {
            var sc = Build(60);
            sc.Civ.UnlockedBuildings[(int)BuildingType.University] = true;
            RunOnce(sc);
            Assert.Greater(B(sc.Town, BuildingType.University), 0f);
        }

        [Test]
        public void ResearchWonder_OnlyWhenNotAlreadyQueued()
        {
            var sc = Build(90);
            RunOnce(sc);
            Assert.Greater(B(sc.Town, BuildingType.GreatLibrary), 0f, "wonder suggested at the best site");

            var queued = Build(90);
            queued.Hamlet.BuildQueue.Add(new BuildOrder { Type = BuildingType.GreatLibrary, Priority = 6f, Reason = "strategy wonder" });
            RunOnce(queued);
            Assert.AreEqual(0f, B(queued.Town, BuildingType.GreatLibrary), "strategy already queued it");
            Assert.AreEqual(1, queued.Hamlet.BuildQueue.Count, "never edits the build queue");
        }

        [Test]
        public void ResearchHint_NeverWritesCurrentResearch()
        {
            var idle = Build();
            RunOnce(idle);
            Assert.AreEqual(-1, idle.Civ.CurrentResearchTechId);
            Assert.IsFalse(idle.Civ.Memory.ContainsKey(ScienceKeys.PreferredTech), "silent while nothing is researched");

            var busy = Build();
            int other = busy.Ctx.Db.TechIdOf("stub.tech");
            busy.Civ.CurrentResearchTechId = other >= 0 ? other : busy.Ctx.Db.TechsOfAge(AgeId.Classical)[2];
            int before = busy.Civ.CurrentResearchTechId;
            RunOnce(busy);
            Assert.AreEqual(before, busy.Civ.CurrentResearchTechId);
            Assert.AreEqual(busy.Ctx.Db.TechIdOf("classical.t0"), MathUtil.RoundToInt(busy.Civ.Mem(ScienceKeys.PreferredTech, -5f)), "cheapest age tech hinted");
        }

        [Test]
        public void RateEma_IsStoredAndFinite()
        {
            var sc = Build();
            sc.Town.ServiceOutput[(int)ServiceType.Research] = float.NaN;
            TestWorlds.Run(sc.Advisor, sc.Ctx, sc.Advisor.TickInterval * 4);
            float rate = sc.Civ.Mem(ScienceKeys.Rate, -1f);
            Assert.IsFalse(float.IsNaN(rate) || float.IsInfinity(rate));
            Assert.AreEqual(0.004f * 72, rate, 1e-3f);
            Assert.Greater(sc.Civ.Mem(ScienceKeys.YearsToNextAge), 10f);

            // a research collapse is tracked quickly (disaster recovery)
            sc.Town.ServiceOutput[(int)ServiceType.Research] = 20f;
            TestWorlds.Run(sc.Advisor, sc.Ctx, sc.Advisor.TickInterval * 6, false);
            float high = sc.Civ.Mem(ScienceKeys.Rate);
            sc.Town.ServiceOutput[(int)ServiceType.Research] = 0f;
            TestWorlds.Run(sc.Advisor, sc.Ctx, sc.Advisor.TickInterval, false);
            Assert.Less(sc.Civ.Mem(ScienceKeys.Rate), high * 0.5f);
            foreach (var kv in sc.Town.Memory) Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
        }

        [Test]
        public void Deterministic_SameInputsSameVotes()
        {
            var a = Build();
            var b = Build();
            TestWorlds.Run(a.Advisor, a.Ctx, a.Advisor.TickInterval * 6);
            TestWorlds.Run(b.Advisor, b.Ctx, b.Advisor.TickInterval * 6);
            Assert.AreEqual(Dump(a.Town.Memory), Dump(b.Town.Memory));
            Assert.AreEqual(Dump(a.Hamlet.Memory), Dump(b.Hamlet.Memory));
            Assert.AreEqual(Dump(a.Civ.Memory), Dump(b.Civ.Memory));
            Assert.AreEqual(string.Join("|", a.Town.AiLog), string.Join("|", b.Town.AiLog));
        }

        private static string Dump(Dictionary<string, float> mem)
        {
            var keys = new List<string>(mem.Keys);
            keys.Sort(StringComparer.Ordinal);
            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++) sb.Append(keys[i]).Append('=').Append(mem[keys[i]].ToString("R")).Append(';');
            return sb.ToString();
        }
    }
}
