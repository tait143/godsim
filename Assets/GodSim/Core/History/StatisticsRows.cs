namespace GodSim.Core.History
{
    /// <summary>Housing figures of one settlement (see <see cref="WorldStatistics.HousingSummary"/>).</summary>
    public struct HousingStats
    {
        /// <summary>Completed, standing dwellings (any building whose definition has HousingMax &gt; 0).</summary>
        public int Houses;
        /// <summary>Dwellings placed but not yet completed.</summary>
        public int HousesUnderConstruction;
        /// <summary>Sum of HousingCapacity over completed dwellings.</summary>
        public int Capacity;
        /// <summary>People living in completed dwellings.</summary>
        public int Residents;
        /// <summary>Living settlement residents without a home.</summary>
        public int Homeless;
        /// <summary>Residents / Houses (0 when there are no houses).</summary>
        public float AvgPerHouse;

        public int Vacancies => Capacity > Residents ? Capacity - Residents : 0;
        public float Occupancy => Capacity > 0 ? Residents / (float)Capacity : 0f;
    }

    /// <summary>One row of <see cref="WorldStatistics.CivRanking"/>.</summary>
    public struct CivScore
    {
        public int CivId;
        public float Score;
        public int Population;
        public int Settlements;
        public AgeId Age;
        public float TotalResearch;
        public bool IsAlive;
    }

    /// <summary>One row of <see cref="WorldStatistics.LargestWonderOwners"/>.</summary>
    public struct WonderOwnership
    {
        public int CivId;
        public int WonderCount;
        /// <summary>Total footprint tiles of the civ's completed wonders.</summary>
        public int TotalArea;
        /// <summary>The civ's wonder with the largest footprint (ties: lowest building id).</summary>
        public BuildingType LargestWonder;
        public int LargestArea;
    }
}
