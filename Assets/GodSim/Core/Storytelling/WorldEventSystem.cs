using System;
using System.Collections.Generic;

namespace GodSim.Core.Storytelling
{
    /// <summary>
    /// Emergent storytelling: every run (48 ticks = half a year), each alive civilization has a small chance
    /// (6% x sqrt(settlements), capped at x3) of drawing one card from <see cref="WorldEventCatalog"/>: a
    /// weighted-random pick among the eligible events, via ctx.Rng. Cooldowns live in civ.Memory (and
    /// WorldState.Memory for world-wide events) under "story.cd.&lt;key&gt;" = last fired tick, so they survive
    /// save/load; the system itself holds no persistent state. A civ that just had an event rests 1.5 years.
    /// Composition: run after Diplomacy.
    /// </summary>
    public sealed class WorldEventSystem : ISimSystem
    {
        public const int Interval = 48;
        /// <summary>Chance per run (at <see cref="Interval"/>) for a one-settlement civ.</summary>
        public const float BaseChancePerRun = 0.06f;
        public const float MaxSettlementChanceScale = 3f;
        /// <summary>Quiet period after any event for the same civ (Tick only; TryFire ignores it).</summary>
        public const float MinYearsBetweenEvents = 1.5f;

        public const string MemoryPrefix = "story.";
        public const string CooldownPrefix = "story.cd.";
        /// <summary>Civ.Memory: tick of the civ's last event.</summary>
        public const string LastEventTickKey = "story.lastTick";
        /// <summary>WorldState.Memory: designer/test multiplier on the event chance (0..10, default 1).</summary>
        public const string ChanceScaleKey = "story.chanceScale";
        /// <summary>WorldState.Memory: total events fired in this world.</summary>
        public const string TotalEventsKey = "story.total";
        /// <summary>Civ.Memory: last observed LeaderId.</summary>
        public const string LeaderLastKey = "story.leader.last";
        /// <summary>Civ.Memory: tick the last observed leader was found dead/gone.</summary>
        public const string LeaderLostTickKey = "story.leader.lostTick";

        private readonly WorldEventContext _e = new WorldEventContext();
        private readonly List<float> _weights = new List<float>(32);
        private readonly List<int> _defIndex = new List<int>(32);

        public string Name => "Storytelling.WorldEvents";
        public int TickInterval => Interval;

        /// <summary>Events fired by this instance (informational; not persisted).</summary>
        public int EventsFired { get; private set; }

        public void Initialize(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) if (civs[i] != null && civs[i].IsAlive) TrackLeader(ctx.State, civs[i]);
        }

        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            var st = ctx.State;
            var civs = st.Civilizations.Items;
            int n = civs.Count; // civs founded by secession this run are not rolled until the next run
            float perRun = PerRunChance(ctx.DeltaTicks) * ChanceScale(st);
            if (perRun <= 0f)
            {
                for (int i = 0; i < n; i++) if (civs[i] != null && civs[i].IsAlive) TrackLeader(st, civs[i]);
                return;
            }
            for (int i = 0; i < n && i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                try
                {
                    TrackLeader(st, civ);
                    int settlements = StoryUtil.AliveSettlementCount(st, civ);
                    if (settlements == 0 || InQuietPeriod(st, civ)) continue;
                    float chance = perRun * MathUtil.Clamp((float)Math.Sqrt(settlements), 1f, MaxSettlementChanceScale);
                    if (!ctx.Rng.Chance(chance)) continue;
                    if (RollAndFire(ctx, civ)) EventsFired++;
                }
                catch (Exception ex)
                {
                    ctx.Log.Error("WorldEventSystem: civ " + civ.Id + " event failed: " + ex);
                    if (ctx.Config != null && ctx.Config.StrictMode) throw;
                }
            }
        }

        /// <summary>
        /// Test/debug hook: fires the event if the civ is alive, the event is off cooldown and all its conditions
        /// hold (bypasses the random chance and the quiet period). Returns true if it fired.
        /// </summary>
        public static bool TryFire(SimContext ctx, Civilization civ, string key)
        {
            if (ctx == null || ctx.State == null || ctx.Ops == null || ctx.Db == null || civ == null || !civ.IsAlive) return false;
            var def = WorldEventCatalog.Find(key);
            if (def == null) return false;
            var e = new WorldEventContext();
            e.Reset(ctx, civ);
            if (Evaluate(def, e) <= 0f) return false;
            return Fire(def, e);
        }

        /// <summary>True if the event is eligible for the civ right now (cooldown + conditions + target). No side effects.</summary>
        public static bool IsEligible(SimContext ctx, Civilization civ, string key)
        {
            if (ctx == null || ctx.State == null || civ == null || !civ.IsAlive) return false;
            var def = WorldEventCatalog.Find(key);
            if (def == null) return false;
            var e = new WorldEventContext();
            e.Reset(ctx, civ);
            return Evaluate(def, e) > 0f;
        }

        /// <summary>True if the event is on cooldown for the civ (civ memory, plus world memory for world-wide events).</summary>
        public static bool IsOnCooldown(WorldState st, Civilization civ, WorldEventDefinition def)
        {
            if (st == null || def == null) return false;
            long cd = def.CooldownTicks;
            if (civ != null && Within(civ.Memory, def.CooldownMemoryKey, st.Tick, cd)) return true;
            return def.WorldCooldown && Within(st.Memory, def.CooldownMemoryKey, st.Tick, cd);
        }

        // ------------------------------------------------------------------ internals
        private bool RollAndFire(SimContext ctx, Civilization civ)
        {
            _e.Reset(ctx, civ);
            _weights.Clear();
            _defIndex.Clear();
            var all = WorldEventCatalog.All;
            for (int d = 0; d < all.Count; d++)
            {
                float w = Evaluate(all[d], _e);
                if (w > 0f) { _weights.Add(w); _defIndex.Add(d); }
            }
            if (_weights.Count == 0) return false;
            int pick = ctx.Rng.WeightedIndex(_weights);
            if (pick < 0) return false;
            var def = all[_defIndex[pick]];
            if (Evaluate(def, _e) <= 0f) return false; // re-resolve targets (later evaluations cleared them)
            return Fire(def, _e);
        }

        /// <summary>Weight of the event for e.Civ (0 = not eligible). Resolves targets into e. Never uses RNG.</summary>
        internal static float Evaluate(WorldEventDefinition def, WorldEventContext e)
        {
            if (def == null || def.Effect == null) return 0f;
            var civ = e.Civ;
            var st = e.State;
            e.ClearTargets();
            e.Definition = def;
            if (civ.Age < def.MinAge || civ.Age > def.MaxAge) return 0f;
            if ((def.SeasonMask & Seasons.Of(st.Season)) == 0) return 0f;
            if (e.SettlementCount < Math.Max(1, def.MinSettlements)) return 0f;
            if (IsOnCooldown(st, civ, def)) return 0f;
            var conds = def.Conditions;
            if (conds != null)
                for (int i = 0; i < conds.Length; i++)
                    if (conds[i] != null && !conds[i](e)) return 0f;
            if (def.SelectTarget != null && !def.SelectTarget(e)) return 0f;
            float w = MathUtil.Sanitize(def.BaseWeight);
            if (def.Weight != null) w *= MathUtil.Sanitize(def.Weight(e));
            return MathUtil.Clamp(w, 0f, 1000f);
        }

        /// <summary>Applies the (already evaluated) event, stamps cooldowns and writes the chronicle.</summary>
        internal static bool Fire(WorldEventDefinition def, WorldEventContext e)
        {
            e.Amount = 0f;
            e.Count = 0;
            e.UseAltTemplates = false;
            if (!def.Effect(e)) return false;
            var st = e.State;
            float now = st.Tick;
            e.Civ.Memory[def.CooldownMemoryKey] = now;
            e.Civ.Memory[LastEventTickKey] = now;
            if (def.WorldCooldown) st.Memory[def.CooldownMemoryKey] = now;
            if (def.CooldownAppliesToOther && e.OtherCiv != null && e.OtherCiv != e.Civ) e.OtherCiv.Memory[def.CooldownMemoryKey] = now;
            st.Memory.TryGetValue(TotalEventsKey, out float total);
            st.Memory[TotalEventsKey] = MathUtil.Clamp(MathUtil.Sanitize(total) + 1f, 0f, 1e7f);
            WriteChronicle(def, e);
            return true;
        }

        private static void WriteChronicle(WorldEventDefinition def, WorldEventContext e)
        {
            var templates = e.UseAltTemplates && def.AltTemplates != null && def.AltTemplates.Length > 0 ? def.AltTemplates : def.Templates;
            string text = templates != null && templates.Length > 0 ? templates[e.Rng.Range(0, templates.Length)] : def.Title;
            text = StoryText.Format(text, e);
            var map = e.Map;
            Vec2i pos;
            if (e.TileIndex >= 0 && e.TileIndex < map.TileCount) pos = map.PosOf(e.TileIndex);
            else if (e.Settlement != null) pos = e.Settlement.Center;
            else { var cap = StoryUtil.Capital(e.State, e.Civ); pos = cap != null ? cap.Center : Vec2i.Invalid; }
            e.Ops.AddChronicle(def.Category, text, e.Civ.Id, e.Settlement != null ? e.Settlement.Id : -1, pos, def.Importance);
        }

        /// <summary>Records leader changes so "the leader just died" can be detected (bounded: two keys per civ).</summary>
        internal static void TrackLeader(WorldState st, Civilization civ)
        {
            bool had = civ.Memory.TryGetValue(LeaderLastKey, out float prev);
            int prevId = had ? (int)MathUtil.Sanitize(prev, -1f) : -1;
            if (had && prevId == civ.LeaderId) return;
            if (had && prevId >= 0)
            {
                var p = st.People.Get(prevId);
                if (p == null || !p.IsAlive || p.CivilizationId != civ.Id) civ.Memory[LeaderLostTickKey] = st.Tick;
            }
            civ.Memory[LeaderLastKey] = civ.LeaderId;
        }

        private static bool InQuietPeriod(WorldState st, Civilization civ) =>
            Within(civ.Memory, LastEventTickKey, st.Tick, SimTime.YearsToTicks(MinYearsBetweenEvents));

        /// <summary>True if memory[key] holds a tick t with 0 &lt;= now - t &lt; span. Corrupt/future values are ignored.</summary>
        private static bool Within(Dictionary<string, float> memory, string key, long now, long span)
        {
            if (span <= 0 || memory == null || !memory.TryGetValue(key, out float v)) return false;
            if (float.IsNaN(v) || float.IsInfinity(v)) return false;
            long t = (long)v;
            return t <= now && now - t < span;
        }

        private static float PerRunChance(int deltaTicks)
        {
            if (deltaTicks <= 0) return 0f;
            double p = 1.0 - Math.Pow(1.0 - BaseChancePerRun, deltaTicks / (double)Interval);
            return MathUtil.Clamp01((float)p);
        }

        private static float ChanceScale(WorldState st)
        {
            if (!st.Memory.TryGetValue(ChanceScaleKey, out float v)) return 1f;
            return MathUtil.Clamp(MathUtil.Sanitize(v, 1f), 0f, 10f);
        }
    }
}
