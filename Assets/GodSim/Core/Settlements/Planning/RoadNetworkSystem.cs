using System.Collections.Generic;

namespace GodSim.Core.Settlements
{
    /// <summary>
    /// Inter-settlement road network. Every 24 ticks, living settlements of tier Hamlet or above are each
    /// connected once to their nearest same-civilization settlement within 60 tiles (bounded A*, 20000
    /// expansions, roads avoid buildings). Success is remembered in both settlements' Memory
    /// (<c>plan.road.&lt;otherId&gt; = 1</c>). Roads are never pruned. One <see cref="TerrainChangedEvent"/>
    /// bounding box is published per run that added roads.
    /// <para>
    /// Fairness and cost control: settlements are visited round-robin from a cursor in world Memory, so a
    /// few hopeless links can never starve the rest. Only real searches use one of the
    /// <see cref="MaxConnectionsPerRun"/> slots. A failed pair is stamped on both sides
    /// (<c>plan.roadtry.&lt;id&gt;</c> tick, <c>plan.roadfail.&lt;id&gt;</c> count), so the two ends never repeat
    /// the same search. It is retried with exponential backoff (96, then 192 ticks), and after
    /// <see cref="MaxFailures"/> failures it is abandoned for good (<c>plan.road.&lt;id&gt; = -1</c>). The
    /// settlement then links to its next-nearest settlement instead.
    /// </para>
    /// </summary>
    public sealed class RoadNetworkSystem : ISimSystem
    {
        /// <summary>Maximum center-to-center distance of a connection.</summary>
        public const int MaxLinkDistance = 60;
        /// <summary>Connection searches per run (work cap).</summary>
        public const int MaxConnectionsPerRun = 2;
        /// <summary>Ticks before the first retry of a failed connection; doubles after each further failure.</summary>
        public const int RetryDelayTicks = 96;
        /// <summary>Failed searches after which a pair is abandoned permanently.</summary>
        public const int MaxFailures = 3;
        /// <summary>Memory value of <see cref="RoadKeyPrefix"/> marking a built link.</summary>
        public const float Connected = 1f;
        /// <summary>Memory value of <see cref="RoadKeyPrefix"/> marking an abandoned (unreachable) link.</summary>
        public const float GaveUp = -1f;
        /// <summary>Settlement memory key prefix for a link's state (1 = built, -1 = abandoned).</summary>
        public const string RoadKeyPrefix = "plan.road.";
        /// <summary>Settlement memory key prefix storing the tick of the last failed attempt.</summary>
        public const string RetryKeyPrefix = "plan.roadtry.";
        /// <summary>Settlement memory key prefix storing the number of failed attempts.</summary>
        public const string FailKeyPrefix = "plan.roadfail.";
        /// <summary>World memory key of the round-robin settlement cursor.</summary>
        public const string CursorMemoryKey = "plan.roadnet.cursor";

        private readonly PlannerRoads _roads = new PlannerRoads();
        private readonly Dictionary<int, string> _roadKeys = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _retryKeys = new Dictionary<int, string>();
        private readonly Dictionary<int, string> _failKeys = new Dictionary<int, string>();

        /// <inheritdoc />
        public string Name => "RoadNetwork";

        /// <inheritdoc />
        public int TickInterval => 24;

        /// <summary>Pathfinding searches started during the last run (at most <see cref="MaxConnectionsPerRun"/>).</summary>
        public int LastRunSearches { get; private set; }

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <summary>Memory key holding the link state to the given settlement (allocates; use for tooling/tests).</summary>
        public static string RoadMemoryKey(int otherSettlementId) => RoadKeyPrefix + otherSettlementId;

        /// <summary>Memory key holding the tick of the last failed link attempt (allocates; tooling/tests).</summary>
        public static string RetryMemoryKey(int otherSettlementId) => RetryKeyPrefix + otherSettlementId;

        /// <summary>Memory key holding the failed-attempt count of a link (allocates; tooling/tests).</summary>
        public static string FailMemoryKey(int otherSettlementId) => FailKeyPrefix + otherSettlementId;

        /// <summary>Ticks to wait after the n-th consecutive failure (n &gt;= 1): 96, 192, 384, ...</summary>
        public static int RetryDelayAfter(int failures) => RetryDelayTicks << MathUtil.Clamp(failures - 1, 0, 10);

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            LastRunSearches = 0;
            if (ctx == null || ctx.State == null || ctx.State.Map == null) return;
            var state = ctx.State;
            var map = state.Map;
            var all = state.Settlements.Items;
            int n = all.Count;
            if (n == 0) return;
            int start = (int)MathUtil.Clamp(MathUtil.Sanitize(state.Memory.TryGetValue(CursorMemoryKey, out float c) ? c : 0f), 0f, 1e9f);
            start = ((start % n) + n) % n;
            int next = start;
            var dirty = default(TerrainDirtyRect);
            for (int k = 0; k < n && LastRunSearches < MaxConnectionsPerRun; k++)
            {
                int idx = (start + k) % n;
                var s = all[idx];
                if (s == null || !s.IsAlive || s.Tier < SettlementTier.Hamlet) continue;
                var civ = state.Civilizations.Get(s.CivilizationId);
                if (civ == null || !civ.IsAlive) continue;
                var other = NearestCandidate(all, s);
                if (other == null) continue;
                string key = Key(_roadKeys, RoadKeyPrefix, other.Id);
                string back = Key(_roadKeys, RoadKeyPrefix, s.Id);
                if (s.Mem(key) > 0f || other.Mem(back) > 0f) continue;
                string retry = Key(_retryKeys, RetryKeyPrefix, other.Id);
                string retryBack = Key(_retryKeys, RetryKeyPrefix, s.Id);
                string fail = Key(_failKeys, FailKeyPrefix, other.Id);
                string failBack = Key(_failKeys, FailKeyPrefix, s.Id);
                int failures = (int)MathUtil.Clamp(System.Math.Max(MathUtil.Sanitize(s.Mem(fail)), MathUtil.Sanitize(other.Mem(failBack))), 0f, MaxFailures);
                if (failures > 0 && state.Tick - LastFailureTick(s, other, retry, retryBack) < RetryDelayAfter(failures)) continue;

                LastRunSearches++;
                next = (idx + 1) % n;
                _roads.EnsurePlaza(state, s, ref dirty);
                _roads.EnsurePlaza(state, other, ref dirty);
                if (_roads.ConnectSettlements(map, s, other, ref dirty))
                {
                    s.Memory[key] = Connected;
                    other.Memory[back] = Connected;
                    ForgetFailures(s, other, retry, retryBack, fail, failBack);
                    s.LogAi("Built road to " + other.Name);
                    continue;
                }
                failures++;
                if (failures >= MaxFailures)
                {
                    s.Memory[key] = GaveUp;
                    other.Memory[back] = GaveUp;
                    ForgetFailures(s, other, retry, retryBack, fail, failBack);
                    s.LogAi("Gave up road to " + other.Name + " after " + failures + " attempts");
                }
                else
                {
                    s.Memory[retry] = state.Tick;
                    other.Memory[retryBack] = state.Tick;
                    s.Memory[fail] = failures;
                    other.Memory[failBack] = failures;
                }
            }
            state.Memory[CursorMemoryKey] = next;
            PlannerRoads.Commit(ctx, ref dirty);
        }

        private static long LastFailureTick(Settlement s, Settlement other, string retry, string retryBack)
        {
            long last = long.MinValue / 2;
            if (s.Memory.TryGetValue(retry, out float a)) last = System.Math.Max(last, (long)MathUtil.Clamp(MathUtil.Sanitize(a), -1e12f, 1e12f));
            if (other.Memory.TryGetValue(retryBack, out float b)) last = System.Math.Max(last, (long)MathUtil.Clamp(MathUtil.Sanitize(b), -1e12f, 1e12f));
            return last;
        }

        private static void ForgetFailures(Settlement s, Settlement other, string retry, string retryBack, string fail, string failBack)
        {
            s.Memory.Remove(retry);
            s.Memory.Remove(fail);
            other.Memory.Remove(retryBack);
            other.Memory.Remove(failBack);
        }

        /// <summary>Nearest living same-civ settlement within <see cref="MaxLinkDistance"/> whose link was not abandoned.</summary>
        private Settlement NearestCandidate(IReadOnlyList<Settlement> all, Settlement s)
        {
            Settlement best = null;
            int bestD = int.MaxValue, maxD = MaxLinkDistance * MaxLinkDistance;
            for (int j = 0; j < all.Count; j++)
            {
                var o = all[j];
                if (o == null || o == s || !o.IsAlive || o.CivilizationId != s.CivilizationId) continue;
                int d = o.Center.DistanceSqTo(s.Center);
                if (d > maxD) continue;
                if (d > bestD || (d == bestD && best != null && o.Id > best.Id)) continue;
                if (s.Mem(Key(_roadKeys, RoadKeyPrefix, o.Id)) < 0f || o.Mem(Key(_roadKeys, RoadKeyPrefix, s.Id)) < 0f) continue;
                bestD = d;
                best = o;
            }
            return best;
        }

        private static string Key(Dictionary<int, string> cache, string prefix, int id)
        {
            if (!cache.TryGetValue(id, out string k)) { k = prefix + id; cache[id] = k; }
            return k;
        }
    }
}
