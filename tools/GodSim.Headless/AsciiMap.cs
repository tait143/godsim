using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;

namespace GodSim.Headless
{
    /// <summary>
    /// Renders the world as ASCII: one character per biome, settlement centers as their civ letter,
    /// other building tiles '#', roads '.'. Large maps are downsampled to fit <see cref="MaxColumns"/>.
    /// Row 0 of the output is the top of the map (highest y).
    /// </summary>
    public static class AsciiMap
    {
        public const int MaxColumns = 160;

        public static char BiomeChar(Biome b)
        {
            switch (b)
            {
                case Biome.DeepOcean: return '~';
                case Biome.ShallowWater: return '-';
                case Biome.Lake: return '=';
                case Biome.Beach: return ',';
                case Biome.Grassland: return '"';
                case Biome.Savanna: return ';';
                case Biome.Forest: return 'f';
                case Biome.Jungle: return 'j';
                case Biome.Desert: return ':';
                case Biome.Swamp: return 's';
                case Biome.Taiga: return 't';
                case Biome.Tundra: return 'u';
                case Biome.Hills: return 'h';
                case Biome.Mountain: return '^';
                case Biome.Snow: return '*';
                case Biome.Volcanic: return 'v';
                default: return '?';
            }
        }

        /// <param name="civLetters">civ id -> letter (from the report); missing civs get '@'.</param>
        public static string Render(WorldState s, IReadOnlyDictionary<int, char> civLetters)
        {
            var map = s.Map;
            int step = Math.Max(1, (map.Width + MaxColumns - 1) / MaxColumns);
            int cols = (map.Width + step - 1) / step;
            int rows = (map.Height + step - 1) / step;
            var grid = new char[rows, cols];

            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                {
                    int x0 = c * step, y0 = r * step;
                    bool road = false, building = false;
                    var counts = new int[Counts.Biomes];
                    for (int y = y0; y < Math.Min(map.Height, y0 + step); y++)
                        for (int x = x0; x < Math.Min(map.Width, x0 + step); x++)
                        {
                            int i = map.Index(x, y);
                            int bi = (int)map.Biomes[i];
                            if (bi >= 0 && bi < counts.Length) counts[bi]++;
                            if ((map.Flags[i] & TileFlags.Road) != 0) road = true;
                            if (map.BuildingAt[i] >= 0) building = true;
                        }
                    int best = 0;
                    for (int k = 1; k < counts.Length; k++) if (counts[k] > counts[best]) best = k;
                    char ch = BiomeChar((Biome)best);
                    if (road) ch = '.';
                    if (building) ch = '#';
                    grid[rows - 1 - r, c] = ch;
                }

            var settlements = s.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var st = settlements[i];
                if (!st.IsAlive || !map.InBounds(st.Center)) continue;
                char letter = civLetters != null && civLetters.TryGetValue(st.CivilizationId, out var l) ? l : '@';
                grid[rows - 1 - st.Center.Y / step, st.Center.X / step] = letter;
            }

            var sb = new StringBuilder(rows * (cols + 1) + 256);
            sb.Append("ASCII map ").Append(map.Width).Append('x').Append(map.Height);
            if (step > 1) sb.Append(" (1 char = ").Append(step).Append('x').Append(step).Append(" tiles)");
            sb.AppendLine();
            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++) sb.Append(grid[r, c]);
                sb.AppendLine();
            }
            sb.AppendLine("Legend: ~ deep ocean  - shallows  = lake  , beach  \" grassland  ; savanna  f forest  j jungle  : desert");
            sb.AppendLine("        s swamp  t taiga  u tundra  h hills  ^ mountain  * snow  v volcanic  . road  # building  A-Z settlement (civ letter)");
            return sb.ToString();
        }
    }
}
