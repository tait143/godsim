namespace GodSim.Core.AI
{
    /// <summary>The ten government definitions (immutable after static construction; shared by all civs).</summary>
    public static class GovernmentCatalog
    {
        /// <summary>Number of government types.</summary>
        public const int Count = 10;

        private static readonly GovernmentDefinition[] s_defs = Build();

        /// <summary>All definitions indexed by (int)GovernmentType.</summary>
        public static GovernmentDefinition[] All => s_defs;

        /// <summary>Definition for a type (falls back to Tribal Council on out-of-range values).</summary>
        public static GovernmentDefinition Get(GovernmentType t)
        {
            int i = (int)t;
            return i >= 0 && i < s_defs.Length ? s_defs[i] : s_defs[0];
        }

        /// <summary>Clamps a stored float index into a valid government type.</summary>
        public static GovernmentType FromFloat(float v)
        {
            v = MathUtil.Sanitize(v);
            int i = MathUtil.Clamp(MathUtil.RoundToInt(v), 0, Count - 1);
            return (GovernmentType)i;
        }

        //                                     Human Elf  Dwarf Orc  Saurian
        private static float[] Aff(float h, float e, float d, float o, float s) => new[] { h, e, d, o, s };

        private static GovernmentDefinition[] Build()
        {
            var a = new GovernmentDefinition[Count];
            a[0] = new GovernmentDefinition
            {
                Type = GovernmentType.TribalCouncil, Name = "Tribal Council", StateName = "Tribal Council", RulerTitle = "Elder Council",
                Faction = "The village elders", ReformReason = "as the elders called the clans to council",
                MinAge = AgeId.Dawn, MaxAge = AgeId.Bronze, IdealAge = 0f, RaceAffinity = Aff(0.5f, 0.7f, 0.5f, 0.4f, 0.5f),
                TraitPulls = new[] { new TraitPull(AITrait.Diplomacy, 0.4f) }, Authority = 0.1f,
                StabilityBonus = 0.05f, TaxEfficiency = 0.6f, TaxResentment = 1.2f, TaxTolerance = 0.15f, BaseTax = 0.08f, MaxTax = 0.25f,
                BaseConscription = 0.03f, MaxConscription = 0.4f, Militarism = 0.8f, DefaultFaith = FaithPolicy.Tolerant,
                AdvisorBiases = new[] { new AdvisorBias("food", 1.1f) },
            };
            a[1] = new GovernmentDefinition
            {
                Type = GovernmentType.Chiefdom, Name = "Chiefdom", StateName = "Chiefdom", RulerTitle = "Chief",
                Faction = "The warrior clans", ReformReason = "as a war-chief rose above the clans",
                MinAge = AgeId.Dawn, MaxAge = AgeId.Iron, IdealAge = 1f, RaceAffinity = Aff(0.5f, 0.3f, 0.5f, 0.8f, 0.5f),
                TraitPulls = new[] { new TraitPull(AITrait.Aggression, 0.5f) }, Authority = 0.6f,
                StabilityBonus = 0f, TaxEfficiency = 0.8f, TaxResentment = 1f, TaxTolerance = 0.2f, BaseTax = 0.12f, MaxTax = 0.35f,
                BaseConscription = 0.08f, MaxConscription = 0.6f, Militarism = 1.2f, DefaultFaith = FaithPolicy.Tolerant,
                AdvisorBiases = new[] { new AdvisorBias("defense", 1.1f) },
            };
            a[2] = new GovernmentDefinition
            {
                Type = GovernmentType.Theocracy, Name = "Theocracy", StateName = "Theocracy", RulerTitle = "High Priest",
                Faction = "The zealots of the faith", ReformReason = "as the priesthood's sway grew",
                MinAge = AgeId.Bronze, MaxAge = AgeId.Information, IdealAge = 2.5f, AgeFalloff = 0.12f, RaceAffinity = Aff(0.4f, 0.6f, 0.4f, 0.4f, 0.8f),
                TraitPulls = new[] { new TraitPull(AITrait.Piety, 1f), new TraitPull(AITrait.Curiosity, -0.2f) }, DevotionPull = 0.4f, Authority = 0.6f,
                StabilityBonus = 0.05f, TaxEfficiency = 0.9f, TaxResentment = 0.8f, TaxTolerance = 0.25f, BaseTax = 0.15f, MaxTax = 0.45f,
                BaseConscription = 0.05f, MaxConscription = 0.5f, Militarism = 0.9f, FaithZeal = 1.5f, DefaultFaith = FaithPolicy.StateFaith,
                AdvisorBiases = new[] { new AdvisorBias("faith", 1.2f) },
            };
            a[3] = new GovernmentDefinition
            {
                Type = GovernmentType.Monarchy, Name = "Monarchy", StateName = "Kingdom", RulerTitle = "King",
                Faction = "A royal pretender", ReformReason = "as a crown was forged for the strongest house",
                MinAge = AgeId.Bronze, MaxAge = AgeId.Industrial, IdealAge = 2.5f, RaceAffinity = Aff(0.7f, 0.4f, 0.7f, 0.5f, 0.4f),
                TraitPulls = new[] { new TraitPull(AITrait.Industriousness, 0.2f) }, Authority = 0.7f,
                StabilityBonus = 0.03f, TaxEfficiency = 1f, TaxResentment = 1f, TaxTolerance = 0.25f, BaseTax = 0.18f, MaxTax = 0.5f,
                BaseConscription = 0.06f, MaxConscription = 0.6f, Militarism = 1f, DefaultFaith = FaithPolicy.StateFaith,
                AdvisorBiases = new[] { new AdvisorBias("defense", 1.1f) },
            };
            a[4] = new GovernmentDefinition
            {
                Type = GovernmentType.FeudalRealm, Name = "Feudal Realm", StateName = "Feudal Realm", RulerTitle = "Liege Lord",
                Faction = "The great lords", ReformReason = "as the lords swore fealty for their lands",
                MinAge = AgeId.Iron, MaxAge = AgeId.Renaissance, IdealAge = 4f, RaceAffinity = Aff(0.6f, 0.3f, 0.7f, 0.5f, 0.4f),
                TraitPulls = new[] { new TraitPull(AITrait.Expansionism, 0.3f) }, LandPull = 0.3f, Authority = 0.5f,
                StabilityBonus = 0f, TaxEfficiency = 0.9f, TaxResentment = 1.1f, TaxTolerance = 0.25f, BaseTax = 0.18f, MaxTax = 0.45f,
                BaseConscription = 0.08f, MaxConscription = 0.6f, Militarism = 1.1f, DefaultFaith = FaithPolicy.StateFaith,
                AdvisorBiases = new[] { new AdvisorBias("defense", 1.15f), new AdvisorBias("food", 1.05f) },
            };
            a[5] = new GovernmentDefinition
            {
                Type = GovernmentType.MerchantRepublic, Name = "Merchant Republic", StateName = "Republic", RulerTitle = "Doge",
                Faction = "The Merchant Guilds", ReformReason = "as the guilds grew rich and claimed the council",
                MinAge = AgeId.Iron, MaxAge = AgeId.Industrial, IdealAge = 4.5f, RaceAffinity = Aff(0.7f, 0.4f, 0.5f, 0.2f, 0.4f),
                TraitPulls = new[] { new TraitPull(AITrait.Mercantilism, 1f), new TraitPull(AITrait.Aggression, -0.2f) }, Authority = 0.2f,
                StabilityBonus = 0.02f, TaxEfficiency = 1.3f, TaxResentment = 0.8f, TaxTolerance = 0.3f, BaseTax = 0.2f, MaxTax = 0.45f,
                BaseConscription = 0.03f, MaxConscription = 0.45f, Militarism = 0.8f, DefaultFaith = FaithPolicy.Tolerant,
                AdvisorBiases = new[] { new AdvisorBias("treasury", 1.2f) },
            };
            a[6] = new GovernmentDefinition
            {
                Type = GovernmentType.Empire, Name = "Empire", StateName = "Empire", RulerTitle = "Emperor",
                Faction = "The legions", ReformReason = "as conquest bound many cities under one throne",
                MinAge = AgeId.Iron, MaxAge = AgeId.Modern, IdealAge = 3.5f, RaceAffinity = Aff(0.5f, 0.3f, 0.4f, 0.8f, 0.5f),
                TraitPulls = new[] { new TraitPull(AITrait.Aggression, 0.5f), new TraitPull(AITrait.Expansionism, 0.3f) }, LandPull = 1.6f, Authority = 0.9f,
                StabilityBonus = -0.02f, TaxEfficiency = 1.1f, TaxResentment = 1.1f, TaxTolerance = 0.25f, BaseTax = 0.2f, MaxTax = 0.55f,
                BaseConscription = 0.1f, MaxConscription = 0.7f, Militarism = 1.4f, DefaultFaith = FaithPolicy.StateFaith,
                AdvisorBiases = new[] { new AdvisorBias("defense", 1.2f), new AdvisorBias("growth", 1.1f) },
            };
            a[7] = new GovernmentDefinition
            {
                Type = GovernmentType.ConstitutionalState, Name = "Constitutional State", StateName = "Constitutional State", RulerTitle = "Chancellor",
                Faction = "The people's assembly", ReformReason = "as a written charter bound the rulers to law",
                MinAge = AgeId.Renaissance, MaxAge = AgeId.Ascension, IdealAge = 6.5f, RaceAffinity = Aff(0.7f, 0.5f, 0.5f, 0.3f, 0.4f),
                TraitPulls = new[] { new TraitPull(AITrait.Diplomacy, 0.5f), new TraitPull(AITrait.Mercantilism, 0.2f) }, Authority = 0.2f,
                StabilityBonus = 0.08f, TaxEfficiency = 1.2f, TaxResentment = 0.9f, TaxTolerance = 0.3f, BaseTax = 0.22f, MaxTax = 0.5f,
                BaseConscription = 0.03f, MaxConscription = 0.5f, Militarism = 0.9f, DefaultFaith = FaithPolicy.Tolerant,
                AdvisorBiases = new[] { new AdvisorBias("growth", 1.1f), new AdvisorBias("treasury", 1.05f) },
            };
            a[8] = new GovernmentDefinition
            {
                Type = GovernmentType.Technocracy, Name = "Technocracy", StateName = "Technocracy", RulerTitle = "Prime Architect",
                Faction = "The academies", ReformReason = "as the scholars took the reins of state",
                MinAge = AgeId.Industrial, MaxAge = AgeId.Ascension, IdealAge = 8f, RaceAffinity = Aff(0.5f, 0.7f, 0.7f, 0.2f, 0.4f),
                TraitPulls = new[] { new TraitPull(AITrait.Curiosity, 1f), new TraitPull(AITrait.Piety, -0.2f) }, Authority = 0.5f,
                StabilityBonus = 0.04f, TaxEfficiency = 1.2f, TaxResentment = 1f, TaxTolerance = 0.3f, BaseTax = 0.22f, MaxTax = 0.5f,
                BaseConscription = 0.03f, MaxConscription = 0.5f, Militarism = 0.9f, FaithZeal = 0.6f, DefaultFaith = FaithPolicy.Secular,
                AdvisorBiases = new[] { new AdvisorBias("science", 1.2f) },
            };
            a[9] = new GovernmentDefinition
            {
                Type = GovernmentType.AscendantCommonwealth, Name = "Ascendant Commonwealth", StateName = "Ascendant Commonwealth", RulerTitle = "Concordant Council",
                Faction = "The Ascendant collective", ReformReason = "as every mind joined the great concord",
                MinAge = AgeId.Information, MaxAge = AgeId.Ascension, IdealAge = 9f, RaceAffinity = Aff(0.5f, 0.7f, 0.5f, 0.4f, 0.6f),
                TraitPulls = new[] { new TraitPull(AITrait.Curiosity, 0.3f), new TraitPull(AITrait.Diplomacy, 0.3f) }, Authority = 0.1f,
                StabilityBonus = 0.12f, TaxEfficiency = 1.3f, TaxResentment = 0.6f, TaxTolerance = 0.35f, BaseTax = 0.2f, MaxTax = 0.45f,
                BaseConscription = 0.02f, MaxConscription = 0.4f, Militarism = 0.7f, DefaultFaith = FaithPolicy.Tolerant,
                AdvisorBiases = new[] { new AdvisorBias("science", 1.15f), new AdvisorBias("growth", 1.1f) },
            };
            return a;
        }
    }
}
