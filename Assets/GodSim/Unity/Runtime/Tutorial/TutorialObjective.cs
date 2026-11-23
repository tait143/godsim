using GodSim.Core;

namespace GodSim.Unity
{
    /// <summary>What the player has to do to complete a tutorial step.</summary>
    public enum TutorialObjectiveKind : byte
    {
        /// <summary>Nothing to do: read the card and press Next.</summary>
        None = 0,
        /// <summary>Pan, rotate or zoom the camera.</summary>
        CameraMoved = 1,
        /// <summary>Select any settlement.</summary>
        SettlementSelected = 2,
        /// <summary>Successfully cast a god power (an exact type, or any power of a category).</summary>
        PowerCast = 3,
        /// <summary>Pause/unpause or change the simulation speed.</summary>
        SpeedChanged = 4,
        /// <summary>Let a number of in-game days pass while the step is active.</summary>
        WaitDays = 5,
        /// <summary>Open the help panel.</summary>
        OpenHelp = 6
    }

    /// <summary>
    /// Immutable description of a tutorial step objective. Build one with the static factories
    /// (<see cref="None"/>, <see cref="CastPower"/>, <see cref="WaitDays"/>...).
    /// </summary>
    public readonly struct TutorialObjective
    {
        /// <summary>What has to happen.</summary>
        public readonly TutorialObjectiveKind Kind;
        /// <summary>For <see cref="TutorialObjectiveKind.PowerCast"/>: true = any power of <see cref="Category"/> counts.</summary>
        public readonly bool MatchCategory;
        /// <summary>For <see cref="TutorialObjectiveKind.PowerCast"/> with <see cref="MatchCategory"/> false: the exact power.</summary>
        public readonly GodPowerType Power;
        /// <summary>For <see cref="TutorialObjectiveKind.PowerCast"/> with <see cref="MatchCategory"/> true: the category.</summary>
        public readonly GodPowerCategory Category;
        /// <summary>For <see cref="TutorialObjectiveKind.WaitDays"/>: how many in-game days (ticks) must pass (>= 1).</summary>
        public readonly int Days;
        /// <summary>Short imperative text shown on the card ("Select a settlement"). Never null.</summary>
        public readonly string Label;

        private TutorialObjective(TutorialObjectiveKind kind, bool matchCategory, GodPowerType power,
            GodPowerCategory category, int days, string label)
        {
            Kind = kind;
            MatchCategory = matchCategory;
            Power = power;
            Category = category;
            Days = days < 1 ? 1 : days;
            Label = label ?? "";
        }

        /// <summary>No objective: the step is read and acknowledged with Next.</summary>
        public static TutorialObjective None(string label = "") =>
            new TutorialObjective(TutorialObjectiveKind.None, false, default, default, 1, label);

        /// <summary>The camera must move.</summary>
        public static TutorialObjective CameraMoved(string label) =>
            new TutorialObjective(TutorialObjectiveKind.CameraMoved, false, default, default, 1, label);

        /// <summary>A settlement must be selected.</summary>
        public static TutorialObjective SettlementSelected(string label) =>
            new TutorialObjective(TutorialObjectiveKind.SettlementSelected, false, default, default, 1, label);

        /// <summary>A specific power must be cast successfully.</summary>
        public static TutorialObjective CastPower(GodPowerType power, string label) =>
            new TutorialObjective(TutorialObjectiveKind.PowerCast, false, power, default, 1, label);

        /// <summary>Any power of the given category must be cast successfully.</summary>
        public static TutorialObjective CastCategory(GodPowerCategory category, string label) =>
            new TutorialObjective(TutorialObjectiveKind.PowerCast, true, default, category, 1, label);

        /// <summary>The player must pause/unpause or change the speed.</summary>
        public static TutorialObjective SpeedChanged(string label) =>
            new TutorialObjective(TutorialObjectiveKind.SpeedChanged, false, default, default, 1, label);

        /// <summary><paramref name="days"/> in-game days (clamped to >= 1) must pass while the step is active.</summary>
        public static TutorialObjective WaitDays(int days, string label) =>
            new TutorialObjective(TutorialObjectiveKind.WaitDays, false, default, default, days, label);

        /// <summary>The help panel must be opened.</summary>
        public static TutorialObjective OpenHelp(string label) =>
            new TutorialObjective(TutorialObjectiveKind.OpenHelp, false, default, default, 1, label);

        /// <summary>True when a successful cast of <paramref name="power"/> (of <paramref name="category"/>) satisfies this objective.</summary>
        public bool MatchesCast(GodPowerType power, GodPowerCategory category)
        {
            if (Kind != TutorialObjectiveKind.PowerCast) return false;
            return MatchCategory ? category == Category : power == Power;
        }
    }
}
