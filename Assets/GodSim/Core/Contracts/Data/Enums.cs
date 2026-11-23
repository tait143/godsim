using System;

namespace GodSim.Core
{
    // =====================================================================================
    // SHARED VOCABULARY. Every module codes against these enums. Do not reorder or remove
    // values (save files store them as numbers); only append new values at the end.
    // =====================================================================================

    /// <summary>The five playable/simulated races.</summary>
    public enum RaceId : byte { Human = 0, Elf = 1, Dwarf = 2, Orc = 3, Saurian = 4 }

    /// <summary>Ten ages of civilization, in order.</summary>
    public enum AgeId : byte
    {
        Dawn = 0,         // stone tools, gatherers, huts
        Bronze = 1,       // farming, copper/bronze, first villages
        Iron = 2,         // iron working, organized religion, fortifications
        Classical = 3,    // philosophy, aqueducts, theaters, markets
        Medieval = 4,     // castles, cathedrals, guilds
        Renaissance = 5,  // banking, universities, printing, gunpowder
        Industrial = 6,   // factories, coal, rail
        Modern = 7,       // electricity, oil, hospitals, mass production
        Information = 8,  // computers, data centers, global trade
        Ascension = 9     // post-scarcity, arcologies, the Ascension Gate
    }

    public enum ResourceType : byte
    {
        Food = 0, Wood = 1, Stone = 2, Ore = 3, Metal = 4, Gold = 5, Tools = 6,
        Cloth = 7, Goods = 8, Weapons = 9, Fuel = 10, Machinery = 11, Electronics = 12
    }

    /// <summary>Per-settlement services produced by staffed buildings (points per tick).</summary>
    public enum ServiceType : byte
    {
        Faith = 0, Health = 1, Research = 2, Entertainment = 3, Defense = 4,
        Water = 5, Power = 6, Commerce = 7, Administration = 8
    }

    public enum Biome : byte
    {
        DeepOcean = 0, ShallowWater = 1, Lake = 2, Beach = 3, Grassland = 4, Savanna = 5,
        Forest = 6, Jungle = 7, Desert = 8, Swamp = 9, Taiga = 10, Tundra = 11,
        Hills = 12, Mountain = 13, Snow = 14, Volcanic = 15
    }

    [Flags]
    public enum TileFlags : ushort
    {
        None = 0,
        River = 1 << 0,
        Road = 1 << 1,
        OreDeposit = 1 << 2,
        StoneDeposit = 1 << 3,
        GoldDeposit = 1 << 4,
        FertileSoil = 1 << 5,
        Ruins = 1 << 6,
        Sacred = 1 << 7,
        Burning = 1 << 8,
        Coastal = 1 << 9,      // land tile adjacent to ocean/lake (computed by world gen)
        CoalDeposit = 1 << 10, // fuel source (Industrial+)
        OilDeposit = 1 << 11,  // fuel source (Modern+)
        Wall = 1 << 12,        // fortification segment
        Farmland = 1 << 13,    // tilled field (visual + fertility use)
        Trees = 1 << 14,       // harvestable woodland on this tile
        Scorched = 1 << 15     // recently burned / meteor crater
    }

    public enum SettlementTier : byte { Camp = 0, Hamlet = 1, Village = 2, Town = 3, City = 4, Metropolis = 5, Megalopolis = 6 }

    public enum BuildingCategory : byte
    {
        Housing, Food, Resource, Industry, Storage, Civic, Religion, Education,
        Health, Culture, Military, Commerce, Infrastructure, Wonder
    }

    /// <summary>Visual archetype used by renderers to procedurally build a mesh.</summary>
    public enum BuildingShape : byte { Hut, House, Hall, Tower, Dome, Pyramid, Spire, Field, Dock, Wall, Tree, Workshop, Factory, Skyscraper, Monument, Cave }

    public enum RoofStyle : byte { Flat, Gable, Hip, Dome, Cone, Spire, Pyramid, Thatch, Tiered, Leaf }

    /// <summary>
    /// Every building in the game. Comment = intended MinAge (age when it becomes available to all races)
    /// and category. Racial uniques are restricted to one race via BuildingDefinition.AllowedRacesMask.
    /// Dwellings ("House") hold 3-5 residents each: settlements grow one building per 3-5 people.
    /// </summary>
    public enum BuildingType : byte
    {
        // --- Civic core
        TownCenter = 0,       // Dawn, Civic. One per settlement, placed at founding. Visual upgrades by tier/age.
        House = 1,            // Dawn, Housing. 3-5 residents. Style changes per race & age.
        Well = 2,             // Dawn, Infrastructure (Water)
        Storehouse = 3,       // Dawn, Storage
        // --- Dawn age economy
        GathererHut = 4,      // Dawn, Food (obsolete after Iron)
        HuntingLodge = 5,     // Dawn, Food (near forest)
        FishingHut = 6,       // Dawn, Food (coastal / river)
        WoodcutterCamp = 7,   // Dawn, Resource (near forest)
        StoneQuarry = 8,      // Dawn, Resource (near stone)
        SpiritTotem = 9,      // Dawn, Religion (Faith)
        Campfire = 10,        // Dawn, Culture (Entertainment)
        // --- Bronze
        Farm = 11,            // Bronze, Food (fertile land)
        Pasture = 12,         // Bronze, Food + Cloth
        Granary = 13,         // Bronze, Storage (food)
        Mine = 14,            // Bronze, Resource (ore deposit / hills)
        Smelter = 15,         // Bronze, Industry (Ore -> Metal)
        Shrine = 16,          // Bronze, Religion
        Herbalist = 17,       // Bronze, Health
        Weaver = 18,          // Bronze, Industry (Cloth)
        Palisade = 19,        // Bronze, Military (Defense)
        // --- Iron
        Blacksmith = 20,      // Iron, Industry (Metal + Wood -> Tools)
        Temple = 21,          // Iron, Religion
        Barracks = 22,        // Iron, Military (Soldier jobs)
        Market = 23,          // Iron, Commerce
        Watchtower = 24,      // Iron, Military
        Armory = 25,          // Iron, Industry (Metal + Wood -> Weapons)
        GoldMine = 26,        // Iron, Resource (gold deposit)
        Scriptorium = 27,     // Iron, Education (Research)
        // --- Classical
        Library = 28,         // Classical, Education
        Aqueduct = 29,        // Classical, Infrastructure (Water)
        Bathhouse = 30,       // Classical, Health
        Theater = 31,         // Classical, Culture
        Harbor = 32,          // Classical, Commerce (coastal)
        Workshop = 33,        // Classical, Industry (Wood + Cloth -> Goods)
        Forum = 34,           // Classical, Civic (Administration)
        Arena = 35,           // Classical, Culture
        StoneWall = 36,       // Classical, Military
        // --- Medieval
        Castle = 37,          // Medieval, Military (one per settlement)
        Cathedral = 38,       // Medieval, Religion
        Monastery = 39,       // Medieval, Religion + Research
        Mill = 40,            // Medieval, Food (boosts farms)
        Guildhall = 41,       // Medieval, Commerce
        Tavern = 42,          // Medieval, Culture
        // --- Renaissance
        University = 43,      // Renaissance, Education
        Bank = 44,            // Renaissance, Commerce (Gold)
        Observatory = 45,     // Renaissance, Education
        Hospital = 46,        // Renaissance, Health
        Printworks = 47,      // Renaissance, Education + Goods
        // --- Industrial
        Factory = 48,         // Industrial, Industry (Metal + Fuel -> Machinery, Goods)
        CoalMine = 49,        // Industrial, Resource (coal deposit -> Fuel)
        Sawmill = 50,         // Industrial, Resource (Wood boost)
        RailStation = 51,     // Industrial, Commerce
        Foundry = 52,         // Industrial, Industry (Ore + Fuel -> Metal)
        // --- Modern
        PowerPlant = 53,      // Modern, Infrastructure (Power)
        OilWell = 54,         // Modern, Resource (oil deposit -> Fuel)
        IndustrialFarm = 55,  // Modern, Food
        ResearchLab = 56,     // Modern, Education
        Stadium = 57,         // Modern, Culture
        MilitaryBase = 58,    // Modern, Military
        // --- Information
        DataCenter = 59,      // Information, Education (Research) + Electronics consumer
        ElectronicsPlant = 60,// Information, Industry (Metal + Gold -> Electronics)
        Airport = 61,         // Information, Commerce
        MedicalCenter = 62,   // Information, Health
        // --- Ascension
        HydroponicsTower = 63,// Ascension, Food
        FusionReactor = 64,   // Ascension, Infrastructure (Power)
        Arcology = 65,        // Ascension, Civic (Administration + Entertainment)
        AscensionGate = 66,   // Ascension, Wonder (one per civilization; endgame)
        // --- Racial uniques (each restricted to one race)
        GrandCathedral = 67,  // Human only, Medieval, Wonder (Faith)
        WorldTree = 68,       // Elf only, Iron, Wonder (Faith + Research + Health)
        DeepForge = 69,       // Dwarf only, Bronze, Wonder/Industry (Metal, Tools)
        WarIdol = 70,         // Orc only, Bronze, Wonder (Defense + Faith)
        SunPyramid = 71,      // Saurian only, Iron, Wonder (Faith + Research)
        // --- Shared wonders
        GreatLibrary = 72,    // Classical, Wonder (Research)
        Colossus = 73,        // Classical, Wonder (Commerce, coastal)
        GrandBazaar = 74      // Medieval, Wonder (Commerce)
    }

    public enum JobType : byte
    {
        None = 0, Gatherer, Hunter, Fisher, Farmer, Herder, Woodcutter, Quarrier, Miner,
        Smith, Craftsman, Weaver, Builder, Merchant, Priest, Scholar, Healer, Entertainer,
        Soldier, Engineer, Laborer, Administrator
    }

    public enum Sex : byte { Female = 0, Male = 1 }

    public enum LifeStage : byte { Child = 0, Adult = 1, Elder = 2 }

    [Flags]
    public enum PersonTraits : ushort
    {
        None = 0, Strong = 1, Clever = 2, Devout = 4, Fertile = 8, Sickly = 16, Brave = 32,
        Lazy = 64, Charismatic = 128, Greedy = 256, Wise = 512, Hardy = 1024, Hero = 2048
    }

    public enum DeathCause : byte { OldAge, Starvation, Disease, Violence, Disaster, DivineWrath, Accident, Exposure }

    public enum DestroyCause : byte { Demolished, Fire, Disaster, War, DivinePower, Decay, Abandoned }

    public enum DisasterType : byte { Wildfire, Earthquake, Flood, Drought, Plague, Meteor, Volcano, Storm, Lightning, Famine }

    public enum DiplomaticStance : byte { War = 0, Hostile = 1, Neutral = 2, Friendly = 3, Allied = 4 }

    public enum ArmyState : byte { Idle, Marching, Besieging, Retreating, Disbanding }

    public enum Season : byte { Spring = 0, Summer = 1, Autumn = 2, Winter = 3 }

    public enum ChronicleCategory : byte { World, Birth, Death, Building, Settlement, Civilization, Age, Technology, War, Diplomacy, Divine, Disaster }

    /// <summary>Divine powers the player-god can invoke.</summary>
    public enum GodPowerType : byte
    {
        // Blessings
        Bless = 0, Rain = 1, Fertility = 2, Inspire = 3, Heal = 4, BountifulHarvest = 5, Prophet = 6,
        // Shaping
        RaiseLand = 7, LowerLand = 8, PlantForest = 9, RevealOre = 10, SpawnTribe = 11,
        // Wrath
        Lightning = 12, Wildfire = 13, Earthquake = 14, Meteor = 15, Plague = 16, Flood = 17, Drought = 18
    }

    public enum GodPowerCategory : byte { Blessing, Shaping, Creation, Wrath }

    /// <summary>Terrain/neighborhood requirements and preferences for building placement.</summary>
    [Flags]
    public enum PlacementRule : ushort
    {
        None = 0,
        NearWater = 1 << 0,     // within 3 tiles of river/lake/coast
        Coastal = 1 << 1,       // footprint touches a Coastal tile adjacent to ocean/lake
        OnHills = 1 << 2,
        NearMountain = 1 << 3,  // within 3 tiles of Mountain biome
        NearForest = 1 << 4,    // within 4 tiles of Trees flag
        NearOre = 1 << 5,
        NearStone = 1 << 6,
        NearGold = 1 << 7,
        NearCoal = 1 << 8,
        NearOil = 1 << 9,
        Fertile = 1 << 10,      // prefers FertileSoil / high fertility
        Central = 1 << 11,      // prefer near the town center
        Outskirts = 1 << 12,    // prefer the settlement edge
        NearRiver = 1 << 13,
        Isolated = 1 << 14      // keep distance from housing (noisy/dirty industry)
    }

    [Flags]
    public enum RaceMask : byte
    {
        None = 0, Human = 1, Elf = 2, Dwarf = 4, Orc = 8, Saurian = 16, All = 31
    }

    /// <summary>Enum sizes for array allocation (computed once; deterministic).</summary>
    public static class Counts
    {
        public static readonly int Races = Enum.GetValues(typeof(RaceId)).Length;
        public static readonly int Ages = Enum.GetValues(typeof(AgeId)).Length;
        public static readonly int Resources = Enum.GetValues(typeof(ResourceType)).Length;
        public static readonly int Services = Enum.GetValues(typeof(ServiceType)).Length;
        public static readonly int Biomes = Enum.GetValues(typeof(Biome)).Length;
        public static readonly int Buildings = Enum.GetValues(typeof(BuildingType)).Length;
        public static readonly int Jobs = Enum.GetValues(typeof(JobType)).Length;
        public static readonly int GodPowers = Enum.GetValues(typeof(GodPowerType)).Length;
        public static readonly int Tiers = Enum.GetValues(typeof(SettlementTier)).Length;
    }

    public static class EnumUtil
    {
        public static RaceMask ToMask(this RaceId race) => (RaceMask)(1 << (int)race);
        public static bool Allows(this RaceMask mask, RaceId race) => (mask & race.ToMask()) != 0;

        public static readonly RaceId[] AllRaces = (RaceId[])Enum.GetValues(typeof(RaceId));
        public static readonly AgeId[] AllAges = (AgeId[])Enum.GetValues(typeof(AgeId));
        public static readonly ResourceType[] AllResources = (ResourceType[])Enum.GetValues(typeof(ResourceType));
        public static readonly BuildingType[] AllBuildings = (BuildingType[])Enum.GetValues(typeof(BuildingType));
        public static readonly ServiceType[] AllServices = (ServiceType[])Enum.GetValues(typeof(ServiceType));
        public static readonly GodPowerType[] AllGodPowers = (GodPowerType[])Enum.GetValues(typeof(GodPowerType));
        public static readonly JobType[] AllJobs = (JobType[])Enum.GetValues(typeof(JobType));

        public static bool IsWater(this Biome b) => b == Biome.DeepOcean || b == Biome.ShallowWater || b == Biome.Lake;
        public static bool IsLand(this Biome b) => !b.IsWater();
        /// <summary>Generic buildability; dwarves may additionally build on Mountain/Hills (planner decides).</summary>
        public static bool IsBuildable(this Biome b) => b.IsLand() && b != Biome.Mountain && b != Biome.Snow && b != Biome.Volcanic;
        /// <summary>Movement cost multiplier for land units; float.PositiveInfinity = impassable.</summary>
        public static float MoveCost(this Biome b)
        {
            switch (b)
            {
                case Biome.DeepOcean: case Biome.ShallowWater: case Biome.Lake: return float.PositiveInfinity;
                case Biome.Mountain: return 4f;
                case Biome.Snow: return 3f;
                case Biome.Volcanic: return 3f;
                case Biome.Swamp: case Biome.Jungle: return 2.2f;
                case Biome.Hills: case Biome.Forest: case Biome.Taiga: return 1.6f;
                default: return 1f;
            }
        }
    }
}
