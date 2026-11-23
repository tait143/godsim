using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Data-driven classification of the building catalog and the job vocabulary for <see cref="SeasonalAdvisor"/>.
    /// Everything is derived from <see cref="BuildingDefinition"/> fields (outputs, services, storage, placement,
    /// footprint, cost) so new or rebalanced buildings are understood automatically. Built once per
    /// <see cref="GameDatabase"/> instance (arrays indexed by BuildingType / JobType), never per tick.
    /// </summary>
    public sealed class SeasonalCatalog
    {
        /// <summary>Builder-ticks of labor at which a project counts as "large" (winter-sensitive).</summary>
        public const float LargeBuildWork = 50f;
        /// <summary>Footprint (tiles) at which a project counts as "large".</summary>
        public const int LargeFootprint = 6;
        /// <summary>Total material cost at which a project counts as "large".</summary>
        public const float LargeCost = 100f;

        /// <summary>The database this catalog was built from (rebuilt when it changes).</summary>
        public GameDatabase Db { get; private set; }

        // ---- buildings (indexed by (int)BuildingType)
        public readonly bool[] Defined = new bool[Counts.Buildings];
        /// <summary>Outputs food per worker.</summary>
        public readonly bool[] FoodProducer = new bool[Counts.Buildings];
        /// <summary>Seasonal field/forage food (fertile placement, field shape, farmer/herder/gatherer job).</summary>
        public readonly bool[] CropProducer = new bool[Counts.Buildings];
        /// <summary>Food that keeps flowing in winter and drought (hunting, fishing, hydroponics...).</summary>
        public readonly bool[] HardyFood = new bool[Counts.Buildings];
        public readonly bool[] WoodProducer = new bool[Counts.Buildings];
        public readonly bool[] Storage = new bool[Counts.Buildings];
        public readonly bool[] WaterProvider = new bool[Counts.Buildings];
        /// <summary>Built on the exposed shore (Coastal placement or dock shape): storm-sensitive.</summary>
        public readonly bool[] Exposed = new bool[Counts.Buildings];
        public readonly bool[] Military = new bool[Counts.Buildings];
        /// <summary>Worth building even in deep winter: shelter, storage, food, fuel, water, health.</summary>
        public readonly bool[] Essential = new bool[Counts.Buildings];
        /// <summary>Expensive / long construction (see the Large* thresholds) or a wonder.</summary>
        public readonly bool[] Large = new bool[Counts.Buildings];
        public readonly float[] CostTotal = new float[Counts.Buildings];
        public readonly float[] StorageCapacity = new float[Counts.Buildings];
        /// <summary>Value-for-effort scores (output per unit of cost + labor); 0 when not applicable.</summary>
        public readonly float[] FoodScore = new float[Counts.Buildings];
        public readonly float[] WoodScore = new float[Counts.Buildings];
        public readonly float[] WaterScore = new float[Counts.Buildings];
        public readonly float[] StorageScore = new float[Counts.Buildings];

        // ---- jobs (indexed by (int)JobType)
        public readonly bool[] JobFood = new bool[Counts.Jobs];
        public readonly bool[] JobCrop = new bool[Counts.Jobs];
        public readonly bool[] JobOutdoor = new bool[Counts.Jobs];
        public readonly bool[] JobWood = new bool[Counts.Jobs];
        public readonly bool[] JobSea = new bool[Counts.Jobs];
        public readonly bool[] JobCraft = new bool[Counts.Jobs];
        public readonly bool[] JobResearch = new bool[Counts.Jobs];
        public readonly bool[] JobFaith = new bool[Counts.Jobs];
        public readonly bool[] JobHealth = new bool[Counts.Jobs];
        public readonly bool[] JobCulture = new bool[Counts.Jobs];

        /// <summary>(Re)builds every table from the database. Safe with a null or partially filled database.</summary>
        public void Build(GameDatabase db)
        {
            Db = db;
            ApplyJobDefaults();
            int n = Counts.Buildings;
            for (int t = 0; t < n; t++)
            {
                Defined[t] = FoodProducer[t] = CropProducer[t] = HardyFood[t] = WoodProducer[t] = false;
                Storage[t] = WaterProvider[t] = Exposed[t] = Military[t] = Essential[t] = Large[t] = false;
                CostTotal[t] = StorageCapacity[t] = FoodScore[t] = WoodScore[t] = WaterScore[t] = StorageScore[t] = 0f;

                BuildingDefinition d = db != null && t < db.Buildings.Count ? db.Buildings[t] : null;
                if (d == null) continue;
                Defined[t] = true;

                float food = Output(d.OutputsPerWorker, ResourceType.Food);
                float wood = Output(d.OutputsPerWorker, ResourceType.Wood);
                float fuel = Output(d.OutputsPerWorker, ResourceType.Fuel);
                float water = ServiceOf(d.Services, ServiceType.Water);
                float cost = 0f;
                if (d.Cost != null)
                    for (int i = 0; i < d.Cost.Length; i++) cost += Math.Max(0f, MathUtil.Sanitize(d.Cost[i].Amount));
                float work = Math.Max(0f, MathUtil.Sanitize(d.BuildWork));
                float effort = 1f + cost + work;
                int slots = d.HasJobs ? d.JobSlots : 1;
                int job = (int)d.Job;
                bool jobValid = d.HasJobs && job > 0 && job < Counts.Jobs;
                bool staticCrop = jobValid && JobCrop[job];

                CostTotal[t] = cost;
                FoodProducer[t] = food > 0f;
                CropProducer[t] = food > 0f && ((d.Placement & PlacementRule.Fertile) != 0 || d.Shape == BuildingShape.Field || staticCrop);
                HardyFood[t] = food > 0f && !CropProducer[t];
                WoodProducer[t] = wood > 0f;
                StorageCapacity[t] = Math.Max(0f, MathUtil.Sanitize(d.StorageCapacity));
                Storage[t] = d.Category == BuildingCategory.Storage && StorageCapacity[t] > 0f;
                WaterProvider[t] = water > 0f;
                Exposed[t] = (d.Placement & PlacementRule.Coastal) != 0 || d.Shape == BuildingShape.Dock;
                Military[t] = d.Category == BuildingCategory.Military;
                Essential[t] = d.IsDwelling || Storage[t] || food > 0f || wood > 0f || fuel > 0f || water > 0f
                               || d.Category == BuildingCategory.Health || d.Type == BuildingType.TownCenter;
                Large[t] = d.IsWonder || work >= LargeBuildWork || d.Width * d.Height >= LargeFootprint || cost >= LargeCost;

                if (food > 0f) FoodScore[t] = Math.Max(1e-3f, food * slots * 100f / effort);
                if (wood > 0f) WoodScore[t] = Math.Max(1e-3f, wood * slots * 100f / effort);
                if (water > 0f) WaterScore[t] = Math.Max(1e-3f, water * 100f / effort);
                if (Storage[t]) StorageScore[t] = Math.Max(1e-3f, StorageCapacity[t] * 10f / effort);

                if (!jobValid) continue;
                if (food > 0f) { JobFood[job] = true; JobOutdoor[job] |= CropProducer[t] || Exposed[t]; if (CropProducer[t]) JobCrop[job] = true; }
                if (wood > 0f) { JobWood[job] = true; JobOutdoor[job] = true; }
                if (Exposed[t] && food > 0f) JobSea[job] = true;
                switch (d.Category)
                {
                    case BuildingCategory.Industry: JobCraft[job] = true; break;
                    case BuildingCategory.Education: JobResearch[job] = true; break;
                    case BuildingCategory.Religion: JobFaith[job] = true; break;
                    case BuildingCategory.Health: JobHealth[job] = true; break;
                    case BuildingCategory.Culture: JobCulture[job] = true; break;
                }
            }
        }

        /// <summary>Vocabulary defaults for the fixed JobType enum (data from definitions is OR-ed on top).</summary>
        private void ApplyJobDefaults()
        {
            for (int j = 0; j < Counts.Jobs; j++)
                JobFood[j] = JobCrop[j] = JobOutdoor[j] = JobWood[j] = JobSea[j] = JobCraft[j] = JobResearch[j] = JobFaith[j] = JobHealth[j] = JobCulture[j] = false;
            Set(JobFood, JobType.Gatherer, JobType.Hunter, JobType.Fisher, JobType.Farmer, JobType.Herder);
            Set(JobCrop, JobType.Gatherer, JobType.Farmer, JobType.Herder);
            Set(JobOutdoor, JobType.Gatherer, JobType.Hunter, JobType.Fisher, JobType.Farmer, JobType.Herder, JobType.Woodcutter, JobType.Quarrier, JobType.Laborer);
            Set(JobWood, JobType.Woodcutter);
            Set(JobSea, JobType.Fisher);
            Set(JobCraft, JobType.Smith, JobType.Craftsman, JobType.Weaver, JobType.Engineer);
            Set(JobResearch, JobType.Scholar);
            Set(JobFaith, JobType.Priest);
            Set(JobHealth, JobType.Healer);
            Set(JobCulture, JobType.Entertainer);
        }

        private static void Set(bool[] a, params JobType[] jobs)
        {
            for (int i = 0; i < jobs.Length; i++) { int j = (int)jobs[i]; if (j >= 0 && j < a.Length) a[j] = true; }
        }

        private static float Output(ResourceAmount[] list, ResourceType type)
        {
            if (list == null) return 0f;
            float v = 0f;
            for (int i = 0; i < list.Length; i++) if (list[i].Type == type) v += Math.Max(0f, MathUtil.Sanitize(list[i].Amount));
            return v;
        }

        private static float ServiceOf(ServiceAmount[] list, ServiceType type)
        {
            if (list == null) return 0f;
            float v = 0f;
            for (int i = 0; i < list.Length; i++) if (list[i].Type == type) v += Math.Max(0f, MathUtil.Sanitize(list[i].Amount));
            return v;
        }
    }

    /// <summary>
    /// Settlement climate from its center tile: biome, latitude (distance from the map's equator row), tile
    /// temperature/moisture and the global weather swing. Pure functions; never return NaN.
    /// </summary>
    public static class SeasonalClimate
    {
        /// <summary>True for the cold biomes (Tundra, Taiga, Snow).</summary>
        public static bool IsColdBiome(Biome b) => b == Biome.Tundra || b == Biome.Taiga || b == Biome.Snow;

        /// <summary>How cold a biome is (0 temperate/hot .. 1 frozen).</summary>
        public static float BiomeCold(Biome b)
        {
            switch (b)
            {
                case Biome.Snow: return 1f;
                case Biome.Tundra: return 0.9f;
                case Biome.Taiga: return 0.72f;
                case Biome.Mountain: return 0.55f;
                case Biome.Hills: return 0.15f;
                default: return 0f;
            }
        }

        /// <summary>How dry a biome is (0 lush .. 1 desert).</summary>
        public static float BiomeArid(Biome b)
        {
            switch (b)
            {
                case Biome.Desert: return 1f;
                case Biome.Savanna: return 0.6f;
                case Biome.Volcanic: return 0.45f;
                case Biome.Tundra: return 0.2f;
                default: return 0f;
            }
        }

        /// <summary>0 at the map's middle row (equator) .. 1 at the top/bottom edge (poles).</summary>
        public static float Latitude(WorldMap map, Vec2i c)
        {
            if (map == null || map.Height <= 1) return 0f;
            float y = MathUtil.Clamp(c.Y, 0, map.Height - 1) / (float)(map.Height - 1);
            return MathUtil.Clamp01(Math.Abs(2f * y - 1f));
        }

        /// <summary>Climate coldness 0..1 from center biome, latitude, tile temperature and the weather's temperature swing.</summary>
        public static float Coldness(WorldMap map, Vec2i c, float temperatureOffset)
        {
            if (map == null || !map.InBounds(c)) return 0f;
            int i = map.Index(c);
            float biome = BiomeCold(map.Biomes[i]);
            float lat = MathUtil.SmoothStep(0.55f, 0.95f, Latitude(map, c)) * 0.85f;
            float temp = (1f - MathUtil.SmoothStep(0.15f, 0.45f, MathUtil.Sanitize(map.Temperature[i], 0.5f))) * 0.9f;
            float cold = Math.Max(biome, Math.Max(lat, temp));
            return MathUtil.Clamp01(cold - 0.3f * MathUtil.Clamp(MathUtil.Sanitize(temperatureOffset), -1f, 1f));
        }

        /// <summary>Climate aridity 0..1 from center biome, tile moisture and current rainfall.</summary>
        public static float Aridity(WorldMap map, Vec2i c, float rainfall)
        {
            if (map == null || !map.InBounds(c)) return 0f;
            int i = map.Index(c);
            float biome = BiomeArid(map.Biomes[i]);
            float moist = (1f - MathUtil.SmoothStep(0.1f, 0.4f, MathUtil.Sanitize(map.Moisture[i], 0.5f))) * 0.8f;
            float rain = (1f - MathUtil.Clamp(MathUtil.Sanitize(rainfall, 1f), 0f, 2f)) * 0.3f;
            return MathUtil.Clamp01(Math.Max(biome, moist) + rain);
        }
    }

    /// <summary>
    /// The seasonal advisor's per-settlement forecast (reused scratch object; read it for UI/debug right after a run).
    /// </summary>
    public sealed class SeasonalOutlook
    {
        public Season Season;
        public int DayInSeason;
        public float Progress;              // 0..1 through the current season
        public int TicksUntilWinter;        // 0 while in winter
        public int WinterTicksLeft;         // days of winter still to endure (full season when not in winter)
        public float PrepPressure;          // 0 far from winter .. 1 at/inside winter (personality-shaped window)

        public Biome CenterBiome;
        public float Cold, Arid, Habitability, Severity;
        public float Yield, Drought, Storm;
        public bool Raining;

        public int Population, Homeless, Backlog;
        public float Consumption, FoodStock, FoodIncome, WinterIncome, WinterNeed, ProjectedFood;
        public float EffectiveCapacity, PendingStorage, StorageGap, FoodGap, FoodDays, Hunger;
        public float WoodStock, WoodTarget, ProjectedWood, WoodGap;
        public bool WoodUnderstaffed, AtWar;

        public float Industry, Curiosity, Piety, Prudence;

        // territory survey
        public int Trees, Coastal, River, Water, Stone, Ore, Gold, Coal, Oil, Hills, Mountain, LandTiles;
        public float Fertility;

        internal void ResetTerritory()
        {
            Trees = Coastal = River = Water = Stone = Ore = Gold = Coal = Oil = Hills = Mountain = LandTiles = 0;
            Fertility = 0f;
        }
    }
}
