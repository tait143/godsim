using System;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Civ-level plan for reaching the next age: research gap, missing techs, smoothed research rate, years to the
    /// next age versus a curiosity-scaled pacing target, unmet population/settlement requirements, and the best
    /// settlements to host new research. Pooled and recomputed once per advisor run per civilization; all fields are
    /// sanitized (no NaN/Infinity).
    /// </summary>
    public sealed class SciencePacePlan
    {
        /// <summary>Band values (see <see cref="Band"/>).</summary>
        public const int BandAhead = -1, BandOnPace = 0, BandBehind = 1, BandFinalAge = 2;
        /// <summary><see cref="Behind"/> above this counts as behind pace.</summary>
        public const float BehindThreshold = 0.05f;
        /// <summary><see cref="Behind"/> below this counts as far ahead of pace.</summary>
        public const float AheadThreshold = -0.3f;

        public int CivId = -1;
        public long Tick = -1;
        public bool HasNextAge;
        public AgeId NextAge;
        public string NextAgeLabel = "";

        /// <summary>Research still needed for the next age's threshold.</summary>
        public float ResearchGap;
        /// <summary>Current-age techs still required by the next age.</summary>
        public int TechsMissing;
        /// <summary>Estimated research points to learn the missing techs.</summary>
        public float TechCostLeft;
        /// <summary>max(ResearchGap, TechCostLeft): the research that still has to happen.</summary>
        public float EffectiveGap;
        /// <summary>Unsmoothed estimate: sum of ServiceOutput[Research] + 0.004 * population (per tick).</summary>
        public float InstantRate;
        /// <summary>Smoothed research rate (EMA, per tick).</summary>
        public float Rate;
        /// <summary>True when the instant rate collapsed below 60% of the EMA (disaster/loss).</summary>
        public bool Recovering;

        public float YearsToNext;
        /// <summary>Desired years per age (100 scaled by curiosity and research focus).</summary>
        public float TargetYears;
        /// <summary>Years left in the pacing budget for this age (never below 20% of the target).</summary>
        public float BudgetYears;
        /// <summary>YearsToNext / BudgetYears (&gt;1 = behind).</summary>
        public float Pressure;
        /// <summary>Signed pace signal in [-1, 1]: positive = behind pace (push research), negative = ahead (ease off).</summary>
        public float Behind;
        public int Band;

        public int Population;
        public int SettlementCount;
        public int PopulationNeeded;
        public int SettlementsNeeded;
        public float Curiosity = 0.5f;
        public bool ResearchFocus;
        public bool AtWar;

        /// <summary>Best settlement to host research (larger tier, spare labor/housing, no crisis); -1 if none.</summary>
        public int BestSettlementId = -1;
        public int SecondSettlementId = -1;
        /// <summary>Cheapest researchable current-age tech when age techs are missing; -1 otherwise.</summary>
        public int PreferredTechId = -1;

        private string _reason;
        private readonly StringBuilder _sb = new StringBuilder(160);

        /// <summary>Recomputes the plan. When <paramref name="writeMemory"/> is true the EMA and summary keys are stored in civ memory.</summary>
        public void Compute(SimContext ctx, Civilization civ, bool writeMemory)
        {
            Reset();
            if (ctx == null || ctx.State == null || ctx.Db == null || civ == null) return;
            var db = ctx.Db;
            var state = ctx.State;
            CivId = civ.Id;
            Tick = ctx.Tick;

            // ---- settlements: population, research output, best research sites
            float output = 0f;
            float bestScore = 0f, secondScore = 0f;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s == null || !s.IsAlive || s.CivilizationId != civ.Id) continue;
                SettlementCount++;
                Population += s.Population;
                output += Math.Max(0f, MathUtil.Sanitize(s.ServiceOutput[(int)ServiceType.Research]));
                if (s.Population <= 0) continue;
                float sc = SiteScore(s, civ);
                if (BestSettlementId < 0 || Better(sc, s.Id, bestScore, BestSettlementId))
                {
                    SecondSettlementId = BestSettlementId; secondScore = bestScore;
                    BestSettlementId = s.Id; bestScore = sc;
                }
                else if (SecondSettlementId < 0 || Better(sc, s.Id, secondScore, SecondSettlementId))
                {
                    SecondSettlementId = s.Id; secondScore = sc;
                }
            }

            // ---- research rate (EMA; snaps down faster after a disaster)
            InstantRate = MathUtil.Clamp(MathUtil.Sanitize(output + ScienceKeys.ResearchPerPersonPerTick * Population), 0f, 1e6f);
            if (civ.Memory.TryGetValue(ScienceKeys.Rate, out float prev) && !float.IsNaN(prev) && !float.IsInfinity(prev) && prev >= 0f)
            {
                Recovering = prev > 0.05f && InstantRate < prev * 0.6f;
                float alpha = Recovering ? ScienceKeys.RateAlphaFast : ScienceKeys.RateAlpha;
                Rate = prev + (InstantRate - prev) * alpha;
            }
            else Rate = InstantRate;
            Rate = MathUtil.Clamp(MathUtil.Sanitize(Rate), 0f, 1e6f);

            // ---- personality & posture
            Curiosity = AdvisorBoard.Trait(civ, db, AITrait.Curiosity);
            ResearchFocus = MathUtil.RoundToInt(MathUtil.Sanitize(civ.Mem(ScienceKeys.Focus, -1f), -1f)) == ScienceKeys.FocusResearch;
            AtWar = MathUtil.Sanitize(civ.Mem(ScienceKeys.ActiveWars)) > 0.5f;

            // ---- next age requirements
            int next = (int)civ.Age + 1;
            AgeDefinition nextDef = next < db.Ages.Count ? db.Ages[next] : null;
            if (nextDef != null)
            {
                HasNextAge = true;
                NextAge = nextDef.Id;
                NextAgeLabel = AgeLabel(nextDef);
                float threshold = Math.Max(0f, MathUtil.Sanitize(nextDef.ResearchThreshold));
                ResearchGap = Math.Max(0f, threshold - Math.Max(0f, MathUtil.Sanitize(civ.TotalResearch)));

                var techIds = db.TechsOfAge(civ.Age);
                int known = 0, unknown = 0;
                float unknownCost = 0f, prefCost = 0f;
                int pref = -1;
                for (int i = 0; i < techIds.Count; i++)
                {
                    var t = db.Tech(techIds[i]);
                    if (t == null) continue;
                    if (civ.KnownTechs.Contains(t.Id)) { known++; continue; }
                    if (!t.AllowedRaces.Allows(civ.Race)) continue;
                    float cost = Math.Max(1f, MathUtil.Sanitize(t.Cost, 100f));
                    unknown++;
                    unknownCost += cost;
                    if (db.CanResearch(civ, t) && (pref < 0 || cost < prefCost - 1e-4f || (Math.Abs(cost - prefCost) <= 1e-4f && t.Id < pref)))
                    {
                        pref = t.Id; prefCost = cost;
                    }
                }
                TechsMissing = Math.Max(0, nextDef.RequiredTechsFromPreviousAge - known);
                float avg = unknown > 0 ? unknownCost / unknown : 100f;
                TechCostLeft = Math.Max(0f, MathUtil.Sanitize(TechsMissing * avg - Math.Max(0f, MathUtil.Sanitize(civ.ResearchPool))));
                EffectiveGap = Math.Max(ResearchGap, TechCostLeft);
                PopulationNeeded = Math.Max(0, nextDef.MinPopulation - Population);
                SettlementsNeeded = Math.Max(0, nextDef.MinSettlements - SettlementCount);
                PreferredTechId = TechsMissing > 0 ? pref : -1;

                // ---- pace
                float perYear = Rate * SimTime.TicksPerYear;
                if (EffectiveGap <= 0f) YearsToNext = 0f;
                else YearsToNext = perYear > 1e-4f ? Math.Min(ScienceKeys.MaxYears, EffectiveGap / perYear) : ScienceKeys.MaxYears;
                TargetYears = ScienceKeys.BaseYearsPerAge * (1.3f - 0.6f * Curiosity) * (ResearchFocus ? 0.85f : 1f);
                float elapsed = Math.Max(0f, (ctx.Tick - civ.AgeEnteredTick) / (float)SimTime.TicksPerYear);
                BudgetYears = Math.Max(TargetYears * 0.2f, TargetYears - elapsed);
                Pressure = MathUtil.Clamp(MathUtil.Sanitize(YearsToNext / Math.Max(1f, BudgetYears)), 0f, 50f);
                Behind = MathUtil.Clamp((Pressure - 0.8f) / 1.2f, -1f, 1f);
                if (Recovering && Behind > AheadThreshold) Behind = Math.Min(1f, Behind + 0.15f);
                Band = Behind > BehindThreshold ? BandBehind : (Behind < AheadThreshold ? BandAhead : BandOnPace);
            }
            else Band = BandFinalAge;

            if (!writeMemory) return;
            var mem = civ.Memory;
            mem[ScienceKeys.Rate] = Rate;
            mem[ScienceKeys.YearsToNextAge] = MathUtil.Sanitize(YearsToNext);
            mem[ScienceKeys.Pressure] = Pressure;
            mem[ScienceKeys.TechsLeft] = TechsMissing;
            // Research-choice nudge: a hint only. Never touches CurrentResearchTechId, and stays silent while it is -1.
            if (civ.CurrentResearchTechId != -1) mem[ScienceKeys.PreferredTech] = PreferredTechId;
        }

        /// <summary>
        /// Short explanation of the plan, e.g. "Iron Age needs 2 more techs, 600 research; ~31y at current pace (target 45y)".
        /// Built once per run (cached).
        /// </summary>
        public string Reason
        {
            get
            {
                if (_reason != null) return _reason;
                var sb = _sb;
                sb.Length = 0;
                if (!HasNextAge) { sb.Append("final age reached"); return _reason = sb.ToString(); }
                sb.Append(NextAgeLabel);
                if (EffectiveGap <= 0f)
                {
                    sb.Append(": research ready");
                    AppendGrowthNeeds(sb, "; waiting on ");
                }
                else
                {
                    sb.Append(" needs ");
                    bool any = false;
                    if (TechsMissing > 0) { sb.Append(TechsMissing).Append(TechsMissing == 1 ? " more tech" : " more techs"); any = true; }
                    if (ResearchGap > 0f) { if (any) sb.Append(", "); sb.Append(MathUtil.RoundToInt(ResearchGap)).Append(" research"); any = true; }
                    AppendGrowthNeeds(sb, any ? ", " : "");
                    sb.Append("; ~");
                    if (YearsToNext >= ScienceKeys.MaxYears) sb.Append(MathUtil.RoundToInt(ScienceKeys.MaxYears)).Append("+");
                    else sb.Append(MathUtil.RoundToInt(YearsToNext));
                    sb.Append("y at current pace (target ").Append(MathUtil.RoundToInt(BudgetYears)).Append("y)");
                }
                if (Recovering) sb.Append(", rebuilding after research losses");
                return _reason = sb.ToString();
            }
        }

        /// <summary>Human-readable band name.</summary>
        public string BandName
        {
            get
            {
                switch (Band)
                {
                    case BandAhead: return "ahead of pace";
                    case BandBehind: return "behind pace";
                    case BandFinalAge: return "final age";
                    default: return "on pace";
                }
            }
        }

        private void AppendGrowthNeeds(StringBuilder sb, string lead)
        {
            if (PopulationNeeded <= 0 && SettlementsNeeded <= 0) return;
            sb.Append(lead);
            if (PopulationNeeded > 0) sb.Append(PopulationNeeded).Append(" more people");
            if (SettlementsNeeded > 0)
            {
                if (PopulationNeeded > 0) sb.Append(", ");
                sb.Append(SettlementsNeeded).Append(SettlementsNeeded == 1 ? " more settlement" : " more settlements");
            }
        }

        /// <summary>How well a settlement can host new research: larger tier, more people, spare labor and housing, capital, no crisis.</summary>
        public static float SiteScore(Settlement s, Civilization civ)
        {
            if (s == null) return 0f;
            float score = (int)s.Tier
                + Math.Min(s.Population, 300) / 60f
                + Math.Min(Math.Max(s.Unemployed, 0), 12) * 0.12f
                + MathUtil.Clamp(MathUtil.Sanitize(s.Mem(ScienceKeys.FreeHousing)), 0f, 10f) * 0.06f
                + (s.IsCapital ? 0.6f : 0f)
                - 2.5f * LocalCrisis(s, civ);
            return MathUtil.Sanitize(score);
        }

        /// <summary>Crisis level 0..1 from other advisors' food/defense urgency (and a mild floor while at war).</summary>
        public static float LocalCrisis(Settlement s, Civilization civ)
        {
            if (s == null) return 0f;
            float c = Math.Max(AdvisorBoard.Urgency(s, ScienceKeys.FoodAdvisorId), AdvisorBoard.Urgency(s, ScienceKeys.DefenseAdvisorId));
            if (civ != null && MathUtil.Sanitize(civ.Mem(ScienceKeys.ActiveWars)) > 0.5f) c = Math.Max(c, 0.35f);
            return MathUtil.Clamp01(MathUtil.Sanitize(c));
        }

        /// <summary>"Iron" -> "Iron Age"; names already ending in "Age" are kept.</summary>
        public static string AgeLabel(AgeDefinition def)
        {
            if (def == null) return "next age";
            string n = string.IsNullOrEmpty(def.Name) ? def.Id.ToString() : def.Name;
            return n.EndsWith("Age", StringComparison.Ordinal) ? n : n + " Age";
        }

        private static bool Better(float score, int id, float otherScore, int otherId) =>
            score > otherScore + 1e-5f || (Math.Abs(score - otherScore) <= 1e-5f && id < otherId);

        private void Reset()
        {
            CivId = -1; Tick = -1; HasNextAge = false; NextAge = AgeId.Dawn; NextAgeLabel = "";
            ResearchGap = TechCostLeft = EffectiveGap = InstantRate = Rate = 0f;
            TechsMissing = 0; Recovering = false;
            YearsToNext = TargetYears = BudgetYears = Pressure = Behind = 0f;
            Band = BandOnPace;
            Population = SettlementCount = PopulationNeeded = SettlementsNeeded = 0;
            Curiosity = 0.5f; ResearchFocus = AtWar = false;
            BestSettlementId = SecondSettlementId = PreferredTechId = -1;
            _reason = null;
        }
    }
}
