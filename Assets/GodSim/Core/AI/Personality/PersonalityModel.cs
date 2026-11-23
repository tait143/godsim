using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Pure, allocation-free mapping from a civilization's experience counters to trait targets.
    /// target(trait) = race default + sum of per-cause contributions, clamped to [default ± MaxDeviation] ∩ [0, 1].
    /// Every experience is turned into a saturating level x/(x+k) in [0, 1) so no amount of history can explode a trait.
    /// </summary>
    public static class PersonalityModel
    {
        public const float MaxStepPerRun = 0.02f;
        public const float MaxDeviation = 0.35f;
        public const float NotableDeviation = 0.2f;
        public const float CalmDeviation = 0.15f;
        public const float ExperienceHalfLifeYears = 30f;
        public const float MaxExperience = 1000f;
        public const int ChronicleCooldownYears = 50;

        // Saturation constant per cause (experience at which the level reaches 0.5).
        private static readonly float[] s_scale = { 6f, 6f, 30f, 20f, 8f, 5f, 5f, 15f, 4f, 6f, 60f, 3f };

        /// <summary>Saturated 0..1 level of a civ's experience of a cause.</summary>
        public static float Level(Civilization civ, PersonalityCause cause)
        {
            if (civ == null) return 0f;
            float x = MathUtil.Sanitize(civ.Mem(PersonalityKeys.ExperienceKey(cause)));
            if (x <= 0f) return 0f;
            return x / (x + s_scale[(int)cause]);
        }

        /// <summary>How proud (vs pragmatic) a race is, 0..1. Proud races answer defeat with revenge and suspicion.</summary>
        public static float Pride(GameDatabase db, RaceId race)
        {
            float x = AdvisorBoard.RaceTrait(db, race, AITrait.Xenophobia);
            float a = AdvisorBoard.RaceTrait(db, race, AITrait.Aggression);
            float d = AdvisorBoard.RaceTrait(db, race, AITrait.Diplomacy);
            return MathUtil.SmoothStep(0.3f, 0.6f, 0.5f * x + 0.3f * a + 0.2f * (1f - d));
        }

        /// <summary>+1: divine wrath drives the race to its knees in prayer; -1: wrath breeds defiance; 0: indifferent.</summary>
        public static float WrathPietyDirection(GameDatabase db, RaceId race)
            => MathUtil.Clamp((AdvisorBoard.RaceTrait(db, race, AITrait.Piety) - 0.5f) * 5f, -1f, 1f);

        public static int Index(AITrait t, PersonalityCause c) => (int)t * PersonalityKeys.CauseCount + (int)c;

        /// <summary>Fills <paramref name="c"/> (length TraitCount*CauseCount) with each cause's contribution to each trait.</summary>
        public static void Contributions(Civilization civ, GameDatabase db, float[] c)
        {
            if (c == null || c.Length < PersonalityKeys.TraitCount * PersonalityKeys.CauseCount) return;
            Array.Clear(c, 0, c.Length);
            if (civ == null) return;
            float v = Level(civ, PersonalityCause.Victory), d = Level(civ, PersonalityCause.Defeat);
            float p = Level(civ, PersonalityCause.Peace), t = Level(civ, PersonalityCause.Trade);
            float h = Level(civ, PersonalityCause.Hardship), b = Level(civ, PersonalityCause.Blessing);
            float w = Level(civ, PersonalityCause.Wrath), l = Level(civ, PersonalityCause.Learning);
            float e = Level(civ, PersonalityCause.Expansion), cr = Level(civ, PersonalityCause.Crowding);
            float k = Level(civ, PersonalityCause.Contact), inv = Level(civ, PersonalityCause.Invaded);
            float pride = Pride(db, civ.Race);
            float wrathDir = WrathPietyDirection(db, civ.Race);

            // Aggression: victories breed appetite for war; proud losers seek revenge, pragmatic ones grow war-weary.
            c[Index(AITrait.Aggression, PersonalityCause.Victory)] = 0.30f * v;
            c[Index(AITrait.Aggression, PersonalityCause.Defeat)] = (0.25f * pride - 0.10f * (1f - pride)) * d;
            c[Index(AITrait.Aggression, PersonalityCause.Peace)] = -0.22f * p;
            c[Index(AITrait.Aggression, PersonalityCause.Invaded)] = 0.08f * inv;
            // Xenophobia: defeats and invasions embitter the proud; contact with many peoples softens it.
            c[Index(AITrait.Xenophobia, PersonalityCause.Defeat)] = 0.30f * pride * d;
            c[Index(AITrait.Xenophobia, PersonalityCause.Invaded)] = 0.12f * inv;
            c[Index(AITrait.Xenophobia, PersonalityCause.Contact)] = -0.30f * k;
            // Diplomacy: pragmatic losers learn to negotiate; peace and trade teach it; victors grow arrogant.
            c[Index(AITrait.Diplomacy, PersonalityCause.Defeat)] = 0.28f * (1f - pride) * d;
            c[Index(AITrait.Diplomacy, PersonalityCause.Peace)] = 0.20f * p;
            c[Index(AITrait.Diplomacy, PersonalityCause.Trade)] = 0.15f * t;
            c[Index(AITrait.Diplomacy, PersonalityCause.Victory)] = -0.10f * v;
            // Mercantilism: long peace and trade agreements.
            c[Index(AITrait.Mercantilism, PersonalityCause.Peace)] = 0.12f * p;
            c[Index(AITrait.Mercantilism, PersonalityCause.Trade)] = 0.25f * t;
            // Piety: hardship and blessings raise it; wrath raises it for the pious and breeds defiance in the impious.
            c[Index(AITrait.Piety, PersonalityCause.Hardship)] = 0.25f * h;
            c[Index(AITrait.Piety, PersonalityCause.Blessing)] = 0.22f * b;
            c[Index(AITrait.Piety, PersonalityCause.Wrath)] = 0.30f * wrathDir * w;
            // Curiosity: research, new ages, and meeting other peoples.
            c[Index(AITrait.Curiosity, PersonalityCause.Learning)] = 0.30f * l;
            c[Index(AITrait.Curiosity, PersonalityCause.Contact)] = 0.06f * k;
            // Expansionism: successful colonies and conquests embolden; crowding turns a people inward.
            c[Index(AITrait.Expansionism, PersonalityCause.Expansion)] = 0.25f * e;
            c[Index(AITrait.Expansionism, PersonalityCause.Victory)] = 0.08f * v;
            c[Index(AITrait.Expansionism, PersonalityCause.Crowding)] = -0.30f * cr;
            // Industriousness: those who cannot grow outward build upward; hardship teaches toil.
            c[Index(AITrait.Industriousness, PersonalityCause.Crowding)] = 0.12f * cr;
            c[Index(AITrait.Industriousness, PersonalityCause.Hardship)] = 0.08f * h;
            c[Index(AITrait.Industriousness, PersonalityCause.Learning)] = 0.05f * l;
        }

        /// <summary>Target value of a trait given contributions computed by <see cref="Contributions"/>.</summary>
        public static float Target(GameDatabase db, RaceId race, AITrait trait, float[] c)
        {
            float baseV = AdvisorBoard.RaceTrait(db, race, trait);
            float sum = 0f;
            int row = (int)trait * PersonalityKeys.CauseCount;
            for (int i = 0; i < PersonalityKeys.CauseCount; i++) sum += c[row + i];
            return ClampToBounds(baseV, baseV + sum);
        }

        /// <summary>Clamps a trait value to [raceDefault ± MaxDeviation] ∩ [0, 1]; NaN becomes the default.</summary>
        public static float ClampToBounds(float raceDefault, float v)
            => MathUtil.Clamp01(MathUtil.Clamp(MathUtil.Sanitize(v, raceDefault), raceDefault - MaxDeviation, raceDefault + MaxDeviation));

        /// <summary>Moves current toward target by a fraction of the gap, never more than maxStep.</summary>
        public static float Step(float current, float target, float maxStep)
        {
            float gap = target - current;
            if (Math.Abs(gap) < 0.0005f) return target;
            return current + MathUtil.Clamp(gap * 0.35f, -maxStep, maxStep);
        }

        /// <summary>Cause with the strongest contribution in the given direction (sign), or -1 if none is meaningful.</summary>
        public static int DominantCause(float[] c, AITrait trait, int sign)
        {
            int row = (int)trait * PersonalityKeys.CauseCount, best = -1;
            float bestV = 0.01f;
            for (int i = 0; i < PersonalityKeys.CauseCount; i++)
            {
                float x = c[row + i] * sign;
                if (x > bestV) { bestV = x; best = i; }
            }
            return best;
        }
    }
}
