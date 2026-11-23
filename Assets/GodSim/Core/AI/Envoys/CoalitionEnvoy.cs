using System;
using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>
    /// COALITIONS: when one civ is a hegemon (military power or settlement count above
    /// <see cref="EnvoyTuning.HegemonRatio"/>x the runner-up), the weaker civs in contact with it close ranks: every
    /// run each pair of members gains <see cref="EnvoyTuning.CoalitionImpulse"/> opinion, cools slightly toward the
    /// hegemon, and stores <c>civ.Memory["env.coalition"] = hegemonId</c>. The formation is chronicled once; the
    /// coalition dissolves (also chronicled) once the hegemon's lead falls below <see cref="EnvoyTuning.HegemonLeaveRatio"/>x.
    /// </summary>
    public sealed class CoalitionEnvoy
    {
        private readonly EnvoyKeyCache _keys = new EnvoyKeyCache();
        private readonly List<Civilization> _members = new List<Civilization>();

        /// <summary>Members found in the last run (read-only view for tests/UI).</summary>
        public IReadOnlyList<Civilization> Members => _members;

        /// <summary>
        /// Finds the hegemon (or -1): the leader in military power or in settlements whose lead over the runner-up is at
        /// least <paramref name="ratio"/>. Military is checked first. Needs at least three alive civs.
        /// </summary>
        public static int FindHegemon(WorldState state, float ratio)
        {
            if (state == null) return -1;
            var civs = state.Civilizations.Items;
            int alive = 0;
            Civilization topP = null, topS = null;
            float p1 = 0f, p2 = 0f;
            int s1 = 0, s2 = 0;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (!EnvoyUtil.IsAlive(c)) continue;
                int sc = EnvoyUtil.SettlementCount(state, c);
                if (sc <= 0) continue;
                alive++;
                float p = EnvoyUtil.Strength(c);
                if (topP == null || p > p1) { p2 = topP == null ? 0f : p1; p1 = p; topP = c; }
                else if (p > p2) p2 = p;
                if (topS == null || sc > s1) { s2 = topS == null ? 0 : s1; s1 = sc; topS = c; }
                else if (sc > s2) s2 = sc;
            }
            if (alive < 3) return -1;
            if (topP != null && p1 >= EnvoyTuning.HegemonMinPower && p1 > ratio * Math.Max(1f, p2)) return topP.Id;
            if (topS != null && s2 > 0 && s1 > ratio * s2) return topS.Id;
            return -1;
        }

        /// <summary>Willingness to join (0..1): 0.5 Diplomacy + 0.3 (1 - Xenophobia) + 0.2 Aggression.</summary>
        public static float Willingness(GameDatabase db, Civilization c) =>
            MathUtil.Clamp01(0.5f * EnvoyUtil.Trait(db, c, AITrait.Diplomacy) + 0.3f * (1f - EnvoyUtil.Trait(db, c, AITrait.Xenophobia))
                             + 0.2f * EnvoyUtil.Trait(db, c, AITrait.Aggression));

        /// <summary>Runs one coalition update. Returns the hegemon id the coalition stands against (or -1).</summary>
        public int Run(SimContext ctx)
        {
            _members.Clear();
            if (ctx == null || ctx.State == null) return -1;
            var state = ctx.State;
            int previous = CurrentHegemon(state);
            int hegemon = FindHegemon(state, EnvoyTuning.HegemonRatio);
            if (hegemon < 0 && previous >= 0 && FindHegemon(state, EnvoyTuning.HegemonLeaveRatio) == previous) hegemon = previous;
            if (previous >= 0 && previous != hegemon) Dissolve(ctx, previous);
            if (hegemon < 0) return -1;

            var heg = state.Civilizations.Get(hegemon);
            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (c == heg || !EnvoyUtil.IsAlive(c) || EnvoyUtil.SettlementCount(state, c) == 0) continue;
                var r = EnvoyUtil.RelationOf(state, c.Id, hegemon);
                bool joins = r != null && r.HasContact && r.Stance != DiplomaticStance.Allied
                             && EnvoyUtil.Opinion(r) < EnvoyTuning.CoalitionMaxHegemonOpinion
                             && Willingness(ctx.Db, c) >= EnvoyTuning.CoalitionMinWillingness;
                if (joins) _members.Add(c);
                else if (MathUtil.Approximately(c.Mem(EnvoyKeys.Coalition, -1f), hegemon)) c.Memory.Remove(EnvoyKeys.Coalition);
            }
            if (_members.Count < 2)
            {
                for (int i = 0; i < _members.Count; i++) _members[i].Memory.Remove(EnvoyKeys.Coalition);
                if (state.Memory.Remove(_keys.CoalitionFormed(hegemon))) Chronicle(ctx, heg, false);
                _members.Clear();
                return -1;
            }

            for (int i = 0; i < _members.Count; i++)
            {
                var a = _members[i];
                a.Memory[EnvoyKeys.Coalition] = hegemon;
                EnvoyUtil.AddOpinion(EnvoyUtil.RelationOf(state, a.Id, hegemon), EnvoyTuning.CoalitionHegemonImpulse);
                for (int j = i + 1; j < _members.Count; j++)
                {
                    var b = _members[j];
                    var r = EnvoyUtil.RelationOf(state, a.Id, b.Id);
                    if (r == null || r.AtWar) continue;
                    EnvoyUtil.AddOpinion(r, EnvoyTuning.CoalitionImpulse);
                }
            }
            string formedKey = _keys.CoalitionFormed(hegemon);
            if (!state.Memory.ContainsKey(formedKey))
            {
                state.Memory[formedKey] = state.Tick;
                Chronicle(ctx, heg, true);
            }
            return hegemon;
        }

        private static int CurrentHegemon(WorldState state)
        {
            var civs = state.Civilizations.Items;
            int best = -1;
            for (int i = 0; i < civs.Count; i++)
            {
                if (!civs[i].Memory.TryGetValue(EnvoyKeys.Coalition, out float v) || float.IsNaN(v) || v < 0f) continue;
                int id = (int)v;
                if (best < 0 || id < best) best = id;
            }
            return best;
        }

        private void Dissolve(SimContext ctx, int hegemon)
        {
            var state = ctx.State;
            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
                if (MathUtil.Approximately(civs[i].Mem(EnvoyKeys.Coalition, -1f), hegemon)) civs[i].Memory.Remove(EnvoyKeys.Coalition);
            if (state.Memory.Remove(_keys.CoalitionFormed(hegemon))) Chronicle(ctx, state.Civilizations.Get(hegemon), false);
        }

        private void Chronicle(SimContext ctx, Civilization heg, bool formed)
        {
            string hegName = heg != null ? "the " + EnvoyUtil.Plural(ctx.Db, heg) : "a fallen power";
            if (!formed)
            {
                EnvoyUtil.Chronicle(ctx, heg, "With the might of " + hegName + " checked, the grand coalition against them dissolved.", 1);
                return;
            }
            var sb = new StringBuilder(160);
            sb.Append("Fearing the might of ").Append(hegName).Append(", the ");
            for (int i = 0; i < _members.Count; i++)
            {
                if (i > 0) sb.Append(i == _members.Count - 1 ? " and the " : ", the ");
                sb.Append(EnvoyUtil.PeopleOf(ctx.State, ctx.Db, _members[i]));
            }
            sb.Append(" joined in a grand coalition.");
            EnvoyUtil.Chronicle(ctx, heg, sb.ToString(), 2);
        }
    }
}
