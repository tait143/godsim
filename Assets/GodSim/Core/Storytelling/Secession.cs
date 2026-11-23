using System;

namespace GodSim.Core.Storytelling
{
    /// <summary>
    /// Rebellion &amp; secession rules. A restless, distant (or ethnically different) Village+ settlement may break
    /// away and found its own civilization, keeping its knowledge. Never the capital, never the last settlement.
    /// </summary>
    public static class Secession
    {
        public const float UnrestThreshold = 0.6f;
        public const float HappinessThreshold = 0.3f;
        /// <summary>Distance (tiles) from the capital beyond which a settlement counts as remote.</summary>
        public const float MinDistanceFromCapital = 40f;
        public const int MinResidents = 3;
        /// <summary>Safety cap: no new states once this many civilizations are alive.</summary>
        public const int MaxAliveCivilizations = 48;
        public const float RebelOpinion = -50f;
        /// <summary>Civ.Memory key on the new state: id of the civilization it broke away from.</summary>
        public const string ParentCivKey = "story.parentCiv";
        /// <summary>Civ.Memory key on the new state: tick it was proclaimed.</summary>
        public const string SecededTickKey = "story.secededTick";

        private static readonly string[] StateNamePatterns =
        {
            "Free State of {0}", "{0} Commune", "Republic of {0}", "{0} Confederacy", "Free Cantons of {0}"
        };

        /// <summary>True if the settlement may secede from civ right now (ignores the 40-year cooldown, which the event enforces).</summary>
        public static bool IsEligible(WorldState st, Civilization civ, Settlement s)
        {
            if (st == null || civ == null || s == null || !civ.IsAlive || !s.IsAlive) return false;
            if (s.CivilizationId != civ.Id || s.IsCapital || s.Id == civ.CapitalId) return false;
            if (s.Tier < SettlementTier.Village) return false;
            float unrest = MathUtil.Sanitize(s.Unrest), happiness = MathUtil.Sanitize(s.Happiness, 0.6f);
            if (!(unrest > UnrestThreshold || happiness < HappinessThreshold)) return false;
            if (StoryUtil.AliveSettlementCount(st, civ) < 2) return false; // the parent must keep >= 1 settlement
            if (StoryUtil.AliveResidents(st, s) < MinResidents) return false;
            if (StoryUtil.AliveCivilizations(st) >= MaxAliveCivilizations) return false;
            var cap = StoryUtil.Capital(st, civ);
            float d = MinDistanceFromCapital;
            bool remote = cap == null || cap.Center.DistanceSqTo(s.Center) > d * d;
            return remote || StoryUtil.MajorityRace(st, s) != civ.Race;
        }

        /// <summary>The most restless eligible settlement of the civ, or null.</summary>
        public static Settlement FindCandidate(WorldState st, Civilization civ)
        {
            if (st == null || civ == null) return null;
            Settlement best = null;
            float bestScore = float.MinValue;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = StoryUtil.Live(st, civ, ids[i]);
                if (s == null || !IsEligible(st, civ, s)) continue;
                float score = RestlessnessOf(s);
                if (score > bestScore) { bestScore = score; best = s; }
            }
            return best;
        }

        /// <summary>0..~2: how strongly a settlement wants out.</summary>
        public static float RestlessnessOf(Settlement s) =>
            MathUtil.Clamp01(MathUtil.Sanitize(s.Unrest)) + Math.Max(0f, HappinessThreshold - MathUtil.Clamp01(MathUtil.Sanitize(s.Happiness, 0.6f))) * 2f;

        /// <summary>
        /// Creates the breakaway civilization (majority race of the settlement) through ctx.Ops, copies the parent's
        /// Age, KnownTechs, UnlockedBuildings and lifetime research, transfers the settlement with its people and
        /// buildings, makes it the new capital and sets the mutual relation to Hostile (-50). Returns null if not eligible.
        /// </summary>
        public static Civilization Execute(SimContext ctx, Civilization parent, Settlement s)
        {
            if (ctx == null || ctx.State == null || ctx.Ops == null || !IsEligible(ctx.State, parent, s)) return null;
            var st = ctx.State;
            var rng = ctx.Rng;
            RaceId race = StoryUtil.MajorityRace(st, s);
            if (StoryUtil.RaceDef(ctx.Db, race) == null) race = parent.Race;
            string name = string.Format(StateNamePatterns[rng.Range(0, StateNamePatterns.Length)], s.Name ?? "the Marches");
            if (name.Length > 64) name = name.Substring(0, 64);

            var parentLeader = StoryUtil.AlivePerson(st, parent.LeaderId);
            bool parentLeaderInside = parentLeader != null && parentLeader.SettlementId == s.Id;

            var nc = ctx.Ops.CreateCivilization(race, name);
            nc.Age = parent.Age;
            nc.AgeEnteredTick = parent.AgeEnteredTick;
            foreach (int tech in parent.KnownTechs) nc.KnownTechs.Add(tech); // SortedSet: ascending, deterministic
            Array.Copy(parent.UnlockedBuildings, nc.UnlockedBuildings, Math.Min(parent.UnlockedBuildings.Length, nc.UnlockedBuildings.Length));
            nc.TotalResearch = MathUtil.Clamp(MathUtil.Sanitize(parent.TotalResearch), 0f, 1e12f);
            nc.Devotion = MathUtil.Clamp01(MathUtil.Sanitize(parent.Devotion, 0.5f));
            nc.GodFavor = MathUtil.Clamp(MathUtil.Sanitize(parent.GodFavor), -1f, 1f);
            if (nc.Color == parent.Color) nc.Color = VaryColor(parent.Color, rng);

            ctx.Ops.TransferSettlement(s, nc);
            if (s.CivilizationId != nc.Id) { ctx.Ops.CollapseCivilization(nc, -1); return null; } // defensive: transfer refused

            nc.CapitalId = s.Id;
            s.IsCapital = true;
            s.Race = race;
            if (parentLeaderInside && parent.LeaderId == parentLeader.Id) parent.LeaderId = -1;
            DisbandOrphanArmies(ctx, parent, s);

            var leader = PickRebelLeader(st, s, rng);
            if (leader != null) { nc.LeaderId = leader.Id; leader.Traits |= PersonTraits.Charismatic; }

            s.Unrest = MathUtil.Clamp01(MathUtil.Sanitize(s.Unrest) * 0.25f);
            StoryUtil.ShiftMood(st, s, 0.10f);

            var rel = st.Diplomacy.Get(parent.Id, nc.Id);
            var old = rel.Stance;
            rel.Stance = DiplomaticStance.Hostile;
            rel.Opinion = RebelOpinion;
            rel.HasContact = true;
            rel.LastContactTick = st.Tick;
            if (old != rel.Stance && ctx.Events != null)
                ctx.Events.Publish(new StanceChangedEvent { CivA = rel.CivA, CivB = rel.CivB, OldStance = old, NewStance = rel.Stance });

            nc.Memory[ParentCivKey] = parent.Id;
            nc.Memory[SecededTickKey] = st.Tick;
            return nc;
        }

        private static Person PickRebelLeader(WorldState st, Settlement s, Rng rng)
        {
            Person best = null;
            int bestScore = int.MinValue;
            var ids = s.ResidentIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = StoryUtil.AlivePerson(st, ids[i]);
                if (p == null || !p.IsAdult) continue;
                int score = (p.HasTrait(PersonTraits.Hero) ? 8 : 0) + (p.HasTrait(PersonTraits.Charismatic) ? 4 : 0) +
                            (p.HasTrait(PersonTraits.Brave) ? 2 : 0) + (p.HasTrait(PersonTraits.Wise) ? 1 : 0);
                if (score > bestScore) { bestScore = score; best = p; }
            }
            return best;
        }

        private static void DisbandOrphanArmies(SimContext ctx, Civilization parent, Settlement s)
        {
            var armies = ctx.State.Diplomacy.Armies.Items;
            for (int i = armies.Count - 1; i >= 0; i--)
            {
                var a = armies[i];
                if (a.IsAlive && a.CivilizationId == parent.Id && a.HomeSettlementId == s.Id && a.SoldierIds.Count == 0) ctx.Ops.DisbandArmy(a);
            }
        }

        private static uint VaryColor(uint rgba, Rng rng)
        {
            int r = (int)((rgba >> 24) & 0xFF), g = (int)((rgba >> 16) & 0xFF), b = (int)((rgba >> 8) & 0xFF);
            r = MathUtil.Clamp(r + rng.Range(-80, 81), 0, 255);
            g = MathUtil.Clamp(g + rng.Range(-80, 81), 0, 255);
            b = MathUtil.Clamp(b + rng.Range(-80, 81), 0, 255);
            uint c = MathUtil.Rgba((byte)r, (byte)g, (byte)b, 255);
            return c == rgba ? MathUtil.Rgba((byte)(255 - r), (byte)g, (byte)b, 255) : c;
        }
    }
}
