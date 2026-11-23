using System;
using System.Collections.Generic;

namespace GodSim.Core
{
    public sealed class Person : IEntity
    {
        public int Id { get; set; }
        public string Name = "";
        public RaceId Race;
        public Sex Sex;
        public long BirthTick;
        public float AgeYears;
        public LifeStage Stage;
        public bool IsAlive = true;
        public long DeathTick = -1;
        public DeathCause DeathCause;

        // Wellbeing (0..1)
        public float Health = 1f;
        public float Happiness = 0.6f;
        public float Satiation = 1f;
        public float Faith = 0.5f;

        public PersonTraits Traits;
        /// <summary>General productivity multiplier (~0.6..1.4).</summary>
        public float Skill = 1f;
        /// <summary>Experience in current job (0..1); resets on job change.</summary>
        public float Experience;

        public JobType Job;
        public int WorkplaceId = -1;
        public int HomeId = -1;
        public int SettlementId = -1;
        public int CivilizationId = -1;
        public int ArmyId = -1;

        public int MotherId = -1;
        public int FatherId = -1;
        public int SpouseId = -1;
        public int ChildrenCount;
        public int Generation;

        public Vec2i Position;

        public bool IsAdult => Stage != LifeStage.Child;
        public bool HasTrait(PersonTraits t) => (Traits & t) != 0;
    }

    public sealed class Building : IEntity
    {
        public int Id { get; set; }
        public BuildingType Type;
        public int SettlementId = -1;
        public int CivilizationId = -1;
        public Vec2i Origin;          // bottom-left tile of footprint
        public int Width = 1;
        public int Height = 1;
        public byte Rotation;         // 0..3 quarter turns (visual only)
        public int Variant;           // visual variation seed

        public bool IsComplete;
        public bool IsDestroyed;
        public float ConstructionProgress;   // 0..1
        public bool CostPaid;                // resources deducted (construction begins)
        public float Condition = 1f;         // 0..1; 0 = ruined
        public int Level = 1;
        public bool IsOnFire;

        public int HousingCapacity;          // rolled from definition (houses: 3..5)
        public readonly List<int> Residents = new List<int>();
        public readonly List<int> Workers = new List<int>();
        /// <summary>Last computed output efficiency 0..1 (staffing * condition * modifiers), set by economy.</summary>
        public float Efficiency;

        public RaceId StyleRace;
        public AgeId StyleAge;               // age it was built (or last renovated) in; drives visuals
        public long PlacedTick;
        public long CompletedTick = -1;

        public Vec2i Center => new Vec2i(Origin.X + Width / 2, Origin.Y + Height / 2);
        public bool Occupies(Vec2i p) => p.X >= Origin.X && p.Y >= Origin.Y && p.X < Origin.X + Width && p.Y < Origin.Y + Height;
        public int FreeHousing => Math.Max(0, HousingCapacity - Residents.Count);
    }

    public sealed class BuildOrder
    {
        public BuildingType Type;
        public float Priority;               // higher first
        public long RequestedTick;
        public string Reason = "";
        public int Attempts;                 // planner failures (give up after a few)
        public bool HasPreferredSite;
        public Vec2i PreferredSite;
    }

    public sealed class Settlement : IEntity
    {
        public int Id { get; set; }
        public string Name = "";
        public int CivilizationId = -1;
        public RaceId Race;
        public Vec2i Center;
        public long FoundedTick;
        public SettlementTier Tier;
        public bool IsCapital;
        public bool IsAlive = true;
        public int TownCenterId = -1;
        public int ParentSettlementId = -1;
        public int TerritoryRadius = 6;

        public readonly List<int> BuildingIds = new List<int>();
        public readonly List<int> ResidentIds = new List<int>();
        public readonly Stockpile Stockpile = new Stockpile();
        public readonly List<BuildOrder> BuildQueue = new List<BuildOrder>();

        /// <summary>Raw service points produced per tick, indexed by ServiceType (set by economy).</summary>
        public readonly float[] ServiceOutput = new float[Counts.Services];
        /// <summary>Per-capita coverage 0..~2 (1 = fully served), indexed by ServiceType (set by economy).</summary>
        public readonly float[] ServiceCoverage = new float[Counts.Services];
        /// <summary>Desired staffing weight per JobType (set by AI, read by job assignment). 0 = default.</summary>
        public readonly float[] JobPriority = new float[Counts.Jobs];

        public int DesiredBuilders;          // set by construction, read by job assignment
        public int Homeless;                 // computed by housing
        public int Unemployed;               // computed by job assignment
        public int Soldiers;                 // computed by job assignment
        public float Happiness = 0.6f;
        public float Health = 1f;
        public float Unrest;
        public float Faith = 0.5f;
        public float DefenseStrength;        // computed by diplomacy/military
        public long LastStarvationTick = -1;

        /// <summary>System/AI blackboard. Keys should be namespaced, e.g. "ai.lastExpandTick". Serialized.</summary>
        public readonly Dictionary<string, float> Memory = new Dictionary<string, float>(StringComparer.Ordinal);
        /// <summary>Recent human-readable AI decisions for debugging UI (bounded; not serialized).</summary>
        public readonly List<string> AiLog = new List<string>();

        public int Population => ResidentIds.Count;

        public float Mem(string key, float fallback = 0f) => Memory.TryGetValue(key, out float v) ? v : fallback;
        public void LogAi(string line) { AiLog.Add(line); if (AiLog.Count > 16) AiLog.RemoveAt(0); }
    }

    /// <summary>Multipliers aggregated from race, age and technologies. Recomputed by the progression module.</summary>
    public sealed class ModifierSet
    {
        public readonly float[] Production = RaceDefinition.Fill(Counts.Resources, 1f);
        public readonly float[] Service = RaceDefinition.Fill(Counts.Services, 1f);
        public float Research = 1f, Growth = 1f, BuildSpeed = 1f, Faith = 1f, Military = 1f, Storage = 1f, Trade = 1f, MoveSpeed = 1f;
        public float HealthBonus, HappinessBonus, LifespanBonusYears;

        public void Reset()
        {
            for (int i = 0; i < Production.Length; i++) Production[i] = 1f;
            for (int i = 0; i < Service.Length; i++) Service[i] = 1f;
            Research = Growth = BuildSpeed = Faith = Military = Storage = Trade = MoveSpeed = 1f;
            HealthBonus = HappinessBonus = LifespanBonusYears = 0f;
        }
    }

    public sealed class Civilization : IEntity
    {
        public int Id { get; set; }
        public string Name = "";
        public RaceId Race;
        public uint Color = 0xFFFFFFFFu;
        public AgeId Age = AgeId.Dawn;
        public long AgeEnteredTick;
        public long FoundedTick;
        public bool IsAlive = true;
        public long CollapsedTick = -1;

        public int CapitalId = -1;
        public readonly List<int> SettlementIds = new List<int>();
        public int LeaderId = -1;

        // Research
        public float ResearchPool;             // progress toward CurrentResearchTechId
        public float TotalResearch;            // lifetime research (age thresholds)
        public int CurrentResearchTechId = -1; // chosen by AI (or progression fallback)
        public readonly SortedSet<int> KnownTechs = new SortedSet<int>();
        public readonly bool[] UnlockedBuildings = new bool[Counts.Buildings];

        public readonly ModifierSet Modifiers = new ModifierSet();

        // Relationship with the god (player)
        public float Devotion = 0.5f;          // 0..1 how much they worship
        public float GodFavor;                 // -1..1 perception: smitten .. blessed

        // Cached stats (recomputed by systems; informational)
        public int Population;
        public float MilitaryPower;

        public readonly Dictionary<string, float> Memory = new Dictionary<string, float>(StringComparer.Ordinal);

        public float Mem(string key, float fallback = 0f) => Memory.TryGetValue(key, out float v) ? v : fallback;
    }

    public sealed class Army : IEntity
    {
        public int Id { get; set; }
        public int CivilizationId = -1;
        public int HomeSettlementId = -1;
        public int TargetSettlementId = -1;
        public int WarId = -1;
        public ArmyState State;
        public Vec2i Position;
        public Vec2i Destination;
        public float MoveProgress;             // fractional tile progress
        public float Strength;                 // aggregated combat power
        public float Morale = 1f;
        public bool IsAlive = true;
        public readonly List<int> SoldierIds = new List<int>();
        public readonly List<Vec2i> Path = new List<Vec2i>();
        public int PathIndex;
        public long CreatedTick;
    }
}
