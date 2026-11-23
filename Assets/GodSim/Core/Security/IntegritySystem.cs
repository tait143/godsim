using System;

namespace GodSim.Core.Security
{
    /// <summary>
    /// Periodic runtime integrity monitor. Every <see cref="TickInterval"/> ticks (once per in-game year), and once at
    /// <see cref="Initialize"/> (so a tampered or corrupted save is caught on load), it runs
    /// <see cref="IntegrityChecker.Check(WorldState, GameDatabase, int, int)"/>; when issues are found it logs a bounded
    /// number of them through <c>ctx.Log.Warn</c>, applies <see cref="IntegrityChecker.Repair(WorldState, GameDatabase)"/>
    /// and accumulates the counts in <c>state.Memory["sec.integrityIssues"]</c> and <c>state.Memory["sec.integrityRepairs"]</c>.
    /// Deterministic (repairs depend only on state) and allocation-free on healthy worlds apart from the scan's own
    /// bookkeeping. Never throws out of Tick unless <see cref="SimConfig.StrictMode"/> is set.
    /// Recommended placement: last in the system order, after every module has written its state for the tick.
    /// </summary>
    public sealed class IntegritySystem : ISimSystem
    {
        /// <summary>World Memory key holding the cumulative number of integrity issues detected.</summary>
        public const string IssuesKey = "sec.integrityIssues";
        /// <summary>World Memory key holding the cumulative number of repairs applied.</summary>
        public const string RepairsKey = "sec.integrityRepairs";
        /// <summary>Maximum issues collected per run.</summary>
        public const int MaxIssuesPerRun = 100;
        /// <summary>Maximum issue lines written to the log per run (the rest are summarized).</summary>
        public const int MaxLoggedIssuesPerRun = 10;

        /// <summary>Creates the system (no configuration needed; limits come from the SimContext).</summary>
        public IntegritySystem() { }

        /// <inheritdoc />
        public string Name => "Security.Integrity";
        /// <inheritdoc />
        public int TickInterval => 96;
        /// <summary>Issues found by the most recent run.</summary>
        public int LastIssueCount { get; private set; }
        /// <summary>Repairs applied by the most recent run.</summary>
        public int LastRepairCount { get; private set; }

        /// <inheritdoc />
        public void Initialize(SimContext ctx) => RunOnce(ctx);

        /// <inheritdoc />
        public void Tick(SimContext ctx) => RunOnce(ctx);

        private void RunOnce(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            try
            {
                var state = ctx.State;
                int maxPopulation = ctx.Config != null ? ctx.Config.MaxPopulation : IntegrityChecker.AbsoluteMaxPopulation;
                var issues = IntegrityChecker.Check(state, ctx.Db, MaxIssuesPerRun, maxPopulation);
                LastIssueCount = issues.Count;
                LastRepairCount = 0;
                if (issues.Count > 0)
                {
                    int logged = Math.Min(issues.Count, MaxLoggedIssuesPerRun);
                    for (int i = 0; i < logged; i++) ctx.Log.Warn("[Security] Integrity issue at tick " + state.Tick + ": " + issues[i]);
                    if (issues.Count > logged) ctx.Log.Warn("[Security] ... and " + (issues.Count - logged) + " more integrity issue(s).");
                    LastRepairCount = IntegrityChecker.Repair(state, ctx.Db);
                    ctx.Log.Warn("[Security] Integrity repair applied " + LastRepairCount + " fix(es).");
                }
                Accumulate(state, IssuesKey, issues.Count);
                Accumulate(state, RepairsKey, LastRepairCount);
            }
            catch (Exception ex)
            {
                ctx.Log.Error("[Security] Integrity monitor failed: " + ex.GetType().Name + ": " + ex.Message);
                if (ctx.Config != null && ctx.Config.StrictMode) throw;
            }
        }

        private static void Accumulate(WorldState state, string key, int amount)
        {
            float current = state.Memory.TryGetValue(key, out float v) && !float.IsNaN(v) && !float.IsInfinity(v) ? v : 0f;
            float next = current + amount;
            if (next > 1e9f) next = 1e9f; // keep the counter finite and exact-ish forever
            state.Memory[key] = next;
        }
    }
}
