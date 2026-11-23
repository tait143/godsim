using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Strategy
{
    /// <summary>Upgrades (3) posture/focus inputs + hysteresis + recovery, (4) wonder timing + material votes, (5) logs.</summary>
    public class StrategyMoodAndWonderTests
    {
        private GameDatabase _db;

        [SetUp]
        public void SetUp() { _db = StrategyTestSupport.CreateDb(); }

        private static int Posture(Civilization c) => (int)c.Mem(CivStrategyKeys.Posture, -1f);
        private static int Focus(Civilization c) => (int)c.Mem(CivStrategyKeys.Focus, -1f);

        private static int WonderOrders(Settlement s)
        {
            int n = 0;
            foreach (var o in s.BuildQueue) if (o.Reason.StartsWith("wonder: ")) n++;
            return n;
        }

        [Test]
        public void DynamicAggression_MakesElvesMilitarist()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(30, 30), 10, out var elf);
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(100, 100), 10, out var human);
            AdvisorBoard.SetTrait(elf, AITrait.Aggression, 1f);
            StrategyTestSupport.StartWar(ctx, elf, human);
            StrategyTestSupport.RunOnce(new CivilizationStrategySystem(), ctx);
            Assert.AreEqual(CivStrategyKeys.PostureMilitarist, Posture(elf));
        }

        [Test]
        public void WarWeariness_TurnsMilitaristIntoDefensive()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(30, 30), 10, out var orc);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(100, 100), 10, out var elf);
            StrategyTestSupport.StartWar(ctx, orc, elf);
            orc.Memory[CivStrategyKeys.WarWeariness] = 1f;
            StrategyTestSupport.RunOnce(new CivilizationStrategySystem(), ctx);
            Assert.AreEqual(CivStrategyKeys.PostureDefensive, Posture(orc));
            Assert.IsTrue(cap.AiLog.Exists(l => l.StartsWith("Posture: defensive (wars 1") && l.Contains("weariness 1.0")));
        }

        [Test]
        public void GodFear_PushesFaithFocus_AndIsExplained()
        {
            var ctx = TestWorlds.CreateContext(_db, 64);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 10, out var human);
            human.Memory[AdvisorBoard.GodFearKey] = 1f;
            StrategyTestSupport.RunOnce(new CivilizationStrategySystem(), ctx);
            Assert.AreEqual(CivStrategyKeys.FocusFaith, Focus(human));
            Assert.IsTrue(cap.AiLog.Exists(l => l.StartsWith("Focus: faith (score ") && l.Contains("fear 1.0")));
        }

        [Test]
        public void HostileArmyNearby_RaisesThreat_AndDefence()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(30, 30), 10, out var elf);
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(110, 110), 10, out var human);
            var rel = ctx.State.Diplomacy.Get(elf.Id, human.Id);
            rel.HasContact = true;
            rel.Stance = DiplomaticStance.Hostile;
            ctx.State.Diplomacy.Armies.Add(new Army { Id = ctx.State.Ids.Army(), CivilizationId = human.Id, Position = new Vec2i(38, 30), State = ArmyState.Marching });
            StrategyTestSupport.RunOnce(new CivilizationStrategySystem(), ctx);
            Assert.AreEqual(CivStrategyKeys.PostureDefensive, Posture(elf));
            Assert.GreaterOrEqual(elf.Mem(CivStrategyKeys.Threat), 0.3f);
        }

        [Test]
        public void Hysteresis_HoldsAMarginalChange_UntilTheDwellTimeHasPassed()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 10, out var human);
            var sys = new CivilizationStrategySystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(CivStrategyKeys.PosturePeaceful, Posture(human));

            // One hostile neighbour: defensive is better, but only by < UrgentMargin.
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(52, 30), 10, out var rival);
            var rel = ctx.State.Diplomacy.Get(human.Id, rival.Id);
            rel.HasContact = true;
            rel.Stance = DiplomaticStance.Hostile;
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(CivStrategyKeys.PosturePeaceful, Posture(human), "too soon to change for a marginal reason");

            TestWorlds.Run(sys, ctx, CivilizationStrategySystem.MinDwellTicks, initialize: false);
            Assert.AreEqual(CivStrategyKeys.PostureDefensive, Posture(human));
            Assert.IsTrue(cap.AiLog.Exists(l => l.StartsWith("Posture: peaceful -> defensive (wars 0, hostile neighbours 1")));

            // A war is drastic: it overrides the dwell time immediately.
            StrategyTestSupport.StartWar(ctx, rival, human);
            AdvisorBoard.SetTrait(human, AITrait.Aggression, 1f);
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(CivStrategyKeys.PostureMilitarist, Posture(human));
        }

        [Test]
        public void EconomyTrend_UsesThreeSamples_AndAFallingEconomyPausesWonders()
        {
            var ctx = TestWorlds.CreateContext(_db, 64);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 20, out var civ);
            cap.Stockpile.Capacity = 2000f;
            cap.Stockpile.SetRaw(ResourceType.Food, 600f);
            cap.Stockpile.SetRaw(ResourceType.Wood, 600f);
            cap.Stockpile.SetRaw(ResourceType.Stone, 600f);
            var sys = new CivilizationStrategySystem();
            StrategyTestSupport.RunOnce(sys, ctx);                 // sample 1 (Camp: no wonder anyway)
            Assert.AreEqual(1f, civ.Mem(CivStrategyKeys.EconomySampleCount));
            Assert.AreEqual(0f, civ.Mem(CivStrategyKeys.EconomyTrend));

            cap.Stockpile.SetRaw(ResourceType.Food, 100f);
            cap.Stockpile.SetRaw(ResourceType.Wood, 60f);
            cap.Stockpile.SetRaw(ResourceType.Stone, 60f);
            cap.Tier = SettlementTier.Town;                        // now big enough for the Great Library
            StrategyTestSupport.RunOnce(sys, ctx);                 // sample 2: collapsing
            Assert.Less(civ.Mem(CivStrategyKeys.EconomyTrend), -0.15f);
            Assert.AreEqual(CivilizationStrategySystem.PauseEconomy, (int)civ.Mem(CivStrategyKeys.WonderPaused));
            Assert.AreEqual(0, WonderOrders(cap));
            Assert.IsTrue(cap.AiLog.Exists(l => l.StartsWith("Wonder: new projects on hold (economy -")));

            StrategyTestSupport.RunOnce(sys, ctx);                 // sample 3: still below the oldest sample
            Assert.AreEqual(3f, civ.Mem(CivStrategyKeys.EconomySampleCount));
            Assert.AreEqual(0, WonderOrders(cap));

            StrategyTestSupport.RunOnce(sys, ctx);                 // the oldest (rich) sample drops out: flat
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(0f, civ.Mem(CivStrategyKeys.EconomyTrend), 1e-4f);
            Assert.AreEqual(1, WonderOrders(cap));
            Assert.IsTrue(cap.AiLog.Exists(l => l.StartsWith("Wonder: projects resumed")));
            Assert.IsTrue(cap.AiLog.Exists(l => l.StartsWith("Wonder: queued Great Library (focus ") && l.Contains("peace")));
        }

        [Test]
        public void Wonders_PausedDuringWarAndFamine_ResumeAfterwards()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(40, 40), 20, out var human);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(110, 110), 6, out var orc);
            cap.Tier = SettlementTier.Town;
            cap.Stockpile.SetRaw(ResourceType.Stone, 60f);
            var war = StrategyTestSupport.StartWar(ctx, orc, human);
            var sys = new CivilizationStrategySystem();
            TestWorlds.Run(sys, ctx, 24 * 3);
            Assert.AreEqual(0, WonderOrders(cap));
            Assert.AreEqual(1, cap.AiLog.FindAll(l => l == "Wonder: new projects on hold (at war x1)").Count, "logged once");

            war.IsActive = false;
            cap.Memory["econ.foodDays"] = 8f;
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(0, WonderOrders(cap));
            Assert.AreEqual(CivilizationStrategySystem.PauseFamine, (int)human.Mem(CivStrategyKeys.WonderPaused));

            cap.Memory["econ.foodDays"] = 60f;
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(1, WonderOrders(cap));
        }

        [Test]
        public void SavingForAWonder_VotesMaterialProducersAtTheCapital()
        {
            var ctx = TestWorlds.CreateContext(_db, 64);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 20, out var human);
            cap.Tier = SettlementTier.Town;
            cap.Stockpile.SetRaw(ResourceType.Stone, 20f); // 20% of 100: not affordable yet
            var sys = new CivilizationStrategySystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(0, WonderOrders(cap));
            Assert.Greater(AdvisorBoard.BuildBias(cap, BuildingType.StoneQuarry), 0.5f);
            Assert.IsTrue(cap.AiLog.Exists(l => l.StartsWith("Wonder: saving for ") && l.Contains("Stone 20/100") && l.Contains("vote Stone Quarry")));

            // Once queued, the order still needs the full cost: producers keep being voted while the cost is unpaid.
            cap.Stockpile.SetRaw(ResourceType.Stone, 60f);
            AdvisorBoard.Decay(cap, 0f);
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(1, WonderOrders(cap));
            AdvisorBoard.Decay(cap, 0f);
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.Greater(AdvisorBoard.BuildBias(cap, BuildingType.StoneQuarry), 0f);
        }

        [Test]
        public void Disaster_TriggersRecoveryFocus_ThatFades()
        {
            var ctx = TestWorlds.CreateContext(_db, 64);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 10, out var human);
            var sys = new CivilizationStrategySystem();
            sys.Initialize(ctx);
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreNotEqual(CivStrategyKeys.FocusRecovery, Focus(human));

            ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Earthquake, Position = new Vec2i(31, 31), Radius = 3, Severity = 0.9f });
            Assert.AreEqual(ctx.Tick, (long)human.Mem(CivStrategyKeys.DisasterTick));
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(CivStrategyKeys.FocusRecovery, Focus(human));
            Assert.AreEqual(CivStrategyKeys.FocusGrowth, (int)human.Mem(CivStrategyKeys.FocusLegacy), "legacy readers see growth");
            Assert.IsTrue(cap.AiLog.Exists(l => l.Contains("-> recovery (score ") && l.Contains("disaster 0.9")));

            TestWorlds.Run(sys, ctx, CivilizationStrategySystem.RecoveryTicks + 24, initialize: false);
            Assert.AreNotEqual(CivStrategyKeys.FocusRecovery, Focus(human));
        }

        [Test]
        public void DisasterFarAway_AndMinorStorms_DoNotTriggerRecovery()
        {
            var ctx = TestWorlds.CreateContext(_db, 128);
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 10, out var human);
            var sys = new CivilizationStrategySystem();
            sys.Initialize(ctx);
            ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Meteor, Position = new Vec2i(110, 110), Radius = 4, Severity = 1f });
            ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Lightning, Position = new Vec2i(30, 30), Radius = 1, Severity = 0.5f });
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreNotEqual(CivStrategyKeys.FocusRecovery, Focus(human));
        }

        [Test]
        public void PopulationCrash_CountsAsDisaster()
        {
            var ctx = TestWorlds.CreateContext(_db, 64);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), 40, out var human);
            var sys = new CivilizationStrategySystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            StrategyTestSupport.RunOnce(sys, ctx);
            for (int i = 0; i < 16; i++) ctx.Ops.KillPerson(ctx.State.People.Get(cap.ResidentIds[0]), DeathCause.Disease);
            ctx.Ops.FlushRemovals();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(CivStrategyKeys.FocusRecovery, Focus(human));
            Assert.AreEqual(1, cap.AiLog.FindAll(l => l.StartsWith("Crisis: population crash 24/40")).Count);
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(1, cap.AiLog.FindAll(l => l.StartsWith("Crisis: population crash")).Count, "one crash, one record");
        }

        [Test]
        public void FocusRecovery_IsAppendedAfterLegacyValues()
        {
            Assert.AreEqual(5, CivStrategyKeys.FocusRecovery);
            Assert.AreEqual(6, CivStrategyKeys.FocusCount);
            Assert.AreEqual("recovery", CivStrategyKeys.FocusName(CivStrategyKeys.FocusRecovery));
            Assert.AreEqual(CivStrategyKeys.FocusWealth, CivStrategyKeys.LegacyFocus(CivStrategyKeys.FocusWealth));
            Assert.AreEqual("ai.posture", CivStrategyKeys.Posture);
            Assert.AreEqual("ai.focus", CivStrategyKeys.Focus);
            Assert.AreEqual("ai.threat", CivStrategyKeys.Threat);
        }
    }
}
