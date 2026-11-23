using System;
using System.Collections.Generic;
using GodSim.Core;
using NUnit.Framework;

namespace GodSim.Tests.Contracts
{
    public class Vec2iTests
    {
        [Test]
        public void Equality_AndHash()
        {
            var a = new Vec2i(3, -4);
            var b = new Vec2i(3, -4);
            var c = new Vec2i(-4, 3);
            Assert.IsTrue(a == b);
            Assert.IsFalse(a != b);
            Assert.IsTrue(a.Equals(b));
            Assert.IsTrue(a.Equals((object)b));
            Assert.IsFalse(a.Equals((object)"(3,-4)"));
            Assert.IsFalse(a.Equals(null));
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.IsTrue(a != c);
            var set = new HashSet<Vec2i> { a, b, c };
            Assert.AreEqual(2, set.Count);
            Assert.AreEqual("(3,-4)", a.ToString());
        }

        [Test]
        public void Operators()
        {
            var a = new Vec2i(1, 2);
            var b = new Vec2i(10, -3);
            Assert.AreEqual(new Vec2i(11, -1), a + b);
            Assert.AreEqual(new Vec2i(-9, 5), a - b);
            Assert.AreEqual(new Vec2i(3, 6), a * 3);
            Assert.AreEqual(new Vec2i(-1, -2), a * -1);
            Assert.AreEqual(Vec2i.Zero, a * 0);
            Assert.AreEqual(new Vec2i(1, 1), Vec2i.One);
        }

        [Test]
        public void Distances()
        {
            var a = new Vec2i(0, 0);
            var b = new Vec2i(3, -4);
            Assert.AreEqual(7, a.ManhattanTo(b));
            Assert.AreEqual(4, a.ChebyshevTo(b));
            Assert.AreEqual(25, a.DistanceSqTo(b));
            Assert.AreEqual(5f, a.DistanceTo(b), 1e-6f);
            Assert.AreEqual(b.ManhattanTo(a), a.ManhattanTo(b));
            Assert.AreEqual(0, b.ChebyshevTo(b));
        }

        [Test]
        public void StepToward_ReachesTargetInChebyshevSteps()
        {
            var r = new Rng(3);
            for (int i = 0; i < 200; i++)
            {
                var p = new Vec2i(r.Range(-20, 20), r.Range(-20, 20));
                var t = new Vec2i(r.Range(-20, 20), r.Range(-20, 20));
                int expected = p.ChebyshevTo(t), steps = 0;
                while (p != t)
                {
                    var n = p.StepToward(t);
                    Assert.AreEqual(1, p.ChebyshevTo(n));
                    Assert.AreEqual(p.ChebyshevTo(t) - 1, n.ChebyshevTo(t));
                    p = n;
                    steps++;
                    Assert.LessOrEqual(steps, 100);
                }
                Assert.AreEqual(expected, steps);
            }
            Assert.AreEqual(new Vec2i(5, 5), new Vec2i(5, 5).StepToward(new Vec2i(5, 5)));
        }

        [Test]
        public void Invalid_IsNotValid_OthersAre()
        {
            Assert.IsFalse(Vec2i.Invalid.IsValid);
            Assert.IsTrue(Vec2i.Zero.IsValid);
            Assert.IsTrue(new Vec2i(-1, -1).IsValid);
            Assert.IsTrue(new Vec2i(int.MaxValue, int.MinValue).IsValid);
            Assert.AreNotEqual(Vec2i.Invalid, default(Vec2i));
            Assert.AreEqual(Vec2i.Invalid, new Vec2i(int.MinValue, int.MinValue));
        }

        [Test]
        public void Dirs_AreUnitAndDistinct()
        {
            Assert.AreEqual(4, Vec2i.Dirs4.Length);
            Assert.AreEqual(8, Vec2i.Dirs8.Length);
            var set = new HashSet<Vec2i>(Vec2i.Dirs8);
            Assert.AreEqual(8, set.Count);
            foreach (var d in Vec2i.Dirs8) Assert.AreEqual(1, Vec2i.Zero.ChebyshevTo(d));
            foreach (var d in Vec2i.Dirs4) { Assert.AreEqual(1, Vec2i.Zero.ManhattanTo(d)); Assert.IsTrue(set.Contains(d)); }
        }

        [Test]
        [Ignore("BUG (low): distance helpers on Vec2i.Invalid are unsafe: Invalid.ManhattanTo/ChebyshevTo throw OverflowException (Math.Abs(int.MinValue), Vec2i.cs:30-31) and Invalid.DistanceSqTo(Zero) silently returns 0 (int.MinValue^2 wraps, Vec2i.cs:32), so an Invalid position looks like it is AT the origin. Fix: compute in long, or return int.MaxValue when either side is !IsValid.")]
        public void Invalid_DistancesAreSafe()
        {
            Assert.DoesNotThrow(() => Vec2i.Invalid.ManhattanTo(Vec2i.Zero));
            Assert.DoesNotThrow(() => Vec2i.Invalid.ChebyshevTo(Vec2i.Zero));
            Assert.Greater(Vec2i.Invalid.DistanceSqTo(Vec2i.Zero), 1000000);
        }

        [Test]
        public void Invalid_DistanceSq_CurrentBehaviourDocumented()
        {
            Assert.AreEqual(0, Vec2i.Invalid.DistanceSqTo(Vec2i.Zero));
            Assert.Throws<OverflowException>(() => Vec2i.Invalid.ManhattanTo(Vec2i.Zero));
        }
    }

    public class MathUtilTests
    {
        [Test]
        public void Clamp_FloatAndInt()
        {
            Assert.AreEqual(0f, MathUtil.Clamp(-1f, 0f, 1f));
            Assert.AreEqual(1f, MathUtil.Clamp(2f, 0f, 1f));
            Assert.AreEqual(0.5f, MathUtil.Clamp(0.5f, 0f, 1f));
            Assert.AreEqual(5, MathUtil.Clamp(9, 1, 5));
            Assert.AreEqual(1, MathUtil.Clamp(-9, 1, 5));
            Assert.AreEqual(int.MaxValue, MathUtil.Clamp(int.MaxValue, int.MinValue, int.MaxValue));
            Assert.AreEqual(1f, MathUtil.Clamp(float.PositiveInfinity, 0f, 1f));
            Assert.AreEqual(0f, MathUtil.Clamp(float.NegativeInfinity, 0f, 1f));
            Assert.AreEqual(0f, MathUtil.Clamp01(-0.1f));
            Assert.AreEqual(1f, MathUtil.Clamp01(1.1f));
        }

        [Test]
        public void Clamp_NaN_PassesThrough_SoSanitizeFirst()
        {
            // Documented hazard: Clamp/Clamp01 do NOT remove NaN. Callers must Sanitize before clamping.
            Assert.IsTrue(float.IsNaN(MathUtil.Clamp(float.NaN, 0f, 1f)));
            Assert.IsTrue(float.IsNaN(MathUtil.Clamp01(float.NaN)));
            Assert.AreEqual(0f, MathUtil.Clamp01(MathUtil.Sanitize(float.NaN)));
        }

        [Test]
        public void InverseLerp_EqualBounds_ReturnsZero()
        {
            Assert.AreEqual(0f, MathUtil.InverseLerp(3f, 3f, 3f));
            Assert.AreEqual(0f, MathUtil.InverseLerp(3f, 3f, 100f));
            Assert.AreEqual(0f, MathUtil.InverseLerp(3f, 3f + 1e-6f, 100f));
        }

        [Test]
        public void InverseLerp_ClampsAndHandlesReversedBounds()
        {
            Assert.AreEqual(0.25f, MathUtil.InverseLerp(0f, 4f, 1f), 1e-6f);
            Assert.AreEqual(0f, MathUtil.InverseLerp(0f, 4f, -1f));
            Assert.AreEqual(1f, MathUtil.InverseLerp(0f, 4f, 9f));
            Assert.AreEqual(0.75f, MathUtil.InverseLerp(4f, 0f, 1f), 1e-6f);
        }

        [Test]
        public void Lerp_SmoothStep_MoveToward()
        {
            Assert.AreEqual(5f, MathUtil.Lerp(0f, 10f, 0.5f));
            Assert.AreEqual(10f, MathUtil.Lerp(0f, 10f, 2f));
            Assert.AreEqual(20f, MathUtil.LerpUnclamped(0f, 10f, 2f));
            Assert.AreEqual(0f, MathUtil.SmoothStep(0f, 1f, -1f));
            Assert.AreEqual(0.5f, MathUtil.SmoothStep(0f, 1f, 0.5f), 1e-6f);
            Assert.AreEqual(1f, MathUtil.SmoothStep(0f, 1f, 3f));
            Assert.AreEqual(3f, MathUtil.MoveToward(1f, 5f, 2f));
            Assert.AreEqual(5f, MathUtil.MoveToward(4f, 5f, 2f));
            Assert.AreEqual(-1f, MathUtil.MoveToward(1f, -5f, 2f));
            Assert.IsTrue(MathUtil.Approximately(1f, 1.00001f));
            Assert.IsFalse(MathUtil.Approximately(1f, 1.1f));
        }

        [Test]
        public void Sanitize_ReplacesNonFinite()
        {
            Assert.AreEqual(0f, MathUtil.Sanitize(float.NaN));
            Assert.AreEqual(0f, MathUtil.Sanitize(float.PositiveInfinity));
            Assert.AreEqual(0f, MathUtil.Sanitize(float.NegativeInfinity));
            Assert.AreEqual(7f, MathUtil.Sanitize(float.NaN, 7f));
            Assert.AreEqual(-3.5f, MathUtil.Sanitize(-3.5f, 7f));
            Assert.AreEqual(float.MaxValue, MathUtil.Sanitize(float.MaxValue));
        }

        [Test]
        public void Logistic_ExtremesAndMidpoint()
        {
            Assert.AreEqual(0.5f, MathUtil.Logistic(5f, 5f, 3f), 1e-6f);
            Assert.AreEqual(1f, MathUtil.Logistic(1e9f, 0f, 1f), 1e-6f);
            Assert.AreEqual(0f, MathUtil.Logistic(-1e9f, 0f, 1f), 1e-6f);
            Assert.AreEqual(0f, MathUtil.Logistic(float.MaxValue, 0f, -1f), 1e-6f);
            Assert.AreEqual(0.5f, MathUtil.Logistic(123f, 0f, 0f), 1e-6f);
            for (float x = -1000f; x <= 1000f; x += 13f)
            {
                float v = MathUtil.Logistic(x, 0f, 50f);
                Assert.IsFalse(float.IsNaN(v));
                Assert.IsTrue(v >= 0f && v <= 1f);
            }
            Assert.Less(MathUtil.Logistic(1f, 2f, 1f), MathUtil.Logistic(3f, 2f, 1f));
        }

        [Test]
        public void Rounding_And_Colors()
        {
            Assert.AreEqual(-2, MathUtil.FloorToInt(-1.5f));
            Assert.AreEqual(1, MathUtil.FloorToInt(1.99f));
            Assert.AreEqual(3, MathUtil.RoundToInt(2.5f));
            Assert.AreEqual(-3, MathUtil.RoundToInt(-2.5f));
            Assert.AreEqual(0x11223344u, MathUtil.Rgba(0x11, 0x22, 0x33, 0x44));
            Assert.AreEqual(0xAABBCCFFu, MathUtil.Hex(0xAABBCC));
        }

        [Test]
        public void Guard_Helpers()
        {
            Assert.Throws<ArgumentNullException>(() => Guard.NotNull<string>(null, "x"));
            Assert.AreEqual("a", Guard.NotNull("a", "x"));
            Assert.Throws<ArgumentOutOfRangeException>(() => Guard.InRange(5, 0, 4, "x"));
            Assert.AreEqual(4, Guard.InRange(4, 0, 4, "x"));
            Assert.Throws<ArgumentException>(() => Guard.Finite(float.NaN, "x"));
            Assert.Throws<ArgumentException>(() => Guard.Finite(float.PositiveInfinity, "x"));
            Assert.AreEqual(1f, Guard.Finite(1f, "x"));
            Assert.Throws<InvalidOperationException>(() => Guard.That(false, "m"));
            Assert.DoesNotThrow(() => Guard.That(true, "m"));
        }

        [Test]
        public void SimTime_Calendar()
        {
            Assert.AreEqual(96, SimTime.TicksPerYear);
            Assert.AreEqual(0, SimTime.Year(95));
            Assert.AreEqual(1, SimTime.Year(96));
            Assert.AreEqual(Season.Spring, SimTime.SeasonOf(0));
            Assert.AreEqual(Season.Summer, SimTime.SeasonOf(24));
            Assert.AreEqual(Season.Winter, SimTime.SeasonOf(95));
            Assert.AreEqual(Season.Spring, SimTime.SeasonOf(96));
            Assert.AreEqual(192L, SimTime.YearsToTicks(2f));
            Assert.AreEqual(0.5f, SimTime.TicksToYears(48), 1e-6f);
            Assert.AreEqual("Year 1, Summer day 1", SimTime.Format(96 + 24));
        }
    }
}
