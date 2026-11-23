using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Learning
{
    public class AdvisorLearningTests
    {
        private static readonly Vec2i At = new Vec2i(32, 32);

        private static SimContext MakeWorld(ulong seed, out Civilization civ, out Settlement s, int adults = 12)
        {
            var ctx = TestWorlds.CreateContext(TestDb.CreateStub(), 64, seed);
            s = TestWorlds.AddSettlement(ctx, RaceId.Human, At, adults, out civ);
            return ctx;
        }

        /// <summary>Advances to the next evaluation tick and runs the learner once.</summary>
        private static void Evaluate(AdvisorLearningSystem sys, SimContext ctx)
        {
            ctx.State.Tick += sys.TickInterval;
            ctx.DeltaTicks = sys.TickInterval;
            sys.Tick(ctx);
        }

        private static void SetFood(Settlement s, float days) => s.Memory[AILearningKeys.EconFoodDays] = days;

        // ------------------------------------------------------------------ scorecard
        [Test]
        public void Scorecard_ValuesInUnitRange_OnHandBuiltWorld()
        {
            var ctx = MakeWorld(3, out var civ, out var s);
            s.Stockpile.SetRaw(ResourceType.Food, 40f);
            s.Stockpile.SetRaw(ResourceType.Gold, 25f);
            s.Homeless = 4;
            s.Health = 0.7f;
            s.Happiness = 0.55f;
            s.Unrest = 0.2f;
            s.ServiceOutput[(int)ServiceType.Research] = 0.3f;
            s.ServiceCoverage[(int)ServiceType.Defense] = 0.5f;
            civ.Memory[AILearningKeys.StrategyThreat] = 0.6f;
            var site = ctx.Ops.FindFreeSite(s.Center, 1, 1, 20);
            ctx.Ops.CreateBuilding(s, BuildingType.House, site);
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.House, Attempts = 5 });
            var empty = ctx.Ops.CreateCivilization(RaceId.Elf); // civ without settlements

            for (int round = 0; round < 4; round++)
            {
                ctx.State.Tick += 192;
                if (round == 2) { s.Health = float.NaN; s.Happiness = float.PositiveInfinity; civ.TotalResearch = float.NaN; }
                float overall = AIScorecard.Evaluate(ctx.State, ctx.Db, civ);
                Assert.IsTrue(overall >= 0f && overall <= 1f, "overall " + overall);
                for (int m = 0; m < AILearningKeys.MetricCount; m++)
                {
                    Assert.IsTrue(civ.Memory.TryGetValue(AILearningKeys.ScoreKey((AIMetric)m), out float v), "missing " + (AIMetric)m);
                    Assert.IsTrue(v >= 0f && v <= 1f && !float.IsNaN(v), (AIMetric)m + " = " + v);
                }
                AIScorecard.Evaluate(ctx.State, ctx.Db, empty);
                for (int m = 0; m < AILearningKeys.MetricCount; m++)
                {
                    float v = AIScorecard.Get(empty, (AIMetric)m);
                    Assert.IsTrue(v >= 0f && v <= 1f && !float.IsNaN(v));
                }
            }
            foreach (var kv in civ.Memory) Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
        }

        [Test]
        public void Scorecard_ReflectsFoodAndHousing()
        {
            var ctx = MakeWorld(4, out var civ, out var s);
            SetFood(s, 3f);
            s.Homeless = 6;
            AIScorecard.Evaluate(ctx.State, ctx.Db, civ);
            float poorFood = AIScorecard.Get(civ, AIMetric.Food);
            float housing = AIScorecard.Get(civ, AIMetric.Housing);
            Assert.AreEqual(0.5f, housing, 1e-4f);
            SetFood(s, 200f);
            s.Homeless = 0;
            AIScorecard.Evaluate(ctx.State, ctx.Db, civ);
            Assert.Less(poorFood, 0.1f);
            Assert.AreEqual(1f, AIScorecard.Get(civ, AIMetric.Food), 1e-4f);
            Assert.AreEqual(1f, AIScorecard.Get(civ, AIMetric.Housing), 1e-4f);
        }

        // ------------------------------------------------------------------ credit assignment
        [Test]
        public void PoorNonImprovingFood_WithHighUrgency_RaisesFoodWeight()
        {
            var ctx = MakeWorld(7, out var civ, out var s);
            var sys = new AdvisorLearningSystem();
            sys.Initialize(ctx);
            SetFood(s, 2f);
            AdvisorBoard.ReportUrgency(s, "food", 0.95f);
            Evaluate(sys, ctx); // baseline observation
            float before = AdvisorBoard.Weight(civ, "food");
            Evaluate(sys, ctx);
            float after = AdvisorBoard.Weight(civ, "food");
            Assert.Greater(after, before + 0.02f);
            Assert.AreEqual(LearningReason.Heeded, AILearningQueries.LastReason(civ, "food"));
            Assert.AreEqual(after - before, AILearningQueries.LastChange(civ, "food"), 1e-5f);
        }

        [Test]
        public void CryingWolf_AdvisorIsLowered()
        {
            var ctx = MakeWorld(8, out var civ, out var s);
            var sys = new AdvisorLearningSystem();
            sys.Initialize(ctx);
            SetFood(s, 500f); // plenty of food
            AdvisorBoard.ReportUrgency(s, "food", 1f);
            Evaluate(sys, ctx);
            float before = AdvisorBoard.Weight(civ, "food");
            Evaluate(sys, ctx);
            Assert.Less(AdvisorBoard.Weight(civ, "food"), before - 0.01f);
            Assert.AreEqual(LearningReason.CryingWolf, AILearningQueries.LastReason(civ, "food"));
        }

        [Test]
        public void SilentAdvisors_AreNotJudged()
        {
            var ctx = MakeWorld(9, out var civ, out var s);
            var sys = new AdvisorLearningSystem();
            SetFood(s, 1f);
            for (int i = 0; i < 4; i++) Evaluate(sys, ctx);
            Assert.IsFalse(civ.Memory.ContainsKey(AdvisorBoard.WeightKey("food")));
            Assert.AreEqual(1f, AdvisorBoard.Weight(civ, "health"));
        }

        [Test]
        public void ExcellentAndCalm_RelaxesTowardOne()
        {
            var ctx = MakeWorld(10, out var civ, out var s);
            var sys = new AdvisorLearningSystem();
            SetFood(s, 500f);
            AdvisorBoard.SetWeight(civ, "food", 1.8f);
            AdvisorBoard.ReportUrgency(s, "food", 0.05f);
            Evaluate(sys, ctx);
            float prev = AdvisorBoard.Weight(civ, "food");
            for (int i = 0; i < 30; i++) Evaluate(sys, ctx);
            float w = AdvisorBoard.Weight(civ, "food");
            Assert.Less(w, prev);
            Assert.AreEqual(1f, w, 0.1f);
        }

        [Test]
        public void Weights_AreBounded_AndChangeLimited()
        {
            var ctx = MakeWorld(11, out var civ, out var s);
            var sys = new AdvisorLearningSystem();
            SetFood(s, 1f);
            s.Health = 1f; // health is fine...
            AdvisorBoard.ReportUrgency(s, "food", 1f);
            AdvisorBoard.ReportUrgency(s, "health", 1f); // ...but the physician screams
            float lastFood = AdvisorBoard.Weight(civ, "food"), lastHealth = AdvisorBoard.Weight(civ, "health");
            for (int i = 0; i < 60; i++)
            {
                Evaluate(sys, ctx);
                float f = AdvisorBoard.Weight(civ, "food"), h = AdvisorBoard.Weight(civ, "health");
                Assert.LessOrEqual(Math.Abs(f - lastFood), AdvisorLearningSystem.MaxStep + 1e-5f);
                Assert.LessOrEqual(Math.Abs(h - lastHealth), AdvisorLearningSystem.MaxStep + 1e-5f);
                Assert.IsTrue(f >= AdvisorLearningSystem.MinWeight && f <= AdvisorLearningSystem.MaxWeight);
                Assert.IsTrue(h >= AdvisorLearningSystem.MinWeight && h <= AdvisorLearningSystem.MaxWeight);
                lastFood = f; lastHealth = h;
            }
            Assert.Greater(lastFood, 2.0f);
            Assert.Less(lastHealth, 0.5f);
        }

        [Test]
        public void CreditAndStep_AreFiniteAndBounded_ForGarbageInputs()
        {
            float[] vals = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -5f, -0.1f, 0f, 0.3f, 0.5f, 0.7f, 0.9f, 1f, 7f };
            foreach (var m in vals)
                foreach (var t in vals)
                    foreach (var u in vals)
                        foreach (var w in vals)
                        {
                            float d = AdvisorLearningSystem.Credit(m, t, u, w, 1f, out _);
                            Assert.IsFalse(float.IsNaN(d) || float.IsInfinity(d));
                            float next = AdvisorLearningSystem.Step(w, d * 100f);
                            Assert.IsTrue(next >= AdvisorLearningSystem.MinWeight && next <= AdvisorLearningSystem.MaxWeight);
                            float sw = MathUtil.Clamp(MathUtil.Sanitize(w, 1f), AdvisorLearningSystem.MinWeight, AdvisorLearningSystem.MaxWeight);
                            Assert.LessOrEqual(Math.Abs(next - sw), AdvisorLearningSystem.MaxStep + 1e-5f);
                        }
        }

        [Test]
        public void Learning_IsDeterministic()
        {
            ulong a = Run(42, out var wa);
            ulong b = Run(42, out var wb);
            Assert.AreEqual(a, b);
            CollectionAssert.AreEqual(wa, wb);
        }

        private static ulong Run(ulong seed, out List<float> weights)
        {
            var ctx = MakeWorld(seed, out var civ, out var s);
            var sys = new AdvisorLearningSystem();
            var sampler = new AdvisorUrgencySampler();
            sys.Initialize(ctx);
            string[] ids = { "food", "health", "treasury", "defense", "science" };
            for (int round = 0; round < 12; round++)
            {
                SetFood(s, 5f + round * 7f);
                s.Health = 0.3f + 0.05f * round;
                for (int k = 0; k < ids.Length; k++) AdvisorBoard.ReportUrgency(s, ids[k], ((round * 3 + k * 5) % 10) / 9f);
                sampler.Tick(ctx);
                Evaluate(sys, ctx);
            }
            weights = new List<float>();
            for (int k = 0; k < ids.Length; k++) weights.Add(AdvisorBoard.Weight(civ, ids[k]));
            return ctx.State.Rng.State;
        }

        [Test]
        public void Sampler_UsesAverageUrgencyOverThePeriod()
        {
            var ctx = MakeWorld(12, out var civ, out var s);
            var sys = new AdvisorLearningSystem();
            var sampler = new AdvisorUrgencySampler();
            SetFood(s, 1f);
            AdvisorBoard.ReportUrgency(s, "food", 0.9f);
            Evaluate(sys, ctx);
            float before = AdvisorBoard.Weight(civ, "food");
            for (int i = 0; i < 7; i++) sampler.Tick(ctx); // worried all period...
            AdvisorBoard.ReportUrgency(s, "food", 0.1f);  // ...calm only at the very end
            sampler.Tick(ctx);
            Assert.IsTrue(AdvisorLearningSystem.AverageUrgency(ctx.State, civ, AdvisorDomains.Find("food"), out float avg));
            Assert.AreEqual(0.8f, avg, 1e-4f);
            Evaluate(sys, ctx);
            Assert.Greater(AdvisorBoard.Weight(civ, "food"), before);
            Assert.AreEqual(0f, civ.Mem(AdvisorDomains.Find("food").UrgencyWeightKey), "samples reset after evaluation");
        }

        // ------------------------------------------------------------------ priors & queries
        [Test]
        public void Personality_ShapesPriors()
        {
            var ctx = MakeWorld(13, out var civ, out _);
            AdvisorBoard.SetTrait(civ, AITrait.Aggression, 1f);
            AdvisorBoard.SetTrait(civ, AITrait.Diplomacy, 0f);
            float food = AdvisorDomains.Prior(civ, ctx.Db, AdvisorDomains.Find("food"));
            float defense = AdvisorDomains.Prior(civ, ctx.Db, AdvisorDomains.Find("defense"));
            float war = AdvisorDomains.Prior(civ, ctx.Db, AdvisorDomains.Find("war"));
            Assert.AreEqual(1f, food, 1e-6f);
            Assert.Greater(defense, 1.1f);
            Assert.Greater(war, defense);
            Assert.LessOrEqual(war, AdvisorDomains.MaxPrior);
            new AdvisorLearningSystem().Initialize(ctx);
            Assert.AreEqual(war, AdvisorBoard.Weight(civ, "war"), 1e-6f);
            Assert.AreEqual(1f, AdvisorBoard.Weight(civ, "food"), 1e-6f);
        }

        [Test]
        public void TopAdvisors_RanksByWeight_AndSummarises()
        {
            var ctx = MakeWorld(14, out var civ, out _);
            Assert.AreEqual("The Council heeds all advisors equally", AILearningQueries.CouncilSummary(civ));
            AdvisorBoard.SetWeight(civ, "food", 1.6f);
            AdvisorBoard.SetWeight(civ, "treasury", 1.9f);
            AdvisorBoard.SetWeight(civ, "war", 0.5f);
            var top = AILearningQueries.TopAdvisors(civ, 2);
            CollectionAssert.AreEqual(new[] { "treasury", "food" }, top);
            Assert.AreEqual(3, AILearningQueries.TopAdvisors(civ, 10).Count);
            Assert.AreEqual(0, AILearningQueries.TopAdvisors(civ, 0).Count);
            Assert.AreEqual("The Council heeds the Treasurer and the Granary-keeper", AILearningQueries.CouncilSummary(civ, 2));
            Assert.AreEqual(0, AILearningQueries.TopAdvisors(null, 3).Count);
        }

        [Test]
        public void System_RunsThroughEngineInterval_WithoutFaults()
        {
            var ctx = MakeWorld(15, out var civ, out var s);
            SetFood(s, 2f);
            AdvisorBoard.ReportUrgency(s, "food", 1f);
            var systems = new List<ISimSystem>(AILearningModule.CreateSystems());
            TestWorlds.RunAll(systems, ctx, 192 * 4);
            Assert.Greater(civ.Mem(AILearningKeys.Evaluations), 2f);
            Assert.Greater(AdvisorBoard.Weight(civ, "food"), 1.05f);
        }
    }
}
