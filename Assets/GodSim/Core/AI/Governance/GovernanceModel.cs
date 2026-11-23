using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Pure, deterministic political model: how well a government suits a civ, where stability is heading,
    /// and which policies a regime wants. No state is mutated here.
    /// </summary>
    public static class GovernanceModel
    {
        /// <summary>Score bonus the sitting government enjoys (inertia of power).</summary>
        public const float Incumbency = 0.1f;
        /// <summary>Score lead a challenger needs for a peaceful reform without an age change.</summary>
        public const float ReformMargin = 0.2f;
        /// <summary>Score lead needed when the age just changed.</summary>
        public const float AgeReformMargin = 0.02f;
        /// <summary>Score lead needed during a crisis.</summary>
        public const float CrisisReformMargin = 0.08f;
        /// <summary>Below this stability a revolution is certain.</summary>
        public const float RevolutionThreshold = 0.12f;
        /// <summary>Below this stability a revolution may break out (per-year chance).</summary>
        public const float UnrestThreshold = 0.22f;
        /// <summary>Per-year revolution chance between the two thresholds.</summary>
        public const float RevolutionChancePerYear = 0.35f;
        /// <summary>Stability a regime starts with after a revolution / reform.</summary>
        public const float PostRevolutionStability = 0.42f;
        public const float PostReformStability = 0.6f;
        /// <summary>Gold raised per person per tick at a 100% tax rate (before government efficiency).</summary>
        public const float GoldPerCapitaPerTick = 0.02f;
        /// <summary>Happiness lost per year at a 100% tax rate (before resentment).</summary>
        public const float HappinessCostPerYear = 0.08f;
        /// <summary>Hard cap on happiness lost to taxes per run.</summary>
        public const float MaxHappinessCostPerRun = 0.05f;
        /// <summary>Taxes never push happiness below this floor.</summary>
        public const float HappinessFloor = 0.05f;
        /// <summary>Hard cap on gold raised per settlement per run.</summary>
        public const float MaxGoldPerRun = 250f;

        /// <summary>Appeal of a government for a civ (higher is better); float.MinValue when ineligible in this age.</summary>
        public static float Score(GovernmentDefinition def, Civilization civ, GameDatabase db, in GovernanceSnapshot snap, bool incumbent, float crisis)
        {
            if (def == null || civ == null) return float.MinValue;
            if (!def.IsEligible(civ.Age)) return float.MinValue;
            float s = 1f - Math.Abs((float)civ.Age - def.IdealAge) * def.AgeFalloff;
            int race = (int)civ.Race;
            if (def.RaceAffinity != null && race >= 0 && race < def.RaceAffinity.Length) s += (def.RaceAffinity[race] - 0.5f) * 0.4f;
            var pulls = def.TraitPulls;
            if (pulls != null)
                for (int i = 0; i < pulls.Length; i++)
                    s += (AdvisorBoard.Trait(civ, db, pulls[i].Trait) - 0.5f) * 2f * pulls[i].Weight;
            if (def.LandPull != 0f) s += (Math.Min(snap.Settlements, 8) / 8f - 0.4f) * def.LandPull;
            if (def.DevotionPull != 0f) s += (MathUtil.Clamp01(MathUtil.Sanitize(civ.Devotion, 0.5f)) - 0.5f) * def.DevotionPull;
            s += MathUtil.Clamp01(crisis) * def.Authority * 0.3f;
            if (incumbent) s += Incumbency;
            return MathUtil.Sanitize(s, float.MinValue);
        }

        /// <summary>Best eligible government (ties broken by lowest enum index). <paramref name="exclude"/> = -1 for none.</summary>
        public static GovernmentType Best(Civilization civ, GameDatabase db, in GovernanceSnapshot snap, GovernmentType current, int exclude, float crisis, out float bestScore)
        {
            var all = GovernmentCatalog.All;
            int best = -1;
            bestScore = float.MinValue;
            for (int i = 0; i < all.Length; i++)
            {
                if (i == exclude) continue;
                float sc = Score(all[i], civ, db, snap, i == (int)current, crisis);
                if (sc > bestScore) { bestScore = sc; best = i; }
            }
            if (best < 0) { best = (int)current; }
            return (GovernmentType)best;
        }

        /// <summary>Where stability drifts toward given current conditions (0..1).</summary>
        public static float StabilityTarget(GovernmentDefinition def, Civilization civ, in GovernanceSnapshot snap, float tax, float conscription, float yearsInPower)
        {
            float t = 0.55f + def.StabilityBonus;
            t += (MathUtil.Clamp01(snap.AvgHappiness) - 0.55f) * 0.6f;
            t -= MathUtil.Clamp01(snap.AvgUnrest) * 0.7f;
            t -= MathUtil.Clamp01(snap.FamineShare) * 0.25f;
            if (snap.LosingWar) t -= 0.1f;
            if (snap.LostWar) t -= 0.2f;
            if (snap.WonWar) t += 0.1f;
            int outdated = (int)civ.Age - (int)def.MaxAge;
            if (outdated > 0) t -= outdated * 0.12f;
            t -= Math.Max(0f, tax - def.TaxTolerance) * 0.8f;
            t -= conscription * (snap.AtWar ? 0.1f : 0.3f);
            int race = (int)civ.Race;
            if (def.RaceAffinity != null && race >= 0 && race < def.RaceAffinity.Length) t += (def.RaceAffinity[race] - 0.5f) * 0.2f;
            t += MathUtil.Clamp01(yearsInPower / 20f) * 0.08f;
            return MathUtil.Clamp01(MathUtil.Sanitize(t, 0.5f));
        }

        /// <summary>Tax rate the regime wants: raise when broke or at war, lower when unrest rises.</summary>
        public static float TaxTarget(GovernmentDefinition def, in GovernanceSnapshot snap)
        {
            float t = def.BaseTax;
            float perCapita = snap.Population > 0 ? snap.Gold / snap.Population : 0f;
            if (perCapita < 1f || snap.GoldNet < 0f) t += 0.15f;
            else if (perCapita > 10f) t -= 0.05f;
            if (snap.AtWar) t += 0.05f;
            if (snap.AvgUnrest > 0.2f) t -= (snap.AvgUnrest - 0.2f) * 0.6f + 0.05f;
            if (snap.AvgHappiness < 0.4f) t -= 0.08f;
            return MathUtil.Clamp(MathUtil.Sanitize(t, def.BaseTax), 0.02f, def.MaxTax);
        }

        /// <summary>Conscription the regime wants: high in war, a small standing levy in peace.</summary>
        public static float ConscriptionTarget(GovernmentDefinition def, Civilization civ, GameDatabase db, in GovernanceSnapshot snap)
        {
            float aggr = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            float t = def.BaseConscription + (aggr - 0.5f) * 0.1f;
            if (snap.AtWar) t += (0.3f + aggr * 0.15f) * def.Militarism;
            if (snap.LosingWar) t += 0.15f;
            if (!snap.AtWar && snap.AvgUnrest > 0.4f) t -= 0.05f;
            return MathUtil.Clamp(MathUtil.Sanitize(t, def.BaseConscription), 0f, def.MaxConscription);
        }

        /// <summary>Faith policy suited to the regime and the civ's piety.</summary>
        public static FaithPolicy FaithTarget(GovernmentDefinition def, Civilization civ, GameDatabase db)
        {
            float piety = AdvisorBoard.Trait(civ, db, AITrait.Piety);
            int p = (int)def.DefaultFaith;
            if (piety > 0.75f) p++;
            else if (piety < 0.25f) p--;
            if (def.Type == GovernmentType.Theocracy && piety > 0.6f) p = (int)FaithPolicy.Zealotry;
            return (FaithPolicy)MathUtil.Clamp(p, 0, 3);
        }

        /// <summary>Build vote strength for faith buildings under a faith policy (bounded to [-0.15, 1]).</summary>
        public static float FaithVote(FaithPolicy p, float zeal)
        {
            float v;
            switch (p)
            {
                case FaithPolicy.Secular: v = -0.15f; break;
                case FaithPolicy.Tolerant: v = 0.1f; break;
                case FaithPolicy.StateFaith: v = 0.35f; break;
                default: v = 0.6f; break;
            }
            if (v > 0f) v *= zeal;
            return MathUtil.Clamp(v, -0.15f, 1f);
        }

        /// <summary>Gold raised from a settlement in one run.</summary>
        public static float TaxGold(GovernmentDefinition def, int population, float tax, int deltaTicks, float tradeModifier)
        {
            float trade = MathUtil.Clamp(MathUtil.Sanitize(tradeModifier, 1f), 0.5f, 3f);
            float g = population * MathUtil.Clamp01(tax) * GoldPerCapitaPerTick * def.TaxEfficiency * Math.Max(0, deltaTicks) * trade;
            return MathUtil.Clamp(MathUtil.Sanitize(g), 0f, MaxGoldPerRun);
        }

        /// <summary>Happiness lost to taxes in one run (bounded by <see cref="MaxHappinessCostPerRun"/>).</summary>
        public static float TaxHappinessCost(GovernmentDefinition def, float tax, int deltaTicks)
        {
            float c = MathUtil.Clamp01(tax) * HappinessCostPerYear * def.TaxResentment * Math.Max(0, deltaTicks) / SimTime.TicksPerYear;
            return MathUtil.Clamp(MathUtil.Sanitize(c), 0f, MaxHappinessCostPerRun);
        }
    }
}
