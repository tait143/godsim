using System;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>Read-only personality helpers for UI, tooltips and other AI modules.</summary>
    public static class PersonalityQueries
    {
        private const int Candidates = PersonalityKeys.TraitCount + 1; // + derived "proud/pragmatic"
        private const float MinStrength = 0.12f;

        /// <summary>
        /// Returns 2-3 adjectives describing the civ's current character, strongest first (e.g. "proud, warlike, pious").
        /// Traits that history pushed away from the race default weigh more, so a nation's story shows in its description.
        /// </summary>
        public static string Describe(Civilization civ, GameDatabase db)
        {
            if (civ == null) return "unknown";
            var strength = new float[Candidates];
            var words = new string[Candidates];
            for (int i = 0; i < PersonalityKeys.TraitCount; i++)
            {
                var t = (AITrait)i;
                float v = AdvisorBoard.Trait(civ, db, t);
                float race = AdvisorBoard.RaceTrait(db, civ.Race, t);
                bool high = v >= 0.5f;
                float drift = high ? v - race : race - v;
                strength[i] = Math.Abs(v - 0.5f) + 0.75f * Math.Max(0f, drift);
                words[i] = high ? PersonalityText.HighAdjective[i] : PersonalityText.LowAdjective[i];
            }
            float pride = 0.5f * AdvisorBoard.Trait(civ, db, AITrait.Xenophobia) + 0.3f * AdvisorBoard.Trait(civ, db, AITrait.Aggression)
                          + 0.2f * (1f - AdvisorBoard.Trait(civ, db, AITrait.Diplomacy));
            strength[Candidates - 1] = Math.Abs(pride - 0.45f) * 1.1f;
            words[Candidates - 1] = pride >= 0.45f ? "proud" : "pragmatic";

            var sb = new StringBuilder(48);
            var used = new bool[Candidates];
            for (int n = 0; n < 3; n++)
            {
                int best = -1;
                for (int i = 0; i < Candidates; i++)
                    if (!used[i] && (best < 0 || strength[i] > strength[best])) best = i;
                if (best < 0 || (n >= 2 && strength[best] < MinStrength)) break;
                used[best] = true;
                if (sb.Length > 0) sb.Append(", ");
                sb.Append(words[best]);
            }
            return sb.ToString();
        }

        /// <summary>Current deviation of a trait from the race default (positive = history pushed it up).</summary>
        public static float Drift(Civilization civ, GameDatabase db, AITrait trait)
            => civ == null ? 0f : AdvisorBoard.Trait(civ, db, trait) - AdvisorBoard.RaceTrait(db, civ.Race, trait);

        /// <summary>Saturated 0..1 level of an experience (e.g. how scarred by defeat the civ is).</summary>
        public static float Experience(Civilization civ, PersonalityCause cause) => PersonalityModel.Level(civ, cause);
    }
}
