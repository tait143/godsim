using System;

namespace GodSim.Unity
{
    /// <summary>
    /// Reusable, engine-free result buffer of <see cref="OverlayData"/>: one value and one opacity weight per
    /// tile (index = y * Width + x, same as <c>WorldMap</c>), plus the normalization range. Arrays are only
    /// reallocated when the map size changes; per-settlement scratch grows monotonically. A value of NaN
    /// means "no data" (drawn fully transparent).
    /// </summary>
    public sealed class OverlayField
    {
        /// <summary>Upper bound on tracked settlement/civ ids (defends against corrupt ids).</summary>
        public const int MaxTrackedIds = 1 << 20;

        public int Width { get; private set; }
        public int Height { get; private set; }
        public int TileCount => Width * Height;

        /// <summary>Per-tile value (NaN = no data). For categorical modes the value is a category id (civ id, -1 = none).</summary>
        public float[] Values { get; private set; } = new float[0];

        /// <summary>Per-tile opacity weight 0..1 (distance falloff, border emphasis, faith intensity...).</summary>
        public float[] Weights { get; private set; } = new float[0];

        /// <summary>Mode this field was (or is being) computed for.</summary>
        public OverlayMode Mode { get; internal set; }

        /// <summary>Normalization range used for coloring (fixed per mode or derived from data).</summary>
        public float Min { get; internal set; }
        public float Max { get; internal set; } = 1f;

        /// <summary>Smallest / largest finite value found (NaN when <see cref="ValidCount"/> is 0).</summary>
        public float DataMin { get; internal set; } = float.NaN;
        public float DataMax { get; internal set; } = float.NaN;

        /// <summary>Number of tiles with a finite value.</summary>
        public int ValidCount { get; internal set; }

        /// <summary>True once <see cref="OverlayData.End"/> ran for the current computation.</summary>
        public bool IsComplete { get; internal set; }

        /// <summary>True when the categorical value is a civ id (Territory, Religion).</summary>
        public bool IsCategorical => OverlayData.IsCategorical(Mode);

        // ---- per-settlement scratch, indexed by settlement id (ids are monotonic and never reused) ----
        internal int SettlementIdLimit;
        internal bool[] SValid = new bool[0];
        internal int[] SCenterX = new int[0];
        internal int[] SCenterY = new int[0];
        internal float[] SInvRadius2 = new float[0];
        internal int[] SCiv = new int[0];
        internal float[] SMetric = new float[0];
        internal float[] SCategory = new float[0];
        internal float[] SWeightSum = new float[0];

        // ---- bounding box of danger sources (propagation is limited to it plus the decay reach) ----
        internal int DMinX, DMinY, DMaxX, DMaxY;

        internal void ResetDangerBounds() { DMinX = DMinY = int.MaxValue; DMaxX = DMaxY = int.MinValue; }

        internal void IncludeDanger(int x, int y)
        {
            if (x < DMinX) DMinX = x;
            if (x > DMaxX) DMaxX = x;
            if (y < DMinY) DMinY = y;
            if (y > DMaxY) DMaxY = y;
        }

        // ---- active wars (attacker/defender civ ids) for war-front detection ----
        internal int WarCount;
        internal int[] WarA = new int[8];
        internal int[] WarB = new int[8];

        /// <summary>Resizes the per-tile buffers (no allocation when the size is unchanged).</summary>
        public void Resize(int width, int height)
        {
            if (width < 0) width = 0;
            if (height < 0) height = 0;
            if (width == Width && height == Height && Values.Length == width * height) return;
            Width = width;
            Height = height;
            Values = new float[width * height];
            Weights = new float[width * height];
            IsComplete = false;
        }

        /// <summary>Value normalized to [0, 1] by <see cref="Min"/>/<see cref="Max"/> (NaN stays NaN).</summary>
        public float Normalized(int index)
        {
            float v = Values[index];
            if (float.IsNaN(v)) return float.NaN;
            float range = Max - Min;
            if (!(range > 1e-6f)) return 0.5f;
            float t = (v - Min) / range;
            return t < 0f ? 0f : (t > 1f ? 1f : t);
        }

        internal void EnsureSettlementCapacity(int n)
        {
            if (n <= SValid.Length) return;
            int cap = Math.Max(n, Math.Max(64, SValid.Length * 2));
            if (cap > MaxTrackedIds) cap = Math.Max(n, MaxTrackedIds);
            SValid = new bool[cap];
            SCenterX = new int[cap];
            SCenterY = new int[cap];
            SInvRadius2 = new float[cap];
            SCiv = new int[cap];
            SMetric = new float[cap];
            SCategory = new float[cap];
            SWeightSum = new float[cap];
        }

        internal void AddWar(int a, int b)
        {
            if (WarCount == WarA.Length)
            {
                var na = new int[WarA.Length * 2];
                var nb = new int[WarB.Length * 2];
                Array.Copy(WarA, na, WarCount);
                Array.Copy(WarB, nb, WarCount);
                WarA = na;
                WarB = nb;
            }
            WarA[WarCount] = a;
            WarB[WarCount] = b;
            WarCount++;
        }

        internal bool AtWar(int civA, int civB)
        {
            if (civA == civB || civA < 0 || civB < 0) return false;
            for (int k = 0; k < WarCount; k++)
            {
                int a = WarA[k], b = WarB[k];
                if ((a == civA && b == civB) || (a == civB && b == civA)) return true;
            }
            return false;
        }
    }
}
