using System;

namespace GodSim.Core.AI
{
    /// <summary>Pure, deterministic war-planning formulas and tuning (no state; safe for tests and UI).</summary>
    public static class WarPlanMath
    {
        // ------------------------------------------------------------------ tuning
        /// <summary>Tension at or above this starts preparing (phase Tension).</summary>
        public const float TensionEnter = 0.35f;
        /// <summary>Tension below this ends preparation / mobilization (hysteresis).</summary>
        public const float TensionLeave = 0.22f;
        /// <summary>Tension at or above this mobilizes immediately.</summary>
        public const float MobilizeTension = 0.75f;
        /// <summary>Tension at or above this re-arms a demobilizing civ.</summary>
        public const float RearmTension = 0.8f;
        /// <summary>Years of demobilization after a war.</summary>
        public const float DemobilizeYears = 5f;
        /// <summary>Years of standing down after a mobilization that never became a war.</summary>
        public const float StandDownYears = 2f;
        /// <summary>Weariness lost per year of peace.</summary>
        public const float WearinessDecayPerYear = 0.25f;
        /// <summary>Settlements further apart than this (tiles) are not neighbours.</summary>
        public const float NeighbourRange = 40f;
        /// <summary>Own settlements within this many tiles of the nearest one to the enemy count as full frontier.</summary>
        public const float FrontierSlack = 6f;
        /// <summary>Distance beyond the frontier over which settlements fade into the interior.</summary>
        public const float InteriorFalloff = 18f;

        /// <summary>
        /// Tension 0..1 toward one rival from the stance, opinion, opinion trend (per year), the rival's aggression
        /// near our borders (proximity 0..1) and its relative strength. A truce halves it.
        /// </summary>
        public static float Tension(DiplomaticStance stance, float opinion, float trendPerYear, float rivalAggression,
                                    float proximity01, float strengthRatio, bool truce)
        {
            if (stance == DiplomaticStance.War) return 1f;
            if (stance == DiplomaticStance.Allied) return 0f;
            opinion = MathUtil.Sanitize(opinion);
            float t = 0f;
            if (stance == DiplomaticStance.Hostile) t += 0.3f;
            t += 0.3f * MathUtil.InverseLerp(-10f, -70f, opinion);
            float trend = MathUtil.Sanitize(trendPerYear);
            if (trend < 0f && opinion < 10f) t += 0.25f * MathUtil.Clamp01(-trend / 20f);
            float aggr = MathUtil.SmoothStep(0.45f, 0.9f, MathUtil.Clamp01(MathUtil.Sanitize(rivalAggression, 0.5f)));
            float ratio = MathUtil.Clamp(MathUtil.Sanitize(strengthRatio, 1f), 0.5f, 1.5f) / 1.5f;
            float friendly = stance == DiplomaticStance.Friendly ? 0.3f : 1f;
            t += 0.3f * aggr * MathUtil.Clamp01(MathUtil.Sanitize(proximity01)) * ratio * friendly;
            if (truce) t *= 0.5f;
            return MathUtil.Clamp01(MathUtil.Sanitize(t));
        }

        /// <summary>Proximity 0..1 from the nearest settlement distance between two civs.</summary>
        public static float Proximity(float nearestDistance) => MathUtil.InverseLerp(NeighbourRange, 10f, MathUtil.Sanitize(nearestDistance, float.MaxValue));

        /// <summary>Years of tension before mobilizing: aggressive civs mobilize sooner (4 years at 0, 1 year at 1).</summary>
        public static float MobilizeDelayYears(float aggression) => MathUtil.Lerp(4f, 1f, MathUtil.Clamp01(MathUtil.Sanitize(aggression, 0.5f)));

        /// <summary>
        /// War weariness 0..1: own losses relative to the population at war start, years of war (aggressive civs
        /// tolerate longer wars) and a losing score (own perspective, -100..100). Diplomacy raises sensitivity.
        /// </summary>
        public static float Weariness(int ownLosses, int populationAtStart, float warYears, float ownScore, float aggression, float diplomacy)
        {
            float lossFrac = Math.Max(0, ownLosses) / (float)Math.Max(1, populationAtStart);
            float tolerable = MathUtil.Lerp(5f, 14f, MathUtil.Clamp01(MathUtil.Sanitize(aggression, 0.5f)));
            float sens = MathUtil.Lerp(0.6f, 1.6f, MathUtil.Clamp01(MathUtil.Sanitize(diplomacy, 0.5f)));
            float years = Math.Max(0f, MathUtil.Sanitize(warYears));
            float losing = MathUtil.Clamp01(-MathUtil.Sanitize(ownScore) / 100f);
            float raw = 2.5f * lossFrac + 0.5f * years / tolerable + 0.25f * losing;
            return MathUtil.Clamp01(MathUtil.Sanitize(raw * sens));
        }

        /// <summary>Frontier factor 0..1 (1 = among the settlements nearest the enemy, 0 = deep interior).</summary>
        public static float Frontier01(float distance, float nearestDistance)
        {
            distance = MathUtil.Sanitize(distance, float.MaxValue);
            nearestDistance = MathUtil.Sanitize(nearestDistance, float.MaxValue);
            if (nearestDistance >= float.MaxValue * 0.5f) return 0.5f;
            float edge = nearestDistance + FrontierSlack;
            if (distance <= edge) return 1f;
            return MathUtil.InverseLerp(edge + InteriorFalloff, edge, distance);
        }

        /// <summary>
        /// Soldier job vote for a phase: ramps with tension progress (0..1), peaks at war on the frontier
        /// (interior keeps a moderate share), softened by weariness; negative while demobilizing (fading with the remaining fraction).
        /// </summary>
        public static float SoldierVote(WarPhase phase, float frontier01, float progress01, float weariness, float demobRemaining01)
        {
            float f = MathUtil.Clamp01(MathUtil.Sanitize(frontier01));
            switch (phase)
            {
                case WarPhase.Tension: return (0.15f + 0.25f * MathUtil.Clamp01(MathUtil.Sanitize(progress01))) * MathUtil.Lerp(0.6f, 1f, f);
                case WarPhase.Mobilizing: return 0.55f * MathUtil.Lerp(0.6f, 1f, f);
                case WarPhase.AtWar: return MathUtil.Lerp(0.4f, 1f, f) * (1f - 0.3f * MathUtil.Clamp01(MathUtil.Sanitize(weariness)));
                case WarPhase.Demobilizing: return -MathUtil.Lerp(0.2f, 0.7f, MathUtil.Clamp01(MathUtil.Sanitize(demobRemaining01)));
                default: return 0f;
            }
        }

        /// <summary>Objective score (lower is better): distance inflated by the target's defense relative to our power.</summary>
        public static float ObjectiveScore(float distance, float defense, float ownPower)
        {
            float d = Math.Max(0f, MathUtil.Sanitize(distance, 1e6f));
            float def = Math.Max(0f, MathUtil.Sanitize(defense));
            float own = Math.Max(1f, MathUtil.Sanitize(ownPower, 1f));
            return MathUtil.Sanitize(d * (1f + def / own), 1e9f);
        }
    }
}
