namespace GodSim.Core.AI
{
    /// <summary>
    /// Read-only views of how a civilization perceives the player-god (UI, other AI modules, tests).
    /// All methods are null-safe and never return NaN.
    /// </summary>
    public static class GodPerceptionQueries
    {
        private static readonly BuildingType[] s_faithLadder = { BuildingType.Cathedral, BuildingType.Temple, BuildingType.Shrine, BuildingType.SpiritTotem };

        public static float Gratitude(Civilization civ) => Read(civ, GodPerceptionKeys.Gratitude);
        public static float Fear(Civilization civ) => Read(civ, GodPerceptionKeys.Fear);
        public static float Resentment(Civilization civ) => Read(civ, GodPerceptionKeys.Resentment);
        public static float Uncertainty(Civilization civ) => Read(civ, GodPerceptionKeys.Uncertainty);

        /// <summary>True when the civ has turned its back on the heavens (high resentment; it neglects faith).</summary>
        public static bool HasTurnedAway(Civilization civ) => civ != null && civ.Mem(GodPerceptionKeys.Turned) > 0.5f;

        /// <summary>The last divine act this civ remembers.</summary>
        public static DivineActKind LastAct(Civilization civ)
        {
            if (civ == null) return DivineActKind.None;
            int k = (int)MathUtil.Sanitize(civ.Mem(GodPerceptionKeys.LastKind));
            return k >= 0 && k <= (int)DivineActKind.BlamedDisaster ? (DivineActKind)k : DivineActKind.None;
        }

        /// <summary>Summarizes gratitude, fear, resentment, uncertainty and devotion into one mood.</summary>
        public static GodMood Mood(Civilization civ)
        {
            if (civ == null) return GodMood.Indifferent;
            float g = Gratitude(civ), f = Fear(civ), r = Resentment(civ), u = Uncertainty(civ);
            float d = MathUtil.Clamp01(MathUtil.Sanitize(civ.Devotion, 0.5f));
            if (g < 0.12f && f < 0.12f && r < 0.12f && u < 0.2f) return GodMood.Indifferent;
            if (r >= 0.4f && r >= g) return GodMood.Resentful;
            if (g >= 0.7f && f < 0.35f && u < 0.35f && d >= 0.45f) return GodMood.Adoring;
            if (f >= 0.5f && f > g) return GodMood.Fearful;
            if (g >= 0.35f && f >= 0.3f) return GodMood.Reverent;
            if (u >= 0.35f) return GodMood.Uneasy;
            if (g >= 0.15f && g >= f) return GodMood.Grateful;
            if (f >= 0.15f) return GodMood.Uneasy;
            return GodMood.Indifferent;
        }

        /// <summary>The epithet this civ uses for the god right now ("the Storm-Father", "the Sun-Serpent").</summary>
        public static string Epithet(Civilization civ) => GodPerceptionLore.Epithet(EpithetIndex(civ));

        /// <summary>Index into <see cref="GodPerceptionLore"/> of the civ's current epithet.</summary>
        public static int EpithetIndex(Civilization civ)
        {
            if (civ == null) return GodPerceptionLore.EpithetNameless;
            float g = Gratitude(civ), f = Fear(civ), r = Resentment(civ);
            if ((f > g || r > g) && civ.Memory.TryGetValue(GodPerceptionKeys.WrathEpithet, out float w)) return (int)MathUtil.Sanitize(w);
            // A grateful people keeps its loving name even after a minor upheaval.
            if (g > 0.05f) return GodPerceptionLore.RaceEpithet(civ.Race);
            if (civ.Memory.TryGetValue(GodPerceptionKeys.LastEpithet, out float e)) return (int)MathUtil.Sanitize(e);
            return GodPerceptionLore.EpithetNameless;
        }

        /// <summary>Static, allocation-free phrase for a mood (suitable for tooltips).</summary>
        public static string MoodText(GodMood mood)
        {
            switch (mood)
            {
                case GodMood.Adoring: return "sing hymns to their god and would follow it anywhere";
                case GodMood.Grateful: return "give thanks for divine gifts remembered";
                case GodMood.Reverent: return "both love and fear their god; offerings never cease";
                case GodMood.Uneasy: return "cannot read the will of the heavens and pray nervously";
                case GodMood.Fearful: return "cower beneath the heavens, appeasing before the next blow";
                case GodMood.Resentful: return "curse their god as a tyrant and neglect the shrines";
                default: return "barely notice the heavens";
            }
        }

        /// <summary>One-line UI description, e.g. "Fearful: they cower before the Storm-Father ...". Allocates (UI use only).</summary>
        public static string Describe(Civilization civ)
        {
            if (civ == null) return "Indifferent: no one remembers the gods.";
            var mood = Mood(civ);
            string ep = Epithet(civ);
            string name = string.IsNullOrEmpty(civ.Name) ? "These people" : "The " + civ.Name;
            switch (mood)
            {
                case GodMood.Adoring: return "Adoring: " + name + " sing hymns to " + ep + " and would follow it anywhere.";
                case GodMood.Grateful: return "Grateful: " + name + " give thanks to " + ep + " for gifts remembered.";
                case GodMood.Reverent: return "Reverent: " + name + " both love and fear " + ep + "; the offerings never cease.";
                case GodMood.Uneasy: return "Uneasy: " + name + " cannot read the will of " + ep + " and pray nervously.";
                case GodMood.Fearful: return "Fearful: " + name + " cower before " + ep + ", appeasing it before it strikes again.";
                case GodMood.Resentful: return "Resentful: " + name + " curse " + ep + " as a tyrant and let the shrines crumble.";
                default: return "Indifferent: " + name + " barely notice the heavens.";
            }
        }

        /// <summary>The best faith building the civ can currently build (Cathedral &gt; Temple &gt; Shrine &gt; Spirit Totem), or SpiritTotem.</summary>
        public static BuildingType PreferredFaithBuilding(GameDatabase db, Civilization civ)
        {
            if (db == null || civ == null) return BuildingType.SpiritTotem;
            for (int i = 0; i < s_faithLadder.Length; i++)
                if (db.IsBuildingAvailable(civ, s_faithLadder[i])) return s_faithLadder[i];
            return BuildingType.SpiritTotem;
        }

        private static float Read(Civilization civ, string key) => civ == null ? 0f : MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(key)));
    }
}
