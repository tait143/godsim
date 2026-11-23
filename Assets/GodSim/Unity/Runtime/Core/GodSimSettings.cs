using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Designer-facing world-creation settings (mirrors <see cref="SimConfig"/>) plus visual settings.
    /// Create via Assets > Create > GodSim > Settings and assign it to <see cref="GodSimRunner.Settings"/>.
    /// Values are clamped in <see cref="OnValidate"/> using the same ranges as <see cref="SimConfig.Sanitize"/>.
    /// </summary>
    [CreateAssetMenu(fileName = "GodSimSettings", menuName = "GodSim/Settings", order = 0)]
    public sealed class GodSimSettings : ScriptableObject
    {
        [Header("World")]
        [Tooltip("World seed. Ignored when Seed Text is not empty.")]
        public long Seed = 20261119L;
        [Tooltip("Optional seed phrase; when set it is hashed (FNV-1a 64) into the seed.")]
        public string SeedText = "";
        [Min(64)] public int MapWidth = 192;
        [Min(64)] public int MapHeight = 192;
        public WorldPreset Preset = WorldPreset.Continents;
        [Tooltip("One civilization per entry (max 16).")]
        public RaceId[] StartingRaces = { RaceId.Human, RaceId.Elf, RaceId.Dwarf, RaceId.Orc, RaceId.Saurian };
        [Range(2, 200)] public int InitialPopulationPerRace = 14;

        [Header("Caps")]
        public int MaxPopulation = 40000;
        public int MaxSettlementsPerCiv = 24;
        public int MaxBuildingsPerSettlement = 600;
        public int MaxArmies = 256;
        public int MaxCommandsPerTick = 4;
        public int CommandQueueCapacity = 32;

        [Header("Multipliers")]
        [Range(0f, 10f)] public float GrowthMultiplier = 1f;
        [Range(0f, 50f)] public float ResearchMultiplier = 1f;
        [Range(0f, 10f)] public float ProductionMultiplier = 1f;

        [Header("Disasters & Warfare")]
        public bool EnableDisasters = true;
        [Range(0f, 10f)] public float DisasterFrequency = 1f;
        public bool EnableWarfare = true;

        [Header("Divine")]
        public float StartingMana = 120f;
        [Range(0f, 100f)] public float ManaRegenMultiplier = 1f;

        [Header("Diagnostics")]
        [Tooltip("Rethrow system exceptions (development only; the runner pauses on a fault).")]
        public bool StrictMode;
        public int MaxSystemFaults = 25;
        public LogLevel MinLogLevel = LogLevel.Info;

        [Header("Visuals")]
        [Tooltip("World units of height for elevation 1.0 (GodSimCoords.HeightScale).")]
        [Range(0.5f, 40f)] public float HeightScale = 6f;
        public bool ShowTerritory = true;
        [Tooltip("Upper bound on individually rendered people (instanced).")]
        public int MaxVisiblePeople = 6000;
        public ShadowQuality ShadowQuality = ShadowQuality.All;

        /// <summary>The seed actually used: hash of <see cref="SeedText"/> when set, else <see cref="Seed"/>.</summary>
        public ulong ResolveSeed() =>
            string.IsNullOrWhiteSpace(SeedText) ? unchecked((ulong)Seed) : Hashing.Fnv1a64(SeedText.Trim());

        /// <summary>Builds a fresh, <see cref="SimConfig.Sanitize"/>d config from these settings.</summary>
        public SimConfig ToConfig()
        {
            var c = new SimConfig
            {
                Seed = ResolveSeed(),
                MapWidth = MapWidth,
                MapHeight = MapHeight,
                Preset = Preset,
                StartingRaces = StartingRaces != null ? (RaceId[])StartingRaces.Clone() : null,
                InitialPopulationPerRace = InitialPopulationPerRace,
                MaxPopulation = MaxPopulation,
                MaxSettlementsPerCiv = MaxSettlementsPerCiv,
                MaxBuildingsPerSettlement = MaxBuildingsPerSettlement,
                MaxArmies = MaxArmies,
                MaxCommandsPerTick = MaxCommandsPerTick,
                CommandQueueCapacity = CommandQueueCapacity,
                GrowthMultiplier = GrowthMultiplier,
                ResearchMultiplier = ResearchMultiplier,
                ProductionMultiplier = ProductionMultiplier,
                DisasterFrequency = DisasterFrequency,
                EnableWarfare = EnableWarfare,
                EnableDisasters = EnableDisasters,
                StartingMana = StartingMana,
                ManaRegenMultiplier = ManaRegenMultiplier,
                StrictMode = StrictMode,
                MaxSystemFaults = MaxSystemFaults
            };
            return c.Sanitize();
        }

        private void OnValidate()
        {
            if (SeedText == null) SeedText = "";
            // Round-trip through Sanitize so the inspector always shows the ranges Core enforces.
            SimConfig c = ToConfig();
            MapWidth = c.MapWidth;
            MapHeight = c.MapHeight;
            Preset = c.Preset;
            StartingRaces = c.StartingRaces;
            InitialPopulationPerRace = c.InitialPopulationPerRace;
            MaxPopulation = c.MaxPopulation;
            MaxSettlementsPerCiv = c.MaxSettlementsPerCiv;
            MaxBuildingsPerSettlement = c.MaxBuildingsPerSettlement;
            MaxArmies = c.MaxArmies;
            MaxCommandsPerTick = c.MaxCommandsPerTick;
            CommandQueueCapacity = c.CommandQueueCapacity;
            GrowthMultiplier = c.GrowthMultiplier;
            ResearchMultiplier = c.ResearchMultiplier;
            ProductionMultiplier = c.ProductionMultiplier;
            DisasterFrequency = c.DisasterFrequency;
            StartingMana = c.StartingMana;
            ManaRegenMultiplier = c.ManaRegenMultiplier;
            MaxSystemFaults = c.MaxSystemFaults;

            HeightScale = Mathf.Clamp(float.IsNaN(HeightScale) ? 6f : HeightScale, 0.5f, 40f);
            MaxVisiblePeople = Mathf.Clamp(MaxVisiblePeople, 0, 100000);
            if (ShadowQuality < ShadowQuality.Disable || ShadowQuality > ShadowQuality.All) ShadowQuality = ShadowQuality.All;
            if (MinLogLevel < LogLevel.Trace || MinLogLevel > LogLevel.Error) MinLogLevel = LogLevel.Info;
        }
    }
}
