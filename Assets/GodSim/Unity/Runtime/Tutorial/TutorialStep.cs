using System;

namespace GodSim.Unity
{
    /// <summary>Optional "Show me" helper offered by a step's card.</summary>
    public enum TutorialFocus : byte
    {
        /// <summary>No helper button.</summary>
        None = 0,
        /// <summary>"Show me" flies the camera through the living settlements (capitals first).</summary>
        Settlements = 1
    }

    /// <summary>Well-known highlight anchor names (screen regions the card points at).</summary>
    public static class TutorialAnchors
    {
        /// <summary>No anchor: the card is centered and no arrow is drawn.</summary>
        public const string Center = "Center";
        /// <summary>Top resource / time bar.</summary>
        public const string TopBar = "TopBar";
        /// <summary>The bar of god powers (bottom).</summary>
        public const string PowersBar = "PowersBar";
        /// <summary>The selection inspector panel (right).</summary>
        public const string Inspector = "Inspector";
        /// <summary>The chronicle / event log (left).</summary>
        public const string Chronicle = "Chronicle";
        /// <summary>The minimap (bottom right).</summary>
        public const string Minimap = "Minimap";
        /// <summary>The help button (top right).</summary>
        public const string Help = "Help";
    }

    /// <summary>One immutable tutorial step: copy, objective, highlight hint.</summary>
    public sealed class TutorialStep
    {
        /// <summary>Stable id used by progress serialization (lowercase letters, digits, '_' or '-').</summary>
        public string Id { get; }
        /// <summary>Card title (IMGUI rich text allowed).</summary>
        public string Title { get; }
        /// <summary>Card body: friendly second-person copy (IMGUI rich text allowed).</summary>
        public string Body { get; }
        /// <summary>What the player must do to complete the step.</summary>
        public TutorialObjective Objective { get; }
        /// <summary>Screen anchor name the card points at (see <see cref="TutorialAnchors"/>).</summary>
        public string Highlight { get; }
        /// <summary>Whether the player may skip the step without completing its objective.</summary>
        public bool Skippable { get; }
        /// <summary>Optional "Show me" helper.</summary>
        public TutorialFocus Focus { get; }

        /// <summary>Creates a step. Throws <see cref="ArgumentException"/> on an invalid id (content bug).</summary>
        public TutorialStep(string id, string title, string body, TutorialObjective objective,
            string highlight = TutorialAnchors.Center, bool skippable = true, TutorialFocus focus = TutorialFocus.None)
        {
            if (!IsValidId(id)) throw new ArgumentException("Invalid tutorial step id: '" + id + "'", nameof(id));
            Id = id;
            Title = title ?? "";
            Body = body ?? "";
            Objective = objective;
            Highlight = string.IsNullOrEmpty(highlight) ? TutorialAnchors.Center : highlight;
            Skippable = skippable;
            Focus = focus;
        }

        /// <summary>True when the step simply needs Next (no gameplay objective).</summary>
        public bool IsInformational => Objective.Kind == TutorialObjectiveKind.None;

        /// <summary>Ids are 1..32 chars of [a-z0-9_-] so they never collide with the progress string separators.</summary>
        public static bool IsValidId(string id)
        {
            if (string.IsNullOrEmpty(id) || id.Length > 32) return false;
            for (int i = 0; i < id.Length; i++)
            {
                char c = id[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        /// <inheritdoc/>
        public override string ToString() => Id;
    }
}
