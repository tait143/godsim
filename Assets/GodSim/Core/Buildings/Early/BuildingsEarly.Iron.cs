namespace GodSim.Core.Buildings
{
    // Iron age (BuildingType 20..27): Metal and Tools join the costs. The Tools source
    // (Blacksmith) itself needs no Tools, so it can always be bootstrapped.
    public static partial class BuildingsEarly
    {
        private static BuildingDefinition MakeBlacksmith() => new BuildingDefinition
        {
            Type = BuildingType.Blacksmith,
            Name = "Blacksmith",
            Description = "Hammer on anvil from first light, the smith forges bars of metal and seasoned hafts into axes, ploughshares and chisels. Every craft of the age depends on the smith's tools.",
            Category = BuildingCategory.Industry,
            Shape = BuildingShape.Workshop,
            MinAge = AgeId.Iron,
            MaxAge = AgeId.Ascension,
            MinTier = SettlementTier.Camp,
            MinPopulation = 15,
            MaxPerSettlement = -1,
            Width = 2,
            Height = 1,
            // Deliberately no Tools: the Blacksmith is the first source of Tools.
            Cost = new[] { R(ResourceType.Wood, 20f), R(ResourceType.Stone, 20f), R(ResourceType.Metal, 8f) },
            BuildWork = 40f,
            MaxCondition = 140f,
            Job = JobType.Smith,
            JobSlots = 2,
            InputsPerWorker = new[] { R(ResourceType.Metal, 0.04f), R(ResourceType.Wood, 0.02f) },
            OutputsPerWorker = One(ResourceType.Tools, 0.04f),
            ServiceRadius = 12,
            Placement = PlacementRule.Isolated,
            Desirability = -0.4f,
            VisualHeight = 1.2f,
            TintColor = TintIron,
        };

        private static BuildingDefinition MakeTemple() => new BuildingDefinition
        {
            Type = BuildingType.Temple,
            Name = "Temple",
            Description = "A columned house of the gods crowned with a gilded dome, where a priesthood keeps the sacred calendar and the people gather on holy days. Its rites carry the settlement's devotion straight to the heavens.",
            Category = BuildingCategory.Religion,
            Shape = BuildingShape.Dome,
            MinAge = AgeId.Iron,
            MaxAge = AgeId.Ascension,
            MinTier = SettlementTier.Hamlet,
            MinPopulation = 25,
            MaxPerSettlement = 2,
            Width = 2,
            Height = 2,
            Cost = new[]
            {
                R(ResourceType.Stone, 60f), R(ResourceType.Wood, 30f),
                R(ResourceType.Metal, 10f), R(ResourceType.Tools, 5f),
            },
            BuildWork = 80f,
            MaxCondition = 220f,
            Job = JobType.Priest,
            JobSlots = 2,
            Services = One(ServiceType.Faith, 3f),
            ServiceRadius = 16,
            Placement = PlacementRule.Central,
            Desirability = 0.7f,
            VisualHeight = 2.2f,
            TintColor = TintGilded,
        };

        private static BuildingDefinition MakeBarracks() => new BuildingDefinition
        {
            Type = BuildingType.Barracks,
            Name = "Barracks",
            Description = "Bunk halls, a drill yard and racks of spears where the settlement's first professional soldiers train. They are paid in coin and kept ready for war.",
            Category = BuildingCategory.Military,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Iron,
            MaxAge = AgeId.Ascension,
            MinTier = SettlementTier.Hamlet,
            MinPopulation = 30,
            MaxPerSettlement = 2,
            Width = 2,
            Height = 2,
            Cost = new[]
            {
                R(ResourceType.Wood, 40f), R(ResourceType.Stone, 30f),
                R(ResourceType.Metal, 10f), R(ResourceType.Tools, 4f),
            },
            BuildWork = 60f,
            MaxCondition = 200f,
            Job = JobType.Soldier,
            JobSlots = 6,
            Services = One(ServiceType.Defense, 2f),
            ServiceRadius = 14,
            UpkeepGold = 0.01f,
            Placement = PlacementRule.Outskirts,
            Desirability = -0.2f,
            VisualHeight = 1.3f,
            TintColor = TintTimber,
        };

        private static BuildingDefinition MakeMarket() => new BuildingDefinition
        {
            Type = BuildingType.Market,
            Name = "Market",
            Description = "A bustling square of awnings and stalls where merchants haggle over grain, cloth and trinkets from distant lands. Coin changes hands and so do rumours.",
            Category = BuildingCategory.Commerce,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Iron,
            MaxAge = AgeId.Ascension,
            MinTier = SettlementTier.Village,
            MinPopulation = 40,
            MaxPerSettlement = 2,
            Width = 2,
            Height = 2,
            Cost = new[]
            {
                R(ResourceType.Wood, 40f), R(ResourceType.Stone, 30f),
                R(ResourceType.Metal, 6f), R(ResourceType.Tools, 3f),
            },
            BuildWork = 55f,
            MaxCondition = 160f,
            Job = JobType.Merchant,
            JobSlots = 2,
            Services = One(ServiceType.Commerce, 2f),
            ServiceRadius = 14,
            UpkeepGold = 0.01f,
            Placement = PlacementRule.Central,
            Desirability = 0.4f,
            VisualHeight = 1.1f,
            TintColor = TintThatch,
        };

        private static BuildingDefinition MakeWatchtower() => new BuildingDefinition
        {
            Type = BuildingType.Watchtower,
            Name = "Watchtower",
            Description = "A tall stone-footed lookout with a signal brazier at its crown, where a lone sentry watches the horizon for raiders. It keeps its vigil through the Industrial age, until the telegraph finally makes it obsolete.",
            Category = BuildingCategory.Military,
            Shape = BuildingShape.Tower,
            MinAge = AgeId.Iron,
            MaxAge = AgeId.Industrial,
            MinTier = SettlementTier.Camp,
            MinPopulation = 15,
            MaxPerSettlement = -1,
            Width = 1,
            Height = 1,
            Cost = new[] { R(ResourceType.Wood, 20f), R(ResourceType.Stone, 25f), R(ResourceType.Metal, 4f) },
            BuildWork = 35f,
            MaxCondition = 180f,
            Job = JobType.Soldier,
            JobSlots = 1,
            Services = One(ServiceType.Defense, 3f),
            ServiceRadius = 14,
            Placement = PlacementRule.Outskirts,
            Desirability = 0f,
            VisualHeight = 2.5f,
            TintColor = TintFieldStone,
        };

        private static BuildingDefinition MakeArmory() => new BuildingDefinition
        {
            Type = BuildingType.Armory,
            Name = "Armory",
            Description = "A guarded forge-house where blades are tempered, spearheads riveted and shields bound in iron. Its racks decide how well the settlement's soldiers fight.",
            Category = BuildingCategory.Industry,
            Shape = BuildingShape.Workshop,
            MinAge = AgeId.Iron,
            MaxAge = AgeId.Ascension,
            MinTier = SettlementTier.Hamlet,
            MinPopulation = 30,
            MaxPerSettlement = -1,
            Width = 2,
            Height = 1,
            Cost = new[]
            {
                R(ResourceType.Wood, 25f), R(ResourceType.Stone, 25f),
                R(ResourceType.Metal, 10f), R(ResourceType.Tools, 4f),
            },
            BuildWork = 45f,
            MaxCondition = 160f,
            Job = JobType.Smith,
            JobSlots = 2,
            InputsPerWorker = new[] { R(ResourceType.Metal, 0.04f), R(ResourceType.Wood, 0.02f) },
            OutputsPerWorker = One(ResourceType.Weapons, 0.03f),
            ServiceRadius = 12,
            Placement = PlacementRule.Isolated,
            Desirability = -0.4f,
            VisualHeight = 1.2f,
            TintColor = TintIron,
        };

        private static BuildingDefinition MakeGoldMine() => new BuildingDefinition
        {
            Type = BuildingType.GoldMine,
            Name = "Gold Mine",
            Description = "Deep, narrow galleries chasing a glittering vein through the rock. Its grudging trickle of gold pays for markets, soldiers and kings' vanities.",
            Category = BuildingCategory.Resource,
            Shape = BuildingShape.Cave,
            MinAge = AgeId.Iron,
            MaxAge = AgeId.Ascension,
            MinTier = SettlementTier.Camp,
            MinPopulation = 15,
            MaxPerSettlement = -1,
            Width = 2,
            Height = 2,
            Cost = new[]
            {
                R(ResourceType.Wood, 25f), R(ResourceType.Stone, 15f),
                R(ResourceType.Metal, 5f), R(ResourceType.Tools, 4f),
            },
            BuildWork = 40f,
            MaxCondition = 150f,
            Job = JobType.Miner,
            JobSlots = 3,
            OutputsPerWorker = One(ResourceType.Gold, 0.03f),
            ServiceRadius = 12,
            Placement = PlacementRule.NearGold,
            Desirability = -0.5f,
            VisualHeight = 0.8f,
            TintColor = TintBronze,
        };

        private static BuildingDefinition MakeScriptorium() => new BuildingDefinition
        {
            Type = BuildingType.Scriptorium,
            Name = "Scriptorium",
            Description = "A quiet, lamp-lit hall where scholars copy, annotate and argue over scrolls of law, star-lore and medicine. Here knowledge first outlives the people who discovered it.",
            Category = BuildingCategory.Education,
            Shape = BuildingShape.Hall,
            MinAge = AgeId.Iron,
            MaxAge = AgeId.Ascension,
            MinTier = SettlementTier.Hamlet,
            MinPopulation = 30,
            MaxPerSettlement = -1,
            Width = 2,
            Height = 1,
            Cost = new[]
            {
                R(ResourceType.Wood, 30f), R(ResourceType.Stone, 30f),
                R(ResourceType.Metal, 4f), R(ResourceType.Tools, 3f),
            },
            BuildWork = 50f,
            MaxCondition = 140f,
            Job = JobType.Scholar,
            JobSlots = 2,
            Services = One(ServiceType.Research, 1.2f),
            ServiceRadius = 12,
            Placement = PlacementRule.Central,
            Desirability = 0.4f,
            VisualHeight = 1.4f,
            TintColor = TintDressedStone,
        };
    }
}
