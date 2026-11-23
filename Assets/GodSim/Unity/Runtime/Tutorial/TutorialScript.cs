using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using GodSim.Core;

namespace GodSim.Unity
{
    /// <summary>Lifecycle of a <see cref="TutorialScript"/>.</summary>
    public enum TutorialState : byte
    {
        /// <summary>Not started (or reset).</summary>
        NotStarted = 0,
        /// <summary>A step is current.</summary>
        Active = 1,
        /// <summary>Every step was completed or skipped.</summary>
        Finished = 2
    }

    /// <summary>
    /// Unity-free tutorial state machine. Holds an ordered list of <see cref="TutorialStep"/>s and is
    /// fed abstract gameplay signals (<see cref="OnCameraMoved"/>, <see cref="OnPowerCast(GodPowerType)"/>...).
    /// A signal only counts for the step that is current when it arrives. When the current objective
    /// is met the step either waits for <see cref="Advance"/> or auto-advances after
    /// <see cref="AutoAdvanceDelay"/> seconds of <see cref="Update"/>. Progress round-trips through a
    /// compact string (<see cref="Serialize"/> / <see cref="TryDeserialize"/>). Never throws on bad input.
    /// </summary>
    public sealed class TutorialScript
    {
        /// <summary>Maximum number of steps (progress is stored in 64-bit masks).</summary>
        public const int MaxSteps = 64;
        /// <summary>Prefix/version tag of the serialized progress string.</summary>
        public const string FormatTag = "t1";
        /// <summary>Upper bound on the accepted serialized length (defends against garbage in PlayerPrefs).</summary>
        public const int MaxSerializedLength = 160;

        private readonly TutorialStep[] _steps;
        private TutorialState _state;
        private int _index = -1;
        private ulong _completed;
        private ulong _skipped;
        private bool _met;
        private int _days;
        private float _metSeconds;

        /// <summary>Raised after the current step (or state) changes: start, advance, skip, reset, load.</summary>
        public event Action<TutorialScript> StepChanged;
        /// <summary>Raised once when the current step's objective becomes satisfied.</summary>
        public event Action<TutorialScript> ObjectiveCompleted;
        /// <summary>Raised when the last step is completed or skipped.</summary>
        public event Action<TutorialScript> Finished;

        /// <summary>
        /// Maps a power to its category for category objectives. Defaults to <see cref="DefaultCategoryOf"/>;
        /// the Unity controller points it at <c>GameDatabase</c> definitions.
        /// </summary>
        public Func<GodPowerType, GodPowerCategory> CategoryResolver { get; set; }

        /// <summary>
        /// Seconds (fed through <see cref="Update"/>) between an objective being met and the automatic
        /// advance. Negative disables auto-advance. Informational steps never auto-advance.
        /// </summary>
        public float AutoAdvanceDelay { get; set; } = 1.5f;

        /// <summary>Creates a script over a copy of <paramref name="steps"/> (1..64 non-null steps with unique ids).</summary>
        public TutorialScript(IList<TutorialStep> steps)
        {
            if (steps == null) throw new ArgumentNullException(nameof(steps));
            if (steps.Count < 1 || steps.Count > MaxSteps) throw new ArgumentException("A tutorial needs 1.." + MaxSteps + " steps.", nameof(steps));
            _steps = new TutorialStep[steps.Count];
            for (int i = 0; i < steps.Count; i++)
            {
                var s = steps[i] ?? throw new ArgumentException("Null tutorial step at " + i, nameof(steps));
                for (int j = 0; j < i; j++)
                    if (string.Equals(_steps[j].Id, s.Id, StringComparison.Ordinal))
                        throw new ArgumentException("Duplicate tutorial step id: " + s.Id, nameof(steps));
                _steps[i] = s;
            }
        }

        // ------------------------------------------------------------------ queries

        /// <summary>Number of steps.</summary>
        public int StepCount => _steps.Length;
        /// <summary>Lifecycle state.</summary>
        public TutorialState State => _state;
        /// <summary>True while a step is current.</summary>
        public bool IsActive => _state == TutorialState.Active;
        /// <summary>True once every step was completed or skipped.</summary>
        public bool IsFinished => _state == TutorialState.Finished;
        /// <summary>Current step index: -1 before start, <see cref="StepCount"/> when finished.</summary>
        public int CurrentIndex => _index;
        /// <summary>The current step, or null when not active.</summary>
        public TutorialStep CurrentStep => IsActive ? _steps[_index] : null;
        /// <summary>True when the current step's objective has been satisfied (always true for informational steps).</summary>
        public bool ObjectiveMet => IsActive && (_met || _steps[_index].IsInformational);
        /// <summary>True when <see cref="Advance"/> would succeed.</summary>
        public bool CanAdvance => ObjectiveMet;
        /// <summary>True when <see cref="Skip"/> would succeed.</summary>
        public bool CanSkip => IsActive && _steps[_index].Skippable;
        /// <summary>True when the current step is the last one.</summary>
        public bool IsLastStep => IsActive && _index == _steps.Length - 1;
        /// <summary>Days accumulated toward the current <see cref="TutorialObjectiveKind.WaitDays"/> objective.</summary>
        public int DaysAccumulated => _days;

        /// <summary>Objective completion 0..1 (fractional for waiting steps).</summary>
        public float ObjectiveProgress
        {
            get
            {
                if (!IsActive) return IsFinished ? 1f : 0f;
                if (ObjectiveMet) return 1f;
                var o = _steps[_index].Objective;
                if (o.Kind == TutorialObjectiveKind.WaitDays) return Math.Min(1f, _days / (float)o.Days);
                return 0f;
            }
        }

        /// <summary>The step at <paramref name="index"/>, or null when out of range.</summary>
        public TutorialStep GetStep(int index) => index >= 0 && index < _steps.Length ? _steps[index] : null;

        /// <summary>Index of the step with <paramref name="id"/>, or -1.</summary>
        public int IndexOf(string id)
        {
            if (id == null) return -1;
            for (int i = 0; i < _steps.Length; i++) if (string.Equals(_steps[i].Id, id, StringComparison.Ordinal)) return i;
            return -1;
        }

        /// <summary>True when the step was completed (objective met + advanced).</summary>
        public bool IsStepCompleted(int index) => index >= 0 && index < _steps.Length && (_completed & (1UL << index)) != 0;
        /// <summary>True when the step was skipped.</summary>
        public bool IsStepSkipped(int index) => index >= 0 && index < _steps.Length && (_skipped & (1UL << index)) != 0;

        /// <summary>Number of completed steps.</summary>
        public int CompletedCount => PopCount(_completed);
        /// <summary>Number of skipped steps.</summary>
        public int SkippedCount => PopCount(_skipped);

        // ------------------------------------------------------------------ transitions

        /// <summary>Starts at the first step. No-op (returns false) when already active or finished.</summary>
        public bool Start()
        {
            if (_state != TutorialState.NotStarted) return false;
            _state = TutorialState.Active;
            EnterStep(0);
            return true;
        }

        /// <summary>Completes the current step and moves on. Fails when the objective is not met.</summary>
        public bool Advance()
        {
            if (!CanAdvance) return false;
            _completed |= 1UL << _index;
            _skipped &= ~(1UL << _index);
            MoveNext();
            return true;
        }

        /// <summary>Skips the current step (only when <see cref="TutorialStep.Skippable"/>).</summary>
        public bool Skip()
        {
            if (!CanSkip) return false;
            if (ObjectiveMet) _completed |= 1UL << _index; // already done: count it as completed
            else _skipped |= 1UL << _index;
            MoveNext();
            return true;
        }

        /// <summary>Marks every remaining step skipped and finishes (the "skip tutorial" action).</summary>
        public void SkipAll()
        {
            if (_state == TutorialState.Finished) return;
            int from = _state == TutorialState.Active ? _index : 0;
            for (int i = from; i < _steps.Length; i++)
                if ((_completed & (1UL << i)) == 0) _skipped |= 1UL << i;
            Finish();
        }

        /// <summary>Back to <see cref="TutorialState.NotStarted"/> with no progress.</summary>
        public void Reset()
        {
            _state = TutorialState.NotStarted;
            _index = -1;
            _completed = 0;
            _skipped = 0;
            ClearStepProgress();
            StepChanged?.Invoke(this);
        }

        /// <summary>
        /// Advances time for the auto-advance timer. <paramref name="realSeconds"/> is unscaled real
        /// time; NaN/negative values are ignored and huge values clamped.
        /// </summary>
        public void Update(float realSeconds)
        {
            if (!IsActive || !_met || AutoAdvanceDelay < 0f) return;
            if (_steps[_index].IsInformational) return;
            if (!(realSeconds > 0f)) return; // also rejects NaN
            if (realSeconds > 1f) realSeconds = 1f;
            _metSeconds += realSeconds;
            if (_metSeconds >= AutoAdvanceDelay) Advance();
        }

        // ------------------------------------------------------------------ signals

        /// <summary>The camera was moved by the player.</summary>
        public bool OnCameraMoved() => Satisfy(TutorialObjectiveKind.CameraMoved);
        /// <summary>A settlement was selected.</summary>
        public bool OnSettlementSelected() => Satisfy(TutorialObjectiveKind.SettlementSelected);
        /// <summary>The simulation speed or pause state changed.</summary>
        public bool OnSpeedChanged() => Satisfy(TutorialObjectiveKind.SpeedChanged);
        /// <summary>The help panel was opened.</summary>
        public bool OnHelpOpened() => Satisfy(TutorialObjectiveKind.OpenHelp);

        /// <summary>A god power was cast successfully (category from <see cref="CategoryResolver"/>).</summary>
        public bool OnPowerCast(GodPowerType power)
        {
            GodPowerCategory cat;
            var resolver = CategoryResolver;
            if (resolver == null) cat = DefaultCategoryOf(power);
            else
            {
                try { cat = resolver(power); }
                catch (Exception) { cat = DefaultCategoryOf(power); }
            }
            return OnPowerCast(power, cat);
        }

        /// <summary>A god power of a known category was cast successfully.</summary>
        public bool OnPowerCast(GodPowerType power, GodPowerCategory category)
        {
            if (!IsActive || _met) return false;
            if (!_steps[_index].Objective.MatchesCast(power, category)) return false;
            MarkMet();
            return true;
        }

        /// <summary><paramref name="days"/> in-game days passed (non-positive values are ignored).</summary>
        public bool OnDaysPassed(int days)
        {
            if (!IsActive || _met || days <= 0) return false;
            var o = _steps[_index].Objective;
            if (o.Kind != TutorialObjectiveKind.WaitDays) return false;
            _days = (int)Math.Min((long)_days + days, o.Days);
            if (_days < o.Days) return false;
            MarkMet();
            return true;
        }

        // ------------------------------------------------------------------ persistence

        /// <summary>
        /// Compact progress string: <c>t1;state;index;completedHex;skippedHex;currentStepId</c>.
        /// Per-step objective progress (days, met flag) is intentionally not stored.
        /// </summary>
        public string Serialize()
        {
            var sb = new StringBuilder(48);
            sb.Append(FormatTag).Append(';');
            sb.Append((int)_state).Append(';');
            sb.Append(_index.ToString(CultureInfo.InvariantCulture)).Append(';');
            sb.Append(_completed.ToString("x", CultureInfo.InvariantCulture)).Append(';');
            sb.Append(_skipped.ToString("x", CultureInfo.InvariantCulture)).Append(';');
            if (IsActive) sb.Append(_steps[_index].Id);
            return sb.ToString();
        }

        /// <summary>
        /// Restores progress written by <see cref="Serialize"/>. When the stored step id still exists it
        /// wins over the stored index (content may have been reordered). On any malformed input returns
        /// false and leaves the script unchanged.
        /// </summary>
        public bool TryDeserialize(string data)
        {
            if (string.IsNullOrEmpty(data) || data.Length > MaxSerializedLength) return false;
            string[] parts = data.Split(';');
            if (parts.Length != 6 || !string.Equals(parts[0], FormatTag, StringComparison.Ordinal)) return false;
            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int st) || st < 0 || st > 2) return false;
            if (!int.TryParse(parts[2], NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int idx)) return false;
            if (!TryParseHex(parts[3], out ulong done) || !TryParseHex(parts[4], out ulong skipped)) return false;

            ulong valid = _steps.Length >= 64 ? ulong.MaxValue : (1UL << _steps.Length) - 1UL;
            done &= valid;
            skipped &= valid & ~done;

            var state = (TutorialState)st;
            if (state == TutorialState.Active)
            {
                int byId = parts[5].Length > 0 ? IndexOf(parts[5]) : -1;
                if (byId >= 0) idx = byId;
                else if (idx < 0 || idx >= _steps.Length) return false;
            }
            else if (state == TutorialState.NotStarted) { idx = -1; done = 0; skipped = 0; }
            else idx = _steps.Length;

            _state = state;
            _index = idx;
            _completed = done;
            _skipped = skipped;
            ClearStepProgress();
            StepChanged?.Invoke(this);
            return true;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>Category of a power by the contract's enum grouping (fallback when no database is available).</summary>
        public static GodPowerCategory DefaultCategoryOf(GodPowerType power)
        {
            switch (power)
            {
                case GodPowerType.Bless: case GodPowerType.Rain: case GodPowerType.Fertility: case GodPowerType.Inspire:
                case GodPowerType.Heal: case GodPowerType.BountifulHarvest: case GodPowerType.Prophet:
                    return GodPowerCategory.Blessing;
                case GodPowerType.SpawnTribe:
                    return GodPowerCategory.Creation;
                case GodPowerType.RaiseLand: case GodPowerType.LowerLand: case GodPowerType.PlantForest: case GodPowerType.RevealOre:
                    return GodPowerCategory.Shaping;
                default:
                    return GodPowerCategory.Wrath;
            }
        }

        private bool Satisfy(TutorialObjectiveKind kind)
        {
            if (!IsActive || _met || _steps[_index].Objective.Kind != kind) return false;
            MarkMet();
            return true;
        }

        private void MarkMet()
        {
            _met = true;
            _metSeconds = 0f;
            ObjectiveCompleted?.Invoke(this);
        }

        private void MoveNext()
        {
            if (_index + 1 >= _steps.Length) { Finish(); return; }
            EnterStep(_index + 1);
        }

        private void EnterStep(int index)
        {
            _index = index;
            ClearStepProgress();
            StepChanged?.Invoke(this);
        }

        private void Finish()
        {
            _state = TutorialState.Finished;
            _index = _steps.Length;
            ClearStepProgress();
            StepChanged?.Invoke(this);
            Finished?.Invoke(this);
        }

        private void ClearStepProgress()
        {
            _met = false;
            _days = 0;
            _metSeconds = 0f;
        }

        private static bool TryParseHex(string s, out ulong value)
        {
            value = 0;
            if (string.IsNullOrEmpty(s) || s.Length > 16) return false;
            return ulong.TryParse(s, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out value);
        }

        private static int PopCount(ulong v)
        {
            int n = 0;
            while (v != 0) { v &= v - 1; n++; }
            return n;
        }
    }
}
