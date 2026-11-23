using System;

namespace GodSim.Core.Persistence
{
    /// <summary>
    /// World-creation parameters a replay needs to reproduce its world: seed, map size, preset and the
    /// SimConfig values that influence simulation outcomes, plus the start tick/hash and a version tag.
    /// </summary>
    public sealed class ReplayHeader
    {
        /// <summary>Maximum number of starting races stored (mirrors SimConfig.Sanitize).</summary>
        public const int MaxRaces = 16;

        /// <summary>World seed.</summary>
        public ulong Seed;
        /// <summary>Map width in tiles.</summary>
        public int MapWidth;
        /// <summary>Map height in tiles.</summary>
        public int MapHeight;
        /// <summary>World generation preset.</summary>
        public WorldPreset Preset;
        /// <summary>Races that start the world.</summary>
        public RaceId[] StartingRaces = new RaceId[0];
        /// <summary>SimConfig.InitialPopulationPerRace.</summary>
        public int InitialPopulationPerRace;
        /// <summary>SimConfig.MaxPopulation.</summary>
        public int MaxPopulation;
        /// <summary>SimConfig.MaxSettlementsPerCiv.</summary>
        public int MaxSettlementsPerCiv;
        /// <summary>SimConfig.MaxBuildingsPerSettlement.</summary>
        public int MaxBuildingsPerSettlement;
        /// <summary>SimConfig.MaxArmies.</summary>
        public int MaxArmies;
        /// <summary>SimConfig.MaxCommandsPerTick.</summary>
        public int MaxCommandsPerTick;
        /// <summary>SimConfig.CommandQueueCapacity.</summary>
        public int CommandQueueCapacity;
        /// <summary>SimConfig.MaxSystemFaults.</summary>
        public int MaxSystemFaults;
        /// <summary>SimConfig.GrowthMultiplier.</summary>
        public float GrowthMultiplier;
        /// <summary>SimConfig.ResearchMultiplier.</summary>
        public float ResearchMultiplier;
        /// <summary>SimConfig.ProductionMultiplier.</summary>
        public float ProductionMultiplier;
        /// <summary>SimConfig.DisasterFrequency.</summary>
        public float DisasterFrequency;
        /// <summary>SimConfig.StartingMana.</summary>
        public float StartingMana;
        /// <summary>SimConfig.ManaRegenMultiplier.</summary>
        public float ManaRegenMultiplier;
        /// <summary>SimConfig.EnableWarfare.</summary>
        public bool EnableWarfare;
        /// <summary>SimConfig.EnableDisasters.</summary>
        public bool EnableDisasters;
        /// <summary>SimConfig.StrictMode.</summary>
        public bool StrictMode;

        /// <summary>Tick at which recording started (0 for a fresh world).</summary>
        public long StartTick;
        /// <summary><see cref="StateHasher.Compute"/> of the world at <see cref="StartTick"/> (0 = unknown).</summary>
        public ulong StartStateHash;
        /// <summary>Free-form build/version tag (sanitized like entry sources, at most 64 chars).</summary>
        public string GameVersion = "";

        /// <summary>Creates a header holding the default <see cref="SimConfig"/> values.</summary>
        public ReplayHeader() { CopyFrom(new SimConfig()); }

        /// <summary>Captures a (preferably already sanitized) configuration.</summary>
        public static ReplayHeader FromConfig(SimConfig config, long startTick = 0, ulong startStateHash = 0, string gameVersion = "")
        {
            if (config == null) throw new ArgumentNullException(nameof(config));
            var h = new ReplayHeader();
            h.CopyFrom(config);
            h.StartTick = startTick;
            h.StartStateHash = startStateHash;
            h.GameVersion = ReplayLog.SanitizeText(gameVersion);
            return h;
        }

        /// <summary>Builds a sanitized <see cref="SimConfig"/> from the header (for re-creating the world).</summary>
        public SimConfig ToConfig()
        {
            var c = new SimConfig
            {
                Seed = Seed,
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
                MaxSystemFaults = MaxSystemFaults,
                GrowthMultiplier = GrowthMultiplier,
                ResearchMultiplier = ResearchMultiplier,
                ProductionMultiplier = ProductionMultiplier,
                DisasterFrequency = DisasterFrequency,
                StartingMana = StartingMana,
                ManaRegenMultiplier = ManaRegenMultiplier,
                EnableWarfare = EnableWarfare,
                EnableDisasters = EnableDisasters,
                StrictMode = StrictMode,
            };
            return c.Sanitize();
        }

        /// <summary>Structural validation (also applied by <see cref="ReplayLog.FromBytes"/>).</summary>
        public bool Validate(out string reason)
        {
            reason = null;
            if (MapWidth < WorldMap.MinSize || MapWidth > WorldMap.MaxSize || MapHeight < WorldMap.MinSize || MapHeight > WorldMap.MaxSize) { reason = "map size out of range"; return false; }
            if (!Enum.IsDefined(typeof(WorldPreset), Preset)) { reason = "unknown world preset"; return false; }
            if (StartingRaces == null || StartingRaces.Length > MaxRaces) { reason = "invalid starting race list"; return false; }
            for (int i = 0; i < StartingRaces.Length; i++)
                if (!Enum.IsDefined(typeof(RaceId), StartingRaces[i])) { reason = "unknown starting race"; return false; }
            if (!Finite(GrowthMultiplier) || !Finite(ResearchMultiplier) || !Finite(ProductionMultiplier) ||
                !Finite(DisasterFrequency) || !Finite(StartingMana) || !Finite(ManaRegenMultiplier)) { reason = "non-finite multiplier"; return false; }
            if (StartTick < 0) { reason = "negative start tick"; return false; }
            if (GameVersion == null || !ReplayLog.IsCleanText(GameVersion)) { reason = "invalid version tag"; return false; }
            return true;
        }

        private static bool Finite(float f) => !float.IsNaN(f) && !float.IsInfinity(f);

        private void CopyFrom(SimConfig c)
        {
            Seed = c.Seed;
            MapWidth = c.MapWidth;
            MapHeight = c.MapHeight;
            Preset = c.Preset;
            StartingRaces = c.StartingRaces != null ? (RaceId[])c.StartingRaces.Clone() : new RaceId[0];
            if (StartingRaces.Length > MaxRaces) Array.Resize(ref StartingRaces, MaxRaces);
            InitialPopulationPerRace = c.InitialPopulationPerRace;
            MaxPopulation = c.MaxPopulation;
            MaxSettlementsPerCiv = c.MaxSettlementsPerCiv;
            MaxBuildingsPerSettlement = c.MaxBuildingsPerSettlement;
            MaxArmies = c.MaxArmies;
            MaxCommandsPerTick = c.MaxCommandsPerTick;
            CommandQueueCapacity = c.CommandQueueCapacity;
            MaxSystemFaults = c.MaxSystemFaults;
            GrowthMultiplier = c.GrowthMultiplier;
            ResearchMultiplier = c.ResearchMultiplier;
            ProductionMultiplier = c.ProductionMultiplier;
            DisasterFrequency = c.DisasterFrequency;
            StartingMana = c.StartingMana;
            ManaRegenMultiplier = c.ManaRegenMultiplier;
            EnableWarfare = c.EnableWarfare;
            EnableDisasters = c.EnableDisasters;
            StrictMode = c.StrictMode;
        }
    }
}
