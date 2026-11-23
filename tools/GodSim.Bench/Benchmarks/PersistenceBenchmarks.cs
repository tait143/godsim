using System;
using System.Collections.Generic;
using System.Reflection;
using GodSim.Bench.Harness;
using GodSim.Core;
using GodSim.Core.Composition;

namespace GodSim.Bench.Benchmarks
{
    /// <summary>
    /// Save/load timing. Persistence is written in parallel, so it is bound by REFLECTION at runtime:
    /// if GodSim.Core.Persistence.SaveSerializer (Serialize(WorldState, SaveOptions) / Deserialize(byte[], GameDatabase, SaveOptions))
    /// is compiled into GodSim.Core the benchmark runs, otherwise it reports SKIPPED. This keeps the bench
    /// compiling whether or not the Persistence module exists. Also times StateHasher.Compute when present.
    /// </summary>
    public static class PersistenceBenchmarks
    {
        public const string Category = "persistence";

        public static IEnumerable<BenchCase> Cases()
        {
            yield return new BenchCase { Category = Category, Name = "save192", Description = "SaveSerializer Serialize/Deserialize of a 192x192 world (reflection-bound; skipped if absent)", Run = Save };
        }

        private static IEnumerable<BenchResult> Save(BenchOptions o)
        {
            var asm = typeof(WorldState).Assembly;
            var ser = asm.GetType("GodSim.Core.Persistence.SaveSerializer");
            if (ser == null)
            {
                yield return BenchResult.Skip(Category, "save192", "GodSim.Core.Persistence.SaveSerializer not found in " + asm.GetName().Name);
                yield break;
            }
            var optT = asm.GetType("GodSim.Core.Persistence.SaveOptions");
            object opts = optT != null ? Activator.CreateInstance(optT) : null;
            MethodInfo serialize = optT != null ? ser.GetMethod("Serialize", new[] { typeof(WorldState), optT }) : null;
            MethodInfo deserialize = optT != null ? ser.GetMethod("Deserialize", new[] { typeof(byte[]), typeof(GameDatabase), optT }) : null;
            if (serialize == null)
            {
                yield return BenchResult.Fail(Category, "save192", "SaveSerializer found but Serialize(WorldState, SaveOptions) signature is missing (contract drift)");
                yield break;
            }
            var hasherT = asm.GetType("GodSim.Core.Persistence.StateHasher");
            MethodInfo hash = hasherT?.GetMethod("Compute", new[] { typeof(WorldState) });

            var cfg = new SimConfig { Seed = o.Seed, MapWidth = 192, MapHeight = 192 }.Sanitize();
            var sim = GodSimulation.CreateNew(cfg, new CountingLogger());
            sim.StepMany(o.Q(5, 25) * SimTime.TicksPerYear);
            var state = sim.State;
            int iters = o.Q(3, 10);

            byte[] bytes = null;
            var rs = Timing.Measure(Category, "serialize192(year" + state.Year + ")", "save", 1, iters, 1,
                () => bytes = (byte[])Invoke(serialize, state, opts));
            SimSampler.AddWorldMetrics(rs, state);
            rs.Metric("save_bytes", bytes?.Length ?? 0).Metric("save_kb", (bytes?.Length ?? 0) / 1024.0);
            yield return rs;

            if (deserialize != null && bytes != null)
            {
                WorldState loaded = null;
                var rd = Timing.Measure(Category, "deserialize192(year" + state.Year + ")", "load", 1, iters, 1,
                    () => loaded = (WorldState)Invoke(deserialize, bytes, sim.Db, opts));
                if (hash != null && loaded != null)
                {
                    ulong h1 = (ulong)Invoke(hash, state), h2 = (ulong)Invoke(hash, loaded);
                    rd.Metric("roundtrip_hash_match", h1 == h2 ? 1 : 0);
                    if (h1 != h2) rd.Note = "ROUND-TRIP MISMATCH: StateHasher differs after load";
                }
                yield return rd;
            }

            // Signed save (HMAC-SHA256) when SaveOptions exposes a byte[] HmacKey field: cost of integrity protection.
            var keyField = optT.GetField("HmacKey");
            if (keyField != null && keyField.FieldType == typeof(byte[]))
            {
                object signedOpts = Activator.CreateInstance(optT);
                var key = new byte[32];
                for (int i = 0; i < key.Length; i++) key[i] = (byte)(i * 37 + 11);
                keyField.SetValue(signedOpts, key);
                byte[] signedBytes = null;
                var rsig = Timing.Measure(Category, "serialize192.signed(year" + state.Year + ")", "save", 1, iters, 1,
                    () => signedBytes = (byte[])Invoke(serialize, state, signedOpts));
                rsig.Metric("save_bytes", signedBytes?.Length ?? 0);
                yield return rsig;
            }

            if (hash != null)
                yield return Timing.Measure(Category, "statehasher192(year" + state.Year + ")", "hash", 1, iters * 2, 1,
                    () => Timing.Sink += (long)(ulong)Invoke(hash, state));
        }

        private static object Invoke(MethodInfo m, params object[] args)
        {
            try { return m.Invoke(null, args); }
            catch (TargetInvocationException ex) when (ex.InnerException != null)
            {
                System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex.InnerException).Throw();
                throw;
            }
        }
    }
}
