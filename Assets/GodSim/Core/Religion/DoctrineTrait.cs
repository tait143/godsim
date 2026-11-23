namespace GodSim.Core.Religion
{
    /// <summary>
    /// Inputs (each 0..1) that drive deterministic doctrine selection. Derived from the race personality
    /// (<see cref="RaceDefinition"/>), the god's history (<see cref="GodState"/>) and the faith's land.
    /// See <see cref="DoctrineSelector.Features"/>.
    /// </summary>
    public enum DoctrineTrait : byte
    {
        /// <summary>RaceDefinition.Piety.</summary>
        Piety = 0,
        /// <summary>RaceDefinition.Aggression.</summary>
        Aggression = 1,
        /// <summary>RaceDefinition.Curiosity.</summary>
        Curiosity = 2,
        /// <summary>RaceDefinition.Mercantilism.</summary>
        Mercantilism = 3,
        /// <summary>1 - Aggression.</summary>
        Gentleness = 4,
        /// <summary>1 - Curiosity.</summary>
        Tradition = 5,
        /// <summary>1 - Mercantilism.</summary>
        Frugality = 6,
        /// <summary>1 - Piety.</summary>
        Skepticism = 7,
        /// <summary>max(0, GodState.Alignment): the god has been kind.</summary>
        Benevolence = 8,
        /// <summary>max(0, -GodState.Alignment): the god has been wrathful.</summary>
        Wrath = 9,
        /// <summary>How often the god has intervened (powers cast, saturating).</summary>
        Activity = 10,
        /// <summary>(1 - Activity) * (1 - |Alignment|): a silent, neutral god.</summary>
        Aloofness = 11,
        /// <summary>Activity * (1 - |Alignment|): an active god whose deeds are mixed.</summary>
        Ambivalence = 12,
        /// <summary>1 when the faith's holy site stands on a Sacred tile.</summary>
        SacredLand = 13
    }
}
