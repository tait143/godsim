using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Strategy
{
    /// <summary>Upgrade (1): multi-step age plans, dead ends, usable unlocks, single-decision crisis response.</summary>
    public class ResearchPlanningTests
    {
        private static List<string> PlanKeys(GameDatabase db, ResearchDirectorSystem sys)
        {
            var keys = new List<string>();
            foreach (int id in sys.LastPlan) keys.Add(db.Tech(id).Key);
            return keys;
        }

        private static SimContext World(GameDatabase db, out Civilization civ, out Settlement cap, RaceId race = RaceId.Human)
        {
            var ctx = TestWorlds.CreateContext(db, 64);
            cap = TestWorlds.AddSettlement(ctx, race, new Vec2i(32, 32), 10, out civ);
            return ctx;
        }

        [Test]
        public void Plan_MultiStep_PrerequisiteFirst_AndLoggedWithEstimate()
        {
            var db = StrategyTestSupport.CreatePlanDb(strongIron: true);
            var ctx = World(db, out var civ, out var cap);
            civ.Memory[CivStrategyKeys.ResearchPerTick] = 0.5f; // 48 points/year
            var sys = new ResearchDirectorSystem();
            StrategyTestSupport.RunOnce(sys, ctx);

            CollectionAssert.AreEqual(new[] { StrategyTestSupport.Fire, StrategyTestSupport.IronWorking }, PlanKeys(db, sys));
            Assert.AreEqual(StrategyTestSupport.Fire, db.Tech(civ.CurrentResearchTechId).Key, "the prerequisite comes first");
            Assert.AreEqual(2f, civ.Mem(CivStrategyKeys.ResearchPlanLength));
            // 120 points of techs (> 100 threshold) at 48/year = 2.5 years.
            Assert.IsTrue(cap.AiLog.Exists(l => l == "Path to Bronze: Fire -> Iron Working (2 techs, ~3 yrs)"), string.Join("\n", cap.AiLog));
            Assert.IsTrue(cap.AiLog.Exists(l => l.StartsWith("Research: Fire (score ") && l.Contains("path to Bronze step 1/2")));

            // The plan is only logged again when it changes.
            civ.KnownTechs.Add(db.TechIdOf(StrategyTestSupport.Fire));
            civ.CurrentResearchTechId = -1;
            StrategyTestSupport.RunOnce(sys, ctx);
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(StrategyTestSupport.IronWorking, db.Tech(civ.CurrentResearchTechId).Key);
            Assert.AreEqual(1, cap.AiLog.FindAll(l => l.StartsWith("Path to Bronze: Iron Working (1 tech")).Count);
        }

        [Test]
        public void Plan_CheapestValueFirst_SkipsDeadEnds_AndUnusableUnlocks()
        {
            var db = StrategyTestSupport.CreatePlanDb(strongIron: false);
            var ctx = World(db, out var civ, out _);
            var sys = new ResearchDirectorSystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            var plan = PlanKeys(db, sys);
            // Elf Gate is cheap but needs an elves-only tech: a dead end. Sailing unlocks a coastal harbor the inland civ cannot use.
            CollectionAssert.AreEqual(new[] { StrategyTestSupport.Fire, StrategyTestSupport.PlanFarming }, plan);
            Assert.IsTrue(plan.Contains(db.Tech(civ.CurrentResearchTechId).Key), "research follows the plan");

            // The same civ on a coast values the harbor and plans Sailing instead.
            var ctx2 = World(db, out var coastal, out var cap2);
            var map = ctx2.State.Map;
            for (int x = 26; x <= 38; x++) map.Set(map.Index(x, 26), TileFlags.Coastal);
            var sys2 = new ResearchDirectorSystem();
            StrategyTestSupport.RunOnce(sys2, ctx2);
            CollectionAssert.AreEqual(new[] { StrategyTestSupport.Fire, StrategyTestSupport.Sailing }, PlanKeys(db, sys2));

            // Elves can reach the Elf Gate path.
            var ctx3 = World(db, out var elves, out _, RaceId.Elf);
            var sys3 = new ResearchDirectorSystem();
            StrategyTestSupport.RunOnce(sys3, ctx3);
            Assert.IsTrue(PlanKeys(db, sys3).Contains(StrategyTestSupport.ElfGate));
        }

        [Test]
        public void Plan_ReportsUnreachableRequirement()
        {
            var db = StrategyTestSupport.CreatePlanDb(strongIron: false, required: 7); // humans can reach only 5 Dawn techs
            var ctx = World(db, out var civ, out var cap);
            var sys = new ResearchDirectorSystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(5, sys.LastPlan.Count);
            Assert.IsTrue(cap.AiLog.Exists(l => l.StartsWith("Path to Bronze: ") && l.Contains("2 more unreachable") && l.Contains("~? yrs")));
        }

        [Test]
        public void UnusableResourceBoost_ScoresLower_WithoutDeposits()
        {
            var db = StrategyTestSupport.CreatePlanDb(strongIron: false);
            var sys = new ResearchDirectorSystem();
            var ctxA = World(db, out var inland, out _);
            float without = sys.ScoreTech(ctxA, inland, db.TechByKey(StrategyTestSupport.Smelting));
            var ctxB = World(db, out var miners, out _);
            var map = ctxB.State.Map;
            map.Set(map.Index(30, 30), TileFlags.OreDeposit);
            float with = sys.ScoreTech(ctxB, miners, db.TechByKey(StrategyTestSupport.Smelting));
            Assert.Greater(with, without * 1.3f);
        }

        [Test]
        public void FoodCrisis_SwitchesValidResearch_InOneDecision_AndOnlyOnce()
        {
            var db = StrategyTestSupport.CreateDb();
            var ctx = TestWorlds.CreateContext(db, 64);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(32, 32), 40, out var civ);
            civ.CurrentResearchTechId = db.TechIdOf(StrategyTestSupport.Lore);
            var sys = new ResearchDirectorSystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(StrategyTestSupport.Lore, db.Tech(civ.CurrentResearchTechId).Key, "no crisis: keep the valid choice");

            cap.Memory["econ.foodDays"] = 5f;
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(StrategyTestSupport.Farming, db.Tech(civ.CurrentResearchTechId).Key);
            Assert.IsTrue(cap.AiLog.Exists(l => l.StartsWith("Research: Farming (score ") && l.Contains("crisis: food 5d") && l.Contains("was Lore")));

            // The player picks something else mid-crisis: the director does not fight it (the onset was already handled).
            civ.CurrentResearchTechId = db.TechIdOf(StrategyTestSupport.Rites);
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(StrategyTestSupport.Rites, db.Tech(civ.CurrentResearchTechId).Key);
        }

        [Test]
        public void WarOnset_SwitchesToMilitaryTech_OrItsPrerequisite()
        {
            var db = StrategyTestSupport.CreateDb();
            var ctx = TestWorlds.CreateContext(db, 64);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(32, 32), 10, out var elf);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(8, 8), 4, out var orc);
            elf.CurrentResearchTechId = db.TechIdOf(StrategyTestSupport.Lore);
            var sys = new ResearchDirectorSystem();
            StrategyTestSupport.RunOnce(sys, ctx);
            StrategyTestSupport.StartWar(ctx, orc, elf);
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(StrategyTestSupport.Tools, db.Tech(elf.CurrentResearchTechId).Key, "first step toward Spears");
            Assert.IsTrue(cap.AiLog.Exists(l => l.Contains("crisis: war, step toward crisis tech")));

            elf.KnownTechs.Add(db.TechIdOf(StrategyTestSupport.Tools));
            elf.CurrentResearchTechId = -1;
            StrategyTestSupport.RunOnce(sys, ctx);
            Assert.AreEqual(StrategyTestSupport.Spears, db.Tech(elf.CurrentResearchTechId).Key);
        }

        [Test]
        public void DynamicTraits_OverrideRaceDefaults()
        {
            var db = StrategyTestSupport.CreateDb();
            var ctx = TestWorlds.CreateContext(db, 64);
            TestWorlds.AddSettlement(ctx, RaceId.Saurian, new Vec2i(32, 32), 10, out var civ);
            AdvisorBoard.SetTrait(civ, AITrait.Piety, 0f);
            AdvisorBoard.SetTrait(civ, AITrait.Curiosity, 1f);
            StrategyTestSupport.RunOnce(new ResearchDirectorSystem(), ctx);
            Assert.AreEqual(StrategyTestSupport.Lore, db.Tech(civ.CurrentResearchTechId).Key, "a curious, impious saurian studies instead of praying");
        }
    }
}
