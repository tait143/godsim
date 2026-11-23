using System;
using System.Collections.Generic;

namespace GodSim.Core.Settlements
{
    /// <summary>
    /// Road-laying helper shared by <see cref="TownPlannerSystem"/> and <see cref="RoadNetworkSystem"/>.
    /// Uses a <see cref="GridPathfinder"/> cached per map size (4-way; road 0.3, free land 1 (+0.5 hills),
    /// trees 1.5, other land 2.5; water, buildings, mountains, walls and farmland impassable). Access roads
    /// are aimed at the nearest tile of the settlement's linked road network (see <see cref="AccessField"/>),
    /// so a building is never left with a dead-end stub. Also lays the plaza ring around a town center,
    /// tills farm fields and clears fields whose farm is gone. One instance per system (not thread-safe).
    /// </summary>
    public sealed class PlannerRoads
    {
        /// <summary>Path cost of an existing road tile.</summary>
        public const float RoadCost = 0.3f;
        /// <summary>A* budget for a building access road.</summary>
        public const int AccessMaxExpansions = 4000;
        /// <summary>A* budget for an inter-settlement road.</summary>
        public const int NetworkMaxExpansions = 20000;
        /// <summary>Maximum farmland tiles tilled around a newly placed farm.</summary>
        public const int MaxFieldTiles = 8;
        /// <summary>Minimum tile fertility for a field (FertileSoil always qualifies).</summary>
        public const float FieldMinFertility = 0.3f;
        /// <summary>Settlement memory key: id of the town center whose plaza ring was laid.</summary>
        public const string PlazaMemoryKey = "plan.plaza";

        private GridPathfinder _pathfinder;
        private int _pfWidth, _pfHeight;
        private WorldMap _costMap;
        private bool _hasBlock;
        private int _blockX0, _blockY0, _blockX1, _blockY1;
        private readonly Func<int, float> _costFn;
        private readonly List<Vec2i> _path = new List<Vec2i>(128);
        private AccessField _ownField;

        /// <summary>Creates the helper (the cost delegate is allocated once here).</summary>
        public PlannerRoads() { _costFn = CostOnCurrentMap; }

        /// <summary>The most recent path found (start..goal inclusive). Valid until the next query.</summary>
        public IReadOnlyList<Vec2i> LastPath => _path;

        /// <summary>Returns the cached pathfinder, recreating it only when the map size changes.</summary>
        public GridPathfinder GetPathfinder(WorldMap map)
        {
            if (_pathfinder == null || _pfWidth != map.Width || _pfHeight != map.Height)
            {
                _pathfinder = new GridPathfinder(map.Width, map.Height);
                _pfWidth = map.Width;
                _pfHeight = map.Height;
            }
            return _pathfinder;
        }

        /// <summary>Road-building cost of entering tile i; PositiveInfinity = impassable.</summary>
        public static float TileCost(WorldMap map, int i)
        {
            if (map.BuildingAt[i] >= 0) return float.PositiveInfinity;
            Biome b = map.Biomes[i];
            if (b.IsWater() || b == Biome.Mountain) return float.PositiveInfinity;
            TileFlags f = map.Flags[i];
            if ((f & (TileFlags.Wall | TileFlags.Farmland)) != 0) return float.PositiveInfinity;
            if ((f & TileFlags.Road) != 0) return RoadCost;
            if (!b.IsBuildable()) return 2.5f;
            float c = (f & TileFlags.Trees) != 0 ? 1.5f : 1f;
            if (b == Biome.Hills) c += 0.5f;
            return c;
        }

        private float CostOnCurrentMap(int i)
        {
            if (_hasBlock)
            {
                int x = i % _costMap.Width, y = i / _costMap.Width;
                if (x >= _blockX0 && x <= _blockX1 && y >= _blockY0 && y <= _blockY1) return float.PositiveInfinity;
            }
            return TileCost(_costMap, i);
        }

        /// <summary>True if a road may be drawn on tile i (land, no building, wall, farmland or mountain).</summary>
        public static bool CanCarryRoad(WorldMap map, int i)
        {
            if (map.BuildingAt[i] >= 0) return false;
            Biome b = map.Biomes[i];
            if (b.IsWater() || b == Biome.Mountain) return false;
            return (map.Flags[i] & (TileFlags.Wall | TileFlags.Farmland)) == 0;
        }

        /// <summary>Marks a road on (x, y) if allowed and not already a road. Returns true if the tile changed.</summary>
        public static bool TryMarkRoad(WorldMap map, int x, int y, ref TerrainDirtyRect dirty)
        {
            if (map == null || !map.InBounds(x, y)) return false;
            int i = map.Index(x, y);
            if ((map.Flags[i] & TileFlags.Road) != 0 || !CanCarryRoad(map, i)) return false;
            map.Set(i, TileFlags.Road);
            dirty.Include(x, y);
            return true;
        }

        /// <summary>Runs A* (4-way) from <paramref name="from"/> to <paramref name="to"/> into <see cref="LastPath"/>.</summary>
        public bool FindRoadPath(WorldMap map, Vec2i from, Vec2i to, int maxExpansions)
        {
            return FindRoadPath(map, from, to, maxExpansions, Vec2i.Zero, 0, 0);
        }

        /// <summary>
        /// Runs A* (4-way) from <paramref name="from"/> to <paramref name="to"/> into <see cref="LastPath"/>,
        /// treating the rectangle at <paramref name="blockOrigin"/> (<paramref name="blockW"/> x
        /// <paramref name="blockH"/>, 0 x 0 = none) as impassable, e.g. a footprint that is about to be built.
        /// </summary>
        public bool FindRoadPath(WorldMap map, Vec2i from, Vec2i to, int maxExpansions, Vec2i blockOrigin, int blockW, int blockH)
        {
            _path.Clear();
            if (map == null || !map.InBounds(from) || !map.InBounds(to)) return false;
            bool block = blockW > 0 && blockH > 0;
            if (block && (InRect(from, blockOrigin, blockW, blockH) || InRect(to, blockOrigin, blockW, blockH))) return false;
            if (from == to) { _path.Add(from); return true; }
            var pf = GetPathfinder(map);
            _costMap = map;
            _hasBlock = block;
            _blockX0 = blockOrigin.X;
            _blockY0 = blockOrigin.Y;
            _blockX1 = blockOrigin.X + blockW - 1;
            _blockY1 = blockOrigin.Y + blockH - 1;
            bool ok;
            try { ok = pf.FindPath(from, to, _costFn, _path, false, MathUtil.Clamp(maxExpansions, 1, 200000)); }
            finally { _costMap = null; _hasBlock = false; }
            if (!ok) _path.Clear();
            return ok;
        }

        private static bool InRect(Vec2i p, Vec2i o, int w, int h) => p.X >= o.X && p.Y >= o.Y && p.X < o.X + w && p.Y < o.Y + h;

        /// <summary>Marks every tile of <see cref="LastPath"/> that can carry a road. Returns tiles changed.</summary>
        public int LayLastPath(WorldMap map, ref TerrainDirtyRect dirty)
        {
            int changed = 0;
            for (int k = 0; k < _path.Count; k++)
                if (TryMarkRoad(map, _path[k].X, _path[k].Y, ref dirty)) changed++;
            return changed;
        }

        /// <summary>Lays the plaza ring (road on free tiles around the town center footprint), once per town center.</summary>
        public void EnsurePlaza(WorldState state, Settlement s, ref TerrainDirtyRect dirty)
        {
            if (state == null || s == null || s.TownCenterId < 0) return;
            var tc = state.Buildings.Get(s.TownCenterId);
            if (tc == null || tc.IsDestroyed) return;
            if (s.Memory.TryGetValue(PlazaMemoryKey, out float laid) && (int)laid == tc.Id) return;
            var map = state.Map;
            int x0 = tc.Origin.X - 1, y0 = tc.Origin.Y - 1, x1 = tc.Origin.X + tc.Width, y1 = tc.Origin.Y + tc.Height;
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (x > x0 && x < x1 && y > y0 && y < y1) continue;
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    if (map.IsFreeBuildable(i) && (map.Flags[i] & TileFlags.Farmland) == 0) TryMarkRoad(map, x, y, ref dirty);
                }
            s.Memory[PlazaMemoryKey] = tc.Id;
        }

        /// <summary>A* expansions used by the last access-path plan (0 when no search was needed).</summary>
        public int LastAccessExpansions { get; private set; }

        /// <summary>
        /// Plans (does not lay) the access road from <paramref name="access"/> to the nearest tile of the
        /// settlement's linked road network described by <paramref name="field"/>, never entering the footprint
        /// rectangle at <paramref name="blockOrigin"/> (<paramref name="blockW"/> x <paramref name="blockH"/>).
        /// The field's own shortest step path is checked first (cheap, O(distance)). If it is blocked the
        /// entrance is rejected, so a hopeless entrance never costs a search. Otherwise A* (4-way,
        /// <see cref="AccessMaxExpansions"/>, the road cost rules) runs toward the same network tile for a
        /// nicer route, and the step path is the fallback. The result is in <see cref="LastPath"/> (a single
        /// tile when the entrance is already on the network). Returns false if the entrance cannot reach the
        /// network. <paramref name="maxExpansions"/> caps the A* polish (0 = use the step path directly).
        /// </summary>
        public bool PlanAccessPath(WorldMap map, AccessField field, Vec2i access, Vec2i blockOrigin, int blockW, int blockH, int maxExpansions = AccessMaxExpansions)
        {
            _path.Clear();
            LastAccessExpansions = 0;
            if (map == null || field == null || !map.InBounds(access)) return false;
            if (blockW > 0 && blockH > 0 && InRect(access, blockOrigin, blockW, blockH)) return false;
            if (!field.TryGetPathToNetwork(map, access, _path, blockOrigin, blockW, blockH)) return false;
            if (_path.Count <= 1 || maxExpansions <= 0) return true;
            Vec2i goal = _path[_path.Count - 1];
            bool ok = FindRoadPath(map, access, goal, Math.Min(maxExpansions, AccessMaxExpansions), blockOrigin, blockW, blockH);
            LastAccessExpansions = _pathfinder != null ? _pathfinder.LastExpansions : 0;
            if (ok) return true;
            return field.TryGetPathToNetwork(map, access, _path, blockOrigin, blockW, blockH);
        }

        /// <summary>
        /// Connects a building access tile to the settlement's road network (the plaza ring and every road linked
        /// to it) and marks the path as road. Unlinked road stubs do not count as connected. On failure nothing is
        /// marked. Returns true if the access tile is now linked to the network.
        /// </summary>
        public bool ConnectAccess(WorldState state, Settlement s, Vec2i access, ref TerrainDirtyRect dirty)
        {
            if (state == null || s == null || state.Map == null) return false;
            var map = state.Map;
            if (!map.InBounds(access)) return false;
            if (_ownField == null) _ownField = new AccessField();
            int half = MathUtil.Clamp(Math.Max(s.TerritoryRadius, access.ChebyshevTo(s.Center)) + 8, 8, 64);
            _ownField.Build(state, s, half);
            if (!PlanAccessPath(map, _ownField, access, Vec2i.Zero, 0, 0)) return false;
            LayLastPath(map, ref dirty);
            return true;
        }

        /// <summary>Connects two settlements' road anchors (bounded A*). Returns true if a road was laid.</summary>
        public bool ConnectSettlements(WorldMap map, Settlement a, Settlement b, ref TerrainDirtyRect dirty)
        {
            if (map == null || a == null || b == null) return false;
            Vec2i from = FindAnchor(map, a.Center, 10);
            Vec2i to = FindAnchor(map, b.Center, 10);
            if (!from.IsValid || !to.IsValid) return false;
            if (!FindRoadPath(map, from, to, NetworkMaxExpansions)) return false;
            LayLastPath(map, ref dirty);
            return true;
        }

        /// <summary>Nearest road tile to <paramref name="center"/> within maxRadius (ring order, then Manhattan);
        /// falls back to the nearest passable tile. Invalid if neither exists.</summary>
        public static Vec2i FindAnchor(WorldMap map, Vec2i center, int maxRadius)
        {
            Vec2i fallback = Vec2i.Invalid;
            for (int r = 0; r <= maxRadius; r++)
            {
                Vec2i bestRoad = Vec2i.Invalid, bestFree = Vec2i.Invalid;
                int bestRoadD = int.MaxValue, bestFreeD = int.MaxValue;
                int count = PlacementRules.RingCount(r);
                for (int t = 0; t < count; t++)
                {
                    PlacementRules.RingOffset(r, t, out int dx, out int dy);
                    int x = center.X + dx, y = center.Y + dy;
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    float c = TileCost(map, i);
                    if (float.IsInfinity(c)) continue;
                    int d = Math.Abs(dx) + Math.Abs(dy);
                    if ((map.Flags[i] & TileFlags.Road) != 0) { if (d < bestRoadD) { bestRoadD = d; bestRoad = new Vec2i(x, y); } }
                    else if (!fallback.IsValid && d < bestFreeD) { bestFreeD = d; bestFree = new Vec2i(x, y); }
                }
                if (bestRoad.IsValid) return bestRoad;
                if (!fallback.IsValid && bestFree.IsValid) fallback = bestFree;
            }
            return fallback;
        }

        /// <summary>
        /// Tills up to <paramref name="maxTiles"/> free, fertile, tree-less land tiles in the ring around the farm
        /// (Farmland flag). Fields belong to the farm's settlement or are unclaimed. Returns tiles tilled.
        /// </summary>
        public int MarkFarmland(WorldMap map, Settlement s, Building farm, int maxTiles, ref TerrainDirtyRect dirty)
        {
            if (map == null || s == null || farm == null || maxTiles <= 0) return 0;
            int tilled = 0;
            int x0 = farm.Origin.X - 1, y0 = farm.Origin.Y - 1, x1 = farm.Origin.X + farm.Width, y1 = farm.Origin.Y + farm.Height;
            for (int y = y0; y <= y1 && tilled < maxTiles; y++)
                for (int x = x0; x <= x1 && tilled < maxTiles; x++)
                {
                    if (x > x0 && x < x1 && y > y0 && y < y1) continue;
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    if (!map.IsFreeBuildable(i)) continue;
                    TileFlags f = map.Flags[i];
                    if ((f & (TileFlags.Farmland | TileFlags.Trees)) != 0) continue;
                    if (map.Owner[i] >= 0 && map.Owner[i] != s.Id) continue;
                    if (map.Fertility[i] < FieldMinFertility && (f & TileFlags.FertileSoil) == 0) continue;
                    map.Set(i, TileFlags.Farmland);
                    dirty.Include(x, y);
                    tilled++;
                }
            return tilled;
        }

        /// <summary>True if a living (not destroyed) Farm occupies tile i.</summary>
        public static bool IsLiveFarmAt(WorldState state, int i)
        {
            int id = state.Map.BuildingAt[i];
            if (id < 0) return false;
            var b = state.Buildings.Get(id);
            return b != null && !b.IsDestroyed && b.Type == BuildingType.Farm;
        }

        /// <summary>True if a living Farm occupies (x, y) or one of its 8 neighbours (i.e. the tile can be its field).</summary>
        public static bool IsNextToLiveFarm(WorldState state, int x, int y)
        {
            var map = state.Map;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if (map.InBounds(nx, ny) && IsLiveFarmAt(state, map.Index(nx, ny))) return true;
                }
            return false;
        }

        /// <summary>
        /// Clears Farmland on every free tile of the inclusive rectangle (x0, y0)..(x1, y1) that no living Farm
        /// borders any more (orphaned fields of a destroyed farm). Returns tiles cleared.
        /// </summary>
        public static int ClearOrphanFields(WorldState state, int x0, int y0, int x1, int y1, ref TerrainDirtyRect dirty)
        {
            if (state == null || state.Map == null) return 0;
            var map = state.Map;
            int cleared = 0;
            for (int y = Math.Max(0, y0); y <= Math.Min(map.Height - 1, y1); y++)
                for (int x = Math.Max(0, x0); x <= Math.Min(map.Width - 1, x1); x++)
                {
                    int i = map.Index(x, y);
                    if ((map.Flags[i] & TileFlags.Farmland) == 0 || map.BuildingAt[i] >= 0) continue;
                    if (IsNextToLiveFarm(state, x, y)) continue;
                    map.Clear(i, TileFlags.Farmland);
                    dirty.Include(x, y);
                    cleared++;
                }
            return cleared;
        }

        /// <summary>Removes the Farmland flag under a footprint (a farm built on old fields). Returns tiles changed.</summary>
        public static int ClearFarmlandUnder(WorldMap map, Vec2i origin, int w, int h, ref TerrainDirtyRect dirty)
        {
            if (map == null) return 0;
            int changed = 0;
            for (int y = origin.Y; y < origin.Y + h; y++)
                for (int x = origin.X; x < origin.X + w; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    if ((map.Flags[i] & TileFlags.Farmland) == 0) continue;
                    map.Clear(i, TileFlags.Farmland);
                    dirty.Include(x, y);
                    changed++;
                }
            return changed;
        }

        /// <summary>Bumps the map's terrain version and publishes one <see cref="TerrainChangedEvent"/> if anything changed; resets the rect.</summary>
        public static void Commit(SimContext ctx, ref TerrainDirtyRect dirty)
        {
            if (!dirty.Any || ctx == null || ctx.State == null) return;
            ctx.State.Map.MarkTerrainChanged();
            if (ctx.Events != null)
                ctx.Events.Publish(new TerrainChangedEvent { MinX = dirty.MinX, MinY = dirty.MinY, MaxX = dirty.MaxX, MaxY = dirty.MaxY });
            dirty = default(TerrainDirtyRect);
        }
    }
}
