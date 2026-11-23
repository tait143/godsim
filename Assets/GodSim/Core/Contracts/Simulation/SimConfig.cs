using System;

namespace GodSim.Core
{
    public enum WorldPreset : byte { Continents = 0, Pangaea = 1, Archipelago = 2, Highlands = 3 }

    /// <summary>
    /// World creation + runtime tuning. Treated as untrusted input (it may come from a UI or a file):
    /// call <see cref="Sanitize"/> before use. All values are clamped to safe ranges.
    /// </summary>
    public sealed class SimConfig
    {
        public ulong Seed = 20261119UL;
        public int MapWidth = 192;
        public int MapHeight = 192;
        public WorldPreset Preset = WorldPreset.Continents;
        public RaceId[] StartingRaces = (RaceId[])EnumUtil.AllRaces.Clone();
        public int InitialPopulationPerRace = 14;

        public int MaxPopulation = 40000;             // hard global cap (performance safety)
        public int MaxSettlementsPerCiv = 24;
        public int MaxBuildingsPerSettlement = 600;
        public int MaxArmies = 256;
        public int MaxCommandsPerTick = 4;            // god command rate limit
        public int CommandQueueCapacity = 32;

        public float GrowthMultiplier = 1f;
        public float ResearchMultiplier = 1f;
        public float ProductionMultiplier = 1f;
        public float DisasterFrequency = 1f;          // 0 disables natural disasters
        public bool EnableWarfare = true;
        public bool EnableDisasters = true;
        public float StartingMana = 120f;
        public float ManaRegenMultiplier = 1f;

        /// <summary>Rethrow system exceptions instead of isolating them (tests/CI).</summary>
        public bool StrictMode;
        /// <summary>Disable a system after this many consecutive faults (fault isolation).</summary>
        public int MaxSystemFaults = 25;

        public SimConfig Clone() => (SimConfig)MemberwiseClone();

        /// <summary>Clamps every field to a safe range. Returns this for chaining.</summary>
        public SimConfig Sanitize()
        {
            MapWidth = MathUtil.Clamp(MapWidth, WorldMap.MinSize * 4, WorldMap.MaxSize);
            MapHeight = MathUtil.Clamp(MapHeight, WorldMap.MinSize * 4, WorldMap.MaxSize);
            if (!Enum.IsDefined(typeof(WorldPreset), Preset)) Preset = WorldPreset.Continents;
            if (StartingRaces == null || StartingRaces.Length == 0) StartingRaces = (RaceId[])EnumUtil.AllRaces.Clone();
            if (StartingRaces.Length > 16) Array.Resize(ref StartingRaces, 16);
            for (int i = 0; i < StartingRaces.Length; i++) if (!Enum.IsDefined(typeof(RaceId), StartingRaces[i])) StartingRaces[i] = RaceId.Human;
            InitialPopulationPerRace = MathUtil.Clamp(InitialPopulationPerRace, 2, 200);
            MaxPopulation = MathUtil.Clamp(MaxPopulation, 100, 250000);
            MaxSettlementsPerCiv = MathUtil.Clamp(MaxSettlementsPerCiv, 1, 128);
            MaxBuildingsPerSettlement = MathUtil.Clamp(MaxBuildingsPerSettlement, 10, 5000);
            MaxArmies = MathUtil.Clamp(MaxArmies, 0, 4096);
            MaxCommandsPerTick = MathUtil.Clamp(MaxCommandsPerTick, 1, 64);
            CommandQueueCapacity = MathUtil.Clamp(CommandQueueCapacity, 1, 1024);
            GrowthMultiplier = MathUtil.Clamp(MathUtil.Sanitize(GrowthMultiplier, 1f), 0f, 10f);
            ResearchMultiplier = MathUtil.Clamp(MathUtil.Sanitize(ResearchMultiplier, 1f), 0f, 50f);
            ProductionMultiplier = MathUtil.Clamp(MathUtil.Sanitize(ProductionMultiplier, 1f), 0f, 10f);
            DisasterFrequency = MathUtil.Clamp(MathUtil.Sanitize(DisasterFrequency, 1f), 0f, 10f);
            StartingMana = MathUtil.Clamp(MathUtil.Sanitize(StartingMana, 100f), 0f, 100000f);
            ManaRegenMultiplier = MathUtil.Clamp(MathUtil.Sanitize(ManaRegenMultiplier, 1f), 0f, 100f);
            MaxSystemFaults = MathUtil.Clamp(MaxSystemFaults, 1, 100000);
            return this;
        }
    }
}
