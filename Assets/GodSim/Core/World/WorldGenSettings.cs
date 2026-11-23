using System;

namespace GodSim.Core.World
{
    /// <summary>
    /// Fixed elevation levels used by world generation. Land elevation is remapped so these levels
    /// split the land into the configured lowland / hill / mountain / snow fractions, which keeps
    /// thresholds identical for every map size and preset. Water is below <see cref="Sea"/>.
    /// </summary>
    public static class TerrainLevels
    {
        /// <summary>Sea level (WorldMap.SeaLevel). Tiles below are water.</summary>
        public const float Sea = 0f;
        /// <summary>Land below this and next to the ocean becomes Beach.</summary>
        public const float Beach = 0.045f;
        /// <summary>Wet land below this may become Swamp.</summary>
        public const float Lowland = 0.1f;
        /// <summary>Land at or above this is Hills.</summary>
        public const float Hill = 0.35f;
        /// <summary>Land at or above this is Mountain.</summary>
        public const float Mountain = 0.6f;
        /// <summary>Land at or above this is Snow (permanent ice caps).</summary>
        public const float Snow = 0.8f;
        /// <summary>Ocean shallower than this (or close to land) is ShallowWater.</summary>
        public const float ShallowDepth = -0.12f;
    }

    /// <summary>
    /// Seed-independent knobs for <see cref="WorldGenerator"/>. All randomness comes from the Rng
    /// passed to <see cref="WorldGenerator.Generate"/>. Treat instances as untrusted input: the
    /// generator always works on a sanitized clone.
    /// </summary>
    public sealed class WorldGenSettings
    {
        /// <summary>Map width in tiles (clamped to WorldMap.MinSize..MaxSize).</summary>
        public int Width = 192;
        /// <summary>Map height in tiles (clamped to WorldMap.MinSize..MaxSize).</summary>
        public int Height = 192;
        /// <summary>Continental layout preset.</summary>
        public WorldPreset Preset = WorldPreset.Continents;
        /// <summary>Fraction of tiles that become ocean (0.1..0.9).</summary>
        public float TargetOceanRatio = 0.45f;
        /// <summary>Scales the share of hills/mountains and the strength of ridged ranges (0..3).</summary>
        public float MountainIntensity = 1f;
        /// <summary>Number of river sources to trace (0 = none). Scales with map area by default.</summary>
        public int RiverCount = 24;
        /// <summary>Moisture exponent scale: &gt;1 wetter, &lt;1 drier (0.25..4).</summary>
        public float MoistureScale = 1f;
        /// <summary>Added to every tile temperature (-0.5..0.5): + warmer, - colder.</summary>
        public float TemperatureBias;
        /// <summary>Multiplier for the number (and, gently, the size) of mineral deposits (0..4).</summary>
        public float DepositDensity = 1f;
        /// <summary>Number of Sacred sites to place (0..256). Scales with area by default.</summary>
        public int SacredSiteCount = 4;
        /// <summary>Number of ancient Ruins to place (0..512). Scales with area by default.</summary>
        public int RuinCount = 6;

        /// <summary>Default settings for a preset at the given map size (river/site counts scale with area).</summary>
        public static WorldGenSettings ForPreset(WorldPreset preset, int width, int height)
        {
            var s = new WorldGenSettings { Width = width, Height = height, Preset = preset };
            int w = MathUtil.Clamp(width, WorldMap.MinSize, WorldMap.MaxSize);
            int h = MathUtil.Clamp(height, WorldMap.MinSize, WorldMap.MaxSize);
            float area = (float)w * h;
            switch (preset)
            {
                case WorldPreset.Pangaea:
                    s.TargetOceanRatio = 0.30f; s.MountainIntensity = 1.1f; s.MoistureScale = 0.95f;
                    break;
                case WorldPreset.Archipelago:
                    s.TargetOceanRatio = 0.62f; s.MountainIntensity = 0.8f; s.MoistureScale = 1.1f;
                    break;
                case WorldPreset.Highlands:
                    s.TargetOceanRatio = 0.35f; s.MountainIntensity = 2.2f; s.MoistureScale = 1f; s.TemperatureBias = -0.03f;
                    break;
                default:
                    s.Preset = WorldPreset.Continents;
                    s.TargetOceanRatio = 0.45f; s.MountainIntensity = 1f; s.MoistureScale = 1f;
                    break;
            }
            float land = 1f - s.TargetOceanRatio;
            s.RiverCount = Math.Max(2, MathUtil.RoundToInt(area * land / 850f));
            s.SacredSiteCount = Math.Max(2, MathUtil.RoundToInt(2f + area / 15000f));
            s.RuinCount = Math.Max(2, MathUtil.RoundToInt(3f + area / 9000f));
            return s;
        }

        /// <summary>Builds sanitized settings from a SimConfig (size + preset). Null yields defaults.</summary>
        public static WorldGenSettings FromConfig(SimConfig c)
        {
            if (c == null) return ForPreset(WorldPreset.Continents, 192, 192).Sanitize();
            var preset = Enum.IsDefined(typeof(WorldPreset), c.Preset) ? c.Preset : WorldPreset.Continents;
            return ForPreset(preset, c.MapWidth, c.MapHeight).Sanitize();
        }

        /// <summary>Shallow copy (all fields are values).</summary>
        public WorldGenSettings Clone() => (WorldGenSettings)MemberwiseClone();

        /// <summary>Clamps every field to a safe range (NaN/Infinity replaced by defaults). Returns this.</summary>
        public WorldGenSettings Sanitize()
        {
            Width = MathUtil.Clamp(Width, WorldMap.MinSize, WorldMap.MaxSize);
            Height = MathUtil.Clamp(Height, WorldMap.MinSize, WorldMap.MaxSize);
            if (!Enum.IsDefined(typeof(WorldPreset), Preset)) Preset = WorldPreset.Continents;
            TargetOceanRatio = MathUtil.Clamp(MathUtil.Sanitize(TargetOceanRatio, 0.45f), 0.1f, 0.9f);
            MountainIntensity = MathUtil.Clamp(MathUtil.Sanitize(MountainIntensity, 1f), 0f, 3f);
            int area = Width * Height;
            RiverCount = MathUtil.Clamp(RiverCount, 0, Math.Max(1, area / 64));
            MoistureScale = MathUtil.Clamp(MathUtil.Sanitize(MoistureScale, 1f), 0.25f, 4f);
            TemperatureBias = MathUtil.Clamp(MathUtil.Sanitize(TemperatureBias, 0f), -0.5f, 0.5f);
            DepositDensity = MathUtil.Clamp(MathUtil.Sanitize(DepositDensity, 1f), 0f, 4f);
            SacredSiteCount = MathUtil.Clamp(SacredSiteCount, 0, 256);
            RuinCount = MathUtil.Clamp(RuinCount, 0, 512);
            return this;
        }
    }
}
