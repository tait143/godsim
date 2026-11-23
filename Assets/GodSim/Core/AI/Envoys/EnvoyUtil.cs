using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>Small, allocation-free helpers shared by the envoy behaviours. Every method tolerates null/missing data.</summary>
    public static class EnvoyUtil
    {
        /// <summary>True when the civ exists and is alive.</summary>
        public static bool IsAlive(Civilization c) => c != null && c.IsAlive;

        /// <summary>The civ's capital (or first alive settlement); null if none.</summary>
        public static Settlement Capital(WorldState state, Civilization civ)
        {
            if (state == null || civ == null) return null;
            var cap = state.Settlements.Get(civ.CapitalId);
            if (cap != null && cap.IsAlive && cap.CivilizationId == civ.Id) return cap;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s != null && s.IsAlive && s.CivilizationId == civ.Id) return s;
            }
            return null;
        }

        /// <summary>Number of alive settlements the civ owns.</summary>
        public static int SettlementCount(WorldState state, Civilization civ)
        {
            if (state == null || civ == null) return 0;
            int n = 0;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s != null && s.IsAlive && s.CivilizationId == civ.Id) n++;
            }
            return n;
        }

        /// <summary>The relation between two different civs if it exists.</summary>
        public static Relation RelationOf(WorldState state, int a, int b)
        {
            if (state == null || a == b || a < 0 || b < 0) return null;
            return state.Diplomacy.TryGet(a, b, out var r) ? r : null;
        }

        /// <summary>Applies an opinion impulse (clamped, NaN-safe). Returns the applied delta.</summary>
        public static float AddOpinion(Relation r, float delta)
        {
            if (r == null) return 0f;
            float before = MathUtil.Clamp(MathUtil.Sanitize(r.Opinion), EnvoyTuning.MinOpinion, EnvoyTuning.MaxOpinion);
            float after = MathUtil.Clamp(MathUtil.Sanitize(before + MathUtil.Sanitize(delta)), EnvoyTuning.MinOpinion, EnvoyTuning.MaxOpinion);
            r.Opinion = after;
            return after - before;
        }

        /// <summary>Opinion sanitized (0 if no relation).</summary>
        public static float Opinion(Relation r) => r == null ? 0f : MathUtil.Sanitize(r.Opinion);

        /// <summary>True when the pair is Friendly or Allied and not at war.</summary>
        public static bool IsFriendly(Relation r) => r != null && !r.AtWar && r.Stance >= DiplomaticStance.Friendly;

        /// <summary>Dynamic personality trait (0..1).</summary>
        public static float Trait(GameDatabase db, Civilization civ, AITrait trait) => MathUtil.Clamp01(MathUtil.Sanitize(AdvisorBoard.Trait(civ, db, trait), 0.5f));

        /// <summary>Military strength, never negative or NaN.</summary>
        public static float Strength(Civilization c) => c == null ? 0f : Math.Max(0f, MathUtil.Sanitize(c.MilitaryPower));

        /// <summary>How many times stronger <paramref name="a"/> is than <paramref name="b"/> (finite).</summary>
        public static float StrengthRatio(Civilization a, Civilization b)
        {
            float sa = Strength(a), sb = Strength(b);
            if (sb < 1f) sb = 1f;
            return MathUtil.Clamp(MathUtil.Sanitize(sa / sb), 0f, 1000f);
        }

        /// <summary>The registered race definition or null.</summary>
        public static RaceDefinition Race(GameDatabase db, RaceId id)
        {
            if (db == null) return null;
            int i = (int)id;
            return i >= 0 && i < db.Races.Count ? db.Races[i] : null;
        }

        /// <summary>Average of both races' affinity toward each other (-1..1).</summary>
        public static float MutualAffinity(GameDatabase db, RaceId a, RaceId b)
        {
            var ra = Race(db, a);
            var rb = Race(db, b);
            return MathUtil.Clamp(0.5f * (Affinity(ra, b) + Affinity(rb, a)), -1f, 1f);
        }

        private static float Affinity(RaceDefinition r, RaceId toward)
        {
            if (r == null || r.RaceAffinity == null) return 0f;
            int i = (int)toward;
            return i >= 0 && i < r.RaceAffinity.Length ? MathUtil.Clamp(MathUtil.Sanitize(r.RaceAffinity[i]), -1f, 1f) : 0f;
        }

        /// <summary>Proud races (Orcs, Dwarves) refuse tribute unless desperate.</summary>
        public static bool IsProudRace(RaceId race) => race == RaceId.Orc || race == RaceId.Dwarf;

        /// <summary>"Dwarves" (falls back to the civ name).</summary>
        public static string Plural(GameDatabase db, Civilization c)
        {
            var r = c != null ? Race(db, c.Race) : null;
            if (r != null && !string.IsNullOrEmpty(r.PluralName)) return r.PluralName;
            return c != null ? c.Name : "strangers";
        }

        /// <summary>"Dwarven" (falls back to the race enum name).</summary>
        public static string Adjective(GameDatabase db, Civilization c)
        {
            var r = c != null ? Race(db, c.Race) : null;
            if (r != null && !string.IsNullOrEmpty(r.Adjective)) return r.Adjective;
            return c != null ? c.Race.ToString() : "foreign";
        }

        /// <summary>"Dwarves of Karrak": the race plural and the capital's name.</summary>
        public static string PeopleOf(WorldState state, GameDatabase db, Civilization c)
        {
            var cap = Capital(state, c);
            return cap != null && !string.IsNullOrEmpty(cap.Name) ? Plural(db, c) + " of " + cap.Name : Plural(db, c);
        }

        /// <summary>The living leader's name, or a descriptive fallback.</summary>
        public static string LeaderName(WorldState state, GameDatabase db, Civilization c)
        {
            if (state != null && c != null && c.LeaderId >= 0)
            {
                var p = state.People.Get(c.LeaderId);
                if (p != null && p.IsAlive && !string.IsNullOrEmpty(p.Name)) return p.Name;
            }
            return "the ruler of the " + Plural(db, c);
        }

        /// <summary>Moves up to <paramref name="amount"/> of a resource between stockpiles without destroying any. Returns the amount moved.</summary>
        public static float Transfer(Settlement from, Settlement to, ResourceType type, float amount)
        {
            if (from == null || to == null || from == to) return 0f;
            amount = MathUtil.Sanitize(amount);
            amount = Math.Min(amount, to.Stockpile.FreeSpace(type));
            amount = Math.Min(amount, from.Stockpile.Get(type));
            if (!(amount > 0f)) return 0f;
            float taken = from.Stockpile.ConsumeUpTo(type, amount);
            float stored = to.Stockpile.Add(type, taken);
            if (taken - stored > 1e-4f) from.Stockpile.Add(type, taken - stored); // refund what did not fit
            return stored;
        }

        /// <summary>Reads a tick stored as float in a blackboard; -1 when missing or invalid.</summary>
        public static long ReadTick(Dictionary<string, float> mem, string key)
        {
            if (mem == null || key == null || !mem.TryGetValue(key, out float v)) return -1;
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f) return -1;
            return (long)v;
        }

        /// <summary>True when the civ's cooldown key is missing or older than <paramref name="years"/>.</summary>
        public static bool CooldownReady(Civilization civ, string key, float years, long tick)
        {
            long last = ReadTick(civ != null ? civ.Memory : null, key);
            return last < 0 || tick - last >= SimTime.YearsToTicks(years) || last > tick;
        }

        /// <summary>Marks a cooldown key with the current tick.</summary>
        public static void MarkCooldown(Civilization civ, string key, long tick)
        {
            if (civ != null) civ.Memory[key] = tick;
        }

        /// <summary>Writes a chronicle line (no-op without ops) and mirrors it to the capital's AI log.</summary>
        public static void Chronicle(SimContext ctx, Civilization civ, string text, int importance)
        {
            if (ctx == null || string.IsNullOrEmpty(text)) return;
            var cap = Capital(ctx.State, civ);
            if (ctx.Ops != null)
                ctx.Ops.AddChronicle(ChronicleCategory.Diplomacy, text, civ != null ? civ.Id : -1, cap != null ? cap.Id : -1,
                    cap != null ? cap.Center : Vec2i.Invalid, importance);
            if (cap != null) cap.LogAi("Envoys: " + text);
        }

        /// <summary>Writes a debug line to the capital's AI log only.</summary>
        public static void Log(WorldState state, Civilization civ, string text)
        {
            var cap = Capital(state, civ);
            if (cap != null) cap.LogAi(text);
        }
    }
}
