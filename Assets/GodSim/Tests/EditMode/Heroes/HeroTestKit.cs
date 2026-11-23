using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Heroes;
using GodSim.Tests.Shared;

namespace GodSim.Tests.Heroes
{
    /// <summary>Small helpers for hero tests: worlds with fully controlled people.</summary>
    internal static class HeroTestKit
    {
        public static SimContext NewContext(ulong seed = 7)
        {
            return TestWorlds.CreateContext(TestDb.CreateStub(), 64, seed);
        }

        /// <summary>Settlement whose residents are plain adult farmers (skill 1, no traits).</summary>
        public static Settlement PlainSettlement(SimContext ctx, RaceId race, Vec2i at, int adults, out Civilization civ)
        {
            var s = TestWorlds.AddSettlement(ctx, race, at, adults, out civ);
            Normalize(ctx, s);
            return s;
        }

        public static void Normalize(SimContext ctx, Settlement s)
        {
            foreach (int id in s.ResidentIds)
            {
                var p = ctx.State.People.Get(id);
                p.Traits = PersonTraits.None;
                p.Skill = 1f;
                p.Experience = 0f;
                p.Job = JobType.Farmer;
            }
        }

        public static Person Resident(SimContext ctx, Settlement s, int index) => ctx.State.People.Get(s.ResidentIds[index]);

        public static Person MakeEligible(SimContext ctx, Settlement s, int index, JobType job)
        {
            var p = Resident(ctx, s, index);
            p.Job = job;
            p.Skill = 1.4f;
            return p;
        }

        public static int CountChronicle(SimContext ctx, ChronicleCategory cat)
        {
            int n = 0;
            foreach (var e in ctx.State.Chronicle.Entries) if (e.Category == cat) n++;
            return n;
        }

        public static List<ChronicleEntry> Entries(SimContext ctx, ChronicleCategory cat)
        {
            var list = new List<ChronicleEntry>();
            foreach (var e in ctx.State.Chronicle.Entries) if (e.Category == cat) list.Add(e);
            return list;
        }

        public static int CountHeroKeys(WorldState s)
        {
            int n = 0;
            foreach (var kv in s.Memory) if (kv.Key.StartsWith(HeroKeys.Prefix, System.StringComparison.Ordinal)) n++;
            return n;
        }

        public static int LivingCount(WorldState s)
        {
            int n = 0;
            foreach (var h in HeroQueries.Living(s)) n++;
            return n;
        }

        /// <summary>Runs exactly one hero pass (24 ticks of the engine-style loop).</summary>
        public static void OneRun(HeroSystem sys, SimContext ctx) => TestWorlds.Run(sys, ctx, HeroSystem.Interval, false);
    }
}
