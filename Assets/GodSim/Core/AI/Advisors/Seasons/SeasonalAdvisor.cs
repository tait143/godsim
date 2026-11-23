using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Seasonal Advisor ("seasons"): gives every town a believable yearly rhythm and plans one season ahead.
    ///
    /// Spring  - sow the fields (farm/food jobs and crop-building votes scaled by local fertility) and use the free
    ///           labor for construction (Builder up, more with a backlog).
    /// Summer  - the building season (Builder strongly up, by Industriousness); raise storage before the harvest when
    ///           capacity cannot hold the projected winter need; arid towns secure water; cold towns start firewood.
    /// Autumn  - harvest and stockpile: food jobs maxed, Granary/Storehouse when capacity &lt; projected winter need,
    ///           winter-proof food when the larder will fall short, firewood (Woodcutter) and shelter in cold climates.
    /// Winter  - outdoor/field work down in favor of crafts, research and faith (each scaled by personality), large
    ///           non-essential projects discouraged unless their materials are already in stock; hunters and fishers
    ///           are kept working when the town is hungry; late winter prepares spring sowing.
    /// Weather - drought: water + drought-proof food, fewer new fields; storms: no exposed coastal construction,
    ///           boats stay docked; a poor yield outside winter pushes food.
    ///
    /// Everything is derived from <see cref="BuildingDefinition"/> data (<see cref="SeasonalCatalog"/>), gated by
    /// <see cref="GameDatabase.IsBuildingAvailable"/>, MaxPerSettlement, MinTier/MinPopulation and hard placement
    /// requirements versus a territory survey, so it never votes for something the town cannot build. Climate is the
    /// center biome + latitude + tile temperature (cold: Tundra/Taiga/Snow), softened by the race's habitability of it.
    /// Votes are expressed per council cycle (the base class scales them by TickInterval/24); strong build votes are
    /// explained in the AI log once per reason per season. Deterministic (no randomness, index iteration) and
    /// allocation-free per run apart from those bounded, once-per-season log lines.
    /// </summary>
    public sealed class SeasonalAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in board keys and logs.</summary>
        public const string Id = "seasons";

        /// <summary>Settlement memory: climate coldness 0..1.</summary>
        public const string ColdKey = "ai.seasons.cold";
        /// <summary>Settlement memory: projected food that must be in store when winter starts.</summary>
        public const string WinterNeedKey = "ai.seasons.winterNeed";
        /// <summary>Settlement memory: 0..1 projected winter food shortfall.</summary>
        public const string FoodGapKey = "ai.seasons.foodGap";
        /// <summary>Settlement memory: 0..1 projected firewood shortfall (cold climates).</summary>
        public const string WoodGapKey = "ai.seasons.woodGap";
        /// <summary>Settlement memory: last season the plan was announced for (season + 1).</summary>
        public const string LastSeasonKey = "ai.seasons.last";
        /// <summary>Settlement memory: bitmask of reasons already explained this season.</summary>
        public const string LoggedKey = "ai.seasons.logged";

        /// <summary>Climate coldness at which a town stockpiles firewood and fears the frost.</summary>
        public const float ColdThreshold = 0.35f;
        /// <summary>Firewood burned per person per winter day at coldness 1 (planning heuristic).</summary>
        public const float FirewoodPerPersonDay = 0.04f;
        /// <summary>Wood kept back for winter repairs/shelter.</summary>
        public const float WoodReserve = 20f;
        /// <summary>Planning estimate of the winter food-yield multiplier (weather's winter base).</summary>
        public const float WinterYieldEstimate = 0.4f;
        /// <summary>Days of food kept as a safety buffer on top of the winter deficit.</summary>
        public const float SafetyDays = 6f;

        private const int RSpringFields = 0;
        private const int RSummerStorage = 1;
        private const int RSummerWater = 2;
        private const int RAutumnStorage = 3;
        private const int RAutumnFood = 4;
        private const int RFirewood = 5;
        private const int RShelter = 6;
        private const int RWinterHold = 7;
        private const int RLateWinter = 8;
        private const int RDroughtWater = 9;
        private const int RDroughtFood = 10;
        private const int RStorm = 11;
        private const int RRecovery = 12;
        private static readonly string[] s_reasons =
        {
            "spring planting: sow fields while labor is free",
            "summer: raise storage before the autumn harvest",
            "summer heat: secure water for an arid town",
            "autumn: harvest won't fit, capacity below winter need",
            "autumn: winter larder short, food that works in winter",
            "firewood: stockpile wood before the frost",
            "cold: shelter the homeless before the frost",
            "winter: hold big projects until thaw or materials in stock",
            "late winter: ready the fields for spring",
            "drought: water before the crops fail",
            "drought: drought-proof food",
            "storm: no building on the exposed shore",
            "lean times: food that does not wait for harvest",
        };

        /// <inheritdoc />
        public override string AdvisorId => Id;
        /// <inheritdoc />
        public override int TickInterval => 6;

        private readonly SeasonalCatalog _cat = new SeasonalCatalog();
        private readonly SeasonalOutlook _o = new SeasonalOutlook();
        private readonly int[] _typeCount = new int[Counts.Buildings];
        private readonly int[] _typePending = new int[Counts.Buildings];
        private readonly int[] _jobSlots = new int[Counts.Jobs];
        private readonly int[] _jobFree = new int[Counts.Jobs];

        /// <summary>The data-driven catalog classification (built lazily from the database).</summary>
        public SeasonalCatalog Catalog => _cat;
        /// <summary>The forecast of the most recently advised settlement (debug/UI/tests).</summary>
        public SeasonalOutlook LastOutlook => _o;

        /// <inheritdoc />
        public override void Initialize(SimContext ctx)
        {
            if (ctx != null && ctx.Db != null) _cat.Build(ctx.Db);
        }

        /// <inheritdoc />
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            if (db == null || ctx.State == null || ctx.State.Map == null) return;
            if (!ReferenceEquals(_cat.Db, db)) _cat.Build(db);

            Survey(ctx, civ, s);
            var o = _o;
            AnnouncePlan(s);

            switch (o.Season)
            {
                case Season.Spring: AdviseSpring(civ, s, db); break;
                case Season.Summer: AdviseSummer(civ, s, db); break;
                case Season.Autumn: AdviseAutumn(civ, s, db); break;
                default: AdviseWinter(civ, s, db); break;
            }
            AdviseWeather(civ, s, db);
            AdviseRecovery(civ, s, db);

            s.Memory[ColdKey] = o.Cold;
            s.Memory[WinterNeedKey] = o.WinterNeed;
            s.Memory[FoodGapKey] = o.FoodGap;
            s.Memory[WoodGapKey] = o.WoodGap;
            Urgency(s, ComputeUrgency());
        }

        // =====================================================================================
        // seasons
        // =====================================================================================

        private void AdviseSpring(Civilization civ, Settlement s, GameDatabase db)
        {
            var o = _o;
            float rain = o.Raining ? 0.2f : 0f;
            VoteJobs(civ, s, _cat.JobCrop, 0.7f + rain);
            VoteJobsExcept(civ, s, _cat.JobFood, _cat.JobCrop, 0.3f);

            float pressure = Math.Max(o.FoodGap, o.Hunger);
            float fieldStrength = (0.35f + 0.5f * pressure) * (1f - 0.6f * o.Drought);
            VoteBest(civ, s, db, _cat.CropProducer, _cat.FoodScore, fieldStrength, RSpringFields);

            float builder = (0.45f + (o.Backlog > 0 ? 0.25f : 0f)) * BuildDrive();
            VoteJob(s, civ, JobType.Builder, builder);

            if (o.StorageGap > 0f && o.PrepPressure > 0f)
                VoteBest(civ, s, db, _cat.Storage, _cat.StorageScore, (0.3f + 0.5f * o.StorageGap) * o.PrepPressure, RSummerStorage);
        }

        private void AdviseSummer(Civilization civ, Settlement s, GameDatabase db)
        {
            var o = _o;
            VoteJobs(civ, s, _cat.JobCrop, 0.3f);
            VoteJobsExcept(civ, s, _cat.JobFood, _cat.JobCrop, 0.2f);
            VoteJob(s, civ, JobType.Builder, Math.Min(1f, (0.6f + (o.Backlog > 0 ? 0.2f : 0f)) * BuildDrive()));

            if (o.StorageGap > 0f)
                VoteBest(civ, s, db, _cat.Storage, _cat.StorageScore, (0.45f + 0.55f * o.StorageGap) * (0.7f + 0.3f * o.PrepPressure), RSummerStorage);

            if (o.Cold >= ColdThreshold && o.WoodGap > 0.2f && o.PrepPressure > 0.25f)
            {
                VoteJobs(civ, s, _cat.JobWood, 0.4f * o.WoodGap * o.Cold);
                VoteBest(civ, s, db, _cat.WoodProducer, _cat.WoodScore, 0.35f * o.WoodGap * o.Cold * WoodBuildFactor(), RFirewood);
            }

            float cov = WaterCoverage(s);
            if (o.Arid > 0.4f && cov < 1f)
                VoteBest(civ, s, db, _cat.WaterProvider, _cat.WaterScore, 0.25f + 0.35f * o.Arid * (1f - cov), RSummerWater);
        }

        private void AdviseAutumn(Civilization civ, Settlement s, GameDatabase db)
        {
            var o = _o;
            VoteJobs(civ, s, _cat.JobCrop, 1f);
            VoteJobsExcept(civ, s, _cat.JobFood, _cat.JobCrop, 0.8f);
            VoteJob(s, civ, JobType.Builder, o.FoodGap > 0.3f ? -0.3f : 0.15f * BuildDrive());

            if (o.StorageGap > 0f)
                VoteBest(civ, s, db, _cat.Storage, _cat.StorageScore, 0.55f + 0.45f * o.StorageGap, RAutumnStorage);
            if (o.FoodGap > 0.25f)
                VoteBest(civ, s, db, _cat.HardyFood, _cat.FoodScore, 0.25f + 0.5f * o.FoodGap, RAutumnFood);

            if (o.Cold >= ColdThreshold)
            {
                if (o.WoodGap > 0.05f) VoteJobs(civ, s, _cat.JobWood, (0.5f + 0.5f * o.WoodGap) * (0.6f + 0.4f * o.Cold));
                if (o.WoodGap > 0.25f)
                    VoteBest(civ, s, db, _cat.WoodProducer, _cat.WoodScore, (0.35f + 0.5f * o.WoodGap) * (0.5f + 0.5f * o.Cold) * WoodBuildFactor(), RFirewood);
                VoteShelter(civ, s, db, 0.3f + 0.4f * o.Cold);
            }
        }

        private void AdviseWinter(Civilization civ, Settlement s, GameDatabase db)
        {
            var o = _o;
            // How hostile the outdoors is right now: climate severity, relieved by an unusually good yield (blessings).
            float relief = MathUtil.Clamp01((o.Yield - WinterYieldEstimate) / 0.6f);
            float outdoor = o.Severity * (1f - 0.6f * relief);
            float indoor = 0.6f + 0.4f * o.Severity;
            bool cold = o.Cold >= ColdThreshold;

            for (int j = 1; j < Counts.Jobs; j++)
            {
                if (_jobSlots[j] <= 0 || j == (int)JobType.Builder) continue;
                float v = 0f;
                if (_cat.JobCrop[j]) v = -0.7f * outdoor;
                else if (_cat.JobFood[j]) v = o.Hunger > 0.2f ? 0.6f * o.Hunger : -0.2f * outdoor;
                else if (_cat.JobWood[j]) v = cold && o.WoodGap > 0.1f ? 0.3f + 0.4f * o.WoodGap : -0.35f * outdoor;
                else if (_cat.JobOutdoor[j]) v = -0.35f * outdoor;
                if (_cat.JobCraft[j]) v += 0.45f * (0.6f + 0.8f * o.Industry) * indoor;
                if (_cat.JobResearch[j]) v += 0.45f * (0.6f + 0.8f * o.Curiosity) * indoor;
                if (_cat.JobFaith[j]) v += 0.45f * (0.6f + 0.8f * o.Piety) * indoor;
                if (_cat.JobHealth[j]) v += 0.3f * (0.5f + 0.5f * o.Severity);
                if (_cat.JobCulture[j]) v += 0.25f * (0.5f + 0.5f * o.Severity);
                if (v != 0f) VoteJob(s, civ, (JobType)j, v);
            }

            bool shelterNeeded = cold && o.Homeless > 0;
            if (!shelterNeeded) VoteJob(s, civ, JobType.Builder, -0.25f * o.Severity);
            if (cold) VoteShelter(civ, s, db, 0.4f + 0.3f * o.Cold);

            // Large, non-essential projects: wait for the thaw unless every material is already in the stores.
            float hold = -(0.35f + 0.45f * o.Severity);
            for (int t = 0; t < Counts.Buildings; t++)
            {
                if (!_cat.Defined[t] || !_cat.Large[t] || _cat.Essential[t]) continue;
                if (_cat.Military[t] && o.AtWar) continue;
                var type = (BuildingType)t;
                if (!db.IsBuildingAvailable(civ, type)) continue;
                var def = db.Buildings[t];
                if (def == null || s.Stockpile.CanAfford(def.Cost, 1.25f)) continue;
                Vote(s, civ, type, hold, RWinterHold);
            }

            if (o.Progress >= 0.65f && o.Drought <= 0f)
                VoteBest(civ, s, db, _cat.CropProducer, _cat.FoodScore, 0.2f + 0.3f * Math.Max(o.FoodGap, o.Hunger), RLateWinter);
            if (o.Hunger > 0.3f)
                VoteBest(civ, s, db, _cat.HardyFood, _cat.FoodScore, 0.3f + 0.4f * o.Hunger, RRecovery);
        }

        // =====================================================================================
        // weather & recovery
        // =====================================================================================

        private void AdviseWeather(Civilization civ, Settlement s, GameDatabase db)
        {
            var o = _o;
            if (o.Drought > 0f)
            {
                float cov = WaterCoverage(s);
                float waterNeed = MathUtil.Clamp01(MathUtil.Clamp01(1.2f - cov) + 0.2f * o.Arid);
                if (waterNeed > 0.05f)
                    VoteBest(civ, s, db, _cat.WaterProvider, _cat.WaterScore, 0.4f + 0.6f * o.Drought * waterNeed, RDroughtWater);
                VoteJobsExcept(civ, s, _cat.JobFood, _cat.JobCrop, 0.5f * o.Drought);
                if (o.Season != Season.Winter) VoteJobs(civ, s, _cat.JobCrop, 0.3f * o.Drought); // water and tend what survives
                VoteBest(civ, s, db, _cat.HardyFood, _cat.FoodScore, 0.3f + 0.4f * o.Drought, RDroughtFood);
                VoteAll(civ, s, db, _cat.CropProducer, -0.3f * o.Drought); // new fields would wither
            }

            if (o.Storm >= 0.25f)
            {
                if (o.Coastal > 0)
                {
                    float v = -(0.3f + 0.7f * o.Storm);
                    for (int t = 0; t < Counts.Buildings; t++)
                    {
                        if (!_cat.Defined[t] || !_cat.Exposed[t]) continue;
                        var type = (BuildingType)t;
                        if (db.IsBuildingAvailable(civ, type)) Vote(s, civ, type, v, RStorm);
                    }
                }
                VoteJobs(civ, s, _cat.JobSea, -0.5f * o.Storm);
                VoteJob(s, civ, JobType.Builder, -0.3f * o.Storm);
            }

            if (o.Raining && o.Season != Season.Winter && o.Drought <= 0f) VoteJobs(civ, s, _cat.JobCrop, 0.15f);
        }

        private void AdviseRecovery(Civilization civ, Settlement s, GameDatabase db)
        {
            var o = _o;
            if (o.Season == Season.Winter) return; // handled by the winter plan
            bool poorYield = o.Yield < 0.7f;
            if (o.Hunger <= 0f && !poorYield) return;
            float p = Math.Max(o.Hunger, poorYield ? 0.6f * (0.7f - o.Yield) / 0.7f + 0.2f : 0f);
            VoteJobs(civ, s, _cat.JobFood, 0.4f * p);
            if (p > 0.3f) VoteBest(civ, s, db, _cat.HardyFood, _cat.FoodScore, 0.25f + 0.4f * p, RRecovery);
        }

        // =====================================================================================
        // survey & forecast
        // =====================================================================================

        private void Survey(SimContext ctx, Civilization civ, Settlement s)
        {
            var o = _o;
            var state = ctx.State;
            var db = ctx.Db;
            var w = state.Weather;
            var map = state.Map;
            long tick = state.Tick < 0 ? 0 : state.Tick;

            // ---- calendar (the weather's season is authoritative; the tick gives the position inside it)
            Season season = w != null ? w.Season : SimTime.SeasonOf(tick);
            if ((int)season > (int)Season.Winter) season = SimTime.SeasonOf(tick);
            o.Season = season;
            o.DayInSeason = SimTime.DayOfYear(tick) % SimTime.TicksPerSeason;
            o.Progress = o.DayInSeason / (float)SimTime.TicksPerSeason;
            o.TicksUntilWinter = season == Season.Winter ? 0 : ((int)Season.Winter - (int)season) * SimTime.TicksPerSeason - o.DayInSeason;
            o.WinterTicksLeft = season == Season.Winter ? SimTime.TicksPerSeason - o.DayInSeason : SimTime.TicksPerSeason;

            // ---- weather (sanitized)
            o.Yield = w != null ? MathUtil.Clamp(MathUtil.Sanitize(w.FoodYieldMultiplier, 1f), 0.05f, 3f) : 1f;
            int drought = w != null ? w.DroughtTicksRemaining : 0;
            o.Drought = drought > 0 ? MathUtil.Clamp01(0.5f + 0.5f * Math.Min(1f, drought / (float)SimTime.TicksPerSeason)) : 0f;
            o.Raining = w != null && w.RainTicksRemaining > 0;
            o.Storm = w != null ? MathUtil.Clamp01(MathUtil.Sanitize(w.StormIntensity)) : 0f;
            float tempOffset = w != null ? w.TemperatureOffset : 0f;
            float rainfall = w != null ? w.Rainfall : 1f;

            // ---- climate
            Vec2i c = map.ClampToBounds(s.Center);
            o.CenterBiome = map.Biomes[map.Index(c)];
            o.Cold = SeasonalClimate.Coldness(map, c, tempOffset);
            o.Arid = SeasonalClimate.Aridity(map, c, rainfall);
            RaceDefinition race = (int)civ.Race < db.Races.Count ? db.Races[(int)civ.Race] : null;
            o.Habitability = race != null && race.BiomeHabitability != null && (int)o.CenterBiome < race.BiomeHabitability.Length
                ? MathUtil.Clamp01(MathUtil.Sanitize(race.BiomeHabitability[(int)o.CenterBiome], 0.5f)) : 0.5f;
            o.Severity = MathUtil.Clamp01(0.35f + 0.65f * o.Cold * (1f - 0.3f * o.Habitability));
            float foodPerPerson = race != null ? MathUtil.Clamp(MathUtil.Sanitize(race.FoodPerTick, 0.05f), 0.001f, 1f) : 0.05f;

            // ---- personality
            o.Industry = AdvisorBoard.Trait(civ, db, AITrait.Industriousness);
            o.Curiosity = AdvisorBoard.Trait(civ, db, AITrait.Curiosity);
            o.Piety = AdvisorBoard.Trait(civ, db, AITrait.Piety);
            float aggression = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            o.Prudence = MathUtil.Clamp(0.5f + 0.5f * o.Industry + 0.25f * (1f - aggression), 0.5f, 1.25f);
            float window = SimTime.TicksPerSeason * (1f + o.Prudence);
            o.PrepPressure = season == Season.Winter ? 1f : 1f - MathUtil.Clamp01(o.TicksUntilWinter / window);

            o.AtWar = false;
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var war = wars[i];
                if (war != null && war.IsActive && (war.AttackerId == civ.Id || war.DefenderId == civ.Id)) { o.AtWar = true; break; }
            }

            SurveyTerritory(map, c, s.TerritoryRadius);
            SurveyBuildings(state, db, s);

            // ---- food forecast
            var st = s.Stockpile;
            o.Population = s.Population;
            o.Homeless = s.Homeless;
            float storageMul = MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Storage, 1f), 0.1f, 10f);
            float capacity = Math.Max(0f, MathUtil.Sanitize(st.Capacity));
            o.EffectiveCapacity = capacity + o.PendingStorage * storageMul;
            o.FoodStock = Math.Max(0f, MathUtil.Sanitize(st.Get(ResourceType.Food)));
            o.FoodIncome = Math.Max(0f, MathUtil.Sanitize(st.AvgIncome[(int)ResourceType.Food]));
            float expense = Math.Max(0f, MathUtil.Sanitize(st.AvgExpense[(int)ResourceType.Food]));
            o.Consumption = o.Population > 0 ? Math.Max(o.Population * foodPerPerson, expense) : expense;
            float winterYield = WinterYieldEstimate * (1f - 0.35f * o.Cold);
            o.WinterIncome = season == Season.Winter ? o.FoodIncome : o.FoodIncome * winterYield / Math.Max(0.2f, o.Yield);
            float winterLen = SimTime.TicksPerSeason * (1f + 0.25f * o.Cold); // cold winters bite early and linger
            float remaining = season == Season.Winter ? o.WinterTicksLeft * (1f + 0.25f * o.Cold) : winterLen;
            o.WinterNeed = Math.Max(0f, o.Consumption - o.WinterIncome) * remaining + o.Consumption * SafetyDays;
            o.ProjectedFood = MathUtil.Clamp(o.FoodStock + (o.FoodIncome - o.Consumption) * o.TicksUntilWinter, 0f, Math.Max(capacity, o.FoodStock));
            o.StorageGap = o.WinterNeed > o.EffectiveCapacity && o.WinterNeed > 1f ? MathUtil.Clamp01((o.WinterNeed - o.EffectiveCapacity) / o.WinterNeed) : 0f;
            o.FoodGap = o.WinterNeed > 1f ? MathUtil.Clamp01((o.WinterNeed - Math.Min(o.ProjectedFood, o.EffectiveCapacity)) / o.WinterNeed) : 0f;
            o.FoodDays = o.Consumption > 1e-3f ? MathUtil.Clamp(o.FoodStock / o.Consumption, 0f, 9999f) : 9999f;
            o.Hunger = o.Population > 0 ? MathUtil.Clamp01((15f - o.FoodDays) / 15f) : 0f;

            // ---- firewood forecast (cold climates only)
            o.WoodStock = Math.Max(0f, MathUtil.Sanitize(st.Get(ResourceType.Wood)));
            float woodNet = MathUtil.Sanitize(st.Net(ResourceType.Wood));
            if (o.Cold >= ColdThreshold && o.Population > 0)
            {
                o.WoodTarget = o.Population * FirewoodPerPersonDay * remaining * o.Cold + WoodReserve;
                o.ProjectedWood = MathUtil.Clamp(o.WoodStock + woodNet * o.TicksUntilWinter, 0f, Math.Max(capacity, o.WoodStock));
                o.WoodGap = MathUtil.Clamp01((o.WoodTarget - o.ProjectedWood) / o.WoodTarget);
            }
            else { o.WoodTarget = 0f; o.ProjectedWood = o.WoodStock; o.WoodGap = 0f; }
        }

        private void SurveyTerritory(WorldMap map, Vec2i c, int territoryRadius)
        {
            var o = _o;
            o.ResetTerritory();
            int r = MathUtil.Clamp(territoryRadius, 3, 12) + 1;
            int step = r > 8 ? 2 : 1;
            int r2 = r * r;
            float fert = 0f;
            for (int dy = -r; dy <= r; dy += step)
                for (int dx = -r; dx <= r; dx += step)
                {
                    if (dx * dx + dy * dy > r2) continue;
                    int x = c.X + dx, y = c.Y + dy;
                    if (!map.InBounds(x, y)) continue;
                    int i = map.Index(x, y);
                    Biome b = map.Biomes[i];
                    if (b.IsWater()) { o.Water++; continue; }
                    o.LandTiles++;
                    fert += MathUtil.Clamp01(MathUtil.Sanitize(map.Fertility[i]));
                    if (b == Biome.Hills) o.Hills++;
                    else if (b == Biome.Mountain) o.Mountain++;
                    var f = map.Flags[i];
                    if ((f & TileFlags.Trees) != 0) o.Trees++;
                    if ((f & TileFlags.Coastal) != 0) o.Coastal++;
                    if ((f & TileFlags.River) != 0) o.River++;
                    if ((f & TileFlags.StoneDeposit) != 0) o.Stone++;
                    if ((f & TileFlags.OreDeposit) != 0) o.Ore++;
                    if ((f & TileFlags.GoldDeposit) != 0) o.Gold++;
                    if ((f & TileFlags.CoalDeposit) != 0) o.Coal++;
                    if ((f & TileFlags.OilDeposit) != 0) o.Oil++;
                    if ((f & TileFlags.FertileSoil) != 0) fert += 0.25f;
                }
            o.Fertility = o.LandTiles > 0 ? MathUtil.Clamp01(fert / o.LandTiles) : 0f;
        }

        private void SurveyBuildings(WorldState state, GameDatabase db, Settlement s)
        {
            var o = _o;
            Array.Clear(_typeCount, 0, _typeCount.Length);
            Array.Clear(_typePending, 0, _typePending.Length);
            Array.Clear(_jobSlots, 0, _jobSlots.Length);
            Array.Clear(_jobFree, 0, _jobFree.Length);
            o.PendingStorage = 0f;
            o.Backlog = 0;
            o.WoodUnderstaffed = false;

            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= Counts.Buildings) continue;
                _typeCount[t]++;
                if (!b.IsComplete)
                {
                    _typePending[t]++;
                    o.Backlog++;
                    o.PendingStorage += _cat.StorageCapacity[t];
                    continue;
                }
                var def = db.Buildings[t];
                if (def == null || !def.HasJobs) continue;
                int j = (int)def.Job;
                if (j <= 0 || j >= Counts.Jobs) continue;
                int free = Math.Max(0, def.JobSlots - b.Workers.Count);
                _jobSlots[j] += def.JobSlots;
                _jobFree[j] += free;
                if (_cat.WoodProducer[t] && free > 0) o.WoodUnderstaffed = true;
            }

            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                var order = q[i];
                if (order == null) continue;
                int t = (int)order.Type;
                if (t < 0 || t >= Counts.Buildings) continue;
                _typePending[t]++;
                o.Backlog++;
                o.PendingStorage += _cat.StorageCapacity[t];
            }
        }

        // =====================================================================================
        // voting helpers
        // =====================================================================================

        /// <summary>Votes the best-value buildable candidate at full strength and the runner-up at half.</summary>
        private void VoteBest(Civilization civ, Settlement s, GameDatabase db, bool[] mask, float[] score, float strength, int reason)
        {
            strength = MathUtil.Sanitize(strength);
            if (!(strength > 0.01f)) return;
            int best = -1, second = -1;
            float bs = 0f, ss = 0f;
            for (int t = 0; t < Counts.Buildings; t++)
            {
                if (!mask[t]) continue;
                if (!CanPropose(civ, s, db, t, out var def)) continue;
                float sc = score[t] * Feasibility(def);
                if (_typePending[t] > 0) sc *= 0.5f;
                if (sc > bs) { second = best; ss = bs; best = t; bs = sc; }
                else if (sc > ss) { second = t; ss = sc; }
            }
            if (best >= 0) Vote(s, civ, (BuildingType)best, strength * (_typePending[best] > 0 ? 0.6f : 1f), reason);
            if (second >= 0) Vote(s, civ, (BuildingType)second, strength * 0.5f, reason);
        }

        /// <summary>
        /// Casts a build vote (cycle-scaled by the base class) and explains strong ones in the AI log, at most once per
        /// reason per season per settlement, so players see why without log spam. Strength is per council cycle.
        /// </summary>
        private void Vote(Settlement s, Civilization civ, BuildingType type, float strength, int reason)
        {
            strength = MathUtil.Clamp(MathUtil.Sanitize(strength), -1f, 1f);
            if (strength == 0f) return;
            VoteBuild(s, civ, type, strength);
            if (reason < 0 || reason >= s_reasons.Length) return;
            float effective = strength * AdvisorBoard.Weight(civ, Id);
            if (Math.Abs(effective) < 0.5f) return;
            int mask = (int)MathUtil.Clamp(s.Mem(LoggedKey), 0f, 16777215f);
            int bit = 1 << reason;
            if ((mask & bit) != 0) return;
            s.Memory[LoggedKey] = mask | bit;
            s.LogAi("[" + Id + "] " + (effective > 0f ? "+" : "") + effective.ToString("0.00") + " " + type + ": " + s_reasons[reason]);
        }

        /// <summary>Votes every available candidate of a class (used for soft negative votes; no reason).</summary>
        private void VoteAll(Civilization civ, Settlement s, GameDatabase db, bool[] mask, float strength)
        {
            for (int t = 0; t < Counts.Buildings; t++)
            {
                if (!mask[t] || !_cat.Defined[t]) continue;
                var type = (BuildingType)t;
                if (db.IsBuildingAvailable(civ, type)) VoteBuild(s, civ, type, strength);
            }
        }

        private void VoteShelter(Civilization civ, Settlement s, GameDatabase db, float strength)
        {
            if (_o.Homeless <= 0 || !db.IsBuildingAvailable(civ, BuildingType.House)) return;
            float need = MathUtil.Clamp01(_o.Homeless / 5f);
            Vote(s, civ, BuildingType.House, strength * (0.5f + 0.5f * need), RShelter);
        }

        private bool CanPropose(Civilization civ, Settlement s, GameDatabase db, int t, out BuildingDefinition def)
        {
            def = null;
            if (!_cat.Defined[t]) return false;
            if (!db.IsBuildingAvailable(civ, (BuildingType)t)) return false;
            def = db.Buildings[t];
            if (def == null || def.IsWonder || def.UniquePerCivilization) return false;
            if (def.MaxPerSettlement >= 0 && _typeCount[t] >= def.MaxPerSettlement) return false;
            if (s.Tier < def.MinTier || s.Population < def.MinPopulation) return false;
            return Feasibility(def) > 0f;
        }

        /// <summary>0 when a hard placement requirement cannot be met in the territory, else a fertility-shaped factor.</summary>
        private float Feasibility(BuildingDefinition d)
        {
            var o = _o;
            var p = d.Placement;
            if ((p & PlacementRule.Coastal) != 0 && o.Coastal == 0) return 0f;
            if ((p & PlacementRule.NearWater) != 0 && o.Coastal + o.River + o.Water == 0) return 0f;
            if ((p & PlacementRule.NearRiver) != 0 && o.River == 0) return 0f;
            if ((p & PlacementRule.NearForest) != 0 && o.Trees == 0) return 0f;
            if ((p & PlacementRule.NearOre) != 0 && o.Ore == 0) return 0f;
            if ((p & PlacementRule.NearStone) != 0 && o.Stone + o.Mountain == 0) return 0f;
            if ((p & PlacementRule.NearGold) != 0 && o.Gold == 0) return 0f;
            if ((p & PlacementRule.NearCoal) != 0 && o.Coal == 0) return 0f;
            if ((p & PlacementRule.NearOil) != 0 && o.Oil == 0) return 0f;
            if ((p & PlacementRule.OnHills) != 0 && o.Hills + o.Mountain == 0) return 0f;
            if ((p & PlacementRule.NearMountain) != 0 && o.Mountain == 0) return 0f;
            if ((p & PlacementRule.Fertile) != 0)
            {
                if (o.Fertility < 0.12f) return 0f;
                return MathUtil.Clamp(0.4f + o.Fertility, 0.5f, 1.3f);
            }
            return 1f;
        }

        private void VoteJobs(Civilization civ, Settlement s, bool[] mask, float strength)
        {
            if (strength == 0f) return;
            for (int j = 1; j < Counts.Jobs; j++)
                if (mask[j] && _jobSlots[j] > 0 && j != (int)JobType.Builder) VoteJob(s, civ, (JobType)j, strength);
        }

        private void VoteJobsExcept(Civilization civ, Settlement s, bool[] mask, bool[] except, float strength)
        {
            if (strength == 0f) return;
            for (int j = 1; j < Counts.Jobs; j++)
                if (mask[j] && !except[j] && _jobSlots[j] > 0 && j != (int)JobType.Builder) VoteJob(s, civ, (JobType)j, strength);
        }

        private float BuildDrive() => 0.7f + 0.6f * _o.Industry;

        /// <summary>Unstaffed woodcutters should be filled before new camps are raised.</summary>
        private float WoodBuildFactor() => _o.WoodUnderstaffed ? 0.5f : 1f;

        private static float WaterCoverage(Settlement s) =>
            MathUtil.Clamp(MathUtil.Sanitize(s.ServiceCoverage[(int)ServiceType.Water]), 0f, 2f);

        private float ComputeUrgency()
        {
            var o = _o;
            float u = 0f;
            if (o.Season != Season.Winter)
            {
                u = Math.Max(u, o.StorageGap * (0.4f + 0.6f * o.PrepPressure));
                u = Math.Max(u, o.FoodGap * (0.3f + 0.7f * o.PrepPressure));
                u = Math.Max(u, o.WoodGap * o.Cold * (0.3f + 0.7f * o.PrepPressure));
            }
            else
            {
                u = Math.Max(u, o.Hunger);
                u = Math.Max(u, o.WoodGap * o.Cold);
                if (o.Cold >= ColdThreshold && o.Homeless > 0) u = Math.Max(u, 0.5f * o.Cold);
            }
            if (o.Drought > 0f) u = Math.Max(u, o.Drought * (0.5f + 0.5f * o.Arid));
            if (o.Storm > 0f && o.Coastal > 0) u = Math.Max(u, 0.6f * o.Storm);
            return MathUtil.Clamp01(MathUtil.Sanitize(u));
        }

        /// <summary>Once per season, writes a short human-readable plan to the settlement's AI log.</summary>
        private void AnnouncePlan(Settlement s)
        {
            var o = _o;
            float code = (int)o.Season + 1;
            if (s.Mem(LastSeasonKey) == code) return;
            s.Memory[LastSeasonKey] = code;
            s.Memory[LoggedKey] = 0f;
            string line = "[" + Id + "] " + o.Season + " plan: winter need " + o.WinterNeed.ToString("0")
                          + " food (store " + o.EffectiveCapacity.ToString("0") + ", proj " + o.ProjectedFood.ToString("0") + ")";
            if (o.Cold >= ColdThreshold) line += ", firewood " + o.ProjectedWood.ToString("0") + "/" + o.WoodTarget.ToString("0");
            line += ", cold " + o.Cold.ToString("0.00");
            if (o.Drought > 0f) line += ", DROUGHT";
            if (o.Storm >= 0.25f) line += ", STORM";
            s.LogAi(line);
        }
    }
}
