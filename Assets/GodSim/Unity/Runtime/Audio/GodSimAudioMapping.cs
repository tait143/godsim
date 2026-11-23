using GodSim.Core;

namespace GodSim.Unity
{
    /// <summary>
    /// Unity-free mapping from simulation vocabulary to audio decisions (power categories, wonders, the
    /// music mode/root/brightness for the world's age and the god's alignment). Shared by
    /// <see cref="GodSimAudio"/> and the dotnet unit tests.
    /// </summary>
    public static class GodSimAudioMapping
    {
        /// <summary>
        /// Category of a power, matching Divine's GodPowerCatalog (Blessings 0-6, Shaping 7-10,
        /// SpawnTribe = Creation, Wrath 12-18). Used when no GameDatabase is at hand.
        /// </summary>
        public static GodPowerCategory CategoryOf(GodPowerType power)
        {
            int p = (int)power;
            if (p <= (int)GodPowerType.Prophet) return GodPowerCategory.Blessing;
            if (power == GodPowerType.SpawnTribe) return GodPowerCategory.Creation;
            if (p <= (int)GodPowerType.SpawnTribe) return GodPowerCategory.Shaping;
            return GodPowerCategory.Wrath;
        }

        /// <summary>Wonder buildings (shared and racial) get the harp stinger instead of hammer taps.</summary>
        public static bool IsWonder(BuildingType type)
        {
            switch (type)
            {
                case BuildingType.AscensionGate:
                case BuildingType.GrandCathedral:
                case BuildingType.WorldTree:
                case BuildingType.DeepForge:
                case BuildingType.WarIdol:
                case BuildingType.SunPyramid:
                case BuildingType.GreatLibrary:
                case BuildingType.Colossus:
                case BuildingType.GrandBazaar:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Music mode for the god's alignment (-1 wrathful .. +1 benevolent):
        /// strongly benevolent Lydian, benevolent major, neutral Dorian, wrathful minor, strongly wrathful Phrygian.
        /// </summary>
        public static ProceduralSynth.Mode MusicModeFor(float alignment)
        {
            float a = MathUtil.Sanitize(alignment);
            if (a >= 0.4f) return ProceduralSynth.Mode.Lydian;
            if (a >= 0.1f) return ProceduralSynth.Mode.Ionian;
            if (a <= -0.4f) return ProceduralSynth.Mode.Phrygian;
            if (a <= -0.1f) return ProceduralSynth.Mode.Aeolian;
            return ProceduralSynth.Mode.Dorian;
        }

        private static readonly int[] MusicRootOffsets = { 0, 2, 3, 5, 7, 5, 3, 7, 8, 10 };

        /// <summary>Root MIDI note of the music bed for the highest world age (A2 upward).</summary>
        public static int MusicRootFor(AgeId age)
        {
            int a = MathUtil.Clamp((int)age, 0, MusicRootOffsets.Length - 1);
            return 45 + MusicRootOffsets[a];
        }

        /// <summary>Pad brightness 0..1: later ages and benevolence open the filter.</summary>
        public static float MusicBrightnessFor(AgeId age, float alignment)
        {
            float a = MathUtil.Clamp((int)age, 0, 9) / 9f;
            return MathUtil.Clamp01(0.25f + 0.5f * a + 0.15f * MathUtil.Sanitize(alignment));
        }

        /// <summary>Melodic density 0..1: later ages are busier.</summary>
        public static float MusicDensityFor(AgeId age)
        {
            float a = MathUtil.Clamp((int)age, 0, 9) / 9f;
            return MathUtil.Clamp01(0.25f + 0.55f * a);
        }
    }
}
