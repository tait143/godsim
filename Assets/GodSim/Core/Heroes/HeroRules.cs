using System;

namespace GodSim.Core.Heroes
{
    /// <summary>
    /// Pure balance rules for heroes: who is eligible, which role a job leads to, how likely a civ is to
    /// raise a hero, and the per-run effect magnitudes. All values are small on purpose: heroes give
    /// history faces, they do not decide it.
    /// </summary>
    public static class HeroRules
    {
        /// <summary>Living heroes allowed per civilization.</summary>
        public const int MaxHeroesPerCiv = 3;
        /// <summary>Living heroes allowed per settlement.</summary>
        public const int MaxHeroesPerSettlement = 1;
        /// <summary>A civ needs at least this many living people before it can raise heroes.</summary>
        public const int MinCivPopulation = 8;
        /// <summary>Minimum <see cref="PromiseScore"/> to be considered.</summary>
        public const float MinPromise = 1.15f;
        /// <summary>Deeds strictly above this make a hero legendary (importance-3 biography).</summary>
        public const int LegendaryDeeds = 25;
        /// <summary>Deeds are capped to keep values sane.</summary>
        public const float MaxDeeds = 9999f;

        /// <summary>Reference interval all per-run magnitudes are expressed for.</summary>
        public const int ReferenceInterval = 24;

        // ---- per-run effects (at ReferenceInterval ticks)
        public const float GeneralMoraleBonus = 0.05f;
        public const float GeneralMoraleCap = 1.2f;
        public const float GeneralSiegeDefenseMultiplier = 1.10f;
        public const float SageResearchBase = 2f;
        public const float SageResearchPerAge = 0.5f;
        public const float ProphetDevotion = 0.01f;
        public const float ProphetFaith = 0.01f;
        public const float ArchitectProgress = 0.02f;
        public const float ExplorerDiscoveriesPerYear = 0.4f;
        public const float ExplorerRuinsChance = 0.25f;
        public const float MerchantGoldBase = 1.5f;
        public const float MerchantGoldPerAge = 0.25f;
        public const float HealerSettlementHealth = 0.01f;
        public const float HealerResidentHealth = 0.005f;
        public const float BardSettlementHappiness = 0.01f;
        public const float BardResidentHappiness = 0.005f;

        // ---- deeds per run for continuous roles (4 runs per year => 1 deed per year)
        public const float SteadyDeedsPerRun = 0.25f;
        public const float GeneralCampaignDeedsPerRun = 0.5f;
        public const float GeneralSiegeDeedsPerRun = 1f;

        /// <summary>Traits that mark a promising individual.</summary>
        public const PersonTraits PromisingTraits = PersonTraits.Hero | PersonTraits.Wise | PersonTraits.Charismatic | PersonTraits.Brave | PersonTraits.Clever;

        /// <summary>Maps a job to the hero role it can lead to, or null if the job produces no heroes.</summary>
        public static HeroRole? RoleForJob(JobType job)
        {
            switch (job)
            {
                case JobType.Soldier: return HeroRole.General;
                case JobType.Scholar: return HeroRole.Sage;
                case JobType.Priest: return HeroRole.Prophet;
                case JobType.Builder: return HeroRole.Architect;
                case JobType.Merchant: return HeroRole.MerchantPrince;
                case JobType.Healer: return HeroRole.Healer;
                case JobType.Entertainer: return HeroRole.Bard;
                case JobType.Hunter:
                case JobType.Fisher: return HeroRole.Explorer;
                default: return null;
            }
        }

        /// <summary>The signature trait granted (with <see cref="PersonTraits.Hero"/>) when a hero rises.</summary>
        public static PersonTraits RoleTrait(HeroRole role)
        {
            switch (role)
            {
                case HeroRole.General: return PersonTraits.Brave;
                case HeroRole.Sage: return PersonTraits.Wise;
                case HeroRole.Prophet: return PersonTraits.Devout;
                case HeroRole.Architect: return PersonTraits.Clever;
                case HeroRole.Explorer: return PersonTraits.Brave;
                case HeroRole.MerchantPrince: return PersonTraits.Charismatic;
                case HeroRole.Healer: return PersonTraits.Wise;
                case HeroRole.Bard: return PersonTraits.Charismatic;
                default: return PersonTraits.None;
            }
        }

        /// <summary>Skill plus bonuses for promising traits and experience; Lazy is a penalty.</summary>
        public static float PromiseScore(Person p)
        {
            if (p == null) return 0f;
            float score = MathUtil.Clamp(MathUtil.Sanitize(p.Skill, 1f), 0f, 3f);
            PersonTraits t = p.Traits & PromisingTraits;
            int n = 0;
            while (t != 0) { n++; t &= t - 1; }
            score += 0.15f * n;
            score += 0.2f * MathUtil.Clamp01(MathUtil.Sanitize(p.Experience));
            if (p.HasTrait(PersonTraits.Lazy)) score -= 0.15f;
            return score;
        }

        /// <summary>Alive adult (or elder) with a hero-producing job and enough promise. Does not check existing hero status.</summary>
        public static bool IsEligible(Person p)
        {
            if (p == null || !p.IsAlive || !p.IsAdult) return false;
            if (RoleForJob(p.Job) == null) return false;
            return PromiseScore(p) >= MinPromise;
        }

        /// <summary>Per-year chance that a civilization raises a new hero, scaled by population and age.</summary>
        public static float RaiseChancePerYear(int population, AgeId age)
        {
            if (population < MinCivPopulation) return 0f;
            float p = MathUtil.Clamp(0.03f + 0.0008f * population, 0f, 0.3f);
            p *= 1f + 0.08f * (int)age;
            return MathUtil.Clamp01(p);
        }

        /// <summary>Research a Sage adds per run at the reference interval.</summary>
        public static float SageResearch(AgeId age) => SageResearchBase + SageResearchPerAge * (int)age;

        /// <summary>Gold a Merchant Prince adds per run at the reference interval.</summary>
        public static float MerchantGold(AgeId age, float skill) =>
            (MerchantGoldBase + MerchantGoldPerAge * (int)age) * MathUtil.Clamp(MathUtil.Sanitize(skill, 1f), 0.5f, 1.5f);

        /// <summary>True when deeds exceed the legendary threshold.</summary>
        public static bool IsLegendary(float deeds) => MathUtil.Sanitize(deeds) > LegendaryDeeds;

        /// <summary>Raises v by delta without exceeding cap; never lowers a value already above cap.</summary>
        public static float Nudge(float v, float delta, float cap)
        {
            v = MathUtil.Sanitize(v);
            if (v >= cap || !(delta > 0f)) return v;
            return Math.Min(cap, v + delta);
        }

        /// <summary>Deposit (or ruins) an explorer finds on a biome, from a uniform roll in [0,1).</summary>
        public static TileFlags DiscoveryFor(Biome biome, float ruinsRoll, float depositRoll)
        {
            if (ruinsRoll < ExplorerRuinsChance) return TileFlags.Ruins;
            switch (biome)
            {
                case Biome.Hills:
                case Biome.Mountain:
                    return depositRoll < 0.5f ? TileFlags.OreDeposit : (depositRoll < 0.8f ? TileFlags.StoneDeposit : TileFlags.GoldDeposit);
                case Biome.Desert:
                case Biome.Savanna:
                case Biome.Beach:
                    return depositRoll < 0.3f ? TileFlags.GoldDeposit : TileFlags.StoneDeposit;
                default:
                    return depositRoll < 0.7f ? TileFlags.StoneDeposit : TileFlags.OreDeposit;
            }
        }
    }
}
