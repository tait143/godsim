namespace GodSim.Core
{
    // Simulation events (structs, published synchronously on the EventBus).
    // Renderers/UI subscribe to these to update incrementally instead of polling state.

    public struct PersonBornEvent { public int PersonId; public int SettlementId; public int CivilizationId; public RaceId Race; public int MotherId; public int FatherId; }
    public struct PersonDiedEvent { public int PersonId; public int SettlementId; public int CivilizationId; public DeathCause Cause; public float AgeYears; }
    public struct PersonMigratedEvent { public int PersonId; public int FromSettlementId; public int ToSettlementId; }

    public struct BuildingPlacedEvent { public int BuildingId; public BuildingType Type; public int SettlementId; public Vec2i Origin; }
    public struct BuildingCompletedEvent { public int BuildingId; public BuildingType Type; public int SettlementId; }
    public struct BuildingDestroyedEvent { public int BuildingId; public BuildingType Type; public int SettlementId; public Vec2i Origin; public DestroyCause Cause; }
    public struct BuildingUpgradedEvent { public int BuildingId; public int NewLevel; public AgeId StyleAge; }

    public struct SettlementFoundedEvent { public int SettlementId; public int CivilizationId; public Vec2i Position; public int ParentSettlementId; }
    public struct SettlementTierChangedEvent { public int SettlementId; public SettlementTier OldTier; public SettlementTier NewTier; }
    public struct SettlementCapturedEvent { public int SettlementId; public int OldCivilizationId; public int NewCivilizationId; }
    public struct SettlementDestroyedEvent { public int SettlementId; public int CivilizationId; public Vec2i Position; }
    public struct TerritoryChangedEvent { public int SettlementId; }

    public struct CivilizationFoundedEvent { public int CivilizationId; public RaceId Race; }
    public struct CivilizationCollapsedEvent { public int CivilizationId; public int ConquerorId; }
    public struct AgeAdvancedEvent { public int CivilizationId; public AgeId OldAge; public AgeId NewAge; }
    public struct TechResearchedEvent { public int CivilizationId; public int TechId; }

    public struct WarDeclaredEvent { public int WarId; public int AttackerId; public int DefenderId; }
    public struct PeaceSignedEvent { public int WarId; public int AttackerId; public int DefenderId; public float WarScore; }
    public struct StanceChangedEvent { public int CivA; public int CivB; public DiplomaticStance OldStance; public DiplomaticStance NewStance; }
    public struct ArmyCreatedEvent { public int ArmyId; public int CivilizationId; public Vec2i Position; }
    public struct ArmyDisbandedEvent { public int ArmyId; public int CivilizationId; }
    public struct BattleEvent { public Vec2i Position; public int AttackerCivId; public int DefenderCivId; public int SettlementId; public int AttackerLosses; public int DefenderLosses; public bool AttackerWon; }

    public struct GodPowerCastEvent { public GodPowerType Power; public Vec2i Target; public int Radius; public int Parameter; public bool Success; public string Message; }
    public struct GodCommandRejectedEvent { public GodPowerType Power; public Vec2i Target; public string Reason; }
    public struct DisasterEvent { public DisasterType Type; public Vec2i Position; public int Radius; public float Severity; }
    public struct TerrainChangedEvent { public int MinX; public int MinY; public int MaxX; public int MaxY; }

    public struct SeasonChangedEvent { public Season Season; public int Year; }
    public struct YearPassedEvent { public int Year; }
    public struct FamineEvent { public int SettlementId; public float Shortfall; }
    public struct ChronicleAddedEvent { public ChronicleEntry Entry; }
}
