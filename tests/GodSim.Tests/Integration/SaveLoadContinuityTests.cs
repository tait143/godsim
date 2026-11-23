using System;
using GodSim.Core;
using GodSim.Core.Composition;
using NUnit.Framework;
using Util = GodSim.Tests.Integration.IntegrationTestUtil;

namespace GodSim.Tests.Integration
{
    /// <summary>
    /// (c) Save/load continuity: 10y -> Save -> Load -> 10y must equal a straight 20y run (hash), proving
    /// the save captures ALL simulation state (RNG, blackboards, queues...). Also integrity (HMAC) checks.
    /// </summary>
    [TestFixture]
    public class SaveLoadContinuityTests
    {
        private const ulong Seed = 1357924680UL;

        [Test]
        [Category("Slow")]
        public void SaveLoadMidRunMatchesStraightRun()
        {
            var first = GodSimulation.CreateNew(Util.SmallConfig(Seed));
            first.StepMany(Util.Ticks(10));
            ulong hashAtSave = first.ComputeHash();
            byte[] data = first.Save();
            Assert.IsNotNull(data, "Save returned null");
            Assert.That(data.Length, Is.GreaterThan(0), "Save returned no bytes");
            Assert.AreEqual(hashAtSave, first.ComputeHash(), "Save() mutated the state");

            var resumed = GodSimulation.Load(data, Util.SmallConfig(Seed));
            Assert.AreEqual(first.Tick, resumed.Tick, "tick lost in save/load");
            Assert.AreEqual(hashAtSave, resumed.ComputeHash(), "state hash differs right after load");
            resumed.StepMany(Util.Ticks(10));

            var straight = GodSimulation.CreateNew(Util.SmallConfig(Seed));
            straight.StepMany(Util.Ticks(20));

            first.StepMany(Util.Ticks(10));
            Assert.AreEqual(straight.ComputeHash(), first.ComputeHash(), "calling Save() changed the future of the saved world");
            Assert.AreEqual(straight.Tick, resumed.Tick);
            Assert.AreEqual(straight.ComputeHash(), resumed.ComputeHash(),
                "resumed world diverged from the straight run\nstraight: " + Util.Describe(straight.State) + "resumed: " + Util.Describe(resumed.State));
        }

        [Test]
        public void SignedSaveRoundTripsWithKey()
        {
            byte[] key = Key(0x11);
            var sim = Util.CreateAndRun(Util.SmallConfig(Seed, 64), 1);
            byte[] data = sim.Save(key);
            var loaded = GodSimulation.Load(data, Util.SmallConfig(Seed, 64), null, key);
            Assert.AreEqual(sim.ComputeHash(), loaded.ComputeHash());
        }

        [Test]
        public void TamperedSignedSaveIsRejected()
        {
            byte[] key = Key(0x22);
            var sim = Util.CreateAndRun(Util.SmallConfig(Seed, 64), 1);
            byte[] data = sim.Save(key);
            var tampered = (byte[])data.Clone();
            tampered[tampered.Length / 2] ^= 0x5A;
            Assert.Catch<Exception>(() => GodSimulation.Load(tampered, Util.SmallConfig(Seed, 64), null, key), "tampered save was accepted");
        }

        [Test]
        public void SignedSaveWithWrongKeyIsRejected()
        {
            var sim = Util.CreateAndRun(Util.SmallConfig(Seed, 64), 1);
            byte[] data = sim.Save(Key(0x33));
            Assert.Catch<Exception>(() => GodSimulation.Load(data, Util.SmallConfig(Seed, 64), null, Key(0x44)), "save accepted with the wrong HMAC key");
        }

        [Test]
        public void GarbageAndTruncatedSavesAreRejected()
        {
            var sim = Util.CreateAndRun(Util.SmallConfig(Seed, 64), 0);
            byte[] data = sim.Save();
            var truncated = new byte[data.Length / 2];
            Array.Copy(data, truncated, truncated.Length);
            var garbage = new byte[512];
            for (int i = 0; i < garbage.Length; i++) garbage[i] = (byte)(i * 31 + 7);

            Assert.Catch<Exception>(() => GodSimulation.Load(new byte[0], Util.SmallConfig(Seed, 64)), "empty save accepted");
            Assert.Catch<Exception>(() => GodSimulation.Load(truncated, Util.SmallConfig(Seed, 64)), "truncated save accepted");
            Assert.Catch<Exception>(() => GodSimulation.Load(garbage, Util.SmallConfig(Seed, 64)), "garbage save accepted");
        }

        private static byte[] Key(byte fill)
        {
            var k = new byte[32];
            for (int i = 0; i < k.Length; i++) k[i] = (byte)(fill + i);
            return k;
        }
    }
}
