using System;
using System.Collections.Generic;
using System.Diagnostics;
using GodSim.Core;

namespace GodSim.Bench.Harness
{
    /// <summary>
    /// Measures a window of simulation ticks: wall time of each Step() (median/p95/mean), allocations and GC
    /// counts for the window, and an exact per-system breakdown built by reading SystemProfile.LastMs whenever
    /// SystemProfile.Runs advances (more precise than the engine's EMA AvgMs, and amortized per tick so
    /// interval systems are comparable with every-tick systems).
    /// </summary>
    public sealed class SimSampler
    {
        private readonly SimulationEngine _engine;
        private readonly long[] _lastRuns;
        private readonly long[] _runs;
        private readonly double[] _total;
        private readonly double[] _max;
        private readonly int[] _faultsAtStart;

        public SimSampler(SimulationEngine engine)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            int n = engine.Profiles.Count;
            _lastRuns = new long[n];
            _runs = new long[n];
            _total = new double[n];
            _max = new double[n];
            _faultsAtStart = new int[n];
            for (int i = 0; i < n; i++) { _lastRuns[i] = engine.Profiles[i].Runs; _faultsAtStart[i] = engine.Profiles[i].TotalFaults; }
        }

        /// <summary>Steps <paramref name="ticks"/> times through <paramref name="step"/>, sampling after each, and returns the result.</summary>
        public BenchResult Run(string category, string name, int ticks, Action step)
        {
            var samples = new double[ticks];
            Timing.Settle();
            int g0 = GC.CollectionCount(0), g1 = GC.CollectionCount(1), g2 = GC.CollectionCount(2);
            long allocStart = GC.GetAllocatedBytesForCurrentThread();
            long allocExcluded = 0;
            var profiles = _engine.Profiles;
            for (int t = 0; t < ticks; t++)
            {
                long t0 = Stopwatch.GetTimestamp();
                step();
                long t1 = Stopwatch.GetTimestamp();
                samples[t] = (t1 - t0) * Timing.NsPerTick;
                long a0 = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < profiles.Count; i++)
                {
                    var p = profiles[i];
                    if (p.Runs == _lastRuns[i]) continue;
                    _lastRuns[i] = p.Runs;
                    _runs[i]++;
                    _total[i] += p.LastMs;
                    if (p.LastMs > _max[i]) _max[i] = p.LastMs;
                }
                allocExcluded += GC.GetAllocatedBytesForCurrentThread() - a0; // sampler itself (should be 0)
            }
            long alloc = GC.GetAllocatedBytesForCurrentThread() - allocStart - allocExcluded;
            var r = new BenchResult
            {
                Category = category, Name = name, OpName = "tick",
                Iterations = ticks, OpsPerIteration = 1, TotalOps = ticks,
                AllocBytesTotal = alloc, AllocBytesPerOp = alloc / (double)Math.Max(1, ticks),
                Gen0 = GC.CollectionCount(0) - g0, Gen1 = GC.CollectionCount(1) - g1, Gen2 = GC.CollectionCount(2) - g2,
            };
            Timing.FillStats(r, samples, ticks);

            var systems = _engine.Systems;
            int faults = 0;
            for (int i = 0; i < profiles.Count; i++)
            {
                var p = profiles[i];
                int f = p.TotalFaults - _faultsAtStart[i];
                faults += f;
                r.Systems.Add(new SystemRow
                {
                    Name = p.Name ?? ("system" + i),
                    TickInterval = i < systems.Count ? systems[i].TickInterval : 0,
                    Runs = _runs[i],
                    TotalMs = _total[i],
                    MsPerTick = _total[i] / ticks,
                    MsPerRun = _runs[i] > 0 ? _total[i] / _runs[i] : 0,
                    MaxRunMs = _max[i],
                    EngineAvgMs = p.AvgMs,
                    Faults = f,
                    Disabled = p.Disabled,
                    LastError = p.LastError,
                });
            }
            r.Metric("system_faults_in_window", faults);
            return r;
        }

        /// <summary>Entity/world counters attached to every simulation result.</summary>
        public static void AddWorldMetrics(BenchResult r, WorldState s, string prefix = "")
        {
            int civs = 0, settlements = 0, buildings = 0, armies = 0;
            for (int i = 0; i < s.Civilizations.Count; i++) if (s.Civilizations[i].IsAlive) civs++;
            for (int i = 0; i < s.Settlements.Count; i++) if (s.Settlements[i].IsAlive) settlements++;
            for (int i = 0; i < s.Buildings.Count; i++) if (!s.Buildings[i].IsDestroyed) buildings++;
            var a = s.Diplomacy.Armies;
            for (int i = 0; i < a.Count; i++) if (a[i].IsAlive) armies++;
            r.Metric(prefix + "year", s.Year)
             .Metric(prefix + "people", s.LivingPopulation)
             .Metric(prefix + "buildings", buildings)
             .Metric(prefix + "settlements", settlements)
             .Metric(prefix + "civilizations", civs)
             .Metric(prefix + "armies", armies)
             .Metric(prefix + "world_age", (int)s.WorldAge);
        }
    }

    /// <summary>Counts warnings/errors from the simulation without spamming the console.</summary>
    public sealed class CountingLogger : ISimLogger
    {
        public readonly List<string> FirstErrors = new List<string>();
        public int Errors;
        public int Warnings;
        public bool Echo;

        public LogLevel MinLevel => LogLevel.Warning;

        public void Log(LogLevel level, string message)
        {
            if (level == LogLevel.Error)
            {
                Errors++;
                if (FirstErrors.Count < 5) FirstErrors.Add(Trim(message));
            }
            else if (level == LogLevel.Warning) Warnings++;
            if (Echo) Console.WriteLine("    [" + level + "] " + Trim(message));
        }

        private static string Trim(string m)
        {
            if (m == null) return "";
            int nl = m.IndexOf('\n');
            if (nl > 0) m = m.Substring(0, nl);
            return m.Length > 240 ? m.Substring(0, 240) + "..." : m;
        }
    }
}
