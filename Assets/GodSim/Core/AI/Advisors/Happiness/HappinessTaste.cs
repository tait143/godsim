namespace GodSim.Core.AI
{
    /// <summary>
    /// Race- and personality-flavoured preferences among culture venues: Orcs roar in arenas, Humans gather in
    /// taverns and theaters, Elves favour gentle, pleasant venues, Dwarves love their ale halls, Saurians their
    /// fire rituals and plays. Values are additive utility bonuses (roughly -0.5..+0.6).
    /// </summary>
    public static class HappinessTaste
    {
        /// <summary>Hand-tuned racial taste for a culture building type.</summary>
        public static float Race(RaceId race, BuildingType t)
        {
            switch (race)
            {
                case RaceId.Orc:
                    switch (t)
                    {
                        case BuildingType.Arena: return 0.5f;
                        case BuildingType.Stadium: return 0.3f;
                        case BuildingType.Tavern: return 0.15f;
                        case BuildingType.Campfire: return 0.1f;
                        case BuildingType.Theater: return -0.2f;
                    }
                    break;
                case RaceId.Human:
                    switch (t)
                    {
                        case BuildingType.Tavern: return 0.35f;
                        case BuildingType.Theater: return 0.35f;
                        case BuildingType.Stadium: return 0.1f;
                        case BuildingType.Arena: return 0.05f;
                    }
                    break;
                case RaceId.Elf:
                    switch (t)
                    {
                        case BuildingType.Theater: return 0.3f;
                        case BuildingType.Campfire: return 0.25f;
                        case BuildingType.Tavern: return -0.05f;
                        case BuildingType.Stadium: return -0.2f;
                        case BuildingType.Arena: return -0.5f;
                    }
                    break;
                case RaceId.Dwarf:
                    switch (t)
                    {
                        case BuildingType.Tavern: return 0.5f;
                        case BuildingType.Arena: return 0.1f;
                        case BuildingType.Theater: return -0.05f;
                    }
                    break;
                case RaceId.Saurian:
                    switch (t)
                    {
                        case BuildingType.Theater: return 0.2f;
                        case BuildingType.Arena: return 0.15f;
                        case BuildingType.Campfire: return 0.15f;
                    }
                    break;
            }
            return 0f;
        }

        /// <summary>Blood-sport style spectacles favoured by aggressive peoples.</summary>
        public static bool IsMartialSpectacle(BuildingType t) => t == BuildingType.Arena || t == BuildingType.Stadium;

        /// <summary>
        /// Personality-driven taste (dynamic traits): aggression favours martial spectacles, peaceful peoples favour
        /// pleasant venues (Desirability), piety favours venues that also give Faith, mercantilism those giving Commerce.
        /// </summary>
        public static float Traits(BuildingDefinition d, RaceId race, float aggression, float piety, float mercantilism)
        {
            if (d == null) return 0f;
            float v = 0f;
            float desir = MathUtil.Clamp(MathUtil.Sanitize(d.Desirability), -1f, 1f);
            if (IsMartialSpectacle(d.Type)) v += (aggression - 0.5f) * 0.6f;
            else v += (0.5f - aggression) * 0.4f * desir;
            v += desir * (race == RaceId.Elf ? 0.4f : 0.15f);
            if (HappinessCatalog.ServicePoints(d, ServiceType.Faith) > 0f) v += piety * 0.3f;
            if (HappinessCatalog.ServicePoints(d, ServiceType.Commerce) > 0f) v += (mercantilism - 0.3f) * 0.3f;
            return MathUtil.Sanitize(v);
        }

        /// <summary>Short flavour phrase used in vote reasons when unrest must be calmed quickly.</summary>
        public static string CalmPhrase(RaceId race)
        {
            switch (race)
            {
                case RaceId.Orc: return "blood and glory for the crowd";
                case RaceId.Elf: return "songs beneath the leaves";
                case RaceId.Dwarf: return "ale and song in the halls";
                case RaceId.Saurian: return "rites and revels";
                default: return "bread and circuses";
            }
        }
    }
}
