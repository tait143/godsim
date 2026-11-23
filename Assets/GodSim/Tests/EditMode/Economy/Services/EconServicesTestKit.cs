using GodSim.Core;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Economy
{
    /// <summary>Test database and world helpers for the economy service systems.</summary>
    internal static class EconServicesTestKit
    {
        public const float StorehouseCapacity = 100f;
        public const float GranaryCapacity = 50f;
        public const float StorehouseUpkeep = 0.1f;
        public const float TempleUpkeep = 0.2f;
        public const float MarketUpkeep = 0.5f;
        public const float TempleFaith = 4f;
        public const float WellWater = 3f;
        public const float MarketCommerce = 10f;
        public const float PalisadeDefense = 5f;

        /// <summary>Frozen database with economy-relevant test definitions plus stubs for everything else.</summary>
        public static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Storehouse, Name = "Storehouse", Category = BuildingCategory.Storage,
                StorageCapacity = StorehouseCapacity, UpkeepGold = StorehouseUpkeep,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Granary, Name = "Granary", Category = BuildingCategory.Storage,
                StorageCapacity = GranaryCapacity,
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Temple, Name = "Temple", Category = BuildingCategory.Religion,
                Job = JobType.Priest, JobSlots = 2, UpkeepGold = TempleUpkeep,
                Services = new[] { new ServiceAmount(ServiceType.Faith, TempleFaith) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Well, Name = "Well", Category = BuildingCategory.Infrastructure,
                Services = new[] { new ServiceAmount(ServiceType.Water, WellWater) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Market, Name = "Market", Category = BuildingCategory.Commerce,
                Job = JobType.Merchant, JobSlots = 2, UpkeepGold = MarketUpkeep,
                Services = new[] { new ServiceAmount(ServiceType.Commerce, MarketCommerce) },
            });
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Palisade, Name = "Palisade", Category = BuildingCategory.Military,
                Services = new[] { new ServiceAmount(ServiceType.Defense, PalisadeDefense) },
            });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        public static SimContext CreateContext(int size = 64, ulong seed = 7) => TestWorlds.CreateContext(CreateDb(), size, seed);

        /// <summary>Places a building near the settlement center (complete by default).</summary>
        public static Building Place(SimContext ctx, Settlement s, BuildingType type, bool complete = true)
        {
            var def = ctx.Db.Building(type);
            var site = ctx.Ops.FindFreeSite(new Vec2i(s.Center.X + 3, s.Center.Y + 1), def.Width, def.Height, 16);
            Assert.IsTrue(site.IsValid, "no free site for " + type);
            var b = ctx.Ops.CreateBuilding(s, type, site, 0, complete);
            Assert.IsNotNull(b);
            return b;
        }

        /// <summary>Assigns the building's job to the first <paramref name="count"/> unemployed adults.</summary>
        public static void Staff(SimContext ctx, Settlement s, Building b, int count)
        {
            var def = ctx.Db.Building(b.Type);
            int assigned = 0;
            for (int i = 0; i < s.ResidentIds.Count && assigned < count; i++)
            {
                var p = ctx.State.People.Get(s.ResidentIds[i]);
                if (p == null || !p.IsAlive || !p.IsAdult || p.Job != JobType.None) continue;
                if (ctx.Ops.AssignJob(p, b, def.Job)) assigned++;
            }
            Assert.AreEqual(count, assigned, "could not staff " + b.Type);
        }

        /// <summary>Sets every resource of a stockpile to 0 without flow accounting.</summary>
        public static void Empty(Stockpile sp)
        {
            for (int r = 0; r < Counts.Resources; r++) sp.SetRaw((ResourceType)r, 0f);
        }

        /// <summary>Runs one Tick with the system's interval as DeltaTicks (or an override), flushing removals.</summary>
        public static void TickOnce(ISimSystem system, SimContext ctx, int deltaTicks = 0)
        {
            ctx.State.Tick++;
            ctx.DeltaTicks = deltaTicks > 0 ? deltaTicks : system.TickInterval;
            system.Tick(ctx);
            ctx.Ops.FlushRemovals();
        }

        public static bool IsFinite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
    }
}
