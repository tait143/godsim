using System;

namespace GodSim.Core.Population
{
    /// <summary>
    /// Keeps every civilization led. When <see cref="Civilization.LeaderId"/> is invalid, dead or no longer
    /// belongs to the civilization, the best adult of the capital (falling back to the civ's most populous
    /// settlement) takes over: Wise, Charismatic and Hero traits, mature age, skill and health score highest,
    /// with a small dynastic bonus for children of the previous leader. Writes the chronicle
    /// ("Name rose to lead the X.", importance 1).
    /// </summary>
    public sealed class LeadershipSystem : ISimSystem
    {
        /// <summary>Run interval in ticks.</summary>
        public const int Interval = 48;

        /// <summary>Creates the system.</summary>
        public LeadershipSystem() { }

        /// <inheritdoc />
        public string Name => "Population.Leadership";

        /// <inheritdoc />
        public int TickInterval => Interval;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                UpdateCivilization(ctx, civ);
            }
        }

        /// <summary>True if the person is a living adult member of the civilization.</summary>
        public static bool IsValidLeader(WorldState state, Civilization civ, Person p) =>
            p != null && p.IsAlive && p.IsAdult && p.CivilizationId == civ.Id && state.Settlements.Get(p.SettlementId) != null;

        /// <summary>Ensures the civilization has a valid leader; returns the leader (or null when nobody qualifies).</summary>
        public Person UpdateCivilization(SimContext ctx, Civilization civ)
        {
            var state = ctx.State;
            var current = civ.LeaderId >= 0 ? state.People.Get(civ.LeaderId) : null;
            if (IsValidLeader(state, civ, current))
            {
                civ.Memory[SocietyKeys.LeaderId] = current.Id;
                return current;
            }

            int previousId = civ.LeaderId >= 0 ? civ.LeaderId : (int)MathUtil.Sanitize(civ.Mem(SocietyKeys.LeaderId, -1f), -1f);
            civ.LeaderId = -1;
            Settlement seat = state.Settlements.Get(civ.CapitalId);
            Person best = null;
            if (seat != null && seat.IsAlive && seat.CivilizationId == civ.Id) best = BestIn(ctx, seat, civ, previousId);
            if (best == null)
            {
                seat = null;
                // Fallback: the most populous settlement with an eligible adult.
                Settlement fallback = null;
                var ids = civ.SettlementIds;
                for (int i = 0; i < ids.Count; i++)
                {
                    var s = state.Settlements.Get(ids[i]);
                    if (s == null || !s.IsAlive || s.CivilizationId != civ.Id || s.Id == civ.CapitalId) continue;
                    if (fallback == null || s.Population > fallback.Population || (s.Population == fallback.Population && s.Id < fallback.Id)) fallback = s;
                }
                if (fallback != null) { best = BestIn(ctx, fallback, civ, previousId); seat = fallback; }
            }
            if (best == null) return null;

            civ.LeaderId = best.Id;
            civ.Memory[SocietyKeys.LeaderId] = best.Id;
            civ.Memory[SocietyKeys.LeaderSinceTick] = ctx.Tick;
            civ.Memory[SocietyKeys.LeaderCount] = civ.Mem(SocietyKeys.LeaderCount) + 1f;
            ctx.Ops.AddChronicle(ChronicleCategory.Civilization, best.Name + " rose to lead the " + civ.Name + ".",
                civ.Id, seat != null ? seat.Id : -1, seat != null ? seat.Center : best.Position, 1);
            return best;
        }

        /// <summary>Leadership score of a candidate (higher is better).</summary>
        public static float Score(Person p, RaceDefinition race, int previousLeaderId)
        {
            float score = 0f;
            if (p.HasTrait(PersonTraits.Wise)) score += 3f;
            if (p.HasTrait(PersonTraits.Charismatic)) score += 2.5f;
            if (p.HasTrait(PersonTraits.Hero)) score += 4f;
            if (p.HasTrait(PersonTraits.Clever)) score += 1f;
            if (p.HasTrait(PersonTraits.Brave)) score += 0.5f;
            if (p.HasTrait(PersonTraits.Lazy)) score -= 1.5f;
            if (p.HasTrait(PersonTraits.Greedy)) score -= 0.5f;
            if (p.HasTrait(PersonTraits.Sickly)) score -= 0.5f;

            float adulthood = race != null ? MathUtil.Sanitize(race.AdulthoodAge, 16f) : 16f;
            float elder = race != null ? MathUtil.Sanitize(race.ElderAge, 55f) : 55f;
            float life = race != null ? MathUtil.Sanitize(race.LifeExpectancy, 70f) : 70f;
            float age = MathUtil.Sanitize(p.AgeYears);
            // Maturity grows through adulthood; the very old (near life expectancy) are a risky choice.
            score += 1.5f * MathUtil.InverseLerp(adulthood, Math.Max(adulthood + 1f, elder), age);
            if (age > life * 0.9f) score -= 1f;
            score += 2f * (MathUtil.Clamp(MathUtil.Sanitize(p.Skill, 1f), 0f, 3f) - 1f);
            score += 0.5f * SocietyUtil.Unit(p.Health, 1f);
            if (previousLeaderId >= 0 && (p.MotherId == previousLeaderId || p.FatherId == previousLeaderId)) score += 1f;
            return score;
        }

        private static Person BestIn(SimContext ctx, Settlement s, Civilization civ, int previousLeaderId)
        {
            var state = ctx.State;
            var residents = s.ResidentIds;
            Person best = null;
            float bestScore = 0f;
            for (int i = 0; i < residents.Count; i++)
            {
                var p = state.People.Get(residents[i]);
                if (!IsValidLeader(state, civ, p) || p.SettlementId != s.Id) continue;
                float sc = Score(p, SocietyUtil.RaceOf(ctx.Db, p.Race), previousLeaderId);
                if (best == null || sc > bestScore || (sc == bestScore && p.Id < best.Id)) { best = p; bestScore = sc; }
            }
            return best;
        }
    }
}
