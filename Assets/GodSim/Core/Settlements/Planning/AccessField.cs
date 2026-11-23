using System.Collections.Generic;

namespace GodSim.Core.Settlements
{
    /// <summary>
    /// Road reachability of the tiles around a settlement, inside a square window centred on it.
    /// <para>
    /// The settlement's <b>road network</b> is seeded from the tiles around its Town Center (the plaza ring,
    /// or the nearest passable tile to the centre when there is no Town Center). It then grows through
    /// every Road tile connected to those seeds. All network tiles have distance 0. A breadth-first search
    /// from the network over every tile a road may be built on (finite <see cref="PlannerRoads.TileCost"/>)
    /// gives each reachable tile its step distance to the network and a parent pointer one step closer.
    /// </para>
    /// The planner uses it to reject sites whose entrance cannot reach the town, to choose the entrance
    /// closest to the network, and to aim the access road at the nearest network tile. Road stubs that are
    /// not linked to the network do not count as network. Buffers are reused, and the per-build reset is an
    /// integer stamp, so a build costs time in proportion to the tiles it reaches. Not thread-safe.
    /// </summary>
    public sealed class AccessField
    {
        /// <summary>Distance reported for tiles that are outside the window or cannot be reached.</summary>
        public const int Unreachable = -1;

        private int[] _stamp = new int[0];
        private int[] _dist = new int[0];
        private int[] _parent = new int[0];
        private int[] _queue = new int[0];
        private int _gen;
        private int _minX, _minY, _size;

        /// <summary>Window origin X (may be negative near map edges).</summary>
        public int MinX => _minX;
        /// <summary>Window origin Y.</summary>
        public int MinY => _minY;
        /// <summary>Window side length in tiles (0 before the first build or after a failed one).</summary>
        public int Size => _size;
        /// <summary>Tiles at distance 0 (the town center approach plus the road network linked to it).</summary>
        public int NetworkTiles { get; private set; }
        /// <summary>Tiles reached by the last build (network included).</summary>
        public int ReachableTiles { get; private set; }

        /// <summary>
        /// Rebuilds the field for settlement <paramref name="s"/> over the window of half-size
        /// <paramref name="halfSize"/> around <see cref="Settlement.Center"/>. Never throws on bad data;
        /// an invalid input leaves an empty field (everything unreachable).
        /// </summary>
        public void Build(WorldState state, Settlement s, int halfSize)
        {
            _size = 0;
            NetworkTiles = 0;
            ReachableTiles = 0;
            if (state == null || s == null || state.Map == null) return;
            var map = state.Map;
            if (!map.InBounds(s.Center)) return;
            halfSize = MathUtil.Clamp(halfSize, 2, 96);
            _minX = s.Center.X - halfSize;
            _minY = s.Center.Y - halfSize;
            _size = 2 * halfSize + 1;
            int n = _size * _size;
            if (_stamp.Length < n)
            {
                _stamp = new int[n];
                _dist = new int[n];
                _parent = new int[n];
                _queue = new int[n];
                _gen = 0;
            }
            unchecked { _gen++; }
            if (_gen <= 0) { System.Array.Clear(_stamp, 0, _stamp.Length); _gen = 1; }

            int tail = 0;
            var tc = s.TownCenterId >= 0 ? state.Buildings.Get(s.TownCenterId) : null;
            if (tc != null && !tc.IsDestroyed)
            {
                int x0 = tc.Origin.X - 1, y0 = tc.Origin.Y - 1, x1 = tc.Origin.X + tc.Width, y1 = tc.Origin.Y + tc.Height;
                for (int y = y0; y <= y1; y++)
                    for (int x = x0; x <= x1; x++)
                    {
                        if (x > x0 && x < x1 && y > y0 && y < y1) continue;
                        Seed(map, x, y, ref tail);
                    }
            }
            if (tail == 0)
            {
                Vec2i anchor = PlannerRoads.FindAnchor(map, s.Center, 4);
                if (anchor.IsValid) Seed(map, anchor.X, anchor.Y, ref tail);
            }

            // Phase 1: the road network = roads linked to the seeds through roads only (distance 0).
            int head = 0;
            while (head < tail)
            {
                int cur = _queue[head++];
                int cx = _minX + cur % _size, cy = _minY + cur / _size;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + Vec2i.Dirs4[d].X, ny = cy + Vec2i.Dirs4[d].Y;
                    int k = WindowIndex(nx, ny);
                    if (k < 0 || _stamp[k] == _gen || !map.InBounds(nx, ny)) continue;
                    int i = map.Index(nx, ny);
                    if ((map.Flags[i] & TileFlags.Road) == 0 || float.IsInfinity(PlannerRoads.TileCost(map, i))) continue;
                    Mark(k, 0, cur);
                    _queue[tail++] = k;
                }
            }
            NetworkTiles = tail;

            // Phase 2: breadth-first distance from the network over every road-capable tile.
            head = 0;
            while (head < tail)
            {
                int cur = _queue[head++];
                int cx = _minX + cur % _size, cy = _minY + cur / _size;
                int nd = _dist[cur] + 1;
                for (int d = 0; d < 4; d++)
                {
                    int nx = cx + Vec2i.Dirs4[d].X, ny = cy + Vec2i.Dirs4[d].Y;
                    int k = WindowIndex(nx, ny);
                    if (k < 0 || _stamp[k] == _gen || !map.InBounds(nx, ny)) continue;
                    if (float.IsInfinity(PlannerRoads.TileCost(map, map.Index(nx, ny)))) continue;
                    Mark(k, nd, cur);
                    _queue[tail++] = k;
                }
            }
            ReachableTiles = tail;
        }

        private void Seed(WorldMap map, int x, int y, ref int tail)
        {
            int k = WindowIndex(x, y);
            if (k < 0 || _stamp[k] == _gen || !map.InBounds(x, y)) return;
            if (float.IsInfinity(PlannerRoads.TileCost(map, map.Index(x, y)))) return;
            Mark(k, 0, -1);
            _queue[tail++] = k;
        }

        private void Mark(int k, int dist, int parent)
        {
            _stamp[k] = _gen;
            _dist[k] = dist;
            _parent[k] = parent;
        }

        /// <summary>Step distance from (x, y) to the road network: 0 = on it, <see cref="Unreachable"/> = no route inside the window.</summary>
        public int DistanceAt(int x, int y)
        {
            int k = WindowIndex(x, y);
            return k < 0 || _stamp[k] != _gen ? Unreachable : _dist[k];
        }

        /// <summary>Step distance from <paramref name="p"/> to the road network (see <see cref="DistanceAt(int,int)"/>).</summary>
        public int DistanceAt(Vec2i p) => p.IsValid ? DistanceAt(p.X, p.Y) : Unreachable;

        /// <summary>True if a road can be built from <paramref name="p"/> to the settlement's road network.</summary>
        public bool IsReachable(Vec2i p) => DistanceAt(p) >= 0;

        /// <summary>True if <paramref name="p"/> is part of the road network (or the town center approach).</summary>
        public bool IsNetwork(Vec2i p) => DistanceAt(p) == 0;

        /// <summary>The network tile reached by following the parent chain from <paramref name="p"/>; Invalid if unreachable.</summary>
        public Vec2i NearestNetworkTile(Vec2i p)
        {
            int k = p.IsValid ? WindowIndex(p.X, p.Y) : -1;
            if (k < 0 || _stamp[k] != _gen) return Vec2i.Invalid;
            int guard = _size * _size;
            while (_dist[k] > 0 && guard-- > 0)
            {
                k = _parent[k];
                if (k < 0 || _stamp[k] != _gen) return Vec2i.Invalid;
            }
            return new Vec2i(_minX + k % _size, _minY + k / _size);
        }

        /// <summary>
        /// Copies the shortest step path from <paramref name="from"/> to the network (both ends inclusive) into
        /// <paramref name="outPath"/>. Fails (and clears the list) if the tile is unreachable, or if the path
        /// enters the rectangle at <paramref name="blockOrigin"/> (size <paramref name="blockW"/> x
        /// <paramref name="blockH"/>; use 0 x 0 for none), or a tile is no longer road-capable on
        /// <paramref name="map"/>.
        /// </summary>
        public bool TryGetPathToNetwork(WorldMap map, Vec2i from, List<Vec2i> outPath, Vec2i blockOrigin, int blockW, int blockH)
        {
            if (outPath == null) return false;
            outPath.Clear();
            if (map == null) return false;
            int k = from.IsValid ? WindowIndex(from.X, from.Y) : -1;
            if (k < 0 || _stamp[k] != _gen) return false;
            int guard = _size * _size;
            while (guard-- > 0)
            {
                int x = _minX + k % _size, y = _minY + k / _size;
                bool blocked = x >= blockOrigin.X && y >= blockOrigin.Y && x < blockOrigin.X + blockW && y < blockOrigin.Y + blockH;
                if (blocked || !map.InBounds(x, y) || float.IsInfinity(PlannerRoads.TileCost(map, map.Index(x, y)))) { outPath.Clear(); return false; }
                outPath.Add(new Vec2i(x, y));
                if (_dist[k] == 0) return true;
                k = _parent[k];
                if (k < 0 || _stamp[k] != _gen) break;
            }
            outPath.Clear();
            return false;
        }

        private int WindowIndex(int x, int y)
        {
            int lx = x - _minX, ly = y - _minY;
            if (_size == 0 || lx < 0 || ly < 0 || lx >= _size || ly >= _size) return -1;
            return ly * _size + lx;
        }
    }
}
