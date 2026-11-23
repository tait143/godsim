using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Data-driven building "slots" for the <see cref="FrontierAdvisor"/>, derived once per <see cref="GameDatabase"/>
    /// from <see cref="BuildingDefinition"/> fields (category, shape, services, jobs, placement, outputs, inputs).
    /// No building is hard-coded: a modded watchtower or harbor is picked up automatically. Lists are ordered by
    /// MinAge then enum value, so iteration is deterministic.
    /// </summary>
    public sealed class FrontierCatalog
    {
        /// <summary>Lookout towers: military, defensive, few or no soldiers, not a wall.</summary>
        public const int SlotWatch = 0;
        /// <summary>Fortification segments (palisade, stone wall).</summary>
        public const int SlotWall = 1;
        /// <summary>Soldier halls (barracks, castle, military base).</summary>
        public const int SlotGarrison = 2;
        /// <summary>Inland commerce (market, guildhall...).</summary>
        public const int SlotTrade = 3;
        /// <summary>Food from water (fishing hut...).</summary>
        public const int SlotFishing = 4;
        /// <summary>Coastal commerce (harbor, lighthouse-like ports).</summary>
        public const int SlotPort = 5;
        /// <summary>Coastal wonders (colossus).</summary>
        public const int SlotPortWonder = 6;
        /// <summary>General storage (storehouse, granary).</summary>
        public const int SlotStorage = 7;
        /// <summary>Wild food near forests (hunting lodge).</summary>
        public const int SlotWildFood = 8;
        /// <summary>Dwellings.</summary>
        public const int SlotHousing = 9;
        /// <summary>Number of slots.</summary>
        public const int SlotCount = 10;

        /// <summary>Deposit kinds: 0 ore, 1 stone, 2 gold, 3 coal, 4 oil, 5 trees.</summary>
        public const int KindOre = 0, KindStone = 1, KindGold = 2, KindCoal = 3, KindOil = 4, KindTrees = 5, KindCount = 6;

        /// <summary>Tile flag per deposit kind.</summary>
        public static readonly TileFlags[] KindFlags = { TileFlags.OreDeposit, TileFlags.StoneDeposit, TileFlags.GoldDeposit, TileFlags.CoalDeposit, TileFlags.OilDeposit, TileFlags.Trees };
        /// <summary>Placement rule an extractor of each kind declares.</summary>
        public static readonly PlacementRule[] KindRules = { PlacementRule.NearOre, PlacementRule.NearStone, PlacementRule.NearGold, PlacementRule.NearCoal, PlacementRule.NearOil, PlacementRule.NearForest };
        /// <summary>Display names per kind.</summary>
        public static readonly string[] KindNames = { "ore", "stone", "gold", "coal", "oil", "timber" };

        /// <summary>The database these lists were built from.</summary>
        public readonly GameDatabase Db;
        /// <summary>Building types per slot.</summary>
        public readonly List<BuildingType>[] Slots = new List<BuildingType>[SlotCount];
        /// <summary>Extractors per deposit kind.</summary>
        public readonly List<BuildingType>[] Extractors = new List<BuildingType>[KindCount];
        /// <summary>Industry buildings consuming each resource (processing chain for resource towns).</summary>
        public readonly List<BuildingType>[] Processors;
        /// <summary>Lower-case display names per building type (for reasons).</summary>
        public readonly string[] LowerNames = new string[Counts.Buildings];

        /// <summary>Builds the slot lists from the database definitions.</summary>
        public FrontierCatalog(GameDatabase db)
        {
            Db = db;
            for (int i = 0; i < SlotCount; i++) Slots[i] = new List<BuildingType>();
            for (int i = 0; i < KindCount; i++) Extractors[i] = new List<BuildingType>();
            Processors = new List<BuildingType>[Counts.Resources];
            for (int i = 0; i < Processors.Length; i++) Processors[i] = new List<BuildingType>();
            if (db == null) return;

            for (int age = 0; age < Counts.Ages; age++)
            {
                for (int t = 0; t < Counts.Buildings; t++)
                {
                    if (!db.TryGetBuilding((BuildingType)t, out var d)) continue;
                    if ((int)d.MinAge != age) continue;
                    Classify(d);
                }
            }
            for (int t = 0; t < Counts.Buildings; t++)
                LowerNames[t] = db.TryGetBuilding((BuildingType)t, out var d) && !string.IsNullOrEmpty(d.Name) ? d.Name.ToLowerInvariant() : ((BuildingType)t).ToString().ToLowerInvariant();
        }

        private void Classify(BuildingDefinition d)
        {
            var type = d.Type;
            if (type == BuildingType.TownCenter) return;
            bool defense = HasService(d, ServiceType.Defense);
            bool commerce = HasService(d, ServiceType.Commerce);
            bool coastal = (d.Placement & PlacementRule.Coastal) != 0;
            bool waterside = (d.Placement & (PlacementRule.Coastal | PlacementRule.NearWater | PlacementRule.NearRiver)) != 0;
            bool soldiers = d.Job == JobType.Soldier && d.JobSlots > 0;

            if (d.Shape == BuildingShape.Wall && defense) Slots[SlotWall].Add(type);
            else if (d.Category == BuildingCategory.Military && defense && !d.IsWonder && d.JobSlots <= 2) Slots[SlotWatch].Add(type);
            if (soldiers && d.JobSlots >= 3 && !d.IsWonder) Slots[SlotGarrison].Add(type);

            if (coastal && d.IsWonder && (commerce || d.Category == BuildingCategory.Commerce)) Slots[SlotPortWonder].Add(type);
            else if (coastal && !d.IsWonder && (commerce || d.Category == BuildingCategory.Commerce)) Slots[SlotPort].Add(type);
            else if (!coastal && !d.IsWonder && commerce && d.Category == BuildingCategory.Commerce) Slots[SlotTrade].Add(type);

            if (!d.IsWonder && (d.Job == JobType.Fisher || (d.Category == BuildingCategory.Food && waterside && Outputs(d, ResourceType.Food))))
                Slots[SlotFishing].Add(type);
            if (!d.IsWonder && d.Category == BuildingCategory.Storage && d.StorageCapacity > 0f) Slots[SlotStorage].Add(type);
            if (!d.IsWonder && d.Category == BuildingCategory.Food && d.HasJobs && (d.Placement & PlacementRule.NearForest) != 0) Slots[SlotWildFood].Add(type);
            if (d.IsDwelling && !d.IsWonder) Slots[SlotHousing].Add(type);

            if (d.Category == BuildingCategory.Resource && d.HasJobs && !d.IsWonder)
                for (int k = 0; k < KindCount; k++)
                    if ((d.Placement & KindRules[k]) != 0) Extractors[k].Add(type);

            if (d.Category == BuildingCategory.Industry && d.HasJobs && !d.IsWonder && d.InputsPerWorker != null)
                for (int i = 0; i < d.InputsPerWorker.Length; i++)
                {
                    int r = (int)d.InputsPerWorker[i].Type;
                    if (r >= 0 && r < Processors.Length && d.InputsPerWorker[i].Amount > 0f && !Processors[r].Contains(type)) Processors[r].Add(type);
                }
        }

        /// <summary>The main raw resource an extractor yields (first positive output), or -1.</summary>
        public int MainOutput(BuildingType type)
        {
            if (Db == null || !Db.TryGetBuilding(type, out var d) || d.OutputsPerWorker == null) return -1;
            for (int i = 0; i < d.OutputsPerWorker.Length; i++)
                if (d.OutputsPerWorker[i].Amount > 0f) return (int)d.OutputsPerWorker[i].Type;
            return -1;
        }

        private static bool HasService(BuildingDefinition d, ServiceType svc)
        {
            if (d.Services == null) return false;
            for (int i = 0; i < d.Services.Length; i++) if (d.Services[i].Type == svc && d.Services[i].Amount > 0f) return true;
            return false;
        }

        private static bool Outputs(BuildingDefinition d, ResourceType res)
        {
            if (d.OutputsPerWorker == null) return false;
            for (int i = 0; i < d.OutputsPerWorker.Length; i++) if (d.OutputsPerWorker[i].Type == res && d.OutputsPerWorker[i].Amount > 0f) return true;
            return false;
        }
    }
}
