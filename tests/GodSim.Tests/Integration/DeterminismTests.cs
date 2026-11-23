using GodSim.Core;
using GodSim.Core.Composition;
using NUnit.Framework;
using Util = GodSim.Tests.Integration.IntegrationTestUtil;

namespace GodSim.Tests.Integration
{
    /// <summary>(b) Same seed => identical state hash every year for 25 years; a different seed diverges.</summary>
    [TestFixture]
    [Category("Slow")]
    public class DeterminismTests
    {
        private const int Years = 25;

        [Test]
        public void SameSeedProducesSameHash_DifferentSeedDiffers()
        {
            const ulong seed = 987654321UL;
            var a = GodSimulation.CreateNew(Util.SmallConfig(seed));
            var b = GodSimulation.CreateNew(Util.SmallConfig(seed));
            var c = GodSimulation.CreateNew(Util.SmallConfig(seed + 1));

            Assert.AreEqual(a.ComputeHash(), b.ComputeHash(), "worlds created from the same seed differ before the first tick");
            Assert.AreNotEqual(a.ComputeHash(), c.ComputeHash(), "worlds created from different seeds are identical");

            for (int year = 1; year <= Years; year++)
            {
                a.StepMany(SimTime.TicksPerYear);
                b.StepMany(SimTime.TicksPerYear);
                c.StepMany(SimTime.TicksPerYear);
                ulong ha = a.ComputeHash(), hb = b.ComputeHash();
                if (ha != hb)
                    Assert.Fail("same-seed worlds diverged in year " + year + " (0x" + ha.ToString("X16") + " vs 0x" + hb.ToString("X16") + ")\nA: " +
                                Util.Describe(a.State) + "B: " + Util.Describe(b.State));
            }

            Assert.AreEqual(a.Tick, b.Tick);
            Assert.AreEqual(a.State.LivingPopulation, b.State.LivingPopulation);
            Assert.AreNotEqual(a.ComputeHash(), c.ComputeHash(), "different seeds produced identical worlds after " + Years + " years");
        }

        [Test]
        public void ComputeHashDoesNotMutateState()
        {
            var a = Util.CreateAndRun(Util.SmallConfig(5UL, 64), 2);
            var b = Util.CreateAndRun(Util.SmallConfig(5UL, 64), 2);
            for (int i = 0; i < 5; i++) a.ComputeHash();
            a.StepMany(SimTime.TicksPerYear);
            b.StepMany(SimTime.TicksPerYear);
            Assert.AreEqual(b.ComputeHash(), a.ComputeHash());
        }
    }
}
