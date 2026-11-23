namespace GodSim.Core.AI
{
    /// <summary>Evocative chronicle phrasing for personality shifts and adjectives for the UI.</summary>
    public static class PersonalityText
    {
        private static readonly string[] s_up =
        {
            "grew warlike and quick to the sword",          // Aggression
            "hungered for new lands",                        // Expansionism
            "turned fervently to the gods",                  // Piety
            "threw themselves into tireless labour",         // Industriousness
            "grew hungry for knowledge",                     // Curiosity
            "learned the worth of envoys and treaties",      // Diplomacy
            "grew suspicious of outsiders",                  // Xenophobia
            "became a nation of merchants",                  // Mercantilism
        };

        private static readonly string[] s_down =
        {
            "laid down their spears and grew gentle",
            "turned inward, content with the lands they held",
            "turned their backs on the gods",
            "grew idle and complacent",
            "lost their thirst for knowledge",
            "came to scorn the words of envoys",
            "opened their gates to strangers",
            "turned away from the markets",
        };

        /// <summary>Adjectives for a trait well above / below neutral.</summary>
        public static readonly string[] HighAdjective = { "warlike", "expansionist", "pious", "industrious", "inquisitive", "diplomatic", "xenophobic", "mercantile" };
        public static readonly string[] LowAdjective = { "peaceful", "insular", "irreverent", "idle", "traditional", "aloof", "cosmopolitan", "austere" };

        /// <summary>Opening clause explaining why a trait moved, e.g. "Bitter from their defeats".</summary>
        public static string Lead(int cause, AITrait trait, bool up)
        {
            switch ((PersonalityCause)cause)
            {
                case PersonalityCause.Victory: return up ? "Flushed with victory" : "Made arrogant by victory";
                case PersonalityCause.Defeat:
                    if (trait == AITrait.Diplomacy) return "Chastened by their defeats";
                    return up ? "Bitter from their defeats" : "Weary of fruitless wars";
                case PersonalityCause.Peace: return "After long years of peace";
                case PersonalityCause.Trade: return "Enriched by caravans from distant lands";
                case PersonalityCause.Hardship: return "Humbled by famine and calamity";
                case PersonalityCause.Blessing: return "Grateful for the gifts of heaven";
                case PersonalityCause.Wrath: return up ? "Trembling beneath the wrath of the heavens" : "Scarred by the wrath of a cruel god";
                case PersonalityCause.Learning: return "Awakened by new discoveries";
                case PersonalityCause.Expansion: return "Emboldened by their spreading settlements";
                case PersonalityCause.Crowding: return "Hemmed in on every side";
                case PersonalityCause.Contact: return "Mingling with many peoples";
                case PersonalityCause.Invaded: return "Betrayed by the invasions of their neighbours";
                default: return "As the generations passed";
            }
        }

        /// <summary>Full chronicle sentence, e.g. "Bitter from their defeats, the Elves of Sylvanor grew suspicious of outsiders."</summary>
        public static string Shift(Civilization civ, GameDatabase db, AITrait trait, bool up, int cause)
        {
            string people = "people";
            if (civ != null && db != null && (int)civ.Race < db.Races.Count && db.Races[(int)civ.Race] != null)
            {
                var r = db.Races[(int)civ.Race];
                if (!string.IsNullOrEmpty(r.PluralName)) people = r.PluralName;
            }
            string name = civ != null && !string.IsNullOrEmpty(civ.Name) ? civ.Name : "a forgotten realm";
            return Lead(cause, trait, up) + ", the " + people + " of " + name + " " + (up ? s_up[(int)trait] : s_down[(int)trait]) + ".";
        }
    }
}
