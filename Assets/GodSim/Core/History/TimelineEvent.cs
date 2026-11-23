namespace GodSim.Core.History
{
    /// <summary>
    /// One notable moment in the <see cref="HistoryRecorder.Eras"/> timeline.
    /// Field meaning by <see cref="Kind"/>:
    /// <list type="bullet">
    /// <item>CivilizationFounded: CivId; Value = (int)RaceId.</item>
    /// <item>AgeAdvanced: CivId; SubjectId = (int)old age; Value = (int)new age.</item>
    /// <item>WarDeclared: CivId = attacker; OtherCivId = defender; SubjectId = war id.</item>
    /// <item>PeaceSigned: CivId = attacker; OtherCivId = defender; SubjectId = war id; Value = rounded war score.</item>
    /// <item>SettlementFounded: CivId; SubjectId = settlement id; Value = parent settlement id.</item>
    /// <item>SettlementCaptured: CivId = new owner; OtherCivId = old owner; SubjectId = settlement id.</item>
    /// <item>CivilizationCollapsed: CivId; OtherCivId = conqueror (-1 if none).</item>
    /// <item>TechResearched: CivId; SubjectId = tech id.</item>
    /// </list>
    /// </summary>
    public struct TimelineEvent
    {
        public long Tick;
        public TimelineEventKind Kind;
        public int CivId;
        public int OtherCivId;
        public int SubjectId;
        public int Value;
        /// <summary>Short human-readable label built when the event happened (names as they were then).</summary>
        public string Label;

        public int Year => SimTime.Year(Tick);

        public override string ToString() => "[" + SimTime.Format(Tick) + "] " + Kind + ": " + (Label ?? "");
    }
}
