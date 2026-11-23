using System.Collections.Generic;

namespace GodSim.Core.History
{
    /// <summary>Recorded series of one civilization (bounded; see <see cref="HistoryRecorder"/>).</summary>
    public sealed class CivHistory
    {
        internal readonly DownsamplingBuffer<CivSample> Buffer;

        // Per-sample accumulators (reused every sampling pass; no allocation).
        internal int AccPopulation;
        internal int AccSettlements;
        internal int AccBuildings;
        internal double AccHappiness;
        internal bool SeenThisPass;

        internal CivHistory(int civId, int capacity)
        {
            CivId = civId;
            Buffer = new DownsamplingBuffer<CivSample>(capacity);
        }

        public int CivId { get; }
        /// <summary>Civilization name at the last sample.</summary>
        public string Name { get; internal set; } = "";
        public RaceId Race { get; internal set; }
        public long FirstTick { get; internal set; } = -1;
        public long LastTick { get; internal set; } = -1;
        /// <summary>False once the civilization collapsed (a final zero-population sample is recorded, then sampling stops).</summary>
        public bool IsAlive { get; internal set; } = true;
        public long CollapsedTick { get; internal set; } = -1;

        /// <summary>Stored samples, oldest first (first and latest samples are always kept).</summary>
        public IReadOnlyList<CivSample> Samples => Buffer;
        public int Count => Buffer.Count;
        /// <summary>Raw sampling intervals represented by each stored sample (doubles on each downsampling pass).</summary>
        public long Stride => Buffer.Stride;
        public CivSample Latest => Buffer.Last;
    }
}
