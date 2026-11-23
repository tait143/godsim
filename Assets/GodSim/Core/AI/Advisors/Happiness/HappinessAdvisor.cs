using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The Happiness advisor keeps people content and heads off unrest before it turns into rebellion.
    /// <para>
    /// Every 24 ticks, per settlement, it (1) tracks EMAs and trends of happiness, unrest and population in
    /// <c>adv.happy.*</c> memory; (2) diagnoses the causes of discontent (homelessness, food, Entertainment and
    /// Faith coverage projected for coming growth and pending construction, war, recent deaths, distance from the
    /// capital); (3) forecasts unrest a couple of runs ahead into a 0..1 risk that remote settlements feel more
    /// strongly; and (4) votes. Culture venues are chosen from data (Entertainment services, age, cost, existing
    /// count) with a racial/personality taste (Orcs: Arena, Humans: Tavern/Theater, Elves: gentle venues), with
    /// diminishing returns as coverage approaches full. When the real cause is housing or food it votes House/food
    /// at moderate strength instead of more theaters. Idle venues get staffed before new ones are built, and high
    /// unrest triggers an urgent vote plus Entertainer job votes.
    /// </para>
    /// Votes are soft (AdvisorBoard); the advisor never touches the build queue.
    /// </summary>
    public sealed class HappinessAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in memory keys and logs.</summary>
        public const string Id = "happiness";

        /// <summary>Coverage the advisor aims for (raised for remote settlements).</summary>
        public const float CultureTarget = 0.8f;
        /// <summary>Unrest at or above which the advisor treats the settlement as an emergency.</summary>
        public const float UrgentUnrest = 0.4f;
        /// <summary>Smoothed unrest increase per run above which unrest counts as rising.</summary>
        public const float RisingTrend = 0.006f;

        private const float TrendAlpha = 0.5f;
        private const float EmaAlpha = 0.35f;
        private const float DeathDecayPerRun = 0.7f;

        private HappinessCatalog _catalog;
        private readonly int[] _existing = new int[Counts.Buildings];
        private readonly int[] _pending = new int[Counts.Buildings];
        private readonly int[] _idleSlotsByType = new int[Counts.Buildings];
        private readonly int[] _idleSlotsByJob = new int[Counts.Jobs];
        private float _pendingEnt, _pendingFaith;
        private int _pendingHousing, _entertainerVenues, _pendingEntertainerVenues, _idleCultureSlots;
        private readonly HappinessDiagnosis _diag = new HappinessDiagnosis();

        private WorldState _state;
        private EventBus _bus;
        private readonly Action<PersonDiedEvent> _onDied;

        /// <summary>Creates the advisor.</summary>
        public HappinessAdvisor() { _onDied = OnPersonDied; }

        /// <inheritdoc />
        public override string AdvisorId => Id;
        /// <inheritdoc />
        public override int TickInterval => 24;

        /// <summary>Diagnosis of the settlement advised most recently (reused object; for UI/tests).</summary>
        public HappinessDiagnosis LastDiagnosis => _diag;

        /// <inheritdoc />
        public override void Initialize(SimContext ctx)
        {
            if (ctx == null) return;
            _state = ctx.State;
            if (ctx.Events != null && !ReferenceEquals(ctx.Events, _bus))
            {
                if (_bus != null) _bus.Unsubscribe(_onDied);
                _bus = ctx.Events;
                _bus.Subscribe(_onDied);
            }
        }

        private void OnPersonDied(PersonDiedEvent e)
        {
            var st = _state;
            if (st == null || e.SettlementId < 0) return;
            var s = st.Settlements.Get(e.SettlementId);
            if (s == null || !s.IsAlive) return;
            float w = e.Cause == DeathCause.OldAge ? 0.2f : 1f;
            float v = MathUtil.Sanitize(s.Mem(HappinessAdvisorKeys.RecentDeaths)) + w;
            s.Memory[HappinessAdvisorKeys.RecentDeaths] = MathUtil.Clamp(v, 0f, 10000f);
        }

        // =================================================================== main
        /// <inheritdoc />
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            if (db == null) return;
            _state = ctx.State;
            if (_catalog == null || !ReferenceEquals(_catalog.Db, db)) _catalog = new HappinessCatalog(db);

            var d = _diag;
            d.Reset(s.Id);
            int pop = s.Population;
            d.Population = pop;
            if (pop <= 0) { Urgency(s, 0f); return; }

            Track(s, pop, d);
            Survey(ctx, s);
            Diagnose(ctx, civ, s, pop, d);

            s.Memory[HappinessAdvisorKeys.Risk] = d.Risk;
            s.Memory[HappinessAdvisorKeys.Cause] = (int)d.Cause;
            s.Memory[HappinessAdvisorKeys.Remote] = d.Remote;
            Urgency(s, d.Urgency);

            float aggression = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            float piety = AdvisorBoard.Trait(civ, db, AITrait.Piety);
            float mercantilism = AdvisorBoard.Trait(civ, db, AITrait.Mercantilism);

            VoteHousing(ctx, civ, s, pop, d);
            VoteFood(ctx, civ, s, pop, d);
            VoteCulture(ctx, civ, s, pop, d, aggression, piety, mercantilism);
            VoteFaith(ctx, civ, s, pop, d, piety);
            VoteGovernance(ctx, civ, s, d);
        }

        // =================================================================== tracking (EMA + trends)
        private static void Track(Settlement s, int pop, HappinessDiagnosis d)
        {
            float h = MathUtil.Clamp01(MathUtil.Sanitize(s.Happiness, 0.6f));
            float u = MathUtil.Clamp01(MathUtil.Sanitize(s.Unrest));
            d.Happiness = h;
            d.Unrest = u;
            var m = s.Memory;
            float n = MathUtil.Sanitize(s.Mem(HappinessAdvisorKeys.Samples));
            bool first = n < 1f;
            float prevH = first ? h : MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(HappinessAdvisorKeys.LastHappiness, h), h));
            float prevU = first ? u : MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(HappinessAdvisorKeys.LastUnrest, u), u));
            float prevPop = first ? pop : Math.Max(0f, MathUtil.Sanitize(s.Mem(HappinessAdvisorKeys.LastPopulation, pop), pop));

            float hTrend = first ? 0f : MathUtil.Lerp(MathUtil.Sanitize(s.Mem(HappinessAdvisorKeys.HappinessTrend)), h - prevH, TrendAlpha);
            float uTrend = first ? 0f : MathUtil.Lerp(MathUtil.Sanitize(s.Mem(HappinessAdvisorKeys.UnrestTrend)), u - prevU, TrendAlpha);
            float popTrend = first ? 0f : MathUtil.Lerp(MathUtil.Sanitize(s.Mem(HappinessAdvisorKeys.PopulationTrend)), pop - prevPop, 0.4f);
            float hEma = first ? h : MathUtil.Lerp(MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(HappinessAdvisorKeys.HappinessEma, h), h)), h, EmaAlpha);
            float uEma = first ? u : MathUtil.Lerp(MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(HappinessAdvisorKeys.UnrestEma, u), u)), u, EmaAlpha);

            d.HappinessTrend = MathUtil.Clamp(hTrend, -1f, 1f);
            d.UnrestTrend = MathUtil.Clamp(uTrend, -1f, 1f);
            d.PopulationTrend = MathUtil.Clamp(popTrend, -10000f, 10000f);

            float deaths = MathUtil.Clamp(MathUtil.Sanitize(s.Mem(HappinessAdvisorKeys.RecentDeaths)), 0f, 10000f);
            d.RecentDeaths = deaths;

            m[HappinessAdvisorKeys.LastHappiness] = h;
            m[HappinessAdvisorKeys.LastUnrest] = u;
            m[HappinessAdvisorKeys.LastPopulation] = pop;
            m[HappinessAdvisorKeys.HappinessTrend] = d.HappinessTrend;
            m[HappinessAdvisorKeys.UnrestTrend] = d.UnrestTrend;
            m[HappinessAdvisorKeys.PopulationTrend] = d.PopulationTrend;
            m[HappinessAdvisorKeys.HappinessEma] = MathUtil.Clamp01(hEma);
            m[HappinessAdvisorKeys.UnrestEma] = MathUtil.Clamp01(uEma);
            if (deaths * DeathDecayPerRun < 0.05f) m.Remove(HappinessAdvisorKeys.RecentDeaths);
            else m[HappinessAdvisorKeys.RecentDeaths] = deaths * DeathDecayPerRun;
            m[HappinessAdvisorKeys.Samples] = Math.Min(1000000f, n + 1f);
        }

        // =================================================================== survey (existing, pending, idle venues)
        private void Survey(SimContext ctx, Settlement s)
        {
            Array.Clear(_existing, 0, _existing.Length);
            Array.Clear(_pending, 0, _pending.Length);
            Array.Clear(_idleSlotsByType, 0, _idleSlotsByType.Length);
            Array.Clear(_idleSlotsByJob, 0, _idleSlotsByJob.Length);
            _pendingEnt = _pendingFaith = 0f;
            _pendingHousing = _entertainerVenues = _pendingEntertainerVenues = _idleCultureSlots = 0;

            var db = ctx.Db;
            var buildings = ctx.State.Buildings;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                if (!db.TryGetBuilding(b.Type, out var def)) continue;
                int t = (int)b.Type;
                if (b.IsComplete)
                {
                    _existing[t]++;
                    if (def.Job == JobType.Entertainer && def.JobSlots > 0) _entertainerVenues++;
                    if (def.HasJobs && (_catalog.IsCulture[t] || _catalog.IsFaith[t]) && b.Workers.Count < def.JobSlots)
                    {
                        int open = def.JobSlots - b.Workers.Count;
                        _idleSlotsByType[t] += open;
                        _idleSlotsByJob[(int)def.Job] += open;
                        if (_catalog.IsCulture[t]) _idleCultureSlots += open;
                    }
                }
                else AddPending(def);
            }
            var queue = s.BuildQueue;
            for (int i = 0; i < queue.Count; i++)
            {
                var o = queue[i];
                if (o == null || !db.TryGetBuilding(o.Type, out var def)) continue;
                AddPending(def);
            }
        }

        private void AddPending(BuildingDefinition def)
        {
            _pending[(int)def.Type]++;
            _pendingEnt += HappinessCatalog.ServicePoints(def, ServiceType.Entertainment);
            _pendingFaith += HappinessCatalog.ServicePoints(def, ServiceType.Faith);
            if (def.IsDwelling) _pendingHousing += Math.Max(1, (def.HousingMin + def.HousingMax) / 2);
            if (def.Job == JobType.Entertainer && def.JobSlots > 0) _pendingEntertainerVenues++;
        }

        // =================================================================== diagnosis
        private void Diagnose(SimContext ctx, Civilization civ, Settlement s, int pop, HappinessDiagnosis d)
        {
            var db = ctx.Db;
            RaceDefinition race = (int)s.Race < db.Races.Count ? db.Races[(int)s.Race] : null;

            // Growth anticipation: services and housing will be shared by more people soon.
            float growth = Math.Max(0f, d.PopulationTrend) * 2f;
            float popFactor = pop / (pop + growth);

            float entCov = MathUtil.Clamp(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Entertainment]), 0f, 4f);
            float faithCov = MathUtil.Clamp(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Faith]), 0f, 4f);
            d.EntertainmentCoverage = entCov;
            d.FaithCoverage = faithCov;
            d.EntertainmentProjected = MathUtil.Clamp((entCov + _pendingEnt * CoveragePerPoint(civ, s, ServiceType.Entertainment, pop)) * popFactor, 0f, 4f);
            d.FaithProjected = MathUtil.Clamp((faithCov + _pendingFaith * CoveragePerPoint(civ, s, ServiceType.Faith, pop)) * popFactor, 0f, 4f);

            // Housing (now and soon).
            d.Homeless = MathUtil.Clamp(s.Homeless, 0, pop);
            float free = s.Mem("pop.freeHousing", -1f);
            if (free >= 0f && growth >= 1f)
                d.PredictedHomeless = Math.Max(0, (int)Math.Ceiling(growth) - MathUtil.FloorToInt(MathUtil.Sanitize(free)) - _pendingHousing);
            d.HousingLoss = 0.35f * (d.Homeless + 0.5f * d.PredictedHomeless) / pop;

            // Food.
            float perPerson = race != null ? Math.Max(0.005f, MathUtil.Sanitize(race.FoodPerTick, 0.05f)) : 0.05f;
            float food = Math.Max(0f, MathUtil.Sanitize(s.Stockpile.Get(ResourceType.Food)));
            float net = MathUtil.Sanitize(s.Stockpile.Net(ResourceType.Food));
            float days;
            if (net < -0.0001f) days = food / -net;
            else if (net > 0.0001f) days = 999f;
            else days = food / (pop * perPerson);
            if (s.Mem("pop.foodEmergency") >= 0.5f) days = Math.Min(days, 10f);
            d.Starving = s.LastStarvationTick >= 0 && ctx.Tick - s.LastStarvationTick <= 48;
            d.FoodDays = MathUtil.Clamp(MathUtil.Sanitize(days, 999f), 0f, 999f);
            d.FoodSeverity = d.Starving ? 1f : MathUtil.InverseLerp(30f, 6f, d.FoodDays);
            d.FoodLoss = 0.2f * d.FoodSeverity + (d.Starving ? 0.08f : 0f);

            // Services.
            d.EntertainmentLoss = 0.1f * (1f - Math.Min(1f, d.EntertainmentProjected));
            d.FaithLoss = 0.08f * (1f - Math.Min(1f, d.FaithProjected));

            // War, grief, distance.
            d.AtWar = AtWar(ctx.State, civ.Id);
            d.WarLoss = d.AtWar ? 0.08f : 0f;
            d.DeathShare = MathUtil.Clamp01(d.RecentDeaths / pop);
            d.GriefLoss = 0.12f * MathUtil.SmoothStep(0f, 0.15f, d.DeathShare);
            d.Remote = Remoteness(ctx.State, civ, s, race, out d.DistanceToCapital);
            d.RemoteLoss = 0.06f * d.Remote;

            d.TotalLoss = d.HousingLoss + d.FoodLoss + d.EntertainmentLoss + d.FaithLoss + d.WarLoss + d.GriefLoss + d.RemoteLoss;
            PickCause(d);

            // Forecast-based risk.
            d.UnrestForecast = MathUtil.Clamp01(d.Unrest + Math.Max(0f, d.UnrestTrend) * 2f);
            d.HappinessForecast = MathUtil.Clamp01(d.Happiness + Math.Min(0f, d.HappinessTrend) * 2f);
            float unrestRisk = MathUtil.SmoothStep(0.03f, 0.5f, d.UnrestForecast);
            float moodRisk = MathUtil.SmoothStep(0.52f, 0.3f, d.HappinessForecast);
            float lossRisk = MathUtil.SmoothStep(0.05f, 0.3f, d.TotalLoss);
            float risk = Math.Max(unrestRisk, Math.Max(0.8f * moodRisk, 0.5f * lossRisk));
            d.Risk = MathUtil.Clamp01(MathUtil.Sanitize(risk * (1f + 0.5f * d.Remote)));
            d.Rising = d.UnrestTrend > RisingTrend || (d.HappinessTrend < -0.02f && d.Happiness < 0.5f);
            d.Urgent = d.Unrest >= UrgentUnrest || d.UnrestForecast >= 0.5f;
            d.Urgency = MathUtil.Clamp01(Math.Max(d.Risk, d.Urgent ? 0.85f : 0f));
        }

        private static void PickCause(HappinessDiagnosis d)
        {
            HappinessCause c = HappinessCause.None;
            float best = 0.015f;
            Consider(ref c, ref best, HappinessCause.Housing, d.HousingLoss);
            Consider(ref c, ref best, HappinessCause.Food, d.FoodLoss);
            Consider(ref c, ref best, HappinessCause.Entertainment, d.EntertainmentLoss);
            Consider(ref c, ref best, HappinessCause.Faith, d.FaithLoss);
            Consider(ref c, ref best, HappinessCause.War, d.WarLoss);
            Consider(ref c, ref best, HappinessCause.Grief, d.GriefLoss);
            Consider(ref c, ref best, HappinessCause.Remoteness, d.RemoteLoss);
            d.Cause = c;
            d.CauseLoss = c == HappinessCause.None ? 0f : best;
        }

        private static void Consider(ref HappinessCause c, ref float best, HappinessCause candidate, float loss)
        {
            if (loss > best) { best = loss; c = candidate; }
        }

        // =================================================================== votes
        private void VoteHousing(SimContext ctx, Civilization civ, Settlement s, int pop, HappinessDiagnosis d)
        {
            int uncovered = d.Homeless + d.PredictedHomeless - _pendingHousing;
            if (uncovered <= 0 || (d.Homeless < 1 && d.PredictedHomeless < 2)) return;
            int best = PickBest(ctx, civ, s, _catalog.Dwellings, CandidateKind.Dwelling, 0f, 0f, d.Urgency, 0.5f, 0.5f, 0.5f, out _, out _, out _);
            if (best < 0) return;
            float share = uncovered / (float)pop;
            float strength = MathUtil.Clamp(0.25f + 1.4f * share + 0.2f * d.Risk, 0f, 0.65f) * (1f + 0.15f * d.Remote);
            string reason = null;
            if (WillLog(civ, strength))
                reason = d.Homeless > 0
                    ? "Unrest from homelessness: " + d.Homeless + " without a roof"
                    : "Growth will outpace housing: ~" + d.PredictedHomeless + " soon homeless";
            VoteBuild(s, civ, (BuildingType)best, strength, reason);
        }

        private void VoteFood(SimContext ctx, Civilization civ, Settlement s, int pop, HappinessDiagnosis d)
        {
            if (d.FoodSeverity < 0.25f) return;
            int best = PickBest(ctx, civ, s, _catalog.Food, CandidateKind.Food, 0f, 0f, d.Urgency, 0.5f, 0.5f, 0.5f, out _, out _, out _);
            if (best < 0) return;
            float strength = MathUtil.Clamp(0.2f + 0.45f * d.FoodSeverity + 0.1f * d.Risk, 0f, 0.6f);
            string reason = null;
            if (WillLog(civ, strength))
                reason = d.Starving ? "Starvation stirs unrest" : "Hunger breeds unrest: " + MathUtil.RoundToInt(d.FoodDays) + " days of food left";
            VoteBuild(s, civ, (BuildingType)best, strength, reason);
            if (ctx.Db.TryGetBuilding((BuildingType)best, out var def) && def.HasJobs)
                VoteJob(s, civ, def.Job, 0.2f + 0.4f * d.FoodSeverity);
        }

        private void VoteCulture(SimContext ctx, Civilization civ, Settlement s, int pop, HappinessDiagnosis d, float aggression, float piety, float mercantilism)
        {
            float target = CultureTarget + 0.15f * d.Remote;
            float gapNeed = MathUtil.Clamp01((target - d.EntertainmentProjected) / target);
            float riskNeed = d.Risk * (d.Rising ? 1f : 0.6f);
            float griefNeed = 0.5f * MathUtil.SmoothStep(0f, 0.12f, d.DeathShare);
            float satiation = MathUtil.SmoothStep(0.9f, 1.4f, d.EntertainmentProjected); // diminishing returns
            float need = Math.Max(gapNeed, Math.Max(riskNeed, griefNeed)) * (1f - satiation) * (1f + 0.5f * d.Remote);
            bool otherCause = (d.Cause == HappinessCause.Housing || d.Cause == HappinessCause.Food) && d.EntertainmentLoss < 0.5f * d.CauseLoss;
            if (otherCause) need *= 0.45f;
            need = MathUtil.Clamp01(MathUtil.Sanitize(need));
            if (need < 0.06f) return;

            // Staff idle venues first: an unstaffed theater entertains nobody.
            for (int j = 1; j < _idleSlotsByJob.Length; j++)
                if (_idleSlotsByJob[j] > 0) VoteJob(s, civ, (JobType)j, MathUtil.Clamp(0.25f + 0.5f * Math.Max(need, d.Risk), 0f, 0.8f));

            float gapAmount = Math.Max(0.15f, target - d.EntertainmentProjected);
            float k = CoveragePerPoint(civ, s, ServiceType.Entertainment, pop);
            int best = PickBest(ctx, civ, s, _catalog.Culture, CandidateKind.Culture, k, gapAmount, d.Urgency, aggression, piety, mercantilism,
                out float bestScore, out int second, out float secondScore);

            bool usesEntertainers = false;
            if (best >= 0)
            {
                float strength = 0.15f + 0.75f * need;
                if (d.Urgent && !otherCause) strength = Math.Max(strength, 0.9f);
                if (_idleCultureSlots > 0) strength *= 0.6f;
                string reason = null;
                if (WillLog(civ, strength)) reason = CultureReason(s, d, pop);
                VoteBuild(s, civ, (BuildingType)best, strength, reason);
                if (second >= 0 && secondScore >= 0.75f * bestScore && bestScore > 0f) VoteBuild(s, civ, (BuildingType)second, strength * 0.35f);
                usesEntertainers = ctx.Db.TryGetBuilding((BuildingType)best, out var bd) && bd.Job == JobType.Entertainer;
            }

            // High unrest: get performers on the streets (staff venues existing, coming or proposed).
            if (d.Risk >= 0.3f && satiation < 0.95f)
            {
                float venue = _entertainerVenues > 0 || _pendingEntertainerVenues > 0 || usesEntertainers ? 1f : 0.35f;
                VoteJob(s, civ, JobType.Entertainer, (0.3f + 0.6f * d.Risk) * venue);
            }
        }

        private void VoteFaith(SimContext ctx, Civilization civ, Settlement s, int pop, HappinessDiagnosis d, float piety)
        {
            float grief = MathUtil.SmoothStep(0f, 0.12f, d.DeathShare);
            if (d.Risk < 0.15f && d.Cause != HappinessCause.Faith && grief < 0.2f) return;
            float target = 0.6f + 0.2f * piety;
            float gap = MathUtil.Clamp01((target - d.FaithProjected) / target);
            float satiation = MathUtil.SmoothStep(0.85f, 1.3f, d.FaithProjected);
            float war = d.AtWar ? 0.3f * piety : 0f;
            float need = Math.Max(gap * (0.4f + 0.6f * piety), Math.Max(grief * 0.7f, war)) * (1f - satiation);
            need = MathUtil.Clamp01(MathUtil.Sanitize(need));
            if (need < 0.1f) return;
            float k = CoveragePerPoint(civ, s, ServiceType.Faith, pop);
            int best = PickBest(ctx, civ, s, _catalog.Faith, CandidateKind.Faith, k, Math.Max(0.15f, target - d.FaithProjected), d.Urgency, 0.5f, piety, 0.5f,
                out _, out _, out _);
            if (best < 0) return;
            float strength = MathUtil.Clamp(0.1f + 0.45f * need, 0f, 0.55f);
            string reason = null;
            if (WillLog(civ, strength))
                reason = grief >= 0.2f
                    ? "Mourning " + MathUtil.RoundToInt(d.RecentDeaths) + " dead: the people seek the gods"
                    : "Little faith (" + HappinessDiagnosis.Pct(d.FaithCoverage) + "%) sours the mood";
            VoteBuild(s, civ, (BuildingType)best, strength, reason);
        }

        private void VoteGovernance(SimContext ctx, Civilization civ, Settlement s, HappinessDiagnosis d)
        {
            if (d.Remote < 0.45f || d.Risk < 0.35f) return;
            int best = PickBest(ctx, civ, s, _catalog.Admin, CandidateKind.Admin, 0f, 0f, d.Urgency, 0.5f, 0.5f, 0.5f, out _, out _, out _);
            if (best < 0) return;
            float strength = MathUtil.Clamp(0.2f + 0.35f * d.Risk, 0f, 0.55f);
            string reason = WillLog(civ, strength) ? "Far from the capital (" + MathUtil.RoundToInt(d.DistanceToCapital) + " tiles) and restless" : null;
            VoteBuild(s, civ, (BuildingType)best, strength, reason);
        }

        private static string CultureReason(Settlement s, HappinessDiagnosis d, int pop)
        {
            if (d.Urgent || (d.Rising && d.Risk >= 0.5f))
                return "Unrest " + HappinessDiagnosis.Pct(d.Unrest) + "%" + (d.Rising ? " and rising" : "") + ": " + HappinessTaste.CalmPhrase(s.Race);
            if (d.DeathShare >= 0.05f) return "Mourning " + MathUtil.RoundToInt(d.RecentDeaths) + " dead: a place to gather";
            if (d.Remote >= 0.5f) return "Remote and restless: entertainment " + HappinessDiagnosis.Pct(d.EntertainmentCoverage) + "%";
            return "Entertainment " + HappinessDiagnosis.Pct(d.EntertainmentCoverage) + "% for " + pop + " people";
        }

        private bool WillLog(Civilization civ, float strength) => Math.Abs(strength) * AdvisorBoard.Weight(civ, Id) >= 0.5f;

        // =================================================================== candidate scoring (data-driven)
        private enum CandidateKind : byte { Culture, Faith, Food, Dwelling, Admin }

        private int PickBest(SimContext ctx, Civilization civ, Settlement s, List<BuildingType> list, CandidateKind kind, float k, float gap, float urgency,
            float aggression, float piety, float mercantilism, out float bestScore, out int second, out float secondScore)
        {
            int best = -1;
            second = -1;
            bestScore = secondScore = float.MinValue;
            var db = ctx.Db;
            for (int i = 0; i < list.Count; i++)
            {
                var t = list[i];
                if (!db.TryGetBuilding(t, out var def) || !Eligible(db, civ, s, def)) continue;
                float score = MathUtil.Sanitize(Score(def, kind, s.Race, k, gap, urgency, aggression, piety, mercantilism), float.MinValue);
                if (score > bestScore)
                {
                    second = best; secondScore = bestScore;
                    best = (int)t; bestScore = score;
                }
                else if (score > secondScore)
                {
                    second = (int)t; secondScore = score;
                }
            }
            if (best < 0) bestScore = 0f;
            if (second < 0) secondScore = 0f;
            return best;
        }

        private bool Eligible(GameDatabase db, Civilization civ, Settlement s, BuildingDefinition def)
        {
            if (!db.IsBuildingAvailable(civ, def.Type)) return false;
            if (def.IsWonder || def.UniquePerCivilization) return false;
            if (s.Tier < def.MinTier || s.Population < def.MinPopulation) return false;
            int t = (int)def.Type;
            if (def.MaxPerSettlement >= 0 && _existing[t] + _pending[t] >= def.MaxPerSettlement) return false;
            return true;
        }

        private float Score(BuildingDefinition def, CandidateKind kind, RaceId race, float k, float gap, float urgency, float aggression, float piety, float mercantilism)
        {
            int t = (int)def.Type;
            float cost = HappinessCatalog.CostWeight(def);
            float costFit = 1f / (1f + cost / MathUtil.Lerp(400f, 120f, urgency)); // urgent: cheap and fast wins
            int n = _existing[t] + _pending[t];
            float common = 0.35f * costFit + 0.08f * (int)def.MinAge - 0.3f * n / (n + 2f)
                - (HappinessCatalog.HasHardPlacement(def) ? 0.15f : 0f)
                - (_idleSlotsByType[t] > 0 ? 0.35f : 0f);
            switch (kind)
            {
                case CandidateKind.Culture:
                {
                    float eff = MathUtil.Clamp01(HappinessCatalog.ServicePoints(def, ServiceType.Entertainment) * k / Math.Max(0.15f, gap));
                    return eff + common + HappinessTaste.Race(race, def.Type) + HappinessTaste.Traits(def, race, aggression, piety, mercantilism);
                }
                case CandidateKind.Faith:
                {
                    float eff = MathUtil.Clamp01(HappinessCatalog.ServicePoints(def, ServiceType.Faith) * k / Math.Max(0.15f, gap));
                    float bonus = HappinessCatalog.ServicePoints(def, ServiceType.Entertainment) > 0f ? 0.1f : 0f;
                    return eff + common + bonus + 0.1f * piety;
                }
                case CandidateKind.Food:
                {
                    float rate = Math.Max(0f, HappinessCatalog.NetFoodPerWorker(def)) * Math.Max(1, def.JobSlots);
                    return rate / (rate + 0.4f) + common;
                }
                case CandidateKind.Dwelling:
                {
                    float cap = 0.5f * (def.HousingMin + def.HousingMax);
                    return cap / (cap + 4f) + common + (def.Type == BuildingType.House ? 0.2f : 0f);
                }
                default:
                {
                    float pts = HappinessCatalog.ServicePoints(def, ServiceType.Administration);
                    return pts / (pts + 2f) + common;
                }
            }
        }

        // =================================================================== helpers
        /// <summary>Coverage gained per service point: observed ratio when available, else the per-capita need (0.05/person).</summary>
        private static float CoveragePerPoint(Civilization civ, Settlement s, ServiceType svc, int pop)
        {
            int i = (int)svc;
            float output = MathUtil.Sanitize(s.ServiceOutput[i]);
            float cov = MathUtil.Sanitize(s.ServiceCoverage[i]);
            float k;
            if (output > 0.01f && cov > 0.001f) k = cov / output;
            else
            {
                float mod = civ != null && i < civ.Modifiers.Service.Length ? MathUtil.Sanitize(civ.Modifiers.Service[i], 1f) : 1f;
                k = Math.Max(0.1f, mod) / (Math.Max(1, pop) * 0.05f);
            }
            return MathUtil.Clamp(MathUtil.Sanitize(k), 0f, 10f);
        }

        private static bool AtWar(WorldState state, int civId)
        {
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w != null && w.IsActive && (w.AttackerId == civId || w.DefenderId == civId)) return true;
            }
            return false;
        }

        /// <summary>0 at the capital or within 1.5x settlement spacing, 1 beyond 5x spacing.</summary>
        private static float Remoteness(WorldState state, Civilization civ, Settlement s, RaceDefinition race, out float distance)
        {
            distance = 0f;
            if (s.IsCapital || civ.CapitalId == s.Id) return 0f;
            var cap = state.Settlements.Get(civ.CapitalId);
            if (cap == null || !cap.IsAlive) return 0f;
            float dx = s.Center.X - cap.Center.X, dy = s.Center.Y - cap.Center.Y;
            distance = (float)Math.Sqrt(dx * dx + dy * dy);
            float spacing = race != null ? MathUtil.Clamp(race.MinSettlementSpacing, 6, 40) : 14f;
            return MathUtil.Clamp01(MathUtil.SmoothStep(1.5f * spacing, 5f * spacing, distance));
        }
    }
}
