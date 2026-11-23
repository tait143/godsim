using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Divine;
using GodSim.Tests.Shared;

namespace GodSim.Tests.Divine.Powers
{
    /// <summary>Shared setup for the divine power tests: real power catalog + stub content, tiny worlds, event capture.</summary>
    internal static class PowersTestKit
    {
        public static GameDatabase CreateDb(RaceDefinition extraRace = null)
        {
            var db = new GameDatabase();
            GodPowerCatalog.Register(db);
            if (extraRace != null) db.Register(extraRace);
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        public static SimContext CreateContext(GameDatabase db = null, Biome fill = Biome.Grassland, ulong seed = 7)
        {
            var ctx = TestWorlds.CreateContext(db ?? CreateDb(), 64, seed, fill);
            ctx.State.God.Mana = 5000f;
            ctx.State.God.MaxMana = 5000f;
            ctx.State.Tick = 100;
            return ctx;
        }

        public static GodCommand Cmd(GodPowerType power, int x, int y, int radius, int parameter = 0) =>
            new GodCommand { Power = power, Target = new Vec2i(x, y), Radius = radius, Parameter = parameter };

        /// <summary>Sets the elevation (and matching water/land biome) of a disc of tiles.</summary>
        public static void SetElevationDisc(SimContext ctx, Vec2i c, int r, float elevation, Biome biome)
        {
            var map = ctx.State.Map;
            for (int y = c.Y - r; y <= c.Y + r; y++)
                for (int x = c.X - r; x <= c.X + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int dx = x - c.X, dy = y - c.Y;
                    if (dx * dx + dy * dy > r * r) continue;
                    int i = map.Index(x, y);
                    map.Elevation[i] = elevation;
                    map.Biomes[i] = biome;
                }
        }

        public static List<Person> Residents(SimContext ctx, Settlement s)
        {
            var list = new List<Person>();
            foreach (int id in s.ResidentIds) { var p = ctx.State.People.Get(id); if (p != null && p.IsAlive) list.Add(p); }
            return list;
        }

        public static int Alive(SimContext ctx)
        {
            int n = 0;
            var items = ctx.State.People.Items;
            for (int i = 0; i < items.Count; i++) if (items[i].IsAlive) n++;
            return n;
        }
    }

    /// <summary>Records divine events published on a bus.</summary>
    internal sealed class DivineEventLog
    {
        public readonly List<GodPowerCastEvent> Casts = new List<GodPowerCastEvent>();
        public readonly List<GodCommandRejectedEvent> Rejections = new List<GodCommandRejectedEvent>();
        public readonly List<TerrainChangedEvent> Terrain = new List<TerrainChangedEvent>();
        public readonly List<DisasterEvent> Disasters = new List<DisasterEvent>();
        public readonly List<BuildingDestroyedEvent> Destroyed = new List<BuildingDestroyedEvent>();
        public readonly List<PersonDiedEvent> Deaths = new List<PersonDiedEvent>();

        public DivineEventLog(EventBus bus)
        {
            bus.Subscribe<GodPowerCastEvent>(e => Casts.Add(e));
            bus.Subscribe<GodCommandRejectedEvent>(e => Rejections.Add(e));
            bus.Subscribe<TerrainChangedEvent>(e => Terrain.Add(e));
            bus.Subscribe<DisasterEvent>(e => Disasters.Add(e));
            bus.Subscribe<BuildingDestroyedEvent>(e => Destroyed.Add(e));
            bus.Subscribe<PersonDiedEvent>(e => Deaths.Add(e));
        }
    }
}
