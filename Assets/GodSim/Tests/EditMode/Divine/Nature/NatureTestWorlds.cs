using System.Collections.Generic;
using GodSim.Core;
using GodSim.Tests.Shared;

namespace GodSim.Tests.Divine.Nature
{
    /// <summary>Hand-built worlds for the nature module tests.</summary>
    internal static class NatureTestWorlds
    {
        /// <summary>Flat grassland context on a stub database.</summary>
        public static SimContext Flat(ulong seed = 1, int size = 64) => TestWorlds.CreateContext(TestDb.CreateStub(), size, seed);

        /// <summary>Turns a rectangle into forest with Trees and wood.</summary>
        public static void Forest(WorldMap map, int x0, int y0, int x1, int y1, float wood = 100f)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    int i = map.Index(x, y);
                    map.Biomes[i] = Biome.Forest;
                    map.Flags[i] |= TileFlags.Trees;
                    map.Resource[i] = wood;
                }
        }

        /// <summary>Sets a rectangle to a biome (with an elevation).</summary>
        public static void Fill(WorldMap map, int x0, int y0, int x1, int y1, Biome biome, float elevation)
        {
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    map.Biomes[i] = biome;
                    map.Elevation[i] = elevation;
                }
        }

        /// <summary>
        /// A 64x64 world with ocean + coast (west), a river (x = 20), a forest, mountains and a small
        /// volcanic field, plus one coastal Human settlement at about (11,33) with houses and farmland.
        /// </summary>
        public static SimContext Rich(ulong seed, out Settlement town, out Civilization civ)
        {
            var ctx = Flat(seed);
            var map = ctx.State.Map;
            Fill(map, 0, 0, 4, 63, Biome.DeepOcean, -0.4f);
            Fill(map, 5, 0, 5, 63, Biome.ShallowWater, -0.1f);
            Fill(map, 6, 0, 6, 63, Biome.Beach, 0.05f);
            for (int y = 0; y < 64; y++)
            {
                map.Flags[map.Index(6, y)] |= TileFlags.Coastal;
                int r = map.Index(20, y);
                map.Flags[r] |= TileFlags.River;
                map.Elevation[r] = 0.15f;
            }
            for (int y = 26; y <= 40; y++)
                for (int x = 17; x <= 23; x++)
                    if (x != 20) map.Flags[map.Index(x, y)] |= TileFlags.Farmland;
            Forest(map, 13, 37, 18, 46);
            Fill(map, 27, 26, 31, 38, Biome.Mountain, 0.7f);
            Fill(map, 26, 20, 28, 22, Biome.Volcanic, 0.8f);

            town = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(10, 32), 30, out civ);
            AddHouses(ctx, town, new[] { new Vec2i(9, 28), new Vec2i(12, 28), new Vec2i(14, 30), new Vec2i(15, 33), new Vec2i(8, 36) });
            return ctx;
        }

        public static List<Building> AddHouses(SimContext ctx, Settlement s, Vec2i[] sites)
        {
            var list = new List<Building>();
            foreach (var p in sites)
            {
                var b = ctx.Ops.CreateBuilding(s, BuildingType.House, p, 0, true);
                if (b != null) list.Add(b);
            }
            return list;
        }

        public static int CountFlag(WorldMap map, TileFlags flag)
        {
            int n = 0;
            for (int i = 0; i < map.TileCount; i++) if ((map.Flags[i] & flag) != 0) n++;
            return n;
        }

        /// <summary>Stable hash of everything the nature systems may change.</summary>
        public static ulong Fingerprint(SimContext ctx)
        {
            var s = ctx.State;
            var map = s.Map;
            ulong h = 1469598103934665603UL;
            for (int i = 0; i < map.TileCount; i++)
            {
                h = Hashing.Combine(h, (ulong)map.Flags[i]);
                h = Hashing.Combine(h, (ulong)System.BitConverter.SingleToInt32Bits(map.Resource[i]));
                h = Hashing.Combine(h, (ulong)System.BitConverter.SingleToInt32Bits(map.Elevation[i]));
            }
            foreach (var b in s.Buildings.Items) h = Hashing.Combine(h, (ulong)System.BitConverter.SingleToInt32Bits(b.Condition) ^ ((ulong)b.Id << 32));
            foreach (var p in s.People.Items) h = Hashing.Combine(h, (ulong)System.BitConverter.SingleToInt32Bits(p.Health) ^ ((ulong)p.Id << 32));
            foreach (var e in s.Chronicle.Entries) h = Hashing.Combine(h, Hashing.Fnv1a64(e.Text) ^ (ulong)e.Tick);
            var w = s.Weather;
            h = Hashing.Combine(h, (ulong)System.BitConverter.SingleToInt32Bits(w.Rainfall));
            h = Hashing.Combine(h, (ulong)System.BitConverter.SingleToInt32Bits(w.TemperatureOffset));
            h = Hashing.Combine(h, (ulong)System.BitConverter.SingleToInt32Bits(w.FoodYieldMultiplier));
            h = Hashing.Combine(h, (ulong)System.BitConverter.SingleToInt32Bits(w.StormIntensity));
            h = Hashing.Combine(h, s.Rng.State);
            return h;
        }
    }
}
