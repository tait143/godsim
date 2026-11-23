using System;
using System.Collections.Generic;

namespace GodSim.Core.Security
{
    /// <summary>
    /// Production <see cref="ICommandValidator"/> for player-god commands. Treats every <see cref="GodCommand"/> as
    /// untrusted input (UI, replay file or, in future, a network peer) and checks, in order:
    /// non-null; Source (non-empty, &lt;= 64 chars, printable ASCII); Power is a defined enum with a registered, sane
    /// definition; Target in bounds; Radius within [MinRadius, MaxRadius] (and never above <see cref="AbsoluteMaxRadius"/>);
    /// Parameter == 0 unless the power uses a race parameter (then a defined, registered RaceId); world age gate;
    /// mana; cooldown; land target; an alive settlement within radius + <see cref="SettlementReachSlack"/>; and finally
    /// the <see cref="RateLimiter"/> (global per-tick cap + per-source token bucket).
    /// <para>
    /// <b>Two-phase use.</b> The command queue calls <see cref="Validate"/> at enqueue time and the divine module calls it
    /// again at execution (TOCTOU safety). Only the enqueue call may consume a rate-limit token. A call is treated as an
    /// execution re-validation when <c>command.Sequence &gt; 0</c> (the queue assigns it after enqueue) <b>and</b> this
    /// validator admitted that exact command object and has not yet spent its one free re-validation. Each admission
    /// buys exactly one free re-validation, so a forged <c>Sequence</c> on a new object, or re-submitting the same object
    /// over and over, cannot bypass rate limiting (at most 2 accepted validations per token).
    /// <see cref="ValidateForExecution"/> runs the pure checks only and never touches the limiter; UIs should use it for
    /// previews ("can I cast here?").
    /// </para>
    /// Reasons are short user-facing sentences and never echo untrusted text (such as <c>Source</c>).
    /// Not thread-safe (Core is single-threaded by contract).
    /// </summary>
    public sealed class GodCommandValidator : ICommandValidator
    {
        /// <summary>Maximum length of <see cref="GodCommand.Source"/>.</summary>
        public const int MaxSourceLength = 64;
        /// <summary>Extra tiles beyond the radius within which a settlement center must lie for RequiresSettlementInRange.</summary>
        public const int SettlementReachSlack = 3;
        /// <summary>Absolute radius cap, independent of content (defends against malicious/buggy definitions).</summary>
        public const int AbsoluteMaxRadius = 64;
        /// <summary>Maximum number of admitted commands awaiting their free execution re-validation.</summary>
        public const int AdmissionCapacity = 2048;

        private struct Admission
        {
            public int Credits;
            public long Stamp;
        }

        /// <summary>Identity comparer so that command equality can never be redefined by a future Equals override.</summary>
        private sealed class IdentityComparer : IEqualityComparer<GodCommand>
        {
            public static readonly IdentityComparer Instance = new IdentityComparer();
            public bool Equals(GodCommand x, GodCommand y) => ReferenceEquals(x, y);
            public int GetHashCode(GodCommand obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
        }

        private readonly RateLimiter _limiter;
        private readonly Dictionary<GodCommand, Admission> _admitted = new Dictionary<GodCommand, Admission>(IdentityComparer.Instance);
        private long _admissionStamp;

        /// <summary>
        /// Creates a validator whose rate limits derive from <paramref name="config"/>.MaxCommandsPerTick (clamped to the
        /// same range as <see cref="SimConfig.Sanitize"/>, without mutating the caller's config). A null config uses defaults.
        /// </summary>
        public GodCommandValidator(SimConfig config)
        {
            int perTick = config != null ? config.MaxCommandsPerTick : new SimConfig().MaxCommandsPerTick;
            MaxCommandsPerTick = MathUtil.Clamp(perTick, 1, 64);
            _limiter = new RateLimiter(MaxCommandsPerTick);
        }

        /// <summary>Effective global per-tick command cap.</summary>
        public int MaxCommandsPerTick { get; }
        /// <summary>The limiter used at enqueue time (exposed for diagnostics and tests).</summary>
        public RateLimiter RateLimiter => _limiter;
        /// <summary>Admitted commands still holding a free execution re-validation.</summary>
        public int PendingAdmissions => _admitted.Count;
        /// <summary>Total commands accepted at enqueue time.</summary>
        public long AdmittedCount { get; private set; }
        /// <summary>Total validations refused (any phase).</summary>
        public long RejectedCount { get; private set; }

        /// <summary>
        /// <see cref="ICommandValidator"/> entry point. Enqueue-time calls run every check plus rate limiting; execution
        /// re-validations (see class remarks) run the pure checks only and consume no token.
        /// </summary>
        public bool Validate(GodCommand command, WorldState state, GameDatabase db, out string reason)
        {
            if (command != null && command.Sequence > 0 && TrySpendCredit(command))
                return ValidateForExecution(command, state, db, out reason);

            if (!CheckRules(command, state, db, out reason)) { RejectedCount++; return false; }
            if (!_limiter.TryAcquire(command.Source, state.Tick, out reason)) { RejectedCount++; return false; }
            GrantCredit(command);
            AdmittedCount++;
            reason = null;
            return true;
        }

        /// <summary>
        /// Pure checks only (no rate limiting, no state change). Used by the divine module right before executing a
        /// dequeued command, and by UIs to preview whether a cast would be accepted.
        /// </summary>
        public bool ValidateForExecution(GodCommand command, WorldState state, GameDatabase db, out string reason)
        {
            if (CheckRules(command, state, db, out reason)) return true;
            RejectedCount++;
            return false;
        }

        // ------------------------------------------------------------------ rules

        private static bool CheckRules(GodCommand c, WorldState state, GameDatabase db, out string reason)
        {
            if (c == null) { reason = "No command was given."; return false; }
            if (state == null || state.Map == null || db == null) { reason = "The world is not ready to receive divine commands."; return false; }

            // Source first: cheap, O(1) for oversized strings, and it is the rate-limit key.
            string src = c.Source;
            if (src == null || src.Length == 0) { reason = "The command has no source."; return false; }
            if (src.Length > MaxSourceLength) { reason = "The command source is too long (at most " + MaxSourceLength + " characters)."; return false; }
            if (!IsPrintableAscii(src)) { reason = "The command source contains invalid characters (printable ASCII only)."; return false; }

            int pi = (int)c.Power;
            if (pi < 0 || pi >= Counts.GodPowers || pi >= db.GodPowers.Count || !Enum.IsDefined(typeof(GodPowerType), c.Power))
            { reason = "Unknown divine power."; return false; }
            GodPowerDefinition def = db.GodPowers[pi];
            if (def == null) { reason = "That divine power does not exist in this world."; return false; }
            string name = PowerName(def, c.Power);
            if (!IsSaneDefinition(def)) { reason = name + " is misconfigured and cannot be used."; return false; }

            if (!state.Map.InBounds(c.Target)) { reason = "The target is outside the world."; return false; }

            int minR = def.MinRadius, maxR = Math.Min(def.MaxRadius, AbsoluteMaxRadius);
            if (c.Radius < minR || c.Radius > maxR)
            {
                reason = "Radius " + c.Radius + " is not allowed for " + name + " (allowed " + minR + "-" + maxR + ").";
                return false;
            }

            if (def.UsesRaceParameter)
            {
                if (c.Parameter < 0 || c.Parameter >= Counts.Races || c.Parameter >= db.Races.Count || db.Races[c.Parameter] == null)
                { reason = name + " requires a valid race."; return false; }
            }
            else if (c.Parameter != 0) { reason = name + " does not take a parameter."; return false; }

            if (state.WorldAge < def.MinWorldAge)
            { reason = name + " is not available until a civilization reaches the " + def.MinWorldAge + " age."; return false; }

            float mana = state.God.Mana;
            if (float.IsNaN(mana) || float.IsInfinity(mana)) { reason = "Divine energy is unstable; casting is blocked."; return false; }
            if (!(mana >= def.ManaCost))
            {
                reason = "Not enough mana: " + name + " costs " + Ceil(def.ManaCost) + " but you have " + Floor(mana) + ".";
                return false;
            }

            long ready = state.God.CooldownUntil[pi];
            if (state.Tick < ready)
            {
                long wait = ready - state.Tick;
                reason = name + " is recharging (" + wait + (wait == 1 ? " more day)." : " more days).");
                return false;
            }

            if (def.RequiresLandTarget && state.Map.IsWater(c.Target)) { reason = name + " must target land."; return false; }

            if (def.RequiresSettlementInRange && !HasSettlementWithin(state, c.Target, c.Radius + SettlementReachSlack))
            {
                reason = name + " must be cast within " + (c.Radius + SettlementReachSlack) + " tiles of a living settlement.";
                return false;
            }

            reason = null;
            return true;
        }

        private static bool IsSaneDefinition(GodPowerDefinition d)
        {
            if (float.IsNaN(d.ManaCost) || float.IsInfinity(d.ManaCost) || d.ManaCost < 0f) return false;
            if (d.CooldownTicks < 0 || d.MinRadius < 0 || d.MaxRadius < d.MinRadius) return false;
            if (d.MinRadius > AbsoluteMaxRadius) return false;
            return true;
        }

        private static bool HasSettlementWithin(WorldState state, Vec2i target, int reach)
        {
            long r = reach, r2 = r * r;
            var items = state.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (s == null || !s.IsAlive) continue;
                long dx = (long)s.Center.X - target.X, dy = (long)s.Center.Y - target.Y;
                if (dx > r || dx < -r || dy > r || dy < -r) continue; // also keeps the squares below overflow-free
                if (dx * dx + dy * dy <= r2) return true;
            }
            return false;
        }

        private static bool IsPrintableAscii(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char ch = s[i];
                if (ch < ' ' || ch > '~') return false;
            }
            return true;
        }

        private static string PowerName(GodPowerDefinition def, GodPowerType type)
        {
            string n = def.Name;
            return !string.IsNullOrEmpty(n) && n.Length <= ContentValidator.MaxNameLength && InputSanitizer.IsSafeDisplayText(n, false)
                ? n : type.ToString();
        }

        private static long Ceil(float v) => v >= 1e15f ? (long)1e15 : (long)Math.Ceiling(v);
        private static long Floor(float v) => v >= 1e15f ? (long)1e15 : (long)Math.Floor(v);

        // ------------------------------------------------------------------ admission credits

        private bool TrySpendCredit(GodCommand c)
        {
            if (!_admitted.TryGetValue(c, out Admission a)) return false;
            a.Credits--;
            if (a.Credits <= 0) _admitted.Remove(c);
            else _admitted[c] = a;
            return true;
        }

        private void GrantCredit(GodCommand c)
        {
            if (_admitted.TryGetValue(c, out Admission a))
            {
                a.Credits++;
                a.Stamp = ++_admissionStamp;
                _admitted[c] = a;
                return;
            }
            if (_admitted.Count >= AdmissionCapacity) EvictOldestAdmission();
            _admitted.Add(c, new Admission { Credits = 1, Stamp = ++_admissionStamp });
        }

        /// <summary>Drops the admission with the smallest stamp (stamps are unique: deterministic choice).</summary>
        private void EvictOldestAdmission()
        {
            GodCommand victim = null;
            long best = long.MaxValue;
            foreach (var kv in _admitted)
                if (kv.Value.Stamp < best) { best = kv.Value.Stamp; victim = kv.Key; }
            if (victim != null) _admitted.Remove(victim);
        }
    }
}
