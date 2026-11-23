using GodSim.Core.Security;
using NUnit.Framework;

namespace GodSim.Tests.Security
{
    public class RateLimiterTests
    {
        [Test]
        public void DefaultPolicy_DerivesFromMaxCommandsPerTick()
        {
            var rl = new RateLimiter(4);
            Assert.AreEqual(8, rl.BucketCapacity);
            Assert.AreEqual(4, rl.RefillPerTick);
            Assert.AreEqual(4, rl.GlobalPerTick);
            Assert.AreEqual(RateLimiter.MaxTrackedSources, rl.SourceLimit);
        }

        [Test]
        public void GlobalCap_LimitsAcquisitionsPerTick()
        {
            var rl = new RateLimiter(4);
            for (int i = 0; i < 4; i++) Assert.IsTrue(rl.TryAcquire("local", 10));
            Assert.IsFalse(rl.TryAcquire("local", 10, out string reason));
            Assert.IsTrue(reason.Contains("Too many"), reason);
            Assert.IsFalse(rl.TryAcquire("other", 10), "the global cap applies across sources");
            Assert.AreEqual(4, rl.AcceptedInTick(10));
            Assert.IsTrue(rl.TryAcquire("local", 11), "the cap resets on the next tick");
        }

        [Test]
        public void PerSourceBucket_LimitsOneSource_NotOthers()
        {
            var rl = new RateLimiter(2, 1, 100, 64);
            Assert.IsTrue(rl.TryAcquire("a", 1));
            Assert.IsTrue(rl.TryAcquire("a", 1));
            Assert.IsFalse(rl.TryAcquire("a", 1, out string reason));
            Assert.IsTrue(reason.Contains("too quickly"), reason);
            Assert.IsTrue(rl.TryAcquire("b", 1), "another source has its own bucket");
        }

        [Test]
        public void Bucket_RefillsByTick_UpToCapacity()
        {
            var rl = new RateLimiter(3, 1, 100, 64);
            for (int i = 0; i < 3; i++) Assert.IsTrue(rl.TryAcquire("a", 0));
            Assert.IsFalse(rl.TryAcquire("a", 0));
            Assert.AreEqual(1, rl.AvailableTokens("a", 1));
            Assert.IsTrue(rl.TryAcquire("a", 1));
            Assert.IsFalse(rl.TryAcquire("a", 1));
            Assert.AreEqual(3, rl.AvailableTokens("a", 1000), "refill saturates at capacity");
        }

        [Test]
        public void HugeTickJumps_DoNotOverflow()
        {
            var rl = new RateLimiter(3, 4096, 100, 64);
            for (int i = 0; i < 3; i++) rl.TryAcquire("a", long.MinValue + 1);
            Assert.AreEqual(3, rl.AvailableTokens("a", long.MaxValue));
            Assert.IsTrue(rl.TryAcquire("a", long.MaxValue));
        }

        [Test]
        public void TimeGoingBackwards_GrantsNoTokens()
        {
            var rl = new RateLimiter(2, 1, 100, 64);
            rl.TryAcquire("a", 100);
            rl.TryAcquire("a", 100);
            Assert.IsFalse(rl.TryAcquire("a", 50), "rewinding the clock must not refill");
            Assert.IsTrue(rl.TryAcquire("a", 51), "refill resumes from the re-anchored tick");
        }

        [Test]
        public void SourceMap_IsBounded_AndEvictsLeastRecentlyUsed()
        {
            var rl = new RateLimiter(2, 1, 4096, RateLimiter.MaxTrackedSources);
            for (int i = 0; i < 64; i++) Assert.IsTrue(rl.TryAcquire("s" + i, 1));
            Assert.AreEqual(64, rl.TrackedSourceCount);
            Assert.IsTrue(rl.TryAcquire("s0", 1)); // s0 becomes most recently used; s1 is now the LRU
            Assert.IsTrue(rl.TryAcquire("s64", 1));
            Assert.AreEqual(64, rl.TrackedSourceCount);
            Assert.IsTrue(rl.IsTracked("s0"));
            Assert.IsFalse(rl.IsTracked("s1"));
            Assert.IsTrue(rl.IsTracked("s64"));
            Assert.AreEqual(1, rl.TotalEvictions);
        }

        [Test]
        public void SourceFlood_NeverGrowsBeyondLimit()
        {
            var rl = new RateLimiter(2, 1, 4096, RateLimiter.MaxTrackedSources);
            for (int i = 0; i < 20000; i++)
            {
                rl.TryAcquire("flood-" + i, i / 1000);
                Assert.IsTrue((rl.TrackedSourceCount) <= (RateLimiter.MaxTrackedSources));
            }
            Assert.AreEqual(RateLimiter.MaxTrackedSources, rl.TrackedSourceCount);
        }

        [Test]
        public void Eviction_IsDeterministic()
        {
            var a = new RateLimiter(2, 1, 4096, 8);
            var b = new RateLimiter(2, 1, 4096, 8);
            for (int i = 0; i < 100; i++)
            {
                string key = "k" + ((i * 7919) % 23);
                Assert.AreEqual(a.TryAcquire(key, i / 5), b.TryAcquire(key, i / 5));
            }
            for (int i = 0; i < 23; i++) Assert.AreEqual(a.IsTracked("k" + i), b.IsTracked("k" + i));
        }

        [Test]
        public void GlobalRejection_DoesNotTrackNewSources()
        {
            var rl = new RateLimiter(1);
            Assert.IsTrue(rl.TryAcquire("a", 5));
            for (int i = 0; i < 100; i++) Assert.IsFalse(rl.TryAcquire("x" + i, 5));
            Assert.AreEqual(1, rl.TrackedSourceCount);
        }

        [Test]
        public void HugeAndNullKeys_AreSafe()
        {
            var rl = new RateLimiter(2, 1, 100, 64);
            string huge = new string('k', 1000000);
            Assert.IsTrue(rl.TryAcquire(huge, 1));
            Assert.IsTrue(rl.IsTracked(huge.Substring(0, RateLimiter.MaxKeyLength)), "keys are truncated");
            Assert.IsTrue(rl.TryAcquire(null, 1));
            Assert.AreEqual(2, rl.TrackedSourceCount);
        }

        [Test]
        public void Parameters_AreClamped()
        {
            var rl = new RateLimiter(int.MinValue, 0, -3, 100000);
            Assert.AreEqual(1, rl.BucketCapacity);
            Assert.AreEqual(1, rl.RefillPerTick);
            Assert.AreEqual(1, rl.GlobalPerTick);
            Assert.AreEqual(RateLimiter.MaxTrackedSources, rl.SourceLimit);
            var big = new RateLimiter(int.MaxValue);
            Assert.AreEqual(RateLimiter.HardMaxRate, big.BucketCapacity);
        }

        [Test]
        public void CanAcquire_AndAvailableTokens_ArePure()
        {
            var rl = new RateLimiter(2);
            for (int i = 0; i < 10; i++) { Assert.IsTrue(rl.CanAcquire("a", 1)); rl.AvailableTokens("a", 1); }
            Assert.AreEqual(0, rl.TrackedSourceCount);
            Assert.AreEqual(0, rl.AcceptedInTick(1));
        }

        [Test]
        public void Reset_ClearsEverything()
        {
            var rl = new RateLimiter(1);
            rl.TryAcquire("a", 1);
            rl.Reset();
            Assert.AreEqual(0, rl.TrackedSourceCount);
            Assert.AreEqual(0, rl.TotalAccepted);
            Assert.IsTrue(rl.TryAcquire("a", 1));
        }
    }
}
