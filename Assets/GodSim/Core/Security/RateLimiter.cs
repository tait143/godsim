using System;
using System.Collections.Generic;

namespace GodSim.Core.Security
{
    /// <summary>
    /// Deterministic token-bucket rate limiter for god commands.
    /// <para>
    /// Two limits apply to every acquisition:
    /// <list type="bullet">
    /// <item><description>a <b>global per-tick cap</b> shared by all sources (default <c>MaxCommandsPerTick</c>), and</description></item>
    /// <item><description>a <b>per-source token bucket</b> (default capacity <c>2 * MaxCommandsPerTick</c>, refilled by
    /// <c>MaxCommandsPerTick</c> tokens per simulation tick).</description></item>
    /// </list>
    /// Time is measured exclusively in simulation ticks supplied by the caller (never the wall clock), so the limiter
    /// behaves identically in live play, headless runs and replays.
    /// </para>
    /// <para>
    /// Memory is bounded: at most <see cref="SourceLimit"/> (&lt;= <see cref="MaxTrackedSources"/>) sources are tracked and
    /// source keys are truncated to <see cref="MaxKeyLength"/> characters. When a new source arrives and the map is full,
    /// the least-recently-used source is evicted. Recency is a monotonic counter (not a clock and not dictionary order),
    /// so eviction is fully deterministic. Evicted bucket objects are recycled, so a flood of distinct sources does not
    /// allocate per command.
    /// </para>
    /// <para>
    /// With the default parameters the global cap is the binding limit for a single source; the per-source bucket becomes
    /// binding when it is configured with a refill below the global cap (e.g. several network peers sharing one world).
    /// Not thread-safe (Core is single-threaded by contract).
    /// </para>
    /// </summary>
    public sealed class RateLimiter
    {
        /// <summary>Hard upper bound on the number of distinct sources tracked at once.</summary>
        public const int MaxTrackedSources = 64;
        /// <summary>Source keys longer than this are truncated before use (bounds memory held by keys).</summary>
        public const int MaxKeyLength = 64;
        /// <summary>Hard upper bound for any rate/capacity parameter.</summary>
        public const int HardMaxRate = 4096;

        private sealed class Bucket
        {
            public string Key;
            public int Tokens;
            public long LastRefillTick;
            public long LastUsedStamp;
        }

        private readonly Dictionary<string, Bucket> _buckets = new Dictionary<string, Bucket>(StringComparer.Ordinal);
        private long _stamp;
        private long _globalTick = long.MinValue;
        private int _globalCount;

        /// <summary>
        /// Creates a limiter with the standard policy: bucket capacity = 2 * <paramref name="maxCommandsPerTick"/>,
        /// refill = <paramref name="maxCommandsPerTick"/> per tick, global cap = <paramref name="maxCommandsPerTick"/> per tick,
        /// and <see cref="MaxTrackedSources"/> tracked sources. The rate is clamped to [1, HardMaxRate / 2].
        /// </summary>
        public RateLimiter(int maxCommandsPerTick)
            : this(2 * ClampRate(maxCommandsPerTick, HardMaxRate / 2), ClampRate(maxCommandsPerTick, HardMaxRate / 2),
                   ClampRate(maxCommandsPerTick, HardMaxRate / 2), MaxTrackedSources)
        {
        }

        /// <summary>
        /// Creates a limiter with explicit parameters (all clamped to safe ranges).
        /// </summary>
        /// <param name="bucketCapacity">Maximum tokens a single source can bank (burst size), [1, HardMaxRate].</param>
        /// <param name="refillPerTick">Tokens added to every bucket per elapsed tick, [1, HardMaxRate].</param>
        /// <param name="globalPerTick">Maximum acquisitions per tick across all sources, [1, HardMaxRate].</param>
        /// <param name="maxTrackedSources">Maximum distinct sources tracked at once, [1, MaxTrackedSources].</param>
        public RateLimiter(int bucketCapacity, int refillPerTick, int globalPerTick, int maxTrackedSources)
        {
            BucketCapacity = ClampRate(bucketCapacity, HardMaxRate);
            RefillPerTick = ClampRate(refillPerTick, HardMaxRate);
            GlobalPerTick = ClampRate(globalPerTick, HardMaxRate);
            SourceLimit = MathUtil.Clamp(maxTrackedSources, 1, MaxTrackedSources);
        }

        /// <summary>Maximum tokens a source can hold.</summary>
        public int BucketCapacity { get; }
        /// <summary>Tokens added to each bucket per elapsed simulation tick.</summary>
        public int RefillPerTick { get; }
        /// <summary>Maximum acquisitions per tick across all sources.</summary>
        public int GlobalPerTick { get; }
        /// <summary>Maximum number of sources tracked at once (LRU eviction beyond this).</summary>
        public int SourceLimit { get; }
        /// <summary>Number of sources currently tracked (always &lt;= <see cref="SourceLimit"/>).</summary>
        public int TrackedSourceCount => _buckets.Count;
        /// <summary>Total successful acquisitions since creation or <see cref="Reset"/>.</summary>
        public long TotalAccepted { get; private set; }
        /// <summary>Total refused acquisitions since creation or <see cref="Reset"/>.</summary>
        public long TotalRejected { get; private set; }
        /// <summary>Total sources evicted to respect <see cref="SourceLimit"/>.</summary>
        public long TotalEvictions { get; private set; }

        /// <summary>Acquisitions already granted during <paramref name="tick"/> (0 for any other tick).</summary>
        public int AcceptedInTick(long tick) => tick == _globalTick ? _globalCount : 0;

        /// <summary>
        /// Attempts to take one token for <paramref name="source"/> at simulation tick <paramref name="tick"/>.
        /// Returns false (with a user-facing reason) when the global per-tick cap or the source's bucket is exhausted.
        /// A refusal caused by the global cap does not create or touch any per-source state.
        /// </summary>
        public bool TryAcquire(string source, long tick, out string reason)
        {
            RollGlobal(tick);
            if (_globalCount >= GlobalPerTick)
            {
                TotalRejected++;
                reason = "Too many divine commands this day (limit " + GlobalPerTick + " per day). Wait for the next day.";
                return false;
            }
            Bucket b = GetOrCreate(NormalizeKey(source), tick);
            Refill(b, tick);
            b.LastUsedStamp = ++_stamp;
            if (b.Tokens < 1)
            {
                TotalRejected++;
                reason = "Commands from this source are arriving too quickly. Slow down and try again shortly.";
                return false;
            }
            b.Tokens--;
            _globalCount++;
            TotalAccepted++;
            reason = null;
            return true;
        }

        /// <summary>Convenience overload of <see cref="TryAcquire(string, long, out string)"/> without a reason.</summary>
        public bool TryAcquire(string source, long tick) => TryAcquire(source, tick, out _);

        /// <summary>True if an acquisition would currently succeed. Pure: changes no state and tracks no source.</summary>
        public bool CanAcquire(string source, long tick)
        {
            if (AcceptedInTick(tick) >= GlobalPerTick) return false;
            return AvailableTokens(source, tick) >= 1;
        }

        /// <summary>
        /// Tokens the source would have at <paramref name="tick"/> (after refill), ignoring the global cap.
        /// Untracked sources report a full bucket. Pure: changes no state.
        /// </summary>
        public int AvailableTokens(string source, long tick)
        {
            if (!_buckets.TryGetValue(NormalizeKey(source), out Bucket b)) return BucketCapacity;
            return ComputeRefilled(b.Tokens, b.LastRefillTick, tick);
        }

        /// <summary>True if the source currently has a tracked bucket.</summary>
        public bool IsTracked(string source) => _buckets.ContainsKey(NormalizeKey(source));

        /// <summary>Forgets every source and the per-tick counter (e.g. after loading a different world).</summary>
        public void Reset()
        {
            _buckets.Clear();
            _stamp = 0;
            _globalTick = long.MinValue;
            _globalCount = 0;
            TotalAccepted = 0;
            TotalRejected = 0;
            TotalEvictions = 0;
        }

        // ------------------------------------------------------------------ internals

        private static int ClampRate(int v, int max) => MathUtil.Clamp(v, 1, max);

        private static string NormalizeKey(string source)
        {
            if (source == null) return string.Empty;
            return source.Length > MaxKeyLength ? source.Substring(0, MaxKeyLength) : source;
        }

        private void RollGlobal(long tick)
        {
            if (tick == _globalTick) return;
            _globalTick = tick;
            _globalCount = 0;
        }

        private Bucket GetOrCreate(string key, long tick)
        {
            if (_buckets.TryGetValue(key, out Bucket b)) return b;
            b = _buckets.Count >= SourceLimit ? EvictLeastRecentlyUsed() : new Bucket();
            b.Key = key;
            b.Tokens = BucketCapacity;
            b.LastRefillTick = tick;
            b.LastUsedStamp = 0;
            _buckets.Add(key, b);
            return b;
        }

        /// <summary>Removes and returns the bucket with the smallest use stamp. Stamps are unique, so the choice
        /// does not depend on dictionary enumeration order (deterministic).</summary>
        private Bucket EvictLeastRecentlyUsed()
        {
            Bucket victim = null;
            foreach (var kv in _buckets)
                if (victim == null || kv.Value.LastUsedStamp < victim.LastUsedStamp) victim = kv.Value;
            _buckets.Remove(victim.Key);
            TotalEvictions++;
            return victim;
        }

        private void Refill(Bucket b, long tick)
        {
            b.Tokens = ComputeRefilled(b.Tokens, b.LastRefillTick, tick);
            // Time moving backwards (world reloaded, tampered tick) re-anchors without granting tokens.
            b.LastRefillTick = tick;
        }

        private int ComputeRefilled(int tokens, long lastTick, long tick)
        {
            if (tick <= lastTick) return tokens;
            // True difference is in (0, 2^64): unchecked ulong subtraction is exact and cannot overflow.
            ulong elapsed = unchecked((ulong)tick - (ulong)lastTick);
            if (elapsed >= (ulong)BucketCapacity) return BucketCapacity; // RefillPerTick >= 1
            long refilled = tokens + (long)elapsed * RefillPerTick;      // elapsed < 4096, rate <= 4096: no overflow
            return refilled >= BucketCapacity ? BucketCapacity : (int)refilled;
        }
    }
}
