using static GodSim.Core.Buildings.MiddleBuildingKit;

namespace GodSim.Core.Buildings
{
    /// <summary>Medieval-age building definitions (BuildingType 37..42): castles, cathedrals, guilds.</summary>
    internal static class MiddleMedievalBuildings
    {
        /// <summary>Castle: Soldier 10, Defense 20 + Administration 3; one per settlement; obsolete after the Renaissance.</summary>
        internal static BuildingDefinition Castle() => new BuildingDefinition
        {
            Type = BuildingType.Castle,
            Name = "Castle",
            Description = "A towering keep within concentric walls, seat of the local lord and home of his garrison; its banners can be seen for leagues.",
            Category = BuildingCategory.Military,
            Shape = BuildingShape.Tower,
            MinAge = AgeId.Medieval,
            MaxAge = AgeId.Renaissance,
            MinTier = SettlementTier.Village,
            MinPopulation = 40,
            MaxPerSettlement = 1,
            Width = 3, Height = 3,
            Cost = new[]
            {
                Res(ResourceType.Stone, 200f), Res(ResourceType.Wood, 60f), Res(ResourceType.Metal, 20f),
                Res(ResourceType.Tools, 15f), Res(ResourceType.Gold, 30f)
            },
            BuildWork = 400f,
            Job = JobType.Soldier, JobSlots = 10,
            Services = new[] { Svc(ServiceType.Defense, 20f), Svc(ServiceType.Administration, 3f) },
            ServiceRadius = 16,
            StorageCapacity = 80f,
            UpkeepGold = 0.05f,
            Placement = PlacementRule.Central,
            Desirability = 0.1f,
            VisualHeight = 2.6f,
            TintColor = Tint(0xA8A296),
        };

        /// <summary>Cathedral: Priest 4, Faith 8; one per settlement; Town tier.</summary>
        internal static BuildingDefinition Cathedral() => new BuildingDefinition
        {
            Type = BuildingType.Cathedral,
            Name = "Cathedral",
            Description = "Generations of masons raised this vaulted nave and its soaring spire; stained glass bathes the faithful in coloured light during every hymn.",
            Category = BuildingCategory.Religion,
            Shape = BuildingShape.Spire,
            MinAge = AgeId.Medieval,
            MinTier = SettlementTier.Town,
            MinPopulation = 60,
            MaxPerSettlement = 1,
            Width = 3, Height = 3,
            Cost = new[]
            {
                Res(ResourceType.Stone, 180f), Res(ResourceType.Wood, 50f), Res(ResourceType.Gold, 40f),
                Res(ResourceType.Goods, 10f), Res(ResourceType.Cloth, 10f)
            },
            BuildWork = 380f,
            Job = JobType.Priest, JobSlots = 4,
            Services = new[] { Svc(ServiceType.Faith, 8f) },
            ServiceRadius = 18,
            UpkeepGold = 0.04f,
            Placement = PlacementRule.Central,
            Desirability = 0.6f,
            VisualHeight = 3.0f,
        };

        /// <summary>Monastery: Priest 3, Faith 3 + Research 1.5.</summary>
        internal static BuildingDefinition Monastery() => new BuildingDefinition
        {
            Type = BuildingType.Monastery,
            Name = "Monastery",
            Description = "A quiet cloister of cells, gardens and a scriptorium where monks pray at every bell and preserve the learning of older ages.",
            Category = BuildingCategory.Religion,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Medieval,
            MinTier = SettlementTier.Hamlet,
            MinPopulation = 15,
            Width = 2, Height = 2,
            Cost = new[] { Res(ResourceType.Wood, 40f), Res(ResourceType.Stone, 50f), Res(ResourceType.Tools, 5f) },
            BuildWork = 170f,
            Job = JobType.Priest, JobSlots = 3,
            Services = new[] { Svc(ServiceType.Faith, 3f), Svc(ServiceType.Research, 1.5f) },
            ServiceRadius = 12,
            UpkeepGold = 0.02f,
            Placement = PlacementRule.Outskirts,
            Desirability = 0.3f,
            VisualHeight = 1.5f,
        };

        /// <summary>Mill: Laborer 2, 0.12 Food per worker; needs fertile land by a river; obsolete after the Industrial age.</summary>
        internal static BuildingDefinition Mill() => new BuildingDefinition
        {
            Type = BuildingType.Mill,
            Name = "Mill",
            Description = "A creaking water wheel turns great millstones day and night, grinding the harvest of the surrounding fields into flour for the town's bread.",
            Category = BuildingCategory.Food,
            Shape = BuildingShape.Tower,
            MinAge = AgeId.Medieval,
            MaxAge = AgeId.Industrial,
            MinTier = SettlementTier.Hamlet,
            MinPopulation = 10,
            Width = 2, Height = 2,
            Cost = new[] { Res(ResourceType.Wood, 45f), Res(ResourceType.Stone, 20f), Res(ResourceType.Tools, 6f) },
            BuildWork = 120f,
            Job = JobType.Laborer, JobSlots = 2,
            OutputsPerWorker = new[] { Res(ResourceType.Food, 0.12f) },
            ServiceRadius = 8,
            StorageCapacity = 40f,
            UpkeepGold = 0.015f,
            Placement = PlacementRule.Fertile | PlacementRule.NearRiver,
            Desirability = 0f,
            VisualHeight = 1.8f,
        };

        /// <summary>Guildhall: Merchant 2, Commerce 3.</summary>
        internal static BuildingDefinition Guildhall() => new BuildingDefinition
        {
            Type = BuildingType.Guildhall,
            Name = "Guildhall",
            Description = "A proud timber-framed hall hung with trade banners, where masters set prices, train apprentices and guard the secrets of their crafts.",
            Category = BuildingCategory.Commerce,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Medieval,
            MinTier = SettlementTier.Village,
            MinPopulation = 30,
            Width = 2, Height = 2,
            Cost = new[] { Res(ResourceType.Wood, 40f), Res(ResourceType.Stone, 40f), Res(ResourceType.Gold, 15f), Res(ResourceType.Goods, 5f) },
            BuildWork = 170f,
            Job = JobType.Merchant, JobSlots = 2,
            Services = new[] { Svc(ServiceType.Commerce, 3f) },
            ServiceRadius = 14,
            UpkeepGold = 0.02f,
            Placement = PlacementRule.Central,
            Desirability = 0.3f,
            VisualHeight = 1.5f,
        };

        /// <summary>Tavern: Merchant 1, Entertainment 3 + Commerce 1.</summary>
        internal static BuildingDefinition Tavern() => new BuildingDefinition
        {
            Type = BuildingType.Tavern,
            Name = "Tavern",
            Description = "Low beams, a roaring hearth and barrels of ale; travellers swap rumours, minstrels sing and deals are sealed over a shared cup.",
            Category = BuildingCategory.Culture,
            Shape = BuildingShape.House,
            MinAge = AgeId.Medieval,
            MinTier = SettlementTier.Hamlet,
            MinPopulation = 10,
            Width = 2, Height = 2,
            Cost = new[] { Res(ResourceType.Wood, 35f), Res(ResourceType.Stone, 10f), Res(ResourceType.Goods, 4f) },
            BuildWork = 80f,
            Job = JobType.Merchant, JobSlots = 1,
            Services = new[] { Svc(ServiceType.Entertainment, 3f), Svc(ServiceType.Commerce, 1f) },
            ServiceRadius = 10,
            StorageCapacity = 20f,
            UpkeepGold = 0.01f,
            Placement = PlacementRule.None,
            Desirability = 0.1f,
            VisualHeight = 1.1f,
        };
    }
}
