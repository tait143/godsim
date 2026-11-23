namespace GodSim.Core.Divine
{
    /// <summary>
    /// Result of applying one divine power effect. Produced by <see cref="GodPowerEffects"/> and consumed by
    /// <see cref="GodCommandSystem"/>, which turns it into a <see cref="GodPowerCastEvent"/> and (for
    /// momentous casts) a chronicle entry.
    /// </summary>
    public sealed class GodPowerOutcome
    {
        /// <summary>True when the power took effect (it may still have touched nothing, e.g. Heal over empty land).</summary>
        public bool Success;
        /// <summary>Short human-readable result for the UI.</summary>
        public string Message = "";
        /// <summary>Power-specific count of things touched (people, tiles, settlements...).</summary>
        public int Affected;
        /// <summary>People killed by the effect.</summary>
        public int Killed;
        /// <summary>People born or created by the effect.</summary>
        public int Created;
        /// <summary>Buildings destroyed by the effect.</summary>
        public int BuildingsDestroyed;
        /// <summary>Tiles whose terrain (elevation, biome or flags) changed.</summary>
        public int TilesChanged;
        /// <summary>Chronicle text to record for this cast, or null when the cast is not chronicle-worthy.</summary>
        public string ChronicleText;
        /// <summary>Chronicle importance (0 minor .. 3 legendary).</summary>
        public int ChronicleImportance;
        /// <summary>Civilization most associated with the cast (-1 = none).</summary>
        public int CivilizationId = -1;
        /// <summary>Settlement most associated with the cast (-1 = none).</summary>
        public int SettlementId = -1;

        /// <summary>A successful outcome with the given message.</summary>
        public static GodPowerOutcome Ok(string message) => new GodPowerOutcome { Success = true, Message = message ?? "" };

        /// <summary>A failed outcome (nothing was changed).</summary>
        public static GodPowerOutcome Fail(string message) => new GodPowerOutcome { Success = false, Message = message ?? "" };
    }
}
