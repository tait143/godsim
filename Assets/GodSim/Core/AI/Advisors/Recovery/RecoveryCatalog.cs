using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Data-driven role tables derived once from the <see cref="GameDatabase"/> building definitions: which types
    /// supply water, walls, storage, food, health, faith, or a given resource, and the rebuild tier of each type.
    /// Nothing here is hard-coded per building except the Town Center (the heart of the settlement) and the
    /// Granary preference after famine.
    /// </summary>
    public sealed class RecoveryCatalog
    {
        /// <summary>PlacementRule bits that are hard terrain requirements (the rest are soft preferences).</summary>
        public const ushort HardPlacementMask = (ushort)(PlacementRule.NearWater | PlacementRule.Coastal | PlacementRule.OnHills | PlacementRule.NearMountain
            | PlacementRule.NearForest | PlacementRule.NearOre | PlacementRule.NearStone | PlacementRule.NearGold | PlacementRule.NearCoal
            | PlacementRule.NearOil | PlacementRule.Fertile | PlacementRule.NearRiver);

        public readonly GameDatabase Db;
        public readonly int[] Water;
        public readonly int[] Walls;
        public readonly int[] Storage;
        public readonly int[] Food;
        public readonly int[] Health;
        public readonly int[] Faith;
        /// <summary>Producers per resource (index = (int)ResourceType).</summary>
        public readonly int[][] Producers;
        /// <summary>Rebuild tier per building type.</summary>
        public readonly RecoveryTier[] Tier = new RecoveryTier[Counts.Buildings];
        /// <summary>Total resource cost per type (cheapness tie-breaker).</summary>
        public readonly float[] CostTotal = new float[Counts.Buildings];
        public readonly float[] WaterAmount = new float[Counts.Buildings];
        public readonly float[] DefenseAmount = new float[Counts.Buildings];
        public readonly float[] HealthAmount = new float[Counts.Buildings];
        public readonly float[] FaithAmount = new float[Counts.Buildings];
        public readonly float[] StorageAmount = new float[Counts.Buildings];

        public RecoveryCatalog(GameDatabase db)
        {
            Db = db;
            var water = new List<int>(); var walls = new List<int>(); var storage = new List<int>();
            var food = new List<int>(); var health = new List<int>(); var faith = new List<int>();
            var prod = new List<int>[Counts.Resources];
            for (int r = 0; r < prod.Length; r++) prod[r] = new List<int>();
            for (int i = 0; i < Counts.Buildings; i++)
            {
                var d = db != null ? db.Buildings[i] : null;
                Tier[i] = RecoveryTier.Other;
                if (d == null) continue;
                Tier[i] = TierOf(d);
                float cost = 0f;
                var c = d.Cost;
                if (c != null) for (int k = 0; k < c.Length; k++) cost += MathUtil.Sanitize(c[k].Amount);
                CostTotal[i] = cost;
                if (d.IsWonder || d.UniquePerCivilization) continue;
                WaterAmount[i] = ServiceAmount(d, ServiceType.Water);
                DefenseAmount[i] = ServiceAmount(d, ServiceType.Defense);
                HealthAmount[i] = ServiceAmount(d, ServiceType.Health);
                FaithAmount[i] = ServiceAmount(d, ServiceType.Faith);
                StorageAmount[i] = d.Category == BuildingCategory.Storage ? MathUtil.Sanitize(d.StorageCapacity) : 0f;
                if (WaterAmount[i] > 0f) water.Add(i);
                if (d.Category == BuildingCategory.Military && d.Shape == BuildingShape.Wall && DefenseAmount[i] > 0f) walls.Add(i);
                if (StorageAmount[i] > 0f) storage.Add(i);
                if (OutputAmount(d, ResourceType.Food) > 0f) food.Add(i);
                if (HealthAmount[i] > 0f) health.Add(i);
                if (FaithAmount[i] > 0f) faith.Add(i);
                var o = d.OutputsPerWorker;
                if (o != null) for (int k = 0; k < o.Length; k++) if (o[k].Amount > 0f && !prod[(int)o[k].Type].Contains(i)) prod[(int)o[k].Type].Add(i);
            }
            Water = water.ToArray(); Walls = walls.ToArray(); Storage = storage.ToArray();
            Food = food.ToArray(); Health = health.ToArray(); Faith = faith.ToArray();
            Producers = new int[Counts.Resources][];
            for (int r = 0; r < prod.Length; r++) Producers[r] = prod[r].ToArray();
        }

        /// <summary>Rebuild priority: Town Center, then dwellings, food, storage, everything else.</summary>
        public static RecoveryTier TierOf(BuildingDefinition d)
        {
            if (d.Type == BuildingType.TownCenter) return RecoveryTier.TownCenter;
            if (d.IsDwelling) return RecoveryTier.Dwelling;
            if (d.Category == BuildingCategory.Food || OutputAmount(d, ResourceType.Food) > 0f) return RecoveryTier.Food;
            if (d.Category == BuildingCategory.Storage || (d.StorageCapacity > 0f && !d.HasJobs)) return RecoveryTier.Storage;
            return RecoveryTier.Other;
        }

        public static float ServiceAmount(BuildingDefinition d, ServiceType t)
        {
            float a = 0f;
            var s = d.Services;
            if (s != null) for (int i = 0; i < s.Length; i++) if (s[i].Type == t) a += MathUtil.Sanitize(s[i].Amount);
            return a;
        }

        public static float OutputAmount(BuildingDefinition d, ResourceType r)
        {
            float a = 0f;
            var o = d.OutputsPerWorker;
            if (o != null) for (int i = 0; i < o.Length; i++) if (o[i].Type == r) a += MathUtil.Sanitize(o[i].Amount);
            return a * (d.JobSlots > 0 ? d.JobSlots : 1);
        }

        /// <summary>True if two definitions fill the same role (shared output, service, housing or storage).</summary>
        public static bool SameRole(BuildingDefinition a, BuildingDefinition b)
        {
            if (a.IsDwelling && b.IsDwelling) return true;
            if (a.StorageCapacity > 0f && b.StorageCapacity > 0f && a.Category == BuildingCategory.Storage && b.Category == BuildingCategory.Storage) return true;
            var oa = a.OutputsPerWorker;
            if (oa != null) for (int i = 0; i < oa.Length; i++) if (oa[i].Amount > 0f && OutputAmount(b, oa[i].Type) > 0f) return true;
            var sa = a.Services;
            if (sa != null) for (int i = 0; i < sa.Length; i++) if (sa[i].Amount > 0f && ServiceAmount(b, sa[i].Type) > 0f) return true;
            return false;
        }

        /// <summary>True when every hard placement requirement of the definition is present in the feature mask.</summary>
        public static bool PlacementViable(BuildingDefinition d, int featureMask)
        {
            int need = (int)d.Placement & HardPlacementMask;
            return (need & ~featureMask) == 0;
        }

        /// <summary>Scans the territory once and returns the PlacementRule bits its terrain can satisfy.</summary>
        public static int ScanFeatures(WorldMap map, Vec2i center, int radius)
        {
            if (map == null) return 0;
            radius = MathUtil.Clamp(radius, 3, 16);
            int mask = 0;
            for (int y = center.Y - radius; y <= center.Y + radius; y++)
                for (int x = center.X - radius; x <= center.X + radius; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    var f = map.Flags[i];
                    var b = map.Biomes[i];
                    if (b.IsWater()) mask |= (int)PlacementRule.NearWater;
                    if ((f & TileFlags.River) != 0) mask |= (int)(PlacementRule.NearWater | PlacementRule.NearRiver);
                    if ((f & TileFlags.Coastal) != 0) mask |= (int)(PlacementRule.NearWater | PlacementRule.Coastal);
                    if (b == Biome.Hills) mask |= (int)PlacementRule.OnHills;
                    if (b == Biome.Mountain) mask |= (int)PlacementRule.NearMountain;
                    if ((f & TileFlags.Trees) != 0) mask |= (int)PlacementRule.NearForest;
                    if ((f & TileFlags.OreDeposit) != 0) mask |= (int)PlacementRule.NearOre;
                    if ((f & TileFlags.StoneDeposit) != 0) mask |= (int)PlacementRule.NearStone;
                    if ((f & TileFlags.GoldDeposit) != 0) mask |= (int)PlacementRule.NearGold;
                    if ((f & TileFlags.CoalDeposit) != 0) mask |= (int)PlacementRule.NearCoal;
                    if ((f & TileFlags.OilDeposit) != 0) mask |= (int)PlacementRule.NearOil;
                    if ((f & TileFlags.FertileSoil) != 0 || map.Fertility[i] >= 0.5f) mask |= (int)PlacementRule.Fertile;
                }
            return mask;
        }
    }
}
