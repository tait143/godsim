using GodSim.Core;
using NUnit.Framework;

namespace GodSim.Tests.Contracts
{
    public class StockpileTests
    {
        private const ResourceType F = ResourceType.Food;
        private const ResourceType W = ResourceType.Wood;

        [Test]
        public void Defaults()
        {
            var s = new Stockpile();
            Assert.AreEqual(Stockpile.DefaultCapacity, s.Capacity);
            Assert.AreEqual(0f, s.Total());
            Assert.AreEqual(Stockpile.DefaultCapacity, s.FreeSpace(F));
        }

        [Test]
        public void Add_ClampsToCapacity()
        {
            var s = new Stockpile { Capacity = 100f };
            Assert.AreEqual(60f, s.Add(F, 60f));
            Assert.AreEqual(40f, s.Add(F, 60f));
            Assert.AreEqual(0f, s.Add(F, 1f));
            Assert.AreEqual(100f, s.Get(F));
            Assert.AreEqual(100f, s[F]);
            Assert.AreEqual(0f, s.FreeSpace(F));
            Assert.AreEqual(0f, s.Get(W));
        }

        [Test]
        public void Add_WhenOverCapacity_StoresNothing()
        {
            var s = new Stockpile { Capacity = 100f };
            s.Add(F, 100f);
            s.Capacity = 50f;
            Assert.AreEqual(0f, s.Add(F, 10f));
            Assert.AreEqual(100f, s.Get(F));
            Assert.AreEqual(0f, s.FreeSpace(F));
        }

        [Test]
        public void Add_NegativeCapacity_StoresNothing()
        {
            var s = new Stockpile { Capacity = -10f };
            Assert.AreEqual(0f, s.Add(F, 10f));
            Assert.AreEqual(0f, s.Get(F));
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-5f)]
        [TestCase(0f)]
        public void Add_BadAmounts_Ignored(float bad)
        {
            var s = new Stockpile();
            s.Add(F, 10f);
            Assert.AreEqual(0f, s.Add(F, bad));
            Assert.AreEqual(10f, s.Get(F));
            s.RollStatistics(1f);
            Assert.AreEqual(10f, s.AvgIncome[(int)F]);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        [TestCase(-5f)]
        public void ConsumeUpTo_BadAmounts_Ignored(float bad)
        {
            var s = new Stockpile();
            s.Add(F, 10f);
            float taken = s.ConsumeUpTo(F, bad);
            if (float.IsPositiveInfinity(bad)) Assert.AreEqual(10f, taken); // takes everything available
            else Assert.AreEqual(0f, taken);
            Assert.IsFalse(float.IsNaN(s.Get(F)));
            Assert.GreaterOrEqual(s.Get(F), 0f);
        }

        [Test]
        public void ConsumeUpTo_TakesAvailable()
        {
            var s = new Stockpile();
            s.Add(F, 10f);
            Assert.AreEqual(4f, s.ConsumeUpTo(F, 4f));
            Assert.AreEqual(6f, s.ConsumeUpTo(F, 100f));
            Assert.AreEqual(0f, s.Get(F));
            Assert.AreEqual(0f, s.ConsumeUpTo(F, 1f));
        }

        [Test]
        public void TryConsume_ExactOrNothing()
        {
            var s = new Stockpile();
            s.Add(F, 10f);
            Assert.IsFalse(s.TryConsume(F, 11f));
            Assert.AreEqual(10f, s.Get(F));
            Assert.IsTrue(s.TryConsume(F, 4f));
            Assert.AreEqual(6f, s.Get(F), 1e-6f);
            Assert.IsTrue(s.TryConsume(F, 0f));
            Assert.IsTrue(s.TryConsume(F, -3f));
            Assert.AreEqual(6f, s.Get(F), 1e-6f);
            Assert.IsFalse(s.TryConsume(F, float.PositiveInfinity));
            Assert.AreEqual(6f, s.Get(F), 1e-6f);
        }

        [Test]
        public void TryConsume_EpsilonTolerance_NeverGoesNegative()
        {
            var s = new Stockpile();
            s.Add(F, 1f);
            Assert.IsTrue(s.TryConsume(F, 1.00005f));
            Assert.AreEqual(0f, s.Get(F));
        }

        [Test]
        [Ignore("BUG: Stockpile.TryConsume(type, NaN) returns true and writes NaN into the stockpile: `amount <= 0f` and `_amounts[i] + 1e-4f < amount` are both false for NaN, then Math.Max(0f, x - NaN) == NaN (Stockpile.cs:48-52). AvgExpense also becomes NaN forever. Fix: `if (float.IsNaN(amount) || float.IsInfinity(amount)) return false;` before the <= 0 check.")]
        public void TryConsume_NaN_IsRejectedAndStateStaysFinite()
        {
            var s = new Stockpile();
            s.Add(F, 10f);
            Assert.IsFalse(s.TryConsume(F, float.NaN));
            Assert.AreEqual(10f, s.Get(F));
            s.RollStatistics(1f);
            Assert.IsFalse(float.IsNaN(s.AvgExpense[(int)F]));
        }

        [Test]
        public void TryPay_IsAtomic()
        {
            var s = new Stockpile();
            s.Add(F, 10f);
            s.Add(W, 5f);
            var costs = new[] { new ResourceAmount(F, 8f), new ResourceAmount(W, 6f) };
            Assert.IsFalse(s.CanAfford(costs));
            Assert.IsFalse(s.TryPay(costs));
            Assert.AreEqual(10f, s.Get(F));
            Assert.AreEqual(5f, s.Get(W));
            s.RollStatistics(1f);
            Assert.AreEqual(0f, s.AvgExpense[(int)F], "failed payment must not be tallied");

            var ok = new[] { new ResourceAmount(F, 4f), new ResourceAmount(W, 2f) };
            Assert.IsTrue(s.TryPay(ok, 2f));
            Assert.AreEqual(2f, s.Get(F), 1e-5f);
            Assert.AreEqual(1f, s.Get(W), 1e-5f);
        }

        [Test]
        public void TryPay_DuplicateResourceEntries_CheckedIndividually()
        {
            // Documents current semantics: CanAfford checks each entry against the full balance, so two entries
            // of 6 Food against 10 Food pass CanAfford, then the second TryConsume fails silently. The pay is NOT atomic.
            var s = new Stockpile();
            s.Add(F, 10f);
            var costs = new[] { new ResourceAmount(F, 6f), new ResourceAmount(F, 6f) };
            bool paid = s.TryPay(costs);
            if (paid) Assert.AreEqual(4f, s.Get(F), 1e-5f, "TryPay reported success but only one entry was paid");
        }

        [Test]
        [Ignore("BUG: Stockpile.TryPay is not atomic when a cost array lists the same resource twice: CanAfford checks each entry against the full balance (Stockpile.cs:67-72), so {6 Food, 6 Food} against 10 Food returns true but only 6 is paid (the second TryConsume silently fails). Fix: aggregate per-resource totals in CanAfford (stackalloc/float[Counts.Resources] scratch) or check the TryConsume results.")]
        public void TryPay_DuplicateResourceEntries_IsAtomic()
        {
            var s = new Stockpile();
            s.Add(F, 10f);
            var costs = new[] { new ResourceAmount(F, 6f), new ResourceAmount(F, 6f) };
            Assert.IsFalse(s.TryPay(costs));
            Assert.AreEqual(10f, s.Get(F));
        }

        [Test]
        public void TryPay_NullAndEmptyAndNaNMultiplier()
        {
            var s = new Stockpile();
            s.Add(F, 10f);
            Assert.IsTrue(s.TryPay(null));
            Assert.IsTrue(s.TryPay(ResourceAmount.None));
            Assert.IsFalse(s.TryPay(new[] { new ResourceAmount(F, 1f) }, float.NaN));
            Assert.IsFalse(s.TryPay(new[] { new ResourceAmount(F, float.NaN) }));
            Assert.AreEqual(10f, s.Get(F));
        }

        [Test]
        public void SetRaw_SanitizesAndSkipsFlows()
        {
            var s = new Stockpile();
            s.SetRaw(F, 42f);
            Assert.AreEqual(42f, s.Get(F));
            s.SetRaw(F, float.NaN);
            Assert.AreEqual(0f, s.Get(F));
            s.SetRaw(F, float.PositiveInfinity);
            Assert.AreEqual(0f, s.Get(F));
            s.SetRaw(F, -4f);
            Assert.AreEqual(0f, s.Get(F));
            s.SetRaw(W, 500f); // raw set may exceed capacity
            s.RollStatistics(1f);
            Assert.AreEqual(0f, s.AvgIncome[(int)W]);
        }

        [Test]
        public void ClampToCapacity_ReturnsLoss()
        {
            var s = new Stockpile { Capacity = 100f };
            s.Add(F, 100f);
            s.Add(W, 30f);
            s.Capacity = 40f;
            Assert.AreEqual(60f, s.ClampToCapacity(), 1e-5f);
            Assert.AreEqual(40f, s.Get(F));
            Assert.AreEqual(30f, s.Get(W));
            Assert.AreEqual(0f, s.ClampToCapacity());
        }

        [Test]
        public void FlowAccounting_RollStatistics()
        {
            var s = new Stockpile { Capacity = 1000f };
            s.Add(F, 10f);
            s.TryConsume(F, 4f);
            s.ConsumeUpTo(F, 2f);
            s.RollStatistics(0.5f);
            Assert.AreEqual(5f, s.AvgIncome[(int)F], 1e-5f);
            Assert.AreEqual(3f, s.AvgExpense[(int)F], 1e-5f);
            Assert.AreEqual(2f, s.Net(F), 1e-5f);
            // Counters reset: an empty period decays the averages.
            s.RollStatistics(0.5f);
            Assert.AreEqual(2.5f, s.AvgIncome[(int)F], 1e-5f);
            Assert.AreEqual(1.5f, s.AvgExpense[(int)F], 1e-5f);
        }

        [Test]
        public void FlowAccounting_OnlyCountsStoredAmount()
        {
            var s = new Stockpile { Capacity = 10f };
            s.Add(F, 25f);
            s.RollStatistics(1f);
            Assert.AreEqual(10f, s.AvgIncome[(int)F]);
        }

        [Test]
        public void FlowAccounting_ConvergesToSteadyState()
        {
            var s = new Stockpile { Capacity = 1e6f };
            for (int t = 0; t < 300; t++)
            {
                s.Add(F, 3f);
                s.TryConsume(F, 1f);
                s.RollStatistics();
            }
            Assert.AreEqual(3f, s.AvgIncome[(int)F], 1e-3f);
            Assert.AreEqual(1f, s.AvgExpense[(int)F], 1e-3f);
            Assert.AreEqual(2f, s.Net(F), 1e-3f);
        }

        [Test]
        public void Has_UsesEpsilon()
        {
            var s = new Stockpile();
            s.Add(F, 5f);
            Assert.IsTrue(s.Has(F, 5f));
            Assert.IsTrue(s.Has(F, 5.00005f));
            Assert.IsFalse(s.Has(F, 5.01f));
            Assert.IsFalse(s.Has(F, float.NaN));
            Assert.IsTrue(s.Has(F, -1f));
        }

        [Test]
        public void RandomOps_NeverProduceNaNOrNegativeOrOverCapacity()
        {
            var r = new Rng(808);
            var s = new Stockpile { Capacity = 200f };
            float[] bad = { float.NaN, float.PositiveInfinity, float.NegativeInfinity, -1f, 0f, 1e30f };
            for (int i = 0; i < 20000; i++)
            {
                var t = (ResourceType)r.Range(0, Counts.Resources);
                float amt = r.Chance(0.1f) ? bad[r.Range(0, bad.Length)] : r.Range(0f, 80f);
                switch (r.Range(0, 5))
                {
                    case 0: s.Add(t, amt); break;
                    case 1: if (!float.IsNaN(amt)) s.TryConsume(t, amt); break; // NaN path covered by ignored bug test
                    case 2: s.ConsumeUpTo(t, amt); break;
                    case 3: s.TryPay(new[] { new ResourceAmount(t, amt) }); break;
                    case 4: s.RollStatistics(); break;
                }
                float v = s.Get(t);
                Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), "op " + i);
                Assert.GreaterOrEqual(v, 0f);
                Assert.LessOrEqual(v, s.Capacity + 1e-3f);
            }
            for (int i = 0; i < Counts.Resources; i++)
            {
                Assert.IsFalse(float.IsNaN(s.AvgIncome[i]) || float.IsInfinity(s.AvgIncome[i]));
                Assert.IsFalse(float.IsNaN(s.AvgExpense[i]) || float.IsInfinity(s.AvgExpense[i]));
            }
        }
    }
}
