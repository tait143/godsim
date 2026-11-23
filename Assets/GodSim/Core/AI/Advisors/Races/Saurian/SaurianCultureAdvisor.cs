using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The Saurian cultural advisor: gives Saurian civilizations their own way of building, working and reacting.
    /// It only votes (via the Advisor Council board); the settlement governor still decides.
    ///
    /// <para><b>Culture notes.</b> Saurians are patient, pious, star-watching people of the warm swamps and
    /// coasts. Their towns stand on stilts over shallow water and are crowned by stepped temples. They take
    /// food from the water before the forest: nets, weirs and wetland paddies. Families are clutches, so a town
    /// can gain a dozen hatchlings in one season, and the eggwardens put up nests (houses) <i>before</i> the
    /// clutch hatches rather than after. Faith is solar: a shrine comes before the granary, priests are a
    /// respected caste, and the solstice is the high point of the ritual year. Astronomer-priests keep the
    /// calendars, and the Observatory is built the moment it can be. Every Saurian capital dreams of its
    /// Sun Pyramid and quietly hoards stone for it. Being cold-blooded, they sicken and sulk in cold lands
    /// and winters, and look for warm baths and fever-bark. They are slow to anger: a wrong is recorded
    /// ("race.saurian.wronged.&lt;civId&gt;") and sung at the temple, then answered by years of steady
    /// military building, not a sudden levy. The grudge lasts as long as the offender does, and only fades
    /// after a decade of peace.</para>
    ///
    /// <para>Acts only on settlements whose civilization's race is <see cref="RaceId.Saurian"/>. Deterministic
    /// (no RNG, index iteration only) and allocation-free per run after warm-up (reason strings are constants).
    /// Every building vote is gated by <see cref="GameDatabase.IsBuildingAvailable"/>.</para>
    /// </summary>
    public sealed class SaurianCultureAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in board keys and logs.</summary>
        public const string Id = "race_saurian";

        // ------------------------------------------------------------------ tuning
        /// <summary>Water/river/swamp tiles in territory at which the water identity saturates.</summary>
        public const float WaterTilesForFull = 30f;
        /// <summary>Largest territory radius scanned (tiles).</summary>
        public const int MaxScanRadius = 24;
        /// <summary>Years over which vengeance ramps from nothing to the full grievance.</summary>
        public const float VengeanceRampYears = 4f;
        /// <summary>Years after the last wrong before a grievance starts to fade.</summary>
        public const float GrudgeHoldYears = 10f;
        /// <summary>Grievance lost per year of peace once the hold has passed.</summary>
        public const float GrievanceDecayPerYear = 0.05f;
        /// <summary>Grievance added when a war is declared on the Saurians.</summary>
        public const float SeverityWar = 0.5f;
        /// <summary>Grievance added per battle fought on Saurian soil.</summary>
        public const float SeverityBattle = 0.15f;
        /// <summary>Grievance added when a Saurian settlement is captured.</summary>
        public const float SeverityCapture = 0.5f;

        // ------------------------------------------------------------------ reasons (constants: no per-tick allocation)
        private const string ReasonNets = "The waters feed the clutch; nets go out on the tide";
        private const string ReasonHarbor = "Stilted quays over the tide";
        private const string ReasonPaddies = "Paddies in the warm mud";
        private const string ReasonTotem = "A sun-stone is raised at the water's edge";
        private const string ReasonShrine = "The sun's shrine comes before the granary";
        private const string ReasonTemple = "A stepped temple for the sun-priests";
        private const string ReasonMonastery = "A cloister for the star-priests";
        private const string ReasonSolsticeApproach = "The solstice rites approach";
        private const string ReasonSolsticeUnderway = "The solstice rites are underway";
        private const string ReasonEquinox = "The equinox is reckoned from the terraces";
        private const string ReasonPyramid = "The Sun's stair must rise above the capital";
        private const string ReasonPyramidStone = "Masons hoard stone for the Sun Pyramid";
        private const string ReasonPyramidStore = "Stone-yards to hold the pyramid's blocks";
        private const string ReasonObservatory = "The heavens must be charted";
        private const string ReasonCalendar = "The calendar-keepers need a hall";
        private const string ReasonClutch = "A great clutch is due; nests must be ready before the hatching";
        private const string ReasonNesting = "Nesting season: the eggs will hatch soon";
        private const string ReasonBath = "Warm waters for cold-blooded bones";
        private const string ReasonHerbs = "Fever-bark against the chill";
        private const string ReasonBasking = "Basking fires against the cold";
        private const string ReasonGrudgeQuiet = "The wrong is remembered; spears are quietly sharpened";
        private const string ReasonGrudgeReckoning = "The patient ones prepare their reckoning";
        private const string ReasonGrievanceSung = "The grievance is sung before the sun";

        // ------------------------------------------------------------------ reusable state (no per-run allocations)
        private readonly int[] _have = new int[Counts.Buildings];
        private readonly Action<WarDeclaredEvent> _onWar;
        private readonly Action<BattleEvent> _onBattle;
        private readonly Action<SettlementCapturedEvent> _onCaptured;
        private WorldState _state;
        private EventBus _bus;

        private int _freeHousing, _pipelineHousing;
        private int _waterTiles, _openWater, _coastalTiles, _wetTiles, _landTiles;
        private float _coldTiles, _tempSum;

        /// <summary>Creates the advisor (event handlers are cached so they can be unsubscribed).</summary>
        public SaurianCultureAdvisor()
        {
            _onWar = OnWarDeclared;
            _onBattle = OnBattle;
            _onCaptured = OnCaptured;
        }

        /// <inheritdoc />
        public override string AdvisorId => Id;
        /// <inheritdoc />
        public override int TickInterval => 24;

        /// <summary>Subscribes (once per event bus) to the events that wrong a Saurian civilization.</summary>
        public override void Initialize(SimContext ctx)
        {
            if (ctx == null) return;
            _state = ctx.State;
            if (ReferenceEquals(_bus, ctx.Events)) return;
            if (_bus != null)
            {
                _bus.Unsubscribe(_onWar);
                _bus.Unsubscribe(_onBattle);
                _bus.Unsubscribe(_onCaptured);
            }
            _bus = ctx.Events;
            if (_bus == null) return;
            _bus.Subscribe(_onWar);
            _bus.Subscribe(_onBattle);
            _bus.Subscribe(_onCaptured);
        }

        // ================================================================== grievances (event side)
        private void OnWarDeclared(WarDeclaredEvent e) => RecordWrong(e.DefenderId, e.AttackerId, SeverityWar);
        private void OnBattle(BattleEvent e) => RecordWrong(e.DefenderCivId, e.AttackerCivId, SeverityBattle);
        private void OnCaptured(SettlementCapturedEvent e) => RecordWrong(e.OldCivilizationId, e.NewCivilizationId, SeverityCapture);

        private void RecordWrong(int victimId, int offenderId, float severity)
        {
            var st = _state;
            if (st == null || victimId < 0 || offenderId < 0 || victimId == offenderId) return;
            var victim = st.Civilizations.Get(victimId);
            if (victim == null || !victim.IsAlive || victim.Race != RaceId.Saurian) return;
            Remember(victim, offenderId, severity, st.Tick);
        }

        /// <summary>
        /// Records a wrong done to a Saurian civ: keeps the grudge's start tick
        /// ("race.saurian.wronged.&lt;offenderId&gt;"), refreshes the last-wrong tick and adds severity (0..1).
        /// </summary>
        public static void Remember(Civilization victim, int offenderId, float severity, long tick)
        {
            if (victim == null || offenderId < 0 || offenderId == victim.Id) return;
            string start = SaurianCultureKeys.WrongedKey(offenderId);
            if (!victim.Memory.ContainsKey(start)) victim.Memory[start] = tick;
            victim.Memory[SaurianCultureKeys.WrongedLastKey(offenderId)] = tick;
            string g = SaurianCultureKeys.GrievanceKey(offenderId);
            victim.Memory[g] = MathUtil.Clamp01(MathUtil.Sanitize(victim.Mem(g)) + MathUtil.Clamp01(MathUtil.Sanitize(severity)));
        }

        private static void Forget(Civilization civ, int offenderId)
        {
            civ.Memory.Remove(SaurianCultureKeys.WrongedKey(offenderId));
            civ.Memory.Remove(SaurianCultureKeys.WrongedLastKey(offenderId));
            civ.Memory.Remove(SaurianCultureKeys.GrievanceKey(offenderId));
        }

        // ================================================================== per settlement
        /// <inheritdoc />
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            if (civ.Race != RaceId.Saurian) return; // only Saurian civilizations carry this culture
            var db = ctx.Db;
            if (db == null || ctx.State == null || ctx.State.Map == null) return;
            _state = ctx.State;
            RaceDefinition race = (int)civ.Race < db.Races.Count ? db.Races[(int)civ.Race] : null;

            RefreshCiv(ctx, civ, race);
            Survey(ctx.State, db, s);
            ScanTerritory(ctx.State.Map, s);

            float piety = AdvisorBoard.Trait(civ, db, AITrait.Piety);
            float curiosity = AdvisorBoard.Trait(civ, db, AITrait.Curiosity);
            Season season = SimTime.SeasonOf(ctx.Tick);
            int day = SimTime.DayOfYear(ctx.Tick) % SimTime.TicksPerSeason;

            float water = AdviseWater(db, civ, s);
            float faith = AdvisePiety(db, civ, s, piety);
            float rite = AdviseRituals(db, civ, s, season, day);
            float project = AdvisePyramid(db, civ, s, race);
            AdviseStars(db, civ, s, curiosity);
            float housing = AdviseClutches(ctx, db, civ, s, race, season);
            float chill = AdviseWarmth(ctx.State, db, civ, s, season);
            float vengeance = AdviseVengeance(db, civ, s);
            AdviseFavored(db, civ, s, race);

            float u = Math.Max(Math.Max(water * 0.5f, faith), Math.Max(rite * 0.5f, project));
            u = Math.Max(u, Math.Max(housing, Math.Max(chill * 0.6f, vengeance)));
            Urgency(s, u);
        }

        // ------------------------------------------------------------------ water identity
        private float AdviseWater(GameDatabase db, Civilization civ, Settlement s)
        {
            float waterScore = MathUtil.Clamp01(_waterTiles / WaterTilesForFull);
            s.Memory[SaurianCultureKeys.Water] = waterScore;
            if (_waterTiles <= 0) return 0f;

            if (Can(db, civ, BuildingType.FishingHut))
            {
                float desired = 1f + s.Population / 6f; // a Saurian town lives off its nets
                float saturation = _have[(int)BuildingType.FishingHut] >= desired ? 0.35f : 1f;
                VoteBuild(s, civ, BuildingType.FishingHut, (0.25f + 0.75f * waterScore) * saturation, ReasonNets);
            }
            if ((_openWater > 0 || _coastalTiles > 0) && Can(db, civ, BuildingType.Harbor))
            {
                float h = _have[(int)BuildingType.Harbor] == 0 ? 0.5f + 0.5f * waterScore : 0.2f * waterScore;
                VoteBuild(s, civ, BuildingType.Harbor, h, ReasonHarbor);
            }
            VoteJob(s, civ, JobType.Fisher, 0.2f + 0.6f * waterScore);

            if (_wetTiles > 0 && Can(db, civ, BuildingType.Farm))
                VoteBuild(s, civ, BuildingType.Farm, 0.15f + 0.45f * MathUtil.Clamp01(_wetTiles / 20f), ReasonPaddies);
            // Why chase deer through the reeds when the river gives?
            if (waterScore > 0.5f && Can(db, civ, BuildingType.HuntingLodge))
                VoteBuild(s, civ, BuildingType.HuntingLodge, -0.3f * waterScore);
            return waterScore;
        }

        // ------------------------------------------------------------------ sun-temple piety
        private float AdvisePiety(GameDatabase db, Civilization civ, Settlement s, float piety)
        {
            int pop = s.Population;
            bool shrineOk = Can(db, civ, BuildingType.Shrine);
            int shrines = _have[(int)BuildingType.Shrine], temples = _have[(int)BuildingType.Temple];
            float urgency = 0f;

            if (!shrineOk && Can(db, civ, BuildingType.SpiritTotem) && _have[(int)BuildingType.SpiritTotem] == 0)
            {
                VoteBuild(s, civ, BuildingType.SpiritTotem, 0.5f + 0.4f * piety, ReasonTotem);
                urgency = 0.5f;
            }
            if (shrineOk)
            {
                float v = shrines == 0 ? 0.6f + 0.4f * piety : (pop > 30 * shrines ? 0.45f : 0f);
                VoteBuild(s, civ, BuildingType.Shrine, v, ReasonShrine);
                if (shrines == 0) urgency = Math.Max(urgency, 0.7f);
            }
            if (Can(db, civ, BuildingType.Temple))
            {
                float v = temples == 0 ? 0.5f + 0.5f * piety : (pop > 60 * temples ? 0.5f : 0f);
                VoteBuild(s, civ, BuildingType.Temple, v, ReasonTemple);
                if (temples == 0) urgency = Math.Max(urgency, 0.7f);
            }
            if (pop >= 15 && _have[(int)BuildingType.Monastery] == 0 && Can(db, civ, BuildingType.Monastery))
                VoteBuild(s, civ, BuildingType.Monastery, 0.4f + 0.4f * piety, ReasonMonastery);

            float faithGap = 1f - MathUtil.Clamp01(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Faith]));
            VoteJob(s, civ, JobType.Priest, 0.4f + 0.5f * piety + 0.1f * faithGap);
            return urgency;
        }

        // ------------------------------------------------------------------ ritual calendar
        /// <summary>Ritual intensity 0..1: the run that opens Summer (solstice ahead) is the peak; late Spring stirs.</summary>
        public static float RiteIntensity(Season season, int dayOfSeason)
        {
            if (season == Season.Summer) return dayOfSeason <= SimTime.TicksPerSeason / 2 ? 1f : 0.7f;
            if (season == Season.Spring && dayOfSeason >= SimTime.TicksPerSeason * 2 / 3) return 0.5f;
            return 0f;
        }

        private float AdviseRituals(GameDatabase db, Civilization civ, Settlement s, Season season, int day)
        {
            float rite = RiteIntensity(season, day);
            if (rite > 0f)
            {
                string reason = season == Season.Summer && day > SimTime.TicksPerSeason / 2 ? ReasonSolsticeUnderway : ReasonSolsticeApproach;
                BuildingType ritual = Can(db, civ, BuildingType.Temple) ? BuildingType.Temple
                    : Can(db, civ, BuildingType.Shrine) ? BuildingType.Shrine : BuildingType.SpiritTotem;
                if (Can(db, civ, ritual))
                    VoteBuild(s, civ, ritual, 0.6f * rite * (_have[(int)ritual] > 0 ? 0.6f : 1f), reason);
                VoteJob(s, civ, JobType.Priest, 0.5f * rite);
                VoteJob(s, civ, JobType.Scholar, 0.2f * rite); // astronomers time the solstice
            }
            else if ((season == Season.Spring || season == Season.Autumn) && day < SimTime.TicksPerSeason / 3)
            {
                if (Can(db, civ, BuildingType.Observatory) && _have[(int)BuildingType.Observatory] == 0)
                    VoteBuild(s, civ, BuildingType.Observatory, 0.2f, ReasonEquinox);
                VoteJob(s, civ, JobType.Scholar, 0.2f);
            }
            return rite;
        }

        // ------------------------------------------------------------------ the Sun Pyramid (capital project)
        private float AdvisePyramid(GameDatabase db, Civilization civ, Settlement s, RaceDefinition race)
        {
            BuildingType project = ProjectOf(race);
            if (!db.TryGetBuilding(project, out var def)) return 0f;
            bool present = civ.Mem(SaurianCultureKeys.PyramidPresent) > 0.5f;
            if (present)
            {
                if (_have[(int)project] > 0) VoteJob(s, civ, JobType.Builder, 0.6f); // all hands to the Sun's stair
                return 0f;
            }
            if (!s.IsCapital && s.Id != civ.CapitalId) return 0f;
            if (!def.AllowedRaces.Allows(civ.Race)) return 0f;

            bool available = db.IsBuildingAvailable(civ, project);
            bool nextAge = !available && civ.Age < def.MinAge && (int)civ.Age + 1 >= (int)def.MinAge;
            if (!available && !nextAge) return 0f;
            float scale = available ? 1f : 0.4f; // the age before: masons begin to gather stone

            if (available) VoteBuild(s, civ, project, 1f, ReasonPyramid);
            float maxCost = 0f;
            var cost = def.Cost ?? ResourceAmount.None;
            for (int i = 0; i < cost.Length; i++)
            {
                float need = MathUtil.Sanitize(cost[i].Amount);
                if (need <= 0f) continue;
                if (need > maxCost) maxCost = need;
                float held = MathUtil.Sanitize(s.Stockpile.Get(cost[i].Type));
                if (held >= need) continue;
                float deficit = MathUtil.Clamp01(1f - held / need);
                BuildingType producer = BestProducer(db, civ, cost[i].Type, out JobType job);
                if (producer != BuildingType.TownCenter)
                    VoteBuild(s, civ, producer, (0.25f + 0.45f * deficit) * scale, ReasonPyramidStone);
                VoteJob(s, civ, job, (0.2f + 0.4f * deficit) * scale);
            }
            if (s.Stockpile.Capacity < maxCost)
            {
                BuildingType store = BestStorage(db, civ);
                if (store != BuildingType.TownCenter) VoteBuild(s, civ, store, 0.6f * scale, ReasonPyramidStore);
            }
            civ.Memory[SaurianCultureKeys.PyramidSaving] = available ? 1f : 0f;
            return 0.5f * scale;
        }

        private static BuildingType ProjectOf(RaceDefinition race)
        {
            if (race != null && race.UniqueBuildings != null && race.UniqueBuildings.Length > 0) return race.UniqueBuildings[0];
            return BuildingType.SunPyramid;
        }

        /// <summary>Available non-wonder building with the highest per-worker output of a resource (TownCenter = none).</summary>
        private static BuildingType BestProducer(GameDatabase db, Civilization civ, ResourceType res, out JobType job)
        {
            BuildingType best = BuildingType.TownCenter;
            float bestAmount = 0f;
            job = JobType.None;
            var defs = db.Buildings;
            for (int t = 0; t < defs.Count; t++)
            {
                var d = defs[t];
                if (d == null || d.IsWonder || d.Category == BuildingCategory.Wonder || !d.HasJobs) continue;
                var outs = d.OutputsPerWorker;
                if (outs == null) continue;
                for (int k = 0; k < outs.Length; k++)
                {
                    if (outs[k].Type != res || !(outs[k].Amount > bestAmount)) continue;
                    if (!db.IsBuildingAvailable(civ, d.Type)) break;
                    best = d.Type; bestAmount = outs[k].Amount; job = d.Job;
                }
            }
            if (best == BuildingType.TownCenter)
            {
                // Fallback for sparse catalogs: the traditional trade of each material.
                switch (res)
                {
                    case ResourceType.Stone: best = BuildingType.StoneQuarry; job = JobType.Quarrier; break;
                    case ResourceType.Wood: best = BuildingType.WoodcutterCamp; job = JobType.Woodcutter; break;
                    case ResourceType.Metal: best = BuildingType.Smelter; job = JobType.Smith; break;
                    case ResourceType.Gold: best = BuildingType.GoldMine; job = JobType.Miner; break;
                    default: job = JobType.Laborer; break;
                }
                if (best != BuildingType.TownCenter && !db.IsBuildingAvailable(civ, best)) best = BuildingType.TownCenter;
            }
            return best;
        }

        private static BuildingType BestStorage(GameDatabase db, Civilization civ)
        {
            BuildingType best = BuildingType.TownCenter;
            float bestCap = 0f;
            var defs = db.Buildings;
            for (int t = 0; t < defs.Count; t++)
            {
                var d = defs[t];
                if (d == null || d.IsWonder || !(d.StorageCapacity > bestCap) || d.Category == BuildingCategory.Wonder) continue;
                if (!db.IsBuildingAvailable(civ, d.Type)) continue;
                best = d.Type; bestCap = d.StorageCapacity;
            }
            if (best == BuildingType.TownCenter && db.IsBuildingAvailable(civ, BuildingType.Storehouse)) best = BuildingType.Storehouse;
            return best;
        }

        // ------------------------------------------------------------------ star lore
        private void AdviseStars(GameDatabase db, Civilization civ, Settlement s, float curiosity)
        {
            bool observatory = Can(db, civ, BuildingType.Observatory);
            int have = _have[(int)BuildingType.Observatory];
            if (observatory && have == 0)
                VoteBuild(s, civ, BuildingType.Observatory, s.IsCapital || s.Tier >= SettlementTier.Town ? 1f : 0.6f, ReasonObservatory);
            if (!observatory)
            {
                if (Can(db, civ, BuildingType.Scriptorium) && _have[(int)BuildingType.Scriptorium] == 0)
                    VoteBuild(s, civ, BuildingType.Scriptorium, 0.35f, ReasonCalendar);
                else if (Can(db, civ, BuildingType.Library) && _have[(int)BuildingType.Library] == 0)
                    VoteBuild(s, civ, BuildingType.Library, 0.3f, ReasonCalendar);
            }
            VoteJob(s, civ, JobType.Scholar, 0.25f + 0.35f * curiosity + (have > 0 ? 0.2f : 0f));
        }

        // ------------------------------------------------------------------ clutch housing
        /// <summary>Expected eggs per clutch relative to a 5-child baseline race (1..3).</summary>
        public static float ClutchFactor(RaceDefinition race)
        {
            if (race == null) return 1f;
            float byLifetime = MathUtil.Clamp(race.MaxChildren / 5f, 1f, 3f);
            // Balance pass 2026-11-19: Saurian MaxChildren fell 8 -> 4, so the lifetime proxy no longer shows the clutch;
            // Saurians lay 1-2 eggs per birth (Population LifecycleBalance), 1.5 on average.
            return race.Id == RaceId.Saurian ? Math.Max(SaurianClutchSize, byLifetime) : byLifetime;
        }

        /// <summary>Average eggs per Saurian birth (mirrors the Population module's 1-2 egg clutch).</summary>
        public const float SaurianClutchSize = 1.5f;

        private float AdviseClutches(SimContext ctx, GameDatabase db, Civilization civ, Settlement s, RaceDefinition race, Season season)
        {
            int maxChildren = race != null ? race.MaxChildren : 5;
            int mothers = 0;
            var people = ctx.State.People;
            var ids = s.ResidentIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = people.Get(ids[i]);
                if (p == null || !p.IsAlive || p.Sex != Sex.Female || p.Stage != LifeStage.Adult) continue;
                if (p.SpouseId < 0 || p.ChildrenCount >= maxChildren) continue;
                mothers++;
            }
            float fertility = MathUtil.Clamp(MathUtil.Sanitize(race != null ? race.FertilityPerYear : 0.25f), 0f, 1f);
            float growth = MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Growth, 1f), 0f, 5f);
            float expected = mothers * fertility * growth * ClutchFactor(race);
            s.Memory[SaurianCultureKeys.ExpectedHatchlings] = MathUtil.Sanitize(expected);
            if (!Can(db, civ, BuildingType.House)) return 0f;

            float avgCap = 4f;
            if (db.TryGetBuilding(BuildingType.House, out var house) && house.HousingMax > 0)
                avgCap = Math.Max(1f, (house.HousingMin + house.HousingMax) * 0.5f);
            bool nesting = season == Season.Spring;
            float lead = nesting ? 1.25f : 1f;
            float room = _freeHousing + _pipelineHousing - Math.Max(0, s.Homeless);
            float gap = expected * lead - room;
            if (gap > 0f)
            {
                VoteBuild(s, civ, BuildingType.House, 0.35f + 0.25f * gap / avgCap, nesting ? ReasonNesting : ReasonClutch);
                return MathUtil.Clamp01(gap / (avgCap * 2f));
            }
            if (expected > 0f && room < expected * 1.5f) VoteBuild(s, civ, BuildingType.House, 0.2f);
            return 0f;
        }

        // ------------------------------------------------------------------ heat-lovers
        private float AdviseWarmth(WorldState state, GameDatabase db, Civilization civ, Settlement s, Season season)
        {
            int land = Math.Max(1, _landTiles);
            float coldLand = MathUtil.Clamp01(_coldTiles / land * 1.2f);
            float avgTemp = MathUtil.Clamp01(MathUtil.Sanitize(_tempSum / land, 0.5f));
            float coolClimate = MathUtil.Clamp01((0.45f - avgTemp) / 0.25f);
            float seasonal = season == Season.Winter ? 0.45f : season == Season.Autumn ? 0.15f : 0f;
            float weather = state.Weather != null ? MathUtil.Clamp01(-MathUtil.Sanitize(state.Weather.TemperatureOffset)) * 0.5f : 0f;
            float chill = MathUtil.Clamp01(Math.Max(coldLand, coolClimate) + seasonal + weather);
            s.Memory[SaurianCultureKeys.Chill] = chill;
            if (chill <= 0.15f) return chill;

            float misery = 0.6f + 0.4f * (1f - MathUtil.Clamp01(MathUtil.Sanitize(s.Happiness, 0.6f)));
            bool bath = Can(db, civ, BuildingType.Bathhouse);
            if (bath) VoteBuild(s, civ, BuildingType.Bathhouse, chill * misery * (_have[(int)BuildingType.Bathhouse] == 0 ? 1f : 0.4f), ReasonBath);
            if (Can(db, civ, BuildingType.Herbalist))
                VoteBuild(s, civ, BuildingType.Herbalist, 0.7f * chill * misery * (_have[(int)BuildingType.Herbalist] == 0 ? 1f : 0.4f), ReasonHerbs);
            if (!bath && Can(db, civ, BuildingType.Campfire) && _have[(int)BuildingType.Campfire] == 0)
                VoteBuild(s, civ, BuildingType.Campfire, 0.6f * chill * misery, ReasonBasking);
            VoteJob(s, civ, JobType.Healer, 0.4f * chill);
            return chill;
        }

        // ------------------------------------------------------------------ patient vengeance
        private float AdviseVengeance(GameDatabase db, Civilization civ, Settlement s)
        {
            float v = MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(SaurianCultureKeys.Vengeance)));
            if (civ.Mem(SaurianCultureKeys.FreshWound) > 0.5f)
            {
                BuildingType hall = Can(db, civ, BuildingType.Temple) ? BuildingType.Temple : BuildingType.Shrine;
                if (Can(db, civ, hall)) VoteBuild(s, civ, hall, 0.3f, ReasonGrievanceSung);
            }
            if (v <= 0.03f) return 0f;

            string reason = v < 0.35f ? ReasonGrudgeQuiet : ReasonGrudgeReckoning;
            var defs = db.Buildings;
            for (int t = 0; t < defs.Count; t++)
            {
                var d = defs[t];
                if (d == null || d.Category != BuildingCategory.Military || d.IsWonder) continue;
                if (!Can(db, civ, d.Type)) continue;
                float st = v * (d.Job == JobType.Soldier ? 0.9f : 0.45f);
                if (_have[t] > 0) st *= 0.4f;
                VoteBuild(s, civ, d.Type, st, reason);
            }
            BuildingType arms = BestProducer(db, civ, ResourceType.Weapons, out JobType armsJob);
            if (arms != BuildingType.TownCenter && _have[(int)arms] == 0) VoteBuild(s, civ, arms, 0.5f * v, reason);
            VoteJob(s, civ, JobType.Soldier, 0.15f + 0.85f * v);
            if (armsJob != JobType.None) VoteJob(s, civ, armsJob, 0.4f * v);
            return v;
        }

        // ------------------------------------------------------------------ data-driven favourites
        private void AdviseFavored(GameDatabase db, Civilization civ, Settlement s, RaceDefinition race)
        {
            if (race == null || race.FavoredBuildings == null) return;
            var fav = race.FavoredBuildings;
            for (int i = 0; i < fav.Length; i++)
            {
                var t = fav[i];
                if ((int)t >= _have.Length || _have[(int)t] > 0 || !Can(db, civ, t)) continue;
                if (db.TryGetBuilding(t, out var d) && (d.Placement & (PlacementRule.NearWater | PlacementRule.Coastal)) != 0 && _waterTiles == 0) continue;
                if (_waterTiles == 0 && (t == BuildingType.FishingHut || t == BuildingType.Harbor)) continue;
                VoteBuild(s, civ, t, 0.15f); // old custom
            }
        }

        // ================================================================== civ-level cache (once per run per civ)
        private void RefreshCiv(SimContext ctx, Civilization civ, RaceDefinition race)
        {
            if (civ.Memory.TryGetValue(SaurianCultureKeys.CivRefreshTick, out float last) && (long)last == ctx.Tick) return;
            civ.Memory[SaurianCultureKeys.CivRefreshTick] = ctx.Tick;
            var st = ctx.State;

            // Wars in which we are the defender keep the wound open (also covers saves loaded mid-war).
            var wars = st.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null || !w.IsActive || w.DefenderId != civ.Id || w.AttackerId == civ.Id || w.AttackerId < 0) continue;
                if (!civ.Memory.ContainsKey(SaurianCultureKeys.WrongedKey(w.AttackerId))) Remember(civ, w.AttackerId, SeverityWar, ctx.Tick);
                else civ.Memory[SaurianCultureKeys.WrongedLastKey(w.AttackerId)] = ctx.Tick;
            }

            float years = MathUtil.Clamp(ctx.DeltaTicks, 1, SimTime.TicksPerYear) / (float)SimTime.TicksPerYear;
            float vengeance = 0f;
            bool fresh = false;
            var civs = st.Civilizations.Items;
            for (int k = 0; k < civs.Count; k++)
            {
                var other = civs[k];
                if (other == null || other.Id == civ.Id) continue;
                if (!civ.Memory.TryGetValue(SaurianCultureKeys.WrongedKey(other.Id), out float start)) continue;
                if (!other.IsAlive) { Forget(civ, other.Id); continue; }
                start = MathUtil.Sanitize(start, ctx.Tick);
                string gk = SaurianCultureKeys.GrievanceKey(other.Id);
                float grievance = MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(gk)));
                float lastWrong = MathUtil.Sanitize(civ.Mem(SaurianCultureKeys.WrongedLastKey(other.Id), start), start);
                if ((ctx.Tick - lastWrong) / SimTime.TicksPerYear > GrudgeHoldYears)
                {
                    grievance -= GrievanceDecayPerYear * years;
                    if (grievance <= 0.02f) { Forget(civ, other.Id); continue; }
                    civ.Memory[gk] = grievance;
                }
                float sinceStart = Math.Max(0f, (ctx.Tick - start) / SimTime.TicksPerYear);
                if (sinceStart < 1f) fresh = true;
                float v = grievance * MathUtil.Clamp01(sinceStart / VengeanceRampYears);
                if (v > vengeance) vengeance = v;
            }
            civ.Memory[SaurianCultureKeys.Vengeance] = MathUtil.Clamp01(vengeance);
            civ.Memory[SaurianCultureKeys.FreshWound] = fresh ? 1f : 0f;

            civ.Memory[SaurianCultureKeys.PyramidPresent] = ProjectPresent(st, civ, ProjectOf(race)) ? 1f : 0f;
            civ.Memory[SaurianCultureKeys.PyramidSaving] = 0f; // set again by the capital pass while saving

            float hab = 1f;
            if (race != null && race.BiomeHabitability != null && race.BiomeHabitability.Length > (int)Biome.Beach)
                hab = Math.Max(race.BiomeHabitability[(int)Biome.Swamp], race.BiomeHabitability[(int)Biome.Beach]);
            civ.Memory[SaurianCultureKeys.SettleWaterWeight] = 1f + 0.5f * MathUtil.Clamp01(MathUtil.Sanitize(hab));
        }

        private static bool ProjectPresent(WorldState st, Civilization civ, BuildingType project)
        {
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = st.Settlements.Get(ids[i]);
                if (s == null || !s.IsAlive) continue;
                var q = s.BuildQueue;
                for (int k = 0; k < q.Count; k++) if (q[k] != null && q[k].Type == project) return true;
                var b = s.BuildingIds;
                for (int k = 0; k < b.Count; k++)
                {
                    var bld = st.Buildings.Get(b[k]);
                    if (bld != null && !bld.IsDestroyed && bld.Type == project) return true;
                }
            }
            return false;
        }

        // ================================================================== surveys
        private bool Can(GameDatabase db, Civilization civ, BuildingType t)
        {
            if (!db.IsBuildingAvailable(civ, t) || !db.TryGetBuilding(t, out var d)) return false;
            return d.MaxPerSettlement <= 0 || _have[(int)t] < d.MaxPerSettlement;
        }

        private void Survey(WorldState st, GameDatabase db, Settlement s)
        {
            Array.Clear(_have, 0, _have.Length);
            _freeHousing = 0;
            _pipelineHousing = 0;
            int avgHouse = 4;
            if (db.TryGetBuilding(BuildingType.House, out var house) && house.HousingMax > 0) avgHouse = Math.Max(1, (house.HousingMin + house.HousingMax) / 2);

            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = st.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= _have.Length) continue;
                _have[t]++;
                if (!db.TryGetBuilding(b.Type, out var def) || !def.IsDwelling) continue;
                if (b.IsComplete) _freeHousing += b.FreeHousing;
                else _pipelineHousing += b.HousingCapacity > 0 ? b.HousingCapacity : avgHouse;
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                var o = q[i];
                if (o == null) continue;
                int t = (int)o.Type;
                if (t < 0 || t >= _have.Length) continue;
                _have[t]++;
                if (db.TryGetBuilding(o.Type, out var def) && def.IsDwelling) _pipelineHousing += avgHouse;
            }
        }

        private void ScanTerritory(WorldMap map, Settlement s)
        {
            _waterTiles = _openWater = _coastalTiles = _wetTiles = _landTiles = 0;
            _coldTiles = _tempSum = 0f;
            int r = MathUtil.Clamp(s.TerritoryRadius, 2, MaxScanRadius), r2 = r * r;
            int cx = s.Center.X, cy = s.Center.Y;
            for (int y = cy - r; y <= cy + r; y++)
            {
                for (int x = cx - r; x <= cx + r; x++)
                {
                    if (!map.InBounds(x, y)) continue;
                    int dx = x - cx, dy = y - cy;
                    if (dx * dx + dy * dy > r2) continue;
                    int i = map.Index(x, y);
                    int owner = map.Owner[i];
                    if (owner >= 0 && owner != s.Id) continue; // a neighbour's land is not ours to fish
                    Biome b = map.Biomes[i];
                    if (b.IsWater()) { _waterTiles++; _openWater++; continue; }
                    _landTiles++;
                    _tempSum += MathUtil.Clamp01(MathUtil.Sanitize(map.Temperature[i], 0.5f));
                    TileFlags f = map.Flags[i];
                    if ((f & TileFlags.River) != 0 || b == Biome.Swamp) { _waterTiles++; _wetTiles++; }
                    if ((f & TileFlags.Coastal) != 0) _coastalTiles++;
                    if (b == Biome.Taiga || b == Biome.Tundra || b == Biome.Snow) _coldTiles += 1f;
                    else if (b == Biome.Mountain) _coldTiles += 0.5f;
                }
            }
        }
    }
}
