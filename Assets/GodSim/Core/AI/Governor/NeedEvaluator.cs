using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Computes a settlement's need vector (<see cref="SettlementNeeds"/>, every entry 0..1) through
    /// response curves: housing (free-housing target, homeless, growth, pending dwellings), food (reserve
    /// days and net flow), resources for upcoming costs, production chains (consumers lacking inputs),
    /// services (coverage below 0.7, weighted by race personality), storage (stock above 85% of capacity),
    /// defense (war, nearby hostile civs, strategy posture) and commerce (gold upkeep above income).
    /// Stateless between calls apart from configuration; allocation-free.
    /// </summary>
    public sealed class NeedEvaluator
    {
        // ------------------------------------------------------------------ considerations (shared curves)
        /// <summary>Housing deficit (residents) -> urgency.</summary>
        public static readonly Consideration HousingDeficit = new Consideration("housing.deficit", ResponseCurve.Linear(0f, 6f));
        /// <summary>Days of food reserve -> urgency (about 0.5 at 24 days, ~0 above 50 days).</summary>
        public static readonly Consideration FoodReserveDays = new Consideration("food.days", ResponseCurve.Logistic(0f, 60f, 9f, 0.4f, true));
        /// <summary>Food net flow as a fraction of consumption -> urgency (1 at -60%, 0 at +25%).</summary>
        public static readonly Consideration FoodNetRatio = new Consideration("food.net", ResponseCurve.Linear(0.25f, -0.6f));
        /// <summary>Service coverage -> urgency (1 when uncovered, linearly 0 at 0.7).</summary>
        public static readonly Consideration ServiceCoverage = new Consideration("service.coverage", ResponseCurve.Linear(0.7f, 0f));
        /// <summary>Population -> how much services matter (scaled into 0.3..1).</summary>
        public static readonly Consideration ServicePopulation = new Consideration("service.population", ResponseCurve.Linear(0f, 21f));
        /// <summary>Highest stockpile fill ratio -> urgency (from 85%).</summary>
        public static readonly Consideration StorageFill = new Consideration("storage.fill", ResponseCurve.Linear(0.85f, 1f));
        /// <summary>Defense current/desired ratio -> urgency.</summary>
        public static readonly Consideration DefenseRatio = new Consideration("defense.ratio", ResponseCurve.Inverse(0f, 1.2f, 1.5f));
        /// <summary>Resource shortfall fraction of demand -> urgency (concave).</summary>
        public static readonly Consideration ResourceShortfall = new Consideration("resource.shortfall", ResponseCurve.Quadratic(0f, 1f, 0.7f));
        /// <summary>Ticks of income needed to cover a shortfall -> urgency (0 if immediate, 1 after a year).</summary>
        public static readonly Consideration ResourceRefill = new Consideration("resource.refill", ResponseCurve.Linear(0f, SimTime.TicksPerYear));
        /// <summary>Gold upkeep deficit fraction -> urgency.</summary>
        public static readonly Consideration GoldDeficit = new Consideration("gold.deficit", ResponseCurve.Linear(0f, 1f));

        private static readonly RaceDefinition DefaultRace = new RaceDefinition();
        private static readonly GovernorPersonality DefaultPersonality = new GovernorPersonality();
        private readonly float[] _reserveSum = new float[Counts.Resources];
        private readonly int[] _reserveCount = new int[Counts.Resources];

        /// <summary>Settings used for housing targets and threat radius.</summary>
        public GovernorSettings Settings { get; set; }

        /// <summary>Creates an evaluator with the given (or default) settings.</summary>
        public NeedEvaluator(GovernorSettings settings = null) { Settings = settings ?? new GovernorSettings().Sanitize(); }

        /// <summary>
        /// Fills <paramref name="needs"/> for one settlement. <paramref name="candidates"/> are the buildable
        /// candidates (used for cost reserves and production-chain propagation); <paramref name="growthPerYear"/>
        /// is the smoothed population growth. Never throws on bad data.
        /// </summary>
        public void Evaluate(WorldState state, Settlement s, Civilization civ, RaceDefinition race, SettlementSurvey survey,
                             List<BuildingProfile> candidates, BuildingProfile[] profiles, float growthPerYear, SettlementNeeds needs)
            => Evaluate(state, s, civ, race, null, survey, candidates, profiles, growthPerYear, state != null ? state.Tick : 0L, needs);

        /// <summary>
        /// Full form: <paramref name="personality"/> carries the civ's current traits (from <see cref="AdvisorBoard.Trait"/>;
        /// null = neutral) and <paramref name="tick"/> is the current tick.
        /// </summary>
        public void Evaluate(WorldState state, Settlement s, Civilization civ, RaceDefinition race, GovernorPersonality personality, SettlementSurvey survey,
                             List<BuildingProfile> candidates, BuildingProfile[] profiles, float growthPerYear, long tick, SettlementNeeds needs)
        {
            if (needs == null) return;
            needs.Clear();
            if (state == null || s == null || survey == null) return;
            race = race ?? DefaultRace;
            personality = personality ?? DefaultPersonality;
            var cfg = Settings ?? new GovernorSettings();
            int focus = civ != null ? MathUtil.Clamp(MathUtil.RoundToInt(MathUtil.Sanitize(civ.Memory.ContainsKey("ai.focusLegacy") ? civ.Mem("ai.focusLegacy") : civ.Mem(GovernorKeys.Focus))), 0, 4) : 0; // focus 5 (recovery) maps to growth via ai.focusLegacy
            needs.Population = survey.Population;

            EvaluateHousing(s, survey, cfg, growthPerYear, needs);
            EvaluateFood(state, s, survey, race, needs);
            EvaluateResources(s, survey, candidates, needs);
            EvaluateChains(s, survey, profiles, needs);
            PropagateUpstream(s, candidates, needs);
            EvaluateServices(s, survey, personality, focus, needs);
            EvaluateStorage(s, survey, tick, needs);
            EvaluateDefense(state, s, civ, survey, cfg, focus, needs);
            EvaluateCommerce(s, survey, focus, needs);
        }

        // ------------------------------------------------------------------ housing
        private static void EvaluateHousing(Settlement s, SettlementSurvey survey, GovernorSettings cfg, float growthPerYear, SettlementNeeds needs)
        {
            int pop = survey.Population;
            int cap = survey.HousingCapacity;
            float free;
            int homeless;
            if (s.Memory.TryGetValue(GovernorKeys.FreeHousing, out float memFree))
            {
                // The population module tracks housing: trust its free-bed and homeless counts.
                free = Math.Max(0f, MathUtil.Sanitize(memFree));
                homeless = Math.Max(0, s.Homeless);
            }
            else
            {
                free = Math.Max(0, cap - pop);
                homeless = Math.Max(Math.Max(0, s.Homeless), pop - cap);
            }
            float target = Math.Max(cfg.MinFreeHousing, cfg.FreeHousingFraction * pop);
            float growth = Math.Max(0f, MathUtil.Sanitize(growthPerYear)) * cfg.HousingLeadYears;
            float deficit = target + growth + homeless - free - survey.PendingHousing;

            needs.Homeless = homeless;
            needs.FreeHousing = free;
            needs.PendingHousing = survey.PendingHousing;
            needs.TargetFreeHousing = target;
            needs.ProjectedGrowth = growth;
            needs.HousingDeficit = deficit;
            needs.Set(SettlementNeeds.Housing, deficit > 0f ? 0.55f + 0.45f * HousingDeficit.Score(deficit) : 0f);
        }

        // ------------------------------------------------------------------ food
        private static void EvaluateFood(WorldState state, Settlement s, SettlementSurvey survey, RaceDefinition race, SettlementNeeds needs)
        {
            int pop = survey.Population;
            var st = s.Stockpile;
            float perPerson = race.FoodPerTick > 0f ? race.FoodPerTick : 0.05f;
            float consumption = pop * perPerson;
            needs.FoodConsumption = consumption;
            if (pop <= 0 || consumption <= 0f) { needs.FoodDays = 9999f; return; }

            float stock = st[ResourceType.Food];
            float days = stock / consumption;
            if (s.Memory.TryGetValue(GovernorKeys.FoodDays, out float memDays))
                days = float.IsPositiveInfinity(memDays) ? 9999f : MathUtil.Sanitize(memDays, days);
            days = MathUtil.Clamp(days, 0f, 9999f);
            needs.FoodDays = days;

            int f = (int)ResourceType.Food;
            bool measured = st.AvgIncome[f] > 1e-5f || st.AvgExpense[f] > 1e-5f;
            float net = measured ? st.Net(ResourceType.Food) : survey.EstimatedOutput[f] - consumption;
            net = MathUtil.Sanitize(net);
            needs.FoodNet = net;
            needs.FoodNetMeasured = measured;

            float pending = survey.PendingOutput[f];
            // Anticipation: winter is coming (autumn) or yields are depressed (drought, blight) -> the reserve
            // is worth less than it looks, so plan food a little earlier instead of panicking later.
            float outlook = 1f;
            if (state.Season == Season.Autumn) outlook *= 0.8f;
            float yield = MathUtil.Sanitize(state.Weather.FoodYieldMultiplier, 1f);
            if (yield < 0.85f) outlook *= MathUtil.Clamp(yield + 0.1f, 0.5f, 1f);
            float daysNeed = FoodReserveDays.Score(days * outlook);
            if (pending > 0f && net + pending >= 0f) daysNeed *= 0.6f;           // a fix is already on the way
            float netNeed = FoodNetRatio.Score((net + 0.5f * pending) / consumption) * (measured ? 0.85f : 0.55f);
            if (days > 90f) netNeed *= 0.6f;                                    // a big reserve buys time
            float need = Math.Max(daysNeed, netNeed);
            if (st.Capacity > 0f && stock >= st.Capacity * 0.95f && net >= 0f) need = Math.Min(need, 0.1f);
            needs.Set(SettlementNeeds.ResourceSlot(ResourceType.Food), need);
        }

        // ------------------------------------------------------------------ resources for upcoming costs
        private void EvaluateResources(Settlement s, SettlementSurvey survey, List<BuildingProfile> candidates, SettlementNeeds needs)
        {
            Array.Clear(_reserveSum, 0, _reserveSum.Length);
            Array.Clear(_reserveCount, 0, _reserveCount.Length);
            if (candidates != null)
                for (int c = 0; c < candidates.Count; c++)
                {
                    var cost = candidates[c].Def.Cost;
                    if (cost == null) continue;
                    for (int i = 0; i < cost.Length; i++)
                    {
                        int r = (int)cost[i].Type;
                        float a = MathUtil.Sanitize(cost[i].Amount);
                        if (r < 0 || r >= Counts.Resources || a <= 0f) continue;
                        _reserveSum[r] += a;
                        _reserveCount[r]++;
                    }
                }
            var st = s.Stockpile;
            float tierScale = 1.5f + 0.25f * (int)s.Tier;
            for (int r = 0; r < Counts.Resources; r++)
            {
                float reserve = _reserveCount[r] > 0 ? _reserveSum[r] / _reserveCount[r] * tierScale : 0f;
                float demand = survey.Reserved[r] + reserve;
                if (st.Capacity > 0f) demand = Math.Min(demand, st.Capacity * 0.9f + survey.Reserved[r]);
                needs.ResourceDemand[r] = demand;
                if (demand < 1f) continue;
                float stock = st[(ResourceType)r];
                // Bootstrap: costs need this resource but nothing produces it yet -> get the first producer up
                // before the founding stock runs out (a woodcutter that costs wood can never be built at 0 wood).
                if (survey.Producers[r] == 0 && stock < 2f * demand)
                    needs.Raise(SettlementNeeds.FirstResource + r, stock < demand ? 0.65f : 0.5f);
                float shortfall = demand - stock;
                if (shortfall <= 0f) continue;
                float v = ResourceShortfall.Score(shortfall / demand);
                bool measured = st.AvgIncome[r] > 1e-5f || st.AvgExpense[r] > 1e-5f;
                float net = measured ? st.Net((ResourceType)r) : survey.EstimatedOutput[r];
                if (net > 1e-4f) v *= 0.2f + 0.8f * ResourceRefill.Score(shortfall / net);
                if (survey.PendingOutput[r] > 0f) v *= 0.6f;
                needs.Raise(SettlementNeeds.FirstResource + r, v * 0.8f);
            }
        }

        // ------------------------------------------------------------------ production chains
        private static void EvaluateChains(Settlement s, SettlementSurvey survey, BuildingProfile[] profiles, SettlementNeeds needs)
        {
            if (profiles == null) return;
            var st = s.Stockpile;
            for (int t = 0; t < profiles.Length && t < Counts.Buildings; t++)
            {
                var p = profiles[t];
                if (p == null || !p.HasInputs || survey.Complete[t] <= 0) continue;
                for (int r = 0; r < Counts.Resources; r++)
                {
                    float perTick = p.Input[r];
                    if (perTick <= 0f) continue;
                    float season = perTick * survey.Complete[t] * SimTime.TicksPerSeason;
                    float stock = st[(ResourceType)r];
                    if (stock >= season || st.Net((ResourceType)r) > 1e-4f || survey.PendingOutput[r] > 0f) continue;
                    float v = 0.45f + 0.35f * (1f - stock / season);
                    int slot = SettlementNeeds.FirstResource + r;
                    if (v > needs.Values[slot]) { needs.Set(slot, v); needs.ChainSource[r] = t; }
                }
            }
        }

        /// <summary>
        /// When a needed resource can only be produced by buildings that consume inputs (e.g. Tools from
        /// Metal + Wood), raises the need for those inputs that are short (two levels deep).
        /// </summary>
        private static void PropagateUpstream(Settlement s, List<BuildingProfile> candidates, SettlementNeeds needs)
        {
            if (candidates == null || candidates.Count == 0) return;
            var st = s.Stockpile;
            for (int pass = 0; pass < 2; pass++)
                for (int r = 0; r < Counts.Resources; r++)
                {
                    float nr = needs.Values[SettlementNeeds.FirstResource + r];
                    if (nr < 0.3f) continue;
                    BuildingProfile best = null;
                    bool inputFree = false;
                    for (int c = 0; c < candidates.Count; c++)
                    {
                        var p = candidates[c];
                        if (p.NetOutput[r] <= 0f) continue;
                        if (!p.HasInputs) { inputFree = true; break; }
                        if (best == null || p.NetOutput[r] > best.NetOutput[r]) best = p;
                    }
                    if (inputFree || best == null) continue;
                    for (int i = 0; i < Counts.Resources; i++)
                    {
                        if (i == r || best.Input[i] <= 0f) continue;
                        if (st[(ResourceType)i] >= best.Input[i] * SimTime.TicksPerSeason && st.Net((ResourceType)i) >= 0f) continue;
                        float v = nr * 0.7f;
                        int slot = SettlementNeeds.FirstResource + i;
                        if (v > needs.Values[slot]) { needs.Set(slot, v); needs.ChainSource[i] = (int)best.Type; }
                    }
                }
        }

        // ------------------------------------------------------------------ services
        private static void EvaluateServices(Settlement s, SettlementSurvey survey, GovernorPersonality personality, int focus, SettlementNeeds needs)
        {
            float popScale = 0.3f + 0.7f * ServicePopulation.Score(survey.Population);
            if (survey.Population <= 0) popScale = 0f;
            for (int sv = 0; sv < Counts.Services; sv++)
            {
                var type = (ServiceType)sv;
                float w = ServiceWeight(type, personality, focus);
                if (w <= 0f) continue;
                float cov = Math.Max(0f, MathUtil.Sanitize(s.ServiceCoverage[sv]));
                float v = ServiceCoverage.Score(cov);
                if (survey.PendingService[sv] > 0f) v *= 0.35f;
                needs.Set(SettlementNeeds.FirstService + sv, v * w * popScale);
            }
        }

        /// <summary>Maximum need weight of a service for a personality (Piety -> Faith, Curiosity -> Research) and strategy focus. 0 for services folded elsewhere.</summary>
        public static float ServiceWeight(ServiceType type, GovernorPersonality personality, int focus = 0)
        {
            personality = personality ?? DefaultPersonality;
            float w;
            switch (type)
            {
                case ServiceType.Faith: w = 0.25f + 0.4f * MathUtil.Clamp01(personality.Piety); if (focus == 2) w *= 1.25f; break;
                case ServiceType.Research: w = 0.25f + 0.4f * MathUtil.Clamp01(personality.Curiosity); if (focus == 1) w *= 1.25f; break;
                case ServiceType.Health: w = 0.5f; break;
                case ServiceType.Water: w = 0.55f; break;
                case ServiceType.Entertainment: w = 0.4f; break;
                case ServiceType.Administration: w = 0.4f; break;
                case ServiceType.Power: w = 0.35f; break;
                default: return 0f; // Defense -> defense need, Commerce -> gold need
            }
            return MathUtil.Clamp(w, 0f, 0.85f);
        }

        // ------------------------------------------------------------------ storage
        /// <summary>
        /// Storage need when a resource is above 85% of capacity, weighted by how useful more of it is
        /// (food up to ~120 days of consumption, other resources up to 3x their upcoming demand), damped
        /// while storage is pending or when the last storage order did not raise capacity (economy not
        /// crediting it yet), so towns never spam storehouses.
        /// </summary>
        private static void EvaluateStorage(Settlement s, SettlementSurvey survey, long tick, SettlementNeeds needs)
        {
            var st = s.Stockpile;
            float cap = st.Capacity;
            if (!(cap > 0f) || float.IsInfinity(cap)) return;
            float bestFill = 0f, bestV = 0f;
            int bestR = 0;
            for (int r = 0; r < Counts.Resources; r++)
            {
                float fill = MathUtil.Sanitize(st[(ResourceType)r] / cap);
                if (fill > bestFill && bestV <= 0f) { bestFill = fill; bestR = r; }
                if (fill < 0.85f) continue;
                float useful = r == (int)ResourceType.Food
                    ? (cap < needs.FoodConsumption * 120f ? 1f : 0.5f)
                    : (cap < 3f * Math.Max(needs.ResourceDemand[r], 40f) ? 1f : 0.5f);
                float v = (0.3f + 0.5f * StorageFill.Score(fill)) * useful;
                if (v > bestV) { bestV = v; bestFill = fill; bestR = r; }
            }
            needs.StorageFill = bestFill;
            needs.StorageResource = (ResourceType)bestR;
            if (bestV <= 0f) return;
            if (survey.PendingStorage > 0f) bestV *= 0.3f;
            if (s.Memory.TryGetValue(GovernorKeys.StorageOrderTick, out float lastTick))
            {
                long age = tick - (long)lastTick;
                if (age >= 0 && age < 2 * SimTime.TicksPerYear && cap <= s.Mem(GovernorKeys.StorageOrderCapacity) + 1f) bestV *= 0.2f;
            }
            needs.Set(SettlementNeeds.Storage, bestV);
        }

        // ------------------------------------------------------------------ defense
        private static void EvaluateDefense(WorldState state, Settlement s, Civilization civ, SettlementSurvey survey, GovernorSettings cfg, int focus, SettlementNeeds needs)
        {
            float threat = EvaluateThreat(state, s, civ, cfg.ThreatRadius, out bool atWar, out bool hostileNear, out int posture);
            int pop = survey.Population;
            threat = Math.Max(threat, MathUtil.Clamp(pop / 400f, 0f, 0.15f));
            needs.Threat = threat;
            needs.AtWar = atWar;
            needs.HostileNear = hostileNear;
            needs.Posture = posture;
            float desired = 1f + pop * (0.02f + 0.1f * threat) + 3f * threat + (atWar ? 3f : 0f);
            float current = Math.Max(survey.DefenseCapacity, Math.Max(0f, MathUtil.Sanitize(s.ServiceOutput[(int)ServiceType.Defense])) + Math.Max(0, s.Soldiers))
                            + 0.8f * survey.PendingDefense;
            needs.DefenseDesired = desired;
            needs.DefenseCurrent = current;
            float v = threat * DefenseRatio.Score(current / desired);
            if (focus == 3) v *= 1.15f;
            needs.Set(SettlementNeeds.Defense, v);
        }

        /// <summary>
        /// Threat 0..1 for a settlement: 1 when at war and an enemy settlement is near (0.85 when the war is
        /// far away), 0.65 when a contacted Hostile civ has a settlement within <paramref name="radius"/>,
        /// at least 0.4 / 0.55 for a defensive / militarist posture, and at least 60% of the civ strategy's
        /// own threat estimate. Never throws.
        /// </summary>
        public static float EvaluateThreat(WorldState state, Settlement s, Civilization civ, int radius, out bool atWar, out bool hostileNear, out int posture)
        {
            atWar = false;
            hostileNear = false;
            posture = 0;
            if (state == null || s == null || civ == null) return 0f;
            posture = MathUtil.Clamp(MathUtil.RoundToInt(MathUtil.Sanitize(civ.Mem(GovernorKeys.Posture))), 0, 2);
            var dip = state.Diplomacy;
            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var other = civs[i];
                if (other == null || other.Id == civ.Id || !other.IsAlive) continue;
                if (dip.AreAtWar(civ.Id, other.Id)) { atWar = true; break; }
            }
            bool enemyNear = false;
            long r2 = (long)radius * radius;
            var all = state.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var o = all[i];
                if (o == null || !o.IsAlive || o.CivilizationId == civ.Id || o.CivilizationId < 0) continue;
                if (o.Center.DistanceSqTo(s.Center) > r2) continue;
                if (!dip.TryGet(civ.Id, o.CivilizationId, out var rel)) continue;
                if (rel.AtWar) { enemyNear = true; hostileNear = true; }
                else if (rel.HasContact && rel.Stance <= DiplomaticStance.Hostile) hostileNear = true;
            }
            float threat = 0f;
            if (posture == 1) threat = 0.4f;
            else if (posture == 2) threat = 0.55f;
            if (hostileNear) threat = Math.Max(threat, 0.65f);
            if (atWar) threat = Math.Max(threat, enemyNear ? 1f : 0.85f);
            threat = Math.Max(threat, 0.6f * MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(GovernorKeys.CivThreat))));
            return MathUtil.Clamp01(threat);
        }

        // ------------------------------------------------------------------ commerce
        private static void EvaluateCommerce(Settlement s, SettlementSurvey survey, int focus, SettlementNeeds needs)
        {
            var st = s.Stockpile;
            float upkeep = survey.GoldUpkeep;
            float income = Math.Max(0f, MathUtil.Sanitize(st.AvgIncome[(int)ResourceType.Gold]));
            needs.GoldUpkeep = upkeep;
            needs.GoldIncome = income;
            if (upkeep <= income + 1e-5f) return;
            float v = 0.35f + 0.5f * GoldDeficit.Score((upkeep - income) / upkeep);
            if (st[ResourceType.Gold] > upkeep * 2f * SimTime.TicksPerYear) v *= 0.5f; // two years of upkeep banked
            if (focus == 4) v *= 1.15f;
            needs.Raise(SettlementNeeds.ResourceSlot(ResourceType.Gold), v);
        }
    }
}
