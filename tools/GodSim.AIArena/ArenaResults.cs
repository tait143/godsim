using System.Collections.Generic;

namespace GodSim.AIArena
{
    /// <summary>Raw metrics of one simulated world (one seed). Serialized to JSON (worker processes and --json).</summary>
    public sealed class SimResult
    {
        public ulong Seed;
        public int Years;
        public int Size;
        public string Preset = "";
        public bool Crashed;
        public string Exception = "";
        public int YearsCompleted;
        public double WallSeconds;
        /// <summary>StateHasher hash of the final world (hex). Same seed + same build must give the same hash.</summary>
        public string FinalHash = "";

        // Performance
        public double AvgTickMs;
        public double P99TickMs;
        public double MaxTickMs;
        public double AvgPopulation;
        public List<SystemCost> SlowestSystems = new List<SystemCost>();

        // Robustness
        public int SystemFaults;
        public int HandlerFaults;
        public int LogErrors;
        public int LogWarnings;
        public List<string> DisabledSystems = new List<string>();
        public List<string> FaultySystems = new List<string>();
        public List<string> FirstErrors = new List<string>();

        // World drama
        public int WarsDeclared;
        public int PeacesSigned;
        public int Battles;
        public int Captures;
        public int Disasters;
        public int CivCollapses;
        public int CivsFoundedLater;
        public Dictionary<string, int> DisastersByType = new Dictionary<string, int>();
        public List<WarInfo> Wars = new List<WarInfo>();

        // Resilience: a destruction incident = a settlement losing completed buildings to fire/disaster/war/divine power;
        // recovered when its completed-building count is back to the pre-incident level.
        public int Incidents;
        public int IncidentBuildingsLost;
        public int IncidentsRecovered;
        public int IncidentsSettlementLost;
        public double AvgRecoveryYears;

        public List<int> WorldPopByYear = new List<int>();
        public List<CivResult> Civs = new List<CivResult>();
    }

    public sealed class SystemCost { public string Name = ""; public double AvgMs; public int Faults; public bool Disabled; public string LastError = ""; }

    public sealed class WarInfo
    {
        public int Id;
        public int AttackerId;
        public int DefenderId;
        public string AttackerRace = "";
        public string DefenderRace = "";
        public double StartYear;
        public double EndYear = -1;
        public double DurationYears;
        /// <summary>attacker, defender, white-peace or ongoing.</summary>
        public string Outcome = "ongoing";
        public float WarScore;
        public int Battles;
        public int Captures;
        public int AttackerLosses;
        public int DefenderLosses;
    }

    /// <summary>Metrics of one civilization over one world's history.</summary>
    public sealed class CivResult
    {
        public int Id;
        public string Race = "";
        public string Name = "";
        public bool Initial;
        public bool Alive;
        public double CollapseYear = -1;

        public int InitialPop;
        public int FinalPop;
        public int PeakPop;
        public int PeakYear;
        public List<int> PopByYear = new List<int>();

        public string FinalAge = "";
        public int AgesGained;
        public List<double> AgeYears = new List<double>();   // year each new age was reached (in order)
        public double AvgYearsPerAge = -1;                    // over completed ages (incl. Dawn duration)

        public long Births;
        public long Deaths;
        public long StarvationDeaths;
        public Dictionary<string, long> DeathsByCause = new Dictionary<string, long>();

        public double HomelessPct;          // time-averaged, % of population
        public double HomelessPctMax;
        public List<double> HomelessPctByDecade = new List<double>();
        public double PeoplePerHouse;       // time-averaged population / completed dwellings
        public double PeoplePerHouseStd;    // std-dev across settlement samples
        public double InBandPct;            // % of settlement samples with 3..5 people per dwelling
        public double HousingCapPerCapita;  // dwelling capacity / population
        public double HousesPerCapita;      // dwellings / population (target 0.2..0.33)

        public Dictionary<string, int> CategoryMix = new Dictionary<string, int>();
        public int[] TypeMix = new int[0];  // completed buildings by BuildingType (diversity vectors)
        public double TierFit;              // 0..1 expected categories present for each settlement's tier
        public string BestTier = "";

        public int BuildingsPlaced;
        public int BuildingsCompleted;
        public double AvgBuildYears;
        public double StallPct;             // % of under-construction building-time with no progress
        public int StuckAtEnd;              // incomplete for more than 2 years at the end

        public double IdlePct;              // unemployed / adults, time-averaged
        public double FoodDays;             // time-averaged days of food stock
        public double FoodDaysMin;
        public int Famines;

        public int Expansions;
        public double FirstExpansionYear = -1;
        public List<double> ExpansionYears = new List<double>();
        public int SettlementsAtEnd;

        public int WarsAttacker;
        public int WarsDefender;
        public int WarsWon;
        public int WarsLost;
        public int Battles;
        public int BattlesWon;
        public int CapturesGained;
        public int CapturesLost;

        public double TotalResearch;
        public double ResearchPerYear;
        public int TechsResearched;

        public double GoldSolventPct;       // % of settlement samples with gold > 1 or non-negative net
        public double GoldEnd;

        public double AiLinesPerYear;       // AiLog lines per year (whole civ)
        public double AiLinesPerSettlementYear;

        public Dictionary<string, float> AiScores = new Dictionary<string, float>();    // civ.Memory "ai.score.*"
        public Dictionary<string, float> AdvisorWeights = new Dictionary<string, float>(); // civ.Memory "ai.w.*"
    }
}
