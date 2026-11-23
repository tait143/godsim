namespace GodSim.Core.Buildings
{
    // Industrial, Modern, Information and Ascension age buildings (BuildingType 48..66).
    public static partial class BuildingsLate
    {
        // ================================================================== Industrial (Metal/Tools/Stone/Wood/Fuel)

        // Only Machinery producer. Its cost uses only pre-Industrial materials (no Fuel) so it can be paid wherever and
        // whenever it is available: in villages (MinTier Village, like CoalMine/Foundry and every Machinery consumer),
        // in a coal-less settlement whose Fuel will come from an OilWell, and if the tech tree unlocks it before the
        // Industrial age. It still needs Fuel as a per-worker input to run.
        private static BuildingDefinition MakeFactory() => new BuildingDefinition
        {
            Name = "Factory",
            Description = "Smoke-belching halls of belts, boilers and pistons where laborers turn metal and coal into machinery and mass-produced goods. Loud, filthy and indispensable; nobody wants to live beside one.",
            Category = BuildingCategory.Industry,
            Shape = BuildingShape.Factory,
            MinAge = AgeId.Industrial,
            MinTier = SettlementTier.Village,
            MaxPerSettlement = 4,
            Width = 3, Height = 3,
            Cost = Cost(wood: 60f, stone: 80f, metal: 60f, tools: 40f),
            BuildWork = 400f,
            Job = JobType.Laborer, JobSlots = 6,
            InputsPerWorker = Flow(ResourceType.Metal, 0.05f, ResourceType.Fuel, 0.04f),
            OutputsPerWorker = Flow(ResourceType.Machinery, 0.05f, ResourceType.Goods, 0.04f),
            UpkeepGold = 0.08f,
            Placement = PlacementRule.Isolated | PlacementRule.Outskirts,
            Desirability = -0.8f,
            VisualHeight = 1.8f,
            TintColor = 0x8A7F76FFu,
        };

        // First Fuel producer: deliberately costs no Fuel so the coal economy can bootstrap.
        private static BuildingDefinition MakeCoalMine() => new BuildingDefinition
        {
            Name = "Coal Mine",
            Description = "A soot-black headframe and winding engine over shafts sunk into a coal seam. Miners haul up the fuel that feeds every furnace, boiler and locomotive of the age. Must be dug beside a coal deposit.",
            Category = BuildingCategory.Resource,
            Shape = BuildingShape.Tower,
            MinAge = AgeId.Industrial,
            MinTier = SettlementTier.Village,
            MaxPerSettlement = 4,
            Width = 2, Height = 2,
            Cost = Cost(wood: 50f, stone: 30f, metal: 20f, tools: 20f),
            BuildWork = 250f,
            Job = JobType.Miner, JobSlots = 4,
            OutputsPerWorker = Flow(ResourceType.Fuel, 0.10f),
            UpkeepGold = 0.05f,
            Placement = PlacementRule.NearCoal | PlacementRule.Isolated,
            Desirability = -0.8f,
            VisualHeight = 1.6f,
            TintColor = 0x3C3A38FFu,
        };

        private static BuildingDefinition MakeSawmill() => new BuildingDefinition
        {
            Name = "Sawmill",
            Description = "Steam-driven band saws rip whole trunks into planks at a pace no axe crew could ever match. It must stand near woodland to keep its blades fed.",
            Category = BuildingCategory.Resource,
            Shape = BuildingShape.Factory,
            MinAge = AgeId.Industrial,
            MinTier = SettlementTier.Village,
            MaxPerSettlement = 3,
            Width = 2, Height = 2,
            Cost = Cost(wood: 40f, stone: 30f, metal: 15f, tools: 20f),
            BuildWork = 200f,
            Job = JobType.Laborer, JobSlots = 3,
            OutputsPerWorker = Flow(ResourceType.Wood, 0.25f),
            UpkeepGold = 0.04f,
            Placement = PlacementRule.NearForest | PlacementRule.Outskirts,
            Desirability = -0.4f,
            VisualHeight = 1.3f,
            TintColor = 0xA0784EFFu,
        };

        private static BuildingDefinition MakeRailStation() => new BuildingDefinition
        {
            Name = "Rail Station",
            Description = "Iron rails and a vaulted, glass-roofed platform hall bind the town to distant markets. Merchants dispatch freight and travelers daily, and the goods depot holds cargo in transit.",
            Category = BuildingCategory.Commerce,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Industrial,
            MinTier = SettlementTier.Town,
            MaxPerSettlement = 1,
            Width = 3, Height = 3,
            Cost = Cost(wood: 60f, stone: 80f, metal: 60f, tools: 20f, fuel: 20f),
            BuildWork = 450f,
            Job = JobType.Merchant, JobSlots = 3,
            Services = Svc(ServiceType.Commerce, 8f),
            ServiceRadius = 20,
            StorageCapacity = 200f, // balance pass 2026-11-19, was 60: late towns could not stock the Ascension Gate's cost lines
            UpkeepGold = 0.08f,
            Placement = PlacementRule.Outskirts,
            Desirability = -0.2f,
            VisualHeight = 1.6f,
        };

        private static BuildingDefinition MakeFoundry() => new BuildingDefinition
        {
            Name = "Foundry",
            Description = "Coal-stoked blast furnaces smelt raw ore into metal by the ton, far more efficiently than any village smelter. The glow of its pouring floor lights the night sky.",
            Category = BuildingCategory.Industry,
            Shape = BuildingShape.Factory,
            MinAge = AgeId.Industrial,
            MinTier = SettlementTier.Village,
            MaxPerSettlement = 3,
            Width = 3, Height = 3,
            Cost = Cost(wood: 30f, stone: 100f, metal: 30f, tools: 25f, fuel: 15f),
            BuildWork = 350f,
            Job = JobType.Laborer, JobSlots = 4,
            InputsPerWorker = Flow(ResourceType.Ore, 0.06f, ResourceType.Fuel, 0.03f),
            OutputsPerWorker = Flow(ResourceType.Metal, 0.09f),
            UpkeepGold = 0.07f,
            Placement = PlacementRule.Isolated | PlacementRule.Outskirts,
            Desirability = -0.8f,
            VisualHeight = 2.0f,
            TintColor = 0x7A4A32FFu,
        };

        // ================================================================== Modern (+ Machinery)

        private static BuildingDefinition MakePowerPlant() => new BuildingDefinition
        {
            Name = "Power Plant",
            Description = "Cooling towers and turbine halls burn fuel around the clock to electrify the settlement, lighting its streets and driving modern industry.",
            Category = BuildingCategory.Infrastructure,
            Shape = BuildingShape.Factory,
            MinAge = AgeId.Modern,
            MinTier = SettlementTier.Town,
            MaxPerSettlement = 2,
            Width = 3, Height = 3,
            Cost = Cost(stone: 120f, metal: 80f, tools: 20f, fuel: 30f, machinery: 30f),
            BuildWork = 500f,
            Job = JobType.Engineer, JobSlots = 3,
            InputsPerWorker = Flow(ResourceType.Fuel, 0.05f),
            Services = Svc(ServiceType.Power, 20f),
            ServiceRadius = 20,
            UpkeepGold = 0.12f,
            Placement = PlacementRule.Isolated | PlacementRule.Outskirts,
            Desirability = -0.8f,
            VisualHeight = 2.6f,
            TintColor = 0xB0B4B8FFu,
        };

        // Second Fuel producer and the only one for settlements without coal. It deliberately costs neither Fuel nor
        // Machinery (an exception to the Modern "+ Machinery" cost rule): Machinery comes only from the Factory, which
        // burns Fuel, so a Machinery cost would deadlock a coal-less settlement (no Fuel -> no Machinery -> no well).
        private static BuildingDefinition MakeOilWell() => new BuildingDefinition
        {
            Name = "Oil Well",
            Description = "A nodding pumpjack and derrick sunk over an underground oil reservoir. A small crew of engineers draws a rich stream of crude that refines into fuel.",
            Category = BuildingCategory.Resource,
            Shape = BuildingShape.Tower,
            MinAge = AgeId.Modern,
            MinTier = SettlementTier.Village,
            MaxPerSettlement = 4,
            Width = 2, Height = 2,
            Cost = Cost(stone: 30f, metal: 60f, tools: 30f),
            BuildWork = 250f,
            Job = JobType.Engineer, JobSlots = 2,
            OutputsPerWorker = Flow(ResourceType.Fuel, 0.18f),
            UpkeepGold = 0.06f,
            Placement = PlacementRule.NearOil | PlacementRule.Isolated,
            Desirability = -0.7f,
            VisualHeight = 2.2f,
            TintColor = 0x2E2B28FFu,
        };

        private static BuildingDefinition MakeIndustrialFarm() => new BuildingDefinition
        {
            Name = "Industrial Farm",
            Description = "Tractors, fertilizers and irrigation pivots let a handful of farmers feed a whole district. Needs fertile land to reach its yields.",
            Category = BuildingCategory.Food,
            Shape = BuildingShape.Field,
            MinAge = AgeId.Modern,
            MinTier = SettlementTier.Village,
            MaxPerSettlement = 8,
            Width = 3, Height = 3,
            Cost = Cost(wood: 40f, metal: 30f, tools: 15f, machinery: 20f),
            BuildWork = 200f,
            Job = JobType.Farmer, JobSlots = 4,
            OutputsPerWorker = Flow(ResourceType.Food, 0.6f),
            UpkeepGold = 0.05f,
            Placement = PlacementRule.Fertile | PlacementRule.Outskirts,
            Desirability = -0.2f,
            VisualHeight = 0.6f,
        };

        private static BuildingDefinition MakeResearchLab() => new BuildingDefinition
        {
            Name = "Research Lab",
            Description = "Clean rooms, particle benches and blackboards crowded with equations. Scholars here push back the frontier of science; the lab belongs near the heart of the city.",
            Category = BuildingCategory.Education,
            Shape = BuildingShape.Dome,
            MinAge = AgeId.Modern,
            MinTier = SettlementTier.Town,
            MaxPerSettlement = 2,
            Width = 2, Height = 2,
            Cost = Cost(stone: 90f, metal: 50f, tools: 20f, machinery: 15f),
            BuildWork = 400f,
            Job = JobType.Scholar, JobSlots = 6,
            Services = Svc(ServiceType.Research, 15f),
            ServiceRadius = 14,
            UpkeepGold = 0.10f,
            Placement = PlacementRule.Central,
            Desirability = 0.3f,
            VisualHeight = 2.0f,
            TintColor = 0xE8ECF0FFu,
        };

        private static BuildingDefinition MakeStadium() => new BuildingDefinition
        {
            Name = "Stadium",
            Description = "A roaring concrete bowl where tens of thousands cheer athletes and performers under floodlights. Match days lift the mood of the entire city.",
            Category = BuildingCategory.Culture,
            Shape = BuildingShape.Dome,
            MinAge = AgeId.Modern,
            MinTier = SettlementTier.Town,
            MinPopulation = 100,
            MaxPerSettlement = 1,
            Width = 3, Height = 3,
            Cost = Cost(wood: 40f, stone: 200f, metal: 80f, machinery: 15f),
            BuildWork = 600f,
            Job = JobType.Entertainer, JobSlots = 4,
            Services = Svc(ServiceType.Entertainment, 15f),
            ServiceRadius = 16,
            UpkeepGold = 0.10f,
            Placement = PlacementRule.Outskirts,
            Desirability = 0.2f,
            VisualHeight = 1.8f,
        };

        private static BuildingDefinition MakeMilitaryBase() => new BuildingDefinition
        {
            Name = "Military Base",
            Description = "Fenced barracks, motor pools and drill grounds for a standing modern army of twenty soldiers. Dramatically hardens the settlement against any attacker.",
            Category = BuildingCategory.Military,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Modern,
            MinTier = SettlementTier.Town,
            MaxPerSettlement = 1,
            Width = 3, Height = 3,
            Cost = Cost(stone: 150f, metal: 150f, tools: 40f, fuel: 40f, machinery: 30f),
            BuildWork = 550f,
            Job = JobType.Soldier, JobSlots = 20,
            Services = Svc(ServiceType.Defense, 60f),
            ServiceRadius = 18,
            UpkeepGold = 0.15f,
            Placement = PlacementRule.Outskirts,
            Desirability = -0.3f,
            VisualHeight = 1.4f,
            TintColor = 0x6B7350FFu,
        };

        // ================================================================== Information (+ Electronics, Gold)

        // Needs Electronics to build and to run, and the ElectronicsPlant (the only Electronics source) is Information age,
        // so this building is only useful from the Information age on; a tech that unlocks it earlier cannot make it payable.
        private static BuildingDefinition MakeDataCenter() => new BuildingDefinition
        {
            Name = "Data Center",
            Description = "Humming, chilled halls of server racks that model weather, markets and molecules. It wears through electronics as hardware burns out, but vastly accelerates research.",
            Category = BuildingCategory.Education,
            Shape = BuildingShape.Skyscraper,
            MinAge = AgeId.Information,
            MinTier = SettlementTier.Town,
            MaxPerSettlement = 2,
            Width = 2, Height = 2,
            Cost = Cost(stone: 60f, metal: 80f, gold: 40f, machinery: 30f, electronics: 40f),
            BuildWork = 450f,
            Job = JobType.Engineer, JobSlots = 4,
            InputsPerWorker = Flow(ResourceType.Electronics, 0.01f),
            Services = Svc(ServiceType.Research, 30f),
            ServiceRadius = 16,
            UpkeepGold = 0.14f,
            Placement = PlacementRule.Outskirts,
            Desirability = -0.1f,
            VisualHeight = 1.6f,
            TintColor = 0x4A5A70FFu,
        };

        // Only Electronics producer: deliberately costs no Electronics so the chain can bootstrap.
        private static BuildingDefinition MakeElectronicsPlant() => new BuildingDefinition
        {
            Name = "Electronics Plant",
            Description = "Dust-free assembly lines etch circuits from refined metal and gold. It is the sole source of the electronics that the information age runs on.",
            Category = BuildingCategory.Industry,
            Shape = BuildingShape.Factory,
            MinAge = AgeId.Information,
            MinTier = SettlementTier.Town,
            MaxPerSettlement = 2,
            Width = 3, Height = 3,
            Cost = Cost(stone: 80f, metal: 100f, gold: 30f, tools: 30f, machinery: 40f),
            BuildWork = 500f,
            Job = JobType.Engineer, JobSlots = 4,
            InputsPerWorker = Flow(ResourceType.Metal, 0.04f, ResourceType.Gold, 0.01f),
            OutputsPerWorker = Flow(ResourceType.Electronics, 0.04f),
            UpkeepGold = 0.12f,
            Placement = PlacementRule.Isolated | PlacementRule.Outskirts,
            Desirability = -0.6f,
            VisualHeight = 1.8f,
            TintColor = 0xD8DEE6FFu,
        };

        private static BuildingDefinition MakeAirport() => new BuildingDefinition
        {
            Name = "Airport",
            Description = "Runways, hangars and a glass terminal connect the city to the whole world. Only a true city can sustain the traffic, and the noise keeps it on the outskirts.",
            Category = BuildingCategory.Commerce,
            Shape = BuildingShape.Tower,
            MinAge = AgeId.Information,
            MinTier = SettlementTier.City,
            MinPopulation = 200,
            MaxPerSettlement = 1,
            Width = 3, Height = 3,
            Cost = Cost(stone: 250f, metal: 150f, gold: 80f, fuel: 60f, machinery: 60f, electronics: 30f),
            BuildWork = 800f,
            Job = JobType.Merchant, JobSlots = 6,
            Services = Svc(ServiceType.Commerce, 20f),
            ServiceRadius = 30,
            UpkeepGold = 0.18f,
            Placement = PlacementRule.Outskirts,
            Desirability = -0.6f,
            VisualHeight = 1.5f,
        };

        private static BuildingDefinition MakeMedicalCenter() => new BuildingDefinition
        {
            Name = "Medical Center",
            Description = "A gleaming hospital tower with surgical theaters, imaging suites and research wards. Its healers cure what earlier ages could only pray over.",
            Category = BuildingCategory.Health,
            Shape = BuildingShape.Skyscraper,
            MinAge = AgeId.Information,
            MinTier = SettlementTier.Town,
            MaxPerSettlement = 1,
            Width = 2, Height = 2,
            Cost = Cost(stone: 120f, metal: 60f, gold: 50f, machinery: 20f, electronics: 25f),
            BuildWork = 500f,
            Job = JobType.Healer, JobSlots = 6,
            Services = Svc(ServiceType.Health, 25f),
            ServiceRadius = 18,
            UpkeepGold = 0.14f,
            Placement = PlacementRule.Central,
            Desirability = 0.4f,
            VisualHeight = 3.0f,
            TintColor = 0xF2F5F8FFu,
        };

        // ================================================================== Ascension (+ Electronics, Gold)

        private static BuildingDefinition MakeHydroponicsTower() => new BuildingDefinition
        {
            Name = "Hydroponics Tower",
            Description = "A vertical farm of stacked, lamp-lit growing trays. Engineers coax crops from nutrient mist on any ground at all, with no need for fertile soil.",
            Category = BuildingCategory.Food,
            Shape = BuildingShape.Skyscraper,
            MinAge = AgeId.Ascension,
            MinTier = SettlementTier.Town,
            MaxPerSettlement = 6,
            Width = 2, Height = 2,
            Cost = Cost(stone: 60f, metal: 120f, gold: 40f, machinery: 40f, electronics: 30f),
            BuildWork = 450f,
            Job = JobType.Engineer, JobSlots = 3,
            OutputsPerWorker = Flow(ResourceType.Food, 1.2f),
            UpkeepGold = 0.10f,
            Placement = PlacementRule.None,
            Desirability = 0.2f,
            VisualHeight = 3.6f,
            TintColor = 0x7FD38AFFu,
        };

        private static BuildingDefinition MakeFusionReactor() => new BuildingDefinition
        {
            Name = "Fusion Reactor",
            Description = "A magnetic bottle holds a captive star. It yields vast, clean power with no fuel to burn, tended by a small team of engineers.",
            Category = BuildingCategory.Infrastructure,
            Shape = BuildingShape.Dome,
            MinAge = AgeId.Ascension,
            MinTier = SettlementTier.Town,
            MaxPerSettlement = 2,
            Width = 3, Height = 3,
            Cost = Cost(stone: 200f, metal: 300f, gold: 150f, machinery: 120f, electronics: 100f),
            BuildWork = 800f,
            MaxCondition = 200f,
            Job = JobType.Engineer, JobSlots = 4,
            Services = Svc(ServiceType.Power, 80f),
            ServiceRadius = 30,
            UpkeepGold = 0.20f,
            Placement = PlacementRule.Isolated | PlacementRule.Outskirts,
            Desirability = -0.3f,
            VisualHeight = 2.4f,
            TintColor = 0x9FD8FFFFu,
        };

        private static BuildingDefinition MakeArcology() => new BuildingDefinition
        {
            Name = "Arcology",
            Description = "A self-contained city in a single tower, with parks, plazas and water reclamation on every tier. Administrators coordinate its endless corridors and keep the metropolis running.",
            Category = BuildingCategory.Civic,
            Shape = BuildingShape.Skyscraper,
            MinAge = AgeId.Ascension,
            MinTier = SettlementTier.City,
            MinPopulation = 250,
            MaxPerSettlement = 2,
            Width = 3, Height = 3,
            Cost = Cost(stone: 500f, metal: 400f, gold: 200f, tools: 60f, machinery: 150f, electronics: 120f),
            BuildWork = 800f,
            MaxCondition = 200f,
            Job = JobType.Administrator, JobSlots = 6,
            Services = Svc(ServiceType.Administration, 20f, ServiceType.Entertainment, 10f, ServiceType.Water, 20f),
            ServiceRadius = 20,
            StorageCapacity = 400f, // balance pass 2026-11-19, was 150 (endgame storage for the Ascension Gate)
            UpkeepGold = 0.20f,
            Placement = PlacementRule.Central,
            Desirability = 0.7f,
            VisualHeight = 4.0f,
            TintColor = 0xCFE8F0FFu,
        };

        // Endgame wonder: BuildWork 3000 (deliberately outside the 150-800 band). Stockpile.TryPay needs the whole cost in
        // stock at once and capacity is per resource, so no single entry exceeds MaxSingleCost; the colossal scale comes
        // from the total (8 resource types, 4800 units), BuildWork and the MinPopulation gate instead.
        private static BuildingDefinition MakeAscensionGate() => AsWonder(new BuildingDefinition
        {
            Name = "Ascension Gate",
            Description = "The culmination of civilization: a colossal ring of light that opens a path beyond mortal existence. Raising it consumes the wealth of an empire, and each civilization may build only one.",
            Shape = BuildingShape.Monument,
            MinAge = AgeId.Ascension,
            MinTier = SettlementTier.Town,
            MinPopulation = 250,
            Width = 4, Height = 4,
            // Balance pass 2026-11-19: stone and metal 900 -> 700 so every line fits a late town's storage with margin.
            Cost = Cost(wood: 500f, stone: 700f, metal: 700f, gold: 600f, tools: 400f, fuel: 300f, machinery: 600f, electronics: 600f),
            BuildWork = 3000f,
            MaxCondition = 500f,
            Services = Svc(ServiceType.Faith, 50f, ServiceType.Research, 50f),
            ServiceRadius = 40,
            UpkeepGold = 0.20f,
            Placement = PlacementRule.Central,
            Desirability = 0.9f,
            VisualHeight = 4.0f,
            TintColor = 0xFFF4C0FFu,
        });
    }
}
