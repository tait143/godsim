using GodSim.Core;
using GodSim.Core.Religion;
using GodSim.Tests.Shared;

namespace GodSim.Tests.Religion
{
    /// <summary>Small helpers shared by the Religion tests.</summary>
    internal static class ReligionTestKit
    {
        public static GameDatabase Db(params RaceDefinition[] races)
        {
            var db = new GameDatabase();
            if (races != null) foreach (var r in races) db.Register(r);
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        public static RaceDefinition Race(RaceId id, float piety, float aggression, float curiosity, float mercantilism)
        {
            var r = new RaceDefinition
            {
                Id = id, Name = id.ToString(), PluralName = id + "s", Adjective = id.ToString(),
                Piety = piety, Aggression = aggression, Curiosity = curiosity, Mercantilism = mercantilism,
            };
            for (int b = 0; b < Counts.Biomes; b++) r.BiomeHabitability[b] = ((Biome)b).IsWater() ? 0f : 0.6f;
            for (int a = 0; a < Counts.Races; a++) r.RaceAffinity[a] = a == (int)id ? 1f : 0f;
            r.Names.PersonPrefixes = new[] { "Kal", "Ves", "Oru" };
            r.Names.MaleSuffixes = new[] { "dor", "gash" };
            r.Names.FemaleSuffixes = new[] { "ira", "ela" };
            r.Names.SettlementPrefixes = new[] { "Iron", "Moss" };
            r.Names.SettlementSuffixes = new[] { "hold", "fen" };
            r.Names.CivilizationPatterns = new[] { "{0} Clans" };
            return r;
        }

        public static Building AddBuilding(SimContext ctx, Settlement s, BuildingType type, bool complete = true)
        {
            var site = ctx.Ops.FindFreeSite(new Vec2i(s.Center.X + 3, s.Center.Y), 1, 1, 10);
            return ctx.Ops.CreateBuilding(s, type, site, 0, complete);
        }

        /// <summary>Runs the system <paramref name="runs"/> times, 48 ticks apart, exactly as the engine would.</summary>
        public static void Runs(ReligionSystem sys, SimContext ctx, int runs, System.Action before = null)
        {
            for (int k = 0; k < runs; k++)
            {
                before?.Invoke();
                ctx.State.Tick += sys.TickInterval;
                ctx.DeltaTicks = sys.TickInterval;
                sys.Tick(ctx);
                ctx.Ops.FlushRemovals();
            }
        }

        /// <summary>Gives a civ a faith with explicit doctrines (bypassing founding).</summary>
        public static void SetFaith(Civilization civ, DoctrineId worship, DoctrineId ethos, DoctrineId practice, DoctrineId view)
        {
            civ.Memory[ReligionKeys.Founded] = 1f;
            civ.Memory[ReligionKeys.FoundedTick] = 0f;
            civ.Memory[ReligionKeys.Heresy] = 0f;
            civ.Memory[ReligionKeys.HolySite] = -1f;
            civ.Memory[DoctrineCatalog.MemoryKey(DoctrineAxis.Worship)] = (float)worship;
            civ.Memory[DoctrineCatalog.MemoryKey(DoctrineAxis.Ethos)] = (float)ethos;
            civ.Memory[DoctrineCatalog.MemoryKey(DoctrineAxis.Practice)] = (float)practice;
            civ.Memory[DoctrineCatalog.MemoryKey(DoctrineAxis.View)] = (float)view;
        }

        public static int CountChronicle(WorldState state, string fragment)
        {
            int n = 0;
            foreach (var e in state.Chronicle.Entries) if (e.Text != null && e.Text.Contains(fragment)) n++;
            return n;
        }

        public static ChronicleEntry? FindChronicle(WorldState state, string fragment)
        {
            foreach (var e in state.Chronicle.Entries) if (e.Text != null && e.Text.Contains(fragment)) return e;
            return null;
        }

        public static DoctrineId Held(Civilization civ, DoctrineAxis axis)
        {
            var d = ReligionQueries.Doctrine(civ, axis);
            return d != null ? d.Id : (DoctrineId)255;
        }
    }
}
