namespace GodSim.Core.Heroes
{
    /// <summary>
    /// Pure, deterministic epithet generator ("the Wise", "Breaker of Sieges"). The same role, race, deeds
    /// tier and seed always produce the same title, so UI labels never flicker; titles grow grander as a
    /// hero's deeds cross tier thresholds.
    /// </summary>
    public static class HeroTitles
    {
        /// <summary>Deeds needed for tier 1 / tier 2. Tier 3 is legendary (deeds &gt; <see cref="HeroRules.LegendaryDeeds"/>).</summary>
        public const int Tier1Deeds = 5;
        public const int Tier2Deeds = 15;

        // [role][tier] epithets
        private static readonly string[][][] RoleEpithets =
        {
            new[] { // General
                new[] { "the Bold", "the Steadfast", "Shieldbearer" },
                new[] { "the Warlord", "the Unbowed", "Spear of the People" },
                new[] { "Breaker of Sieges", "Hammer of Foes", "the Conqueror" },
                new[] { "the Undefeated", "Lord of a Hundred Battles", "Breaker of Sieges" } },
            new[] { // Sage
                new[] { "the Wise", "the Learned", "the Curious" },
                new[] { "the Lorekeeper", "Reader of Stars", "the Scholar" },
                new[] { "the Enlightened", "Keeper of Knowledge", "the Far-Seeing" },
                new[] { "the All-Knowing", "Light of the Ages", "Who Read the World" } },
            new[] { // Prophet
                new[] { "the Devout", "the Faithful", "the Voice" },
                new[] { "the Seer", "Voice of the Heavens", "the Pious" },
                new[] { "the Blessed", "Hand of the God", "the Illumined" },
                new[] { "Chosen of the God", "the Undying Flame", "Who Spoke With Heaven" } },
            new[] { // Architect
                new[] { "the Builder", "the Mason", "Stonesetter" },
                new[] { "the Master Builder", "Raiser of Walls", "the Planner" },
                new[] { "Shaper of Cities", "the Great Architect", "Raiser of Towers" },
                new[] { "Maker of Wonders", "Who Built the World", "Founder of Cities" } },
            new[] { // Explorer
                new[] { "the Wanderer", "the Pathfinder", "the Restless" },
                new[] { "the Far-Traveled", "Finder of Ways", "the Scout" },
                new[] { "Seeker of Lost Places", "the Trailblazer", "Walker of Wilds" },
                new[] { "Who Walked the Edge of the World", "the Discoverer", "Finder of Lost Realms" } },
            new[] { // Merchant Prince
                new[] { "the Shrewd", "the Trader", "the Thrifty" },
                new[] { "the Wealthy", "Gold-Hand", "the Broker" },
                new[] { "the Magnificent", "Prince of Coin", "the Opulent" },
                new[] { "Lord of a Thousand Caravans", "the Golden", "Who Bought the Sun" } },
            new[] { // Healer
                new[] { "the Kind", "the Gentle", "the Mender" },
                new[] { "Mender of Wounds", "the Merciful", "Bane of Fevers" },
                new[] { "Bane of Plague", "the Lifegiver", "the Compassionate" },
                new[] { "Who Turned Back Death", "the Saint", "Hope of the Dying" } },
            new[] { // Bard
                new[] { "the Singer", "Sweet-Voiced", "the Merry" },
                new[] { "the Silver-Tongued", "the Storyteller", "the Minstrel" },
                new[] { "Voice of the Age", "the Beloved", "Master of Songs" },
                new[] { "Whose Songs Never End", "the Immortal Voice", "Heart of the People" } },
        };

        // [race] flavor epithets, used occasionally from tier 1 upward.
        private static readonly string[][] RaceEpithets =
        {
            new[] { "the Lionhearted", "the Just", "the Great" },          // Human
            new[] { "Starborn", "of the Silver Leaf", "the Evergreen" },   // Elf
            new[] { "Ironbeard", "Stoneheart", "Deep-Delver" },            // Dwarf
            new[] { "Ironjaw", "the Fierce", "the Untamed" },              // Orc
            new[] { "Sun-Blessed", "the Patient", "Scale-of-Gold" },       // Saurian
        };

        private static readonly string[] RoleNames = { "General", "Sage", "Prophet", "Architect", "Explorer", "Merchant Prince", "Healer", "Bard" };

        /// <summary>Display name of a role ("Merchant Prince").</summary>
        public static string RoleName(HeroRole role)
        {
            int i = (int)role;
            return i >= 0 && i < RoleNames.Length ? RoleNames[i] : "Hero";
        }

        /// <summary>0..3 renown tier for a deed count (3 = legendary).</summary>
        public static int Tier(int deeds)
        {
            if (deeds > HeroRules.LegendaryDeeds) return 3;
            if (deeds >= Tier2Deeds) return 2;
            if (deeds >= Tier1Deeds) return 1;
            return 0;
        }

        /// <summary>
        /// Deterministic epithet for a hero. <paramref name="seed"/> is typically the person id.
        /// <paramref name="racePlural"/> (optional, e.g. "Dwarves") enables "Glory of the Dwarves" style legendary titles.
        /// </summary>
        public static string Epithet(HeroRole role, RaceId race, int deeds, ulong seed, string racePlural = null)
        {
            int r = (int)role;
            if (r < 0 || r >= RoleEpithets.Length) return "the Renowned";
            int tier = Tier(deeds < 0 ? 0 : deeds);
            ulong h = Hashing.Combine(Hashing.Combine(seed, (ulong)(r + 1)), (ulong)((int)race * 31 + tier + 7));

            int pick = (int)(h % 8UL);
            if (tier == 3 && pick == 0 && !string.IsNullOrEmpty(racePlural))
                return "Glory of the " + racePlural;
            int ri = (int)race;
            if (tier >= 1 && pick == 1 && ri >= 0 && ri < RaceEpithets.Length)
            {
                var re = RaceEpithets[ri];
                return re[(int)((h >> 8) % (ulong)re.Length)];
            }
            var list = RoleEpithets[r][tier];
            return list[(int)((h >> 16) % (ulong)list.Length)];
        }
    }
}
