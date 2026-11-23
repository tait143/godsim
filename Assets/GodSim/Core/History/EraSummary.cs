namespace GodSim.Core.History
{
    /// <summary>One narrated era produced by <see cref="ChronicleNarrator"/> (a civ's age, or a world century).</summary>
    public sealed class EraSummary
    {
        /// <summary>Civilization this era belongs to, or -1 for a world century.</summary>
        public int CivId = -1;
        /// <summary>The civ's age during the era (century summaries: the highest age seen in the events).</summary>
        public AgeId Age;
        /// <summary>Century index (0 = years 0-99) for world summaries; -1 for civ eras.</summary>
        public int Century = -1;
        public long StartTick;
        public long EndTick;
        /// <summary>Short heading, e.g. "The Age of Bronze" or "Years 100-199".</summary>
        public string Title = "";
        /// <summary>Full sentence(s) including the title, bounded by <see cref="ChronicleNarrator.MaxSummaryLength"/>.</summary>
        public string Text = "";

        public override string ToString() => Text;
    }
}
