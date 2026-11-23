using System;
using System.Diagnostics;
using GodSim.Core;
using GodSim.Tests.Shared;
using GodSim.Unity;
using NUnit.Framework;

namespace GodSim.Tests.UnityOverlays
{
    public class OverlayPerfTests
    {
        [Test]
        public void FullComputeAt512IsFastAndAllocationFree()
        {
            var ctx = TestWorlds.CreateContext(TestDb.CreateStub(), 512);
            for (int k = 0; k < 40; k++)
            {
                Civilization civ;
                TestWorlds.AddSettlement(ctx, (RaceId)(k % 5), new Vec2i(20 + (k % 8) * 60, 20 + (k / 8) * 90), 10, out civ);
                if (k > 0) ctx.State.Diplomacy.Wars.Add(new War { Id = k, AttackerId = civ.Id, DefenderId = civ.Id - 1, IsActive = true });
            }
            var field = new OverlayField();
            foreach (OverlayMode mode in Enum.GetValues(typeof(OverlayMode))) OverlayData.Compute(ctx.State, mode, field); // warm-up
            var sw = new Stopwatch();
            var modes = (OverlayMode[])Enum.GetValues(typeof(OverlayMode));
            for (int m = 0; m < modes.Length; m++)
            {
                OverlayMode mode = modes[m];
                long before = GC.GetAllocatedBytesForCurrentThread();
                sw.Restart();
                OverlayData.Compute(ctx.State, mode, field);
                sw.Stop();
                long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.Progress.WriteLine(mode + ": " + sw.Elapsed.TotalMilliseconds.ToString("0.00") + " ms, " + allocated + " B");
                Assert.Less(sw.Elapsed.TotalMilliseconds, 250.0, mode.ToString());
                Assert.AreEqual(0L, allocated, mode + " must not allocate in steady state");
            }
        }
    }
}
