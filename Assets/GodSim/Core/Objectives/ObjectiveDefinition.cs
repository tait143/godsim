namespace GodSim.Core.Objectives
{
    /// <summary>What the god receives when an objective is fulfilled. All values are clamped when applied.</summary>
    public struct ObjectiveReward
    {
        /// <summary>Mana granted (clamped to MaxMana).</summary>
        public float Mana;
        /// <summary>Permanent increase of God.MaxMana.</summary>
        public float MaxManaBonus;
        /// <summary>Change to God.Alignment (+ benevolent, - wrathful), clamped to -1..1.</summary>
        public float AlignmentNudge;

        public ObjectiveReward(float mana, float maxManaBonus, float alignmentNudge)
        {
            Mana = mana;
            MaxManaBonus = maxManaBonus;
            AlignmentNudge = alignmentNudge;
        }

        public override string ToString()
        {
            string s = "+" + Mana.ToString("0") + " mana";
            if (MaxManaBonus != 0f) s += ", +" + MaxManaBonus.ToString("0") + " max mana";
            if (AlignmentNudge != 0f) s += ", " + (AlignmentNudge > 0f ? "+" : "") + AlignmentNudge.ToString("0.00") + " alignment";
            return s;
        }
    }

    /// <summary>
    /// Static data for one divine objective (quest, milestone or achievement). Registered in
    /// <see cref="ObjectiveCatalog"/>; treat instances as read-only at runtime. Progress lives in
    /// WorldState.Memory under "obj.&lt;Key&gt;.progress" / "obj.&lt;Key&gt;.done".
    /// </summary>
    public sealed class ObjectiveDefinition
    {
        private readonly string _progressKey;
        private readonly string _doneKey;
        private readonly string _baseKey;
        private readonly string _armedKey;

        /// <param name="key">Stable unique key: lowercase letters, digits and underscores (stored in saves).</param>
        public ObjectiveDefinition(string key)
        {
            Key = key ?? "";
            _progressKey = ObjectiveKeys.Progress(Key);
            _doneKey = ObjectiveKeys.Done(Key);
            _baseKey = ObjectiveKeys.Base(Key);
            _armedKey = ObjectiveKeys.Armed(Key);
        }

        /// <summary>Stable unique key (e.g. "bronze_shepherd").</summary>
        public string Key { get; }
        public string Title = "";
        public string Description = "";
        public ObjectiveCategory Category;
        public ObjectiveKind Kind;
        /// <summary>Numeric goal; meaning depends on <see cref="Kind"/>.</summary>
        public float Target = 1f;
        /// <summary>Only entities of this race count (settlement/civ/building scans; ignored by pure event counters).</summary>
        public RaceId? RaceFilter;
        /// <summary>For age kinds: the age to reach. Otherwise: only civs (or their settlements) at this age or later count.</summary>
        public AgeId? AgeFilter;
        /// <summary>Power counted by <see cref="ObjectiveKind.PowerCast"/>.</summary>
        public GodPowerType Power;
        /// <summary>Building counted by <see cref="ObjectiveKind.BuildingCompleted"/>.</summary>
        public BuildingType Building;
        /// <summary>Tier used by <see cref="ObjectiveKind.SettlementsAtTier"/>.</summary>
        public SettlementTier Tier;
        /// <summary>Earliest year the objective may complete (used by <see cref="ObjectiveKind.RacesAlive"/>; 0 = any).</summary>
        public int MinYear;
        public ObjectiveReward Reward;
        /// <summary>Can be fulfilled again (counter kinds: every Target more; state kinds: after the condition lapses).</summary>
        public bool Repeatable;
        /// <summary>Not shown in the HUD until fulfilled (a secret achievement).</summary>
        public bool Hidden;
        /// <summary>Chronicle importance of the fulfilment entry (2 notable .. 3 legendary).</summary>
        public int Importance = 2;

        /// <summary>Memory key of the 0..1 progress value.</summary>
        public string ProgressKey => _progressKey;
        /// <summary>Memory key of the completion count (1 for one-shot objectives).</summary>
        public string DoneKey => _doneKey;
        /// <summary>Memory key of the counter baseline used by repeatable counter objectives.</summary>
        public string BaseKey => _baseKey;
        /// <summary>Memory key that disarms repeatable state objectives until their condition lapses.</summary>
        public string ArmedKey => _armedKey;

        public override string ToString() => Key + " (" + Title + ")";
    }
}
