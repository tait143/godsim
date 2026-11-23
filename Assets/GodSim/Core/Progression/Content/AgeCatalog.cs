using System;
using System.Collections.Generic;

namespace GodSim.Core.Progression
{
    /// <summary>
    /// Static content for the ten ages of civilization (Dawn .. Ascension): research thresholds,
    /// advancement requirements, global bonuses, soldier strength and renderer hints.
    /// Pure data: the research / age-advancement systems read these definitions from the database.
    /// </summary>
    public static class AgeCatalog
    {
        /// <summary>Fraction of the previous age's general (all-race) techs a civ must know to enter an age.</summary>
        public const float RequiredTechFraction = 0.6f;

        // Cumulative Civilization.TotalResearch needed to enter each age (indexed by AgeId).
        // Balance pass 2026-11-19: was { 0, 400, 1400, 3200, 6200, 10500, 16500, 25000, 36000, 50000 }, which let the
        // fastest civs reach Ascension by year 42-79. Each age's window was scaled by TechCatalog.CostScale
        // (x2 Dawn, x3 Bronze, x5 Iron, x8 Classical, x12 Medieval, x16 Renaissance, x20 Industrial, x24 Modern,
        // x28 Information), because research buildings make later ages research much faster than early ones.
        private static readonly float[] ResearchThresholds = { 0f, 800f, 3800f, 12800f, 36800f, 88400f, 184400f, 354400f, 618400f, 1010400f };

        // Civ-wide population needed to enter each age (indexed by AgeId).
        private static readonly int[] MinPopulations = { 0, 25, 50, 90, 160, 260, 400, 600, 850, 1200 };

        // Number of settlements needed to enter each age (indexed by AgeId).
        private static readonly int[] MinSettlementCounts = { 1, 1, 1, 2, 2, 3, 3, 4, 4, 5 };

        // Base combat strength of one soldier in each age (indexed by AgeId).
        private static readonly float[] SoldierStrengths = { 1f, 1.4f, 2f, 2.6f, 3.5f, 5f, 8f, 14f, 22f, 35f };

        private static readonly float[] ProductionBonuses = { 0f, 0.05f, 0.10f, 0.16f, 0.23f, 0.31f, 0.42f, 0.55f, 0.70f, 0.90f };
        private static readonly float[] ResearchBonuses = { 0f, 0.04f, 0.09f, 0.15f, 0.22f, 0.30f, 0.40f, 0.53f, 0.68f, 0.88f };
        private static readonly float[] LifespanBonuses = { 0f, 1f, 2f, 4f, 6f, 8f, 11f, 15f, 20f, 25f };
        private static readonly float[] HealthBonuses = { 0f, 0.02f, 0.04f, 0.07f, 0.09f, 0.12f, 0.16f, 0.21f, 0.26f, 0.30f };

        /// <summary>Registers all ten <see cref="AgeDefinition"/>s (fresh instances) into the database.</summary>
        /// <param name="db">A database that has not been frozen yet.</param>
        public static void Register(GameDatabase db)
        {
            Guard.NotNull(db, nameof(db));
            var defs = CreateDefinitions();
            for (int i = 0; i < defs.Count; i++) db.Register(defs[i]);
        }

        /// <summary>
        /// Number of previous-age techs required to enter <paramref name="age"/>:
        /// round(<see cref="RequiredTechFraction"/> x general techs of the previous age); 0 for Dawn.
        /// Only techs every race can research are counted, so the requirement is achievable for all races.
        /// </summary>
        public static int RequiredTechsFor(AgeId age)
        {
            if (age == AgeId.Dawn) return 0;
            int previousCount = TechCatalog.GeneralTechCount((AgeId)((int)age - 1));
            int required = (int)Math.Round(previousCount * RequiredTechFraction, MidpointRounding.AwayFromZero);
            return MathUtil.Clamp(required, 1, Math.Max(1, previousCount));
        }

        /// <summary>Builds new definitions for every age, ordered by AgeId. Each call returns new objects.</summary>
        public static List<AgeDefinition> CreateDefinitions()
        {
            var list = new List<AgeDefinition>(Counts.Ages);

            list.Add(Make(AgeId.Dawn, "Dawn Age", 0xC9A66BFFu,
                "Small bands of foragers and hunters huddle around the first fires. Stone tools, spoken lore and the spirits of the land are all they know.",
                "Thatch, hide and bent saplings: round huts, lean-tos and hide tents around a central fire pit; rough timber totems."));

            list.Add(Make(AgeId.Bronze, "Bronze Age", 0xCD7F32FFu,
                "Fields are sown, herds are kept and the first villages put down roots. Copper and tin are cast into bronze, and writing begins to record the harvest.",
                "Sun-dried mudbrick walls with flat reed roofs, plastered in ochre; timber lintels, low granaries and wattle fences."));

            list.Add(Make(AgeId.Iron, "Iron Age", 0x6E7377FFu,
                "Iron tools and weapons spread through every village. Organized faiths raise the first temples, and settlements ring themselves with walls.",
                "Dry-laid fieldstone foundations under timber frames and thatch or slate; stone temples and wooden watchtowers."));

            list.Add(Make(AgeId.Classical, "Classical Age", 0xE8E2D0FFu,
                "Philosophers, engineers and lawgivers shape the first great cities. Aqueducts carry water, theaters and forums fill with citizens, and trade crosses the seas.",
                "Dressed marble and limestone with colonnades and columns, pediments and terracotta roof tiles; arches and paved plazas."));

            list.Add(Make(AgeId.Medieval, "Medieval Age", 0x5B6E8CFFu,
                "Lords raise castles while guilds and monasteries order daily life. Cathedrals climb toward the heavens over crowded market towns.",
                "Mortared stone and half-timbered upper floors with steep timber roofs; keeps, curtain walls and pointed gothic arches."));

            list.Add(Make(AgeId.Renaissance, "Renaissance Age", 0xC0623BFFu,
                "Printing presses, universities and banks spread knowledge and wealth. Gunpowder and navigation redraw the map of power.",
                "Warm terracotta, stucco and cut stone with domes, loggias and symmetrical facades; clay tile roofs and courtyards."));

            list.Add(Make(AgeId.Industrial, "Industrial Age", 0x8E3B2EFFu,
                "Steam engines, coal and rail transform work and cities alike. Factories pull people from the fields into crowded, smoky towns.",
                "Red brick mills and row houses, iron girders and tall smokestacks; slate roofs, rail sheds and soot-stained walls."));

            list.Add(Make(AgeId.Modern, "Modern Age", 0x9AA5B1FFu,
                "Electricity, oil and mass production reshape everyday life. Hospitals, stadiums and highways serve cities of millions.",
                "Reinforced concrete and structural steel with large windows and flat roofs; apartment blocks, plants and broad avenues."));

            list.Add(Make(AgeId.Information, "Information Age", 0x3FA9F5FFu,
                "Computers and global networks connect every corner of the world. Knowledge, goods and people move faster than ever before.",
                "Glass curtain-wall towers on steel frames, solar panels and illuminated signage; sleek data centers and airports."));

            list.Add(Make(AgeId.Ascension, "Ascension Age", 0xF5E6A8FFu,
                "Scarcity is a memory and minds reach beyond flesh. The civilization prepares to transcend the mortal world through the Ascension Gate.",
                "Luminous white and gold surfaces, floating terraces and organic arcologies; glowing seams of light and crystalline spires."));

            return list;
        }

        private static AgeDefinition Make(AgeId id, string name, uint themeColor, string description, string architecture)
        {
            int i = (int)id;
            return new AgeDefinition
            {
                Id = id,
                Name = name,
                Description = description,
                ResearchThreshold = ResearchThresholds[i],
                RequiredTechsFromPreviousAge = RequiredTechsFor(id),
                MinPopulation = MinPopulations[i],
                MinSettlements = MinSettlementCounts[i],
                ProductionBonus = ProductionBonuses[i],
                ResearchBonus = ResearchBonuses[i],
                LifespanBonusYears = LifespanBonuses[i],
                HealthBonus = HealthBonuses[i],
                BaseSoldierStrength = SoldierStrengths[i],
                ThemeColor = themeColor,
                ArchitectureNotes = architecture
            };
        }
    }
}
