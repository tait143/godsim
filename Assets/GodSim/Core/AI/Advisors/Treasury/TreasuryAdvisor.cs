using System;
using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The Treasury advisor keeps a civilization solvent and rich. Every 24 ticks it reads each settlement's books
    /// (<see cref="TreasuryLedger"/>: gold stock, income, upkeep of complete buildings, the future upkeep of buildings
    /// under construction or queued, the economy's unpaid-upkeep share, commerce coverage) and computes the runway in
    /// years. Then it votes, data-driven from <see cref="BuildingDefinition"/> yields and placement rules:
    /// <list type="bullet">
    /// <item>Low runway / unpaid upkeep / a deficit on the horizon: strong votes for the best feasible gold producers
    /// (Market → Guildhall → Bank, Harbor when coastal, GoldMine near deposits, rail/air later) plus Merchant and
    /// other gold-job staffing, and NEGATIVE votes for upkeep-heavy non-essentials (culture luxuries, extra garrison
    /// buildings in peacetime, prestige halls in severe crises), each with a short reason.</item>
    /// <item>A healthy surplus: mild positive votes for culture/prestige, scaled by Mercantilism.</item>
    /// <item>The richest settlement of each civ is the banking seat (votes the top finance building there).</item>
    /// <item>A wonder order in the capital's queue: fund it, vote gold producers there and postpone luxuries.</item>
    /// </list>
    /// Deterministic (no randomness, index iteration only) and allocation-free except for reason strings on strong votes.
    /// </summary>
    public sealed class TreasuryAdvisor : AdvisorSystemBase
    {
        /// <summary>Stable advisor id.</summary>
        public const string Id = "treasury";
        /// <summary>Stress at/above which non-essential upkeep is cut.</summary>
        public const float AusterityStress = 0.45f;
        /// <summary>Wealth at/above which luxuries are encouraged.</summary>
        public const float ProsperityWealth = 0.4f;
        /// <summary>Score multiplier for coastal gold producers in coastal towns (sea lanes add trade beyond commerce points).</summary>
        public const float CoastalBonus = 1.5f;

        private static readonly float[] s_rankStrength = { 1f, 0.65f, 0.4f };
        private const int TopN = 3;

        private readonly TreasuryBuildingBook _book = new TreasuryBuildingBook();
        private readonly TreasuryLedger _ledger = new TreasuryLedger();
        private readonly float[] _score = new float[Counts.Buildings];
        private readonly int[] _top = new int[TopN];
        private readonly float[] _topScore = new float[TopN];
        private readonly Dictionary<int, int> _richestByCiv = new Dictionary<int, int>();
        private readonly Dictionary<int, float> _richestScore = new Dictionary<int, float>();
        private readonly Dictionary<int, bool> _atWar = new Dictionary<int, bool>();
        private readonly StringBuilder _sb = new StringBuilder(128);
        private WorldState _preparedState;
        private long _preparedTick = long.MinValue;

        // lazily built reasons (one per settlement-run)
        private string _reasonRevenue, _reasonBelt, _reasonGarrison, _reasonPatron, _reasonWonder, _reasonSeat;

        /// <inheritdoc />
        public override string AdvisorId => Id;
        /// <inheritdoc />
        public override int TickInterval => 24;

        /// <summary>The ledger of the most recently advised settlement (debug/UI/tests).</summary>
        public TreasuryLedger LastLedger => _ledger;

        /// <inheritdoc />
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            if (db == null || ctx.State == null || !_book.Bind(db)) return;
            if (!ReferenceEquals(_preparedState, ctx.State) || _preparedTick != ctx.Tick) Prepare(ctx);

            float merc = AdvisorBoard.Trait(civ, db, AITrait.Mercantilism);
            float aggr = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            float weight = AdvisorBoard.Weight(civ, Id);
            bool isCapital = s.IsCapital || civ.CapitalId == s.Id;
            bool atWar = _atWar.ContainsKey(civ.Id);
            bool richest = _richestByCiv.TryGetValue(civ.Id, out int rid) && rid == s.Id;

            var L = _ledger;
            L.Compute(ctx.State, _book, civ, s, isCapital, ctx.DeltaTicks, merc);
            _reasonRevenue = _reasonBelt = _reasonGarrison = _reasonPatron = _reasonWonder = _reasonSeat = null;

            // 1) Revenue: always a gentle investment drive; strong when stressed or funding a wonder.
            float invest = 0.12f + 0.18f * merc;
            float need = Math.Max(Math.Max(L.Stress, L.WonderNeed), invest);
            bool wonderDriven = L.WonderNeed > L.Stress && L.WonderNeed > invest;
            int hub = VoteGoldSources(db, civ, s, L, need, weight, richest, wonderDriven);

            // 2) Staffing: merchants (and every job that already earns gold here).
            float jobNeed = Math.Max(L.Stress, L.WonderNeed);
            if (jobNeed >= 0.3f)
            {
                VoteJob(s, civ, JobType.Merchant, 0.25f + 0.6f * jobNeed);
                for (int j = 0; j < L.GoldJobs.Length; j++)
                    if (L.GoldJobs[j] && (JobType)j != JobType.Merchant) VoteJob(s, civ, (JobType)j, 0.5f * jobNeed);
            }

            // 3) Spending posture.
            if (L.Stress >= AusterityStress) VoteAusterity(db, civ, s, L, aggr, atWar, weight);
            else if (L.WonderNeed >= 0.3f && L.Gold < L.WonderGoldCost) VoteWonderThrift(db, civ, s, L, weight);
            else if (L.Wealth >= ProsperityWealth && L.Stress < 0.1f) VotePatronage(db, civ, s, L, merc, weight);

            // 4) Banking seat: the richest town hosts the top finance house.
            if (richest && hub >= 0 && L.Wealth >= 0.3f && L.Stress < 0.2f && L.Existing[hub] + L.Queued[hub] == 0)
            {
                float v = (0.3f + 0.4f * merc) * L.Wealth;
                VoteBuild(s, civ, (BuildingType)hub, v, Strong(v, weight) ? ReasonSeat(L) : null);
            }

            Urgency(s, Math.Max(L.Stress, L.WonderNeed * 0.6f));
            Publish(s, L);
        }

        // ------------------------------------------------------------------ run preparation (civ-level)
        private void Prepare(SimContext ctx)
        {
            _preparedState = ctx.State;
            _preparedTick = ctx.Tick;
            _richestByCiv.Clear();
            _richestScore.Clear();
            _atWar.Clear();

            var wars = ctx.State.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null || !w.IsActive) continue;
                _atWar[w.AttackerId] = true;
                _atWar[w.DefenderId] = true;
            }

            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                var civ = ctx.State.Civilizations.Get(s.CivilizationId);
                if (civ == null || !civ.IsAlive) continue;
                float gold = TreasuryBuildingBook.NonNeg(s.Stockpile.Get(ResourceType.Gold));
                float inc = TreasuryBuildingBook.NonNeg(s.Stockpile.AvgIncome[(int)ResourceType.Gold]);
                float score = gold + inc * SimTime.TicksPerYear * 2f + ((s.IsCapital || civ.CapitalId == s.Id) ? 0.5f : 0f);
                if (!_richestScore.TryGetValue(civ.Id, out float best) || score > best)
                {
                    _richestScore[civ.Id] = score;
                    _richestByCiv[civ.Id] = s.Id;
                    civ.Memory[TreasuryKeys.Richest] = s.Id;
                }
            }
        }

        // ------------------------------------------------------------------ votes
        /// <summary>Scores every feasible gold producer, votes the best three; returns the finance-hub type or -1.</summary>
        private int VoteGoldSources(GameDatabase db, Civilization civ, Settlement s, TreasuryLedger L, float need, float weight, bool richest, bool wonderDriven)
        {
            int hub = -1;
            float hubCommerce = 0f;
            for (int t = 0; t < Counts.Buildings; t++)
            {
                _score[t] = 0f;
                if (_book.Roles[t] != TreasuryRole.GoldSource || !Feasible(db, civ, s, L, t)) continue;
                float y = _book.NetYield(t, L.Trade);
                if (!(y > 0f)) continue;
                var def = _book.Defs[t];
                float score = y;
                if ((def.Placement & PlacementRule.Coastal) != 0 && L.Coastal) score *= CoastalBonus;
                score /= 1f + L.Stress * _book.TotalCost[t] / 400f;         // broke towns prefer quick paybacks
                if (L.Existing[t] > 0) score *= 0.75f;                      // diversify revenue
                if (L.Queued[t] > 0) score *= 0.35f;                        // already on its way
                _score[t] = MathUtil.Sanitize(score);
                if (_book.CommercePoints[t] > hubCommerce) { hubCommerce = _book.CommercePoints[t]; hub = t; }
            }
            // Banks follow money: a poor outpost should not host the finance house when a richer town exists.
            if (hub >= 0 && !richest && civ.SettlementIds.Count > 1 && L.CoverYears < 2f) _score[hub] *= 0.5f;

            for (int k = 0; k < TopN; k++) { _top[k] = -1; _topScore[k] = 0f; }
            for (int t = 0; t < Counts.Buildings; t++)
            {
                float sc = _score[t];
                if (!(sc > 0f)) continue;
                for (int k = 0; k < TopN; k++)
                {
                    if (sc > _topScore[k])
                    {
                        for (int m = TopN - 1; m > k; m--) { _top[m] = _top[m - 1]; _topScore[m] = _topScore[m - 1]; }
                        _top[k] = t; _topScore[k] = sc;
                        break;
                    }
                }
            }
            for (int k = 0; k < TopN; k++)
            {
                if (_top[k] < 0) break;
                float v = need * s_rankStrength[k];
                string reason = Strong(v, weight) ? (wonderDriven ? ReasonWonder(L) : ReasonRevenue(L)) : null;
                VoteBuild(s, civ, (BuildingType)_top[k], v, reason);
            }
            return hub;
        }

        private void VoteAusterity(GameDatabase db, Civilization civ, Settlement s, TreasuryLedger L, float aggr, bool atWar, float weight)
        {
            float severity = MathUtil.InverseLerp(0.3f, 1f, L.Stress);
            bool hasDefense = L.MilitaryComplete > 0 || L.DefenseCoverage >= 0.75f;
            for (int t = 0; t < Counts.Buildings; t++)
            {
                var role = _book.Roles[t];
                if (role != TreasuryRole.Luxury && role != TreasuryRole.Military && role != TreasuryRole.Prestige) continue;
                float up = _book.Upkeep[t];
                if (!(up > 0f) || !db.IsBuildingAvailable(civ, (BuildingType)t)) continue;
                float heavy = MathUtil.Clamp(MathUtil.InverseLerp(0f, 0.06f, up), 0.35f, 1f);
                float v;
                string reason;
                if (role == TreasuryRole.Luxury)
                {
                    v = -(0.35f + 0.65f * severity) * heavy;
                    reason = Strong(v, weight) ? ReasonBelt(L) : null;
                }
                else if (role == TreasuryRole.Military)
                {
                    if (atWar || !hasDefense) continue; // never disarm in war or leave a town naked
                    v = -(0.3f + 0.6f * severity) * heavy * (1f - 0.6f * aggr);
                    reason = Strong(v, weight) ? ReasonGarrison(L) : null;
                }
                else
                {
                    if (L.Stress < 0.7f) continue; // prestige halls only go in a real crisis
                    v = -0.45f * severity * heavy;
                    reason = Strong(v, weight) ? ReasonBelt(L) : null;
                }
                VoteBuild(s, civ, (BuildingType)t, v, reason);
            }
        }

        private void VoteWonderThrift(GameDatabase db, Civilization civ, Settlement s, TreasuryLedger L, float weight)
        {
            float v = -0.25f * L.WonderNeed;
            for (int t = 0; t < Counts.Buildings; t++)
            {
                if (_book.Roles[t] != TreasuryRole.Luxury || !(_book.Upkeep[t] > 0f)) continue;
                if (!db.IsBuildingAvailable(civ, (BuildingType)t)) continue;
                VoteBuild(s, civ, (BuildingType)t, v, Strong(v, weight) ? ReasonWonder(L) : null);
            }
        }

        private void VotePatronage(GameDatabase db, Civilization civ, Settlement s, TreasuryLedger L, float merc, float weight)
        {
            float budget = Math.Max(0f, L.Surplus) * 0.6f;
            int best = -1, second = -1;
            float bestS = 0f, secondS = 0f;
            for (int t = 0; t < Counts.Buildings; t++)
            {
                var role = _book.Roles[t];
                if (role != TreasuryRole.Luxury && role != TreasuryRole.Prestige) continue;
                if (_book.Upkeep[t] > budget || !Feasible(db, civ, s, L, t)) continue;
                var def = _book.Defs[t];
                float sc = (_book.Entertainment[t] + 0.5f * MathUtil.Clamp(def.Desirability, -1f, 1f) + 0.1f) / (1f + _book.Upkeep[t] * 10f);
                if (role == TreasuryRole.Prestige) sc *= 0.6f;
                if (L.Existing[t] + L.Queued[t] > 0) sc *= 0.5f;
                if (!(sc > 0f)) continue;
                if (sc > bestS) { second = best; secondS = bestS; best = t; bestS = sc; }
                else if (sc > secondS) { second = t; secondS = sc; }
            }
            float v = MathUtil.Clamp(L.Wealth * (0.2f + 0.4f * merc), 0f, 0.6f);
            if (best >= 0) VoteBuild(s, civ, (BuildingType)best, v, Strong(v, weight) ? ReasonPatron(L) : null);
            if (second >= 0) VoteBuild(s, civ, (BuildingType)second, v * 0.6f, Strong(v * 0.6f, weight) ? ReasonPatron(L) : null);
        }

        /// <summary>Available to the civ, reachable tier/population, below per-settlement cap, not unique, placement satisfiable.</summary>
        private bool Feasible(GameDatabase db, Civilization civ, Settlement s, TreasuryLedger L, int t)
        {
            var def = _book.Defs[t];
            if (def == null || def.UniquePerCivilization || !db.IsBuildingAvailable(civ, (BuildingType)t)) return false;
            if (s.Tier < def.MinTier || s.Population < def.MinPopulation) return false;
            if (def.MaxPerSettlement >= 0 && L.Existing[t] + L.Queued[t] >= def.MaxPerSettlement) return false;
            return L.PlacementFeasible(def.Placement);
        }

        private static bool Strong(float v, float weight) => Math.Abs(v) * weight >= 0.5f;

        // ------------------------------------------------------------------ explainability
        private void Publish(Settlement s, TreasuryLedger L)
        {
            var mem = s.Memory;
            mem[TreasuryKeys.Runway] = Math.Min(L.RunwayYears, L.FutureRunwayYears);
            mem[TreasuryKeys.CoverYears] = L.CoverYears;
            mem[TreasuryKeys.Stress] = L.Stress;
            mem[TreasuryKeys.Wealth] = L.Wealth;
            mem[TreasuryKeys.Income] = L.Income;
            mem[TreasuryKeys.Upkeep] = L.Upkeep;
            mem[TreasuryKeys.PendingUpkeep] = L.PendingUpkeep;
            mem[TreasuryKeys.LastGold] = L.Gold;
            var mode = L.Mode;
            float prev = s.Mem(TreasuryKeys.Mode, -1f);
            mem[TreasuryKeys.Mode] = (float)mode;
            if ((int)prev != (int)mode && (prev >= 0f || mode != TreasuryMode.Balanced))
            {
                _sb.Length = 0;
                _sb.Append('[').Append(Id).Append("] ").Append(ModeName(mode)).Append(": runway ");
                AppendYears(_sb, Math.Min(L.RunwayYears, L.FutureRunwayYears));
                _sb.Append(", upkeep "); Append2(_sb, L.Upkeep);
                if (L.PendingUpkeep > 0f) { _sb.Append(" (+"); Append2(_sb, L.PendingUpkeep); _sb.Append(" coming)"); }
                _sb.Append(", income "); Append2(_sb, L.Income);
                s.LogAi(_sb.ToString());
            }
        }

        private static string ModeName(TreasuryMode m)
        {
            switch (m)
            {
                case TreasuryMode.Austerity: return "austerity";
                case TreasuryMode.Caution: return "caution";
                case TreasuryMode.Prosperity: return "prosperity";
                default: return "balanced";
            }
        }

        private string ReasonRevenue(TreasuryLedger L)
        {
            if (_reasonRevenue != null) return _reasonRevenue;
            _sb.Length = 0;
            if (L.Shortfall > 0.001f) { _sb.Append("upkeep unpaid "); AppendPct(_sb, L.Shortfall); _sb.Append(", "); }
            _sb.Append("runway "); AppendYears(_sb, Math.Min(L.RunwayYears, L.FutureRunwayYears));
            _sb.Append(", upkeep "); Append2(_sb, L.ProjectedUpkeep); _sb.Append("/tick, income "); Append2(_sb, L.Income);
            _sb.Append(": need revenue");
            return _reasonRevenue = _sb.ToString();
        }

        private string ReasonBelt(TreasuryLedger L)
        {
            if (_reasonBelt != null) return _reasonBelt;
            _sb.Length = 0;
            _sb.Append("upkeep "); Append2(_sb, L.ProjectedUpkeep); _sb.Append("/tick, income "); Append2(_sb, L.Income);
            _sb.Append(": belt-tightening");
            return _reasonBelt = _sb.ToString();
        }

        private string ReasonGarrison(TreasuryLedger L)
        {
            if (_reasonGarrison != null) return _reasonGarrison;
            _sb.Length = 0;
            _sb.Append("peacetime, upkeep "); Append2(_sb, L.ProjectedUpkeep); _sb.Append("/tick, income "); Append2(_sb, L.Income);
            _sb.Append(": trim the garrison");
            return _reasonGarrison = _sb.ToString();
        }

        private string ReasonPatron(TreasuryLedger L)
        {
            if (_reasonPatron != null) return _reasonPatron;
            _sb.Length = 0;
            _sb.Append("surplus +"); Append2(_sb, L.Surplus); _sb.Append("/tick, reserves "); AppendYears(_sb, L.CoverYears);
            _sb.Append(": patronage");
            return _reasonPatron = _sb.ToString();
        }

        private string ReasonWonder(TreasuryLedger L)
        {
            if (_reasonWonder != null) return _reasonWonder;
            _sb.Length = 0;
            _sb.Append("funding wonder ").Append(L.WonderType.ToString()).Append(": gold ");
            _sb.Append(MathUtil.RoundToInt(L.Gold)).Append('/').Append(MathUtil.RoundToInt(L.WonderGoldCost));
            return _reasonWonder = _sb.ToString();
        }

        private string ReasonSeat(TreasuryLedger L)
        {
            if (_reasonSeat != null) return _reasonSeat;
            _sb.Length = 0;
            _sb.Append("richest town (gold ").Append(MathUtil.RoundToInt(L.Gold)).Append("): seat of banking");
            return _reasonSeat = _sb.ToString();
        }

        // Culture-invariant fixed-point formatting (no CultureInfo dependency).
        private static void Append2(StringBuilder sb, float v)
        {
            v = MathUtil.Clamp(MathUtil.Sanitize(v), -99999f, 99999f);
            if (v < 0f) { sb.Append('-'); v = -v; }
            int scaled = MathUtil.RoundToInt(v * 100f);
            sb.Append(scaled / 100).Append('.');
            int frac = scaled % 100;
            if (frac < 10) sb.Append('0');
            sb.Append(frac);
        }

        private static void AppendYears(StringBuilder sb, float years)
        {
            if (years >= TreasuryLedger.MaxYears) { sb.Append("99+y"); return; }
            int scaled = MathUtil.RoundToInt(MathUtil.Clamp(MathUtil.Sanitize(years), 0f, 99f) * 10f);
            sb.Append(scaled / 10).Append('.').Append(scaled % 10).Append('y');
        }

        private static void AppendPct(StringBuilder sb, float v) => sb.Append(MathUtil.RoundToInt(MathUtil.Clamp01(v) * 100f)).Append('%');
    }
}
