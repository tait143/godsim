using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Objectives;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Objectives
{
    /// <summary>Helpers shared by the objectives tests.</summary>
    internal static class ObjectiveTestUtil
    {
        public static SimContext NewContext(out ObjectiveSystem system, GameDatabase db = null, ulong seed = 1)
        {
            var ctx = TestWorlds.CreateContext(db ?? TestDb.CreateStub(), 64, seed);
            system = new ObjectiveSystem();
            ctx.DeltaTicks = system.TickInterval;
            system.Initialize(ctx);
            return ctx;
        }

        /// <summary>Advances one evaluation interval and runs the periodic check (n times).</summary>
        public static void Evaluate(ObjectiveSystem system, SimContext ctx, int times = 1)
        {
            for (int i = 0; i < times; i++)
            {
                ctx.State.Tick += system.TickInterval;
                ctx.DeltaTicks = system.TickInterval;
                system.Tick(ctx);
                ctx.Ops.FlushRemovals();
            }
        }

        public static void Cast(SimContext ctx, GodPowerType power, int times = 1, bool success = true, Vec2i target = default, int radius = 2, int parameter = 0)
        {
            for (int i = 0; i < times; i++)
                ctx.Events.Publish(new GodPowerCastEvent { Power = power, Target = target, Radius = radius, Parameter = parameter, Success = success, Message = "test" });
        }

        public static float Mem(WorldState s, string key) => s.Memory.TryGetValue(key, out float v) ? v : 0f;

        public static int[] Times(WorldState s)
        {
            var all = ObjectiveCatalog.All;
            var t = new int[all.Count];
            for (int i = 0; i < all.Count; i++) t[i] = ObjectiveQueries.TimesCompleted(s, all[i].Key);
            return t;
        }

        public static GodState CopyGod(GodState g) => new GodState { Mana = g.Mana, MaxMana = g.MaxMana, Alignment = g.Alignment };

        /// <summary>Asserts the god received exactly the rewards of the objectives completed between the two snapshots.</summary>
        public static List<string> AssertRewardsExact(WorldState s, int[] timesBefore, GodState godBefore)
        {
            var all = ObjectiveCatalog.All;
            var expected = CopyGod(godBefore);
            var newly = new List<string>();
            for (int i = 0; i < all.Count; i++)
            {
                int delta = ObjectiveQueries.TimesCompleted(s, all[i].Key) - timesBefore[i];
                Assert.IsTrue(delta >= 0, "completion count decreased for " + all[i].Key);
                for (int k = 0; k < delta; k++) { ObjectiveSystem.GrantReward(expected, all[i].Reward); newly.Add(all[i].Key); }
            }
            Assert.AreEqual(expected.Mana, s.God.Mana, 1e-3f, "mana");
            Assert.AreEqual(expected.MaxMana, s.God.MaxMana, 1e-3f, "max mana");
            Assert.AreEqual(expected.Alignment, s.God.Alignment, 1e-5f, "alignment");
            return newly;
        }

        public static int ChronicleCount(WorldState s, string contains)
        {
            int n = 0;
            foreach (var e in s.Chronicle.Entries) if (e.Text != null && e.Text.Contains(contains)) n++;
            return n;
        }
    }
}
