using System;
using System.Collections.Generic;
using System.Diagnostics;
using GodSim.Bench.Harness;
using GodSim.Core;
using GodSim.Core.Composition;

namespace GodSim.Bench.Benchmarks
{
    /// <summary>
    /// Full-simulation benchmarks through the composition root (GodSimulation):
    ///  - steady state: one 192x192 world advanced to several years, measured for N ticks at each checkpoint;
    ///  - stress: people injected through WorldOps on top of a generated world (5k = budget world, 20k = headroom).
    /// </summary>
    public static class SimBenchmarks
    {
        public const string Category = "sim";
        public const string StressCategory = "stress";

        public static IEnumerable<BenchCase> Cases()
        {
            yield return new BenchCase { Category = Category, Name = "steady192", Description = "CreateNew 192x192, advance to years 25/100/200 (quick: 5/25), ms/tick + per-system", Run = SteadyState };
            yield return new BenchCase { Category = StressCategory, Name = "people5000", Description = "5,000 people (budget world): ms/tick per system", Run = o => new[] { Stress(o, 5000) } };
            yield return new BenchCase { Category = StressCategory, Name = "people20000", Description = "20,000 people synthetic stress (full mode only unless filtered)", Run = o => o.Quick && string.IsNullOrEmpty(o.Filter) ? new[] { BenchResult.Skip(StressCategory, "people20000", "skipped in --quick (use --filter people20000)") } : new[] { Stress(o, 20000) } };
        }

        // =========================================================================== steady state
        private static IEnumerable<BenchResult> SteadyState(BenchOptions o)
        {
            int[] years = o.Quick ? new[] { 5, 25 } : new[] { 25, 100, 200 };
            int ticks = o.Q(150, 500);
            var log = new CountingLogger { Echo = o.Verbose };
            var cfg = new SimConfig { Seed = o.Seed, MapWidth = 192, MapHeight = 192 }.Sanitize();

            long c0 = Stopwatch.GetTimestamp();
            long a0 = GC.GetAllocatedBytesForCurrentThread();
            var sim = GodSimulation.CreateNew(cfg, log);
            double createMs = Timing.ElapsedMs(c0);
            long createAlloc = GC.GetAllocatedBytesForCurrentThread() - a0;
            var results = new List<BenchResult>();

            foreach (int y in years)
            {
                long target = (long)y * SimTime.TicksPerYear;
                long advanceTicks = Math.Max(0, target - sim.State.Tick);
                long t0 = Stopwatch.GetTimestamp();
                while (sim.State.Tick < target) sim.StepMany((int)Math.Min(SimTime.TicksPerYear, target - sim.State.Tick));
                double advMs = Timing.ElapsedMs(t0);

                var r = new SimSampler(sim.Engine).Run(Category, "steady192.year" + y, ticks, sim.Step);
                SimSampler.AddWorldMetrics(r, sim.State);
                r.Metric("advance_ticks", advanceTicks)
                 .Metric("advance_ms_per_tick", advanceTicks > 0 ? advMs / advanceTicks : 0)
                 .Metric("create_ms", createMs)
                 .Metric("create_alloc_mb", createAlloc / 1048576.0);
                long h0 = Stopwatch.GetTimestamp();
                ulong hash = sim.ComputeHash();
                r.Metric("compute_hash_ms", Timing.ElapsedMs(h0));
                r.Note = "hash=" + hash.ToString("X16");
                AddLogMetrics(r, log);
                results.Add(r);
            }
            return results;
        }

        // =========================================================================== stress
        private static BenchResult Stress(BenchOptions o, int targetPeople)
        {
            string name = "people" + targetPeople;
            var log = new CountingLogger { Echo = o.Verbose };
            var cfg = new SimConfig
            {
                Seed = o.Seed ^ (ulong)targetPeople,
                MapWidth = 192, MapHeight = 192,
                MaxPopulation = Math.Max(60000, targetPeople * 3),
                MaxBuildingsPerSettlement = 5000,
            }.Sanitize();
            var sim = GodSimulation.CreateNew(cfg, log);
            // Let towns form organically first (town centers, first houses, AI state).
            sim.StepMany(o.Q(96, 2 * SimTime.TicksPerYear));

            int injected = Inject(sim, targetPeople, o.Seed);
            sim.StepMany(o.Q(24, 48)); // settle: housing/jobs reassignment after the influx
            int popStart = sim.State.LivingPopulation;

            int ticks = o.Q(150, 500);
            var r = new SimSampler(sim.Engine).Run(StressCategory, name, ticks, sim.Step);
            SimSampler.AddWorldMetrics(r, sim.State);
            r.Metric("target_people", targetPeople)
             .Metric("injected", injected)
             .Metric("people_at_window_start", popStart)
             .Metric("ms_per_1000_people", popStart > 0 ? r.MeanMs / (popStart / 1000.0) : 0);
            AddLogMetrics(r, log);
            double popEnd = sim.State.LivingPopulation;
            if (popStart > 0 && Math.Abs(popEnd - popStart) / popStart > 0.25)
                r.Note = (r.Note == null ? "" : r.Note + "; ") + "population drifted " + popStart + " -> " + popEnd + " during the window";
            return r;
        }

        /// <summary>
        /// Adds people (round-robin over living settlements, matching race, ages 0..elder) through WorldOps until
        /// LivingPopulation reaches the target, and tops up food so the influx is not simply starved away.
        /// </summary>
        public static int Inject(GodSimulation sim, int target, ulong seed)
        {
            var state = sim.State;
            var ops = sim.Engine.Context.Ops;
            var db = sim.Db;
            var rng = new Rng(seed ^ 0x5EED5EEDUL);
            var towns = new List<Settlement>();
            for (int i = 0; i < state.Settlements.Count; i++) if (state.Settlements[i].IsAlive) towns.Add(state.Settlements[i]);
            if (towns.Count == 0)
            {
                var land = FindLand(state.Map);
                if (!land.IsValid) return 0;
                RaceId race = RaceId.Human;
                for (int i = 0; i < db.Races.Count; i++) if (db.Races[i] != null) { race = db.Races[i].Id; break; }
                var civ = ops.CreateCivilization(race, "Stress");
                towns.Add(ops.CreateSettlement(civ, land, "Stressville"));
            }

            int added = 0, k = 0, alive = state.LivingPopulation;
            while (alive + added < target)
            {
                var s = towns[k++ % towns.Count];
                var def = db.Race(s.Race);
                float age = rng.Range(0f, Math.Max(1f, def.ElderAge));
                ops.CreatePerson(s, s.Race, rng.Chance(0.5f) ? Sex.Male : Sex.Female, age);
                added++;
            }
            for (int i = 0; i < towns.Count; i++)
            {
                var sp = towns[i].Stockpile;
                sp.Capacity = Math.Max(sp.Capacity, 20000f);
                sp.SetRaw(ResourceType.Food, sp.Capacity);
                sp.SetRaw(ResourceType.Wood, Math.Max(sp.Get(ResourceType.Wood), 2000f));
                sp.SetRaw(ResourceType.Stone, Math.Max(sp.Get(ResourceType.Stone), 1000f));
            }
            ops.FlushRemovals();
            return added;
        }

        private static Vec2i FindLand(WorldMap map)
        {
            int cx = map.Width / 2, cy = map.Height / 2;
            for (int r = 0; r < Math.Max(map.Width, map.Height); r++)
                for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                        if (map.InBounds(x, y) && map.Biomes[map.Index(x, y)].IsBuildable()) return new Vec2i(x, y);
            return Vec2i.Invalid;
        }

        private static void AddLogMetrics(BenchResult r, CountingLogger log)
        {
            r.Metric("log_errors", log.Errors).Metric("log_warnings", log.Warnings);
            if (log.FirstErrors.Count > 0)
                r.Note = (r.Note == null ? "" : r.Note + "; ") + "first error: " + log.FirstErrors[0];
        }
    }
}
