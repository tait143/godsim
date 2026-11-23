using GodSim.Core;
using GodSim.Core.AI;
using NUnit.Framework;

namespace GodSim.Tests.AI.Governor
{
    public class AdvisorIntegrationTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp() { _db = GovernorTestKit.CreateDb(); }

        private static void RunOnce(SettlementGovernorSystem gov, SimContext ctx)
        {
            ctx.State.Tick += gov.TickInterval;
            ctx.DeltaTicks = gov.TickInterval;
            gov.Tick(ctx);
            ctx.Ops.FlushRemovals();
        }

        /// <summary>A town with every governor need satisfied (it orders nothing on its own).</summary>
        private SimContext ContentTown(out Settlement s, out Civilization civ)
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 10, out s, out civ);
            for (int i = 0; i < 4; i++) GovernorTestKit.Place(ctx, s, BuildingType.House, 5);
            GovernorTestKit.Place(ctx, s, BuildingType.GathererHut);
            GovernorTestKit.Place(ctx, s, BuildingType.Farm);
            GovernorTestKit.SetCoverage(s, 1f);
            s.Stockpile.SetRaw(ResourceType.Food, 100f);
            s.Stockpile.SetRaw(ResourceType.Wood, 100f);
            s.Stockpile.SetRaw(ResourceType.Stone, 60f);
            return ctx;
        }

        [Test]
        public void Baseline_ContentTownOrdersNothing()
        {
            var ctx = ContentTown(out var s, out _);
            RunOnce(new SettlementGovernorSystem(), ctx);
            Assert.AreEqual(0, s.BuildQueue.Count, string.Join("\n", s.AiLog));
        }

        [Test]
        public void PositiveCouncilBias_CarriesCandidateWithoutOwnNeed_AndIsExplained()
        {
            var ctx = ContentTown(out var s, out var civ);
            AdvisorBoard.VoteBuild(s, civ, "food", BuildingType.Storehouse, 0.9f, "winter stores");
            AdvisorBoard.VoteBuild(s, civ, "seasons", BuildingType.Storehouse, 0.6f, "autumn");
            AdvisorBoard.VoteBuild(s, civ, "faith", BuildingType.Shrine, 0.2f); // small advice: not enough on its own
            RunOnce(new SettlementGovernorSystem(), ctx);
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.Storehouse), string.Join("\n", s.AiLog));
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.Shrine));
            Assert.IsTrue(s.AiLog.Exists(l => l.StartsWith("Build Storehouse (u=") && l.Contains("; advisors: food +0.9, seasons +0.6)")), string.Join("\n", s.AiLog));
        }

        [Test]
        public void CouncilBiasWithoutAdvisorLogLines_FallsBackToTotal()
        {
            var ctx = ContentTown(out var s, out _);
            s.Memory[AdvisorBoard.BuildKey(BuildingType.Storehouse)] = 2f;
            RunOnce(new SettlementGovernorSystem(), ctx);
            Assert.IsTrue(s.AiLog.Exists(l => l.StartsWith("Build Storehouse") && l.Contains("advisors: council +2.0")), string.Join("\n", s.AiLog));
        }

        [Test]
        public void StrongNegativeBias_VetoesUnlessNeedIsCritical()
        {
            // Faith uncovered: the governor wants a shrine (need ~0.4, not critical).
            var ctx = ContentTown(out var s, out _);
            s.ServiceCoverage[(int)ServiceType.Faith] = 0f;
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.Shrine), "control");

            var ctx2 = ContentTown(out var s2, out _);
            s2.ServiceCoverage[(int)ServiceType.Faith] = 0f;
            s2.Memory[AdvisorBoard.BuildKey(BuildingType.Shrine)] = -1.5f;
            RunOnce(new SettlementGovernorSystem(), ctx2);
            Assert.IsFalse(GovernorTestKit.QueueHas(s2, BuildingType.Shrine), "council veto");

            // Critical housing need overrides a veto on houses.
            var ctx3 = GovernorTestKit.CreateWorld(_db, 12, out var s3, out _);
            s3.Memory[AdvisorBoard.BuildKey(BuildingType.House)] = -3f;
            RunOnce(new SettlementGovernorSystem(), ctx3);
            Assert.AreEqual(BuildingType.House, s3.BuildQueue[0].Type);
        }

        [Test]
        public void MildNegativeBias_LowersButDoesNotVeto()
        {
            var ctx = GovernorTestKit.CreateWorld(_db, 12, out var s, out _);
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            float plain = s.BuildQueue[0].Priority;
            var ctx2 = GovernorTestKit.CreateWorld(_db, 12, out var s2, out _);
            s2.Memory[AdvisorBoard.BuildKey(BuildingType.House)] = 0.5f;
            RunOnce(new SettlementGovernorSystem(), ctx2);
            Assert.AreEqual(plain + 10f * 0.35f * 0.5f, s2.BuildQueue[0].Priority, 0.05f, "+0.35 utility per bias unit");
        }

        [Test]
        public void Wonders_NeverOrdered_EvenWithMaxCouncilBias()
        {
            var ctx = ContentTown(out var s, out _);
            s.Memory[AdvisorBoard.BuildKey(BuildingType.GreatLibrary)] = AdvisorBoard.MaxTotal;
            s.Memory[AdvisorBoard.BuildKey(BuildingType.Temple)] = AdvisorBoard.MaxTotal; // age-gated
            s.Memory[AdvisorBoard.BuildKey(BuildingType.Herbalist)] = AdvisorBoard.MaxTotal; // race-restricted
            var gov = new SettlementGovernorSystem();
            for (int i = 0; i < 5; i++) RunOnce(gov, ctx);
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.GreatLibrary));
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.Temple));
            Assert.IsFalse(GovernorTestKit.QueueHas(s, BuildingType.Herbalist));
        }

        [Test]
        public void JobBias_AddedAndClamped()
        {
            var ctx = ContentTown(out var s, out var civ);
            var gov = new SettlementGovernorSystem();
            RunOnce(gov, ctx);
            float scholar = s.JobPriority[(int)JobType.Scholar];
            float miner = s.JobPriority[(int)JobType.Miner];
            s.Memory[AdvisorBoard.JobKey(JobType.Scholar)] = 1.25f;
            s.Memory[AdvisorBoard.JobKey(JobType.Soldier)] = 3f;    // 0.5 + 3 = 3.5 (above the governor's own 3 cap)
            s.Memory[AdvisorBoard.JobKey(JobType.Miner)] = -1f;     // 1 - 1 = 0 -> clamped to 0.1
            civ.Memory["ai.posture"] = 2f;                           // soldier 2.0 + 3 = 5 (max)
            RunOnce(gov, ctx);
            Assert.AreEqual(scholar + 1.25f, s.JobPriority[(int)JobType.Scholar], 1e-4f);
            Assert.AreEqual(5f, s.JobPriority[(int)JobType.Soldier], 1e-4f);
            Assert.AreEqual(0.1f, s.JobPriority[(int)JobType.Miner], 1e-4f);
            Assert.AreEqual(1f, miner, 1e-4f);
            Assert.AreEqual(0f, s.JobPriority[(int)JobType.None]);
        }

        [Test]
        public void DriftedTraits_ReplaceRaceDefaults()
        {
            var ctx = ContentTown(out var s, out var civ);
            var gov = new SettlementGovernorSystem();
            AdvisorBoard.SetTrait(civ, AITrait.Piety, 1f);
            AdvisorBoard.SetTrait(civ, AITrait.Curiosity, 0f);
            RunOnce(gov, ctx);
            Assert.AreEqual(1.75f, s.JobPriority[(int)JobType.Priest], 1e-4f);
            Assert.AreEqual(0.75f, s.JobPriority[(int)JobType.Scholar], 1e-4f);
            AdvisorBoard.SetTrait(civ, AITrait.Piety, 0f);
            RunOnce(gov, ctx);
            Assert.AreEqual(0.75f, s.JobPriority[(int)JobType.Priest], 1e-4f);
            var traits = new GovernorPersonality().Load(civ, ctx.Db);
            Assert.AreEqual(0f, traits.Piety);
            Assert.AreEqual(0.25f, NeedEvaluator.ServiceWeight(ServiceType.Faith, traits), 1e-4f);
            civ.Memory.Remove(AdvisorBoard.TraitKey(AITrait.Piety));
            Assert.AreEqual(ctx.Db.Race(RaceId.Human).Piety, new GovernorPersonality().Load(civ, ctx.Db).Piety, 1e-6f, "race default when no drift");
        }

        [Test]
        public void PiousCivilization_BuildsFaithFirst_CuriousOneDoesNot()
        {
            var ctx = ContentTown(out var s, out var civ);
            s.ServiceCoverage[(int)ServiceType.Faith] = 0.35f;
            AdvisorBoard.SetTrait(civ, AITrait.Piety, 1f);
            RunOnce(new SettlementGovernorSystem(), ctx);
            Assert.IsTrue(GovernorTestKit.QueueHas(s, BuildingType.Shrine), string.Join("\n", s.AiLog));

            var ctx2 = ContentTown(out var s2, out var civ2);
            s2.ServiceCoverage[(int)ServiceType.Faith] = 0.35f;
            AdvisorBoard.SetTrait(civ2, AITrait.Piety, 0f);
            RunOnce(new SettlementGovernorSystem(), ctx2);
            Assert.IsFalse(GovernorTestKit.QueueHas(s2, BuildingType.Shrine), string.Join("\n", s2.AiLog));
        }

        [Test]
        public void Stall_UnaffordableOrdersAreDroppedAfterAYear()
        {
            var ctx = ContentTown(out var s, out _);
            s.Stockpile.SetRaw(ResourceType.Wood, 0f);
            s.Stockpile.SetRaw(ResourceType.Stone, 0f);
            for (int i = 0; i < 4; i++) s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Barracks, Priority = 3f + i, RequestedTick = 0, Reason = "gov: test" });
            var gov = new SettlementGovernorSystem();
            for (int i = 0; i < SimTime.TicksPerYear / 6 + 2; i++) RunOnce(gov, ctx);
            Assert.IsTrue(s.AiLog.Exists(l => l.StartsWith("Stall: dropped order Barracks")), string.Join("\n", s.AiLog));
            Assert.IsFalse(s.BuildQueue.Exists(o => o.Priority == 3f && o.Reason == "gov: test"), "least urgent dropped first");
        }

        [Test]
        public void NewAge_FirstCurrentAgeBuildingGetsAdoptionPull()
        {
            var civ = new Civilization { Age = AgeId.Iron };
            var temple = _db.Building(BuildingType.Temple);
            Assert.AreEqual(1.25f, BuildingScorer.ModernityFactor(temple, civ, 0f), 1e-5f);
            Assert.AreEqual(1.12f, BuildingScorer.ModernityFactor(temple, civ, 2f), 1e-5f);
            civ.Age = AgeId.Medieval;
            Assert.AreEqual(1f, BuildingScorer.ModernityFactor(temple, civ, 0f), 1e-5f);
            civ.Age = AgeId.Renaissance;
            Assert.AreEqual(0.92f, BuildingScorer.ModernityFactor(temple, civ, 0f), 1e-5f, "dated");
            Assert.AreEqual(1f, BuildingScorer.ModernityFactor(_db.Building(BuildingType.House), civ, 0f), 1e-5f, "dwellings never dated");
        }

        [Test]
        public void Autumn_AnticipatesWinterFood()
        {
            float spring = FoodNeedAt(0);
            float autumn = FoodNeedAt(2 * SimTime.TicksPerSeason + 1);
            Assert.IsTrue(autumn > spring + 0.1f, "autumn " + autumn + " vs spring " + spring);
        }

        private float FoodNeedAt(long tick)
        {
            var ctx = ContentTown(out var s, out _);
            s.Stockpile.SetRaw(ResourceType.Food, 14f); // ~28 days
            ctx.State.Tick = tick;
            var gov = new SettlementGovernorSystem();
            gov.GovernSettlement(ctx, s);
            return gov.LastNeeds.Food;
        }
    }
}
