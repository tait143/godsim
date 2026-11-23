using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Shared stylized color palette (soft, slightly desaturated "diorama" look). All lookups are
    /// allocation-free table reads; unknown enum values return a neutral grey instead of throwing.
    /// </summary>
    public static class GodSimPalette
    {
        private static readonly Color Fallback = new Color(0.6f, 0.6f, 0.6f, 1f);

        // Indexed by (int)Biome. Keep in enum order.
        private static readonly Color[] BiomeColors =
        {
            Hex(0x1F4E79), // DeepOcean
            Hex(0x3A86B4), // ShallowWater
            Hex(0x4C9CC4), // Lake
            Hex(0xE8D8A0), // Beach
            Hex(0x8CC264), // Grassland
            Hex(0xC9B95A), // Savanna
            Hex(0x4E8A3E), // Forest
            Hex(0x2F7A45), // Jungle
            Hex(0xE3C27D), // Desert
            Hex(0x5E6E45), // Swamp
            Hex(0x48705A), // Taiga
            Hex(0xA9AE92), // Tundra
            Hex(0x9AA05E), // Hills
            Hex(0x8A8178), // Mountain
            Hex(0xF2F5F8), // Snow
            Hex(0x4A3A36)  // Volcanic
        };

        // Indexed by (int)AgeId: warm earthy early ages -> cool, luminous late ages.
        private static readonly Color[] AgeColors =
        {
            Hex(0xA9825A), // Dawn
            Hex(0xC08A4B), // Bronze
            Hex(0x8E8E92), // Iron
            Hex(0xE6DCC3), // Classical
            Hex(0x9C7B5B), // Medieval
            Hex(0xC9A36B), // Renaissance
            Hex(0x7A6A62), // Industrial
            Hex(0xA8B4BE), // Modern
            Hex(0x7FB6D9), // Information
            Hex(0xE7D7FF)  // Ascension
        };

        // Indexed by (int)BuildingCategory.
        private static readonly Color[] CategoryColors =
        {
            Hex(0xD9A86C), // Housing
            Hex(0x9BC45A), // Food
            Hex(0x8C6E4E), // Resource
            Hex(0x7D7F86), // Industry
            Hex(0xB89B6A), // Storage
            Hex(0x5C8FD6), // Civic
            Hex(0xF2D16B), // Religion
            Hex(0x6E7FD9), // Education
            Hex(0xE97B7B), // Health
            Hex(0xC57BD9), // Culture
            Hex(0xB5483E), // Military
            Hex(0xE8A93C), // Commerce
            Hex(0x9AA3AD), // Infrastructure
            Hex(0xFFD86E)  // Wonder
        };

        // Indexed by (int)Season: a light tint multiplied over terrain/foliage.
        private static readonly Color[] SeasonColors =
        {
            Hex(0xEAF7DF), // Spring
            Hex(0xFFF6D8), // Summer
            Hex(0xF5D9B0), // Autumn
            Hex(0xDDE7F2)  // Winter
        };

        /// <summary>Base ground color of a biome.</summary>
        public static Color Biome(Biome b) => Lookup(BiomeColors, (int)b);

        /// <summary>Converts a packed 0xRRGGBBAA color (as used in definitions) to a Unity color.</summary>
        public static Color FromRgba(uint rgba) => new Color(
            ((rgba >> 24) & 0xFF) / 255f,
            ((rgba >> 16) & 0xFF) / 255f,
            ((rgba >> 8) & 0xFF) / 255f,
            (rgba & 0xFF) / 255f);

        /// <summary>Signature material tint of an age (useful for UI accents and building trim).</summary>
        public static Color AgeTint(AgeId a) => Lookup(AgeColors, (int)a);

        /// <summary>Legend/overlay color of a building category.</summary>
        public static Color Category(BuildingCategory c) => Lookup(CategoryColors, (int)c);

        /// <summary>Soft multiplicative tint for the current season (near white; multiply into vertex colors).</summary>
        public static Color Season(Season s) => Lookup(SeasonColors, (int)s);

        private static Color Lookup(Color[] table, int i) => (uint)i < (uint)table.Length ? table[i] : Fallback;

        private static Color Hex(uint rrggbb) => new Color(
            ((rrggbb >> 16) & 0xFF) / 255f,
            ((rrggbb >> 8) & 0xFF) / 255f,
            (rrggbb & 0xFF) / 255f,
            1f);
    }
}
