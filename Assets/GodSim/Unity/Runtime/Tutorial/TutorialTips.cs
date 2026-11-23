namespace GodSim.Unity
{
    /// <summary>One-time contextual hints shown as toasts when something happens for the first time.</summary>
    public enum TutorialTipKind : byte
    {
        /// <summary>The first famine anywhere.</summary>
        FirstFamine = 0,
        /// <summary>The first war declaration.</summary>
        FirstWar = 1,
        /// <summary>The first civilization advancing to a new age.</summary>
        FirstAgeAdvance = 2,
        /// <summary>The first time a power cannot be afforded.</summary>
        LowMana = 3
    }

    /// <summary>
    /// Unity-free bookkeeping of which one-time tips were already shown (a bitmask the controller
    /// persists as an int).
    /// </summary>
    public sealed class TutorialTipTracker
    {
        /// <summary>Number of tip kinds.</summary>
        public const int Count = 4;
        private const int AllBits = (1 << Count) - 1;

        private int _shown;

        /// <summary>Bitmask of shown tips (bit = (int)kind). Setting it masks out unknown bits.</summary>
        public int Mask
        {
            get => _shown;
            set => _shown = value & AllBits;
        }

        /// <summary>True when the tip was already shown.</summary>
        public bool WasShown(TutorialTipKind kind)
        {
            int bit = (int)kind;
            return bit >= 0 && bit < Count && (_shown & (1 << bit)) != 0;
        }

        /// <summary>Marks the tip shown. Returns true only the first time (i.e. when it should be displayed).</summary>
        public bool TryConsume(TutorialTipKind kind)
        {
            int bit = (int)kind;
            if (bit < 0 || bit >= Count) return false;
            int flag = 1 << bit;
            if ((_shown & flag) != 0) return false;
            _shown |= flag;
            return true;
        }

        /// <summary>Forgets every shown tip.</summary>
        public void Reset() => _shown = 0;
    }
}
