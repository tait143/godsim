using System.Collections.Generic;
using GodSim.Bench.Harness;
using GodSim.Core;
using GodSim.Core.World;

namespace GodSim.Bench.Benchmarks
{
    /// <summary>World generation at 128/256/512 (WorldGenerator.Generate(WorldGenSettings.FromConfig(cfg), new Rng(seed))).</summary>
    public static class WorldGenBenchmarks
    {
        public const string Category = "worldgen";

        public static IEnumerable<BenchCase> Cases()
        {
            foreach (int size in new[] { 128, 256, 512 })
            {
                int s = size;
                yield return new BenchCase
                {
                    Category = Category, Name = "generate" + s,
                    Description = "WorldGenerator.Generate " + s + "x" + s,
                    Run = o => new[] { Generate(o, s) },
                };
            }
        }

        private static BenchResult Generate(BenchOptions o, int size)
        {
            var cfg = new SimConfig { Seed = o.Seed, MapWidth = size, MapHeight = size }.Sanitize();
            var settings = WorldGenSettings.FromConfig(cfg);
            int iters = size >= 512 ? o.Q(2, 5) : o.Q(3, 8);
            WorldMap last = null;
            ulong firstHash = 0;
            bool haveFirst = false, deterministic = true;
            var r = Timing.Measure(Category, "generate" + size + "x" + size, "world", 1, iters, 1, () =>
            {
                last = WorldGenerator.Generate(settings, new Rng(o.Seed));
            }, () =>
            {
                if (last == null) return;
                ulong h = MapHash(last);
                if (!haveFirst) { firstHash = h; haveFirst = true; }
                else if (h != firstHash) deterministic = false;
            });
            if (last != null)
            {
                ulong h = MapHash(last);
                if (haveFirst && h != firstHash) deterministic = false;
                int land = 0;
                for (int i = 0; i < last.TileCount; i++) if (!last.IsWater(i)) land++;
                r.Metric("tiles", last.TileCount).Metric("land_ratio", land / (double)last.TileCount)
                 .Metric("ms_per_megatile", r.MedianMs / (last.TileCount / 1e6))
                 .Metric("deterministic", deterministic ? 1 : 0);
                if (!deterministic) r.Note = "NON-DETERMINISTIC: same seed produced different maps";
            }
            return r;
        }

        /// <summary>Cheap FNV-style hash over elevation bits + biomes + flags (determinism smoke check).</summary>
        public static ulong MapHash(WorldMap m)
        {
            ulong h = 14695981039346656037UL;
            unchecked
            {
                for (int i = 0; i < m.TileCount; i++)
                {
                    h = (h ^ (uint)System.BitConverter.SingleToInt32Bits(m.Elevation[i])) * 1099511628211UL;
                    h = (h ^ (byte)m.Biomes[i]) * 1099511628211UL;
                    h = (h ^ (ushort)m.Flags[i]) * 1099511628211UL;
                }
            }
            return h;
        }
    }
}
