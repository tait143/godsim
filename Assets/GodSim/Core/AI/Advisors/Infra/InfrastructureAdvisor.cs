using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The city engineer of the Advisor Council: keeps big, modern towns supplied with utilities.
    ///
    /// Domains (all data-driven from <see cref="BuildingDefinition"/> Services / Inputs / Outputs / Placement / MinAge,
    /// gated with <see cref="GameDatabase.IsBuildingAvailable"/> and the governor's tier / population / max rules):
    ///  * Water: Well -> Aqueduct -> Arcology vs ServiceCoverage[Water], projected population growth and droughts.
    ///    Providers are ranked by useful coverage per cost, so the ladder emerges naturally: wells for hamlets,
    ///    aqueducts for towns, arcologies for megacities.
    ///  * Power (Industrial+): only where Industry buildings exist or are planned. The benefit is quantified as
    ///    sum(industry output) x 0.25 (the economy's full-power bonus). Fuel-burning plants are checked against fuel
    ///    stock and income over a runway; without fuel the advisor prefers a fuel-free reactor, else votes the fuel
    ///    producers (CoalMine / OilWell) whose deposits exist in the territory, else discourages a cold plant.
    ///  * Administration (Forum / Arcology) as population grows, stronger for expansionist civs and restless towns.
    ///  * Transport hubs (RailStation / Airport) for Town+ settlements, driven by commerce coverage, sister towns and
    ///    mercantile personality.
    /// It anticipates (pending construction counts as coverage, growth dilutes it ahead of time), staffs before it builds
    /// (understaffed providers get job votes and damp new construction), recovers from disasters (a lost provider
    /// boosts the rebuild vote for a while) and avoids overbuilding (coverage >= 1.2 gives negative votes).
    /// Deterministic (no randomness, index iteration) and allocation-free in steady state.
    /// </summary>
    public sealed class InfrastructureAdvisor : AdvisorSystemBase
    {
        public const string Id = "infra";
        /// <summary>Settlement memory: population at the last run.</summary>
        public const string KeyLastPopulation = "ai.infra.pop";
        /// <summary>Settlement memory: smoothed population growth per tick.</summary>
        public const string KeyGrowth = "ai.infra.grow";
        /// <summary>Settlement memory: extra industry output per tick full power would give (UI/debug).</summary>
        public const string KeyPowerGain = "ai.infra.powerGain";

        private const int SlotWater = 0, SlotPower = 1, SlotAdmin = 2;
        private static readonly string[] s_countKeys = { "ai.infra.n.water", "ai.infra.n.power", "ai.infra.n.admin" };
        private static readonly string[] s_lostKeys = { "ai.infra.lost.water", "ai.infra.lost.power", "ai.infra.lost.admin" };

        private readonly InfraSettings _settings;
        private readonly InfraSurvey _survey = new InfraSurvey();
        private readonly InfraText _text = new InfraText();
        private readonly float[] _votes = new float[Counts.Buildings];
        private readonly float[] _voteMagnitude = new float[Counts.Buildings];
        private readonly string[] _reasons = new string[Counts.Buildings];
        private readonly float[] _jobVotes = new float[Counts.Jobs];
        private InfraCatalog _catalog;

        // Working context, valid only inside Advise.
        private SimContext _ctx;
        private GameDatabase _db;
        private Civilization _civ;
        private Settlement _s;
        private int _pop;
        private float _dilution;
        private float _industriousness;
        private TileFlags _terrain;
        private bool _terrainScanned;

        public InfrastructureAdvisor() : this(null) { }

        public InfrastructureAdvisor(InfraSettings settings) { _settings = settings ?? new InfraSettings(); }

        public override string AdvisorId => Id;
        public override int TickInterval => 24;
        public InfraSettings Settings => _settings;
        /// <summary>The data-driven building classification in use (null until first run).</summary>
        public InfraCatalog Catalog => _catalog;

        public override void Initialize(SimContext ctx)
        {
            if (ctx != null && ctx.Db != null) EnsureCatalog(ctx.Db);
        }

        private void EnsureCatalog(GameDatabase db)
        {
            if (_catalog != null && ReferenceEquals(_catalog.Db, db)) return;
            _catalog = new InfraCatalog(db, _settings.HubMinAge);
            _text.Clear();
        }

        // ================================================================== main
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            if (db == null) return;
            EnsureCatalog(db);
            _ctx = ctx; _db = db; _civ = civ; _s = s;
            _pop = s.Population;
            _terrainScanned = false;
            Array.Clear(_votes, 0, _votes.Length);
            Array.Clear(_voteMagnitude, 0, _voteMagnitude.Length);
            Array.Clear(_reasons, 0, _reasons.Length);
            Array.Clear(_jobVotes, 0, _jobVotes.Length);
            try
            {
                if (_pop <= 0) { Urgency(s, 0f); return; }

                _survey.Scan(ctx.State, _catalog, s, _settings.AssumedStaffing);
                float growth = UpdateGrowth(Math.Max(1, ctx.DeltaTicks));
                float projectedPop = _pop + Math.Max(0f, growth) * Math.Max(0f, MathUtil.Sanitize(_settings.LookaheadTicks));
                _dilution = MathUtil.Clamp(MathUtil.Sanitize(_pop / Math.Max(1f, projectedPop), 1f), 0.25f, 1f);
                _industriousness = AdvisorBoard.Trait(civ, db, AITrait.Industriousness);

                bool drought = ctx.State.Weather != null && ctx.State.Weather.DroughtTicksRemaining > 0;
                float waterTarget = _settings.WaterTarget + (drought ? _settings.DroughtWaterMargin : 0f);
                float uWater = AdviseUtility(ServiceType.Water, SlotWater, waterTarget, drought ? _settings.DroughtWeight : 1f, drought);

                float expansion = AdvisorBoard.Trait(civ, db, AITrait.Expansionism);
                float adminWeight = 0.8f + 0.4f * expansion + 0.3f * MathUtil.Clamp01(MathUtil.Sanitize(s.Unrest));
                float uAdmin = AdviseUtility(ServiceType.Administration, SlotAdmin, _settings.AdminTarget, adminWeight, false);

                float uPower = AdvisePower();
                float uHub = AdviseHubs();

                Emit();
                Urgency(s, Math.Max(Math.Max(uWater, uPower), Math.Max(uAdmin * 0.8f, uHub * 0.5f)));
            }
            finally
            {
                _ctx = null; _db = null; _civ = null; _s = null;
            }
        }

        private float UpdateGrowth(int dt)
        {
            float last = _s.Mem(KeyLastPopulation, -1f);
            float g = MathUtil.Sanitize(_s.Mem(KeyGrowth, 0f));
            if (last >= 0f)
            {
                float inst = MathUtil.Clamp(MathUtil.Sanitize((_pop - last) / dt), -2f, 2f);
                g += (inst - g) * MathUtil.Clamp01(MathUtil.Sanitize(_settings.GrowthSmoothing, 0.35f));
            }
            g = MathUtil.Clamp(MathUtil.Sanitize(g), -2f, 2f);
            _s.Memory[KeyLastPopulation] = _pop;
            _s.Memory[KeyGrowth] = g;
            return g;
        }

        // ================================================================== water / administration (generic utility)
        private float AdviseUtility(ServiceType svc, int slot, float target, float weight, bool drought)
        {
            var providers = _catalog.ProvidersOf[(int)svc];
            if (providers.Count == 0 || !AnyAvailable(providers)) return 0f;
            var cov = Measure(svc);
            bool lost = TrackLosses(slot, svc, cov.Effective);
            float over = Math.Max(_settings.OverCoverage, target + 0.1f);
            if (cov.Effective >= over && cov.Projected >= 1f) { VoteOverbuilt(svc, providers, cov.Effective, over); return 0f; }

            float gap = target - cov.Projected;
            if (!(gap > 0f)) return 0f;
            float severity = MathUtil.Clamp01(gap / Math.Max(0.1f, target));
            float strength = MathUtil.Clamp(_settings.BaseNeedVote + _settings.NeedVoteSlope * gap, _settings.BaseNeedVote, 1f) * weight;
            if (lost) strength += _settings.RebuildBoost;
            strength = ApplyStaffing(svc, cov, gap, MathUtil.Clamp01(strength), severity);

            int kind = lost ? InfraText.Rebuild : drought ? InfraText.Drought : cov.Effective < target ? InfraText.Low : InfraText.Soon;
            int shown = Pct(kind == InfraText.Soon ? cov.Projected : cov.Current);
            PickProvider(svc, cov, gap, strength, kind, shown);
            return MathUtil.Clamp01(severity * (lost ? 1.25f : 1f));
        }

        private void PickProvider(ServiceType svc, in InfraCoverage cov, float gap, float strength, int kind, int shown)
        {
            var providers = _catalog.ProvidersOf[(int)svc];
            int best = -1, second = -1;
            float bestScore = 0f, secondScore = 0f, bestGain = 0f, secondGain = 0f;
            for (int i = 0; i < providers.Count; i++)
            {
                int t = (int)providers[i];
                var def = _catalog.Def(t);
                if (!Eligible(t, def)) continue;
                float gain = GainOf(t, svc, cov);
                if (!(gain > 0f)) continue;
                float score = Math.Min(gain, gap + _settings.UsefulMargin) / CostFactor(def) * Aversion(def) * InputFactor(def);
                if (score > bestScore) { second = best; secondScore = bestScore; secondGain = bestGain; best = t; bestScore = score; bestGain = gain; }
                else if (score > secondScore) { second = t; secondScore = score; secondGain = gain; }
            }
            if (best < 0) return;
            Add(best, strength, _text.Get(kind, svc, _catalog.Def(best), shown, Pct(bestGain)));
            if (second >= 0 && bestGain < gap)
                Add(second, strength * _settings.SecondChoiceShare, _text.Get(kind, svc, _catalog.Def(second), shown, Pct(secondGain)));
        }

        // ================================================================== power
        private float AdvisePower()
        {
            if (_civ.Age < _settings.PowerMinAge) return 0f;
            var providers = _catalog.ProvidersOf[(int)ServiceType.Power];
            if (providers.Count == 0 || !AnyAvailable(providers)) return 0f;

            var cov = Measure(ServiceType.Power);
            bool lost = TrackLosses(SlotPower, ServiceType.Power, cov.Effective);
            int factories = _survey.IndustryCount, planned = _survey.PlannedIndustryCount;
            float bonus = Math.Max(0f, MathUtil.Sanitize(_settings.PowerBonus));
            float benefit = MathUtil.Sanitize(_survey.IndustryOutput * bonus);
            float plannedBenefit = MathUtil.Sanitize(_survey.PlannedIndustryOutput * bonus);
            _s.Memory[KeyPowerGain] = benefit;

            if (cov.Effective >= _settings.OverCoverage && cov.Projected >= 1f)
            {
                VoteOverbuilt(ServiceType.Power, providers, cov.Effective, _settings.OverCoverage);
                return 0f;
            }
            if (factories == 0 && planned == 0)
            {
                // Nothing to electrify: keep fuel-hungry plants off the governor's list.
                if (cov.Effective < 1f)
                    for (int i = 0; i < providers.Count; i++)
                    {
                        int t = (int)providers[i];
                        if (!_db.IsBuildingAvailable(_civ, providers[i])) continue;
                        Add(t, -_settings.NoIndustryPenalty, _text.Get(InfraText.NoIndustry, ServiceType.Power, _catalog.Def(t), 0, 0));
                    }
                return 0f;
            }
            float gap = _settings.PowerTarget - cov.Projected;
            if (!(gap > 0f)) return 0f;

            float reference = Math.Max(0.001f, MathUtil.Sanitize(_settings.PowerBenefitRef, 0.3f));
            float benefitNorm = MathUtil.Clamp01(benefit / reference);
            float strength;
            int kind, a, b;
            if (factories > 0) { strength = 0.5f + 0.5f * benefitNorm; kind = InfraText.PowerIdle; a = factories; b = Hundredths(benefit); }
            else { strength = 0.3f + 0.3f * MathUtil.Clamp01(plannedBenefit / reference); kind = InfraText.PowerPlanned; a = planned; b = Hundredths(plannedBenefit); }
            if (lost) strength += _settings.RebuildBoost;
            strength = MathUtil.Clamp01(strength * (0.75f + 0.5f * _industriousness));
            // The bonus is binary at coverage 1: any shortfall forfeits all of it, so severity is the full gap.
            strength = ApplyStaffing(ServiceType.Power, cov, gap, strength, MathUtil.Clamp01(gap));
            float urgency = MathUtil.Clamp01(gap) * (factories > 0 ? 0.4f + 0.6f * benefitNorm : 0.3f);
            if (lost) urgency = MathUtil.Clamp01(urgency + 0.2f);

            // Rank eligible providers separately: fuel-burning vs fuel-free.
            int fueled = -1, free = -1;
            float fueledScore = 0f, freeScore = 0f, fueledRatio = 1f;
            for (int i = 0; i < providers.Count; i++)
            {
                int t = (int)providers[i];
                var def = _catalog.Def(t);
                if (!Eligible(t, def)) continue;
                float gain = GainOf(t, ServiceType.Power, cov);
                if (!(gain > 0f)) continue;
                float score = Math.Min(gain, gap + _settings.UsefulMargin) / CostFactor(def) * Aversion(def);
                if (_catalog.NeedsInputs[t]) { if (score > fueledScore) { fueled = t; fueledScore = score; fueledRatio = InputRatio(def); } }
                else if (score > freeScore) { free = t; freeScore = score; }
            }
            if (fueled < 0 && free < 0) return urgency * 0.5f; // not yet eligible here (tier / population / limit)

            if (fueled < 0 || fueledRatio >= 0.999f)
            {
                int pick = fueled < 0 || (free >= 0 && freeScore > fueledScore) ? free : fueled;
                Add(pick, strength, _text.Get(kind, ServiceType.Power, _catalog.Def(pick), a, b));
                return urgency;
            }
            // Fuel is short for the fuel-burning option.
            if (free >= 0)
            {
                Add(free, strength, _text.Get(InfraText.FuelFree, ServiceType.Power, _catalog.Def(free), Pct(fueledRatio), 0));
                return urgency;
            }
            var plant = _catalog.Def(fueled);
            bool chain = VoteInputProducers(plant, strength);
            if (chain || fueledRatio >= 0.25f)
                Add(fueled, strength * Math.Max(0.35f, fueledRatio), _text.Get(InfraText.FuelChain, ServiceType.Power, plant, Pct(fueledRatio), 0));
            else
                Add(fueled, -_settings.NoFuelPenalty, _text.Get(InfraText.NoFuel, ServiceType.Power, plant, 0, 0));
            return Math.Max(urgency, 0.5f * (1f - fueledRatio));
        }

        /// <summary>Votes producers (CoalMine, OilWell...) of every input a plant lacks. True if a supply chain exists or was voted.</summary>
        private bool VoteInputProducers(BuildingDefinition plant, float strength)
        {
            bool chain = false;
            var inputs = plant.InputsPerWorker;
            if (inputs == null) return false;
            int slots = Math.Max(1, plant.JobSlots);
            for (int k = 0; k < inputs.Length; k++)
            {
                var res = inputs[k].Type;
                int ri = (int)res;
                if (ri < 0 || ri >= Counts.Resources) continue;
                float perDay = Math.Max(0f, MathUtil.Sanitize(inputs[k].Amount)) * slots;
                if (!(perDay > 0f)) continue;
                float ratio = ResourceRatio(res, perDay);
                if (ratio >= 0.999f) continue;

                var producers = _catalog.ProducersOf[ri];
                int best = -1, second = -1;
                float bestScore = 0f, secondScore = 0f;
                for (int i = 0; i < producers.Count; i++)
                {
                    int t = (int)producers[i];
                    var def = _catalog.Def(t);
                    if (def == null) continue;
                    if (_survey.CountWithPlanned(t) > 0) chain = true;
                    if (_survey.Existing[t] > 0 && _survey.Workers[t] < _survey.Slots[t]) VoteJobs(def.Job, 0.5f + 0.4f * (1f - ratio));
                    if (!Eligible(t, def) || !DepositAvailable(def.Placement)) continue;
                    float score = _catalog.OutputOf(t, res) * Math.Max(1, def.JobSlots) / CostFactor(def) * Aversion(def);
                    if (score > bestScore) { second = best; secondScore = bestScore; best = t; bestScore = score; }
                    else if (score > secondScore) { second = t; secondScore = score; }
                }
                float v = MathUtil.Clamp(strength * (0.6f + 0.4f * (1f - ratio)), 0.3f, 1f);
                int need = Hundredths(perDay);
                if (best >= 0)
                {
                    Add(best, v, _text.Get(InfraText.FuelProducer, ServiceType.Power, _catalog.Def(best), need, 0));
                    chain = true;
                }
                if (second >= 0) Add(second, v * _settings.SecondChoiceShare, _text.Get(InfraText.FuelProducer, ServiceType.Power, _catalog.Def(second), need, 0));
            }
            return chain;
        }

        /// <summary>Worst runway ratio (0..1) over a building's inputs: 1 = supply secured over the fuel horizon.</summary>
        private float InputRatio(BuildingDefinition def)
        {
            var inputs = def.InputsPerWorker;
            if (inputs == null || inputs.Length == 0) return 1f;
            int slots = Math.Max(1, def.JobSlots);
            float worst = 1f;
            for (int k = 0; k < inputs.Length; k++)
            {
                float perDay = Math.Max(0f, MathUtil.Sanitize(inputs[k].Amount)) * slots;
                if (!(perDay > 0f)) continue;
                worst = Math.Min(worst, ResourceRatio(inputs[k].Type, perDay));
            }
            return worst;
        }

        private float ResourceRatio(ResourceType res, float perDay)
        {
            int ri = (int)res;
            if (ri < 0 || ri >= Counts.Resources) return 1f;
            float horizon = Math.Max(1f, MathUtil.Sanitize(_settings.FuelHorizonTicks, 192f));
            var stock = _s.Stockpile;
            float avail = Math.Max(0f, MathUtil.Sanitize(stock.Get(res)))
                        + MathUtil.Sanitize(stock.Net(res)) * horizon
                        + _survey.PlannedOutput[ri] * horizon * 0.5f;
            return MathUtil.Clamp01(MathUtil.Sanitize(avail / (perDay * horizon)));
        }

        private float InputFactor(BuildingDefinition def) =>
            _catalog.NeedsInputs[(int)def.Type] ? MathUtil.Lerp(0.15f, 1f, InputRatio(def)) : 1f;

        // ================================================================== transport hubs
        private float AdviseHubs()
        {
            var hubs = _catalog.Hubs;
            if (hubs.Count == 0 || _s.Tier < _settings.HubMinTier || !AnyAvailable(hubs)) return 0f;
            var cov = Measure(ServiceType.Commerce);
            if (cov.Effective >= _settings.OverCoverage && cov.Projected >= 1f)
            {
                VoteOverbuilt(ServiceType.Commerce, hubs, cov.Effective, _settings.OverCoverage);
                return 0f;
            }
            float need = MathUtil.Clamp01(_settings.HubTarget - cov.Projected);
            int sisters = SisterSettlements();
            float merc = AdvisorBoard.Trait(_civ, _db, AITrait.Mercantilism);
            float xeno = AdvisorBoard.Trait(_civ, _db, AITrait.Xenophobia);
            float strength = (0.2f + 0.5f * need + 0.06f * Math.Min(sisters, 4)) * (0.75f + 0.5f * merc - 0.2f * xeno);
            strength = MathUtil.Clamp(MathUtil.Sanitize(strength), 0f, 0.9f);
            if (strength < 0.15f) return 0f;

            int best = -1;
            float bestScore = 0f;
            for (int i = 0; i < hubs.Count; i++)
            {
                int t = (int)hubs[i];
                var def = _catalog.Def(t);
                if (!Eligible(t, def)) continue;
                float gain = GainOf(t, ServiceType.Commerce, cov);
                if (!(gain > 0f)) continue;
                float score = Math.Min(gain, need + _settings.UsefulMargin) / CostFactor(def) * Aversion(def);
                if (score > bestScore) { best = t; bestScore = score; }
            }
            if (best < 0) return 0f;
            Add(best, strength, _text.Get(InfraText.Hub, ServiceType.Commerce, _catalog.Def(best), Pct(cov.Current), sisters));
            return need * 0.6f;
        }

        private int SisterSettlements()
        {
            int n = 0;
            var ids = _civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                if (ids[i] == _s.Id) continue;
                var o = _ctx.State.Settlements.Get(ids[i]);
                if (o != null && o.IsAlive) n++;
            }
            return n;
        }

        // ================================================================== shared helpers
        private InfraCoverage Measure(ServiceType svc)
        {
            int i = (int)svc;
            var c = new InfraCoverage();
            c.Current = MathUtil.Clamp(MathUtil.Sanitize(_s.ServiceCoverage[i]), 0f, 3f);
            float output = Math.Max(0f, MathUtil.Sanitize(_s.ServiceOutput[i]));
            float fallback = Math.Max(1f, _pop * _settings.NeedFor(svc));
            // Prefer the economy's real demand (output / coverage); fall back to per-capita needs when unknown or saturated.
            float demand = output > 0.01f && c.Current > 0.01f && c.Current < 2.95f ? output / c.Current : fallback;
            c.Demand = demand > 0.01f && !float.IsInfinity(demand) ? demand : fallback;
            float mod = i < _civ.Modifiers.Service.Length ? _civ.Modifiers.Service[i] : 1f;
            c.Modifier = MathUtil.Clamp(MathUtil.Sanitize(mod, 1f), 0.1f, 5f);
            c.Effective = MathUtil.Sanitize(c.Current + _survey.PendingPoints[i] * c.Modifier / c.Demand);
            c.Projected = c.Effective * _dilution;
            c.StaffGap = MathUtil.Sanitize(_survey.StaffGapPoints[i] * c.Modifier / c.Demand);
            return c;
        }

        private float GainOf(int type, ServiceType svc, in InfraCoverage cov) =>
            MathUtil.Sanitize(_catalog.Service(type, svc) * cov.Modifier / cov.Demand);

        /// <summary>Staff first, build second: votes provider jobs and damps construction by the recoverable share of the gap.</summary>
        private float ApplyStaffing(ServiceType svc, in InfraCoverage cov, float gap, float strength, float severity)
        {
            if (!(cov.StaffGap > 0.02f)) return strength;
            float jobVote = MathUtil.Clamp(0.3f + 0.6f * severity, 0.3f, 0.9f);
            var providers = _catalog.ProvidersOf[(int)svc];
            for (int i = 0; i < providers.Count; i++)
            {
                int t = (int)providers[i];
                var def = _catalog.Def(t);
                if (def != null && def.HasJobs && _survey.Existing[t] > 0 && _survey.Workers[t] < _survey.Slots[t]) VoteJobs(def.Job, jobVote);
            }
            float recoverable = MathUtil.Clamp01(cov.StaffGap / Math.Max(0.01f, gap));
            return strength * (1f - MathUtil.Clamp01(_settings.StaffingDamp) * recoverable);
        }

        private void VoteOverbuilt(ServiceType svc, List<BuildingType> providers, float effective, float threshold)
        {
            float v = -MathUtil.Clamp(_settings.OverVoteBase + (effective - threshold) * _settings.OverVoteSlope, _settings.OverVoteBase, _settings.OverVoteMax);
            int pct = Pct(effective);
            for (int i = 0; i < providers.Count; i++)
            {
                int t = (int)providers[i];
                if (!_db.IsBuildingAvailable(_civ, providers[i])) continue;
                float share = _catalog.ServiceShare(t, svc);
                if (share > 0f) Add(t, v * share, _text.Get(InfraText.Over, svc, _catalog.Def(t), pct, 0));
            }
        }

        /// <summary>Remembers provider counts; a drop marks a loss (disaster, fire, war) that boosts rebuilding for a while.</summary>
        private bool TrackLosses(int slot, ServiceType svc, float effective)
        {
            int n = _survey.ProviderCount[(int)svc];
            string countKey = s_countKeys[slot], lostKey = s_lostKeys[slot];
            float prev = _s.Mem(countKey, -1f);
            long tick = _ctx.Tick;
            if (prev >= 0f && n < prev) _s.Memory[lostKey] = tick;
            _s.Memory[countKey] = n;
            if (!_s.Memory.TryGetValue(lostKey, out float at)) return false;
            if (float.IsNaN(at) || tick - (long)at > _settings.RebuildMemoryTicks || tick < (long)at) { _s.Memory.Remove(lostKey); return false; }
            return effective < 1f;
        }

        private bool Eligible(int t, BuildingDefinition def)
        {
            if (def == null || def.IsWonder || def.UniquePerCivilization) return false;
            if (!_db.IsBuildingAvailable(_civ, (BuildingType)t)) return false;
            if (_s.Tier < def.MinTier || _pop < def.MinPopulation) return false;
            return def.MaxPerSettlement < 0 || _survey.CountWithPlanned(t) < def.MaxPerSettlement;
        }

        private bool AnyAvailable(List<BuildingType> types)
        {
            for (int i = 0; i < types.Count; i++) if (_db.IsBuildingAvailable(_civ, types[i])) return true;
            return false;
        }

        private float CostFactor(BuildingDefinition def)
        {
            float c = 0f;
            var cost = def.Cost;
            if (cost != null) for (int i = 0; i < cost.Length; i++) c += Math.Max(0f, MathUtil.Sanitize(cost[i].Amount));
            c += Math.Max(0f, MathUtil.Sanitize(def.BuildWork)) * 0.25f + Math.Max(0f, MathUtil.Sanitize(def.UpkeepGold)) * 200f;
            return 1f + c / Math.Max(1f, MathUtil.Sanitize(_settings.CostScale, 150f));
        }

        /// <summary>Personality: less industrious civs (elves...) shy away from smoky, unpleasant plants.</summary>
        private float Aversion(BuildingDefinition def)
        {
            float ugly = MathUtil.Clamp01(-MathUtil.Sanitize(def.Desirability));
            return 1f - 0.3f * ugly * (1f - _industriousness);
        }

        private bool DepositAvailable(PlacementRule p)
        {
            TileFlags need = TileFlags.None;
            if ((p & PlacementRule.NearCoal) != 0) need |= TileFlags.CoalDeposit;
            if ((p & PlacementRule.NearOil) != 0) need |= TileFlags.OilDeposit;
            if ((p & PlacementRule.NearOre) != 0) need |= TileFlags.OreDeposit;
            if ((p & PlacementRule.NearStone) != 0) need |= TileFlags.StoneDeposit;
            if ((p & PlacementRule.NearGold) != 0) need |= TileFlags.GoldDeposit;
            if ((p & PlacementRule.NearForest) != 0) need |= TileFlags.Trees;
            if ((p & PlacementRule.NearRiver) != 0) need |= TileFlags.River;
            if ((p & PlacementRule.Coastal) != 0) need |= TileFlags.Coastal;
            if (need == TileFlags.None) return true;
            if (!_terrainScanned) ScanTerrain();
            return (_terrain & need) == need;
        }

        private void ScanTerrain()
        {
            _terrainScanned = true;
            _terrain = TileFlags.None;
            var map = _ctx.State.Map;
            if (map == null) return;
            int r = MathUtil.Clamp(_s.TerritoryRadius + _settings.DepositSearchMargin, 1, 48);
            var c = _s.Center;
            for (int y = c.Y - r; y <= c.Y + r; y++)
                for (int x = c.X - r; x <= c.X + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    int owner = map.Owner[i];
                    if (owner >= 0 && owner != _s.Id) continue; // foreign deposits are not ours to dig
                    _terrain |= map.Flags[i];
                }
        }

        private void Add(int t, float v, string reason)
        {
            if (t < 0 || t >= _votes.Length) return;
            v = MathUtil.Sanitize(v);
            if (v == 0f) return;
            _votes[t] += v;
            float m = Math.Abs(v);
            if (m > _voteMagnitude[t]) { _voteMagnitude[t] = m; _reasons[t] = reason; }
        }

        private void VoteJobs(JobType job, float v)
        {
            int j = (int)job;
            if (job == JobType.None || j < 0 || j >= _jobVotes.Length) return;
            v = MathUtil.Clamp01(MathUtil.Sanitize(v));
            if (v > _jobVotes[j]) _jobVotes[j] = v;
        }

        private void Emit()
        {
            float min = Math.Max(0.001f, MathUtil.Sanitize(_settings.MinVote, 0.02f));
            for (int t = 0; t < _votes.Length; t++)
            {
                float v = _votes[t];
                if (!(Math.Abs(v) >= min)) continue;
                var type = (BuildingType)t;
                if (!_db.IsBuildingAvailable(_civ, type)) continue;
                VoteBuild(_s, _civ, type, MathUtil.Clamp(v, -1f, 1f), _reasons[t]);
            }
            for (int j = 1; j < _jobVotes.Length; j++)
                if (_jobVotes[j] >= min) VoteJob(_s, _civ, (JobType)j, _jobVotes[j]);
        }

        private static int Pct(float coverage) => MathUtil.RoundToInt(MathUtil.Clamp(MathUtil.Sanitize(coverage), 0f, 99.99f) * 100f);
        private static int Hundredths(float v) => MathUtil.RoundToInt(MathUtil.Clamp(MathUtil.Sanitize(v), 0f, 9999f) * 100f);
    }
}
