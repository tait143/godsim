using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity.Units
{
    /// <summary>Allocation-free hashing / easing helpers for deterministic (per-id) visual animation.</summary>
    public static class UnitMath
    {
        public const float TwoPi = 6.28318530718f;

        public static uint Hash(int a)
        {
            unchecked
            {
                uint h = (uint)a * 0x9E3779B1u + 0x7F4A7C15u;
                h ^= h >> 16; h *= 0x85EBCA6Bu;
                h ^= h >> 13; h *= 0xC2B2AE35u;
                h ^= h >> 16;
                return h;
            }
        }

        public static uint Hash(int a, int b) { unchecked { return Hash(a ^ (int)Hash(b + 0x51ED27)); } }

        /// <summary>Hash to [0, 1).</summary>
        public static float Unit(uint h) => (h & 0xFFFFFFu) / 16777216f;

        /// <summary>Hash to [-1, 1).</summary>
        public static float Signed(uint h) => Unit(h) * 2f - 1f;

        public static float Frac(float x) => x - Mathf.Floor(x);

        public static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        /// <summary>Yaw in degrees for a world-space XZ direction (0 = +Z).</summary>
        public static float YawOf(float wx, float wz) => Mathf.Atan2(wx, wz) * Mathf.Rad2Deg;

        /// <summary>Small xorshift for visual-only randomness (never touches simulation state).</summary>
        public static float NextUnit(ref uint state)
        {
            if (state == 0) state = 0x6D2B79F5u;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return (state & 0xFFFFFFu) / 16777216f;
        }
    }

    /// <summary>
    /// Cached affine relation between tile space and world space, derived from <see cref="GodSimCoords"/>
    /// (so it stays correct whatever tile size / axis convention the coords use). Gives the inverse mapping
    /// (world to fractional tile) and tile-space directions for formations and facing.
    /// </summary>
    public struct MapFrame
    {
        public WorldMap Map;
        public Vector3 Origin;
        public Vector3 AxisX;
        public Vector3 AxisY;
        /// <summary>World units per tile edge.</summary>
        public float TileSize;
        private float _i00, _i01, _i10, _i11;

        public bool IsValid => Map != null;

        public static MapFrame From(WorldMap map)
        {
            var f = new MapFrame { Map = map };
            if (map == null) { f.TileSize = 1f; return f; }
            Vector3 o = GodSimCoords.TileToWorld(map, 0f, 0f);
            Vector3 ax = GodSimCoords.TileToWorld(map, 1f, 0f) - o;
            Vector3 ay = GodSimCoords.TileToWorld(map, 0f, 1f) - o;
            ax.y = 0f;
            ay.y = 0f;
            float det = ax.x * ay.z - ay.x * ax.z;
            if (!(Mathf.Abs(det) > 1e-6f))
            {
                ax = new Vector3(1f, 0f, 0f);
                ay = new Vector3(0f, 0f, 1f);
                det = 1f;
            }
            f.Origin = o;
            f.AxisX = ax;
            f.AxisY = ay;
            f.TileSize = Mathf.Sqrt(Mathf.Abs(det));
            float inv = 1f / det;
            f._i00 = ay.z * inv;
            f._i01 = -ay.x * inv;
            f._i10 = -ax.z * inv;
            f._i11 = ax.x * inv;
            return f;
        }

        /// <summary>Fractional tile coordinate (integer = tile center) under a world position.</summary>
        public Vector2 WorldToTile(Vector3 w)
        {
            float dx = w.x - Origin.x, dz = w.z - Origin.z;
            return new Vector2(_i00 * dx + _i01 * dz, _i10 * dx + _i11 * dz);
        }

        /// <summary>World XZ direction (y = 0) of a tile-space delta.</summary>
        public Vector3 TileDirToWorld(float dx, float dy) =>
            new Vector3(AxisX.x * dx + AxisY.x * dy, 0f, AxisX.z * dx + AxisY.z * dy);

        /// <summary>Tile-space delta of a world XZ direction.</summary>
        public Vector2 WorldDirToTile(float wx, float wz) => new Vector2(_i00 * wx + _i01 * wz, _i10 * wx + _i11 * wz);

        /// <summary>
        /// Ground point the camera looks at (forward ray against the plane y = <paramref name="groundY"/>),
        /// or the point under the camera when it looks at the horizon.
        /// </summary>
        public static Vector3 CameraFocus(Camera cam, float groundY)
        {
            Transform t = cam.transform;
            Vector3 p = t.position;
            Vector3 f = t.forward;
            if (f.y < -0.05f)
            {
                float d = (groundY - p.y) / f.y;
                if (d > 0f && d < 5000f) return new Vector3(p.x + f.x * d, groundY, p.z + f.z * d);
            }
            return new Vector3(p.x, groundY, p.z);
        }

        /// <summary>Clamps a fractional tile coordinate into the map.</summary>
        public Vector2 ClampTile(Vector2 t)
        {
            if (Map == null) return t;
            return new Vector2(Mathf.Clamp(t.x, 0f, Map.Width - 1), Mathf.Clamp(t.y, 0f, Map.Height - 1));
        }
    }
}
