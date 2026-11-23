using static GodSim.Core.Buildings.MiddleBuildingKit;

namespace GodSim.Core.Buildings
{
    /// <summary>Classical-age building definitions (BuildingType 28..36): philosophy, aqueducts, theaters, markets.</summary>
    internal static class MiddleClassicalBuildings
    {
        /// <summary>Library: Scholar 3, Research 3.</summary>
        internal static BuildingDefinition Library() => new BuildingDefinition
        {
            Type = BuildingType.Library,
            Name = "Library",
            Description = "Colonnaded halls of shelved scrolls and copied codices where scholars argue over the nature of the world and the will of the gods.",
            Category = BuildingCategory.Education,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Classical,
            MinTier = SettlementTier.Village,
            MinPopulation = 25,
            Width = 2, Height = 2,
            Cost = new[] { Res(ResourceType.Wood, 30f), Res(ResourceType.Stone, 40f), Res(ResourceType.Cloth, 5f), Res(ResourceType.Gold, 10f) },
            BuildWork = 140f,
            Job = JobType.Scholar, JobSlots = 3,
            Services = new[] { Svc(ServiceType.Research, 3f) },
            ServiceRadius = 14,
            UpkeepGold = 0.02f,
            Placement = PlacementRule.Central,
            Desirability = 0.3f,
            VisualHeight = 1.4f,
        };

        /// <summary>Aqueduct: Water 10, no workers.</summary>
        internal static BuildingDefinition Aqueduct() => new BuildingDefinition
        {
            Type = BuildingType.Aqueduct,
            Name = "Aqueduct",
            Description = "Tall stone arcades carry fresh water from distant springs into the heart of the town, feeding fountains, baths and gardens.",
            Category = BuildingCategory.Infrastructure,
            Shape = BuildingShape.Wall,
            MinAge = AgeId.Classical,
            MinTier = SettlementTier.Village,
            MinPopulation = 30,
            Width = 1, Height = 3,
            Cost = new[] { Res(ResourceType.Stone, 60f), Res(ResourceType.Wood, 10f), Res(ResourceType.Tools, 5f) },
            BuildWork = 160f,
            Services = new[] { Svc(ServiceType.Water, 10f) },
            ServiceRadius = 18,
            UpkeepGold = 0.02f,
            Placement = PlacementRule.None,
            Desirability = 0.2f,
            VisualHeight = 1.6f,
        };

        /// <summary>Bathhouse: Healer 1, Health 3 + Water 2.</summary>
        internal static BuildingDefinition Bathhouse() => new BuildingDefinition
        {
            Type = BuildingType.Bathhouse,
            Name = "Bathhouse",
            Description = "Domed pools of hot, warm and cold water where citizens wash away grime, sickness and gossip in equal measure.",
            Category = BuildingCategory.Health,
            Shape = BuildingShape.Dome,
            MinAge = AgeId.Classical,
            MinTier = SettlementTier.Village,
            MinPopulation = 20,
            Width = 2, Height = 2,
            Cost = new[] { Res(ResourceType.Stone, 35f), Res(ResourceType.Wood, 15f), Res(ResourceType.Tools, 4f) },
            BuildWork = 120f,
            Job = JobType.Healer, JobSlots = 1,
            Services = new[] { Svc(ServiceType.Health, 3f), Svc(ServiceType.Water, 2f) },
            ServiceRadius = 12,
            UpkeepGold = 0.02f,
            Placement = PlacementRule.Central,
            Desirability = 0.4f,
            VisualHeight = 1.1f,
        };

        /// <summary>Theater: Entertainer 2, Entertainment 4.</summary>
        internal static BuildingDefinition Theater() => new BuildingDefinition
        {
            Type = BuildingType.Theater,
            Name = "Theater",
            Description = "A half-ring of stone seats facing a painted stage, where masked players perform tragedies of fallen kings and comedies of foolish gods.",
            Category = BuildingCategory.Culture,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Classical,
            MinTier = SettlementTier.Village,
            MinPopulation = 25,
            Width = 2, Height = 2,
            Cost = new[] { Res(ResourceType.Wood, 25f), Res(ResourceType.Stone, 35f), Res(ResourceType.Cloth, 8f) },
            BuildWork = 130f,
            Job = JobType.Entertainer, JobSlots = 2,
            Services = new[] { Svc(ServiceType.Entertainment, 4f) },
            ServiceRadius = 14,
            UpkeepGold = 0.02f,
            Placement = PlacementRule.Central,
            Desirability = 0.4f,
            VisualHeight = 1.3f,
        };

        /// <summary>Harbor: Merchant 3, Food 0.08 per worker + Commerce 3; coastal only.</summary>
        internal static BuildingDefinition Harbor() => new BuildingDefinition
        {
            Type = BuildingType.Harbor,
            Name = "Harbor",
            Description = "Stone quays and timber piers where fishing fleets unload their catch and merchant galleys trade wine, oil and news from distant shores.",
            Category = BuildingCategory.Commerce,
            Shape = BuildingShape.Dock,
            MinAge = AgeId.Classical,
            MinTier = SettlementTier.Village,
            MinPopulation = 20,
            MaxPerSettlement = 2,
            Width = 2, Height = 2,
            Cost = new[] { Res(ResourceType.Wood, 60f), Res(ResourceType.Stone, 20f), Res(ResourceType.Tools, 6f), Res(ResourceType.Cloth, 6f) },
            BuildWork = 180f,
            Job = JobType.Merchant, JobSlots = 3,
            OutputsPerWorker = new[] { Res(ResourceType.Food, 0.08f) },
            Services = new[] { Svc(ServiceType.Commerce, 3f) },
            ServiceRadius = 14,
            StorageCapacity = 100f,
            UpkeepGold = 0.03f,
            Placement = PlacementRule.Coastal,
            Desirability = -0.05f,
            VisualHeight = 1.0f,
        };

        /// <summary>Workshop: Craftsman 3, 0.03 Wood + 0.02 Cloth -> 0.04 Goods per worker.</summary>
        internal static BuildingDefinition Workshop() => new BuildingDefinition
        {
            Type = BuildingType.Workshop,
            Name = "Workshop",
            Description = "A smoky yard of lathes, looms and workbenches where craftsmen turn timber and cloth into furniture, pottery and fine wares.",
            Category = BuildingCategory.Industry,
            Shape = BuildingShape.Workshop,
            MinAge = AgeId.Classical,
            MaxAge = AgeId.Modern,
            MinTier = SettlementTier.Hamlet,
            MinPopulation = 12,
            Width = 2, Height = 2,
            Cost = new[] { Res(ResourceType.Wood, 30f), Res(ResourceType.Stone, 15f), Res(ResourceType.Tools, 8f) },
            BuildWork = 100f,
            Job = JobType.Craftsman, JobSlots = 3,
            InputsPerWorker = new[] { Res(ResourceType.Wood, 0.03f), Res(ResourceType.Cloth, 0.02f) },
            OutputsPerWorker = new[] { Res(ResourceType.Goods, 0.04f) },
            ServiceRadius = 8,
            StorageCapacity = 30f,
            UpkeepGold = 0.015f,
            Placement = PlacementRule.Isolated,
            Desirability = -0.2f,
            VisualHeight = 1.0f,
        };

        /// <summary>Forum: Administrator 2, Administration 6; one per settlement.</summary>
        internal static BuildingDefinition Forum() => new BuildingDefinition
        {
            Type = BuildingType.Forum,
            Name = "Forum",
            Description = "A paved square ringed by porticoes and the council house, where magistrates hear disputes, tally taxes and proclaim the laws of the realm.",
            Category = BuildingCategory.Civic,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Classical,
            MinTier = SettlementTier.Village,
            MinPopulation = 30,
            MaxPerSettlement = 1,
            Width = 3, Height = 2,
            Cost = new[] { Res(ResourceType.Stone, 60f), Res(ResourceType.Wood, 20f), Res(ResourceType.Gold, 15f) },
            BuildWork = 180f,
            Job = JobType.Administrator, JobSlots = 2,
            Services = new[] { Svc(ServiceType.Administration, 6f) },
            ServiceRadius = 16,
            UpkeepGold = 0.03f,
            Placement = PlacementRule.Central,
            Desirability = 0.5f,
            VisualHeight = 1.2f,
        };

        /// <summary>Arena: Entertainer 3, Entertainment 6.</summary>
        internal static BuildingDefinition Arena() => new BuildingDefinition
        {
            Type = BuildingType.Arena,
            Name = "Arena",
            Description = "A great oval of tiered stone where roaring crowds watch chariot races, beast hunts and champions duelling for glory.",
            Category = BuildingCategory.Culture,
            Shape = BuildingShape.Dome,
            MinAge = AgeId.Classical,
            MinTier = SettlementTier.Village,
            MinPopulation = 45,
            Width = 3, Height = 3,
            Cost = new[] { Res(ResourceType.Stone, 90f), Res(ResourceType.Wood, 30f), Res(ResourceType.Tools, 8f), Res(ResourceType.Gold, 20f) },
            BuildWork = 260f,
            Job = JobType.Entertainer, JobSlots = 3,
            Services = new[] { Svc(ServiceType.Entertainment, 6f) },
            ServiceRadius = 18,
            UpkeepGold = 0.04f,
            Placement = PlacementRule.Outskirts,
            Desirability = 0.1f,
            VisualHeight = 1.6f,
        };

        /// <summary>Max wall segments one settlement may hold; bounds stacked Defense (4 each) and upkeep.</summary>
        internal const int MaxStoneWallSegments = 24;

        /// <summary>
        /// Stone Wall: Defense 4 per segment, no workers; at most <see cref="MaxStoneWallSegments"/> per
        /// settlement; obsolete after the Industrial age. Priced so a segment's Defense per unit of cost
        /// stays below twice a Castle's (walls are efficient but give no garrison or administration).
        /// </summary>
        internal static BuildingDefinition StoneWall() => new BuildingDefinition
        {
            Type = BuildingType.StoneWall,
            Name = "Stone Wall",
            Description = "A segment of mortared curtain wall with a walkway and crenellations; chained together they turn a town into a fortress.",
            Category = BuildingCategory.Military,
            Shape = BuildingShape.Wall,
            MinAge = AgeId.Classical,
            MaxAge = AgeId.Industrial,
            MinTier = SettlementTier.Hamlet,
            MinPopulation = 10,
            MaxPerSettlement = MaxStoneWallSegments,
            Width = 1, Height = 1,
            Cost = new[] { Res(ResourceType.Stone, 30f), Res(ResourceType.Tools, 2f), Res(ResourceType.Gold, 2f) },
            BuildWork = 100f,
            Services = new[] { Svc(ServiceType.Defense, 4f) },
            ServiceRadius = 10,
            UpkeepGold = 0.01f,
            Placement = PlacementRule.Outskirts,
            Desirability = 0f,
            VisualHeight = 1.2f,
            TintColor = Tint(0xB8B0A0),
        };
    }
}
