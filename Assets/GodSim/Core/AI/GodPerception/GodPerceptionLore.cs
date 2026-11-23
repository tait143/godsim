namespace GodSim.Core.AI
{
    /// <summary>Per-race interpretation multipliers (how strongly a people feels and how long it remembers).</summary>
    public struct RaceTemperament
    {
        public float Gratitude;
        public float Fear;
        public float Resentment;
        /// <summary>Memory length multiplier: 1 = baseline, 1.5 = remembers 50% longer.</summary>
        public float Memory;
        /// <summary>The gift this people treasures most (x1.5 gratitude).</summary>
        public GodPowerType Cherished;
        /// <summary>The punishment this people dreads most (x1.4 fear).</summary>
        public GodPowerType Dreaded;
    }

    /// <summary>
    /// Names mortals give their god and the readable text built from them. Every string used per run is
    /// precomputed (static tables), so voting with reasons does not allocate.
    /// </summary>
    public static class GodPerceptionLore
    {
        public const int EpithetNameless = 0;
        private const int RaceEpithetBase = 1;   // 1..5 by RaceId
        public const int EpithetMountainMaker = 13;
        public const int EpithetPeoplemaker = 14;

        private static readonly string[] s_epithets =
        {
            "the Nameless One",
            "the All-Father", "the Evergreen Mother", "the Deep Smith", "the Blood-Sky", "the Sun-Serpent",
            "the Storm-Father", "the Burning One", "the Earth-Shaker", "the Star-Hurler", "the Pale Rider", "the Drowner", "the Thirsting Sun",
            "the Mountain-Maker", "the Peoplemaker", "the Fire-Mountain", "the Hungering God",
        };

        private static readonly string[] s_nouns =
        {
            "omen",
            "gift", "gift", "gift", "gift", "gift",
            "storm", "wildfire", "earthquake", "falling star", "plague", "flood", "drought",
            "upheaval", "new people", "eruption", "famine",
        };

        private static readonly string[] s_appeaseReasons = BuildReasons("Offerings to calm ");
        private static readonly string[] s_thanksReasons = BuildReasons("Thanksgiving to ");
        private static readonly string[] s_spurnReasons = BuildReasons("No more temples for ");

        private static readonly RaceTemperament[] s_temperaments =
        {
            // Human: balanced; harvests matter, plague is feared.
            new RaceTemperament { Gratitude = 1f, Fear = 1f, Resentment = 1f, Memory = 1f, Cherished = GodPowerType.BountifulHarvest, Dreaded = GodPowerType.Plague },
            // Elf: long memory, love the forest, hate fire, slow to forgive.
            new RaceTemperament { Gratitude = 1.1f, Fear = 0.9f, Resentment = 1.2f, Memory = 1.5f, Cherished = GodPowerType.PlantForest, Dreaded = GodPowerType.Wildfire },
            // Dwarf: stubborn grudges, treasure ore, dread the shaking earth.
            new RaceTemperament { Gratitude = 0.9f, Fear = 0.9f, Resentment = 1.4f, Memory = 1.3f, Cherished = GodPowerType.RevealOre, Dreaded = GodPowerType.Earthquake },
            // Orc: respect raw power, short memory, little bitterness.
            new RaceTemperament { Gratitude = 0.8f, Fear = 0.8f, Resentment = 0.6f, Memory = 0.7f, Cherished = GodPowerType.Fertility, Dreaded = GodPowerType.Drought },
            // Saurian: pious and patient; fear the god, rarely resent it, remember everything.
            new RaceTemperament { Gratitude = 1.1f, Fear = 1.2f, Resentment = 0.5f, Memory = 1.5f, Cherished = GodPowerType.Rain, Dreaded = GodPowerType.Drought },
        };

        /// <summary>Number of epithets in the table.</summary>
        public static int EpithetCount => s_epithets.Length;

        /// <summary>The epithet text for an index (clamped; never null).</summary>
        public static string Epithet(int index) => s_epithets[ClampIndex(index)];

        /// <summary>What mortals call the event behind an epithet ("storm", "plague", "gift").</summary>
        public static string Noun(int index) => s_nouns[ClampIndex(index)];

        public static string AppeaseReason(int epithet) => s_appeaseReasons[ClampIndex(epithet)];
        public static string ThanksReason(int epithet) => s_thanksReasons[ClampIndex(epithet)];
        public static string SpurnReason(int epithet) => s_spurnReasons[ClampIndex(epithet)];

        /// <summary>The name a race gives to a bountiful god.</summary>
        public static int RaceEpithet(RaceId race)
        {
            int r = (int)race;
            return r >= 0 && r < Counts.Races ? RaceEpithetBase + r : EpithetNameless;
        }

        /// <summary>Temperament for a race (Human baseline for unknown ids).</summary>
        public static RaceTemperament Temperament(RaceId race)
        {
            int r = (int)race;
            return r >= 0 && r < s_temperaments.Length ? s_temperaments[r] : s_temperaments[0];
        }

        /// <summary>The epithet a civ gives the god after a power is cast on it.</summary>
        public static int EpithetFor(GodPowerType power, RaceId race)
        {
            switch (power)
            {
                case GodPowerType.Lightning: return 6;
                case GodPowerType.Wildfire: return 7;
                case GodPowerType.Earthquake: return 8;
                case GodPowerType.Meteor: return 9;
                case GodPowerType.Plague: return 10;
                case GodPowerType.Flood: return 11;
                case GodPowerType.Drought: return 12;
                case GodPowerType.RaiseLand:
                case GodPowerType.LowerLand: return EpithetMountainMaker;
                case GodPowerType.SpawnTribe: return EpithetPeoplemaker;
                default: return RaceEpithet(race);
            }
        }

        /// <summary>The epithet a pious civ gives the god it blames for a natural disaster.</summary>
        public static int EpithetFor(DisasterType disaster)
        {
            switch (disaster)
            {
                case DisasterType.Storm:
                case DisasterType.Lightning: return 6;
                case DisasterType.Wildfire: return 7;
                case DisasterType.Earthquake: return 8;
                case DisasterType.Meteor: return 9;
                case DisasterType.Plague: return 10;
                case DisasterType.Flood: return 11;
                case DisasterType.Drought: return 12;
                case DisasterType.Volcano: return 15;
                case DisasterType.Famine: return 16;
                default: return EpithetNameless;
            }
        }

        /// <summary>The god power a disaster resembles (for race dread), or null when none.</summary>
        public static GodPowerType? PowerLike(DisasterType disaster)
        {
            switch (disaster)
            {
                case DisasterType.Wildfire: return GodPowerType.Wildfire;
                case DisasterType.Earthquake: return GodPowerType.Earthquake;
                case DisasterType.Flood: return GodPowerType.Flood;
                case DisasterType.Drought: return GodPowerType.Drought;
                case DisasterType.Plague: return GodPowerType.Plague;
                case DisasterType.Meteor: return GodPowerType.Meteor;
                case DisasterType.Lightning:
                case DisasterType.Storm: return GodPowerType.Lightning;
                default: return null;
            }
        }

        private static int ClampIndex(int i) => i < 0 || i >= s_epithets.Length ? EpithetNameless : i;

        private static string[] BuildReasons(string prefix)
        {
            var a = new string[s_epithets.Length];
            for (int i = 0; i < a.Length; i++) a[i] = prefix + s_epithets[i];
            return a;
        }
    }
}
