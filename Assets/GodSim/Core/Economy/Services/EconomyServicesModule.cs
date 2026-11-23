using System.Collections.Generic;

namespace GodSim.Core.Economy
{
    /// <summary>
    /// Factory for the economy service systems, in their recommended order. Composition (or the economy
    /// module's <c>EconomyModule.CreateSystems()</c>) should add these after production/regrowth:
    /// Storage → Services → Upkeep → Trade → Stats (Stats last so it folds the full tick's flows).
    /// </summary>
    public static class EconomyServicesModule
    {
        /// <summary>Creates fresh instances of the five service systems in recommended order.</summary>
        public static IEnumerable<ISimSystem> CreateSystems()
        {
            return new ISimSystem[]
            {
                new StorageSystem(),
                new ServiceSystem(),
                new UpkeepSystem(),
                new TradeSystem(),
                new EconomyStatsSystem(),
            };
        }
    }
}
