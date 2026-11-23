using System;

namespace GodSim.Core
{
    // =====================================================================================
    // STATIC GAME DATA (definitions). Registered once into GameDatabase at startup, then frozen.
    // These never go into save files; saves reference them by enum/id.
    // =====================================================================================

    [Serializable]
    public struct ResourceAmount
    {
        public ResourceType Type;
        public float Amount;
        public ResourceAmount(ResourceType type, float amount) { Type = type; Amount = amount; }
        public static readonly ResourceAmount[] None = new ResourceAmount[0];
        public override string ToString() => Amount.ToString("0.##") + " " + Type;
    }

    [Serializable]
    public struct ServiceAmount
    {
        public ServiceType Type;
        public float Amount;
        public ServiceAmount(ServiceType type, float amount) { Type = type; Amount = amount; }
        public static readonly ServiceAmount[] None = new ServiceAmount[0];
    }

    public sealed class BuildingDefinition
    {
        public BuildingType Type;
        public string Name = "";
        public string Description = "";
        public BuildingCategory Category;
        public BuildingShape Shape = BuildingShape.House;

        // Availability: available when civ.Age >= MinAge (or explicitly unlocked by tech) and civ.Age <= MaxAge.
        public AgeId MinAge = AgeId.Dawn;
        public AgeId MaxAge = AgeId.Ascension;
        public RaceMask AllowedRaces = RaceMask.All;
        public SettlementTier MinTier = SettlementTier.Camp;
        public int MinPopulation;
        public int MaxPerSettlement = -1;          // -1 = unlimited
        public bool UniquePerCivilization;          // wonders
        public bool IsWonder;

        // Footprint & construction
        public int Width = 1;
        public int Height = 1;
        public ResourceAmount[] Cost = ResourceAmount.None;
        public float BuildWork = 10f;               // builder-ticks of labor
        public float MaxCondition = 100f;

        // Housing: HousingMin..HousingMax residents (rolled per building). Houses: 3..5.
        public int HousingMin;
        public int HousingMax;

        // Jobs & production (per staffed worker per tick)
        public JobType Job = JobType.None;
        public int JobSlots;
        public ResourceAmount[] InputsPerWorker = ResourceAmount.None;
        public ResourceAmount[] OutputsPerWorker = ResourceAmount.None;

        // Services (per fully-staffed building per tick; buildings with no jobs provide them at full rate)
        public ServiceAmount[] Services = ServiceAmount.None;
        public int ServiceRadius = 12;

        public float StorageCapacity;               // added to settlement stockpile capacity (per resource)
        public float UpkeepGold;                    // gold per tick
        public PlacementRule Placement;             // hard requirements (Near*/OnHills/Coastal/Fertile) & soft prefs (Central/Outskirts/Isolated)
        public float Desirability;                  // -1..1: how pleasant to live next to (planner zoning)

        // Visuals
        public float VisualHeight = 1f;             // relative height hint for renderers
        public uint TintColor = 0xFFFFFFFFu;        // optional extra tint (0xRRGGBBAA)

        public bool IsDwelling => HousingMax > 0;
        public bool HasJobs => Job != JobType.None && JobSlots > 0;
    }

    public sealed class ArchitectureStyle
    {
        public RoofStyle Roof = RoofStyle.Gable;
        public float HeightScale = 1f;
        public float WidthScale = 1f;
        public float Roundness;               // 0 = boxy, 1 = round/organic
        public bool BuildsUnderground;        // dwarves: half-sunk halls
        public bool BuildsInTrees;            // elves: elevated platforms
        public bool BuildsOnStilts;           // saurians: swamp stilts
        public uint WallColor = 0xC8B496FFu;
        public uint RoofColor = 0x8B4513FFu;
        public uint TrimColor = 0x5A3A1EFFu;
        public uint AccentColor = 0xD4AF37FFu;
        public string StyleTag = "";          // free-form, e.g. "timber", "organic", "carved-stone"
    }

    public sealed class NameParts
    {
        public string[] PersonPrefixes = new string[0];
        public string[] MaleSuffixes = new string[0];
        public string[] FemaleSuffixes = new string[0];
        public string[] FamilyNames = new string[0];
        public string[] SettlementPrefixes = new string[0];
        public string[] SettlementSuffixes = new string[0];
        /// <summary>string.Format patterns with {0} = a generated root, e.g. "The {0} Dominion".</summary>
        public string[] CivilizationPatterns = new string[0];
    }

    public sealed class RaceDefinition
    {
        public RaceId Id;
        public string Name = "";
        public string PluralName = "";
        public string Adjective = "";
        public string Description = "";
        public uint BannerColor = 0xFFFFFFFFu;   // civ color on maps (0xRRGGBBAA)
        public uint SkinColor = 0xE0B090FFu;     // for people renderers

        /// <summary>Indexed by (int)Biome; 0 = uninhabitable, 1 = ideal.</summary>
        public float[] BiomeHabitability = new float[Counts.Biomes];

        // Life cycle (years; 1 year = SimTime.TicksPerYear ticks)
        public float AdulthoodAge = 16f;
        public float ElderAge = 55f;
        public float LifeExpectancy = 70f;       // mean natural death age
        public float FertilityPerYear = 0.25f;   // chance per year for an eligible partnered adult female
        public int MaxChildren = 5;

        // Physiology & aptitudes (1 = baseline)
        public float FoodPerTick = 0.05f;        // food eaten per person per tick
        public float BaseHealth = 1f;
        public float DiseaseResistance = 1f;
        public float BuildSpeed = 1f;
        public float ResearchRate = 1f;
        public float FaithRate = 1f;
        public float MilitaryStrength = 1f;
        public float TradeAptitude = 1f;
        public float MoveSpeed = 1f;
        /// <summary>Indexed by (int)ResourceType; production multiplier.</summary>
        public float[] ProductionMultiplier = Fill(Counts.Resources, 1f);
        /// <summary>Indexed by (int)RaceId; -1 hatred .. +1 kinship. Self should be ~+1.</summary>
        public float[] RaceAffinity = new float[Counts.Races];

        // AI personality (0..1)
        public float Aggression = 0.5f;
        public float Expansionism = 0.5f;
        public float Piety = 0.5f;
        public float Industriousness = 0.5f;
        public float Curiosity = 0.5f;
        public float Diplomacy = 0.5f;
        public float Xenophobia = 0.3f;
        public float Mercantilism = 0.5f;

        // Settlement layout preferences
        public int MinSettlementSpacing = 14;    // tiles between own settlements
        public float Compactness = 0.5f;         // 0 = sprawling, 1 = dense
        public float RoadAffinity = 0.5f;        // 0 = organic paths, 1 = grid roads

        public ArchitectureStyle Architecture = new ArchitectureStyle();
        public NameParts Names = new NameParts();
        public BuildingType[] UniqueBuildings = new BuildingType[0];
        public BuildingType[] FavoredBuildings = new BuildingType[0]; // AI bias

        public static float[] Fill(int n, float v) { var a = new float[n]; for (int i = 0; i < n; i++) a[i] = v; return a; }
    }

    public sealed class AgeDefinition
    {
        public AgeId Id;
        public string Name = "";
        public string Description = "";
        /// <summary>Cumulative civ research (Civilization.TotalResearch) needed to enter this age.</summary>
        public float ResearchThreshold;
        /// <summary>How many techs of the previous age must be known to enter this age.</summary>
        public int RequiredTechsFromPreviousAge;
        public int MinPopulation;                // civ-wide
        public int MinSettlements = 1;
        // Global bonuses granted while in this age (additive to multipliers: 0.1 = +10%)
        public float ProductionBonus;
        public float ResearchBonus;
        public float LifespanBonusYears;
        public float HealthBonus;
        public float BaseSoldierStrength = 1f;
        public uint ThemeColor = 0xFFFFFFFFu;
        public string ArchitectureNotes = "";    // renderer hint: materials by age
    }

    public enum TechEffectKind : byte
    {
        UnlockBuilding,           // Building: available before its MinAge
        ProductionMultiplier,     // Resource, Value (+%)
        AllProductionMultiplier,  // Value (+%)
        ResearchMultiplier,
        GrowthMultiplier,
        LifespanBonus,            // Value in years
        HealthBonus,              // Value 0..1 additive
        HappinessBonus,           // Value 0..1 additive
        MilitaryMultiplier,
        BuildSpeedMultiplier,
        FaithMultiplier,
        StorageMultiplier,
        TradeMultiplier,
        ServiceMultiplier,        // Service, Value (+%)
        MoveSpeedMultiplier
    }

    [Serializable]
    public struct TechEffect
    {
        public TechEffectKind Kind;
        public BuildingType Building;
        public ResourceType Resource;
        public ServiceType Service;
        public float Value;

        public static TechEffect Unlock(BuildingType b) => new TechEffect { Kind = TechEffectKind.UnlockBuilding, Building = b };
        public static TechEffect Produce(ResourceType r, float v) => new TechEffect { Kind = TechEffectKind.ProductionMultiplier, Resource = r, Value = v };
        public static TechEffect ServiceBoost(ServiceType s, float v) => new TechEffect { Kind = TechEffectKind.ServiceMultiplier, Service = s, Value = v };
        public static TechEffect Of(TechEffectKind k, float v) => new TechEffect { Kind = k, Value = v };
    }

    public sealed class TechDefinition
    {
        public int Id = -1;                      // assigned by GameDatabase.Register (index)
        public string Key = "";                  // stable unique key, e.g. "bronze.casting"
        public string Name = "";
        public string Description = "";
        public AgeId Age;
        public float Cost = 100f;                // research points
        public string[] PrerequisiteKeys = new string[0];
        public int[] Prerequisites = new int[0]; // resolved from keys at Freeze()
        public TechEffect[] Effects = new TechEffect[0];
        public RaceMask AllowedRaces = RaceMask.All;
    }

    public sealed class GodPowerDefinition
    {
        public GodPowerType Type;
        public string Name = "";
        public string Description = "";
        public GodPowerCategory Category;
        public float ManaCost = 10f;
        public int CooldownTicks = 10;
        public int MinRadius = 1;
        public int MaxRadius = 6;
        public bool RequiresLandTarget;
        public bool RequiresSettlementInRange;
        /// <summary>Change to GodState.Alignment per cast (+ benevolent, - wrathful).</summary>
        public float AlignmentDelta;
        /// <summary>For SpawnTribe: Parameter must be a valid RaceId. Otherwise Parameter must be 0.</summary>
        public bool UsesRaceParameter;
        public AgeId MinWorldAge = AgeId.Dawn;   // unlocked when any civ reaches this age (optional gating)
    }
}
