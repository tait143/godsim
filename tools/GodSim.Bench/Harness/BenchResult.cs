using System.Collections.Generic;

namespace GodSim.Bench.Harness
{
    /// <summary>One measured benchmark. All times are nanoseconds per operation (op = a call, a path, a tick, ...).</summary>
    public sealed class BenchResult
    {
        public string Category = "";
        public string Name = "";
        /// <summary>What one operation is ("op", "tick", "path", "world", ...).</summary>
        public string OpName = "op";

        public int Iterations;
        public long OpsPerIteration;
        public long TotalOps;

        public double MedianNs;
        public double P95Ns;
        public double MeanNs;
        public double MinNs;
        public double MaxNs;

        /// <summary>Managed bytes allocated on the benchmark thread per op (GC.GetAllocatedBytesForCurrentThread).</summary>
        public double AllocBytesPerOp;
        public long AllocBytesTotal;
        public int Gen0;
        public int Gen1;
        public int Gen2;

        /// <summary>Extra numbers (entity counts, path lengths, expansions...). Insertion-ordered.</summary>
        public readonly List<KeyValuePair<string, double>> Metrics = new List<KeyValuePair<string, double>>();
        /// <summary>Per-system breakdown for full-simulation benchmarks.</summary>
        public readonly List<SystemRow> Systems = new List<SystemRow>();

        public string Note;
        public string Error;
        public bool Skipped;

        public bool HasTiming => Iterations > 0 && Error == null && !Skipped;
        public double MedianMs => MedianNs / 1e6;
        public double MeanMs => MeanNs / 1e6;
        public double P95Ms => P95Ns / 1e6;

        public string FullName => Category + "/" + Name;

        public BenchResult Metric(string key, double value) { Metrics.Add(new KeyValuePair<string, double>(key, value)); return this; }

        public bool TryGetMetric(string key, out double value)
        {
            for (int i = 0; i < Metrics.Count; i++) if (Metrics[i].Key == key) { value = Metrics[i].Value; return true; }
            value = 0;
            return false;
        }

        public static BenchResult Fail(string category, string name, string error) => new BenchResult { Category = category, Name = name, Error = error };
        public static BenchResult Skip(string category, string name, string note) => new BenchResult { Category = category, Name = name, Skipped = true, Note = note };
    }

    /// <summary>Per-system timing gathered by sampling SimulationEngine.Profiles after every Step().</summary>
    public sealed class SystemRow
    {
        public string Name = "";
        public int TickInterval;
        public long Runs;
        public double TotalMs;
        /// <summary>Amortized cost per simulated tick (TotalMs / ticks measured).</summary>
        public double MsPerTick;
        /// <summary>Average cost of one run (TotalMs / Runs).</summary>
        public double MsPerRun;
        public double MaxRunMs;
        /// <summary>The engine's own SystemProfile.AvgMs (EMA per run, alpha 0.05) at the end of the window.</summary>
        public double EngineAvgMs;
        public int Faults;
        public bool Disabled;
        public string LastError;
    }

    /// <summary>A CI budget: measured value must be &lt;= Limit.</summary>
    public sealed class BudgetCheck
    {
        public string Name = "";
        public string Benchmark = "";
        public string Unit = "";
        public double Limit;
        public double Actual = double.NaN;
        /// <summary>PASS, FAIL, SKIPPED (benchmark filtered out / not applicable) or ERROR (benchmark crashed).</summary>
        public string Status = "SKIPPED";
        public string Detail = "";
        public bool Failed => Status == "FAIL" || Status == "ERROR";
    }
}
