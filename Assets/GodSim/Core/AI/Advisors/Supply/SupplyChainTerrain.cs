using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>Terrain features the supply-chain advisor counts in a settlement's reachable territory.</summary>
    public enum SupplyChainFeature : byte { Ore, Stone, Gold, Coal, Oil, Trees, Fertile, Water, Coast, River, Hills, Mountain }

    /// <summary>Cached terrain counts (plus reason-log throttling) for one settlement. Not serialized; rebuilt on demand.</summary>
    public sealed class SupplyChainTerrainInfo
    {
        /// <summary>Number of <see cref="SupplyChainFeature"/> values.</summary>
        public const int FeatureCount = 12;
        private const long Never = -(1L << 40);

        /// <summary>Settlement this info describes.</summary>
        public int SettlementId = -1;
        /// <summary>Tick of the last scan.</summary>
        public long ScannedTick = Never;
        /// <summary>Map terrain version at the last scan.</summary>
        public int TerrainVersion = int.MinValue;
        /// <summary>Territory radius at the last scan.</summary>
        public int Radius = -1;
        /// <summary>Settlement center at the last scan.</summary>
        public Vec2i Center = Vec2i.Invalid;
        /// <summary>Tile counts indexed by (int)<see cref="SupplyChainFeature"/>.</summary>
        public readonly int[] Features = new int[FeatureCount];
        private readonly long[] _lastLog = new long[Counts.Buildings];

        /// <summary>Creates an empty info.</summary>
        public SupplyChainTerrainInfo() { for (int i = 0; i < _lastLog.Length; i++) _lastLog[i] = Never; }

        /// <summary>Tiles with a feature.</summary>
        public int Count(SupplyChainFeature f) => Features[(int)f];

        /// <summary>True if a reason for building index b may be logged again (throttles the AI log).</summary>
        public bool CanLog(int b, long tick, long cooldown) => b >= 0 && b < _lastLog.Length && tick - _lastLog[b] >= cooldown;

        /// <summary>Remembers that a reason for building index b was logged.</summary>
        public void MarkLogged(int b, long tick) { if (b >= 0 && b < _lastLog.Length) _lastLog[b] = tick; }
    }

    /// <summary>
    /// Per-settlement terrain feasibility cache: counts deposits, trees, fertile land, water, hills and mountains
    /// within the territory (plus a 3-tile reach margin, never tiles owned by other settlements). Rescans when the
    /// terrain version, center or radius changes, or every <see cref="RescanTicks"/>.
    /// </summary>
    public sealed class SupplyChainTerrain
    {
        /// <summary>Maximum age of a scan before it is refreshed (deposits deplete).</summary>
        public const int RescanTicks = 96;
        /// <summary>Extra tiles beyond the territory radius that Near* placement rules can still reach.</summary>
        public const int ReachMargin = 3;

        private readonly Dictionary<int, SupplyChainTerrainInfo> _cache = new Dictionary<int, SupplyChainTerrainInfo>();
        private readonly List<int> _prune = new List<int>();

        /// <summary>Drops every cached entry.</summary>
        public void Clear() => _cache.Clear();

        /// <summary>Cached entries (debug).</summary>
        public int CachedCount => _cache.Count;

        /// <summary>Returns fresh terrain counts for a settlement (rescans when stale).</summary>
        public SupplyChainTerrainInfo Get(WorldMap map, Settlement s, long tick)
        {
            if (s == null) return null;
            if (!_cache.TryGetValue(s.Id, out var info))
            {
                info = new SupplyChainTerrainInfo { SettlementId = s.Id };
                _cache[s.Id] = info;
            }
            if (map != null && (info.TerrainVersion != map.TerrainVersion || info.Radius != s.TerritoryRadius ||
                                info.Center != s.Center || tick - info.ScannedTick >= RescanTicks || tick < info.ScannedTick))
            {
                Scan(map, s, info);
                info.ScannedTick = tick;
                info.TerrainVersion = map.TerrainVersion;
                info.Radius = s.TerritoryRadius;
                info.Center = s.Center;
            }
            return info;
        }

        /// <summary>Removes entries of settlements that no longer exist or are dead.</summary>
        public void Prune(EntityStore<Settlement> settlements)
        {
            if (settlements == null) return;
            _prune.Clear();
            foreach (var kv in _cache) // removal only; order never affects decisions
            {
                var s = settlements.Get(kv.Key);
                if (s == null || !s.IsAlive) _prune.Add(kv.Key);
            }
            for (int i = 0; i < _prune.Count; i++) _cache.Remove(_prune[i]);
        }

        /// <summary>Counts terrain features reachable by a settlement.</summary>
        public static void Scan(WorldMap map, Settlement s, SupplyChainTerrainInfo info)
        {
            Array.Clear(info.Features, 0, info.Features.Length);
            if (map == null || s == null || !map.InBounds(s.Center)) return;
            int terr = Math.Max(1, s.TerritoryRadius);
            int reach = terr + ReachMargin, reach2 = reach * reach, terr2 = terr * terr;
            var f = info.Features;
            for (int y = s.Center.Y - reach; y <= s.Center.Y + reach; y++)
            {
                if (y < 0 || y >= map.Height) continue;
                for (int x = s.Center.X - reach; x <= s.Center.X + reach; x++)
                {
                    if (x < 0 || x >= map.Width) continue;
                    int dx = x - s.Center.X, dy = y - s.Center.Y, d2 = dx * dx + dy * dy;
                    if (d2 > reach2) continue;
                    int i = map.Index(x, y);
                    int owner = map.Owner[i];
                    if (owner >= 0 && owner != s.Id) continue;
                    var flags = map.Flags[i];
                    var biome = map.Biomes[i];
                    bool hasRes = map.Resource[i] > 0.01f;
                    if (hasRes)
                    {
                        if ((flags & TileFlags.OreDeposit) != 0) f[(int)SupplyChainFeature.Ore]++;
                        if ((flags & TileFlags.StoneDeposit) != 0) f[(int)SupplyChainFeature.Stone]++;
                        if ((flags & TileFlags.GoldDeposit) != 0) f[(int)SupplyChainFeature.Gold]++;
                        if ((flags & TileFlags.CoalDeposit) != 0) f[(int)SupplyChainFeature.Coal]++;
                        if ((flags & TileFlags.OilDeposit) != 0) f[(int)SupplyChainFeature.Oil]++;
                        if ((flags & TileFlags.Trees) != 0) f[(int)SupplyChainFeature.Trees]++;
                    }
                    if (biome.IsWater()) f[(int)SupplyChainFeature.Water]++;
                    if ((flags & TileFlags.River) != 0) f[(int)SupplyChainFeature.River]++;
                    if ((flags & TileFlags.Coastal) != 0) f[(int)SupplyChainFeature.Coast]++;
                    if (biome == Biome.Mountain) f[(int)SupplyChainFeature.Mountain]++;
                    bool inside = owner == s.Id || d2 <= terr2;
                    if (!inside) continue;
                    if (biome == Biome.Hills) f[(int)SupplyChainFeature.Hills]++;
                    if (biome.IsBuildable() && map.BuildingAt[i] < 0 &&
                        ((flags & (TileFlags.FertileSoil | TileFlags.Farmland)) != 0 || map.Fertility[i] >= 0.55f))
                        f[(int)SupplyChainFeature.Fertile]++;
                }
            }
        }

        /// <summary>
        /// 0..1 terrain feasibility of placing a building with the given hard placement rules, given how many of that
        /// type already exist or are being built (crowded deposits lower the value). Soft preferences are ignored.
        /// </summary>
        public static float Feasibility(PlacementRule rule, SupplyChainTerrainInfo info, int existing)
        {
            if (info == null) return 1f;
            float f = 1f;
            if ((rule & PlacementRule.NearOre) != 0) f *= Avail(info.Count(SupplyChainFeature.Ore), existing, true);
            if ((rule & PlacementRule.NearStone) != 0) f *= Avail(info.Count(SupplyChainFeature.Stone), existing, true);
            if ((rule & PlacementRule.NearGold) != 0) f *= Avail(info.Count(SupplyChainFeature.Gold), existing, true);
            if ((rule & PlacementRule.NearCoal) != 0) f *= Avail(info.Count(SupplyChainFeature.Coal), existing, true);
            if ((rule & PlacementRule.NearOil) != 0) f *= Avail(info.Count(SupplyChainFeature.Oil), existing, true);
            if ((rule & PlacementRule.NearForest) != 0) f *= Avail(info.Count(SupplyChainFeature.Trees), existing, true);
            if ((rule & PlacementRule.Fertile) != 0) f *= Avail(info.Count(SupplyChainFeature.Fertile), existing, false);
            if ((rule & PlacementRule.NearWater) != 0)
                f *= Avail(info.Count(SupplyChainFeature.Water) + info.Count(SupplyChainFeature.River) + info.Count(SupplyChainFeature.Coast), existing, false);
            if ((rule & PlacementRule.Coastal) != 0) f *= Avail(info.Count(SupplyChainFeature.Coast), existing, false);
            if ((rule & PlacementRule.NearRiver) != 0) f *= Avail(info.Count(SupplyChainFeature.River), existing, false);
            if ((rule & PlacementRule.OnHills) != 0) f *= Avail(info.Count(SupplyChainFeature.Hills), existing, false);
            if ((rule & PlacementRule.NearMountain) != 0) f *= Avail(info.Count(SupplyChainFeature.Mountain), existing, false);
            return MathUtil.Clamp01(f);
        }

        private static float Avail(int n, int existing, bool depleting)
        {
            if (n <= 0) return 0f;
            float a = Math.Min(1f, 0.45f + 0.08f * n);
            if (depleting && existing > 0 && existing * 4 >= n) a *= 0.35f; // deposits already crowded by our own extractors
            return a;
        }
    }
}
