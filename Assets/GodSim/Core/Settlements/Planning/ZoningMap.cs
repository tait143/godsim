using System;

namespace GodSim.Core.Settlements
{
    /// <summary>
    /// Per-settlement zoning fields over a square window around the settlement center: neighbor
    /// <b>desirability</b> (sum of <see cref="BuildingDefinition.Desirability"/> with distance falloff) and
    /// <b>residential density</b> (closeness to dwellings, civic and religious buildings). Rebuilt once per
    /// planner run per settlement and stamped incrementally as buildings are placed. Arrays are reused.
    /// </summary>
    public sealed class ZoningMap
    {
        /// <summary>Extra tiles beyond the planning radius kept in the window.</summary>
        public const int Margin = 6;
        /// <summary>Chebyshev radius of a building's desirability influence.</summary>
        public const int InfluenceRadius = 4;
        /// <summary>Chebyshev radius of a building's residential-density influence.</summary>
        public const int ResidentialRadius = 3;

        private float[] _desirability = new float[0];
        private float[] _residential = new float[0];
        private int _minX, _minY, _size;

        /// <summary>Window origin X (may be negative near map edges).</summary>
        public int MinX => _minX;
        /// <summary>Window origin Y.</summary>
        public int MinY => _minY;
        /// <summary>Window side length in tiles (0 before the first build).</summary>
        public int Size => _size;
        /// <summary>Tiles cleared and written by the last <see cref="Build"/> (work accounting for the planner budget).</summary>
        public int LastBuildTiles { get; private set; }

        /// <summary>True for buildings that make an area residential (dwellings, civic, religion, town center).</summary>
        public static bool IsResidentialAnchor(BuildingDefinition def)
        {
            if (def == null) return false;
            return def.IsDwelling || def.Type == BuildingType.TownCenter
                || def.Category == BuildingCategory.Civic || def.Category == BuildingCategory.Religion;
        }

        /// <summary>Rebuilds both fields from the settlement's existing (non-destroyed) buildings.</summary>
        public void Build(WorldState state, GameDatabase db, Settlement s, int radius)
        {
            LastBuildTiles = 0;
            if (state == null || db == null || s == null) { _size = 0; return; }
            radius = MathUtil.Clamp(radius, 1, 64);
            int r = radius + Margin;
            _minX = s.Center.X - r;
            _minY = s.Center.Y - r;
            _size = 2 * r + 1;
            int n = _size * _size;
            if (_desirability.Length < n)
            {
                _desirability = new float[n];
                _residential = new float[n];
            }
            else
            {
                Array.Clear(_desirability, 0, n);
                Array.Clear(_residential, 0, n);
            }
            int work = n;
            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = state.Buildings.Get(ids[k]);
                if (b == null || b.IsDestroyed) continue;
                if (db.TryGetBuilding(b.Type, out var def)) work += Stamp(b, def);
            }
            LastBuildTiles = work;
        }

        /// <summary>Adds one building's influence to the fields (no-op outside the window). Returns tiles visited.</summary>
        public int Stamp(Building b, BuildingDefinition def)
        {
            if (b == null || def == null || _size == 0) return 0;
            float des = MathUtil.Clamp(MathUtil.Sanitize(def.Desirability), -1f, 1f);
            bool residential = IsResidentialAnchor(def);
            if (des == 0f && !residential) return 0;
            int visited = 0;
            int x0 = b.Origin.X, y0 = b.Origin.Y, x1 = x0 + Math.Max(1, b.Width) - 1, y1 = y0 + Math.Max(1, b.Height) - 1;
            int rad = InfluenceRadius;
            for (int y = y0 - rad; y <= y1 + rad; y++)
            {
                int ly = y - _minY;
                if (ly < 0 || ly >= _size) continue;
                int dy = y < y0 ? y0 - y : (y > y1 ? y - y1 : 0);
                for (int x = x0 - rad; x <= x1 + rad; x++)
                {
                    int lx = x - _minX;
                    if (lx < 0 || lx >= _size) continue;
                    int dx = x < x0 ? x0 - x : (x > x1 ? x - x1 : 0);
                    int d = dx > dy ? dx : dy;
                    int k = ly * _size + lx;
                    visited++;
                    if (des != 0f) _desirability[k] += des * (1f - d / (rad + 1f));
                    if (residential && d <= ResidentialRadius) _residential[k] += 1f - d / (ResidentialRadius + 1f);
                }
            }
            return visited;
        }

        /// <summary>Desirability at a tile (0 outside the window).</summary>
        public float DesirabilityAt(int x, int y)
        {
            int k = WindowIndex(x, y);
            return k < 0 ? 0f : _desirability[k];
        }

        /// <summary>Residential density at a tile (0 outside the window).</summary>
        public float ResidentialAt(int x, int y)
        {
            int k = WindowIndex(x, y);
            return k < 0 ? 0f : _residential[k];
        }

        /// <summary>Average desirability over a footprint.</summary>
        public float AverageDesirability(Vec2i origin, int w, int h) => Average(_desirability, origin, w, h);

        /// <summary>Average residential density over a footprint.</summary>
        public float AverageResidential(Vec2i origin, int w, int h) => Average(_residential, origin, w, h);

        private float Average(float[] field, Vec2i origin, int w, int h)
        {
            if (_size == 0 || w <= 0 || h <= 0) return 0f;
            float sum = 0f;
            for (int y = origin.Y; y < origin.Y + h; y++)
                for (int x = origin.X; x < origin.X + w; x++)
                {
                    int k = WindowIndex(x, y);
                    if (k >= 0) sum += field[k];
                }
            return MathUtil.Sanitize(sum / (w * h));
        }

        private int WindowIndex(int x, int y)
        {
            int lx = x - _minX, ly = y - _minY;
            if (_size == 0 || lx < 0 || ly < 0 || lx >= _size || ly >= _size) return -1;
            return ly * _size + lx;
        }
    }
}
