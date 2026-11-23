using System.Collections.Generic;
using GodSim.Core;
using GodSim.Tests.Shared;

namespace GodSim.Tests.Population
{
    /// <summary>Test helpers for the population society systems (tiny hand-made worlds and definitions).</summary>
    internal static class SocietyTestKit
    {
        /// <summary>Database with a 3-5 House and a handful of job buildings; everything else stubbed.</summary>
        public static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.House, Name = "House", Category = BuildingCategory.Housing, HousingMin = 3, HousingMax = 5 });
            db.Register(Job(BuildingType.GathererHut, JobType.Gatherer, 3));
            db.Register(Job(BuildingType.WoodcutterCamp, JobType.Woodcutter, 2));
            db.Register(Job(BuildingType.Farm, JobType.Farmer, 4));
            db.Register(Job(BuildingType.Barracks, JobType.Soldier, 4));
            db.Register(Job(BuildingType.StoneQuarry, JobType.Quarrier, 5));
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static BuildingDefinition Job(BuildingType t, JobType job, int slots) =>
            new BuildingDefinition { Type = t, Name = t.ToString(), Category = BuildingCategory.Resource, Job = job, JobSlots = slots };

        public static SimContext CreateContext(int size = 64, ulong seed = 1) => TestWorlds.CreateContext(CreateDb(), size, seed);

        /// <summary>A settlement of N adults with all traits cleared (deterministic scoring in tests).</summary>
        public static Settlement Settlement(SimContext ctx, Vec2i at, int adults, out Civilization civ)
        {
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, at, adults, out civ);
            ClearTraits(ctx, s);
            return s;
        }

        public static void ClearTraits(SimContext ctx, Settlement s)
        {
            foreach (int id in s.ResidentIds) { var p = ctx.State.People.Get(id); if (p != null) p.Traits = PersonTraits.None; }
        }

        public static Person AddPerson(SimContext ctx, Settlement s, Sex sex, float age, int mother = -1, int father = -1)
        {
            var p = ctx.Ops.CreatePerson(s, s.Race, sex, age, mother, father);
            p.Traits = PersonTraits.None;
            return p;
        }

        public static void Marry(Person a, Person b) { a.SpouseId = b.Id; b.SpouseId = a.Id; }

        public static Building AddHouse(SimContext ctx, Settlement s, Vec2i at, int capacity)
        {
            var b = ctx.Ops.CreateBuilding(s, BuildingType.House, at, 0, true);
            NUnit.Framework.Assert.IsNotNull(b, "house placement failed at " + at);
            b.HousingCapacity = capacity;
            return b;
        }

        public static Building AddBuilding(SimContext ctx, Settlement s, BuildingType type, Vec2i at, bool complete = true)
        {
            var b = ctx.Ops.CreateBuilding(s, type, at, 0, complete);
            NUnit.Framework.Assert.IsNotNull(b, "placement failed at " + at);
            return b;
        }

        /// <summary>Advances the clock by one interval and runs the system once, the way the engine would.</summary>
        public static void RunOnce(ISimSystem system, SimContext ctx)
        {
            ctx.State.Tick += system.TickInterval;
            ctx.DeltaTicks = system.TickInterval;
            system.Tick(ctx);
            ctx.Ops.FlushRemovals();
        }

        public static List<Person> Residents(SimContext ctx, Settlement s)
        {
            var list = new List<Person>();
            foreach (int id in s.ResidentIds) { var p = ctx.State.People.Get(id); if (p != null && p.IsAlive) list.Add(p); }
            return list;
        }

        public static int CountJob(SimContext ctx, Settlement s, JobType job)
        {
            int n = 0;
            foreach (var p in Residents(ctx, s)) if (p.Job == job) n++;
            return n;
        }

        public static IList<ISimSystem> AllSystems() => new List<ISimSystem>(GodSim.Core.Population.SocietyModule.CreateSystems());
    }
}
