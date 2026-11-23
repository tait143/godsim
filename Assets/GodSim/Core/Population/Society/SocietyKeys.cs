namespace GodSim.Core.Population
{
    /// <summary>
    /// Blackboard keys written by the population society systems (housing, jobs, wellbeing, migration,
    /// leadership). Other modules (AI, UI, economy) may read them; only the society systems write them.
    /// </summary>
    public static class SocietyKeys
    {
        /// <summary>
        /// Settlement key: total free residential capacity across complete dwellings (written by
        /// <see cref="HousingSystem"/>, decremented by <see cref="MigrationSystem"/> when it sends people).
        /// The AI reads this to keep building one house per 3-5 people.
        /// </summary>
        public const string FreeHousing = "pop.freeHousing";

        /// <summary>Settlement key: total residential capacity of complete dwellings (written by <see cref="HousingSystem"/>).</summary>
        public const string HousingCapacity = "pop.housingCapacity";

        /// <summary>Settlement key: number of complete, standing dwellings (written by <see cref="HousingSystem"/>).</summary>
        public const string Dwellings = "pop.dwellings";

        /// <summary>Settlement key: alive working-age residents available to the job market (written by <see cref="JobAssignmentSystem"/>).</summary>
        public const string Workforce = "pop.workforce";

        /// <summary>Settlement key: open job slots left unfilled after the last assignment run (written by <see cref="JobAssignmentSystem"/>).</summary>
        public const string OpenJobs = "pop.openJobs";

        /// <summary>Settlement key: 1 while the food stockpile covers fewer than 15 days (written by <see cref="JobAssignmentSystem"/>).</summary>
        public const string FoodEmergency = "pop.foodEmergency";

        /// <summary>Settlement key: tick of the last epidemic outbreak (written by <see cref="WellbeingSystem"/>).</summary>
        public const string LastEpidemicTick = "pop.lastEpidemicTick";

        /// <summary>Settlement key: total residents killed by epidemics (written by <see cref="WellbeingSystem"/>).</summary>
        public const string EpidemicDeaths = "pop.epidemicDeaths";

        /// <summary>Settlement key: residents who left in the last migration run (written by <see cref="MigrationSystem"/>).</summary>
        public const string Emigrated = "pop.emigrated";

        /// <summary>Settlement key: residents who arrived in the last migration run (written by <see cref="MigrationSystem"/>).</summary>
        public const string Immigrated = "pop.immigrated";

        /// <summary>Civilization key: id of the leader seen by the last leadership run (written by <see cref="LeadershipSystem"/>).</summary>
        public const string LeaderId = "pop.leaderId";

        /// <summary>Civilization key: tick the current leader took office (written by <see cref="LeadershipSystem"/>).</summary>
        public const string LeaderSinceTick = "pop.leaderSinceTick";

        /// <summary>Civilization key: number of leaders the civilization has had (written by <see cref="LeadershipSystem"/>).</summary>
        public const string LeaderCount = "pop.leaderCount";
    }
}
