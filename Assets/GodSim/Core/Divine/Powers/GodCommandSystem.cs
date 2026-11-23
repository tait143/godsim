using System;
using System.Collections.Generic;

namespace GodSim.Core.Divine
{
    /// <summary>
    /// Executes the player-god's commands. Each tick it drains up to <see cref="MaxCommandsPerTick"/> commands
    /// from <c>ctx.Commands</c> (also bounded by <c>Config.MaxCommandsPerTick</c>; the rest wait for the next tick),
    /// re-validates every command against the CURRENT state (TOCTOU: the queue validated it at enqueue time, but
    /// mana, cooldowns and the world may have changed since), applies the effect and, if it took effect, charges
    /// mana, starts the cooldown, shifts alignment and the favor/devotion of affected civilizations, chronicles momentous casts, and
    /// publishes <see cref="GodPowerCastEvent"/>. Rejections publish <see cref="GodCommandRejectedEvent"/>.
    /// </summary>
    public sealed class GodCommandSystem : ISimSystem
    {
        /// <summary>Hard cap on commands processed (accepted or rejected) per tick.</summary>
        public const int MaxCommandsPerTick = 8;

        /// <inheritdoc />
        public string Name => "Divine.GodCommands";
        /// <inheritdoc />
        public int TickInterval => 1;

        /// <summary>Number of commands accepted since construction (diagnostics).</summary>
        public int AcceptedCount { get; private set; }
        /// <summary>Number of commands rejected at execution since construction (diagnostics).</summary>
        public int RejectedCount { get; private set; }

        /// <summary>Sanitizes the god state (NaN mana/alignment from a corrupted save never propagates).</summary>
        public void Initialize(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var god = ctx.State.God;
            god.Mana = Math.Max(0f, MathUtil.Sanitize(god.Mana));
            god.MaxMana = Math.Max(0f, MathUtil.Sanitize(god.MaxMana, 300f));
            god.Alignment = MathUtil.Clamp(MathUtil.Sanitize(god.Alignment), -1f, 1f);
        }

        /// <summary>Processes queued commands (bounded per tick).</summary>
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Commands == null) return;
            int limit = MaxCommandsPerTick;
            if (ctx.Config != null) limit = Math.Min(limit, Math.Max(1, ctx.Config.MaxCommandsPerTick));
            for (int n = 0; n < limit; n++)
            {
                if (!ctx.Commands.TryDequeue(out var cmd)) break;
                Execute(ctx, cmd, out _);
            }
        }

        /// <summary>
        /// Validates and (if valid) executes one command immediately. Returns true if the power was cast.
        /// Publishes <see cref="GodCommandRejectedEvent"/> on rejection and <see cref="GodPowerCastEvent"/> on a cast.
        /// </summary>
        public bool Execute(SimContext ctx, GodCommand cmd, out string reason)
        {
            reason = null;
            if (ctx == null || ctx.State == null || ctx.Db == null) { reason = "no world"; return false; }
            if (!Validate(ctx, cmd, out var def, out int radius, out reason))
            {
                RejectedCount++;
                ctx.Events?.Publish(new GodCommandRejectedEvent
                {
                    Power = cmd != null ? cmd.Power : default(GodPowerType),
                    Target = cmd != null ? cmd.Target : Vec2i.Invalid,
                    Reason = reason ?? "rejected",
                });
                return false;
            }

            var state = ctx.State;
            var god = state.God;

            // Who witnessed the cast (gathered before the effect, so a wrathful cast still angers its victims).
            // Locals, not fields: effects publish events mid-cast and a handler may re-enter Execute.
            var witnesses = new List<Settlement>();
            var civIds = new List<int>();
            GodPowerArea.SettlementsInRange(state, cmd.Target, radius, GodPowerArea.SettlementReach, witnesses);
            GodPowerArea.CivsOf(state, witnesses, civIds);

            var outcome = GodPowerEffects.Apply(ctx, def.Type, cmd.Target, radius, cmd.Parameter) ?? GodPowerOutcome.Fail("no effect");
            if (!outcome.Success)
            {
                // A failed effect changed nothing (GodPowerOutcome contract): treat as a rejection, charge nothing.
                reason = string.IsNullOrEmpty(outcome.Message) ? "the power fizzled" : outcome.Message;
                RejectedCount++;
                ctx.Events?.Publish(new GodCommandRejectedEvent { Power = def.Type, Target = cmd.Target, Reason = reason });
                return false;
            }

            float cost = Math.Max(0f, MathUtil.Sanitize(def.ManaCost));
            god.Mana = Math.Max(0f, MathUtil.Sanitize(god.Mana) - cost);
            god.CooldownUntil[(int)def.Type] = state.Tick + Math.Max(0, def.CooldownTicks);
            god.Alignment = MathUtil.Clamp(MathUtil.Sanitize(god.Alignment) + MathUtil.Sanitize(def.AlignmentDelta), -1f, 1f);
            god.PowersCast++;
            god.LastCastTick = state.Tick;
            state.Stats.GodPowersCast++;
            ApplyReactions(ctx, def, civIds);

            if (!string.IsNullOrEmpty(outcome.ChronicleText))
                ctx.Ops?.AddChronicle(ChronicleCategory.Divine, outcome.ChronicleText, outcome.CivilizationId, outcome.SettlementId, cmd.Target, MathUtil.Clamp(outcome.ChronicleImportance, 0, 3));

            AcceptedCount++;
            ctx.Events?.Publish(new GodPowerCastEvent
            {
                Power = def.Type,
                Target = cmd.Target,
                Radius = radius,
                Parameter = cmd.Parameter,
                Success = true,
                Message = string.IsNullOrEmpty(outcome.Message) ? def.Name : def.Name + ": " + outcome.Message,
            });
            return true;
        }

        /// <summary>
        /// Authoritative execution-time validation. Runs the installed <see cref="ICommandValidator"/> (if any) and
        /// then checks everything itself: power registered, target in bounds, radius within the definition's range
        /// (clamped for execution), parameter, world age, cooldown, mana, land target, settlement in range, and the
        /// power-specific preconditions in <see cref="GodPowerEffects.CheckPreconditions"/>.
        /// </summary>
        public static bool Validate(SimContext ctx, GodCommand cmd, out GodPowerDefinition def, out int radius, out string reason)
        {
            def = null;
            radius = 0;
            reason = null;
            if (ctx == null || ctx.State == null || ctx.Db == null) { reason = "no world"; return false; }
            if (cmd == null) { reason = "null command"; return false; }
            if (!Enum.IsDefined(typeof(GodPowerType), cmd.Power) || (int)cmd.Power >= Counts.GodPowers) { reason = "unknown power"; return false; }
            def = ctx.Db.GodPowers[(int)cmd.Power];
            if (def == null) { reason = "power not registered"; return false; }

            var state = ctx.State;
            var map = state.Map;
            var validator = ctx.Commands != null ? ctx.Commands.Validator : null;
            if (validator != null && !validator.Validate(cmd, state, ctx.Db, out string vr)) { reason = string.IsNullOrEmpty(vr) ? "rejected by validator" : vr; return false; }

            if (map == null || !map.InBounds(cmd.Target)) { reason = "target out of bounds"; return false; }
            if (cmd.Radius < def.MinRadius || cmd.Radius > def.MaxRadius)
            { reason = "radius " + cmd.Radius + " outside [" + def.MinRadius + ", " + def.MaxRadius + "]"; return false; }
            radius = GodPowerArea.ClampRadius(MathUtil.Clamp(cmd.Radius, def.MinRadius, def.MaxRadius));

            if (def.UsesRaceParameter)
            {
                int p = cmd.Parameter;
                if (p < 0 || p >= Counts.Races || !Enum.IsDefined(typeof(RaceId), (RaceId)p) || ctx.Db.Races[p] == null) { reason = "invalid race parameter"; return false; }
            }
            else if (cmd.Parameter != 0) { reason = "unexpected parameter"; return false; }

            if (state.WorldAge < def.MinWorldAge) { reason = def.Name + " is locked until the " + def.MinWorldAge + " age"; return false; }

            var god = state.God;
            long until = god.CooldownUntil[(int)def.Type];
            if (until > state.Tick) { reason = def.Name + " is on cooldown for " + (until - state.Tick) + " more ticks"; return false; }

            float cost = Math.Max(0f, MathUtil.Sanitize(def.ManaCost));
            float mana = MathUtil.Sanitize(god.Mana);
            if (!(mana + 1e-4f >= cost)) { reason = "insufficient mana (" + MathUtil.FloorToInt(mana) + "/" + MathUtil.RoundToInt(cost) + ")"; return false; }

            if (def.RequiresLandTarget && map.IsWater(cmd.Target)) { reason = "target must be land"; return false; }
            if (def.RequiresSettlementInRange && !GodPowerArea.AnySettlementInRange(state, cmd.Target, radius, GodPowerArea.SettlementReach))
            { reason = "no settlement within range"; return false; }

            if (!GodPowerEffects.CheckPreconditions(ctx, def.Type, cmd.Target, radius, cmd.Parameter, out reason)) return false;
            reason = null;
            return true;
        }

        /// <summary>
        /// Mortal reactions: blessings raise GodFavor (+0.05..0.15) and Devotion (+0.02..0.05) scaled by the
        /// power's mana cost; wrath lowers GodFavor (-0.1..-0.2) while pious races (Piety &gt; 0.6) grow more
        /// devoted out of fear. Shaping and creation leave mortal opinion unchanged.
        /// </summary>
        public static void ApplyReactions(SimContext ctx, GodPowerDefinition def, List<int> civIds)
        {
            if (ctx == null || ctx.State == null || def == null || civIds == null) return;
            float t = MathUtil.Clamp01(MathUtil.Sanitize(def.ManaCost) / 150f);
            for (int i = 0; i < civIds.Count; i++)
            {
                var civ = ctx.State.Civilizations.Get(civIds[i]);
                if (civ == null || !civ.IsAlive) continue;
                float favor = MathUtil.Sanitize(civ.GodFavor), devotion = MathUtil.Sanitize(civ.Devotion, 0.5f);
                if (def.Category == GodPowerCategory.Blessing)
                {
                    favor += 0.05f + 0.10f * t;
                    devotion += 0.02f + 0.03f * t;
                }
                else if (def.Category == GodPowerCategory.Wrath)
                {
                    favor -= 0.10f + 0.10f * t;
                    var race = ctx.Db != null ? ctx.Db.Races[(int)civ.Race] : null;
                    if (race != null && race.Piety > 0.6f) devotion += 0.02f + 0.03f * t;
                }
                civ.GodFavor = MathUtil.Clamp(favor, -1f, 1f);
                civ.Devotion = MathUtil.Clamp01(devotion);
            }
        }
    }
}
