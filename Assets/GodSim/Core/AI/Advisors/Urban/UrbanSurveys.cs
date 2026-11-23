using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// What a settlement already has: buildings per type (placed, complete or not) and queued orders per type,
    /// category totals and housing capacity. Arrays are allocated once and reused across settlements.
    /// </summary>
    public sealed class UrbanComposition
    {
        /// <summary>Non-destroyed placed buildings per type.</summary>
        public readonly int[] Existing = new int[Counts.Buildings];
        /// <summary>Queued (not yet placed) orders per type.</summary>
        public readonly int[] Pending = new int[Counts.Buildings];
        /// <summary>Existing + pending buildings per BuildingCategory.</summary>
        public readonly int[] CategoryCount = new int[UrbanArchetypeCatalog.CategoryCount];
        /// <summary>Non-destroyed placed buildings (all types).</summary>
        public int TotalExisting;
        /// <summary>Dwellings (existing + pending).</summary>
        public int Dwellings;
        /// <summary>Housing capacity of dwellings (complete: rolled capacity; pending: definition average).</summary>
        public int HousingCapacity;

        /// <summary>Existing + queued count of a type.</summary>
        public int Present(BuildingType t)
        {
            int i = (int)t;
            return i >= 0 && i < Existing.Length ? Existing[i] + Pending[i] : 0;
        }

        public void Clear()
        {
            Array.Clear(Existing, 0, Existing.Length);
            Array.Clear(Pending, 0, Pending.Length);
            Array.Clear(CategoryCount, 0, CategoryCount.Length);
            TotalExisting = 0; Dwellings = 0; HousingCapacity = 0;
        }

        /// <summary>Counts a settlement's buildings and queue (read-only). Allocation-free.</summary>
        public void Survey(WorldState state, GameDatabase db, Settlement s)
        {
            Clear();
            if (state == null || s == null) return;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= Existing.Length) continue;
                Existing[t]++;
                TotalExisting++;
                BuildingDefinition def = null;
                if (db != null) db.TryGetBuilding(b.Type, out def);
                if (def == null) continue;
                AddCategory(def);
                if (def.IsDwelling)
                {
                    Dwellings++;
                    HousingCapacity += b.IsComplete && b.HousingCapacity > 0 ? b.HousingCapacity : AvgHousing(def);
                }
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                var o = q[i];
                if (o == null) continue;
                int t = (int)o.Type;
                if (t < 0 || t >= Pending.Length) continue;
                Pending[t]++;
                BuildingDefinition def = null;
                if (db != null) db.TryGetBuilding(o.Type, out def);
                if (def == null) continue;
                AddCategory(def);
                if (def.IsDwelling) { Dwellings++; HousingCapacity += AvgHousing(def); }
            }
        }

        private void AddCategory(BuildingDefinition def)
        {
            int c = (int)def.Category;
            if (c >= 0 && c < CategoryCount.Length) CategoryCount[c]++;
        }

        private static int AvgHousing(BuildingDefinition def)
        {
            int v = (def.HousingMin + def.HousingMax + 1) / 2;
            return v < 1 ? 1 : v;
        }
    }

    /// <summary>
    /// Terrain features inside a settlement's territory, so the advisor never pushes buildings whose hard placement
    /// rules (<see cref="PlacementRule"/>) cannot be met (no Harbor inland, no Mine without ore).
    /// </summary>
    public sealed class UrbanSiteSurvey
    {
        public int Land, Water, River, Coastal, Trees, Ore, Stone, Gold, Coal, Oil, Fertile, Hills, Mountain;

        public void Reset() { Land = Water = River = Coastal = Trees = Ore = Stone = Gold = Coal = Oil = Fertile = Hills = Mountain = 0; }

        /// <summary>Marks every feature as present (unknown map: never block on placement).</summary>
        public void AllowAll() { Land = Water = River = Coastal = Trees = Ore = Stone = Gold = Coal = Oil = Fertile = Hills = Mountain = 1; }

        /// <summary>Scans a disc of tiles around <paramref name="center"/>. Allocation-free.</summary>
        public void Scan(WorldMap map, Vec2i center, int radius)
        {
            Reset();
            if (map == null) { AllowAll(); return; }
            int r2 = radius * radius;
            bool hasFert = map.Fertility != null && map.Fertility.Length == map.TileCount;
            for (int dy = -radius; dy <= radius; dy++)
            {
                for (int dx = -radius; dx <= radius; dx++)
                {
                    if (dx * dx + dy * dy > r2) continue;
                    int x = center.X + dx, y = center.Y + dy;
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    var b = map.Biomes[i];
                    if (b.IsWater()) { Water++; continue; }
                    Land++;
                    var f = map.Flags[i];
                    if ((f & TileFlags.River) != 0) River++;
                    if ((f & TileFlags.Coastal) != 0) Coastal++;
                    if ((f & TileFlags.Trees) != 0 || b == Biome.Forest || b == Biome.Jungle || b == Biome.Taiga) Trees++;
                    if ((f & TileFlags.OreDeposit) != 0) Ore++;
                    if ((f & TileFlags.StoneDeposit) != 0) Stone++;
                    if ((f & TileFlags.GoldDeposit) != 0) Gold++;
                    if ((f & TileFlags.CoalDeposit) != 0) Coal++;
                    if ((f & TileFlags.OilDeposit) != 0) Oil++;
                    if ((f & TileFlags.FertileSoil) != 0 || (hasFert && map.Fertility[i] >= 0.55f)) Fertile++;
                    if (b == Biome.Hills) Hills++;
                    else if (b == Biome.Mountain) Mountain++;
                }
            }
        }

        /// <summary>True if the definition's hard placement requirements can be met somewhere in the scanned area.</summary>
        public bool Allows(BuildingDefinition def, RaceId race)
        {
            if (def == null) return false;
            var p = def.Placement;
            if (p == PlacementRule.None) return true;
            if (Has(p, PlacementRule.NearWater) && Water + River + Coastal == 0) return false;
            if (Has(p, PlacementRule.Coastal) && Coastal == 0 && Water == 0) return false;
            if (Has(p, PlacementRule.OnHills) && Hills == 0 && !(race == RaceId.Dwarf && Mountain > 0)) return false;
            if (Has(p, PlacementRule.NearMountain) && Mountain == 0) return false;
            if (Has(p, PlacementRule.NearForest) && Trees == 0) return false;
            if (Has(p, PlacementRule.NearOre) && Ore == 0) return false;
            if (Has(p, PlacementRule.NearStone) && Stone == 0 && Mountain == 0) return false;
            if (Has(p, PlacementRule.NearGold) && Gold == 0) return false;
            if (Has(p, PlacementRule.NearCoal) && Coal == 0) return false;
            if (Has(p, PlacementRule.NearOil) && Oil == 0) return false;
            if (Has(p, PlacementRule.NearRiver) && River == 0) return false;
            if (Has(p, PlacementRule.Fertile) && Fertile == 0) return false;
            return true;
        }

        private static bool Has(PlacementRule p, PlacementRule f) => (p & f) != 0;
    }
}
