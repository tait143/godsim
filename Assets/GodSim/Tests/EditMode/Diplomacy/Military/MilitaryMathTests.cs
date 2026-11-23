using GodSim.Core;
using GodSim.Core.Diplomacy;
using NUnit.Framework;

namespace GodSim.Tests.Diplomacy.Military
{
    public class MilitaryMathTests
    {
        [TestCase(0, 0)]
        [TestCase(3, 0)]
        [TestCase(4, 2)]
        [TestCase(10, 7)]
        [TestCase(30, 21)]
        public void RaiseCount_TakesUpTo70Percent_WithMinimumGarrison(int free, int expected)
        {
            Assert.AreEqual(expected, MilitaryMath.RaiseCount(free));
        }

        [Test]
        public void Casualties_StayWithinBounds()
        {
            var rng = new Rng(42);
            for (int i = 0; i < 2000; i++)
            {
                int n = 1 + (i % 60);
                int lost = MilitaryMath.Casualties(rng, n, true);
                int won = MilitaryMath.Casualties(rng, n, false);
                Assert.That(lost >= 1 && lost <= n);
                Assert.That(lost <= System.Math.Ceiling(n * 0.25f) || lost == 1);
                Assert.That(won >= 0 && won <= System.Math.Ceiling(n * 0.10f));
            }
            Assert.AreEqual(0, MilitaryMath.Casualties(rng, 0, true));
        }

        [Test]
        public void CivilianCasualties_AreAtMostTwoPercentRoundedUp()
        {
            var rng = new Rng(3);
            for (int i = 0; i < 500; i++)
            {
                int c = MilitaryMath.CivilianCasualties(rng, 200);
                Assert.That(c >= 2 && c <= 4, "got " + c);
            }
        }

        [Test]
        public void WarScore_IsClamped()
        {
            Assert.AreEqual(100f, MilitaryMath.ApplyWarScore(95f, 25f));
            Assert.AreEqual(-100f, MilitaryMath.ApplyWarScore(-95f, -25f));
            Assert.AreEqual(5f, MilitaryMath.ApplyWarScore(5f, float.NaN));
        }

        [Test]
        public void ShouldCapture_RequiresLowDefenseAndAtMostOneSoldier()
        {
            Assert.IsTrue(MilitaryMath.ShouldCapture(1f, 1, 20f));
            Assert.IsFalse(MilitaryMath.ShouldCapture(1f, 2, 20f));
            Assert.IsFalse(MilitaryMath.ShouldCapture(3f, 0, 20f));
            Assert.IsFalse(MilitaryMath.ShouldCapture(0f, 0, 0f));
        }

        [Test]
        public void TileMoveCost_WaterImpassable_RoadsHalved()
        {
            var map = new WorldMap(16, 16);
            map.Biomes[0] = Biome.DeepOcean;
            map.Biomes[1] = Biome.Hills;
            map.Biomes[2] = Biome.Hills;
            map.Set(2, TileFlags.Road);
            Assert.IsTrue(float.IsPositiveInfinity(MilitaryMath.TileMoveCost(map, 0)));
            Assert.AreEqual(1.6f, MilitaryMath.TileMoveCost(map, 1), 1e-5f);
            Assert.AreEqual(0.8f, MilitaryMath.TileMoveCost(map, 2), 1e-5f);
            Assert.IsTrue(float.IsPositiveInfinity(MilitaryMath.TileMoveCost(map, -1)));
        }

        [Test]
        public void TargetScore_PrefersNearAndWeak()
        {
            float nearWeak = MilitaryMath.TargetScore(10f, 2f, 10f);
            float farWeak = MilitaryMath.TargetScore(10f, 2f, 60f);
            float nearStrong = MilitaryMath.TargetScore(10f, 40f, 10f);
            Assert.IsTrue(nearWeak > farWeak);
            Assert.IsTrue(nearWeak > nearStrong);
        }
    }
}
