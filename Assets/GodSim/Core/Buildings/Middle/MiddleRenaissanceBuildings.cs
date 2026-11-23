using static GodSim.Core.Buildings.MiddleBuildingKit;

namespace GodSim.Core.Buildings
{
    /// <summary>Renaissance-age building definitions (BuildingType 43..47): banking, universities, printing.</summary>
    internal static class MiddleRenaissanceBuildings
    {
        /// <summary>University: Scholar 5, Research 7; one per settlement; Town tier.</summary>
        internal static BuildingDefinition University() => new BuildingDefinition
        {
            Type = BuildingType.University,
            Name = "University",
            Description = "Quadrangles of lecture halls, libraries and dormitories where masters of law, medicine and natural philosophy train the minds of a new age.",
            Category = BuildingCategory.Education,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Renaissance,
            MinTier = SettlementTier.Town,
            MinPopulation = 60,
            MaxPerSettlement = 1,
            Width = 3, Height = 2,
            Cost = new[]
            {
                Res(ResourceType.Stone, 120f), Res(ResourceType.Wood, 40f), Res(ResourceType.Gold, 40f),
                Res(ResourceType.Goods, 15f), Res(ResourceType.Cloth, 10f)
            },
            BuildWork = 320f,
            Job = JobType.Scholar, JobSlots = 5,
            Services = new[] { Svc(ServiceType.Research, 7f) },
            ServiceRadius = 18,
            UpkeepGold = 0.05f,
            Placement = PlacementRule.Central,
            Desirability = 0.5f,
            VisualHeight = 2.0f,
        };

        /// <summary>Bank: Merchant 2, Commerce 5 + 0.04 Gold per worker; Town tier.</summary>
        internal static BuildingDefinition Bank() => new BuildingDefinition
        {
            Type = BuildingType.Bank,
            Name = "Bank",
            Description = "Behind marble columns and iron-bound vaults, bankers lend to princes, insure merchant fleets and make gold breed more gold.",
            Category = BuildingCategory.Commerce,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Renaissance,
            MinTier = SettlementTier.Town,
            MinPopulation = 60,
            Width = 2, Height = 2,
            Cost = new[] { Res(ResourceType.Stone, 80f), Res(ResourceType.Wood, 20f), Res(ResourceType.Metal, 10f), Res(ResourceType.Gold, 50f) },
            BuildWork = 220f,
            Job = JobType.Merchant, JobSlots = 2,
            OutputsPerWorker = new[] { Res(ResourceType.Gold, 0.04f) },
            Services = new[] { Svc(ServiceType.Commerce, 5f) },
            ServiceRadius = 16,
            StorageCapacity = 50f,
            UpkeepGold = 0.03f,
            Placement = PlacementRule.Central,
            Desirability = 0.3f,
            VisualHeight = 1.6f,
        };

        /// <summary>
        /// Observatory: Scholar 2, Research 4; prefers the settlement edge.
        /// <para>
        /// The brief says it only <i>prefers</i> hills, but <see cref="PlacementRule.OnHills"/> is a HARD
        /// requirement in the contract (the planner rejects any site whose footprint is not mostly Hills),
        /// so it is deliberately not set: otherwise forest, swamp and grassland civilizations could never
        /// build one. The hilltop flavour lives in the description until the contract offers a soft hill
        /// preference.
        /// </para>
        /// </summary>
        internal static BuildingDefinition Observatory() => new BuildingDefinition
        {
            Type = BuildingType.Observatory,
            Name = "Observatory",
            Description = "A domed tower on high ground with a great brass telescope, from which astronomers chart the stars and the wandering lights of the heavens.",
            Category = BuildingCategory.Education,
            Shape = BuildingShape.Dome,
            MinAge = AgeId.Renaissance,
            MinTier = SettlementTier.Village,
            MinPopulation = 35,
            Width = 2, Height = 2,
            Cost = new[]
            {
                Res(ResourceType.Stone, 60f), Res(ResourceType.Wood, 20f), Res(ResourceType.Metal, 15f),
                Res(ResourceType.Tools, 10f), Res(ResourceType.Goods, 5f)
            },
            BuildWork = 200f,
            Job = JobType.Scholar, JobSlots = 2,
            Services = new[] { Svc(ServiceType.Research, 4f) },
            ServiceRadius = 14,
            UpkeepGold = 0.03f,
            Placement = PlacementRule.Outskirts,
            Desirability = 0.2f,
            VisualHeight = 2.2f,
        };

        /// <summary>Hospital: Healer 4, Health 8.</summary>
        internal static BuildingDefinition Hospital() => new BuildingDefinition
        {
            Type = BuildingType.Hospital,
            Name = "Hospital",
            Description = "Long wards of clean beds tended by physicians and nurses, with an herb garden, an apothecary and a surgeon's theatre.",
            Category = BuildingCategory.Health,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Renaissance,
            MinTier = SettlementTier.Village,
            MinPopulation = 40,
            Width = 2, Height = 2,
            Cost = new[]
            {
                Res(ResourceType.Stone, 70f), Res(ResourceType.Wood, 30f), Res(ResourceType.Cloth, 20f),
                Res(ResourceType.Tools, 10f), Res(ResourceType.Gold, 20f)
            },
            BuildWork = 240f,
            Job = JobType.Healer, JobSlots = 4,
            Services = new[] { Svc(ServiceType.Health, 8f) },
            ServiceRadius = 16,
            UpkeepGold = 0.04f,
            Placement = PlacementRule.Central,
            Desirability = 0.3f,
            VisualHeight = 1.6f,
        };

        /// <summary>Printworks: Craftsman 2, 0.02 Wood -> 0.02 Goods per worker + Research 1.5.</summary>
        internal static BuildingDefinition Printworks() => new BuildingDefinition
        {
            Type = BuildingType.Printworks,
            Name = "Printworks",
            Description = "Clattering presses, trays of cast type and the smell of ink; pamphlets, almanacs and books pour out to spread ideas faster than any scribe.",
            Category = BuildingCategory.Education,
            Shape = BuildingShape.Workshop,
            MinAge = AgeId.Renaissance,
            MinTier = SettlementTier.Village,
            MinPopulation = 30,
            Width = 2, Height = 2,
            Cost = new[] { Res(ResourceType.Wood, 40f), Res(ResourceType.Stone, 30f), Res(ResourceType.Metal, 20f), Res(ResourceType.Tools, 12f) },
            BuildWork = 200f,
            Job = JobType.Craftsman, JobSlots = 2,
            InputsPerWorker = new[] { Res(ResourceType.Wood, 0.02f) },
            OutputsPerWorker = new[] { Res(ResourceType.Goods, 0.02f) },
            Services = new[] { Svc(ServiceType.Research, 1.5f) },
            ServiceRadius = 14,
            StorageCapacity = 20f,
            UpkeepGold = 0.02f,
            Placement = PlacementRule.None,
            Desirability = -0.05f,
            VisualHeight = 1.2f,
        };
    }
}
