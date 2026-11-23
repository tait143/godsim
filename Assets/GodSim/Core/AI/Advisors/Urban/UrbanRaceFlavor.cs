using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Race (and personality) flavour applied to archetype category targets: Elves build less industry, Dwarves more
    /// industry and storage, Orcs more military and arenas, Saurians more temples and fisheries, Humans more commerce.
    /// Data-driven on top: every building in <see cref="RaceDefinition.FavoredBuildings"/> raises its category, and the
    /// civ's dynamic personality drift (trait minus race default) nudges related categories.
    /// </summary>
    public static class UrbanRaceFlavor
    {
        public const float FavoredBonus = 0.08f;
        public const float MinMultiplier = 0.3f;
        public const float MaxMultiplier = 2.5f;

        // Housing Food Res Ind Stor Civic Rel Edu Health Culture Mil Commerce Infra Wonder
        private static readonly float[][] s_table =
        {
            new[] { 1f, 1.1f, 1f, 1f, 1f, 1.1f, 1f, 1f, 1f, 1f, 0.9f, 1.4f, 1f, 1f },          // Human: trade towns
            new[] { 1f, 1f, 0.9f, 0.55f, 0.9f, 1f, 1.25f, 1.3f, 1.25f, 1.1f, 0.85f, 0.9f, 1f, 1f }, // Elf: green, scholarly
            new[] { 1f, 0.95f, 1.3f, 1.5f, 1.4f, 1f, 0.9f, 1f, 1f, 0.85f, 1.2f, 1f, 1.1f, 1f },  // Dwarf: forges & vaults
            new[] { 1f, 1.05f, 1f, 1f, 0.9f, 0.9f, 0.9f, 0.7f, 0.9f, 1.3f, 1.6f, 0.8f, 0.9f, 1f }, // Orc: war camps & arenas
            new[] { 1f, 1.3f, 0.9f, 0.8f, 1f, 1f, 1.5f, 1f, 1.15f, 1f, 0.9f, 1f, 1f, 1f },       // Saurian: temples & fisheries
        };

        /// <summary>Fills <paramref name="mult"/> (indexed by BuildingCategory) for a civ. Allocation-free.</summary>
        public static void Fill(GameDatabase db, Civilization civ, float[] mult)
        {
            if (mult == null) return;
            for (int c = 0; c < mult.Length; c++) mult[c] = 1f;
            if (civ == null) return;

            int r = (int)civ.Race;
            var row = r >= 0 && r < s_table.Length ? s_table[r] : null;
            if (row != null) for (int c = 0; c < row.Length && c < mult.Length; c++) mult[c] = row[c];

            var race = UrbanRoleResolver.RaceOf(db, civ.Race);
            var fav = race != null ? race.FavoredBuildings : null;
            if (fav != null && db != null)
            {
                for (int i = 0; i < fav.Length; i++)
                {
                    if (!db.TryGetBuilding(fav[i], out var def) || def == null) continue;
                    int c = (int)def.Category;
                    if (c >= 0 && c < mult.Length) mult[c] += FavoredBonus;
                }
            }

            // Personality drift relative to the race default (history makes civs diverge from their stereotype).
            Drift(db, civ, mult, BuildingCategory.Religion, AITrait.Piety, 0.6f);
            Drift(db, civ, mult, BuildingCategory.Military, AITrait.Aggression, 0.6f);
            Drift(db, civ, mult, BuildingCategory.Industry, AITrait.Industriousness, 0.6f);
            Drift(db, civ, mult, BuildingCategory.Resource, AITrait.Industriousness, 0.3f);
            Drift(db, civ, mult, BuildingCategory.Education, AITrait.Curiosity, 0.6f);
            Drift(db, civ, mult, BuildingCategory.Commerce, AITrait.Mercantilism, 0.6f);

            for (int c = 0; c < mult.Length; c++)
                mult[c] = MathUtil.Clamp(MathUtil.Sanitize(mult[c], 1f), MinMultiplier, MaxMultiplier);
        }

        /// <summary>Convenience (allocates; UI/tests only): the flavour multiplier of one category for a civ.</summary>
        public static float Multiplier(GameDatabase db, Civilization civ, BuildingCategory category)
        {
            var m = new float[UrbanArchetypeCatalog.CategoryCount];
            Fill(db, civ, m);
            int c = (int)category;
            return c >= 0 && c < m.Length ? m[c] : 1f;
        }

        private static void Drift(GameDatabase db, Civilization civ, float[] mult, BuildingCategory cat, AITrait trait, float k)
        {
            int c = (int)cat;
            if (c < 0 || c >= mult.Length) return;
            float d = AdvisorBoard.Trait(civ, db, trait) - AdvisorBoard.RaceTrait(db, civ.Race, trait);
            mult[c] *= 1f + k * MathUtil.Clamp(MathUtil.Sanitize(d), -1f, 1f);
        }
    }
}
