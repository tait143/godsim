using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Settlements.Construction
{
    /// <summary>Test definitions (with costs) and small world helpers for the construction module tests.</summary>
    internal static class ConstructionTestData
    {
        public static ResourceAmount[] Cost(float wood, float stone)
        {
            var list = new List<ResourceAmount>();
            if (wood > 0f) list.Add(new ResourceAmount(ResourceType.Wood, wood));
            if (stone > 0f) list.Add(new ResourceAmount(ResourceType.Stone, stone));
            return list.ToArray();
        }

        public static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition { Type = BuildingType.TownCenter, Name = "Town Center", Category = BuildingCategory.Civic, Width = 2, Height = 2, MaxPerSettlement = 1, BuildWork = 40f, Cost = Cost(50f, 20f) });
            db.Register(new BuildingDefinition { Type = BuildingType.House, Name = "House", Category = BuildingCategory.Housing, HousingMin = 3, HousingMax = 5, BuildWork = 10f, Cost = Cost(10f, 0f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Storehouse, Name = "Storehouse", Category = BuildingCategory.Storage, Width = 2, Height = 1, BuildWork = 20f, Cost = Cost(30f, 10f) });
            db.Register(new BuildingDefinition { Type = BuildingType.Workshop, Name = "Workshop", Category = BuildingCategory.Industry, Job = JobType.Craftsman, JobSlots = 2, BuildWork = 10f, Cost = Cost(20f, 0f) });
            db.Register(new BuildingDefinition { Type = BuildingType.GathererHut, Name = "Gatherer Hut", Category = BuildingCategory.Food, MaxAge = AgeId.Bronze, Job = JobType.Gatherer, JobSlots = 2, BuildWork = 5f, Cost = Cost(5f, 0f) });
            db.Register(new BuildingDefinition { Type = BuildingType.GreatLibrary, Name = "Great Library", Category = BuildingCategory.Wonder, IsWonder = true, UniquePerCivilization = true, Width = 2, Height = 2, BuildWork = 50f, Cost = Cost(0f, 30f) });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        public static SimContext CreateContext(ulong seed = 7) => TestWorlds.CreateContext(CreateDb(), 64, seed);

        public static Settlement AddTown(SimContext ctx, int adults, out Civilization civ) =>
            TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), adults, out civ);

        public static Building Place(SimContext ctx, Settlement s, BuildingType type, bool complete = false)
        {
            var def = ctx.Db.Building(type);
            var site = ctx.Ops.FindFreeSite(new Vec2i(s.Center.X + 4, s.Center.Y + 4), def.Width, def.Height, 24);
            Assert.IsTrue(site.IsValid, "no free site for " + type);
            var b = ctx.Ops.CreateBuilding(s, type, site, 0, complete);
            Assert.IsNotNull(b);
            return b;
        }

        public static void ClearStock(Settlement s)
        {
            for (int i = 0; i < Counts.Resources; i++) s.Stockpile.SetRaw((ResourceType)i, 0f);
        }

        public static void Stock(Settlement s, float wood, float stone)
        {
            ClearStock(s);
            s.Stockpile.SetRaw(ResourceType.Wood, wood);
            s.Stockpile.SetRaw(ResourceType.Stone, stone);
        }

        public static Person AddWorker(SimContext ctx, Settlement s, JobType job, Building workplace = null, float skill = 1f)
        {
            var p = ctx.Ops.CreatePerson(s, s.Race, Sex.Male, 25f);
            p.Skill = skill;
            Assert.IsTrue(ctx.Ops.AssignJob(p, workplace, job));
            return p;
        }

        public static Building TownCenter(SimContext ctx, Settlement s) => ctx.State.Buildings.Get(s.TownCenterId);

        /// <summary>Runs already-initialized systems tick by tick (engine order, interval aware), without re-initializing.</summary>
        public static void Step(IList<ISimSystem> systems, SimContext ctx, int ticks)
        {
            for (int t = 0; t < ticks; t++)
            {
                ctx.State.Tick++;
                for (int i = 0; i < systems.Count; i++)
                {
                    var sys = systems[i];
                    if (sys.TickInterval > 1 && ctx.State.Tick % sys.TickInterval != 0) continue;
                    ctx.DeltaTicks = sys.TickInterval;
                    sys.Tick(ctx);
                    ctx.Ops.FlushRemovals();
                }
            }
        }

        /// <summary>Text fingerprint of everything this module touches (for determinism checks).</summary>
        public static string Fingerprint(SimContext ctx)
        {
            var sb = new StringBuilder();
            sb.Append(ctx.State.Tick).Append('|').Append(ctx.State.Rng.State).Append('\n');
            var bs = ctx.State.Buildings.Items;
            for (int i = 0; i < bs.Count; i++)
            {
                var b = bs[i];
                sb.Append(b.Id).Append(':').Append(b.Type).Append(':').Append(b.IsComplete).Append(':').Append(b.CostPaid)
                  .Append(':').Append(b.ConstructionProgress.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(':').Append(b.Condition.ToString("R", System.Globalization.CultureInfo.InvariantCulture))
                  .Append(':').Append(b.StyleAge).Append(':').Append(b.Level).Append('\n');
            }
            var ss = ctx.State.Settlements.Items;
            for (int i = 0; i < ss.Count; i++)
            {
                var s = ss[i];
                sb.Append(s.Id).Append(':').Append(s.Tier).Append(':').Append(s.TerritoryRadius).Append(':').Append(s.DesiredBuilders);
                for (int r = 0; r < Counts.Resources; r++) sb.Append(':').Append(s.Stockpile.Get((ResourceType)r).ToString("R", System.Globalization.CultureInfo.InvariantCulture));
                sb.Append('\n');
            }
            return sb.ToString();
        }
    }
}
