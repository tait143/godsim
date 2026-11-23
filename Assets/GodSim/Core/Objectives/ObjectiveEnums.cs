namespace GodSim.Core.Objectives
{
    /// <summary>Thematic grouping of divine objectives (HUD tabs).</summary>
    public enum ObjectiveCategory : byte
    {
        /// <summary>Early, tutorial-like goals that teach the god's tools.</summary>
        Guidance = 0,
        /// <summary>Growth of settlements, population and knowledge.</summary>
        Prosperity = 1,
        /// <summary>Benevolence, devotion and peace.</summary>
        Faith = 2,
        /// <summary>Smiting, war and catastrophe.</summary>
        Wrath = 3,
        /// <summary>Wonders raised by mortals.</summary>
        Wonder = 4,
        /// <summary>Long-term milestones and the endgame.</summary>
        Legacy = 5
    }

    /// <summary>
    /// The condition an objective measures. Each kind documents how <see cref="ObjectiveDefinition.Target"/> and the
    /// optional fields are interpreted. Counter kinds are tallied from events and persisted in WorldState.Memory;
    /// state kinds are re-measured on every periodic check.
    /// </summary>
    public enum ObjectiveKind : byte
    {
        /// <summary>State: any settlement (race/age filtered) reaches tier Target ((int)SettlementTier). Sticky when unfiltered.</summary>
        SettlementTier = 0,
        /// <summary>State: at least Target living settlements are at tier <see cref="ObjectiveDefinition.Tier"/> or higher.</summary>
        SettlementsAtTier = 1,
        /// <summary>State: a single settlement has Target residents.</summary>
        SettlementPopulation = 2,
        /// <summary>State: at least Target living settlements exist.</summary>
        SettlementCount = 3,
        /// <summary>State: the world (sum of settlement residents) holds Target people.</summary>
        WorldPopulation = 4,
        /// <summary>State: Target living civs have reached <see cref="ObjectiveDefinition.AgeFilter"/> (required). Sticky when unfiltered and Target is 1.</summary>
        CivilizationAge = 5,
        /// <summary>State: Target living civs spawned by the god (Memory "obj.spawnedByGod") have reached AgeFilter (required).</summary>
        SpawnedCivilizationAge = 6,
        /// <summary>State: at least Target civilizations are alive.</summary>
        CivilizationsAlive = 7,
        /// <summary>State: Target distinct races inhabit living settlements once the year is at least <see cref="ObjectiveDefinition.MinYear"/>.</summary>
        RacesAlive = 8,
        /// <summary>State: a single civilization knows Target technologies.</summary>
        TechsKnown = 9,
        /// <summary>State: a single civilization's Devotion is at least Target (0..1).</summary>
        CivilizationDevotion = 10,
        /// <summary>Counter + state: Target buildings of type <see cref="ObjectiveDefinition.Building"/> completed.</summary>
        BuildingCompleted = 11,
        /// <summary>Counter + state: Target wonders completed anywhere.</summary>
        WondersCompleted = 12,
        /// <summary>Counter: Target buildings of any type completed.</summary>
        BuildingsCompleted = 13,
        /// <summary>Counter: the god successfully cast <see cref="ObjectiveDefinition.Power"/> Target times.</summary>
        PowerCast = 14,
        /// <summary>Counter: the god successfully cast any power Target times.</summary>
        AnyPowerCast = 15,
        /// <summary>State: God.Alignment is at least Target (positive).</summary>
        AlignmentAtLeast = 16,
        /// <summary>State: God.Alignment is at most Target (negative).</summary>
        AlignmentAtMost = 17,
        /// <summary>State: God.Mana is at least Target.</summary>
        ManaReached = 18,
        /// <summary>Counter: Target battles fought.</summary>
        BattlesFought = 19,
        /// <summary>Counter: Target wars declared.</summary>
        WarsDeclared = 20,
        /// <summary>State: Target years without any active war while at least two civilizations live.</summary>
        YearsOfPeace = 21,
        /// <summary>Counter: Target civilizations collapsed.</summary>
        CivilizationsCollapsed = 22,
        /// <summary>Counter: Target settlements captured.</summary>
        SettlementsCaptured = 23,
        /// <summary>Counter: Target disasters (natural or divine) witnessed.</summary>
        DisastersWitnessed = 24,
        /// <summary>State: the world year is at least Target.</summary>
        YearReached = 25,
        /// <summary>Counter: Target age advancements across all civilizations.</summary>
        AgeAdvancements = 26,
        /// <summary>Counter: Target settlements rebuilt (a new settlement within 10 tiles, within 50 years) after the god's Meteor destroyed one.</summary>
        Phoenix = 27
    }
}
