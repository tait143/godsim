namespace GodSim.Core.Buildings
{
    // Racial unique wonders (BuildingType 67..71). Each is restricted to exactly one race, is a wonder and
    // unique per civilization. Racial techs may unlock a wonder one age before its MinAge
    // (TechEffectKind.UnlockBuilding), so each cost only uses resources that earlier catalogs already produce in
    // the age BEFORE its MinAge: Dawn Food/Wood/Stone; Bronze adds Ore/Metal/Cloth (Mine, Smelter, Pasture, Weaver);
    // Iron adds Gold/Tools/Weapons (GoldMine, Blacksmith, Armory); Classical adds Goods (Workshop).
    // Every single cost entry stays within MaxSingleCostPreIndustrial so a small settlement can store it.
    public static partial class BuildingsLate
    {
        private static BuildingDefinition MakeGrandCathedral() => AsRacialWonder(new BuildingDefinition
        {
            Name = "Grand Cathedral",
            Description = "A soaring cathedral of flying buttresses, bell towers and stained glass. Its choirs and feast-day festivals kindle faith and joy across the human realm. Human only.",
            Shape = BuildingShape.Spire,
            MinAge = AgeId.Medieval,
            MinTier = SettlementTier.Town,
            Width = 3, Height = 3,
            Cost = Cost(wood: 150f, stone: 400f, gold: 120f, tools: 40f, goods: 60f),
            BuildWork = 700f,
            MaxCondition = 250f,
            Services = Svc(ServiceType.Faith, 20f, ServiceType.Entertainment, 5f),
            ServiceRadius = 20,
            UpkeepGold = 0.06f,
            Placement = PlacementRule.Central,
            Desirability = 0.8f,
            VisualHeight = 3.5f,
            TintColor = 0xE6DCC8FFu,
        }, RaceId.Human);

        private static BuildingDefinition MakeWorldTree() => AsRacialWonder(new BuildingDefinition
        {
            Name = "World Tree",
            Description = "An ancient tree sung by elven druids to titanic size, its boughs hosting shrines, libraries and healing groves. It can only take root near a living forest. Elf only.",
            Shape = BuildingShape.Tree,
            MinAge = AgeId.Iron,
            MinTier = SettlementTier.Village,
            Width = 3, Height = 3,
            Cost = Cost(wood: 250f, stone: 60f, cloth: 60f),
            BuildWork = 600f,
            MaxCondition = 250f,
            Services = Svc(ServiceType.Faith, 8f, ServiceType.Research, 6f, ServiceType.Health, 6f),
            ServiceRadius = 20,
            UpkeepGold = 0.04f,
            Placement = PlacementRule.NearForest,
            Desirability = 0.9f,
            VisualHeight = 4.0f,
            TintColor = 0x5FA85AFFu,
        }, RaceId.Elf);

        // Placement: the brief asks for "NearMountain or OnHills", but PlacementRule hard flags are all required at once
        // (the planner ANDs them), so a combined mask would demand hills footprint AND a mountain within 3. NearMountain
        // alone is the closest single flag: it also counts Mountain tiles inside the footprint (dwarves may build this on
        // Mountain tiles) and the hills that flank mountains. Unlockable in Dawn, so the cost is Wood/Stone only.
        private static BuildingDefinition MakeDeepForge() => AsRacialWonder(new BuildingDefinition
        {
            Name = "Deep Forge",
            Description = "A dwarven forge carved into the living rock and fed by magma vents. Master smiths turn ore into metal and tools, and its sealed halls double as a fortress. It must be dug into or beside a mountain. Dwarf only.",
            Shape = BuildingShape.Cave,
            MinAge = AgeId.Bronze,
            MinTier = SettlementTier.Village,
            Width = 3, Height = 3,
            Cost = Cost(wood: 120f, stone: 300f),
            BuildWork = 600f,
            MaxCondition = 300f,
            Job = JobType.Smith, JobSlots = 6,
            InputsPerWorker = Flow(ResourceType.Ore, 0.05f),
            OutputsPerWorker = Flow(ResourceType.Metal, 0.08f, ResourceType.Tools, 0.03f),
            Services = Svc(ServiceType.Defense, 6f),
            ServiceRadius = 14,
            UpkeepGold = 0.03f,
            Placement = PlacementRule.NearMountain,
            Desirability = -0.2f,
            VisualHeight = 1.2f,
            TintColor = 0xC8643CFFu,
        }, RaceId.Dwarf);

        private static BuildingDefinition MakeWarIdol() => AsRacialWonder(new BuildingDefinition
        {
            Name = "War Idol",
            Description = "A towering idol of fire-hardened timber and piled stone hung with trophy skulls. Warbands gather before it to howl blood oaths, steeling the whole camp for battle. Orc only.",
            Shape = BuildingShape.Monument,
            MinAge = AgeId.Bronze,
            MinTier = SettlementTier.Hamlet,
            Width = 2, Height = 2,
            Cost = Cost(wood: 180f, stone: 120f),
            BuildWork = 300f,
            MaxCondition = 250f,
            Services = Svc(ServiceType.Defense, 10f, ServiceType.Faith, 4f),
            ServiceRadius = 16,
            UpkeepGold = 0.03f,
            Placement = PlacementRule.Central,
            Desirability = 0.1f,
            VisualHeight = 2.2f,
            TintColor = 0x8C2A1EFFu,
        }, RaceId.Orc);

        private static BuildingDefinition MakeSunPyramid() => AsRacialWonder(new BuildingDefinition
        {
            Name = "Sun Pyramid",
            Description = "A stepped pyramid of sun-baked stone whose summit altar greets each dawn. Saurian priests chart the heavens from its terraces and the faithful climb it in procession. Saurian only.",
            Shape = BuildingShape.Pyramid,
            MinAge = AgeId.Iron,
            MinTier = SettlementTier.Village,
            Width = 3, Height = 3,
            Cost = Cost(wood: 60f, stone: 300f, metal: 40f),
            BuildWork = 700f,
            MaxCondition = 300f,
            Services = Svc(ServiceType.Faith, 10f, ServiceType.Research, 5f),
            ServiceRadius = 20,
            UpkeepGold = 0.05f,
            Placement = PlacementRule.Central,
            Desirability = 0.6f,
            VisualHeight = 3.0f,
            TintColor = 0xD9B45AFFu,
        }, RaceId.Saurian);
    }
}
