using GodSim.Core;
using GodSim.Core.Composition;
using NUnit.Framework;
using Util = GodSim.Tests.Integration.IntegrationTestUtil;

namespace GodSim.Tests.Integration
{
    /// <summary>(f) Ages progress: with research x20, at least one civilization reaches the Iron age within 60 years.</summary>
    [TestFixture]
    [Category("Slow")]
    public class AgeProgressionSmokeTests
    {
        private const int MaxYears = 60;

        [Test]
        public void FastResearchReachesIronAgeWithinSixtyYears()
        {
            var config = Util.SmallConfig(31337UL, Util.SmallMap);
            config.ResearchMultiplier = 20f;
            var sim = GodSimulation.CreateNew(config);
            Assert.That(sim.Config.ResearchMultiplier, Is.EqualTo(20f).Within(1e-4f), "ResearchMultiplier was not applied");

            AgeId best = AgeId.Dawn;
            for (int year = 1; year <= MaxYears; year++)
            {
                sim.StepMany(SimTime.TicksPerYear);
                best = sim.State.WorldAge;
                if (Util.AnyCivAtLeast(sim.State, AgeId.Iron)) break;
            }

            Assert.IsTrue(Util.AnyCivAtLeast(sim.State, AgeId.Iron),
                "no civilization reached Iron in " + MaxYears + " years (best living: " + best + ")\n" + Util.Describe(sim.State) + Util.FaultReport(sim));
            Assert.AreEqual("", Util.FaultReport(sim));
        }
    }
}
