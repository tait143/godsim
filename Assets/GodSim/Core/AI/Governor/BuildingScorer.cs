using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Scores candidate buildings against a settlement's needs:
    /// <c>utility = (sum(need_k * contribution_k) * raceBias * modernity + 0.35 * councilBias) * labor * repeat * saturation * inputs * affordability - redundancy</c>
    /// (councilBias = <see cref="AdvisorBoard.BuildBias"/>; affordability counts only stock not promised to higher-priority orders).
    /// Contributions are data-driven from <see cref="BuildingProfile"/> and normalized per need against the
    /// best candidate currently available (so the best farm answers the food need with ~1.0 whatever the
    /// catalog numbers are); housing uses expected residents / 4. Call <see cref="Prepare"/> with the
    /// candidate list before scoring. Allocation-free.
    /// </summary>
    public sealed class BuildingScorer
    {
        private readonly float[] _maxRaw = new float[SettlementNeeds.Count];
        private readonly float[] _reservedAhead = new float[Counts.Resources];
        private static readonly GovernorPersonality DefaultPersonality = new GovernorPersonality();
        private static readonly GovernorSettings DefaultSettings = new GovernorSettings().Sanitize();

        /// <summary>Settings (dwelling capacity reference, repeat cooldown).</summary>
        public GovernorSettings Settings { get; set; }

        /// <summary>Creates a scorer with the given (or default) settings.</summary>
        public BuildingScorer(GovernorSettings settings = null) { Settings = settings ?? new GovernorSettings().Sanitize(); }

        /// <summary>Computes the per-need normalization maxima over the candidates.</summary>
        public void Prepare(List<BuildingProfile> candidates)
        {
            Array.Clear(_maxRaw, 0, _maxRaw.Length);
            if (candidates == null) return;
            for (int c = 0; c < candidates.Count; c++)
            {
                var p = candidates[c];
                if (p == null) continue;
                for (int k = 0; k < _maxRaw.Length; k++)
                {
                    float v = p.Raw(k);
                    if (v > _maxRaw[k]) _maxRaw[k] = v;
                }
            }
        }

        /// <summary>Normalized contribution 0..1 (housing up to 1.25) of a building to a need slot.</summary>
        public float Contribution(BuildingProfile p, int slot)
        {
            if (p == null) return 0f;
            if (slot == SettlementNeeds.Housing)
            {
                if (p.ExpectedHousing <= 0f) return 0f;
                float reference = Settings != null ? Settings.DefaultDwellingCapacity : 4f;
                return MathUtil.Clamp(p.ExpectedHousing / reference, 0f, 1.25f);
            }
            if (slot < 0 || slot >= _maxRaw.Length) return 0f;
            float raw = p.Raw(slot);
            float max = _maxRaw[slot];
            if (raw <= 0f || max <= 0f) return 0f;
            return MathUtil.Clamp01(raw / max);
        }

        /// <summary>Sum of need * contribution, and the slot contributing most.</summary>
        public float BaseUtility(BuildingProfile p, SettlementNeeds needs, out int topSlot)
        {
            return BaseUtility(p, needs, out topSlot, out _);
        }

        /// <summary>Sum of need * contribution, the slot contributing most, and the highest need this building serves.</summary>
        public float BaseUtility(BuildingProfile p, SettlementNeeds needs, out int topSlot, out float maxServedNeed)
        {
            topSlot = -1;
            maxServedNeed = 0f;
            if (p == null || needs == null) return 0f;
            float sum = 0f, best = 0f;
            var values = needs.Values;
            for (int k = 0; k < values.Length; k++)
            {
                float n = values[k];
                if (n <= 0f) continue;
                float term = n * Contribution(p, k);
                if (term <= 0f) continue;
                sum += term;
                if (n > maxServedNeed) maxServedNeed = n;
                if (term > best) { best = term; topSlot = k; }
            }
            return MathUtil.Sanitize(sum);
        }

        /// <summary>Score without advisor/personality context (neutral traits, no council bias).</summary>
        public float Score(BuildingProfile p, SettlementNeeds needs, SettlementSurvey survey, Settlement s, Civilization civ,
                           RaceDefinition race, float growthPerYear, long tick, out int topSlot)
            => Score(p, needs, survey, s, civ, race, null, growthPerYear, tick, 0f, out topSlot);

        /// <summary>
        /// Full utility of ordering <paramref name="p"/> now (&gt;= 0):
        /// <c>(needUtility * raceBias * modernity + AdvisorBiasScale * councilBias) * labor * repeat * saturation * inputs * affordability - redundancy</c>.
        /// A strongly positive council bias (<see cref="AdvisorBoard.BuildBias"/>) can carry a building the governor's own
        /// needs would not; a bias &lt;= <see cref="GovernorSettings.AdvisorVetoBias"/> vetoes it unless a need it serves is
        /// critical (then negative advice is ignored). <paramref name="topSlot"/> is the need that drove the score, -1 when
        /// only the council did. <paramref name="currentAgeBuildings"/> counts the town's non-dwelling buildings of the
        /// civ's current age (0 = the age has not been adopted yet: stronger modernity pull).
        /// </summary>
        public float Score(BuildingProfile p, SettlementNeeds needs, SettlementSurvey survey, Settlement s, Civilization civ,
                           RaceDefinition race, GovernorPersonality personality, float growthPerYear, long tick, float currentAgeBuildings, out int topSlot)
        {
            topSlot = -1;
            if (p == null || survey == null || s == null) return 0f;
            float need = BaseUtility(p, needs, out topSlot, out float served);
            var def = p.Def;
            var cfg = Settings ?? DefaultSettings;
            float bias = MathUtil.Clamp(MathUtil.Sanitize(AdvisorBoard.BuildBias(s, p.Type)), -AdvisorBoard.MaxTotal, AdvisorBoard.MaxTotal);
            bool critical = served >= cfg.CriticalNeed;
            if (bias <= cfg.AdvisorVetoBias && !critical) return 0f;     // council veto
            if (bias < 0f && critical) bias = 0f;                         // own critical need overrides advice
            float u = need * RaceBias(def, race, personality) * ModernityFactor(def, civ, currentAgeBuildings) + cfg.AdvisorBiasScale * bias;
            if (u <= 0f) return 0f;
            u *= LaborFactor(p, needs, survey, s, growthPerYear);
            u *= RepeatFactor(p, s, tick);
            u *= SaturationFactor(p, survey);
            u *= InputFactor(p, s, survey);
            // Stock already promised to orders the planner serves first (higher priority) is not spendable.
            survey.FillReservedAhead(s, u * 10f, _reservedAhead);
            u *= Affordability(def.Cost, s.Stockpile, _reservedAhead);
            u -= RedundancyPenalty(p, survey);
            return u > 0f ? MathUtil.Sanitize(u) : 0f;
        }

        // ------------------------------------------------------------------ factors (public for tests/tools)
        /// <summary>
        /// Cultural preference: race FavoredBuildings x1.3, racial uniques x1.15, and the civ's current traits by
        /// category (Piety -> Religion, Curiosity -> Education, Aggression -> Military, Mercantilism -> Commerce,
        /// Industriousness -> Industry/Resource; 0.85..1.15). Traits come from <paramref name="personality"/>
        /// (<see cref="AdvisorBoard.Trait"/>), null = neutral.
        /// </summary>
        public static float RaceBias(BuildingDefinition def, RaceDefinition race, GovernorPersonality personality)
        {
            if (def == null) return 1f;
            personality = personality ?? DefaultPersonality;
            float f = 1f;
            if (race != null)
            {
                if (Contains(race.FavoredBuildings, def.Type)) f *= 1.3f;
                else if (Contains(race.UniqueBuildings, def.Type)) f *= 1.15f;
            }
            switch (def.Category)
            {
                case BuildingCategory.Religion: f *= 0.85f + 0.3f * MathUtil.Clamp01(personality.Piety); break;
                case BuildingCategory.Education: f *= 0.85f + 0.3f * MathUtil.Clamp01(personality.Curiosity); break;
                case BuildingCategory.Military: f *= 0.85f + 0.3f * MathUtil.Clamp01(personality.Aggression); break;
                case BuildingCategory.Commerce: f *= 0.85f + 0.3f * MathUtil.Clamp01(personality.Mercantilism); break;
                case BuildingCategory.Industry:
                case BuildingCategory.Resource: f *= 0.9f + 0.2f * MathUtil.Clamp01(personality.Industriousness); break;
            }
            return f;
        }

        /// <summary>
        /// Job buildings need workers: 1.0 with enough unemployed (or growth), 0.75 with a few, 0.2 with none —
        /// except food producers while food is critical (1.0) and soldier buildings at war (&gt;= 0.85).
        /// </summary>
        public static float LaborFactor(BuildingProfile p, SettlementNeeds needs, SettlementSurvey survey, Settlement s, float growthPerYear)
        {
            if (p == null || !p.Def.HasJobs || survey == null) return 1f;
            float growthBuffer = Math.Max(0f, MathUtil.Sanitize(growthPerYear)) * 0.15f;
            float available = survey.Unemployed(s) + growthBuffer - survey.OpenJobSlots - survey.PendingJobSlots;
            float f;
            if (available >= p.Def.JobSlots * 0.5f) f = 1f;
            else if (available > 0f) f = 0.75f;
            // Balance pass 2026-11-19: was 0.3, which still let early villages reach 3-6x more job slots than workers.
            // (0.12 / 0.05 tiers were tried and made race building mixes converge: AIScenarios RaceDistinctness.)
            else f = 0.2f;
            // Idle hands: many unemployed and no open slots -> new workplaces are worth more (employment).
            int idle = survey.Unemployed(s);
            if (f >= 1f && available >= p.Def.JobSlots && idle >= Math.Max(3, (int)(0.15f * survey.Adults))) f = 1.15f;
            if (needs != null)
            {
                if (p.ProducesFood && needs.Food >= 0.75f) f = 1f;
                if (p.SoldierSlots > 0 && needs.AtWar) f = Math.Max(f, 0.85f);
            }
            return f;
        }

        /// <summary>1.0 if affordable now (after reservations), 0.6 if the shortfall is covered by about a year of net income, else 0.25.</summary>
        public static float Affordability(ResourceAmount[] cost, Stockpile stock, float[] reserved)
        {
            if (cost == null || cost.Length == 0 || stock == null) return 1f;
            bool now = true;
            float worst = 0f;
            for (int i = 0; i < cost.Length; i++)
            {
                int r = (int)cost[i].Type;
                if (r < 0 || r >= Counts.Resources) continue;
                float need = MathUtil.Sanitize(cost[i].Amount);
                if (need <= 0f) continue;
                float have = stock[cost[i].Type] - (reserved != null && r < reserved.Length ? reserved[r] : 0f);
                if (have < 0f) have = 0f;
                if (have + 1e-4f >= need) continue;
                now = false;
                float net = stock.Net(cost[i].Type);
                if (!(net > 1e-4f)) return 0.25f;
                float ticks = (need - have) / net;
                if (ticks > worst) worst = ticks;
            }
            if (now) return 1f;
            return worst <= SimTime.TicksPerYear ? 0.6f : 0.25f;
        }

        /// <summary>
        /// Age modernity: buildings of the civ's current age (or unlocked early) x1.12 — x1.25 while the town has
        /// none yet (<paramref name="currentAgeBuildings"/> = 0), so new ages are adopted promptly; x0.92 when the
        /// building becomes obsolete after this age, and x0.92 for non-dwellings three or more ages old.
        /// </summary>
        public static float ModernityFactor(BuildingDefinition def, Civilization civ, float currentAgeBuildings = 1f)
        {
            if (def == null || civ == null) return 1f;
            float f = 1f;
            int t = (int)def.Type;
            bool unlockedEarly = def.MinAge > civ.Age && t >= 0 && t < civ.UnlockedBuildings.Length && civ.UnlockedBuildings[t];
            if (def.MinAge == civ.Age || unlockedEarly) f *= currentAgeBuildings <= 0f && civ.Age != AgeId.Dawn ? 1.25f : 1.12f;
            if (def.MaxAge == civ.Age && civ.Age != AgeId.Ascension) f *= 0.92f;
            if (!def.IsDwelling && (int)civ.Age - (int)def.MinAge >= 3) f *= 0.92f;
            return f;
        }

        /// <summary>x0.7 for a non-dwelling type the governor ordered within the repeat cooldown.</summary>
        public float RepeatFactor(BuildingProfile p, Settlement s, long tick)
        {
            if (p == null || s == null || p.Def.IsDwelling) return 1f;
            int cooldown = Settings != null ? Settings.RepeatCooldownTicks : SimTime.TicksPerYear / 2;
            if (cooldown <= 0) return 1f;
            if (!s.Memory.TryGetValue(GovernorKeys.LastOrderedByType[(int)p.Type], out float last)) return 1f;
            long age = tick - (long)last;
            return age >= 0 && age < cooldown ? 0.7f : 1f;
        }

        /// <summary>x0.25 when a non-dwelling type already exists (or is queued) more than 2 + population/5 times.</summary>
        public static float SaturationFactor(BuildingProfile p, SettlementSurvey survey)
        {
            if (p == null || survey == null || p.Def.IsDwelling) return 1f;
            int n = survey.CountWithQueue(p.Type);
            return n >= 2 + survey.Population / 5 ? 0.25f : 1f;
        }

        /// <summary>x0.5 for a consumer whose input is short, not flowing and has no producer at all.</summary>
        public static float InputFactor(BuildingProfile p, Settlement s, SettlementSurvey survey)
        {
            if (p == null || !p.HasInputs || s == null || survey == null) return 1f;
            for (int r = 0; r < Counts.Resources; r++)
            {
                float perTick = p.Input[r];
                if (perTick <= 0f) continue;
                if (survey.Producers[r] > 0) continue;
                var t = (ResourceType)r;
                if (s.Stockpile[t] >= perTick * SimTime.TicksPerSeason || s.Stockpile.Net(t) > 1e-4f) continue;
                return 0.5f;
            }
            return 1f;
        }

        /// <summary>Subtracted for the same type already queued or under construction (small for dwellings).</summary>
        public static float RedundancyPenalty(BuildingProfile p, SettlementSurvey survey)
        {
            if (p == null || survey == null) return 0f;
            int t = (int)p.Type;
            bool dwelling = p.Def.IsDwelling;
            return survey.Queued[t] * (dwelling ? 0.05f : 0.25f) + survey.UnderConstruction[t] * (dwelling ? 0.03f : 0.12f);
        }

        private static bool Contains(BuildingType[] list, BuildingType t)
        {
            if (list == null) return false;
            for (int i = 0; i < list.Length; i++) if (list[i] == t) return true;
            return false;
        }
    }
}
