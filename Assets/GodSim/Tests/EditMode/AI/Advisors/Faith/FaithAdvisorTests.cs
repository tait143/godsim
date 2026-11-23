using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Advisors.Faith
{
    public sealed class FaithAdvisorTests
    {
        private static readonly BuildingType[] Worship =
            { BuildingType.SpiritTotem, BuildingType.Shrine, BuildingType.Temple, BuildingType.Monastery, BuildingType.Cathedral };

        // ------------------------------------------------------------------ fixtures
        private static BuildingDefinition Def(BuildingType t, AgeId min, AgeId max, SettlementTier tier, int minPop, int maxPer,
            int size, JobType job, int slots, float faith, float research = 0f)
        {
            var svc = research > 0f
                ? new[] { new ServiceAmount(ServiceType.Faith, faith), new ServiceAmount(ServiceType.Research, research) }
                : new[] { new ServiceAmount(ServiceType.Faith, faith) };
            return new BuildingDefinition
            {
                Type = t, Name = t.ToString(), Category = BuildingCategory.Religion, MinAge = min, MaxAge = max, MinTier = tier,
                MinPopulation = minPop, MaxPerSettlement = maxPer, Width = size, Height = size, BuildWork = 20f,
                Job = job, JobSlots = slots, Services = svc,
            };
        }

        private static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            db.Register(Def(BuildingType.SpiritTotem, AgeId.Dawn, AgeId.Iron, SettlementTier.Camp, 5, -1, 1, JobType.None, 0, 0.5f));
            db.Register(Def(BuildingType.Shrine, AgeId.Bronze, AgeId.Ascension, SettlementTier.Camp, 10, -1, 1, JobType.Priest, 1, 1f));
            db.Register(Def(BuildingType.Temple, AgeId.Iron, AgeId.Ascension, SettlementTier.Hamlet, 25, 2, 2, JobType.Priest, 2, 3f));
            db.Register(Def(BuildingType.Monastery, AgeId.Medieval, AgeId.Ascension, SettlementTier.Hamlet, 15, -1, 2, JobType.Priest, 3, 3f, 1.5f));
            db.Register(Def(BuildingType.Cathedral, AgeId.Medieval, AgeId.Ascension, SettlementTier.Town, 60, 1, 3, JobType.Priest, 4, 8f));
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private sealed class World
        {
            public SimContext Ctx;
            public Settlement S;
            public Civilization Civ;
            public FaithAdvisor Advisor;
        }

        private static World Make(AgeId age, int pop, SettlementTier tier, float piety, float coverage)
        {
            var w = new World();
            w.Ctx = TestWorlds.CreateContext(CreateDb(), 64, 7);
            w.S = TestWorlds.AddSettlement(w.Ctx, RaceId.Saurian, new Vec2i(32, 32), pop, out w.Civ);
            w.Civ.Age = age;
            w.S.Tier = tier;
            w.S.ServiceCoverage[(int)ServiceType.Faith] = coverage;
            AdvisorBoard.SetTrait(w.Civ, AITrait.Piety, piety);
            w.Advisor = new FaithAdvisor();
            w.Advisor.Initialize(w.Ctx);
            return w;
        }

        /// <summary>
        /// Per-run board value of a per-council-cycle expectation. Votes are normalized per 24-tick council cycle
        /// (AdvisorSystemBase.CycleScale = TickInterval / 24), so one run of this 12-tick advisor contributes half of
        /// what it votes per cycle. Thresholds below are written per cycle and checked against a single run.
        /// </summary>
        private static float PerRun(float perCycle) => perCycle * System.Math.Min(1f, new FaithAdvisor().TickInterval / 24f);

        private static void RunOnce(World w) => TestWorlds.Run(w.Advisor, w.Ctx, w.Advisor.TickInterval, false);

        private static void Cast(World w, GodPowerType power, Vec2i at, int radius = 4) =>
            w.Ctx.Events.Publish(new GodPowerCastEvent { Power = power, Target = at, Radius = radius, Success = true, Message = "" });

        private static bool Logged(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains(text)) return true;
            return false;
        }

        private static BuildingType Favorite(Settlement s, out float best)
        {
            var pick = BuildingType.TownCenter;
            best = 0f;
            for (int i = 0; i < Worship.Length; i++)
            {
                float b = AdvisorBoard.BuildBias(s, Worship[i]);
                if (b > best) { best = b; pick = Worship[i]; }
            }
            return pick;
        }

        private static Building Place(World w, BuildingType t, bool complete)
        {
            var def = w.Ctx.Db.Building(t);
            var site = w.Ctx.Ops.FindFreeSite(w.S.Center, def.Width, def.Height, 12);
            Assert.IsTrue(site.IsValid, "no free site for " + t);
            var b = w.Ctx.Ops.CreateBuilding(w.S, t, site, 0, complete);
            Assert.IsNotNull(b);
            return b;
        }

        // ------------------------------------------------------------------ gratitude
        [Test]
        public void Blessing_PiousRace_BuildsInThanks()
        {
            var w = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.9f, 1.5f);
            Cast(w, GodPowerType.Rain, w.S.Center);
            RunOnce(w);
            Assert.Greater(AdvisorBoard.BuildBias(w.S, BuildingType.Shrine), PerRun(0.5f));
            Assert.IsTrue(Logged(w.S, "In thanks for the Rain"), "reason should explain the thanks offering");
            Assert.AreEqual((float)FaithAdvisorMotive.Thanks, w.S.Mem(FaithAdvisorKeys.Motive));
            Assert.Greater(AdvisorBoard.Urgency(w.S, FaithAdvisor.Id), 0.3f);
        }

        [Test]
        public void Blessing_ImpiousRace_GivesOnlyWeakThanks()
        {
            var pious = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.9f, 1.5f);
            var impious = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.1f, 1.5f);
            Cast(pious, GodPowerType.Rain, pious.S.Center);
            Cast(impious, GodPowerType.Rain, impious.S.Center);
            RunOnce(pious);
            RunOnce(impious);
            float p = AdvisorBoard.BuildBias(pious.S, BuildingType.Shrine);
            float i = AdvisorBoard.BuildBias(impious.S, BuildingType.Shrine);
            Assert.Less(i, PerRun(0.5f));
            Assert.Greater(p, i + PerRun(0.3f));
        }

        [Test]
        public void Blessing_FarAway_IsNotPerceived()
        {
            var w = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.9f, 1.5f);
            Cast(w, GodPowerType.Rain, new Vec2i(2, 2), 3);
            RunOnce(w);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(w.S, BuildingType.Shrine));
        }

        [Test]
        public void Offering_FulfilsThanks_AndWritesChronicle()
        {
            var w = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.9f, 1.5f);
            Cast(w, GodPowerType.Rain, w.S.Center);
            Place(w, BuildingType.Shrine, false);
            RunOnce(w);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(w.S, BuildingType.Shrine), "vow already fulfilled: no duplicate thanks shrine");
            bool chronicled = false;
            var entries = w.Ctx.State.Chronicle.Entries;
            for (int i = 0; i < entries.Count; i++) if (entries[i].Text.Contains("in thanks for the Rain")) chronicled = true;
            Assert.IsTrue(chronicled);
        }

        // ------------------------------------------------------------------ wrath
        [Test]
        public void Wrath_PiousRace_Appeases()
        {
            var w = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.85f, 1.5f);
            Cast(w, GodPowerType.Lightning, w.S.Center);
            RunOnce(w);
            Assert.Greater(AdvisorBoard.BuildBias(w.S, BuildingType.Shrine), PerRun(0.5f));
            Assert.IsTrue(Logged(w.S, FaithAdvisor.ReasonAppease));
        }

        [Test]
        public void Wrath_FearfulImpiousRace_StillAppeases()
        {
            var w = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.1f, 1.5f);
            w.Civ.Memory[AdvisorBoard.GodFearKey] = 0.9f;
            Cast(w, GodPowerType.Meteor, w.S.Center);
            RunOnce(w);
            Assert.Greater(AdvisorBoard.BuildBias(w.S, BuildingType.Shrine), PerRun(0.5f));
            Assert.IsTrue(Logged(w.S, FaithAdvisor.ReasonAppease));
        }

        [Test]
        public void Wrath_ImpiousRace_GrowsResentful()
        {
            var w = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.1f, 1.5f);
            Place(w, BuildingType.Shrine, true);
            Cast(w, GodPowerType.Lightning, w.S.Center);
            RunOnce(w);
            Assert.Less(AdvisorBoard.BuildBias(w.S, BuildingType.Shrine), PerRun(-0.3f));
            Assert.Less(AdvisorBoard.BuildBias(w.S, BuildingType.SpiritTotem), 0f);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(w.S, BuildingType.Temple), "never vote on unavailable buildings");
            Assert.Less(AdvisorBoard.JobBias(w.S, JobType.Priest), 0f);
            Assert.IsTrue(Logged(w.S, FaithAdvisor.ReasonResent));
            Assert.Greater(w.S.Mem(FaithAdvisorKeys.Resentment), 0.2f);
        }

        [Test]
        public void NaturalDisaster_IsPerceivedAsWrath()
        {
            var w = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.9f, 1.5f);
            w.Ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Earthquake, Position = w.S.Center, Radius = 3, Severity = 1f });
            RunOnce(w);
            Assert.Greater(AdvisorBoard.BuildBias(w.S, BuildingType.Shrine), PerRun(0.5f));
            Assert.AreEqual((float)(FaithAdvisorKeys.DisasterKindOffset + (int)DisasterType.Earthquake), w.S.Mem(FaithAdvisorKeys.WrathKind));
        }

        // ------------------------------------------------------------------ coverage
        [Test]
        public void LowCoverage_VotesScaledByPiety()
        {
            var pious = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.8f, 0f);
            var impious = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.15f, 0f);
            RunOnce(pious);
            RunOnce(impious);
            float p = AdvisorBoard.BuildBias(pious.S, BuildingType.Shrine);
            float i = AdvisorBoard.BuildBias(impious.S, BuildingType.Shrine);
            Assert.Greater(p, PerRun(0.5f));
            Assert.Greater(i, 0f);
            Assert.Greater(p, i * 2f);
            Assert.IsTrue(Logged(pious.S, FaithAdvisor.ReasonCoverage));
        }

        [Test]
        public void HighCoverage_NoVotes()
        {
            var w = Make(AgeId.Iron, 30, SettlementTier.Hamlet, 0.5f, 1.5f);
            RunOnce(w);
            for (int i = 0; i < Worship.Length; i++) Assert.AreEqual(0f, AdvisorBoard.BuildBias(w.S, Worship[i]), Worship[i].ToString());
        }

        [Test]
        public void Dawn_NeverVotesForUnavailableTiers()
        {
            var w = Make(AgeId.Dawn, 12, SettlementTier.Camp, 0.9f, 0f);
            RunOnce(w);
            Assert.Greater(AdvisorBoard.BuildBias(w.S, BuildingType.SpiritTotem), 0f);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(w.S, BuildingType.Shrine));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(w.S, BuildingType.Temple));
        }

        // ------------------------------------------------------------------ escalation
        [TestCase(AgeId.Dawn, 10, SettlementTier.Camp, BuildingType.SpiritTotem)]
        [TestCase(AgeId.Bronze, 12, SettlementTier.Camp, BuildingType.Shrine)]
        [TestCase(AgeId.Iron, 12, SettlementTier.Camp, BuildingType.Shrine)]
        [TestCase(AgeId.Iron, 30, SettlementTier.Hamlet, BuildingType.Temple)]
        [TestCase(AgeId.Classical, 12, SettlementTier.Camp, BuildingType.Shrine)]
        [TestCase(AgeId.Medieval, 30, SettlementTier.Hamlet, BuildingType.Monastery)]
        [TestCase(AgeId.Medieval, 70, SettlementTier.Town, BuildingType.Cathedral)]
        public void Escalation_PicksTierForAgeAndSize(AgeId age, int pop, SettlementTier tier, BuildingType expected)
        {
            var w = Make(age, pop, tier, 0.8f, 0f);
            RunOnce(w);
            var pick = Favorite(w.S, out float best);
            Assert.AreEqual(expected, pick);
            Assert.Greater(best, PerRun(0.3f));
        }

        [Test]
        public void Escalation_RespectsCapsAndExistingBuildings()
        {
            var w = Make(AgeId.Medieval, 70, SettlementTier.Town, 0.8f, 0f);
            Place(w, BuildingType.Cathedral, true);
            RunOnce(w);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(w.S, BuildingType.Cathedral), "Cathedral is capped at one");
            Assert.AreEqual(BuildingType.Monastery, Favorite(w.S, out _));
        }

        [Test]
        public void HolySite_AttractsTemple()
        {
            var w = Make(AgeId.Iron, 30, SettlementTier.Hamlet, 0.7f, 2f);
            var map = w.Ctx.State.Map;
            for (int k = 0; k < 4; k++)
            {
                int i = map.Index(w.S.Center.X + 3, w.S.Center.Y + k);
                map.Owner[i] = w.S.Id;
                map.Set(i, TileFlags.Sacred);
            }
            RunOnce(w);
            Assert.Greater(AdvisorBoard.BuildBias(w.S, BuildingType.Temple), PerRun(0.5f));
            Assert.IsTrue(Logged(w.S, FaithAdvisor.ReasonHoly));
            Assert.AreEqual(4f, w.S.Mem(FaithAdvisorKeys.Sacred));
        }

        [Test]
        public void DestroyedShrine_IsRebuilt()
        {
            var w = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.5f, 1.5f);
            var b = Place(w, BuildingType.Shrine, true);
            w.Ctx.Ops.DestroyBuilding(b, DestroyCause.Fire);
            w.Ctx.Ops.FlushRemovals();
            RunOnce(w);
            Assert.Greater(AdvisorBoard.BuildBias(w.S, BuildingType.Shrine), PerRun(0.5f));
            Assert.IsTrue(Logged(w.S, "Rebuild the fallen Shrine"));
        }

        // ------------------------------------------------------------------ priests
        [Test]
        public void PriestVotes_ScaleWithPietyAndGap()
        {
            var pious = Make(AgeId.Iron, 30, SettlementTier.Hamlet, 0.9f, 0f);
            var impious = Make(AgeId.Iron, 30, SettlementTier.Hamlet, 0.1f, 0f);
            var served = Make(AgeId.Iron, 30, SettlementTier.Hamlet, 0.9f, 1.5f);
            Place(pious, BuildingType.Temple, true);
            Place(impious, BuildingType.Temple, true);
            Place(served, BuildingType.Temple, true);
            RunOnce(pious);
            RunOnce(impious);
            RunOnce(served);
            float p = AdvisorBoard.JobBias(pious.S, JobType.Priest);
            float i = AdvisorBoard.JobBias(impious.S, JobType.Priest);
            float s = AdvisorBoard.JobBias(served.S, JobType.Priest);
            Assert.Greater(p, PerRun(0.5f));
            Assert.Greater(p, i);
            Assert.Greater(i, 0f);
            Assert.Greater(p, s, "coverage gap raises priest demand");
        }

        [Test]
        public void NoTemple_NoPriestVote()
        {
            var w = Make(AgeId.Iron, 30, SettlementTier.Hamlet, 0.9f, 0f);
            RunOnce(w);
            Assert.AreEqual(0f, AdvisorBoard.JobBias(w.S, JobType.Priest));
        }

        // ------------------------------------------------------------------ determinism & safety
        private static string Scenario()
        {
            var w = Make(AgeId.Iron, 30, SettlementTier.Hamlet, 0.6f, 0.2f);
            Place(w, BuildingType.Temple, true);
            Cast(w, GodPowerType.Rain, w.S.Center);
            TestWorlds.Run(w.Advisor, w.Ctx, 36, false);
            Cast(w, GodPowerType.Plague, w.S.Center + new Vec2i(3, 0), 2);
            w.Ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Flood, Position = w.S.Center, Radius = 4, Severity = 0.6f });
            TestWorlds.Run(w.Advisor, w.Ctx, 60, false);
            var sb = new StringBuilder();
            for (int i = 0; i < Counts.Buildings; i++) sb.Append(AdvisorBoard.BuildBias(w.S, (BuildingType)i).ToString("R")).Append(',');
            for (int i = 0; i < Counts.Jobs; i++) sb.Append(AdvisorBoard.JobBias(w.S, (JobType)i).ToString("R")).Append(',');
            sb.Append(AdvisorBoard.Urgency(w.S, FaithAdvisor.Id).ToString("R")).Append(',');
            sb.Append(w.S.Mem(FaithAdvisorKeys.WrathMag).ToString("R")).Append(',').Append(w.S.Mem(FaithAdvisorKeys.Resentment).ToString("R"));
            return sb.ToString();
        }

        [Test]
        public void Deterministic_SameInputsSameVotes()
        {
            Assert.AreEqual(Scenario(), Scenario());
        }

        [Test]
        public void NaNInputs_DoNotPoisonState()
        {
            var w = Make(AgeId.Bronze, 12, SettlementTier.Camp, 0.9f, float.NaN);
            w.Civ.Devotion = float.NaN;
            w.Civ.GodFavor = float.PositiveInfinity;
            w.Ctx.State.God.Alignment = float.NaN;
            Cast(w, GodPowerType.Rain, w.S.Center);
            RunOnce(w);
            foreach (var kv in w.S.Memory)
                Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
        }
    }
}
