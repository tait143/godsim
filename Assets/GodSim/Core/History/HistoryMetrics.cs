namespace GodSim.Core.History
{
    /// <summary>Per-civilization series recorded by <see cref="HistoryRecorder"/> (chart selectors).</summary>
    public enum CivMetric : byte
    {
        Population = 0,
        /// <summary>Current age as a number (0 = Dawn .. 9 = Ascension).</summary>
        Age = 1,
        Settlements = 2,
        /// <summary>Completed, standing buildings.</summary>
        Buildings = 3,
        MilitaryPower = 4,
        /// <summary>Lifetime research (Civilization.TotalResearch).</summary>
        TotalResearch = 5,
        Devotion = 6,
        GodFavor = 7,
        /// <summary>Population-weighted average settlement happiness (0..1).</summary>
        Happiness = 8
    }

    /// <summary>World-level series recorded by <see cref="HistoryRecorder"/> (chart selectors).</summary>
    public enum WorldMetric : byte
    {
        Population = 0,
        Settlements = 1,
        /// <summary>Completed, standing buildings.</summary>
        Buildings = 2,
        Armies = 3,
        ActiveWars = 4,
        Mana = 5,
        Alignment = 6,
        /// <summary>Births since the previous stored sample.</summary>
        Births = 7,
        /// <summary>Deaths since the previous stored sample.</summary>
        Deaths = 8,
        LivingCivilizations = 9,
        /// <summary>Population-weighted average settlement happiness (0..1).</summary>
        Happiness = 10,
        /// <summary>Highest age reached by a living civilization (0..9).</summary>
        WorldAge = 11,
        BuildingsUnderConstruction = 12
    }

    /// <summary>Kinds of entries in the <see cref="HistoryRecorder.Eras"/> timeline.</summary>
    public enum TimelineEventKind : byte
    {
        CivilizationFounded = 0,
        AgeAdvanced = 1,
        WarDeclared = 2,
        PeaceSigned = 3,
        SettlementFounded = 4,
        SettlementCaptured = 5,
        CivilizationCollapsed = 6,
        TechResearched = 7
    }
}
