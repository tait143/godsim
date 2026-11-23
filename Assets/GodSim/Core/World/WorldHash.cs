using System;

namespace GodSim.Core.World
{
    /// <summary>Stable 64-bit hash of a map's contents (for determinism tests and desync checks).</summary>
    public static class WorldHash
    {
        private const ulong Prime = 1099511628211UL;

        /// <summary>Hashes dimensions, sea level and all terrain layers (elevation, climate, fertility, biomes, flags, resources).</summary>
        public static ulong Compute(WorldMap map) => Compute(map, false);

        /// <summary>As <see cref="Compute(WorldMap)"/>; optionally also hashes Owner and BuildingAt.</summary>
        public static ulong Compute(WorldMap map, bool includeOccupancy)
        {
            Guard.NotNull(map, nameof(map));
            ulong h = 14695981039346656037UL;
            h = Step(h, (uint)map.Width);
            h = Step(h, (uint)map.Height);
            h = Step(h, Bits(map.SeaLevel));
            int n = map.TileCount;
            for (int i = 0; i < n; i++)
            {
                h = Step(h, Bits(map.Elevation[i]));
                h = Step(h, Bits(map.Moisture[i]));
                h = Step(h, Bits(map.Temperature[i]));
                h = Step(h, Bits(map.Fertility[i]));
                h = Step(h, Bits(map.Resource[i]));
                h = Step(h, ((uint)map.Biomes[i] << 16) | (uint)map.Flags[i]);
                if (includeOccupancy)
                {
                    h = Step(h, (uint)map.Owner[i]);
                    h = Step(h, (uint)map.BuildingAt[i]);
                }
            }
            return Hashing.Mix64(h);
        }

        private static uint Bits(float v) => unchecked((uint)BitConverter.SingleToInt32Bits(v));

        private static ulong Step(ulong h, uint v)
        {
            unchecked
            {
                h = (h ^ (v & 0xFFFFu)) * Prime;
                h = (h ^ (v >> 16)) * Prime;
                return h;
            }
        }
    }
}
