namespace GodSim.Core.AI
{
    /// <summary>
    /// Blackboard keys and tuning constants of the <see cref="DefenseAdvisor"/>. Every key it writes lives under
    /// the "adv.def." namespace (settlement memory, except <see cref="CivShockTick"/> on civilization memory), is a
    /// bounded float and is removed again once it stops carrying information, so saves stay small.
    /// </summary>
    public static class DefenseAdvisorKeys
    {
        // ------------------------------------------------------------------ trend & memory (settlement)
        /// <summary>Distance (tiles) to the nearest hostile army within <see cref="ArmyRadius"/> at the last run; absent = none.</summary>
        public const string ArmyDist = "adv.def.armyDist";
        /// <summary>Smoothed closing speed of that army (tiles per tick, positive = approaching).</summary>
        public const string ArmyClosing = "adv.def.armyClose";
        /// <summary>Tick of the latest battle fought at (or right next to) this settlement.</summary>
        public const string BattleTick = "adv.def.battleTick";
        /// <summary>Recency-weighted soldiers this settlement lost in recent battles.</summary>
        public const string BattleLosses = "adv.def.battleLoss";
        /// <summary>1 when the latest battle here was lost.</summary>
        public const string BattleLost = "adv.def.battleLost";
        /// <summary>Mobilisation alert 0..1: jumps with imminent danger, decays slowly (drives demobilisation).</summary>
        public const string Alert = "adv.def.alert";
        /// <summary>Smoothed hostile military power projected onto this settlement (arms-race detection).</summary>
        public const string EnemyPower = "adv.def.enemyPow";
        /// <summary>Tick of the advisor's last own explanation line in the settlement AI log (throttle).</summary>
        public const string LogTick = "adv.def.logTick";

        // ------------------------------------------------------------------ explanation (settlement, UI/debug)
        /// <summary>Overall threat 0..1 at the last run.</summary>
        public const string Threat = "adv.def.threat";
        /// <summary>Our defense divided by the force we expect to face (0..5).</summary>
        public const string Ratio = "adv.def.ratio";
        /// <summary>Frontier factor 0 (deep interior) .. 1 (borders another civilization).</summary>
        public const string Frontier = "adv.def.front";
        /// <summary>Imminence 0..1: how soon the danger arrives (army close, siege, fresh battle).</summary>
        public const string Imminence = "adv.def.imm";

        // ------------------------------------------------------------------ civilization
        /// <summary>Civilization memory: tick when the civ last lost a settlement to conquest (civ-wide shock).</summary>
        public const string CivShockTick = "adv.def.shockTick";

        /// <summary>Settlement key written by the military module: siege damage 0..0.9 (read only).</summary>
        public const string ExternalSiegeDamage = "mil.siegeDamage";

        // ------------------------------------------------------------------ tuning
        /// <summary>Hostile armies farther than this (tiles) are ignored.</summary>
        public const float ArmyRadius = 30f;
        /// <summary>Hostile settlements farther than this (tiles) are ignored.</summary>
        public const float SettlementReach = 64f;
        /// <summary>Hostile settlements closer than this count with full weight.</summary>
        public const float SettlementNear = 10f;
        /// <summary>Fraction of a neighbour's total military power we expect it to commit against one settlement.</summary>
        public const float ProjectionFraction = 0.4f;
        /// <summary>How long a battle is remembered (ticks, ~1.5 years).</summary>
        public const float BattleMemoryTicks = 144f;
        /// <summary>How long the loss of a settlement shocks the whole civilization (ticks, 1 year).</summary>
        public const float ShockMemoryTicks = 96f;
        /// <summary>Alert multiplier per 12 ticks once the danger is gone.</summary>
        public const float AlertDecayPer12 = 0.8f;
    }
}
