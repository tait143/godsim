using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Public-health advisor: prevents epidemics before they happen.
    ///
    /// Every season (24 ticks) it scores each settlement's epidemic risk with <see cref="HealthRiskModel"/>
    /// (crowding, town size, ailing/frail residents, race disease resistance, epidemic memory, plague and disasters
    /// nearby, sick neighbor towns, season and drought) against Water/Health coverage projected two seasons ahead.
    /// It then votes, water first, for the best available water building (Well -> Aqueduct) and healer building
    /// (Herbalist -> Bathhouse -> Hospital -> MedicalCenter), picked data-driven from BuildingDefinition.Services,
    /// MinAge/MinTier/MinPopulation/MaxPerSettlement and placement, gated by db.IsBuildingAvailable. After an
    /// outbreak (PersonDiedEvent with cause Disease) it raises an urgent, decaying spike ("3 died of fever last
    /// season"), and pushes healer jobs in understaffed clinics. No votes when coverage is already sufficient,
    /// and softened votes while a provider is already queued or under construction.
    /// </summary>
    public sealed class HealthAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in board keys and logs.</summary>
        public const string Id = "health";

        public override string AdvisorId => Id;
        public override int TickInterval => 24;

        private const float SpikeDecay = 0.55f;
        private const float HazardDecay = 0.6f;
        private const float EmaAlpha = 0.3f;
        private const float NeighborReach = 36f;
        private const float HotThreshold = 0.15f;
        private const float MaxHazard = 1.5f;

        private static readonly string[] s_diseaseNames = { "fever", "the grey wasting", "black lung", "the bloody flux", "scale-rot" };

        private readonly Action<PersonDiedEvent> _onDied;
        private readonly Action<DisasterEvent> _onDisaster;
        private readonly Action<FamineEvent> _onFamine;
        private SimContext _ctx;
        private EventBus _bus;

        private long _prepTick = long.MinValue;
        private readonly float[] _waterAmt = new float[Counts.Buildings];
        private readonly float[] _healthAmt = new float[Counts.Buildings];
        private readonly bool[] _waterCandidate = new bool[Counts.Buildings];
        private readonly bool[] _healthCandidate = new bool[Counts.Buildings];
        private readonly int[] _typeCount = new int[Counts.Buildings];
        private readonly int[] _jobVacancy = new int[Counts.Jobs];
        private readonly int[] _jobSlots = new int[Counts.Jobs];
        private readonly List<int> _hotIds = new List<int>();
        private readonly List<int> _hotCivs = new List<int>();
        private readonly List<Vec2i> _hotPos = new List<Vec2i>();
        private readonly List<float> _hotVal = new List<float>();

        /// <summary>Report of the most recently advised settlement (debug/tests; no allocation).</summary>
        public HealthRiskReport LastReport { get; private set; }

        public HealthAdvisor()
        {
            _onDied = OnPersonDied;
            _onDisaster = OnDisaster;
            _onFamine = OnFamine;
        }

        public override void Initialize(SimContext ctx)
        {
            if (_bus != null)
            {
                _bus.Unsubscribe(_onDied);
                _bus.Unsubscribe(_onDisaster);
                _bus.Unsubscribe(_onFamine);
            }
            _ctx = ctx;
            _bus = ctx != null ? ctx.Events : null;
            if (_bus != null)
            {
                _bus.Subscribe(_onDied);
                _bus.Subscribe(_onDisaster);
                _bus.Subscribe(_onFamine);
            }
            _prepTick = long.MinValue;
        }

        // ------------------------------------------------------------------ events (deterministic, synchronous)
        private void OnPersonDied(PersonDiedEvent e)
        {
            if (e.Cause != DeathCause.Disease || _ctx == null || _ctx.State == null) return;
            var s = _ctx.State.Settlements.Get(e.SettlementId);
            if (s == null || !s.IsAlive) return;
            s.Memory[HealthAdvisorKeys.PendingDeaths] = MathUtil.Clamp(MathUtil.Sanitize(s.Mem(HealthAdvisorKeys.PendingDeaths)) + 1f, 0f, 10000f);
        }

        private void OnFamine(FamineEvent e)
        {
            if (_ctx == null || _ctx.State == null) return;
            var s = _ctx.State.Settlements.Get(e.SettlementId);
            if (s == null || !s.IsAlive) return;
            AddHazard(s, HealthAdvisorKeys.InjuryHazard, 0.3f); // hunger weakens the body before the fever comes
        }

        private void OnDisaster(DisasterEvent e)
        {
            if (_ctx == null || _ctx.State == null) return;
            float sev = MathUtil.Sanitize(e.Severity);
            if (sev <= 0f) sev = 0.5f;
            sev = MathUtil.Clamp(sev, 0.1f, 2f);
            string key; float weight;
            switch (e.Type)
            {
                case DisasterType.Plague: key = HealthAdvisorKeys.Plague; weight = 1f; break;
                case DisasterType.Flood: key = HealthAdvisorKeys.WaterHazard; weight = 0.7f; break;
                case DisasterType.Drought: key = HealthAdvisorKeys.WaterHazard; weight = 0.4f; break;
                case DisasterType.Famine: key = HealthAdvisorKeys.InjuryHazard; weight = 0.4f; break;
                case DisasterType.Earthquake: case DisasterType.Volcano: case DisasterType.Meteor: key = HealthAdvisorKeys.InjuryHazard; weight = 0.5f; break;
                default: key = HealthAdvisorKeys.InjuryHazard; weight = 0.2f; break;
            }
            var items = _ctx.State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (s == null || !s.IsAlive) continue;
                float reach = Math.Max(0, e.Radius) + Math.Max(0, s.TerritoryRadius) + 6f;
                float d = Distance(e.Position, s.Center);
                if (d > reach) continue;
                float prox = MathUtil.Clamp01(1f - d / (reach + 1f));
                AddHazard(s, key, weight * sev * prox);
            }
        }

        private static void AddHazard(Settlement s, string key, float amount)
        {
            float v = MathUtil.Clamp(MathUtil.Sanitize(s.Mem(key)) + MathUtil.Sanitize(amount), 0f, MaxHazard);
            s.Memory[key] = v;
        }

        // ------------------------------------------------------------------ advising
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            if (_prepTick != ctx.Tick) Prepare(ctx);
            var db = ctx.Db;
            var mem = s.Memory;
            int pop = s.Population;

            // 1) Epidemic memory: consume event-fed deaths, update EMA and the decaying outbreak spike.
            float pend = MathUtil.Clamp(MathUtil.Sanitize(s.Mem(HealthAdvisorKeys.PendingDeaths)), 0f, 10000f);
            mem.Remove(HealthAdvisorKeys.PendingDeaths);
            float ema = MathUtil.Clamp(MathUtil.Sanitize(s.Mem(HealthAdvisorKeys.DeathEma)), 0f, 10000f) * (1f - EmaAlpha) + pend * EmaAlpha;
            float fresh = OutbreakSeverity(pend, pop);
            float decayed = MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(HealthAdvisorKeys.Spike))) * SpikeDecay;
            float spike = MathUtil.Clamp01(Math.Max(decayed, fresh) + 0.3f * Math.Min(decayed, fresh));
            if (pend >= 1f) mem[HealthAdvisorKeys.LastSeasonDeaths] = pend;
            float plague = MathUtil.Clamp(MathUtil.Sanitize(s.Mem(HealthAdvisorKeys.Plague)), 0f, MaxHazard);
            float wHaz = MathUtil.Clamp(MathUtil.Sanitize(s.Mem(HealthAdvisorKeys.WaterHazard)), 0f, MaxHazard);
            float iHaz = MathUtil.Clamp(MathUtil.Sanitize(s.Mem(HealthAdvisorKeys.InjuryHazard)), 0f, MaxHazard);

            // 2) Residents: frailty, sickness, race resistance.
            int n = 0; float healthSum = 0f, resistSum = 0f, vulnerable = 0f; int sick = 0;
            var people = ctx.State.People;
            for (int i = 0; i < s.ResidentIds.Count; i++)
            {
                var p = people.Get(s.ResidentIds[i]);
                if (p == null || !p.IsAlive) continue;
                n++;
                float h = MathUtil.Clamp01(MathUtil.Sanitize(p.Health, 1f));
                healthSum += h;
                if (h < 0.5f) sick++;
                if (p.Stage != LifeStage.Adult) vulnerable += 1f;
                if (p.HasTrait(PersonTraits.Sickly)) vulnerable += 0.5f;
                if (p.HasTrait(PersonTraits.Hardy)) vulnerable -= 0.3f;
                resistSum += RaceResistance(db, p.Race);
            }
            float settlementHealth = MathUtil.Clamp01(MathUtil.Sanitize(s.Health, 1f));
            float avgHealth = n > 0 ? 0.5f * settlementHealth + 0.5f * (healthSum / n) : settlementHealth;

            // 3) Buildings: count, pending providers, staffing of clinics and waterworks.
            Array.Clear(_typeCount, 0, _typeCount.Length);
            Array.Clear(_jobVacancy, 0, _jobVacancy.Length);
            Array.Clear(_jobSlots, 0, _jobSlots.Length);
            int bcount = 0, pendWater = 0, pendHealth = 0, idleWater = 0, idleHealth = 0;
            var buildings = ctx.State.Buildings;
            for (int i = 0; i < s.BuildingIds.Count; i++)
            {
                var b = buildings.Get(s.BuildingIds[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= _typeCount.Length) continue;
                bcount++;
                _typeCount[t]++;
                bool w = _waterAmt[t] > 0f, h = _healthAmt[t] > 0f;
                if (!w && !h) continue;
                if (!b.IsComplete) { if (w) pendWater++; if (h) pendHealth++; continue; }
                var def = db.Buildings[t];
                if (def == null || !def.HasJobs) continue;
                int j = (int)def.Job;
                if (j <= 0 || j >= _jobSlots.Length) continue;
                _jobSlots[j] += def.JobSlots;
                int vac = def.JobSlots - b.Workers.Count;
                if (vac > 0) _jobVacancy[j] += vac;
                if (b.Workers.Count == 0) { if (w) idleWater++; if (h) idleHealth++; }
            }
            for (int i = 0; i < s.BuildQueue.Count; i++)
            {
                var o = s.BuildQueue[i];
                if (o == null) continue;
                int t = (int)o.Type;
                if (t < 0 || t >= _waterAmt.Length) continue;
                if (_waterAmt[t] > 0f) pendWater++;
                if (_healthAmt[t] > 0f) pendHealth++;
            }

            // 4) Pick the best buildable providers (data-driven escalation by age).
            int waterAccess = 0;
            int waterBest = SelectProvider(db, civ, s, _waterCandidate, _waterAmt, ref waterAccess, out int waterSecond);
            int healthBest = SelectProvider(db, civ, s, _healthCandidate, _healthAmt, ref waterAccess, out int healthSecond);

            // 5) Risk model.
            float lastPop = MathUtil.Sanitize(s.Mem(HealthAdvisorKeys.LastPopulation, pop), pop);
            var input = new HealthRiskInputs
            {
                Population = pop,
                BuildingCount = bcount,
                AvgHealth = avgHealth,
                SickFraction = n > 0 ? sick / (float)n : 0f,
                VulnerableFraction = n > 0 ? MathUtil.Clamp01(vulnerable / n) : 0f,
                DiseaseResistance = n > 0 ? resistSum / n : RaceResistance(db, s.Race),
                HealthBonus = civ.Modifiers.HealthBonus,
                WaterCoverage = s.ServiceCoverage[(int)ServiceType.Water],
                HealthCoverage = s.ServiceCoverage[(int)ServiceType.Health],
                GrowthPerRun = pop - lastPop,
                DeathEma = ema,
                Spike = spike,
                Plague = plague,
                WaterHazard = wHaz,
                InjuryHazard = iHaz,
                Neighbor = NeighborPressure(s),
                Season = SimTime.SeasonOf(ctx.Tick),
                Drought = ctx.State.Weather != null && ctx.State.Weather.DroughtTicksRemaining > 0,
                WaterBuildable = waterBest >= 0,
            };
            var r = HealthRiskModel.Evaluate(in input);
            LastReport = r;

            // 6) Votes. Personality: curious, peaceful civs are prudent; warlike ones shrug off sickness.
            float prudence = MathUtil.Clamp(0.85f
                + 0.25f * AdvisorBoard.Trait(civ, db, AITrait.Curiosity)
                + 0.20f * (1f - AdvisorBoard.Trait(civ, db, AITrait.Aggression))
                - 0.10f * AdvisorBoard.Trait(civ, db, AITrait.Piety), 0.6f, 1.4f);
            float weight = AdvisorBoard.Weight(civ, Id);

            if (waterBest >= 0 && r.WaterNeed > 0.04f)
            {
                float str = r.WaterNeed * prudence;
                if (pendWater > 0) str *= 0.3f;   // already coming: don't pile up
                if (idleWater > 0) str *= 0.5f;   // staff what exists first
                str = MathUtil.Clamp01(str);
                VoteBuild(s, civ, (BuildingType)waterBest, str, str * weight >= 0.5f ? BuildReason(in r, s, true) : null);
                if (waterSecond >= 0 && str >= 0.45f) VoteBuild(s, civ, (BuildingType)waterSecond, str * 0.3f);
            }
            if (healthBest >= 0 && r.HealthNeed > 0.04f)
            {
                float str = r.HealthNeed * prudence;
                if (pendHealth > 0) str *= 0.3f;
                if (idleHealth > 0) str *= 0.5f;
                str = MathUtil.Clamp01(str);
                VoteBuild(s, civ, (BuildingType)healthBest, str, str * weight >= 0.5f ? BuildReason(in r, s, false) : null);
                if (healthSecond >= 0 && str >= 0.45f) VoteBuild(s, civ, (BuildingType)healthSecond, str * 0.3f);
            }

            // Jobs: staff understaffed clinics/waterworks; during outbreaks keep healers at their posts.
            float need = Math.Max(r.HealthNeed, r.WaterNeed);
            for (int j = 1; j < _jobVacancy.Length; j++)
            {
                if (_jobVacancy[j] <= 0) continue;
                VoteJob(s, civ, (JobType)j, MathUtil.Clamp01((0.25f + 0.6f * need + 0.5f * spike) * prudence));
            }
            int healer = (int)JobType.Healer;
            if (spike >= 0.25f && _jobSlots[healer] > 0 && _jobVacancy[healer] <= 0) VoteJob(s, civ, JobType.Healer, 0.35f * spike);

            Urgency(s, r.Urgency);

            // 7) Persist decayed state (bounded keys; near-zero entries removed).
            SetOrRemove(mem, HealthAdvisorKeys.DeathEma, ema, 0.01f);
            SetOrRemove(mem, HealthAdvisorKeys.Spike, spike, 0.01f);
            SetOrRemove(mem, HealthAdvisorKeys.Plague, plague * HazardDecay, 0.01f);
            SetOrRemove(mem, HealthAdvisorKeys.WaterHazard, wHaz * HazardDecay, 0.01f);
            SetOrRemove(mem, HealthAdvisorKeys.InjuryHazard, iHaz * HazardDecay, 0.01f);
            if (spike < 0.05f) mem.Remove(HealthAdvisorKeys.LastSeasonDeaths);
            mem[HealthAdvisorKeys.Risk] = MathUtil.Clamp01(MathUtil.Sanitize(r.Risk));
            mem[HealthAdvisorKeys.LastPopulation] = pop;
        }

        // ------------------------------------------------------------------ helpers
        private void Prepare(SimContext ctx)
        {
            _prepTick = ctx.Tick;
            var db = ctx.Db;
            for (int i = 0; i < Counts.Buildings; i++)
            {
                _waterAmt[i] = 0f; _healthAmt[i] = 0f; _waterCandidate[i] = false; _healthCandidate[i] = false;
                var def = db != null && i < db.Buildings.Count ? db.Buildings[i] : null;
                if (def == null || def.Services == null) continue;
                float total = 0f, w = 0f, h = 0f;
                for (int k = 0; k < def.Services.Length; k++)
                {
                    float a = Math.Max(0f, MathUtil.Sanitize(def.Services[k].Amount));
                    total += a;
                    if (def.Services[k].Type == ServiceType.Water) w += a;
                    else if (def.Services[k].Type == ServiceType.Health) h += a;
                }
                _waterAmt[i] = w; _healthAmt[i] = h;
                bool eligible = !def.IsWonder && !def.UniquePerCivilization;
                _waterCandidate[i] = eligible && w > 0f && w >= 0.5f * total;   // primary purpose = water
                _healthCandidate[i] = eligible && h > 0f && h >= 0.5f * total;  // primary purpose = healing
            }

            // Outbreak hot spots (order-independent: uses last run's spike and this season's raw deaths).
            _hotIds.Clear(); _hotCivs.Clear(); _hotPos.Clear(); _hotVal.Clear();
            var items = ctx.State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (s == null || !s.IsAlive) continue;
                float val = Math.Max(MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(HealthAdvisorKeys.Spike))) * SpikeDecay,
                                     OutbreakSeverity(MathUtil.Clamp(MathUtil.Sanitize(s.Mem(HealthAdvisorKeys.PendingDeaths)), 0f, 10000f), s.Population));
                val = Math.Max(val, 0.5f * MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(HealthAdvisorKeys.Plague))));
                if (val < HotThreshold) continue;
                _hotIds.Add(s.Id); _hotCivs.Add(s.CivilizationId); _hotPos.Add(s.Center); _hotVal.Add(val);
            }
        }

        private float NeighborPressure(Settlement s)
        {
            float np = 0f;
            for (int k = 0; k < _hotIds.Count; k++)
            {
                if (_hotIds[k] == s.Id) continue;
                float d = Distance(_hotPos[k], s.Center);
                if (d >= NeighborReach) continue;
                float kin = _hotCivs[k] == s.CivilizationId ? 1f : 0.6f; // own roads and trade carry sickness further
                np += (1f - d / NeighborReach) * _hotVal[k] * kin;
            }
            return MathUtil.Clamp01(np);
        }

        private int SelectProvider(GameDatabase db, Civilization civ, Settlement s, bool[] candidate, float[] amount, ref int waterAccess, out int second)
        {
            int best = -1; second = -1;
            float bestScore = float.MinValue, secondScore = float.MinValue;
            for (int i = 0; i < candidate.Length; i++)
            {
                if (!candidate[i]) continue;
                var type = (BuildingType)i;
                if (!db.IsBuildingAvailable(civ, type)) continue;
                var def = db.Buildings[i];
                if (def == null) continue;
                if (s.Tier < def.MinTier || s.Population < def.MinPopulation) continue;
                if (def.MaxPerSettlement >= 0 && _typeCount[i] >= def.MaxPerSettlement) continue;
                if (!PlacementPossible(s, def.Placement, ref waterAccess)) continue;
                float score = amount[i] + 0.01f * (int)def.MinAge; // stronger service wins; later age breaks ties
                if (score > bestScore) { second = best; secondScore = bestScore; best = i; bestScore = score; }
                else if (score > secondScore) { second = i; secondScore = score; }
            }
            return best;
        }

        /// <summary>Cheap feasibility check for water-related hard placement rules (scanned at most once per settlement per run).</summary>
        private bool PlacementPossible(Settlement s, PlacementRule rule, ref int access)
        {
            const PlacementRule waterRules = PlacementRule.NearWater | PlacementRule.NearRiver | PlacementRule.Coastal;
            if ((rule & waterRules) == 0) return true;
            if (access == 0) access = ScanWaterAccess(s);
            if ((rule & PlacementRule.NearRiver) != 0 && (access & 4) == 0) return false;
            if ((rule & PlacementRule.Coastal) != 0 && (access & 8) == 0) return false;
            if ((rule & PlacementRule.NearWater) != 0 && (access & 2) == 0) return false;
            return true;
        }

        private int ScanWaterAccess(Settlement s)
        {
            int access = 1;
            var map = _ctx != null && _ctx.State != null ? _ctx.State.Map : null;
            if (map == null) return access | 2 | 4 | 8;
            int r = MathUtil.Clamp(s.TerritoryRadius + 3, 3, 20);
            for (int y = s.Center.Y - r; y <= s.Center.Y + r; y++)
                for (int x = s.Center.X - r; x <= s.Center.X + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    if (map.IsWater(i)) access |= 2;
                    if (map.Has(i, TileFlags.River)) access |= 2 | 4;
                    if (map.Has(i, TileFlags.Coastal)) access |= 2 | 8;
                    if (access == 15) return access;
                }
            return access;
        }

        private string BuildReason(in HealthRiskReport r, Settlement s, bool water)
        {
            int last = MathUtil.RoundToInt(MathUtil.Clamp(MathUtil.Sanitize(s.Mem(HealthAdvisorKeys.LastSeasonDeaths)), 0f, 9999f));
            if (r.Spike >= 0.35f && last > 0)
                return last + " died of " + DiseaseName(s.Race) + " last season" + (water ? "; clean water first" : "; healers needed");
            if (r.Plague >= 0.3f) return "plague reported nearby";
            if (r.Neighbor >= 0.3f) return "sickness spreading from a nearby town";
            if (water && r.WaterHazard >= 0.3f) return "floodwater fouling the wells";
            if (!water && r.InjuryHazard >= 0.3f) return "the wounded and the weak after disaster";
            string what = water ? "water" : "healer";
            int pct = MathUtil.RoundToInt(MathUtil.Clamp01(water ? r.WaterCoverage : r.HealthCoverage) * 100f);
            if (r.DensityRisk >= 0.5f) return "crowded (" + MathUtil.RoundToInt(r.Density) + " per building), " + what + " coverage " + pct + "%";
            if (r.Sickness >= 0.4f) return "residents ailing, " + what + " coverage " + pct + "%";
            if (r.Growing) return "growing fast; " + what + " coverage slipping to " + pct + "%";
            if (r.RaceFactor > 1.15f) return "our people sicken easily; " + what + " coverage " + pct + "%";
            return what + " coverage only " + pct + "%";
        }

        /// <summary>Flavor name of the dreaded local disease, by race (used in explanations).</summary>
        public static string DiseaseName(RaceId race)
        {
            int i = (int)race;
            return i >= 0 && i < s_diseaseNames.Length ? s_diseaseNames[i] : s_diseaseNames[0];
        }

        /// <summary>One-line health summary for UI/debug (allocates; call on demand only).</summary>
        public static string Describe(Settlement s)
        {
            if (s == null) return "";
            int risk = MathUtil.RoundToInt(MathUtil.Clamp01(s.Mem(HealthAdvisorKeys.Risk)) * 100f);
            int urg = MathUtil.RoundToInt(AdvisorBoard.Urgency(s, Id) * 100f);
            int spike = MathUtil.RoundToInt(MathUtil.Clamp01(s.Mem(HealthAdvisorKeys.Spike)) * 100f);
            int last = MathUtil.RoundToInt(MathUtil.Clamp(s.Mem(HealthAdvisorKeys.LastSeasonDeaths), 0f, 9999f));
            string text = "Epidemic risk " + risk + "%, urgency " + urg + "%";
            if (spike > 0) text += ", outbreak alarm " + spike + "%" + (last > 0 ? " (" + last + " died of " + DiseaseName(s.Race) + ")" : "");
            if (s.Mem(HealthAdvisorKeys.Plague) >= 0.1f) text += ", plague nearby";
            return text;
        }

        private static float OutbreakSeverity(float deaths, int pop)
        {
            if (deaths <= 0f) return 0f;
            float scale = Math.Max(3f, (Math.Max(0, pop) + deaths) * 0.06f);
            return MathUtil.Clamp01(deaths / scale);
        }

        private static float RaceResistance(GameDatabase db, RaceId race)
        {
            int i = (int)race;
            if (db == null || i < 0 || i >= db.Races.Count || db.Races[i] == null) return 1f;
            return MathUtil.Clamp(MathUtil.Sanitize(db.Races[i].DiseaseResistance, 1f), 0.1f, 10f);
        }

        private static float Distance(Vec2i a, Vec2i b)
        {
            float dx = a.X - b.X, dy = a.Y - b.Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }

        private static void SetOrRemove(Dictionary<string, float> mem, string key, float v, float eps)
        {
            v = MathUtil.Sanitize(v);
            if (v < eps) mem.Remove(key); else mem[key] = v;
        }
    }
}
