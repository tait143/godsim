namespace GodSim.Core.AI
{
    /// <summary>
    /// The civilization personality the governor uses this run. Values come from
    /// <see cref="AdvisorBoard.Trait"/> (race defaults that drift with history), never directly from
    /// <see cref="RaceDefinition"/>. Reused between settlements (no allocation per run).
    /// </summary>
    public sealed class GovernorPersonality
    {
        /// <summary>0..1 warlike.</summary>
        public float Aggression = 0.5f;
        /// <summary>0..1 eager to spread.</summary>
        public float Expansionism = 0.5f;
        /// <summary>0..1 religious.</summary>
        public float Piety = 0.5f;
        /// <summary>0..1 hard-working / industrial.</summary>
        public float Industriousness = 0.5f;
        /// <summary>0..1 scholarly.</summary>
        public float Curiosity = 0.5f;
        /// <summary>0..1 diplomatic.</summary>
        public float Diplomacy = 0.5f;
        /// <summary>0..1 distrust of others.</summary>
        public float Xenophobia = 0.3f;
        /// <summary>0..1 trade-minded.</summary>
        public float Mercantilism = 0.5f;

        /// <summary>Loads the current (possibly drifted) traits of a civilization. Null civ = neutral 0.5 traits.</summary>
        public GovernorPersonality Load(Civilization civ, GameDatabase db)
        {
            Aggression = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            Expansionism = AdvisorBoard.Trait(civ, db, AITrait.Expansionism);
            Piety = AdvisorBoard.Trait(civ, db, AITrait.Piety);
            Industriousness = AdvisorBoard.Trait(civ, db, AITrait.Industriousness);
            Curiosity = AdvisorBoard.Trait(civ, db, AITrait.Curiosity);
            Diplomacy = AdvisorBoard.Trait(civ, db, AITrait.Diplomacy);
            Xenophobia = AdvisorBoard.Trait(civ, db, AITrait.Xenophobia);
            Mercantilism = AdvisorBoard.Trait(civ, db, AITrait.Mercantilism);
            return this;
        }
    }
}
