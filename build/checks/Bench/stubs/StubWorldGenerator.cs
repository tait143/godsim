// STUB for build/checks/Bench only (never copied to tools/). Mirrors the public surface the bench uses:
//   GodSim.Core.World.WorldGenSettings.FromConfig(SimConfig) and WorldGenerator.Generate(WorldGenSettings, Rng).
// The real implementation lives in Assets/GodSim/Core/World (written in parallel).
using System;

namespace GodSim.Core.World
{
    public sealed class WorldGenSettings
    {
        public int Width = 192;
        public int Height = 192;
        public WorldPreset Preset = WorldPreset.Continents;

        public static WorldGenSettings FromConfig(SimConfig c)
        {
            if (c == null) return new WorldGenSettings();
            return new WorldGenSettings { Width = c.MapWidth, Height = c.MapHeight, Preset = c.Preset };
        }
    }

    public static class WorldGenerator
    {
        /// <summary>Cheap value-noise island map (a few octaves) so the harness has realistic-ish work to time.</summary>
        public static WorldMap Generate(WorldGenSettings settings, Rng rng)
        {
            var s = settings ?? new WorldGenSettings();
            var map = new WorldMap(s.Width, s.Height);
            int g = 17;
            var lattice = new float[g * g];
            for (int i = 0; i < lattice.Length; i++) lattice[i] = rng.NextFloat() * 2f - 1f;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    float e = 0f, amp = 0.6f, freq = 1f;
                    for (int oct = 0; oct < 4; oct++)
                    {
                        float fx = x / (float)map.Width * (g - 1) * freq / 4f, fy = y / (float)map.Height * (g - 1) * freq / 4f;
                        int x0 = (int)fx % (g - 1), y0 = (int)fy % (g - 1);
                        float tx = fx - (int)fx, ty = fy - (int)fy;
                        float a = lattice[y0 * g + x0], b = lattice[y0 * g + x0 + 1], c = lattice[(y0 + 1) * g + x0], d = lattice[(y0 + 1) * g + x0 + 1];
                        e += amp * (a + (b - a) * tx + (c - a) * ty + (a - b - c + d) * tx * ty);
                        amp *= 0.5f; freq *= 2f;
                    }
                    float dx = x / (float)map.Width - 0.5f, dy = y / (float)map.Height - 0.5f;
                    e += 0.45f - 1.6f * (dx * dx + dy * dy);
                    int i = map.Index(x, y);
                    map.Elevation[i] = Math.Max(-1f, Math.Min(1f, e));
                    map.Moisture[i] = rng.NextFloat();
                    map.Temperature[i] = 1f - y / (float)map.Height;
                    map.Biomes[i] = e < 0f ? Biome.DeepOcean : e < 0.05f ? Biome.Beach : e < 0.4f ? (map.Moisture[i] > 0.5f ? Biome.Forest : Biome.Grassland) : e < 0.6f ? Biome.Hills : Biome.Mountain;
                    map.Fertility[i] = map.Biomes[i] == Biome.Grassland ? 0.8f : 0.3f;
                }
            return map;
        }
    }
}
