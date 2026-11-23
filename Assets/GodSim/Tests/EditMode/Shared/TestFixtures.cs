using System.Collections.Generic;
using GodSim.Core;

namespace GodSim.Tests.Shared
{
    /// <summary>
    /// Shared test helpers (owned by lead; do not edit from module work). Lets every module test in
    /// isolation: build a GameDatabase with stub definitions for everything you do not own, plus a tiny
    /// hand-made world and a SimContext.
    /// </summary>
    public static class TestDb
    {
        /// <summary>Database where every slot is filled with a simple stub definition.</summary>
        public static GameDatabase CreateStub(bool freeze = true)
        {
            var db = new GameDatabase();
            FillMissing(db);
            if (freeze) db.Freeze();
            return db;
        }

        /// <summary>Registers stub definitions for every race/age/building/power slot still empty (and one tech if none).</summary>
        public static void FillMissing(GameDatabase db)
        {
            for (int i = 0; i < Counts.Races; i++)
            {
                if (db.Races[i] != null) continue;
                var r = new RaceDefinition { Id = (RaceId)i, Name = ((RaceId)i).ToString(), PluralName = ((RaceId)i) + "s", Adjective = ((RaceId)i).ToString() };
                for (int b = 0; b < Counts.Biomes; b++) r.BiomeHabitability[b] = ((Biome)b).IsWater() ? 0f : 0.6f;
                for (int a = 0; a < Counts.Races; a++) r.RaceAffinity[a] = a == i ? 1f : 0f;
                r.Names.PersonPrefixes = new[] { "Al", "Bo", "Ca" };
                r.Names.MaleSuffixes = new[] { "dor", "rik" };
                r.Names.FemaleSuffixes = new[] { "ra", "lia" };
                r.Names.SettlementPrefixes = new[] { "Oak", "Stone" };
                r.Names.SettlementSuffixes = new[] { "ford", "hold" };
                r.Names.CivilizationPatterns = new[] { "{0} Realm" };
                db.Register(r);
            }
            for (int i = 0; i < Counts.Ages; i++)
                if (db.Ages[i] == null)
                    db.Register(new AgeDefinition { Id = (AgeId)i, Name = ((AgeId)i).ToString(), ResearchThreshold = i * 1000f, MinPopulation = 0 });
            for (int i = 0; i < Counts.Buildings; i++)
            {
                if (db.Buildings[i] != null) continue;
                var t = (BuildingType)i;
                var d = new BuildingDefinition { Type = t, Name = t.ToString(), Category = BuildingCategory.Civic, BuildWork = 10f };
                if (t == BuildingType.House) { d.Category = BuildingCategory.Housing; d.HousingMin = 3; d.HousingMax = 5; }
                if (t == BuildingType.TownCenter) { d.MaxPerSettlement = 1; d.Width = 2; d.Height = 2; }
                db.Register(d);
            }
            for (int i = 0; i < Counts.GodPowers; i++)
                if (db.GodPowers[i] == null)
                    db.Register(new GodPowerDefinition { Type = (GodPowerType)i, Name = ((GodPowerType)i).ToString(), ManaCost = 10f, CooldownTicks = 5, MinRadius = 0, MaxRadius = 8 });
            if (db.Techs.Count == 0)
                db.Register(new TechDefinition { Key = "stub.tech", Name = "Stub Tech", Age = AgeId.Dawn, Cost = 50f });
        }
    }

    public static class TestWorlds
    {
        /// <summary>A flat, uniform world (default grassland) with a strict-mode SimContext.</summary>
        public static SimContext CreateContext(GameDatabase db, int size = 64, ulong seed = 1, Biome fill = Biome.Grassland)
        {
            var map = new WorldMap(size, size);
            for (int i = 0; i < map.TileCount; i++)
            {
                map.Biomes[i] = fill;
                map.Elevation[i] = fill.IsWater() ? -0.3f : 0.2f;
                map.Fertility[i] = 0.6f;
                map.Moisture[i] = 0.5f;
                map.Temperature[i] = 0.5f;
            }
            var state = new WorldState(seed, map);
            var events = new EventBus { StrictMode = true };
            var cfg = new SimConfig { StrictMode = true, Seed = seed, MapWidth = size, MapHeight = size }.Sanitize();
            return new SimContext
            {
                State = state,
                Db = db,
                Events = events,
                Config = cfg,
                Log = NullLogger.Instance,
                Ops = new WorldOps(state, db, events),
                Commands = new CommandQueue(cfg.CommandQueueCapacity, null),
            };
        }

        /// <summary>Creates a civilization with one founded settlement (town center placed) and N adults.</summary>
        public static Settlement AddSettlement(SimContext ctx, RaceId race, Vec2i at, int adults, out Civilization civ)
        {
            civ = ctx.Ops.CreateCivilization(race);
            var s = ctx.Ops.FoundSettlement(civ, at);
            for (int i = 0; i < adults; i++)
                ctx.Ops.CreatePerson(s, race, i % 2 == 0 ? Sex.Female : Sex.Male, 20f + (i % 10));
            return s;
        }

        /// <summary>Runs a system for the given number of ticks the way the engine would (interval-aware), flushing removals.</summary>
        public static void Run(ISimSystem system, SimContext ctx, int ticks, bool initialize = true)
        {
            if (initialize) { ctx.DeltaTicks = system.TickInterval; system.Initialize(ctx); ctx.Ops.FlushRemovals(); }
            for (int t = 0; t < ticks; t++)
            {
                ctx.State.Tick++;
                if (system.TickInterval > 1 && ctx.State.Tick % system.TickInterval != 0) continue;
                ctx.DeltaTicks = system.TickInterval;
                system.Tick(ctx);
                ctx.Ops.FlushRemovals();
            }
        }

        /// <summary>Runs several systems in order, tick by tick.</summary>
        public static void RunAll(IList<ISimSystem> systems, SimContext ctx, int ticks)
        {
            foreach (var s in systems) { ctx.DeltaTicks = s.TickInterval; s.Initialize(ctx); }
            ctx.Ops.FlushRemovals();
            for (int t = 0; t < ticks; t++)
            {
                ctx.State.Tick++;
                foreach (var s in systems)
                {
                    if (s.TickInterval > 1 && ctx.State.Tick % s.TickInterval != 0) continue;
                    ctx.DeltaTicks = s.TickInterval;
                    s.Tick(ctx);
                    ctx.Ops.FlushRemovals();
                }
            }
        }
    }
}
