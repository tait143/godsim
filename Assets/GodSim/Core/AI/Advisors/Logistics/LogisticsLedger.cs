using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Construction ledger of one settlement, rebuilt in place (no allocations) each logistics run: the material
    /// demand of placed-but-unpaid sites and of queued build orders, which sites are blocked by which resource,
    /// how long blocked sites have waited (toward abandonment), paid sites and their remaining work, building counts
    /// (complete / pending) and free job slots per type, and the current builder headcount.
    /// </summary>
    public sealed class LogisticsLedger
    {
        /// <summary>Material demand of placed sites that have not paid their cost yet, per resource.</summary>
        public readonly float[] SiteDemand = new float[Counts.Resources];
        /// <summary>Material demand of queued (not yet placed) build orders, per resource.</summary>
        public readonly float[] QueueDemand = new float[Counts.Resources];
        /// <summary>Unpaid sites whose cost of this resource exceeds the current stock.</summary>
        public readonly int[] BlockedSites = new int[Counts.Resources];
        /// <summary>Largest single cost entry of this resource among unpaid sites and queued orders.</summary>
        public readonly float[] MaxSingle = new float[Counts.Resources];
        /// <summary>Building type owning <see cref="MaxSingle"/> (-1 none).</summary>
        public readonly int[] MaxSingleType = new int[Counts.Resources];
        /// <summary>Longest wait (fraction of the abandon limit) of a site blocked by this resource.</summary>
        public readonly float[] MaxWaitFrac = new float[Counts.Resources];
        /// <summary>Complete buildings per type.</summary>
        public readonly int[] CompleteCount = new int[Counts.Buildings];
        /// <summary>Incomplete sites plus queued orders per type (production already on its way).</summary>
        public readonly int[] PendingCount = new int[Counts.Buildings];
        /// <summary>Unfilled job slots in complete buildings per type.</summary>
        public readonly int[] FreeSlots = new int[Counts.Buildings];

        public int PaidSites;
        public int UnpaidSites;
        /// <summary>Unpaid sites the stockpile cannot pay right now.</summary>
        public int UnaffordableSites;
        public int QueuedOrders;
        public int Builders;
        public int Adults;
        /// <summary>Remaining builder-ticks of labor on paid sites.</summary>
        public float PaidWorkRemaining;
        /// <summary>Longest wait (fraction of the abandon limit) of any unaffordable site.</summary>
        public float MaxWaitFracAll;

        private readonly Dictionary<int, string> _waitKeys = new Dictionary<int, string>();

        public void Clear()
        {
            Array.Clear(SiteDemand, 0, SiteDemand.Length);
            Array.Clear(QueueDemand, 0, QueueDemand.Length);
            Array.Clear(BlockedSites, 0, BlockedSites.Length);
            Array.Clear(MaxSingle, 0, MaxSingle.Length);
            Array.Clear(MaxWaitFrac, 0, MaxWaitFrac.Length);
            Array.Clear(CompleteCount, 0, CompleteCount.Length);
            Array.Clear(PendingCount, 0, PendingCount.Length);
            Array.Clear(FreeSlots, 0, FreeSlots.Length);
            for (int i = 0; i < MaxSingleType.Length; i++) MaxSingleType[i] = -1;
            PaidSites = UnpaidSites = UnaffordableSites = QueuedOrders = Builders = Adults = 0;
            PaidWorkRemaining = 0f;
            MaxWaitFracAll = 0f;
        }

        /// <summary>Rebuilds the ledger for a settlement. Never throws on missing entities or definitions.</summary>
        public void Gather(WorldState state, GameDatabase db, Settlement s)
        {
            Clear();
            if (state == null || db == null || s == null) return;
            var stock = s.Stockpile;

            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = state.Buildings.Get(ids[k]);
                if (b == null || b.IsDestroyed) continue;
                if (!db.TryGetBuilding(b.Type, out var def)) continue;
                int t = (int)b.Type;
                if (b.IsComplete)
                {
                    CompleteCount[t]++;
                    if (def.HasJobs) FreeSlots[t] += Math.Max(0, def.JobSlots - b.Workers.Count);
                    continue;
                }
                PendingCount[t]++;
                if (b.CostPaid)
                {
                    PaidSites++;
                    PaidWorkRemaining += Math.Max(0f, MathUtil.Sanitize(def.BuildWork)) * (1f - MathUtil.Clamp01(MathUtil.Sanitize(b.ConstructionProgress)));
                    continue;
                }
                UnpaidSites++;
                float wait = MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(WaitKey(b.Id))) / LogisticsKeys.AbandonTicks);
                bool affordable = true;
                var cost = def.Cost;
                if (cost != null)
                    for (int c = 0; c < cost.Length; c++)
                    {
                        float a = LogisticsCatalog.Amount(cost[c]);
                        if (a <= 0f) continue;
                        int r = (int)cost[c].Type;
                        SiteDemand[r] += a;
                        Track(r, a, t);
                        if (!stock.Has(cost[c].Type, a))
                        {
                            affordable = false;
                            BlockedSites[r]++;
                            if (wait > MaxWaitFrac[r]) MaxWaitFrac[r] = wait;
                        }
                    }
                if (!affordable)
                {
                    UnaffordableSites++;
                    if (wait > MaxWaitFracAll) MaxWaitFracAll = wait;
                }
            }

            var queue = s.BuildQueue;
            for (int q = 0; q < queue.Count; q++)
            {
                var o = queue[q];
                if (o == null || !db.TryGetBuilding(o.Type, out var def)) continue;
                int t = (int)o.Type;
                QueuedOrders++;
                PendingCount[t]++;
                var cost = def.Cost;
                if (cost == null) continue;
                for (int c = 0; c < cost.Length; c++)
                {
                    float a = LogisticsCatalog.Amount(cost[c]);
                    if (a <= 0f) continue;
                    int r = (int)cost[c].Type;
                    QueueDemand[r] += a;
                    Track(r, a, t);
                }
            }

            var residents = s.ResidentIds;
            for (int k = 0; k < residents.Count; k++)
            {
                var p = state.People.Get(residents[k]);
                if (p == null || !p.IsAlive || !p.IsAdult) continue;
                Adults++;
                if (p.Job == JobType.Builder) Builders++;
            }
        }

        private void Track(int r, float amount, int type)
        {
            if (amount > MaxSingle[r]) { MaxSingle[r] = amount; MaxSingleType[r] = type; }
        }

        private string WaitKey(int buildingId)
        {
            if (!_waitKeys.TryGetValue(buildingId, out var key))
            {
                if (_waitKeys.Count >= 4096) _waitKeys.Clear();
                key = LogisticsKeys.WaitPrefix + buildingId.ToString();
                _waitKeys[buildingId] = key;
            }
            return key;
        }
    }
}
