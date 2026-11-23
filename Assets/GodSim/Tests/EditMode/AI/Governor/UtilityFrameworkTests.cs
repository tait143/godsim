using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Governor
{
    public class UtilityFrameworkTests
    {
        [Test]
        public void Linear_NormalizesAndClamps()
        {
            var c = ResponseCurve.Linear(10f, 20f);
            Assert.AreEqual(0f, c.Evaluate(5f), 1e-6f);
            Assert.AreEqual(0f, c.Evaluate(10f), 1e-6f);
            Assert.AreEqual(0.5f, c.Evaluate(15f), 1e-6f);
            Assert.AreEqual(1f, c.Evaluate(20f), 1e-6f);
            Assert.AreEqual(1f, c.Evaluate(99f), 1e-6f);
        }

        [Test]
        public void DescendingRange_AndInvert()
        {
            var down = ResponseCurve.Linear(20f, 10f);
            Assert.AreEqual(1f, down.Evaluate(10f), 1e-6f);
            Assert.AreEqual(0f, down.Evaluate(20f), 1e-6f);
            var inv = ResponseCurve.Linear(10f, 20f, true);
            Assert.AreEqual(1f, inv.Evaluate(10f), 1e-6f);
            Assert.AreEqual(0.25f, inv.Evaluate(17.5f), 1e-6f);
        }

        [Test]
        public void Quadratic_IsConvexByDefault()
        {
            var c = ResponseCurve.Quadratic(0f, 1f);
            Assert.AreEqual(0.25f, c.Evaluate(0.5f), 1e-5f);
            var concave = ResponseCurve.Quadratic(0f, 1f, 0.5f);
            Assert.AreEqual(0.7071f, concave.Evaluate(0.5f), 1e-3f);
        }

        [Test]
        public void Logistic_HitsEndpointsAndMidpoint()
        {
            var c = ResponseCurve.Logistic(0f, 100f, 10f, 0.5f);
            Assert.AreEqual(0f, c.Evaluate(0f), 1e-5f);
            Assert.AreEqual(1f, c.Evaluate(100f), 1e-5f);
            Assert.AreEqual(0.5f, c.Evaluate(50f), 1e-4f);
            Assert.IsTrue(c.Evaluate(30f) < c.Evaluate(40f));
        }

        [Test]
        public void Inverse_FallsFromOneToZero()
        {
            var c = ResponseCurve.Inverse(0f, 1f, 4f);
            Assert.AreEqual(1f, c.Evaluate(0f), 1e-6f);
            Assert.AreEqual(0f, c.Evaluate(1f), 1e-6f);
            float mid = c.Evaluate(0.5f);
            Assert.IsTrue(mid < 0.5f && mid > 0f, "hyperbolic falloff is below the straight line: " + mid);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void BadInputs_NeverProduceNaN(float x)
        {
            var kinds = new[] { ResponseCurveKind.Linear, ResponseCurveKind.Quadratic, ResponseCurveKind.Logistic, ResponseCurveKind.Inverse };
            foreach (var k in kinds)
            {
                var c = new ResponseCurve(k, 0f, 1f, float.NaN, float.NaN);
                float y = c.Evaluate(x);
                Assert.IsFalse(float.IsNaN(y) || float.IsInfinity(y), k + " -> " + y);
                Assert.IsTrue(y >= 0f && y <= 1f);
            }
        }

        [Test]
        public void DegenerateRange_IsAStep()
        {
            var c = ResponseCurve.Linear(5f, 5f);
            Assert.AreEqual(0f, c.Evaluate(4f));
            Assert.AreEqual(1f, c.Evaluate(5f));
        }

        [Test]
        public void Consideration_WeightsAndCompensates()
        {
            var c = new Consideration("x", ResponseCurve.Linear(0f, 10f), 0.5f);
            Assert.AreEqual(0.25f, c.Score(5f), 1e-6f);
            Assert.AreEqual(0f, new Consideration("neg", ResponseCurve.Linear(0f, 1f), -3f).Score(1f));
            float raw = 0.5f * 0.5f;
            float comp = Consideration.CombineMultiplicative(new[] { 0.5f, 0.5f }, 2);
            Assert.IsTrue(comp > raw && comp <= 0.5f, "compensated product " + comp);
            Assert.AreEqual(0f, Consideration.CombineMultiplicative(new[] { 0.9f, 0f }, 2));
            Assert.AreEqual(0f, Consideration.CombineMultiplicative(null, 3));
        }

        [Test]
        public void Affordability_NowSoonNever()
        {
            var st = new Stockpile();
            var cost = new[] { new ResourceAmount(ResourceType.Wood, 20f) };
            st.SetRaw(ResourceType.Wood, 25f);
            Assert.AreEqual(1f, BuildingScorer.Affordability(cost, st, null));
            var reserved = new float[Counts.Resources];
            reserved[(int)ResourceType.Wood] = 10f;
            Assert.AreEqual(0.25f, BuildingScorer.Affordability(cost, st, reserved), "reserved stock is not spendable and there is no income");
            // positive income: 5 short at ~0.5/tick -> affordable within a year
            for (int i = 0; i < 60; i++) { st.Add(ResourceType.Wood, 0.5f); st.RollStatistics(0.2f); }
            st.SetRaw(ResourceType.Wood, 15f);
            Assert.AreEqual(0.6f, BuildingScorer.Affordability(cost, st, null));
        }

        [Test]
        public void RaceBias_FavoredAndPersonality()
        {
            var race = new RaceDefinition { Piety = 0f, FavoredBuildings = new[] { BuildingType.Farm } };
            var traits = new GovernorPersonality { Piety = 1f };
            var farm = new BuildingDefinition { Type = BuildingType.Farm, Category = BuildingCategory.Food };
            var shrine = new BuildingDefinition { Type = BuildingType.Shrine, Category = BuildingCategory.Religion };
            Assert.AreEqual(1.3f, BuildingScorer.RaceBias(farm, race, traits), 1e-5f);
            Assert.AreEqual(1.15f, BuildingScorer.RaceBias(shrine, race, traits), 1e-5f, "traits, not the race's static Piety, drive it");
            traits.Piety = 0f;
            Assert.AreEqual(0.85f, BuildingScorer.RaceBias(shrine, race, traits), 1e-5f);
        }

        [Test]
        public void Profile_IsDataDriven()
        {
            var def = new BuildingDefinition
            {
                Type = BuildingType.Smelter, Job = JobType.Smith, JobSlots = 2,
                InputsPerWorker = new[] { new ResourceAmount(ResourceType.Ore, 0.1f) },
                OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Metal, 0.05f), new ResourceAmount(ResourceType.Gold, float.NaN) },
                Services = new[] { new ServiceAmount(ServiceType.Commerce, 2f) },
            };
            var p = new BuildingProfile(def);
            Assert.AreEqual(0.1f, p.NetOutput[(int)ResourceType.Metal], 1e-6f);
            Assert.AreEqual(-0.2f, p.NetOutput[(int)ResourceType.Ore], 1e-6f);
            Assert.IsTrue(p.HasInputs);
            Assert.AreEqual(1f, p.GoldValue, 1e-6f, "NaN output ignored; half the commerce service counts as gold value");
            Assert.AreEqual(0f, p.Raw(SettlementNeeds.ResourceSlot(ResourceType.Ore)));
            Assert.AreEqual(0f, p.Raw(SettlementNeeds.ServiceSlot(ServiceType.Commerce)), "commerce folds into gold");
            var house = new BuildingProfile(new BuildingDefinition { Type = BuildingType.House, HousingMin = 3, HousingMax = 5 });
            Assert.AreEqual(4f, house.ExpectedHousing, 1e-6f);
        }

        [Test]
        public void ThreatEvaluation_PostureWarAndHostility()
        {
            var db = GovernorTestKit.CreateDb();
            var ctx = GovernorTestKit.CreateWorld(db, 10, out var s, out var civ);
            Assert.AreEqual(0f, NeedEvaluator.EvaluateThreat(ctx.State, s, civ, 40, out bool war, out bool hostile, out int posture));
            Assert.IsFalse(war || hostile);
            civ.Memory["ai.posture"] = 1f;
            Assert.AreEqual(0.4f, NeedEvaluator.EvaluateThreat(ctx.State, s, civ, 40, out war, out hostile, out posture), 1e-6f);
            Assert.AreEqual(1, posture);
            civ.Memory["ai.posture"] = 0f;

            var other = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(50, 50), 4, out var orcs);
            var rel = ctx.State.Diplomacy.Get(civ.Id, orcs.Id);
            rel.HasContact = true;
            rel.Stance = DiplomaticStance.Hostile;
            Assert.AreEqual(0.65f, NeedEvaluator.EvaluateThreat(ctx.State, s, civ, 40, out war, out hostile, out posture), 1e-6f);
            Assert.IsTrue(hostile);
            Assert.AreEqual(0f, NeedEvaluator.EvaluateThreat(ctx.State, s, civ, 10, out war, out hostile, out posture), 1e-6f, "too far away");
            rel.WarId = 1;
            rel.Stance = DiplomaticStance.War;
            Assert.AreEqual(1f, NeedEvaluator.EvaluateThreat(ctx.State, s, civ, 40, out war, out hostile, out posture), 1e-6f);
            Assert.IsTrue(war);
            Assert.AreEqual(0.85f, NeedEvaluator.EvaluateThreat(ctx.State, s, civ, 10, out war, out hostile, out posture), 1e-6f, "war far away");
            Assert.IsNotNull(other);
        }
    }
}
