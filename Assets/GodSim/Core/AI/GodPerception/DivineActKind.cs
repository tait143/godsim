namespace GodSim.Core.AI
{
    /// <summary>How mortals classify a divine (or divinely-attributed) event. Stored as a float in "godp.lastKind".</summary>
    public enum DivineActKind : byte
    {
        /// <summary>No act remembered.</summary>
        None = 0,
        /// <summary>A gift: Bless, Rain, Fertility, Inspire, Heal, BountifulHarvest, Prophet, PlantForest, RevealOre.</summary>
        Blessing = 1,
        /// <summary>The god reshaping the world: RaiseLand, LowerLand, SpawnTribe. Awe rather than love or fear.</summary>
        Shaping = 2,
        /// <summary>Divine punishment: Lightning, Wildfire, Earthquake, Meteor, Plague, Flood, Drought.</summary>
        Wrath = 3,
        /// <summary>A natural disaster that a pious civilization attributed to the god.</summary>
        BlamedDisaster = 4,
    }

    /// <summary>Pure classification helpers for god powers and disasters (no state, allocation-free).</summary>
    public static class DivineActClassifier
    {
        /// <summary>Classifies a god power the way mortals perceive it.</summary>
        public static DivineActKind Classify(GodPowerType power)
        {
            switch (power)
            {
                case GodPowerType.Bless:
                case GodPowerType.Rain:
                case GodPowerType.Fertility:
                case GodPowerType.Inspire:
                case GodPowerType.Heal:
                case GodPowerType.BountifulHarvest:
                case GodPowerType.Prophet:
                case GodPowerType.PlantForest:
                case GodPowerType.RevealOre:
                    return DivineActKind.Blessing;
                case GodPowerType.RaiseLand:
                case GodPowerType.LowerLand:
                case GodPowerType.SpawnTribe:
                    return DivineActKind.Shaping;
                case GodPowerType.Lightning:
                case GodPowerType.Wildfire:
                case GodPowerType.Earthquake:
                case GodPowerType.Meteor:
                case GodPowerType.Plague:
                case GodPowerType.Flood:
                case GodPowerType.Drought:
                    return DivineActKind.Wrath;
                default:
                    return DivineActKind.None;
            }
        }

        /// <summary>+1 for gifts, -1 for punishment (cast or blamed), 0 for shaping/none. Used to detect an erratic god.</summary>
        public static int Polarity(DivineActKind kind)
        {
            switch (kind)
            {
                case DivineActKind.Blessing: return 1;
                case DivineActKind.Wrath:
                case DivineActKind.BlamedDisaster: return -1;
                default: return 0;
            }
        }
    }
}
