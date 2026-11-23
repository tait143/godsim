using System;
using System.Collections.Generic;

namespace GodSim.Core.Diplomacy
{
    /// <summary>
    /// Read-only diplomacy questions other modules (AI, military, UI) may ask. Every method tolerates
    /// unknown ids, dead civs and missing relations (it never throws on bad data and never creates
    /// relations).
    /// </summary>
    public static class DiplomacyQueries
    {
        /// <summary>Opinion between two civs (-100..100). 100 for a civ with itself, 0 when no relation exists.</summary>
        public static float Opinion(WorldState state, int civA, int civB)
        {
            if (state == null) return 0f;
            if (civA == civB) return RelationsTuning.MaxOpinion;
            return state.Diplomacy.TryGet(civA, civB, out var r) ? MathUtil.Sanitize(r.Opinion) : 0f;
        }

        /// <summary>Current stance between two civs (Allied for itself, Neutral when no relation exists).</summary>
        public static DiplomaticStance Stance(WorldState state, int civA, int civB)
        {
            if (state == null) return DiplomaticStance.Neutral;
            if (civA == civB) return DiplomaticStance.Allied;
            if (!state.Diplomacy.TryGet(civA, civB, out var r)) return DiplomaticStance.Neutral;
            return r.AtWar ? DiplomaticStance.War : r.Stance;
        }

        /// <summary>True when the two (different) civs are at war or their stance is Hostile.</summary>
        public static bool IsHostile(WorldState state, int civA, int civB)
        {
            if (state == null || civA == civB) return false;
            if (!state.Diplomacy.TryGet(civA, civB, out var r)) return false;
            return r.AtWar || r.Stance <= DiplomaticStance.Hostile;
        }

        /// <summary>
        /// Military strength ratio civ.MilitaryPower / max(1, target.MilitaryPower). 0 if the civ is unknown;
        /// a missing target counts as power 0.
        /// </summary>
        public static float StrengthRatio(WorldState state, int civId, int targetId)
        {
            if (state == null) return 0f;
            var civ = state.Civilizations.Get(civId);
            if (civ == null) return 0f;
            var target = state.Civilizations.Get(targetId);
            return StrengthRatio(civ, target);
        }

        /// <summary>Strength ratio of two civ objects (see <see cref="StrengthRatio(WorldState,int,int)"/>).</summary>
        public static float StrengthRatio(Civilization civ, Civilization target)
        {
            if (civ == null) return 0f;
            float mine = Math.Max(0f, MathUtil.Sanitize(civ.MilitaryPower));
            float theirs = target != null ? Math.Max(0f, MathUtil.Sanitize(target.MilitaryPower)) : 0f;
            return MathUtil.Sanitize(mine / Math.Max(1f, theirs));
        }

        /// <summary>
        /// Fills <paramref name="results"/> (cleared first) with the ids of alive civs in contact with
        /// <paramref name="civId"/>, ascending by id. With <paramref name="maxDistance"/> &gt;= 0 only civs
        /// with a settlement within that many tiles (Chebyshev, center to center) of one of ours are kept.
        /// Returns the number of neighbours.
        /// </summary>
        public static int Neighbours(WorldState state, int civId, List<int> results, int maxDistance = -1)
        {
            if (results == null) return 0;
            results.Clear();
            if (state == null) return 0;
            var me = state.Civilizations.Get(civId);
            if (me == null || !me.IsAlive) return 0;
            // AllRelations is ordered by (lo, hi), so the "other" ids of civId's relations come out ascending.
            foreach (var r in state.Diplomacy.AllRelations)
            {
                if (r == null || !r.HasContact) continue;
                int other;
                if (r.CivA == civId) other = r.CivB;
                else if (r.CivB == civId) other = r.CivA;
                else continue;
                var oc = state.Civilizations.Get(other);
                if (oc == null || !oc.IsAlive) continue;
                if (maxDistance >= 0 && MinSettlementDistance(state, civId, other) > maxDistance) continue;
                results.Add(other);
            }
            return results.Count;
        }

        /// <summary>Number of active wars a civ takes part in (as attacker or defender).</summary>
        public static int ActiveWarCount(WorldState state, int civId)
        {
            if (state == null) return 0;
            var wars = state.Diplomacy.Wars;
            int n = 0;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w != null && w.IsActive && (w.AttackerId == civId || w.DefenderId == civId)) n++;
            }
            return n;
        }

        /// <summary>True if both civs are at war with the same third civ.</summary>
        public static bool ShareEnemy(WorldState state, int civA, int civB)
        {
            if (state == null) return false;
            return ShareEnemy(state.Diplomacy.Wars, civA, civB);
        }

        /// <summary>ShareEnemy over a given war list (systems pass a pre-filtered list of active wars).</summary>
        internal static bool ShareEnemy(List<War> wars, int civA, int civB)
        {
            if (wars == null || civA == civB) return false;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null || !w.IsActive) continue;
                int enemy;
                if (w.AttackerId == civA) enemy = w.DefenderId;
                else if (w.DefenderId == civA) enemy = w.AttackerId;
                else continue;
                if (enemy == civB) continue;
                if (IsInActiveWar(wars, civB, enemy)) return true;
            }
            return false;
        }

        /// <summary>Smallest Chebyshev distance between alive settlement centers of two civs (int.MaxValue if either has none).</summary>
        public static int MinSettlementDistance(WorldState state, int civA, int civB)
        {
            int best = int.MaxValue;
            if (state == null) return best;
            var all = state.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || !a.IsAlive || a.CivilizationId != civA) continue;
                for (int j = 0; j < all.Count; j++)
                {
                    var b = all[j];
                    if (b == null || !b.IsAlive || b.CivilizationId != civB) continue;
                    int d = a.Center.ChebyshevTo(b.Center);
                    if (d < best) best = d;
                }
            }
            return best;
        }

        /// <summary>Number of settlement pairs of two civs whose centers lie within <paramref name="range"/> tiles (Chebyshev).</summary>
        public static int BorderPairs(WorldState state, int civA, int civB, int range = RelationsTuning.BorderRange)
        {
            if (state == null || civA == civB) return 0;
            int n = 0;
            var all = state.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var a = all[i];
                if (a == null || !a.IsAlive || a.CivilizationId != civA) continue;
                for (int j = 0; j < all.Count; j++)
                {
                    var b = all[j];
                    if (b == null || !b.IsAlive || b.CivilizationId != civB) continue;
                    if (a.Center.ChebyshevTo(b.Center) <= range) n++;
                }
            }
            return n;
        }

        /// <summary>Tick the last war between two civs ended (from the world blackboard), or -1 if they never fought.</summary>
        public static long LastWarEndTick(WorldState state, int civA, int civB)
        {
            if (state == null || civA == civB) return -1;
            return state.Memory.TryGetValue(RelationsMemoryKeys.LastWar(civA, civB), out float t) ? ToTick(t) : -1;
        }

        /// <summary>True while a truce between the two civs is in force.</summary>
        public static bool InTruce(WorldState state, int civA, int civB)
        {
            if (state == null || civA == civB) return false;
            return state.Diplomacy.TryGet(civA, civB, out var r) && r.TruceUntilTick > state.Tick;
        }

        /// <summary>True if the favors have opposite signs (each outside the indifference dead zone).</summary>
        public static bool FavorSignsDiffer(float favorA, float favorB)
        {
            int a = FavorSign(favorA), b = FavorSign(favorB);
            return a != 0 && b != 0 && a != b;
        }

        /// <summary>True if both favors have the same non-zero sign (outside the indifference dead zone).</summary>
        public static bool FavorSignsMatch(float favorA, float favorB)
        {
            int a = FavorSign(favorA);
            return a != 0 && a == FavorSign(favorB);
        }

        /// <summary>Mutual race affinity: average of both races' RaceAffinity toward each other, clamped to [-1, 1].</summary>
        public static float MutualAffinity(RaceDefinition a, RaceDefinition b)
        {
            if (a == null || b == null) return 0f;
            float ab = AffinityOf(a, b.Id), ba = AffinityOf(b, a.Id);
            return MathUtil.Clamp((ab + ba) * 0.5f, -1f, 1f);
        }

        // ------------------------------------------------------------------ internal helpers
        internal static int FavorSign(float favor)
        {
            favor = MathUtil.Sanitize(favor);
            if (favor > RelationsTuning.GodFavorDeadZone) return 1;
            if (favor < -RelationsTuning.GodFavorDeadZone) return -1;
            return 0;
        }

        internal static float AffinityOf(RaceDefinition r, RaceId toward)
        {
            if (r == null || r.RaceAffinity == null) return 0f;
            int i = (int)toward;
            if (i < 0 || i >= r.RaceAffinity.Length) return 0f;
            return MathUtil.Clamp(MathUtil.Sanitize(r.RaceAffinity[i]), -1f, 1f);
        }

        internal static RaceDefinition RaceOf(GameDatabase db, RaceId id)
        {
            if (db == null) return null;
            int i = (int)id;
            var races = db.Races;
            return i >= 0 && i < races.Count ? races[i] : null;
        }

        /// <summary>Personality trait sanitized to 0..1.</summary>
        internal static float Trait(float v) => MathUtil.Clamp01(MathUtil.Sanitize(v));

        internal static bool IsAlive(Civilization c) => c != null && c.IsAlive;

        internal static long ToTick(float stored) => (long)Math.Round(MathUtil.Sanitize(stored, -1f));

        /// <summary>Copies the ordered relation set into a reusable list (safe against creation during iteration).</summary>
        internal static void Snapshot(WorldState state, List<Relation> into)
        {
            into.Clear();
            foreach (var r in state.Diplomacy.AllRelations) if (r != null) into.Add(r);
        }

        private static bool IsInActiveWar(List<War> wars, int a, int b)
        {
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null || !w.IsActive) continue;
                if ((w.AttackerId == a && w.DefenderId == b) || (w.AttackerId == b && w.DefenderId == a)) return true;
            }
            return false;
        }
    }
}
