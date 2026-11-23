using static GodSim.Core.Buildings.MiddleBuildingKit;

namespace GodSim.Core.Buildings
{
    /// <summary>
    /// Shared (all-race) wonders of the middle ages (BuildingType 72..74). Wonders are unique per
    /// civilization, occupy a 3x3 footprint and cost several times a regular building.
    /// </summary>
    internal static class MiddleWonderBuildings
    {
        /// <summary>Great Library: Scholar 4, Research 25.</summary>
        internal static BuildingDefinition GreatLibrary() => new BuildingDefinition
        {
            Type = BuildingType.GreatLibrary,
            Name = "Great Library",
            Description = "A wonder of the world: endless galleries holding a copy of every scroll ever written, where sages from every nation gather to learn.",
            Category = BuildingCategory.Wonder,
            Shape = BuildingShape.Monument,
            MinAge = AgeId.Classical,
            MinTier = SettlementTier.Town,
            MinPopulation = 80,
            MaxPerSettlement = 1,
            UniquePerCivilization = true,
            IsWonder = true,
            Width = 3, Height = 3,
            Cost = new[]
            {
                Res(ResourceType.Stone, 400f), Res(ResourceType.Wood, 150f), Res(ResourceType.Gold, 120f),
                Res(ResourceType.Cloth, 40f), Res(ResourceType.Goods, 40f), Res(ResourceType.Tools, 30f)
            },
            BuildWork = 750f,
            Job = JobType.Scholar, JobSlots = 4,
            Services = new[] { Svc(ServiceType.Research, 25f) },
            ServiceRadius = 24,
            UpkeepGold = 0.05f,
            Placement = PlacementRule.Central,
            Desirability = 0.8f,
            VisualHeight = 3.0f,
            TintColor = Tint(0xF2E6C8),
        };

        /// <summary>Colossus: Commerce 20, no workers; coastal only.</summary>
        internal static BuildingDefinition Colossus() => new BuildingDefinition
        {
            Type = BuildingType.Colossus,
            Name = "Colossus",
            Description = "A wonder of the world: a titanic bronze god straddles the harbor mouth, its torch guiding ships and its gaze drawing traders from every coast.",
            Category = BuildingCategory.Wonder,
            Shape = BuildingShape.Monument,
            MinAge = AgeId.Classical,
            MinTier = SettlementTier.Town,
            MinPopulation = 70,
            MaxPerSettlement = 1,
            UniquePerCivilization = true,
            IsWonder = true,
            Width = 3, Height = 3,
            Cost = new[]
            {
                Res(ResourceType.Stone, 350f), Res(ResourceType.Metal, 200f), Res(ResourceType.Tools, 50f),
                Res(ResourceType.Gold, 120f)
            },
            BuildWork = 700f,
            Services = new[] { Svc(ServiceType.Commerce, 20f) },
            ServiceRadius = 24,
            UpkeepGold = 0.05f,
            Placement = PlacementRule.Coastal,
            Desirability = 0.7f,
            VisualHeight = 4.0f,
            TintColor = Tint(0xCD7F32),
        };

        /// <summary>Grand Bazaar: Merchant 4, Commerce 25 + 0.06 Gold per worker.</summary>
        internal static BuildingDefinition GrandBazaar() => new BuildingDefinition
        {
            Type = BuildingType.GrandBazaar,
            Name = "Grand Bazaar",
            Description = "A wonder of the world: a labyrinth of vaulted, lamp-lit arcades where silks, spices and secrets from every civilization change hands.",
            Category = BuildingCategory.Wonder,
            Shape = BuildingShape.Dome,
            MinAge = AgeId.Medieval,
            MinTier = SettlementTier.Town,
            MinPopulation = 90,
            MaxPerSettlement = 1,
            UniquePerCivilization = true,
            IsWonder = true,
            Width = 3, Height = 3,
            Cost = new[]
            {
                Res(ResourceType.Stone, 300f), Res(ResourceType.Wood, 250f), Res(ResourceType.Cloth, 100f),
                Res(ResourceType.Goods, 80f), Res(ResourceType.Gold, 150f)
            },
            BuildWork = 850f,
            Job = JobType.Merchant, JobSlots = 4,
            OutputsPerWorker = new[] { Res(ResourceType.Gold, 0.06f) },
            Services = new[] { Svc(ServiceType.Commerce, 25f) },
            ServiceRadius = 24,
            StorageCapacity = 200f,
            UpkeepGold = 0.05f,
            Placement = PlacementRule.Central,
            Desirability = 0.6f,
            VisualHeight = 2.4f,
            TintColor = Tint(0xE0B060),
        };
    }
}
