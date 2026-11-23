using System;
using GodSim.Core;

namespace GodSim.Tests.Geography
{
    /// <summary>
    /// Self-contained pseudo-realistic world for geography stress tests (value noise elevation/moisture, latitude
    /// temperature, downhill rivers). Independent of the World module so this check builds in isolation.
    /// </summary>
    public static class NoiseWorld
    {
        public static WorldMap Create(int size, ulong seed)
        {
            var map = new WorldMap(size, size);
            int n = map.TileCount;
            var elev = new float[n];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    int i = y * size + x;
                    float e = 0.55f * Noise(x / 48f, y / 48f, seed) + 0.3f * Noise(x / 20f, y / 20f, seed + 1) + 0.15f * Noise(x / 7f, y / 7f, seed + 2);
                    float m = 0.7f * Noise(x / 36f, y / 36f, seed + 3) + 0.3f * Noise(x / 11f, y / 11f, seed + 4);
                    float lat = Math.Abs((y + 0.5f) / size - 0.5f) * 2f;
                    float t = MathUtil.Clamp01(1f - lat * 0.95f + (Noise(x / 25f, y / 25f, seed + 5) - 0.5f) * 0.2f - Math.Max(0f, e - 0.6f));
                    elev[i] = e;
                    map.Elevation[i] = e * 2f - 1f;
                    map.Moisture[i] = m;
                    map.Temperature[i] = t;
                    map.Biomes[i] = Classify(e, t, m);
                }
            var rng = new Rng(seed ^ 0xABCDEFUL);
            int sources = Math.Max(4, n / 2500);
            for (int s = 0; s < sources * 4 && sources > 0; s++)
            {
                int src = rng.Range(0, n);
                if (map.Biomes[src] != Biome.Hills && map.Biomes[src] != Biome.Mountain) continue;
                Trace(map, elev, src);
                sources--;
            }
            for (int v = 0; v < Math.Max(1, n / 30000); v++)
            {
                int c = rng.Range(0, n);
                if (map.Biomes[c] != Biome.Mountain) continue;
                int cx = c % size, cy = c / size;
                for (int dy = -1; dy <= 1; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                        if (map.InBounds(cx + dx, cy + dy)) map.Biomes[map.Index(cx + dx, cy + dy)] = Biome.Volcanic;
            }
            return map;
        }

        private static Biome Classify(float e, float t, float m)
        {
            if (e < 0.42f) return Biome.DeepOcean;
            if (e < 0.5f) return Biome.ShallowWater;
            if (e < 0.52f && t > 0.3f) return Biome.Beach;
            if (e > 0.82f || (e > 0.74f && t < 0.15f)) return Biome.Snow;
            if (e > 0.74f) return Biome.Mountain;
            if (e > 0.67f) return Biome.Hills;
            if (t < 0.15f) return Biome.Tundra;
            if (t < 0.35f) return m > 0.45f ? Biome.Taiga : Biome.Tundra;
            if (m > 0.72f && e < 0.56f) return Biome.Swamp;
            if (t < 0.65f) return m < 0.3f ? Biome.Desert : (m < 0.55f ? Biome.Grassland : Biome.Forest);
            return m < 0.32f ? Biome.Desert : (m < 0.58f ? Biome.Savanna : Biome.Jungle);
        }

        private static void Trace(WorldMap map, float[] elev, int cur)
        {
            int w = map.Width, h = map.Height;
            for (int guard = 0; guard < 400; guard++)
            {
                if (map.Biomes[cur].IsWater()) return;
                map.Set(cur, TileFlags.River);
                int x = cur % w, y = cur / w, next = -1;
                float best = elev[cur];
                if (x > 0 && elev[cur - 1] < best) { best = elev[cur - 1]; next = cur - 1; }
                if (x < w - 1 && elev[cur + 1] < best) { best = elev[cur + 1]; next = cur + 1; }
                if (y > 0 && elev[cur - w] < best) { best = elev[cur - w]; next = cur - w; }
                if (y < h - 1 && elev[cur + w] < best) { best = elev[cur + w]; next = cur + w; }
                if (next < 0) return;
                cur = next;
            }
        }

        private static float Noise(float x, float y, ulong seed)
        {
            int x0 = (int)Math.Floor(x), y0 = (int)Math.Floor(y);
            float fx = x - x0, fy = y - y0;
            fx = fx * fx * (3f - 2f * fx);
            fy = fy * fy * (3f - 2f * fy);
            float a = Lattice(x0, y0, seed), b = Lattice(x0 + 1, y0, seed);
            float c = Lattice(x0, y0 + 1, seed), d = Lattice(x0 + 1, y0 + 1, seed);
            return (a + (b - a) * fx) * (1f - fy) + (c + (d - c) * fx) * fy;
        }

        private static float Lattice(int x, int y, ulong seed)
        {
            ulong hsh = Hashing.Combine(seed, ((ulong)(uint)x << 32) | (uint)y);
            return (hsh >> 40) * (1f / 16777216f);
        }
    }
}
