using System;
using GodSim.Core;

namespace GodSim.Tests.Scenarios
{
    /// <summary>An event observed on the EventBus, stamped with the tick in which it was published.</summary>
    public struct Timed<T> where T : struct
    {
        public long Tick;
        public T Event;

        public Timed(long tick, T evt) { Tick = tick; Event = evt; }

        public int Year => SimTime.Year(Tick);
    }

    /// <summary>Per-year counters collected by <see cref="ScenarioHarness"/> from EventBus subscriptions.</summary>
    public sealed class YearMetrics
    {
        public static readonly int DeathCauseCount = Enum.GetValues(typeof(DeathCause)).Length;
        public static readonly int DestroyCauseCount = Enum.GetValues(typeof(DestroyCause)).Length;
        public static readonly int DisasterTypeCount = Enum.GetValues(typeof(DisasterType)).Length;

        public int Year;
        public int Births;
        public readonly int[] DeathsByCause = new int[DeathCauseCount];
        public readonly int[] BuildingsCompletedByType = new int[Counts.Buildings];
        public readonly int[] BuildingsDestroyedByCause = new int[DestroyCauseCount];
        public readonly int[] DisastersByType = new int[DisasterTypeCount];
        public int BuildingsPlaced;
        public int BuildingsUpgraded;
        public int AgeAdvancements;
        public int TechsResearched;
        public int WarsDeclared;
        public int PeacesSigned;
        public int Battles;
        public int SettlementsFounded;
        public int SettlementsDestroyed;
        public int SettlementsCaptured;
        public int CivsFounded;
        public int CivsCollapsed;
        public int Famines;
        public int Migrations;
        public int GodPowersSucceeded;
        public int GodPowersFailed;
        public int GodCommandsRejected;

        /// <summary>Snapshot taken when the year closes (-1 while the year is still running).</summary>
        public int PopulationAtEnd = -1;
        public int SettlementsAtEnd = -1;
        public int CivsAtEnd = -1;

        public int Deaths
        {
            get { int n = 0; for (int i = 0; i < DeathsByCause.Length; i++) n += DeathsByCause[i]; return n; }
        }

        public int BuildingsCompleted
        {
            get { int n = 0; for (int i = 0; i < BuildingsCompletedByType.Length; i++) n += BuildingsCompletedByType[i]; return n; }
        }

        public int BuildingsDestroyed
        {
            get { int n = 0; for (int i = 0; i < BuildingsDestroyedByCause.Length; i++) n += BuildingsDestroyedByCause[i]; return n; }
        }

        public int DeathsOf(DeathCause c) => DeathsByCause[(int)c];
    }

    /// <summary>Point-in-time view of one settlement (taken every SampleEveryTicks).</summary>
    public struct SettlementSample
    {
        public long Tick;
        public int SettlementId;
        public int CivilizationId;
        public bool IsCapital;
        public int Population;
        public int Adults;
        public int CompleteBuildings;
        public int IncompleteBuildings;
        public int CompleteHouses;
        /// <summary>Sum of residents over complete Houses (BuildingType.House only).</summary>
        public int HouseResidents;
        /// <summary>Sum of HousingCapacity over complete Houses.</summary>
        public int HouseCapacity;
        /// <summary>Residents with no valid home (any dwelling type counts as a home).</summary>
        public int Homeless;
        /// <summary>The value the housing system wrote into Settlement.Homeless (informational).</summary>
        public int ReportedHomeless;
        public int FoodProducers;
        public int FoodWorkers;
        public float Food;

        public float HouseOccupancy => CompleteHouses > 0 ? HouseResidents / (float)CompleteHouses : 0f;
        public float HomelessFraction => Population > 0 ? Homeless / (float)Population : 0f;
        public int Year => SimTime.Year(Tick);
    }

    /// <summary>Point-in-time world totals.</summary>
    public struct WorldSample
    {
        public long Tick;
        public int Population;
        public int Settlements;
        public int Civilizations;
        public int Buildings;
        public int Armies;
        public int ActiveWars;
        public float Mana;
        public int DroughtTicksRemaining;
    }

    /// <summary>A BuildingUpgradedEvent enriched with the building's ownership at event time.</summary>
    public struct UpgradeRecord
    {
        public long Tick;
        public int BuildingId;
        public BuildingType Type;
        public int SettlementId;
        public int CivilizationId;
        public long PlacedTick;
        public int NewLevel;
        public AgeId StyleAge;
        public AgeId OwnerAgeAtEvent;
    }
}
