using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Bounded land survey for the <see cref="ProspectorAdvisor"/>. Scans a settlement's territory (Chebyshev radius
    /// TerritoryRadius + <see cref="Margin"/>, skipping tiles owned by other settlements) and caches in
    /// <see cref="Settlement.Memory"/>: remaining amount and tile count per deposit kind (from <see cref="WorldMap.Resource"/>
    /// on Ore/Stone/Gold/Coal/Oil/Trees tiles), the peak amount seen, a smoothed depletion rate per year, and land
    /// features (fertile, water, coast, river, hills, mountain, land tiles). Everything lives in the save, so the
    /// advisor decides identically after a load. Allocation-free after construction.
    /// </summary>
    public sealed class ProspectorSurveyor
    {
        /// <summary>Tiles scanned beyond the territory radius.</summary>
        public const int Margin = 2;
        /// <summary>Hard cap on the scan radius.</summary>
        public const int MaxRadius = 40;
        /// <summary>Normal refresh period (~2 years).</summary>
        public const int RefreshTicks = 2 * SimTime.TicksPerYear;
        /// <summary>Refresh period while a deposit is being watched (running low).</summary>
        public const int WatchTicks = SimTime.TicksPerYear / 2;
        /// <summary>Fertility at or above which buildable land counts as fertile.</summary>
        public const float FertileThreshold = 0.5f;

        private readonly float[] _amount = new float[ProspectorKinds.Count];
        private readonly int[] _tiles = new int[ProspectorKinds.Count];

        /// <summary>True once the settlement has been surveyed at least once.</summary>
        public static bool HasSurvey(Settlement s) => s != null && s.Memory.ContainsKey(ProspectorKinds.KeyTick);

        /// <summary>Scan radius for a settlement.</summary>
        public static int ScanRadius(Settlement s) => MathUtil.Clamp(s.TerritoryRadius + Margin, 1, MaxRadius);

        /// <summary>Packs a center into one exactly-representable float (maps are at most 1024 wide).</summary>
        public static float PackCenter(Vec2i c) => c.IsValid ? c.X * 2048f + c.Y : -1f;

        /// <summary>True when the cached survey is missing, stale, or the territory moved/resized.</summary>
        public bool NeedsRefresh(Settlement s, long tick)
        {
            if (s == null) return false;
            if (!s.Memory.TryGetValue(ProspectorKinds.KeyTick, out float last)) return true;
            if ((int)s.Mem(ProspectorKinds.KeyRadius, -1f) != ScanRadius(s)) return true;
            if (s.Mem(ProspectorKinds.KeyCenter, -1f) != PackCenter(s.Center)) return true;
            long age = tick - (long)last;
            if (age < 0 || age >= RefreshTicks) return true;
            return s.Mem(ProspectorKinds.KeyWatch) > 0.5f && age >= WatchTicks;
        }

        /// <summary>Surveys the settlement's land now and writes the cache. Null-safe.</summary>
        public void Run(WorldMap map, Settlement s, long tick)
        {
            if (map == null || s == null || !s.Center.IsValid) return;
            int r = ScanRadius(s);
            var c = map.ClampToBounds(s.Center);
            for (int k = 0; k < ProspectorKinds.Count; k++) { _amount[k] = 0f; _tiles[k] = 0; }
            int land = 0, fertile = 0, water = 0, coast = 0, river = 0, hills = 0, mountain = 0;

            int x0 = Math.Max(0, c.X - r), y0 = Math.Max(0, c.Y - r);
            int x1 = Math.Min(map.Width - 1, c.X + r), y1 = Math.Min(map.Height - 1, c.Y + r);
            var flags = map.Flags;
            var biomes = map.Biomes;
            var owner = map.Owner;
            var resource = map.Resource;
            for (int y = y0; y <= y1; y++)
            {
                int row = y * map.Width;
                for (int x = x0; x <= x1; x++)
                {
                    int i = row + x;
                    int o = owner[i];
                    if (o >= 0 && o != s.Id) continue; // a neighbor's land is not ours to plan with
                    Biome b = biomes[i];
                    TileFlags f = flags[i];
                    bool isWater = b.IsWater();
                    bool isRiver = (f & TileFlags.River) != 0;
                    if (isWater || isRiver) water++;
                    if (isRiver) river++;
                    if (!isWater)
                    {
                        land++;
                        if ((f & TileFlags.Coastal) != 0) coast++;
                        if (b == Biome.Hills) hills++;
                        else if (b == Biome.Mountain) mountain++;
                        if (b.IsBuildable() && ((f & TileFlags.FertileSoil) != 0 || map.Fertility[i] >= FertileThreshold)) fertile++;
                    }
                    if ((f & ProspectorKinds.AnyDepositFlag) == 0) continue;
                    for (int k = 0; k < ProspectorKinds.Count; k++)
                    {
                        if ((f & ProspectorKinds.TileFlag[k]) == 0) continue;
                        _tiles[k]++;
                        float a = resource[i];
                        _amount[k] += a > 0f && !float.IsInfinity(a) ? Math.Min(a, 1e6f) : ProspectorKinds.DefaultTileAmount[k];
                    }
                }
            }

            var mem = s.Memory;
            float pack = PackCenter(s.Center);
            bool had = mem.TryGetValue(ProspectorKinds.KeyTick, out float lastTick);
            bool sameArea = had && (int)s.Mem(ProspectorKinds.KeyRadius, -1f) == r && s.Mem(ProspectorKinds.KeyCenter, -1f) == pack;
            float years = had ? (tick - lastTick) / (float)SimTime.TicksPerYear : 0f;
            for (int k = 0; k < ProspectorKinds.Count; k++)
            {
                float amt = MathUtil.Sanitize(_amount[k]);
                float prev = s.Mem(ProspectorKinds.AmountKey[k]);
                if (sameArea && years > 0.05f)
                {
                    float drop = Math.Max(0f, prev - amt) / years;
                    float old = s.Mem(ProspectorKinds.RateKey[k]);
                    SetOrRemove(s, ProspectorKinds.RateKey[k], old > 0f ? 0.5f * (old + drop) : drop);
                }
                else if (!sameArea) mem.Remove(ProspectorKinds.RateKey[k]);
                // A moved or resized territory starts a fresh baseline so lost land never reads as "exhausted".
                float peak = sameArea ? Math.Max(s.Mem(ProspectorKinds.PeakKey[k]), amt) : amt;
                SetOrRemove(s, ProspectorKinds.AmountKey[k], amt);
                SetOrRemove(s, ProspectorKinds.TilesKey[k], _tiles[k]);
                SetOrRemove(s, ProspectorKinds.PeakKey[k], peak);
            }
            SetOrRemove(s, ProspectorKinds.KeyLand, land);
            SetOrRemove(s, ProspectorKinds.KeyFertile, fertile);
            SetOrRemove(s, ProspectorKinds.KeyWater, water);
            SetOrRemove(s, ProspectorKinds.KeyCoast, coast);
            SetOrRemove(s, ProspectorKinds.KeyRiver, river);
            SetOrRemove(s, ProspectorKinds.KeyHills, hills);
            SetOrRemove(s, ProspectorKinds.KeyMountain, mountain);
            mem[ProspectorKinds.KeyTick] = tick;
            mem[ProspectorKinds.KeyRadius] = r;
            mem[ProspectorKinds.KeyCenter] = pack;
        }

        private static void SetOrRemove(Settlement s, string key, float v)
        {
            v = MathUtil.Sanitize(v);
            if (Math.Abs(v) < 1e-4f) s.Memory.Remove(key); else s.Memory[key] = v;
        }
    }
}
