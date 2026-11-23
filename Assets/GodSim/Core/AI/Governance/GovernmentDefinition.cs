namespace GodSim.Core.AI
{
    /// <summary>Weight of one personality trait in a government's appeal.</summary>
    public struct TraitPull
    {
        public AITrait Trait;
        /// <summary>Score added per unit of (trait - 0.5) * 2; may be negative.</summary>
        public float Weight;
        public TraitPull(AITrait trait, float weight) { Trait = trait; Weight = weight; }
    }

    /// <summary>A multiplicative bias on one advisor's learned weight, applied when the government takes power.</summary>
    public struct AdvisorBias
    {
        public string AdvisorId;
        public float Multiplier;
        public AdvisorBias(string advisorId, float multiplier) { AdvisorId = advisorId; Multiplier = multiplier; }
    }

    /// <summary>Static data for one <see cref="GovernmentType"/>: eligibility, race and personality appeal, and policy effects.</summary>
    public sealed class GovernmentDefinition
    {
        public GovernmentType Type;
        /// <summary>Display name, e.g. "Merchant Republic".</summary>
        public string Name = "";
        /// <summary>State form used in "the {StateName} of {place}", e.g. "Republic".</summary>
        public string StateName = "";
        /// <summary>Ruler title used in "the {RulerTitle} of {place}", e.g. "King".</summary>
        public string RulerTitle = "";
        /// <summary>Faction that installs this government in a revolution, e.g. "The Merchant Guilds".</summary>
        public string Faction = "";
        /// <summary>Clause explaining a personality-driven reform, e.g. "as the guilds grew rich".</summary>
        public string ReformReason = "";

        // ---- eligibility
        public AgeId MinAge = AgeId.Dawn;
        public AgeId MaxAge = AgeId.Ascension;
        /// <summary>Age (fractional) where this government fits best.</summary>
        public float IdealAge;
        /// <summary>Score lost per age of distance from <see cref="IdealAge"/>.</summary>
        public float AgeFalloff = 0.3f;
        /// <summary>Affinity 0..1 per race (indexed by RaceId); 0.5 is neutral.</summary>
        public float[] RaceAffinity = { 0.5f, 0.5f, 0.5f, 0.5f, 0.5f };
        public TraitPull[] TraitPulls = new TraitPull[0];
        /// <summary>Appeal of settlement count (empires/realms need land): score += (min(n,8)/8 - 0.4) * this.</summary>
        public float LandPull;
        /// <summary>Appeal of the civ's devotion to the god: score += (Devotion - 0.5) * this.</summary>
        public float DevotionPull;
        /// <summary>Order and control 0..1; favored in crises and revolutions born of chaos.</summary>
        public float Authority = 0.5f;

        // ---- effects
        /// <summary>Additive bonus to the stability target.</summary>
        public float StabilityBonus;
        /// <summary>Multiplier on gold raised per tax point.</summary>
        public float TaxEfficiency = 1f;
        /// <summary>Multiplier on happiness lost per tax point.</summary>
        public float TaxResentment = 1f;
        /// <summary>Tax rate the populace accepts without losing faith in the regime.</summary>
        public float TaxTolerance = 0.25f;
        public float BaseTax = 0.15f;
        public float MaxTax = 0.5f;
        public float BaseConscription = 0.05f;
        public float MaxConscription = 0.6f;
        /// <summary>Multiplier on conscription's Soldier job vote and war-time conscription target.</summary>
        public float Militarism = 1f;
        /// <summary>Multiplier on the faith policy's faith-building vote.</summary>
        public float FaithZeal = 1f;
        public FaithPolicy DefaultFaith = FaithPolicy.Tolerant;
        /// <summary>Advisor weight multipliers applied on taking power (and undone on leaving it).</summary>
        public AdvisorBias[] AdvisorBiases = new AdvisorBias[0];

        public bool IsEligible(AgeId age) => age >= MinAge && age <= MaxAge;
    }
}
