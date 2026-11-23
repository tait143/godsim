using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GodSim.Bench.Harness
{
    /// <summary>Command-line options shared by every benchmark.</summary>
    public sealed class BenchOptions
    {
        public bool Quick;
        public string Filter;
        public string JsonPath;
        public bool Budget;
        public ulong Seed = 20261119UL;
        public bool Verbose;
        public bool ListOnly;

        /// <summary>Picks the quick or the full value.</summary>
        public int Q(int quick, int full) => Quick ? quick : full;
    }

    /// <summary>
    /// Stopwatch-based measurement core: warmup iterations, N measured iterations, per-op median/p95/mean,
    /// thread allocation delta and GC collection counts. Deliberately tiny so it runs the same on any
    /// machine without BenchmarkDotNet.
    /// </summary>
    public static class Timing
    {
        public static readonly double NsPerTick = 1e9 / Stopwatch.Frequency;

        /// <summary>Minimum warmup wall time per measurement (set from --quick).</summary>
        public static double MinWarmupMs = 400;
        public static int MaxWarmupIterations = 100000;

        /// <summary>Sink for results so the JIT cannot eliminate benchmark bodies.</summary>
        public static long Sink;
        public static double SinkD;

        /// <summary>
        /// Runs <paramref name="body"/> (which performs <paramref name="opsPerIteration"/> operations) warmup+iterations
        /// times. <paramref name="setup"/> (optional) runs before every iteration OUTSIDE the timed/allocation window.
        /// </summary>
        public static BenchResult Measure(string category, string name, string opName, int warmup, int iterations,
            long opsPerIteration, Action body, Action setup = null)
        {
            if (iterations < 1) iterations = 1;
            if (opsPerIteration < 1) opsPerIteration = 1;
            // Warm up for at least `warmup` iterations AND MinWarmupMs of wall time so tiered JIT / dynamic PGO
            // (tier0 -> instrumented -> tier1, each promotion after ~30 calls + a 100 ms quiet period) has finished.
            long w0 = Stopwatch.GetTimestamp();
            for (int i = 0; i < MaxWarmupIterations && (i < warmup || ElapsedMs(w0) < MinWarmupMs); i++) { setup?.Invoke(); body(); }

            var samples = new double[iterations];
            long allocTotal = 0;
            Settle();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            for (int i = 0; i < iterations; i++)
            {
                setup?.Invoke();
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                long t0 = Stopwatch.GetTimestamp();
                body();
                long t1 = Stopwatch.GetTimestamp();
                long a1 = GC.GetAllocatedBytesForCurrentThread();
                allocTotal += a1 - a0;
                samples[i] = (t1 - t0) * NsPerTick / opsPerIteration;
            }
            var r = new BenchResult
            {
                Category = category, Name = name, OpName = opName,
                Iterations = iterations, OpsPerIteration = opsPerIteration, TotalOps = opsPerIteration * iterations,
                AllocBytesTotal = allocTotal,
                AllocBytesPerOp = allocTotal / (double)(opsPerIteration * iterations),
                Gen0 = GC.CollectionCount(0) - g0, Gen1 = GC.CollectionCount(1) - g1, Gen2 = GC.CollectionCount(2) - g2,
            };
            FillStats(r, samples, iterations);
            return r;
        }

        /// <summary>Fills median/p95/mean/min/max from per-op samples (ns). Uses the first <paramref name="count"/> entries.</summary>
        public static void FillStats(BenchResult r, double[] samplesNs, int count)
        {
            if (count <= 0) return;
            var s = new double[count];
            Array.Copy(samplesNs, s, count);
            Array.Sort(s);
            double sum = 0;
            for (int i = 0; i < count; i++) sum += s[i];
            r.MeanNs = sum / count;
            r.MinNs = s[0];
            r.MaxNs = s[count - 1];
            r.MedianNs = Percentile(s, 0.50);
            r.P95Ns = Percentile(s, 0.95);
            if (r.Iterations == 0) r.Iterations = count;
        }

        /// <summary>Linear-interpolated percentile of a sorted array.</summary>
        public static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 0) return double.NaN;
            if (sorted.Length == 1) return sorted[0];
            double pos = p * (sorted.Length - 1);
            int lo = (int)Math.Floor(pos), hi = Math.Min(sorted.Length - 1, lo + 1);
            return sorted[lo] + (sorted[hi] - sorted[lo]) * (pos - lo);
        }

        /// <summary>Full blocking GC so one benchmark's garbage is not billed to the next.</summary>
        public static void Settle()
        {
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            GC.Collect(2, GCCollectionMode.Forced, true, true);
        }

        public static double ElapsedMs(long startTimestamp) => (Stopwatch.GetTimestamp() - startTimestamp) * NsPerTick / 1e6;
    }

    /// <summary>A named benchmark group. Run returns one or more results; exceptions are caught by the runner.</summary>
    public sealed class BenchCase
    {
        public string Category;
        public string Name;
        public string Description;
        public Func<BenchOptions, IEnumerable<BenchResult>> Run;

        public string FullName => Category + "/" + Name;

        public bool Matches(string filter)
        {
            if (string.IsNullOrEmpty(filter)) return true;
            foreach (var part in filter.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (FullName.IndexOf(part.Trim(), StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
        }
    }
}
