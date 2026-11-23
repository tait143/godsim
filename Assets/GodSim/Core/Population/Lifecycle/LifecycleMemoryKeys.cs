namespace GodSim.Core.Population
{
    /// <summary>
    /// Settlement blackboard keys (<see cref="Settlement.Memory"/>) written by the lifecycle systems.
    /// Other modules (economy, AI, UI) may read them; only the lifecycle systems write them.
    /// </summary>
    public static class LifecycleMemoryKeys
    {
        /// <summary>Food the residents want to eat per tick (sum of per-person needs), written by <see cref="NeedsSystem"/>.</summary>
        public const string FoodNeedPerTick = "lifecycle.foodNeedPerTick";

        /// <summary>Smoothed share of food demand actually eaten (0..1, 1 = fully fed), written by <see cref="NeedsSystem"/>.</summary>
        public const string FoodRatio = "lifecycle.foodRatio";

        /// <summary>Tick of the last famine chronicle entry, written by <see cref="NeedsSystem"/>.</summary>
        public const string LastFamineChronicleTick = "lifecycle.lastFamineChronicleTick";

        /// <summary>Births in the settlement over its lifetime, written by <see cref="BirthSystem"/>.</summary>
        public const string TotalBirths = "lifecycle.totalBirths";
    }
}
