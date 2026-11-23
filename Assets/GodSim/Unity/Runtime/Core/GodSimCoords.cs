using System;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// The single mapping between simulation tile space and Unity world space. Every renderer, the camera
    /// and input picking use this so that everything lines up.
    /// Tile (x, y) occupies world [x, x+1) x [y, y+1) on the XZ plane; its center is (x + 0.5, h, y + 0.5).
    /// Land height is max(0, elevation) * <see cref="HeightScale"/>; water tiles sit at height 0.
    /// </summary>
    public static class GodSimCoords
    {
        /// <summary>World units per tile edge.</summary>
        public const float TileSize = 1f;

        /// <summary>World units of height for elevation 1.0. Set from <see cref="GodSimSettings.HeightScale"/> by the runner.</summary>
        public static float HeightScale = 6f;

        /// <summary>World position of the center of a tile, on the terrain surface. Out-of-bounds tiles are clamped for height.</summary>
        public static Vector3 TileToWorld(WorldMap map, Vec2i t)
        {
            float h = map != null ? SurfaceHeight(map, t.X, t.Y) : 0f;
            return new Vector3((t.X + 0.5f) * TileSize, h, (t.Y + 0.5f) * TileSize);
        }

        /// <summary>
        /// World position for a continuous tile-space coordinate where integer values are tile centers
        /// (so TileToWorld(map, 3f, 4f) == TileToWorld(map, new Vec2i(3, 4))). Height is bilinearly
        /// interpolated between the four surrounding tile centers. Allocation-free.
        /// </summary>
        public static Vector3 TileToWorld(WorldMap map, float x, float y)
        {
            if (float.IsNaN(x) || float.IsInfinity(x)) x = 0f;
            if (float.IsNaN(y) || float.IsInfinity(y)) y = 0f;
            float h = 0f;
            if (map != null)
            {
                int x0 = Mathf.FloorToInt(x);
                int y0 = Mathf.FloorToInt(y);
                float fx = x - x0;
                float fy = y - y0;
                float h00 = SurfaceHeight(map, x0, y0);
                float h10 = SurfaceHeight(map, x0 + 1, y0);
                float h01 = SurfaceHeight(map, x0, y0 + 1);
                float h11 = SurfaceHeight(map, x0 + 1, y0 + 1);
                float a = h00 + (h10 - h00) * fx;
                float b = h01 + (h11 - h01) * fx;
                h = a + (b - a) * fy;
            }
            return new Vector3((x + 0.5f) * TileSize, h, (y + 0.5f) * TileSize);
        }

        /// <summary>
        /// Converts a world position to the tile it lies over (XZ plane). Returns false when the position is
        /// outside the map (or not finite); <paramref name="tile"/> is then clamped to the nearest valid tile
        /// (or <see cref="Vec2i.Invalid"/> when <paramref name="map"/> is null or the input is not finite).
        /// </summary>
        public static bool WorldToTile(WorldMap map, Vector3 world, out Vec2i tile)
        {
            if (map == null || float.IsNaN(world.x) || float.IsNaN(world.z) || float.IsInfinity(world.x) || float.IsInfinity(world.z))
            {
                tile = Vec2i.Invalid;
                return false;
            }
            double fx = Math.Floor(world.x / TileSize);
            double fz = Math.Floor(world.z / TileSize);
            // Clamp in double space first so huge coordinates cannot overflow the int cast.
            int tx = (int)Math.Max(-1.0, Math.Min(map.Width, fx));
            int ty = (int)Math.Max(-1.0, Math.Min(map.Height, fz));
            if (map.InBounds(tx, ty))
            {
                tile = new Vec2i(tx, ty);
                return true;
            }
            tile = map.ClampToBounds(new Vec2i(tx, ty));
            return false;
        }

        /// <summary>
        /// Height of the terrain surface at the given tile (clamped to the map). Water = 0;
        /// land = max(0, elevation) * <see cref="HeightScale"/>.
        /// </summary>
        public static float SurfaceHeight(WorldMap map, int x, int y)
        {
            if (map == null) return 0f;
            if (x < 0) x = 0; else if (x >= map.Width) x = map.Width - 1;
            if (y < 0) y = 0; else if (y >= map.Height) y = map.Height - 1;
            int i = map.Index(x, y);
            if (map.IsWater(i)) return 0f;
            float e = map.Elevation[i];
            if (!(e > 0f)) return 0f;                     // also rejects NaN
            if (float.IsPositiveInfinity(e)) e = 1f;
            return e * HeightScale;
        }
    }
}
