using System;
using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Grand strategy for every alive civilization. Every <see cref="Interval"/> ticks it:
    /// <list type="number">
    /// <item>samples the economy (stock + yearly net flow per capita) into a 3-sample shift register and stores the
    /// trend (<c>ai.econTrend</c>); a population crash (&gt;= 20% below the recent peak) counts as a disaster;</item>
    /// <item>computes the posture (<c>ai.posture</c>: peaceful / defensive / militarist) from the dynamic personality
    /// (<see cref="AdvisorBoard.Trait"/>), active wars, war weariness (<c>war.weariness</c>), relative military power,
    /// hostile neighbours, hostile armies near our towns, god mood (gratitude / fear) and the economy trend;</item>
    /// <item>computes the development focus (<c>ai.focus</c>: growth / research / faith / military / wealth / recovery)
    /// from the same inputs plus food, housing and age progress. <see cref="CivStrategyKeys.FocusRecovery"/> wins
    /// after a recent serious disaster (DisasterEvent / FamineEvent / population crash) and fades over
    /// <see cref="RecoveryTicks"/>;</item>
    /// <item>times wonders at the capital: new wonder orders are paused during wars, famine, recovery or a falling
    /// economy; otherwise the best wonder (Ascension Gate, racial uniques, then the focus fit) is queued once the capital
    /// holds half its cost, and until then the Advisor Council is asked (<see cref="AdvisorBoard.VoteBuild"/>,
    /// advisor "wonder") to build producers of the missing materials at the capital.</item>
    /// </list>
    /// Posture and focus use two-level hysteresis: a challenger must beat the current value by <see cref="Hysteresis"/>,
    /// and within <see cref="MinDwellTicks"/> of the last change by <see cref="UrgentMargin"/>. Every decision writes one
    /// concise <c>LogAi</c> line at the capital naming its main factors.
    /// </summary>
    public sealed class CivilizationStrategySystem : ISimSystem
    {
        /// <summary>Tick interval of this system.</summary>
        public const int Interval = 24;
        /// <summary>BuildOrder priority used for wonders.</summary>
        public const float WonderPriority = 6f;
        /// <summary>Fraction of the wonder cost the capital must already hold.</summary>
        public const float WonderAffordFraction = 0.5f;
        /// <summary>Minimum ticks between two queue attempts of the same wonder (if the planner dropped the order).</summary>
        public const int WonderRetryTicks = SimTime.TicksPerYear * 3;
        /// <summary>Margin a challenger posture/focus needs over the current one (hysteresis).</summary>
        public const float Hysteresis = 0.1f;
        /// <summary>Margin needed to change again within <see cref="MinDwellTicks"/> of the last change.</summary>
        public const float UrgentMargin = 0.5f;
        /// <summary>Minimum time a posture/focus is kept unless the situation changes drastically.</summary>
        public const int MinDwellTicks = SimTime.TicksPerYear;
        /// <summary>Minimum radius (tiles) within which a hostile civ's settlement counts as a neighbour.</summary>
        public const int MinNeighbourRadius = 40;
        /// <summary>A hostile army this close (tiles) to one of our settlements raises the threat.</summary>
        public const int ArmyThreatRadius = 16;
        /// <summary>How long the recovery focus can last after a disaster.</summary>
        public const int RecoveryTicks = SimTime.TicksPerYear * 2;
        /// <summary>Disasters below this severity never trigger the recovery focus.</summary>
        public const float MinRecoverySeverity = 0.2f;
        /// <summary>Population below this fraction of the recent peak counts as a crash (disaster).</summary>
        public const float PopulationCrashFraction = 0.8f;
        /// <summary>New wonders are paused while the economy trend is below this.</summary>
        public const float WonderMinTrend = -0.15f;

        /// <summary>Wonder pause reasons stored in <c>ai.wonderPaused</c>.</summary>
        public const int PauseNone = 0, PauseWar = 1, PauseFamine = 2, PauseRecovery = 3, PauseEconomy = 4;

        private static readonly string[] WonderAttemptKeys = BuildAttemptKeys();
        private const string WonderTargetKey = "ai.wonderTarget";

        private readonly bool[] _wonderPresent = new bool[Counts.Buildings];
        private PlacementRule _capitalFeatures;
        private bool _capitalFeaturesValid;
        private readonly float[] _postureScores = new float[3];
        private readonly float[] _focusScores = new float[CivStrategyKeys.FocusCount];
        private readonly StringBuilder _sb = new StringBuilder(160);

        // Producer index per resource (rebuilt when the database instance changes).
        private GameDatabase _producerDb;
        private readonly List<BuildingType>[] _producers = NewProducerLists();

        // Event wiring (disasters / famines are remembered in civ memory, so nothing here needs saving).
        private EventBus _bus;
        private SimContext _ctx;
        private readonly Action<DisasterEvent> _onDisaster;
        private readonly Action<FamineEvent> _onFamine;

        // Per-civ situation (recomputed every run; scratch only).
        private struct Situation
        {
            public int Wars;
            public bool Defending;
            public int HostileNeighbours;
            public float PowerRatio;
            public bool Stronger, Weaker;
            public float ArmyDistance;
            public bool ArmyNear;
            public float Weariness, Gratitude, Fear, Trend;
            public bool FoodCrisis;
            public float WorstFoodDays;
        }

        /// <summary>Creates the system.</summary>
        public CivilizationStrategySystem()
        {
            _onDisaster = OnDisaster;
            _onFamine = OnFamine;
        }

        /// <inheritdoc />
        public string Name => "AI.CivilizationStrategy";

        /// <inheritdoc />
        public int TickInterval => Interval;

        /// <inheritdoc />
        public void Initialize(SimContext ctx)
        {
            if (ctx == null) return;
            _ctx = ctx;
            if (ctx.Events == null || ReferenceEquals(_bus, ctx.Events)) return;
            if (_bus != null) { _bus.Unsubscribe(_onDisaster); _bus.Unsubscribe(_onFamine); }
            _bus = ctx.Events;
            _bus.Subscribe(_onDisaster);
            _bus.Subscribe(_onFamine);
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            _ctx = ctx;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                var race = StrategyHelpers.RaceOf(ctx.Db, civ.Race);
                if (race == null) continue;
                var capital = StrategyHelpers.Capital(ctx.State, civ);
                if (capital == null) continue;
                SampleEconomy(ctx, civ, capital);
                var sit = Assess(ctx, civ, race);
                int posture = UpdatePosture(ctx, civ, race, capital, ref sit);
                int focus = UpdateFocus(ctx, civ, race, capital, posture, ref sit);
                ConsiderWonder(ctx, civ, race, capital, focus, ref sit);
            }
        }

        // ================================================================== events
        private void OnDisaster(DisasterEvent e)
        {
            var state = _ctx != null ? _ctx.State : null;
            if (state == null) return;
            float weight;
            switch (e.Type)
            {
                case DisasterType.Lightning: weight = 0.3f; break;
                case DisasterType.Storm: weight = 0.5f; break;
                default: weight = 1f; break;
            }
            float sev = MathUtil.Clamp01(MathUtil.Sanitize(e.Severity)) * weight;
            if (sev <= 0f) return;
            int r = Math.Max(0, e.Radius);
            var items = state.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (s == null || !s.IsAlive) continue;
                int d = s.Center.ChebyshevTo(e.Position);
                if (d > r + Math.Max(0, s.TerritoryRadius)) continue;
                var civ = state.Civilizations.Get(s.CivilizationId);
                if (civ == null || !civ.IsAlive) continue;
                StrategyHelpers.RecordDisaster(civ, state.Tick, d <= r ? sev : sev * 0.6f);
            }
        }

        private void OnFamine(FamineEvent e)
        {
            var state = _ctx != null ? _ctx.State : null;
            if (state == null) return;
            var s = state.Settlements.Get(e.SettlementId);
            if (s == null || !s.IsAlive) return;
            var civ = state.Civilizations.Get(s.CivilizationId);
            if (civ == null || !civ.IsAlive) return;
            civ.Memory[CivStrategyKeys.FamineTick] = state.Tick;
            StrategyHelpers.RecordDisaster(civ, state.Tick, 0.3f);
        }

        // ================================================================== economy trend
        /// <summary>Pushes one economy/population sample, updates the trend and detects population crashes.</summary>
        private static void SampleEconomy(SimContext ctx, Civilization civ, Settlement capital)
        {
            var state = ctx.State;
            float stock = 0f, net = 0f;
            int pop = 0;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (!StrategyHelpers.IsOwnAlive(s, civ.Id)) continue;
                pop += s.Population;
                stock += MathUtil.Sanitize(s.Stockpile.Total());
                for (int r = 0; r < Counts.Resources; r++)
                    net += MathUtil.Sanitize(s.Stockpile.Net((ResourceType)r)) * (r <= (int)ResourceType.Ore ? 1f : 2f);
            }
            float index = MathUtil.Clamp(MathUtil.Sanitize((stock + net * SimTime.TicksPerYear) / Math.Max(10, pop)), -1e6f, 1e6f);

            int n = MathUtil.Clamp(MathUtil.RoundToInt(MathUtil.Sanitize(civ.Mem(CivStrategyKeys.EconomySampleCount))), 0, CivStrategyKeys.EconomySamples);
            float peakPop = 0f;
            for (int i = 0; i < n; i++) peakPop = Math.Max(peakPop, MathUtil.Sanitize(civ.Mem(CivStrategyKeys.PopulationSample(i))));
            for (int i = CivStrategyKeys.EconomySamples - 1; i > 0; i--)
            {
                civ.Memory[CivStrategyKeys.EconomySample(i)] = MathUtil.Sanitize(civ.Mem(CivStrategyKeys.EconomySample(i - 1)));
                civ.Memory[CivStrategyKeys.PopulationSample(i)] = MathUtil.Sanitize(civ.Mem(CivStrategyKeys.PopulationSample(i - 1)));
            }
            civ.Memory[CivStrategyKeys.EconomySample(0)] = index;
            civ.Memory[CivStrategyKeys.PopulationSample(0)] = pop;
            n = Math.Min(CivStrategyKeys.EconomySamples, n + 1);
            civ.Memory[CivStrategyKeys.EconomySampleCount] = n;

            float trend = 0f;
            if (n >= 2)
            {
                float oldest = civ.Mem(CivStrategyKeys.EconomySample(n - 1));
                trend = MathUtil.Clamp(MathUtil.Sanitize((index - oldest) / (Math.Abs(oldest) + 1f)), -1f, 1f);
            }
            civ.Memory[CivStrategyKeys.EconomyTrend] = trend;

            if (peakPop >= 10f && pop <= peakPop * PopulationCrashFraction)
            {
                float sev = MathUtil.Clamp01((1f - pop / peakPop) * 2f);
                StrategyHelpers.RecordDisaster(civ, ctx.Tick, sev);
                for (int i = 1; i < n; i++) civ.Memory[CivStrategyKeys.PopulationSample(i)] = pop; // one crash, one record
                capital.LogAi("Crisis: population crash " + pop + "/" + MathUtil.RoundToInt(peakPop) + " (severity " + StrategyHelpers.Fmt1(sev) + ")");
            }
        }

        // ================================================================== situation
        private Situation Assess(SimContext ctx, Civilization civ, RaceDefinition race)
        {
            var state = ctx.State;
            var sit = new Situation();
            sit.Wars = StrategyHelpers.ActiveWars(state, civ.Id, out sit.Defending);
            float own = StrategyHelpers.EffectivePower(state, ctx.Db, civ);
            float maxRival = 0f;
            int rivals = 0;
            int radius = Math.Max(MinNeighbourRadius, Math.Max(1, race.MinSettlementSpacing) * 3);
            int radiusSq = radius * radius;

            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var other = civs[i];
                if (other == null || !other.IsAlive || other.Id == civ.Id) continue;
                bool hasRel = state.Diplomacy.TryGet(civ.Id, other.Id, out var rel);
                bool atWar = StrategyHelpers.WarBetween(state, civ.Id, other.Id);
                bool contact = atWar || (hasRel && (rel.HasContact || rel.AtWar));
                if (!contact) continue;
                bool hostile = atWar || (hasRel && (rel.AtWar || rel.Stance <= DiplomaticStance.Hostile));
                bool friendly = !hostile && hasRel && rel.Stance >= DiplomaticStance.Friendly;
                if (!friendly)
                {
                    rivals++;
                    float p = StrategyHelpers.EffectivePower(state, ctx.Db, other);
                    if (p > maxRival) maxRival = p;
                }
                if (hostile && StrategyHelpers.MinSettlementDistanceSq(state, civ, other) <= radiusSq) sit.HostileNeighbours++;
            }
            sit.PowerRatio = MathUtil.Clamp(MathUtil.Sanitize((own + 1f) / (maxRival + 1f), 1f), 0f, 100f);
            sit.Stronger = rivals > 0 && sit.PowerRatio >= 1.5f;
            sit.Weaker = rivals > 0 && sit.PowerRatio <= 0.67f;

            sit.ArmyDistance = float.MaxValue;
            if (state.Diplomacy.Armies.Count > 0)
                for (int i = 0; i < civ.SettlementIds.Count; i++)
                {
                    var s = state.Settlements.Get(civ.SettlementIds[i]);
                    if (!StrategyHelpers.IsOwnAlive(s, civ.Id)) continue;
                    float d = StrategyHelpers.NearestHostileArmy(state, civ.Id, s.Center);
                    if (d < sit.ArmyDistance) sit.ArmyDistance = d;
                }
            sit.ArmyNear = sit.ArmyDistance <= ArmyThreatRadius;
            sit.Weariness = StrategyHelpers.WarWeariness(civ);
            sit.Gratitude = AdvisorBoard.GodGratitude(civ);
            sit.Fear = AdvisorBoard.GodFear(civ);
            sit.Trend = StrategyHelpers.EconomyTrend(civ);
            sit.FoodCrisis = StrategyHelpers.InFoodCrisis(state, civ, race, ctx.Tick, out sit.WorstFoodDays);
            return sit;
        }

        // ================================================================== posture
        /// <summary>Computes and stores the civ's posture; returns it.</summary>
        private int UpdatePosture(SimContext ctx, Civilization civ, RaceDefinition race, Settlement capital, ref Situation sit)
        {
            float a = StrategyHelpers.Trait(civ, ctx.Db, AITrait.Aggression);
            float dip = StrategyHelpers.Trait(civ, ctx.Db, AITrait.Diplomacy);
            float war = sit.Wars > 0 ? 1f : 0f;
            float neighbourTerm = Math.Min(3, sit.HostileNeighbours);
            float army = sit.ArmyNear ? 1f : 0f;
            float weary = sit.Weariness * war;
            float falling = Math.Max(0f, -sit.Trend);
            float rising = Math.Max(0f, sit.Trend);

            _postureScores[CivStrategyKeys.PosturePeaceful] = (1f - a) * 0.8f + dip * 0.2f - war * 1.2f - neighbourTerm * 0.3f
                + sit.Gratitude * 0.15f + weary * 0.9f - army * 0.4f;
            _postureScores[CivStrategyKeys.PostureDefensive] = 0.3f + war * (sit.Defending ? 0.75f : 0.65f)
                + Math.Min(0.7f, neighbourTerm * 0.35f) + (sit.Weaker ? 0.35f : 0f) + army * 0.45f + sit.Fear * 0.2f + weary * 0.3f;
            _postureScores[CivStrategyKeys.PostureMilitarist] = a * 0.9f + war * (0.3f + 0.8f * a)
                + (sit.Stronger ? 0.25f * a : 0f) + (sit.HostileNeighbours > 0 ? 0.15f * a : 0f)
                - sit.Weariness * 1.0f - falling * 0.5f + rising * 0.1f * a - sit.Fear * 0.15f;

            int previous = civ.Memory.TryGetValue(CivStrategyKeys.Posture, out float prevF) ? MathUtil.Clamp(MathUtil.RoundToInt(MathUtil.Sanitize(prevF)), 0, 2) : -1;
            int posture = Decide(_postureScores, previous, civ, CivStrategyKeys.PostureSince, ctx.Tick);

            float threat = MathUtil.Clamp01(sit.Wars * 0.5f + sit.HostileNeighbours * 0.2f + (sit.Weaker ? 0.3f : 0f) + army * 0.3f);
            civ.Memory[CivStrategyKeys.Posture] = posture;
            civ.Memory[CivStrategyKeys.Threat] = threat;
            civ.Memory[CivStrategyKeys.PowerRatio] = sit.PowerRatio;
            civ.Memory[CivStrategyKeys.ActiveWars] = sit.Wars;
            civ.Memory[CivStrategyKeys.HostileNeighbours] = sit.HostileNeighbours;

            if (posture != previous)
            {
                _sb.Length = 0;
                _sb.Append("Posture: ");
                if (previous >= 0) _sb.Append(CivStrategyKeys.PostureName(previous)).Append(" -> ");
                _sb.Append(CivStrategyKeys.PostureName(posture)).Append(" (wars ").Append(sit.Wars)
                  .Append(", hostile neighbours ").Append(sit.HostileNeighbours)
                  .Append(", power x").Append(StrategyHelpers.Fmt1(sit.PowerRatio))
                  .Append(", aggr ").Append(StrategyHelpers.Fmt1(a));
                if (sit.ArmyNear) _sb.Append(", army ").Append(MathUtil.RoundToInt(sit.ArmyDistance)).Append("t");
                if (sit.Weariness > 0.05f) _sb.Append(", weariness ").Append(StrategyHelpers.Fmt1(sit.Weariness));
                AppendMood(ref sit);
                _sb.Append(")");
                capital.LogAi(_sb.ToString());
            }
            return posture;
        }

        private void AppendMood(ref Situation sit)
        {
            if (sit.Gratitude > 0.05f) _sb.Append(", gratitude ").Append(StrategyHelpers.Fmt1(sit.Gratitude));
            if (sit.Fear > 0.05f) _sb.Append(", fear ").Append(StrategyHelpers.Fmt1(sit.Fear));
            if (Math.Abs(sit.Trend) >= 0.05f) _sb.Append(", econ ").Append(StrategyHelpers.Pct(sit.Trend));
        }

        /// <summary>
        /// Two-level hysteresis: keep <paramref name="previous"/> unless the best alternative beats it by
        /// <see cref="Hysteresis"/> (by <see cref="UrgentMargin"/> within <see cref="MinDwellTicks"/> of the last change).
        /// </summary>
        private static int Decide(float[] scores, int previous, Civilization civ, string sinceKey, long tick)
        {
            for (int i = 0; i < scores.Length; i++) scores[i] = MathUtil.Sanitize(scores[i], -10f);
            int best = ArgMax(scores);
            int chosen = best;
            if (previous >= 0 && previous < scores.Length && best != previous)
            {
                float margin = scores[best] - scores[previous];
                long since = civ.Memory.TryGetValue(sinceKey, out float sf) ? (long)MathUtil.Sanitize(sf) : long.MinValue / 4;
                bool dwelling = tick - since < MinDwellTicks && tick >= since;
                if (margin <= Hysteresis || (dwelling && margin < UrgentMargin)) chosen = previous;
            }
            if (chosen != previous) civ.Memory[sinceKey] = tick;
            return chosen;
        }

        // ================================================================== focus
        /// <summary>Computes and stores the civ's development focus; returns it.</summary>
        private int UpdateFocus(SimContext ctx, Civilization civ, RaceDefinition race, Settlement capital, int posture, ref Situation sit)
        {
            var state = ctx.State;
            var db = ctx.Db;
            int pop = 0, homeless = 0;
            float happinessSum = 0f;
            int settlements = 0;
            for (int i = 0; i < civ.SettlementIds.Count; i++)
            {
                var s = state.Settlements.Get(civ.SettlementIds[i]);
                if (!StrategyHelpers.IsOwnAlive(s, civ.Id)) continue;
                settlements++;
                pop += s.Population;
                homeless += Math.Max(0, s.Homeless);
                happinessSum += MathUtil.Clamp01(MathUtil.Sanitize(s.Happiness, 0.5f));
            }
            float happiness = settlements > 0 ? happinessSum / settlements : 0.5f;
            float homelessRatio = pop > 0 ? MathUtil.Clamp01(homeless / (float)pop) : 0f;

            bool behindOnAgeTechs = false;
            if (civ.Age < AgeId.Ascension)
            {
                int next = (int)civ.Age + 1;
                var nextAge = next < db.Ages.Count ? db.Ages[next] : null;
                if (nextAge != null && CountKnownTechsOfAge(db, civ, civ.Age) < nextAge.RequiredTechsFromPreviousAge) behindOnAgeTechs = true;
            }
            float goldNet = MathUtil.Sanitize(capital.Stockpile.Net(ResourceType.Gold));
            float exp = StrategyHelpers.Trait(civ, db, AITrait.Expansionism);
            float cur = StrategyHelpers.Trait(civ, db, AITrait.Curiosity);
            float pie = StrategyHelpers.Trait(civ, db, AITrait.Piety);
            float agg = StrategyHelpers.Trait(civ, db, AITrait.Aggression);
            float mer = StrategyHelpers.Trait(civ, db, AITrait.Mercantilism);
            float falling = Math.Max(0f, -sit.Trend);
            float rising = Math.Max(0f, sit.Trend);
            float recovery = RecoveryScore(civ, ctx.Tick, out float disasterSeverity);

            _focusScores[CivStrategyKeys.FocusGrowth] = 0.25f + exp * 0.5f + (sit.FoodCrisis ? 0.5f : 0f)
                + homelessRatio * 0.8f + (pop < 30 ? 0.25f : 0f);
            _focusScores[CivStrategyKeys.FocusResearch] = 0.1f + cur * 0.9f + (behindOnAgeTechs ? 0.15f : 0f) + rising * 0.15f;
            _focusScores[CivStrategyKeys.FocusFaith] = 0.05f + pie * 0.9f + (civ.Devotion < 0.35f ? 0.15f : 0f)
                + (happiness < 0.45f ? 0.15f : 0f) + sit.Fear * 0.35f + sit.Gratitude * 0.2f;
            _focusScores[CivStrategyKeys.FocusMilitary] = agg * 0.4f
                + (posture == CivStrategyKeys.PostureMilitarist ? 0.5f : posture == CivStrategyKeys.PostureDefensive ? 0.3f : 0f)
                + Math.Min(0.6f, sit.Wars * 0.3f) - sit.Weariness * 0.4f + (sit.ArmyNear ? 0.2f : 0f);
            _focusScores[CivStrategyKeys.FocusWealth] = 0.1f + mer * 0.8f + (goldNet < 0f ? 0.25f : 0f) + (falling >= 0.15f ? 0.2f : 0f);
            _focusScores[CivStrategyKeys.FocusRecovery] = recovery;

            int previous = civ.Memory.TryGetValue(CivStrategyKeys.Focus, out float prevF) ? MathUtil.Clamp(MathUtil.RoundToInt(MathUtil.Sanitize(prevF)), 0, CivStrategyKeys.FocusCount - 1) : -1;
            int focus = Decide(_focusScores, previous, civ, CivStrategyKeys.FocusSince, ctx.Tick);
            civ.Memory[CivStrategyKeys.Focus] = focus;
            civ.Memory[CivStrategyKeys.FocusLegacy] = CivStrategyKeys.LegacyFocus(focus);
            if (focus != previous)
            {
                _sb.Length = 0;
                _sb.Append("Focus: ");
                if (previous >= 0) _sb.Append(CivStrategyKeys.FocusName(previous)).Append(" -> ");
                _sb.Append(CivStrategyKeys.FocusName(focus)).Append(" (score ").Append(StrategyHelpers.Fmt1(_focusScores[focus]));
                if (sit.FoodCrisis) _sb.Append(", food crisis ").Append(MathUtil.RoundToInt(Math.Min(999f, sit.WorstFoodDays))).Append("d");
                if (focus == CivStrategyKeys.FocusRecovery) _sb.Append(", disaster ").Append(StrategyHelpers.Fmt1(disasterSeverity));
                if (homelessRatio > 0.05f) _sb.Append(", homeless ").Append(StrategyHelpers.Pct(homelessRatio));
                if (behindOnAgeTechs) _sb.Append(", behind on age techs");
                if (sit.Wars > 0) _sb.Append(", wars ").Append(sit.Wars);
                AppendMood(ref sit);
                _sb.Append(")");
                capital.LogAi(_sb.ToString());
            }
            return focus;
        }

        /// <summary>Recovery focus score: strong right after a serious disaster, fading to nothing over <see cref="RecoveryTicks"/>.</summary>
        private static float RecoveryScore(Civilization civ, long tick, out float severity)
        {
            severity = 0f;
            if (!civ.Memory.TryGetValue(CivStrategyKeys.DisasterTick, out float dt)) return -10f;
            long age = tick - (long)MathUtil.Sanitize(dt);
            if (age < 0 || age > RecoveryTicks) return -10f;
            severity = MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(CivStrategyKeys.DisasterSeverity)));
            if (severity < MinRecoverySeverity) return -10f;
            return 0.5f + 1.1f * severity * (1f - age / (float)RecoveryTicks);
        }

        private static int CountKnownTechsOfAge(GameDatabase db, Civilization civ, AgeId age)
        {
            var ids = db.TechsOfAge(age);
            int n = 0;
            for (int i = 0; i < ids.Count; i++) if (civ.KnownTechs.Contains(ids[i])) n++;
            return n;
        }

        private static int ArgMax(float[] scores)
        {
            int best = 0;
            for (int i = 1; i < scores.Length; i++) if (scores[i] > scores[best] + 1e-6f) best = i;
            return best;
        }

        // ================================================================== wonders
        /// <summary>
        /// Times wonders: pauses new orders in bad times, otherwise queues the best qualifying wonder once affordable,
        /// or votes for producers of the missing materials at the capital while saving up.
        /// </summary>
        private void ConsiderWonder(SimContext ctx, Civilization civ, RaceDefinition race, Settlement capital, int focus, ref Situation sit)
        {
            var db = ctx.Db;
            var state = ctx.State;
            bool free = ScanWonders(state, db, civ, race, capital, out BuildingDefinition pending);
            _capitalFeaturesValid = false;
            if (pending != null) VoteMaterials(ctx, civ, capital, pending, "building");
            if (!free) return; // a wonder project is already under way

            int pause = sit.Wars > 0 ? PauseWar
                : sit.FoodCrisis ? PauseFamine
                : focus == CivStrategyKeys.FocusRecovery ? PauseRecovery
                : sit.Trend < WonderMinTrend ? PauseEconomy : PauseNone;
            int prevPause = MathUtil.RoundToInt(MathUtil.Sanitize(civ.Mem(CivStrategyKeys.WonderPaused)));
            if (pause != prevPause)
            {
                civ.Memory[CivStrategyKeys.WonderPaused] = pause;
                if (pause != PauseNone) capital.LogAi("Wonder: new projects on hold (" + PauseName(pause, ref sit) + ")");
                else if (prevPause != PauseNone) capital.LogAi("Wonder: projects resumed (peace, econ " + StrategyHelpers.Pct(sit.Trend) + ")");
            }
            if (pause != PauseNone) return;

            BuildingDefinition chosen = null;
            string why = null;

            // 1) The Ascension Gate is the endgame goal in the Ascension age.
            if (civ.Age == AgeId.Ascension && db.TryGetBuilding(BuildingType.AscensionGate, out var gate)
                && Qualifies(ctx, civ, race, capital, gate))
            {
                chosen = gate;
                why = "ascension";
            }

            // 2) Racial uniques, in the race's preference order.
            if (chosen == null && race.UniqueBuildings != null)
            {
                for (int i = 0; i < race.UniqueBuildings.Length && chosen == null; i++)
                    if (db.TryGetBuilding(race.UniqueBuildings[i], out var u) && Qualifies(ctx, civ, race, capital, u))
                    {
                        chosen = u;
                        why = "racial unique";
                    }
            }

            // 3) Shared wonders, best fit with the focus first (tie: lowest type id).
            if (chosen == null)
            {
                float bestFit = float.NegativeInfinity;
                var all = db.Buildings;
                for (int i = 0; i < all.Count; i++)
                {
                    var def = all[i];
                    if (def == null || !IsWonder(def, race)) continue;
                    if (!Qualifies(ctx, civ, race, capital, def)) continue;
                    float fit = FocusFit(def, focus);
                    if (fit > bestFit + 1e-6f) { bestFit = fit; chosen = def; }
                }
                if (chosen != null) why = "focus " + CivStrategyKeys.FocusName(focus);
            }

            if (chosen == null) return;
            if (!Affordable(capital, chosen, out float held))
            {
                VoteMaterials(ctx, civ, capital, chosen, "saving for");
                return;
            }
            capital.BuildQueue.Add(new BuildOrder
            {
                Type = chosen.Type,
                Priority = WonderPriority,
                RequestedTick = ctx.Tick,
                Reason = "wonder: " + StrategyHelpers.NameOf(chosen),
            });
            civ.Memory[WonderAttemptKeys[(int)chosen.Type]] = ctx.Tick;
            civ.Memory[CivStrategyKeys.WonderType] = (int)chosen.Type;
            civ.Memory[CivStrategyKeys.WonderTick] = ctx.Tick;
            civ.Memory.Remove(WonderTargetKey);
            capital.LogAi("Wonder: queued " + StrategyHelpers.NameOf(chosen) + " (" + why + "; peace, econ " + StrategyHelpers.Pct(sit.Trend)
                + ", holds " + MathUtil.RoundToInt(held * 100f) + "% of cost)");
            ctx.Log.Debug(civ.Name + " queued wonder " + StrategyHelpers.NameOf(chosen) + " at " + capital.Name);
        }

        private static string PauseName(int pause, ref Situation sit)
        {
            switch (pause)
            {
                case PauseWar: return "at war x" + sit.Wars;
                case PauseFamine: return "famine, " + MathUtil.RoundToInt(Math.Min(999f, sit.WorstFoodDays)) + " food days";
                case PauseRecovery: return "recovering from disaster";
                case PauseEconomy: return "economy " + StrategyHelpers.Pct(sit.Trend);
                default: return "-";
            }
        }

        /// <summary>True when the capital holds <see cref="WonderAffordFraction"/> of every cost item; held = worst held fraction.</summary>
        private static bool Affordable(Settlement capital, BuildingDefinition def, out float held)
        {
            held = 1f;
            var cost = def.Cost;
            if (cost == null) return true;
            bool ok = true;
            for (int i = 0; i < cost.Length; i++)
            {
                float amount = MathUtil.Sanitize(cost[i].Amount);
                if (amount <= 0f) continue;
                float have = capital.Stockpile.Get(cost[i].Type);
                held = Math.Min(held, MathUtil.Clamp01(have / amount));
                if (have + 1e-4f < amount * WonderAffordFraction) ok = false;
            }
            return ok;
        }

        /// <summary>
        /// Asks the Advisor Council (advisor "wonder") to build producers of every material the capital still lacks for the
        /// wonder (strength grows with the shortfall), and storage when the capital cannot even hold the cost. Logs once per target.
        /// </summary>
        private void VoteMaterials(SimContext ctx, Civilization civ, Settlement capital, BuildingDefinition wonder, string verb)
        {
            var cost = wonder.Cost;
            if (cost == null || cost.Length == 0) return;
            EnsureProducers(ctx.Db);
            bool log = MathUtil.RoundToInt(civ.Mem(WonderTargetKey, -1f)) != (int)wonder.Type; // strings only when logging
            string firstVote = null;
            string firstShort = null;
            bool anyShort = false;
            for (int i = 0; i < cost.Length; i++)
            {
                var res = cost[i].Type;
                float need = MathUtil.Sanitize(cost[i].Amount);
                if (need <= 0f) continue;
                float have = capital.Stockpile.Get(res);
                float shortfall = MathUtil.Clamp01(1f - have / need);
                if (shortfall < 0.05f) continue;
                anyShort = true;
                if (log && firstShort == null) firstShort = res + " " + MathUtil.RoundToInt(have) + "/" + MathUtil.RoundToInt(need);
                float strength = 0.4f + 0.6f * shortfall;
                var list = _producers[(int)res];
                int voted = 0;
                for (int p = 0; p < list.Count && voted < 2; p++)
                {
                    var type = list[p];
                    if (!ctx.Db.IsBuildingAvailable(civ, type) || !ctx.Db.TryGetBuilding(type, out var def)) continue;
                    if (!PlacementPlausible(ctx.State.Map, capital, def.Placement)) continue;
                    AdvisorBoard.VoteBuild(capital, civ, CivStrategyKeys.WonderAdvisorId, type, strength);
                    if (log && firstVote == null) firstVote = StrategyHelpers.NameOf(def) + " +" + StrategyHelpers.Fmt1(strength);
                    voted++;
                }
                if (capital.Stockpile.Capacity + 1e-3f < need)
                {
                    var store = res == ResourceType.Food ? BuildingType.Granary : BuildingType.Storehouse;
                    if (ctx.Db.IsBuildingAvailable(civ, store)) AdvisorBoard.VoteBuild(capital, civ, CivStrategyKeys.WonderAdvisorId, store, 0.6f);
                }
            }
            if (!anyShort || !log) return;
            civ.Memory[WonderTargetKey] = (int)wonder.Type;
            capital.LogAi("Wonder: " + verb + " " + StrategyHelpers.NameOf(wonder) + " (" + firstShort + ")"
                + (firstVote != null ? " -> vote " + firstVote : " -> no producer available"));
        }

        private void EnsureProducers(GameDatabase db)
        {
            if (ReferenceEquals(_producerDb, db)) return;
            _producerDb = db;
            for (int r = 0; r < _producers.Length; r++) _producers[r].Clear();
            var all = db.Buildings;
            for (int r = 0; r < _producers.Length; r++)
            {
                var list = _producers[r];
                for (int i = 0; i < all.Count; i++)
                {
                    var def = all[i];
                    if (def == null || def.IsWonder || def.Category == BuildingCategory.Wonder || def.IsDwelling) continue;
                    if (OutputOf(def, (ResourceType)r) <= 0f) continue;
                    // Insert sorted by output desc, ties by type id (deterministic).
                    float o = OutputOf(def, (ResourceType)r);
                    int at = list.Count;
                    for (int k = 0; k < list.Count; k++)
                        if (o > OutputOf(db.Building(list[k]), (ResourceType)r) + 1e-6f) { at = k; break; }
                    list.Insert(at, def.Type);
                }
            }
        }

        private static float OutputOf(BuildingDefinition def, ResourceType r)
        {
            var outs = def.OutputsPerWorker;
            if (outs == null) return 0f;
            float sum = 0f;
            for (int i = 0; i < outs.Length; i++) if (outs[i].Type == r) sum += Math.Max(0f, MathUtil.Sanitize(outs[i].Amount));
            return sum * Math.Max(1, def.JobSlots);
        }

        /// <summary>
        /// Marks every wonder type built, under construction or queued anywhere in the civ.
        /// Returns false when a wonder project (queued or under construction) is already in progress; <paramref name="pending"/>
        /// is a wonder at the capital whose cost is not paid yet (needs materials), or null.
        /// </summary>
        private bool ScanWonders(WorldState state, GameDatabase db, Civilization civ, RaceDefinition race, Settlement capital, out BuildingDefinition pending)
        {
            Array.Clear(_wonderPresent, 0, _wonderPresent.Length);
            pending = null;
            bool inProgress = false;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s == null || s.CivilizationId != civ.Id) continue;
                for (int q = 0; q < s.BuildQueue.Count; q++)
                {
                    var order = s.BuildQueue[q];
                    if (order == null) continue;
                    if (!db.TryGetBuilding(order.Type, out var def) || !IsWonder(def, race)) continue;
                    _wonderPresent[(int)order.Type] = true;
                    inProgress = true;
                    if (s == capital && pending == null) pending = def;
                }
                for (int b = 0; b < s.BuildingIds.Count; b++)
                {
                    var bld = state.Buildings.Get(s.BuildingIds[b]);
                    if (bld == null || bld.IsDestroyed) continue;
                    if (!db.TryGetBuilding(bld.Type, out var def) || !IsWonder(def, race)) continue;
                    _wonderPresent[(int)bld.Type] = true;
                    if (!bld.IsComplete)
                    {
                        inProgress = true;
                        if (s == capital && !bld.CostPaid && pending == null) pending = def;
                    }
                }
            }
            return !inProgress;
        }

        /// <summary>A wonder for AI purposes: flagged wonder, wonder category, or one of the race's unique buildings.</summary>
        private static bool IsWonder(BuildingDefinition def, RaceDefinition race)
        {
            if (def.IsWonder || def.Category == BuildingCategory.Wonder) return true;
            return IsRaceUnique(race, def.Type);
        }

        private static bool IsRaceUnique(RaceDefinition race, BuildingType type)
        {
            var u = race?.UniqueBuildings;
            if (u == null) return false;
            for (int i = 0; i < u.Length; i++) if (u[i] == type) return true;
            return false;
        }

        /// <summary>All gating rules (except affordability) for queueing one wonder at the capital.</summary>
        private bool Qualifies(SimContext ctx, Civilization civ, RaceDefinition race, Settlement capital, BuildingDefinition def)
        {
            int t = (int)def.Type;
            if (t < 0 || t >= _wonderPresent.Length || _wonderPresent[t]) return false;
            if (!ctx.Db.IsBuildingAvailable(civ, def.Type)) return false;
            if (civ.Memory.TryGetValue(WonderAttemptKeys[t], out float last) && ctx.Tick - (long)MathUtil.Sanitize(last) < WonderRetryTicks) return false;

            bool earlyUnique = IsRaceUnique(race, def.Type) && def.MinAge <= AgeId.Bronze;
            SettlementTier required = earlyUnique ? SettlementTier.Village : SettlementTier.Town;
            if (def.MinTier > required) required = def.MinTier;
            if (capital.Tier < required) return false;
            if (capital.Population < def.MinPopulation) return false;
            return PlacementPlausible(ctx.State.Map, capital, def.Placement);
        }

        /// <summary>Cheap check that the capital's surroundings can satisfy the hard placement rules (scan cached per civ).</summary>
        private bool PlacementPlausible(WorldMap map, Settlement capital, PlacementRule rule)
        {
            if ((rule & StrategyHelpers.HardPlacementRules) == PlacementRule.None) return true;
            if (!_capitalFeaturesValid)
            {
                _capitalFeatures = StrategyHelpers.ScanFeatures(map, capital.Center, MathUtil.Clamp(capital.TerritoryRadius + 2, 6, 24));
                _capitalFeaturesValid = true;
            }
            return StrategyHelpers.PlacementSatisfied(_capitalFeatures, rule);
        }

        /// <summary>How well a wonder's services match the focus (higher = better).</summary>
        private static float FocusFit(BuildingDefinition def, int focus)
        {
            float fit = 0f;
            var svc = def.Services;
            if (svc != null)
                for (int i = 0; i < svc.Length; i++)
                {
                    float w;
                    switch (svc[i].Type)
                    {
                        case ServiceType.Research: w = focus == CivStrategyKeys.FocusResearch ? 3f : 1f; break;
                        case ServiceType.Faith: w = focus == CivStrategyKeys.FocusFaith ? 3f : 1f; break;
                        case ServiceType.Commerce: w = focus == CivStrategyKeys.FocusWealth ? 3f : 1f; break;
                        case ServiceType.Defense: w = focus == CivStrategyKeys.FocusMilitary ? 3f : 1f; break;
                        case ServiceType.Health: case ServiceType.Water: w = focus == CivStrategyKeys.FocusGrowth || focus == CivStrategyKeys.FocusRecovery ? 3f : 1f; break;
                        default: w = 1f; break;
                    }
                    fit += w * Math.Max(0f, MathUtil.Sanitize(svc[i].Amount));
                }
            return MathUtil.Sanitize(fit);
        }

        private static string[] BuildAttemptKeys()
        {
            var keys = new string[Counts.Buildings];
            for (int i = 0; i < keys.Length; i++) keys[i] = CivStrategyKeys.WonderAttemptPrefix + i;
            return keys;
        }

        private static List<BuildingType>[] NewProducerLists()
        {
            var a = new List<BuildingType>[Counts.Resources];
            for (int i = 0; i < a.Length; i++) a[i] = new List<BuildingType>();
            return a;
        }
    }
}
