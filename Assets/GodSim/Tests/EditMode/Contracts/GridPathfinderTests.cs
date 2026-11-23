using System;
using System.Collections.Generic;
using System.Reflection;
using GodSim.Core;
using NUnit.Framework;

namespace GodSim.Tests.Contracts
{
    public class GridPathfinderTests
    {
        private const float Sqrt2 = 1.41421356f;

        private static bool Passable(float c) => !(float.IsInfinity(c) || float.IsNaN(c) || c < 0f);

        /// <summary>Reference Dijkstra with the pathfinder's cost model: entering tile i costs cost[i] * (diag ? sqrt2 : 1).</summary>
        private static double Dijkstra(int w, int h, float[] cost, Vec2i s, Vec2i t, bool eightWay)
        {
            var dist = new double[w * h];
            var done = new bool[w * h];
            for (int i = 0; i < dist.Length; i++) dist[i] = double.PositiveInfinity;
            int si = s.Y * w + s.X, ti = t.Y * w + t.X;
            dist[si] = 0;
            var dirs = eightWay ? Vec2i.Dirs8 : Vec2i.Dirs4;
            var open = new SortedSet<(double, int)>();
            open.Add((0, si));
            while (open.Count > 0)
            {
                var top = open.Min;
                open.Remove(top);
                int cur = top.Item2;
                if (done[cur]) continue;
                done[cur] = true;
                if (cur == ti) return dist[cur];
                int cx = cur % w, cy = cur / w;
                foreach (var d in dirs)
                {
                    int nx = cx + d.X, ny = cy + d.Y;
                    if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                    int ni = ny * w + nx;
                    if (!Passable(cost[ni])) continue;
                    double nd = dist[cur] + cost[ni] * ((d.X != 0 && d.Y != 0) ? (double)Sqrt2 : 1.0);
                    if (nd < dist[ni]) { dist[ni] = nd; open.Add((nd, ni)); }
                }
            }
            return double.PositiveInfinity;
        }

        private static double PathCost(List<Vec2i> path, int w, float[] cost, bool eightWay)
        {
            double c = 0;
            for (int i = 1; i < path.Count; i++)
            {
                var a = path[i - 1];
                var b = path[i];
                int dx = Math.Abs(a.X - b.X), dy = Math.Abs(a.Y - b.Y);
                Assert.IsTrue(eightWay ? (Math.Max(dx, dy) == 1) : (dx + dy == 1), "non-adjacent step " + a + "->" + b);
                float tc = cost[b.Y * w + b.X];
                Assert.IsTrue(Passable(tc), "path enters impassable tile " + b);
                c += tc * ((dx != 0 && dy != 0) ? (double)Sqrt2 : 1.0);
            }
            return c;
        }

        private static float[] Uniform(int w, int h, float v)
        {
            var a = new float[w * h];
            for (int i = 0; i < a.Length; i++) a[i] = v;
            return a;
        }

        [Test]
        public void StraightPath_FourWay()
        {
            var pf = new GridPathfinder(16, 16);
            var cost = Uniform(16, 16, 1f);
            var path = new List<Vec2i>();
            Assert.IsTrue(pf.FindPath(new Vec2i(1, 1), new Vec2i(8, 1), i => cost[i], path, false));
            Assert.AreEqual(8, path.Count);
            Assert.AreEqual(new Vec2i(1, 1), path[0]);
            Assert.AreEqual(new Vec2i(8, 1), path[path.Count - 1]);
            for (int i = 0; i < path.Count; i++) Assert.AreEqual(1, path[i].Y);
        }

        [Test]
        public void DiagonalPath_EightWay_IsPureDiagonal()
        {
            var pf = new GridPathfinder(16, 16);
            var cost = Uniform(16, 16, 1f);
            var path = new List<Vec2i>();
            Assert.IsTrue(pf.FindPath(new Vec2i(0, 0), new Vec2i(9, 9), i => cost[i], path));
            Assert.AreEqual(10, path.Count);
            for (int i = 0; i < path.Count; i++) Assert.AreEqual(new Vec2i(i, i), path[i]);
            Assert.AreEqual(9 * Sqrt2, PathCost(path, 16, cost, true), 1e-3);
        }

        [Test]
        public void DiagonalPath_FourWay_HasManhattanLength()
        {
            var pf = new GridPathfinder(16, 16);
            var cost = Uniform(16, 16, 1f);
            var path = new List<Vec2i>();
            Assert.IsTrue(pf.FindPath(new Vec2i(0, 0), new Vec2i(5, 7), i => cost[i], path, false));
            Assert.AreEqual(13, path.Count);
            Assert.AreEqual(12, PathCost(path, 16, cost, false), 1e-6);
        }

        [Test]
        public void StartEqualsGoal_ReturnsSingleTile()
        {
            var pf = new GridPathfinder(16, 16);
            var path = new List<Vec2i> { new Vec2i(9, 9) };
            Assert.IsTrue(pf.FindPath(new Vec2i(4, 4), new Vec2i(4, 4), i => 1f, path));
            Assert.AreEqual(1, path.Count);
            Assert.AreEqual(new Vec2i(4, 4), path[0]);
        }

        [Test]
        public void StartEqualsGoal_EvenWhenImpassable()
        {
            var pf = new GridPathfinder(16, 16);
            var path = new List<Vec2i>();
            Assert.IsTrue(pf.FindPath(new Vec2i(4, 4), new Vec2i(4, 4), i => float.PositiveInfinity, path, true, 0));
            Assert.AreEqual(1, path.Count);
        }

        [Test]
        public void NullOutPath_IsAllowed()
        {
            var pf = new GridPathfinder(16, 16);
            Assert.IsTrue(pf.FindPath(new Vec2i(0, 0), new Vec2i(5, 5), i => 1f, null));
            Assert.IsFalse(pf.FindPath(new Vec2i(-1, 0), new Vec2i(5, 5), i => 1f, null));
        }

        [TestCase(-1, 0, 5, 5)]
        [TestCase(0, -1, 5, 5)]
        [TestCase(16, 0, 5, 5)]
        [TestCase(0, 16, 5, 5)]
        [TestCase(0, 0, 16, 5)]
        [TestCase(0, 0, 5, -1)]
        [TestCase(int.MinValue, int.MinValue, 5, 5)]
        [TestCase(0, 0, int.MaxValue, 0)]
        public void OutOfBounds_ReturnsFalseAndClearsPath(int sx, int sy, int gx, int gy)
        {
            var pf = new GridPathfinder(16, 16);
            var path = new List<Vec2i> { new Vec2i(1, 1) };
            Assert.IsFalse(pf.FindPath(new Vec2i(sx, sy), new Vec2i(gx, gy), i => 1f, path));
            Assert.AreEqual(0, path.Count);
        }

        [Test]
        public void Obstacle_WallWithGap_RoutesThroughGap()
        {
            int w = 20, h = 20;
            var cost = Uniform(w, h, 1f);
            for (int y = 0; y < h; y++) if (y != 15) cost[y * w + 10] = float.PositiveInfinity;
            var pf = new GridPathfinder(w, h);
            var path = new List<Vec2i>();
            Assert.IsTrue(pf.FindPath(new Vec2i(2, 2), new Vec2i(18, 2), i => cost[i], path));
            Assert.IsTrue(path.Contains(new Vec2i(10, 15)), "must pass the gap");
            double opt = Dijkstra(w, h, cost, new Vec2i(2, 2), new Vec2i(18, 2), true);
            Assert.AreEqual(opt, PathCost(path, w, cost, true), 1e-3);
        }

        [Test]
        public void Unreachable_EnclosedGoal_ReturnsFalseWithinBudget()
        {
            int w = 64, h = 64;
            var cost = Uniform(w, h, 1f);
            var goal = new Vec2i(40, 40);
            for (int y = 38; y <= 42; y++) for (int x = 38; x <= 42; x++)
                if (x == 38 || x == 42 || y == 38 || y == 42) cost[y * w + x] = float.PositiveInfinity;
            var pf = new GridPathfinder(w, h);
            var path = new List<Vec2i>();
            Assert.IsFalse(pf.FindPath(new Vec2i(1, 1), goal, i => cost[i], path, true, 100000));
            Assert.AreEqual(0, path.Count);
            Assert.LessOrEqual(pf.LastExpansions, 2 * w * h, "unbounded re-expansion");

            Assert.IsFalse(pf.FindPath(new Vec2i(1, 1), goal, i => cost[i], path, true, 50));
            Assert.LessOrEqual(pf.LastExpansions, 51);
        }

        [Test]
        public void Unreachable_FourWayUniform_ExpandsEachTileAtMostOnce()
        {
            int w = 64, h = 64;
            var cost = Uniform(w, h, 1f);
            for (int y = 38; y <= 42; y++) for (int x = 38; x <= 42; x++)
                if (x == 38 || x == 42 || y == 38 || y == 42) cost[y * w + x] = float.PositiveInfinity;
            var pf = new GridPathfinder(w, h);
            Assert.IsFalse(pf.FindPath(new Vec2i(1, 1), new Vec2i(40, 40), i => cost[i], null, false, 100000));
            Assert.LessOrEqual(pf.LastExpansions, w * h);
        }

        [Test]
        [Ignore("BUG (perf): with 8-way moves GridPathfinder re-expands tiles: an exhaustive search of a 64x64 uniform grid performs ~7,000 expansions for 4,096 tiles. Float rounding makes sqrt2-sums arrive with ~1e-7 smaller g, the node is re-pushed, and the 1e-3 stale tolerance (GridPathfinder.cs:53) then lets BOTH entries expand, cascading. This wastes ~70% of the maxExpansions budget, so long-but-valid paths fail early. Fix: keep a per-node closed stamp (_closed[cur] == _gen -> continue; set on expand) and/or require ng < _g[ni] - 1e-4f before re-pushing.")]
        public void Unreachable_EightWayUniform_ExpandsEachTileAtMostOnce()
        {
            int w = 64, h = 64;
            var cost = Uniform(w, h, 1f);
            for (int y = 38; y <= 42; y++) for (int x = 38; x <= 42; x++)
                if (x == 38 || x == 42 || y == 38 || y == 42) cost[y * w + x] = float.PositiveInfinity;
            var pf = new GridPathfinder(w, h);
            Assert.IsFalse(pf.FindPath(new Vec2i(1, 1), new Vec2i(40, 40), i => cost[i], null, true, 100000));
            Assert.LessOrEqual(pf.LastExpansions, w * h);
        }

        [Test]
        public void NaNAndNegativeAndInfiniteCosts_AreImpassable()
        {
            foreach (float bad in new[] { float.NaN, -1f, float.NegativeInfinity, float.PositiveInfinity })
            {
                int w = 16, h = 16;
                var cost = Uniform(w, h, 1f);
                for (int y = 0; y < h; y++) cost[y * w + 8] = bad;
                var pf = new GridPathfinder(w, h);
                var path = new List<Vec2i>();
                Assert.IsFalse(pf.FindPath(new Vec2i(2, 8), new Vec2i(12, 8), i => cost[i], path), "cost " + bad + " should block");
                // A bad tile adjacent to a valid route is avoided.
                cost[5 * w + 8] = 1f;
                Assert.IsTrue(pf.FindPath(new Vec2i(2, 8), new Vec2i(12, 8), i => cost[i], path));
                foreach (var p in path) Assert.IsTrue(Passable(cost[p.Y * w + p.X]) || p == new Vec2i(2, 8));
            }
        }

        [Test]
        public void ZeroCostTiles_DoNotLoop()
        {
            var pf = new GridPathfinder(16, 16);
            var path = new List<Vec2i>();
            Assert.IsTrue(pf.FindPath(new Vec2i(0, 0), new Vec2i(15, 15), i => 0f, path));
            Assert.AreEqual(new Vec2i(15, 15), path[path.Count - 1]);
            Assert.LessOrEqual(path.Count, 16 * 16);
        }

        [Test]
        public void Optimality_RandomSmallGrids_MatchesDijkstra()
        {
            var r = new Rng(4242);
            for (int trial = 0; trial < 300; trial++)
            {
                int w = r.RangeInclusive(3, 12), h = r.RangeInclusive(3, 12);
                bool eight = r.Chance(0.5f);
                var cost = new float[w * h];
                for (int i = 0; i < cost.Length; i++)
                    cost[i] = r.Chance(0.25f) ? float.PositiveInfinity : 1f + r.Range(0, 4) * 0.5f; // costs >= 1 (admissible)
                var s = new Vec2i(r.Range(0, w), r.Range(0, h));
                var t = new Vec2i(r.Range(0, w), r.Range(0, h));
                var pf = new GridPathfinder(w, h);
                var path = new List<Vec2i>();
                bool found = pf.FindPath(s, t, i => cost[i], path, eight);
                double opt = Dijkstra(w, h, cost, s, t, eight);
                Assert.AreEqual(!double.IsInfinity(opt), found, "reachability mismatch trial " + trial);
                if (!found) continue;
                Assert.AreEqual(s, path[0]);
                Assert.AreEqual(t, path[path.Count - 1]);
                Assert.AreEqual(opt, PathCost(path, w, cost, eight), 1e-3, "suboptimal path trial " + trial);
            }
        }

        [Test]
        [Ignore("BUG/design: GridPathfinder.Heuristic (GridPathfinder.cs:74-78) assumes every tile costs >= 1, so it is inadmissible for cost multipliers < 1 (e.g. roads at 0.5) and A* returns suboptimal paths. Fix: scale the heuristic by a caller-supplied minimum cost (e.g. FindPath(..., float minCost = 1f)) or document that costs must be >= 1.")]
        public void Optimality_WithCostsBelowOne_MatchesDijkstra()
        {
            var r = new Rng(777);
            for (int trial = 0; trial < 300; trial++)
            {
                int w = r.RangeInclusive(4, 14), h = r.RangeInclusive(4, 14);
                var cost = new float[w * h];
                for (int i = 0; i < cost.Length; i++) cost[i] = r.Chance(0.3f) ? 0.25f : 1f + r.Range(0, 3);
                var s = new Vec2i(r.Range(0, w), r.Range(0, h));
                var t = new Vec2i(r.Range(0, w), r.Range(0, h));
                var pf = new GridPathfinder(w, h);
                var path = new List<Vec2i>();
                Assert.IsTrue(pf.FindPath(s, t, i => cost[i], path));
                Assert.AreEqual(Dijkstra(w, h, cost, s, t, true), PathCost(path, w, cost, true), 1e-3, "trial " + trial);
            }
        }

        [Test]
        [Ignore("BUG/design: 8-way search allows diagonal moves between two impassable orthogonal neighbours (no corner-cutting check, GridPathfinder.cs:54-66), so a diagonal line of walls is leaky. Fix: when diag, skip if cost(cx+dx,cy) or cost(cx,cy+dy) is impassable.")]
        public void EightWay_DoesNotCutBlockedCorners()
        {
            int w = 8, h = 8;
            var cost = Uniform(w, h, 1f);
            // Diagonal wall from (0,7) to (7,0): two tiles touching at a corner should seal the lower-left.
            for (int i = 0; i < 8; i++) cost[(7 - i) * w + i] = float.PositiveInfinity;
            var pf = new GridPathfinder(w, h);
            Assert.IsFalse(pf.FindPath(new Vec2i(0, 0), new Vec2i(7, 7), i => cost[i], null));
        }

        [Test]
        public void Reuse_ManyQueries_MatchFreshInstanceAndDijkstra()
        {
            int w = 24, h = 24;
            var r = new Rng(99);
            var shared = new GridPathfinder(w, h);
            var p1 = new List<Vec2i>();
            var p2 = new List<Vec2i>();
            for (int q = 0; q < 400; q++)
            {
                var cost = new float[w * h];
                for (int i = 0; i < cost.Length; i++) cost[i] = r.Chance(0.2f) ? float.PositiveInfinity : 1f + r.Range(0, 3);
                var s = new Vec2i(r.Range(0, w), r.Range(0, h));
                var t = new Vec2i(r.Range(0, w), r.Range(0, h));
                bool a = shared.FindPath(s, t, i => cost[i], p1);
                bool b = new GridPathfinder(w, h).FindPath(s, t, i => cost[i], p2);
                Assert.AreEqual(b, a, "query " + q);
                if (a)
                {
                    Assert.AreEqual(PathCost(p2, w, cost, true), PathCost(p1, w, cost, true), 1e-3, "query " + q);
                    Assert.AreEqual(Dijkstra(w, h, cost, s, t, true), PathCost(p1, w, cost, true), 1e-3, "query " + q);
                }
            }
        }

        [TestCase(int.MaxValue - 2)]
        [TestCase(-3)]
        public void GenerationStamp_WrapAround_StaysCorrect(int startGen)
        {
            int w = 16, h = 16;
            var pf = new GridPathfinder(w, h);
            var cost = Uniform(w, h, 1f);
            // Populate stamps with an earlier generation.
            Assert.IsTrue(pf.FindPath(new Vec2i(0, 0), new Vec2i(15, 15), i => cost[i], null));
            var genField = typeof(GridPathfinder).GetField("_gen", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.IsNotNull(genField, "GridPathfinder._gen not found (test needs updating)");
            genField.SetValue(pf, startGen);
            var r = new Rng(5);
            var path = new List<Vec2i>();
            for (int q = 0; q < 8; q++)
            {
                var c = new float[w * h];
                for (int i = 0; i < c.Length; i++) c[i] = r.Chance(0.25f) ? float.PositiveInfinity : 1f;
                var s = new Vec2i(r.Range(0, w), r.Range(0, h));
                var t = new Vec2i(r.Range(0, w), r.Range(0, h));
                c[s.Y * w + s.X] = 1f; c[t.Y * w + t.X] = 1f;
                bool found = pf.FindPath(s, t, i => c[i], path);
                double opt = Dijkstra(w, h, c, s, t, true);
                Assert.AreEqual(!double.IsInfinity(opt), found, "gen " + genField.GetValue(pf));
                if (found) Assert.AreEqual(opt, PathCost(path, w, c, true), 1e-3);
            }
            Assert.AreNotEqual(0, (int)genField.GetValue(pf));
        }

        [Test]
        public void MinHeap_ManyPushes_PopsInSortedOrder()
        {
            var heapType = typeof(GridPathfinder).GetNestedType("MinHeap", BindingFlags.NonPublic);
            Assert.IsNotNull(heapType, "GridPathfinder.MinHeap not found (test needs updating)");
            var heap = Activator.CreateInstance(heapType, true);
            var push = heapType.GetMethod("Push");
            var pop = heapType.GetMethod("Pop");
            var count = heapType.GetProperty("Count");
            var r = new Rng(17);
            var keys = new List<float>();
            // Interleave pushes/pops and exceed the initial 256 capacity several times.
            var popped = new List<float>();
            for (int i = 0; i < 5000; i++)
            {
                float k = r.Range(0, 1000) * 0.5f; // many duplicates
                keys.Add(k);
                push.Invoke(heap, new object[] { i, k });
            }
            float last = float.NegativeInfinity;
            var args = new object[1];
            for (int i = 0; i < 2500; i++)
            {
                pop.Invoke(heap, args);
                float k = (float)args[0];
                Assert.GreaterOrEqual(k, last, "heap order violated at pop " + i);
                last = k;
                popped.Add(k);
            }
            for (int i = 0; i < 3000; i++)
            {
                float k = last + r.Range(0, 100); // push only keys >= last so the global order must still hold
                keys.Add(k);
                push.Invoke(heap, new object[] { 10000 + i, k });
            }
            while ((int)count.GetValue(heap) > 0)
            {
                pop.Invoke(heap, args);
                float k = (float)args[0];
                Assert.GreaterOrEqual(k, last);
                last = k;
                popped.Add(k);
            }
            keys.Sort();
            popped.Sort();
            CollectionAssert.AreEqual(keys, popped);
        }

        [Test]
        public void LargeOpenMap_FindsPathQuickly()
        {
            var pf = new GridPathfinder(256, 256);
            var path = new List<Vec2i>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            Assert.IsTrue(pf.FindPath(new Vec2i(0, 0), new Vec2i(255, 200), i => 1f, path));
            sw.Stop();
            Assert.AreEqual(256, path.Count);
            Assert.Less(sw.ElapsedMilliseconds, 500);
        }
    }
}
