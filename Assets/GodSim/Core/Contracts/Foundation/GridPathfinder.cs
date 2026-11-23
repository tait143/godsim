using System;
using System.Collections.Generic;

namespace GodSim.Core
{
    /// <summary>
    /// A* over the tile grid with a caller-supplied cost function (tile index -> cost multiplier;
    /// float.PositiveInfinity = impassable). Bounded by maxExpansions so a pathological query can
    /// never stall a frame (DoS-safety). Not thread-safe; create one per thread.
    /// </summary>
    public sealed class GridPathfinder
    {
        private readonly int _w, _h;
        private float[] _g;
        private int[] _parent;
        private int[] _stamp;
        private int _gen;
        private readonly MinHeap _open = new MinHeap();

        public GridPathfinder(int width, int height)
        {
            _w = width;
            _h = height;
            int n = width * height;
            _g = new float[n];
            _parent = new int[n];
            _stamp = new int[n];
        }

        public int LastExpansions { get; private set; }

        /// <summary>Finds a path from start to goal (inclusive). Returns false if unreachable within budget.</summary>
        public bool FindPath(Vec2i start, Vec2i goal, Func<int, float> cost, List<Vec2i> outPath, bool eightWay = true, int maxExpansions = 20000)
        {
            outPath?.Clear();
            if (start.X < 0 || start.Y < 0 || start.X >= _w || start.Y >= _h) return false;
            if (goal.X < 0 || goal.Y < 0 || goal.X >= _w || goal.Y >= _h) return false;
            unchecked { _gen++; }
            if (_gen == 0) { Array.Clear(_stamp, 0, _stamp.Length); _gen = 1; }
            _open.Clear();
            int s = start.Y * _w + start.X, t = goal.Y * _w + goal.X;
            Visit(s, 0f, -1);
            _open.Push(s, Heuristic(start, goal));
            var dirs = eightWay ? Vec2i.Dirs8 : Vec2i.Dirs4;
            int expansions = 0;
            while (_open.Count > 0)
            {
                int cur = _open.Pop(out float f);
                if (cur == t) { Reconstruct(t, outPath); LastExpansions = expansions; return true; }
                if (++expansions > maxExpansions) break;
                int cx = cur % _w, cy = cur / _w;
                float gCur = _g[cur];
                if (f - Heuristic(new Vec2i(cx, cy), goal) > gCur + 1e-3f) continue; // stale entry
                for (int d = 0; d < dirs.Length; d++)
                {
                    int nx = cx + dirs[d].X, ny = cy + dirs[d].Y;
                    if (nx < 0 || ny < 0 || nx >= _w || ny >= _h) continue;
                    int ni = ny * _w + nx;
                    float c = cost(ni);
                    if (float.IsInfinity(c) || float.IsNaN(c) || c < 0f) continue;
                    bool diag = dirs[d].X != 0 && dirs[d].Y != 0;
                    float ng = gCur + c * (diag ? 1.41421356f : 1f);
                    if (_stamp[ni] == _gen && _g[ni] <= ng) continue;
                    Visit(ni, ng, cur);
                    _open.Push(ni, ng + Heuristic(new Vec2i(nx, ny), goal));
                }
            }
            LastExpansions = expansions;
            return false;
        }

        private void Visit(int i, float g, int parent) { _stamp[i] = _gen; _g[i] = g; _parent[i] = parent; }

        private static float Heuristic(Vec2i a, Vec2i b)
        {
            int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
            return (dx + dy) + (1.41421356f - 2f) * Math.Min(dx, dy);
        }

        private void Reconstruct(int t, List<Vec2i> outPath)
        {
            if (outPath == null) return;
            int cur = t, guard = _w * _h;
            while (cur >= 0 && guard-- > 0) { outPath.Add(new Vec2i(cur % _w, cur / _w)); cur = _parent[cur]; }
            outPath.Reverse();
        }

        private sealed class MinHeap
        {
            private int[] _items = new int[256];
            private float[] _keys = new float[256];
            public int Count { get; private set; }
            public void Clear() => Count = 0;

            public void Push(int item, float key)
            {
                if (Count == _items.Length) { Array.Resize(ref _items, Count * 2); Array.Resize(ref _keys, Count * 2); }
                int i = Count++;
                while (i > 0)
                {
                    int p = (i - 1) >> 1;
                    if (_keys[p] <= key) break;
                    _items[i] = _items[p]; _keys[i] = _keys[p]; i = p;
                }
                _items[i] = item; _keys[i] = key;
            }

            public int Pop(out float key)
            {
                int top = _items[0]; key = _keys[0];
                int lastItem = _items[--Count]; float lastKey = _keys[Count];
                int i = 0;
                while (true)
                {
                    int l = 2 * i + 1;
                    if (l >= Count) break;
                    int r = l + 1, m = (r < Count && _keys[r] < _keys[l]) ? r : l;
                    if (_keys[m] >= lastKey) break;
                    _items[i] = _items[m]; _keys[i] = _keys[m]; i = m;
                }
                if (Count > 0) { _items[i] = lastItem; _keys[i] = lastKey; }
                return top;
            }
        }
    }
}
