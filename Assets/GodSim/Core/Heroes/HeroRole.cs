namespace GodSim.Core.Heroes
{
    /// <summary>
    /// The calling of a notable individual. Stored numerically in <c>WorldState.Memory["hero.&lt;id&gt;.role"]</c>,
    /// so values must never be reordered or removed (append only).
    /// </summary>
    public enum HeroRole : byte
    {
        /// <summary>Soldier who leads armies and holds walls (army morale, siege defense).</summary>
        General = 0,
        /// <summary>Scholar whose insight feeds civilization research.</summary>
        Sage = 1,
        /// <summary>Priest who deepens devotion and settlement faith.</summary>
        Prophet = 2,
        /// <summary>Builder who speeds construction in their settlement.</summary>
        Architect = 3,
        /// <summary>Hunter/fisher who discovers deposits and ruins near the frontier.</summary>
        Explorer = 4,
        /// <summary>Merchant who brings gold to their settlement.</summary>
        MerchantPrince = 5,
        /// <summary>Healer who improves settlement and resident health.</summary>
        Healer = 6,
        /// <summary>Entertainer who lifts settlement and resident happiness.</summary>
        Bard = 7
    }
}
