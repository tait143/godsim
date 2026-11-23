using System;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Pure, allocation-free chunk mesh generation: WorldMap (+ <see cref="TerrainBuildInput"/>) -> vertex/color/index
    /// lists in a reusable <see cref="TerrainMeshData"/>. Stylized low-poly look:
    /// one flat quad per tile at its surface height, vertical cliff skirts toward every lower neighbor (each tile
    /// owns the skirts on its higher sides, so chunk seams are watertight without duplicates), vertex colors from
    /// the biome color with a deterministic per-tile variation and slope/relief shading, and overlays
    /// (river / road strips, striped farmland, ruins, scorched, burning, territory) baked into vertex colors.
    /// Rect arguments are tile coordinates, max EXCLUSIVE.
    /// </summary>
    public static class TerrainMeshBuilder
    {
        public const float RiverWidth = 0.42f;
        public const float RoadWidth = 0.30f;
        /// <summary>Fraction of the territory color blended over interior / border tiles.</summary>
        public const float TerritoryInterior = 0.25f;
        public const float TerritoryBorder = 0.60f;

        private const ulong FnvOffset = 14695981039346656037UL;
        private const ulong FnvPrime = 1099511628211UL;

        private static readonly Color RiverColor = new Color(0.22f, 0.46f, 0.80f, 1f);
        private static readonly Color RoadColor = new Color(0.64f, 0.53f, 0.37f, 1f);
        private static readonly Color BridgeColor = new Color(0.46f, 0.33f, 0.21f, 1f);
        private static readonly Color FarmGreen = new Color(0.43f, 0.62f, 0.21f, 1f);
        private static readonly Color FarmYellow = new Color(0.87f, 0.77f, 0.33f, 1f);
        private static readonly Color FarmFallow = new Color(0.55f, 0.42f, 0.27f, 1f);
        private static readonly Color RuinsGrey = new Color(0.56f, 0.55f, 0.53f, 1f);
        private static readonly Color Charcoal = new Color(0.15f, 0.13f, 0.12f, 1f);
        private static readonly Color FireOrange = new Color(1.00f, 0.47f, 0.10f, 1f);
        private static readonly Color CliffRock = new Color(0.46f, 0.41f, 0.37f, 1f);

        private static readonly Vector3 Up = new Vector3(0f, 1f, 0f);
        private static readonly Vector3 East = new Vector3(1f, 0f, 0f);
        private static readonly Vector3 West = new Vector3(-1f, 0f, 0f);
        private static readonly Vector3 North = new Vector3(0f, 0f, 1f);
        private static readonly Vector3 South = new Vector3(0f, 0f, -1f);

        /// <summary>Per-tile shading context shared by all cells of one tile.</summary>
        private struct TileShade
        {
            public float Shade;
            public TileFlags Flags;
            public uint Hash;
            public Color Territory;
            public float TerritoryAmount;
        }

        // ------------------------------------------------------------------------------------------------
        // Ground + water
        // ------------------------------------------------------------------------------------------------

        /// <summary>Builds the ground and water geometry of the tile rect [minX,maxX) x [minY,maxY) into <paramref name="output"/>.</summary>
        public static void BuildChunk(TerrainBuildInput input, int minX, int minY, int maxX, int maxY, TerrainMeshData output)
        {
            if (output == null) return;
            output.ClearGround();
            if (input == null || !input.IsValid) return;

            WorldMap map = input.Map;
            int w = map.Width, hgt = map.Height;
            minX = MathUtil.Clamp(minX, 0, w); maxX = MathUtil.Clamp(maxX, minX, w);
            minY = MathUtil.Clamp(minY, 0, hgt); maxY = MathUtil.Clamp(maxY, minY, hgt);
            int cw = maxX - minX, ch = maxY - minY;
            if (cw == 0 || ch == 0) return;

            if (output.WaterMask.Length < cw * ch) output.WaterMask = new bool[cw * ch];
            bool anyWater = false;

            float ts = input.TileSize > 0f ? input.TileSize : 1f;
            float half = ts * 0.5f;
            float[] heights = input.Heights;

            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    int i = y * w + x;
                    float h = heights[i];
                    float cx = (x - minX) * ts, cz = (y - minY) * ts;
                    float x0 = cx - half, x1 = cx + half, z0 = cz - half, z1 = cz + half;

                    bool water = map.Biomes[i].IsWater();
                    output.WaterMask[(y - minY) * cw + (x - minX)] = water;
                    anyWater |= water;

                    TileShade sh;
                    sh.Flags = water ? TileFlags.None : map.Flags[i];
                    sh.Hash = TileHash(x, y, input.Seed);
                    sh.Shade = ReliefShade(input, x, y, i, h, ts, water);
                    sh.TerritoryAmount = TerritoryAmount(input, x, y, i, out sh.Territory);

                    Color baseColor = BaseColor(input, i, sh.Hash);

                    if ((sh.Flags & (TileFlags.River | TileFlags.Road)) != 0)
                        EmitCross(output, input, x, y, i, x0, z0, ts, h, baseColor, in sh);
                    else if ((sh.Flags & TileFlags.Farmland) != 0)
                        EmitFarm(output, input, x, y, x0, z0, ts, h, baseColor, in sh);
                    else
                        AddTop(output, x0, z0, x1, z1, h, Finish(baseColor, in sh));

                    // Cliff skirts toward lower neighbors (or the map border).
                    EmitSkirt(output, input, x + 1, y, h, new Vector3(x1, h, z0), new Vector3(x1, h, z1), East, baseColor, ts);
                    EmitSkirt(output, input, x - 1, y, h, new Vector3(x0, h, z1), new Vector3(x0, h, z0), West, baseColor, ts);
                    EmitSkirt(output, input, x, y + 1, h, new Vector3(x1, h, z1), new Vector3(x0, h, z1), North, baseColor, ts);
                    EmitSkirt(output, input, x, y - 1, h, new Vector3(x0, h, z0), new Vector3(x1, h, z0), South, baseColor, ts);
                }
            }

            if (anyWater) BuildWater(input, minX, minY, cw, ch, ts, output);
        }

        /// <summary>Water surface: a shared vertex grid at WaterHeight (smooth depth tint across tiles and chunk seams), triangles only on water tiles.</summary>
        private static void BuildWater(TerrainBuildInput input, int minX, int minY, int cw, int ch, float ts, TerrainMeshData output)
        {
            WorldMap map = input.Map;
            float half = ts * 0.5f;
            float wh = input.WaterHeight;
            for (int vy = 0; vy <= ch; vy++)
            {
                for (int vx = 0; vx <= cw; vx++)
                {
                    int gx = minX + vx, gy = minY + vy;
                    float r = 0f, g = 0f, b = 0f, a = 0f;
                    int n = 0;
                    for (int ty = gy - 1; ty <= gy; ty++)
                    {
                        for (int tx = gx - 1; tx <= gx; tx++)
                        {
                            if (!map.InBounds(tx, ty)) continue;
                            int ti = ty * map.Width + tx;
                            if (!map.Biomes[ti].IsWater()) continue;
                            Color c = WaterTileColor(input, ti, ts);
                            r += c.r; g += c.g; b += c.b; a += c.a; n++;
                        }
                    }
                    Color32 col = n > 0 ? (Color32)new Color(r / n, g / n, b / n, a / n) : new Color32(40, 90, 160, 160);
                    output.WaterVertices.Add(new Vector3(vx * ts - half, wh, vy * ts - half));
                    output.WaterNormals.Add(Up);
                    output.WaterColors.Add(col);
                }
            }

            int stride = cw + 1;
            for (int ly = 0; ly < ch; ly++)
            {
                for (int lx = 0; lx < cw; lx++)
                {
                    if (!output.WaterMask[ly * cw + lx]) continue;
                    int v00 = ly * stride + lx, v10 = v00 + 1, v01 = v00 + stride, v11 = v01 + 1;
                    // (x0,z0) -> (x0,z1) -> (x1,z1) is clockwise seen from above: front face up in Unity.
                    output.WaterTriangles.Add(v00); output.WaterTriangles.Add(v01); output.WaterTriangles.Add(v11);
                    output.WaterTriangles.Add(v00); output.WaterTriangles.Add(v11); output.WaterTriangles.Add(v10);
                }
            }
        }

        private static Color WaterTileColor(TerrainBuildInput input, int i, float ts)
        {
            Color c = BiomeColor(input, input.Map.Biomes[i]);
            float depth = input.WaterHeight - input.Heights[i];
            c.a = MathUtil.Lerp(0.55f, 0.92f, depth / (ts * 1.5f));
            return c;
        }

        // ------------------------------------------------------------------------------------------------
        // Colors
        // ------------------------------------------------------------------------------------------------

        private static Color BiomeColor(TerrainBuildInput input, Biome b)
        {
            int bi = (int)b;
            return bi < input.BiomeColors.Length ? (Color)input.BiomeColors[bi] : new Color(0.5f, 0.5f, 0.5f, 1f);
        }

        /// <summary>Biome color with a deterministic, subtle per-tile brightness/hue variation.</summary>
        private static Color BaseColor(TerrainBuildInput input, int i, uint hash)
        {
            Color c = BiomeColor(input, input.Map.Biomes[i]);
            float v = 0.94f + 0.12f * Hash01(hash);
            float dr = (Hash01(hash >> 7) - 0.5f) * 0.035f;
            float dg = (Hash01(hash >> 13) - 0.5f) * 0.035f;
            float db = (Hash01(hash >> 19) - 0.5f) * 0.035f;
            return new Color(c.r * v + dr, c.g * v + dg, c.b * v + db, 1f);
        }

        /// <summary>Relief shading: darker on steep tiles and in hollows, lighter on crests; slight elevation gradient.</summary>
        private static float ReliefShade(TerrainBuildInput input, int x, int y, int i, float h, float ts, bool water)
        {
            WorldMap map = input.Map;
            float[] heights = input.Heights;
            float slope = 0f, sum = 0f;
            int n = 0;
            if (x + 1 < map.Width) Accumulate(heights[i + 1], h, ref slope, ref sum, ref n);
            if (x > 0) Accumulate(heights[i - 1], h, ref slope, ref sum, ref n);
            if (y + 1 < map.Height) Accumulate(heights[i + map.Width], h, ref slope, ref sum, ref n);
            if (y > 0) Accumulate(heights[i - map.Width], h, ref slope, ref sum, ref n);
            float relief = n > 0 ? (h - sum / n) / ts : 0f;
            float shade = 1f + MathUtil.Clamp(relief * 0.18f, -0.14f, 0.08f) - MathUtil.Clamp01(slope / ts * 0.12f) * 0.16f;
            if (water) return shade * 0.72f; // seabed, seen through the water surface
            float e = MathUtil.Sanitize(map.Elevation[i]);
            return shade * (0.93f + 0.10f * MathUtil.Clamp01(e));
        }

        private static void Accumulate(float hn, float h, ref float slope, ref float sum, ref int n)
        {
            float d = Math.Abs(h - hn);
            if (d > slope) slope = d;
            sum += hn;
            n++;
        }

        /// <summary>Territory blend amount (0 = none) and civ color for tile i. Border = a land 4-neighbor with another owner.</summary>
        private static float TerritoryAmount(TerrainBuildInput input, int x, int y, int i, out Color color)
        {
            color = default(Color);
            if (!input.ShowTerritory) return 0f;
            WorldMap map = input.Map;
            int owner = map.Owner[i];
            if (owner < 0) return 0f;
            if (!input.SettlementColors.TryGetValue(owner, out Color32 c32)) return 0f;
            color = c32;
            bool border = IsForeignLand(map, x + 1, y, owner) || IsForeignLand(map, x - 1, y, owner)
                       || IsForeignLand(map, x, y + 1, owner) || IsForeignLand(map, x, y - 1, owner);
            return border ? TerritoryBorder : TerritoryInterior;
        }

        private static bool IsForeignLand(WorldMap map, int x, int y, int owner)
        {
            if (!map.InBounds(x, y)) return false;
            int i = y * map.Width + x;
            return map.Owner[i] != owner && !map.Biomes[i].IsWater();
        }

        /// <summary>Applies flag tints, relief shade and territory to a cell color.</summary>
        private static Color32 Finish(Color c, in TileShade s)
        {
            TileFlags f = s.Flags;
            if ((f & TileFlags.Ruins) != 0) c = Color.Lerp(c, RuinsGrey * (0.85f + 0.3f * Hash01(s.Hash >> 3)), 0.55f);
            if ((f & TileFlags.Scorched) != 0) c = Color.Lerp(c, Charcoal, 0.70f);
            if ((f & TileFlags.Burning) != 0) c = Color.Lerp(c, FireOrange, 0.55f);
            c.r *= s.Shade; c.g *= s.Shade; c.b *= s.Shade;
            if (s.TerritoryAmount > 0f) c = Color.Lerp(c, s.Territory, s.TerritoryAmount);
            c.a = 1f;
            return c; // implicit Color -> Color32 clamps to [0,1]
        }

        // ------------------------------------------------------------------------------------------------
        // Tile patterns
        // ------------------------------------------------------------------------------------------------

        /// <summary>River/road tile: 3x3 cells, the center and the arms toward connected neighbors use the strip color.</summary>
        private static void EmitCross(TerrainMeshData d, TerrainBuildInput input, int x, int y, int i, float x0, float z0,
                                      float ts, float h, Color baseColor, in TileShade sh)
        {
            WorldMap map = input.Map;
            bool river = (sh.Flags & TileFlags.River) != 0;
            bool road = (sh.Flags & TileFlags.Road) != 0;
            float width = (river ? RiverWidth : RoadWidth) * ts;
            float a = x0 + (ts - width) * 0.5f, b = a + width;
            float za = z0 + (ts - width) * 0.5f, zb = za + width;
            float x1 = x0 + ts, z1 = z0 + ts;

            float tint = 0.9f + 0.2f * Hash01(sh.Hash >> 5);
            Color riverC = RiverColor * tint;
            Color roadC = Color.Lerp(RoadColor, baseColor, 0.12f) * tint;
            Color center = river && road ? BridgeColor : (river ? riverC : roadC);

            Color n = ArmColor(map, x, y + 1, river, road, riverC, roadC, baseColor);
            Color s = ArmColor(map, x, y - 1, river, road, riverC, roadC, baseColor);
            Color e = ArmColor(map, x + 1, y, river, road, riverC, roadC, baseColor);
            Color wst = ArmColor(map, x - 1, y, river, road, riverC, roadC, baseColor);

            Color32 baseF = Finish(baseColor, in sh);
            // bottom row (z0..za)
            AddTop(d, x0, z0, a, za, h, baseF);
            AddTop(d, a, z0, b, za, h, Finish(s, in sh));
            AddTop(d, b, z0, x1, za, h, baseF);
            // middle row (za..zb)
            AddTop(d, x0, za, a, zb, h, Finish(wst, in sh));
            AddTop(d, a, za, b, zb, h, Finish(center, in sh));
            AddTop(d, b, za, x1, zb, h, Finish(e, in sh));
            // top row (zb..z1)
            AddTop(d, x0, zb, a, z1, h, baseF);
            AddTop(d, a, zb, b, z1, h, Finish(n, in sh));
            AddTop(d, b, zb, x1, z1, h, baseF);
        }

        private static Color ArmColor(WorldMap map, int nx, int ny, bool river, bool road, Color riverC, Color roadC, Color baseColor)
        {
            if (!map.InBounds(nx, ny)) return baseColor;
            int ni = ny * map.Width + nx;
            TileFlags nf = map.Flags[ni];
            if (river && ((nf & TileFlags.River) != 0 || map.Biomes[ni].IsWater())) return riverC;
            if (road && (nf & TileFlags.Road) != 0) return roadC;
            return baseColor;
        }

        /// <summary>Farmland: three alternating crop stripes; orientation shared by 4x4 blocks so fields read as plots.</summary>
        private static void EmitFarm(TerrainMeshData d, TerrainBuildInput input, int x, int y, float x0, float z0,
                                     float ts, float h, Color baseColor, in TileShade sh)
        {
            uint block = TileHash(x >> 2, y >> 2, input.Seed ^ 0xFA4Bu);
            bool alongX = (block & 1u) == 0u;
            bool fallow = (block % 7u) == 3u;
            Color colA = Color.Lerp(baseColor, FarmGreen, 0.8f);
            Color colB = Color.Lerp(baseColor, fallow ? FarmFallow : FarmYellow, 0.8f);
            float bright = 0.92f + 0.16f * Hash01(block >> 9);
            float step = ts / 3f;
            for (int k = 0; k < 3; k++)
            {
                int phase = alongX ? y * 3 + k : x * 3 + k;
                Color c = ((phase & 1) == 0 ? colA : colB) * bright;
                Color32 fc = Finish(c, in sh);
                if (alongX) AddTop(d, x0, z0 + k * step, x0 + ts, z0 + (k + 1) * step, h, fc);
                else AddTop(d, x0 + k * step, z0, x0 + (k + 1) * step, z0 + ts, h, fc);
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Geometry primitives
        // ------------------------------------------------------------------------------------------------

        /// <summary>Horizontal quad facing up. Order (xa,za)->(xa,zb)->(xb,zb) is clockwise from above = front face in Unity.</summary>
        private static void AddTop(TerrainMeshData d, float xa, float za, float xb, float zb, float h, Color32 c)
        {
            int v = d.Vertices.Count;
            d.Vertices.Add(new Vector3(xa, h, za));
            d.Vertices.Add(new Vector3(xa, h, zb));
            d.Vertices.Add(new Vector3(xb, h, zb));
            d.Vertices.Add(new Vector3(xb, h, za));
            for (int k = 0; k < 4; k++) { d.Normals.Add(Up); d.Colors.Add(c); }
            d.Triangles.Add(v); d.Triangles.Add(v + 1); d.Triangles.Add(v + 2);
            d.Triangles.Add(v); d.Triangles.Add(v + 2); d.Triangles.Add(v + 3);
        }

        /// <summary>Vertical cliff quad from the top edge (ta,tb) down to the neighbor height, if the neighbor is lower.</summary>
        private static void EmitSkirt(TerrainMeshData d, TerrainBuildInput input, int nx, int ny, float h,
                                      Vector3 ta, Vector3 tb, Vector3 normal, Color baseColor, float ts)
        {
            WorldMap map = input.Map;
            float hn = map.InBounds(nx, ny) ? input.Heights[ny * map.Width + nx] : input.EdgeBottom;
            float drop = h - hn;
            if (drop <= 0.001f) return;

            Color rock = Color.Lerp(baseColor, CliffRock, 0.15f + 0.55f * MathUtil.Clamp01(drop / ts));
            Color32 top = Shade(rock, 0.80f);
            Color32 bottom = Shade(rock, 0.52f);
            Vector3 bb = new Vector3(tb.x, hn, tb.z);
            Vector3 ba = new Vector3(ta.x, hn, ta.z);

            int v = d.Vertices.Count;
            d.Vertices.Add(ta); d.Vertices.Add(tb); d.Vertices.Add(bb); d.Vertices.Add(ba);
            for (int k = 0; k < 4; k++) d.Normals.Add(normal);
            d.Colors.Add(top); d.Colors.Add(top); d.Colors.Add(bottom); d.Colors.Add(bottom);
            // Robust winding: make the front face point along the outward normal.
            Vector3 cr = Vector3.Cross(tb - ta, bb - ta);
            if (Vector3.Dot(cr, normal) >= 0f)
            {
                d.Triangles.Add(v); d.Triangles.Add(v + 1); d.Triangles.Add(v + 2);
                d.Triangles.Add(v); d.Triangles.Add(v + 2); d.Triangles.Add(v + 3);
            }
            else
            {
                d.Triangles.Add(v); d.Triangles.Add(v + 2); d.Triangles.Add(v + 1);
                d.Triangles.Add(v); d.Triangles.Add(v + 3); d.Triangles.Add(v + 2);
            }
        }

        private static Color32 Shade(Color c, float k) => new Color(c.r * k, c.g * k, c.b * k, 1f);

        // ------------------------------------------------------------------------------------------------
        // Trees
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Fills <see cref="TerrainMeshData.Trees"/> with world-space instance matrices for every wooded tile of the rect
        /// (no trees on water, roads or building tiles). Size scales with the tile's remaining Resource.
        /// </summary>
        public static void BuildTrees(TerrainBuildInput input, int minX, int minY, int maxX, int maxY, TerrainMeshData output)
        {
            if (output == null) return;
            output.ClearTrees();
            if (input == null || !input.IsValid || input.MaxTreesPerTile <= 0) return;

            WorldMap map = input.Map;
            int w = map.Width;
            minX = MathUtil.Clamp(minX, 0, w); maxX = MathUtil.Clamp(maxX, minX, w);
            minY = MathUtil.Clamp(minY, 0, map.Height); maxY = MathUtil.Clamp(maxY, minY, map.Height);
            float ts = input.TileSize > 0f ? input.TileSize : 1f;
            float full = input.TreeFullResource > 1f ? input.TreeFullResource : 1f;
            int maxPerTile = Math.Min(3, input.MaxTreesPerTile);

            float bminX = float.MaxValue, bminY = float.MaxValue, bminZ = float.MaxValue;
            float bmaxX = float.MinValue, bmaxY = float.MinValue, bmaxZ = float.MinValue;
            bool any = false;

            for (int y = minY; y < maxY; y++)
            {
                for (int x = minX; x < maxX; x++)
                {
                    int i = y * w + x;
                    TileFlags f = map.Flags[i];
                    if ((f & TileFlags.Trees) == 0 || (f & TileFlags.Road) != 0) continue;
                    if (map.BuildingAt[i] >= 0 || map.Biomes[i].IsWater()) continue;

                    float res = MathUtil.Clamp01(MathUtil.Sanitize(map.Resource[i]) / full);
                    int count = res >= 0.6f ? 2 : 1;
                    if (res >= 0.95f) count = 3;
                    if (count > maxPerTile) count = maxPerTile;
                    TreeKind kind = ChooseKind(map, x, y, i);
                    if (kind == TreeKind.Palm && count > 2) count = 2;
                    var list = output.Trees[(int)kind];
                    uint th = TileHash(x, y, input.Seed ^ 0x7EE5u);
                    float h = input.Heights[i];

                    for (int k = 0; k < count; k++)
                    {
                        uint hk = Rehash(th + (uint)k * 0x9E3779B9u);
                        float ox, oz;
                        if (count == 1)
                        {
                            ox = (Hash01(hk) - 0.5f) * 0.40f * ts;
                            oz = (Hash01(hk >> 8) - 0.5f) * 0.40f * ts;
                        }
                        else
                        {
                            // spread around the tile center on a rotated ring
                            float ang = (k / (float)count) * 6.2831853f + Hash01(th) * 6.2831853f;
                            float rad = 0.24f * ts;
                            ox = (float)Math.Cos(ang) * rad + (Hash01(hk) - 0.5f) * 0.10f * ts;
                            oz = (float)Math.Sin(ang) * rad + (Hash01(hk >> 8) - 0.5f) * 0.10f * ts;
                        }
                        float scale = ts * MathUtil.Lerp(0.50f, 1.0f, res) * (0.85f + 0.30f * Hash01(hk >> 16)) * (count > 1 ? 0.82f : 1f);
                        float hMul = 0.9f + 0.3f * Hash01(Rehash(hk));
                        float yaw = Hash01(Rehash(hk ^ 0xABCDu)) * 360f;

                        Vector3 pos = new Vector3(input.Origin.x + x * ts + ox, h, input.Origin.z + y * ts + oz);
                        list.Add(Matrix4x4.TRS(pos, Quaternion.Euler(0f, yaw, 0f), new Vector3(scale, scale * hMul, scale)));

                        float rr = scale * 0.6f, top = scale * hMul * 1.3f;
                        if (pos.x - rr < bminX) bminX = pos.x - rr;
                        if (pos.x + rr > bmaxX) bmaxX = pos.x + rr;
                        if (pos.z - rr < bminZ) bminZ = pos.z - rr;
                        if (pos.z + rr > bmaxZ) bmaxZ = pos.z + rr;
                        if (pos.y < bminY) bminY = pos.y;
                        if (pos.y + top > bmaxY) bmaxY = pos.y + top;
                        any = true;
                    }
                }
            }

            if (any)
            {
                Bounds b = default(Bounds);
                b.SetMinMax(new Vector3(bminX, bminY, bminZ), new Vector3(bmaxX, bmaxY, bmaxZ));
                output.TreeBounds = b;
            }
        }

        /// <summary>Conifer for cold/snowy land (Taiga, Snow-adjacent), palm for Jungle/Beach/Swamp/Desert, deciduous otherwise.</summary>
        public static TreeKind ChooseKind(WorldMap map, int x, int y, int i)
        {
            switch (map.Biomes[i])
            {
                case Biome.Taiga: case Biome.Snow: case Biome.Tundra: case Biome.Mountain:
                    return TreeKind.Conifer;
                case Biome.Jungle: case Biome.Beach: case Biome.Swamp: case Biome.Desert:
                    return TreeKind.Palm;
            }
            if (map.Temperature[i] < 0.3f) return TreeKind.Conifer;
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int nx = x + dx, ny = y + dy;
                    if ((dx != 0 || dy != 0) && map.InBounds(nx, ny) && map.Biomes[ny * map.Width + nx] == Biome.Snow)
                        return TreeKind.Conifer;
                }
            return TreeKind.Deciduous;
        }

        // ------------------------------------------------------------------------------------------------
        // Change detection
        // ------------------------------------------------------------------------------------------------

        /// <summary>
        /// Cheap content hash of everything that affects a chunk's look (flags, biome, elevation, building presence,
        /// tree size bucket and, when territory is shown, the owner and its color), over the rect plus a 1-tile ring
        /// (skirts and borders depend on neighbors). Used by the renderer's periodic refresh to find stale chunks.
        /// </summary>
        public static ulong ChunkSignature(TerrainBuildInput input, int minX, int minY, int maxX, int maxY)
        {
            if (input == null || input.Map == null) return 0UL;
            WorldMap map = input.Map;
            int x0 = Math.Max(0, minX - 1), y0 = Math.Max(0, minY - 1);
            int x1 = Math.Min(map.Width, maxX + 1), y1 = Math.Min(map.Height, maxY + 1);
            float full = input.TreeFullResource > 1f ? input.TreeFullResource : 1f;
            ulong h = FnvOffset;
            int lastOwner = int.MinValue;
            uint ownerKey = 0u;
            unchecked
            {
                for (int y = y0; y < y1; y++)
                {
                    int row = y * map.Width;
                    for (int x = x0; x < x1; x++)
                    {
                        int i = row + x;
                        TileFlags f = map.Flags[i];
                        uint treeBucket = (f & TileFlags.Trees) != 0
                            ? (uint)(int)(MathUtil.Clamp01(MathUtil.Sanitize(map.Resource[i]) / full) * 3.99f) : 0u;
                        uint a = (uint)f | ((uint)map.Biomes[i] << 16) | ((map.BuildingAt[i] >= 0 ? 1u : 0u) << 21) | (treeBucket << 22);
                        h = (h ^ a) * FnvPrime;
                        h = (h ^ (uint)(int)(MathUtil.Sanitize(map.Elevation[i]) * 65536f)) * FnvPrime;
                        if (input.ShowTerritory)
                        {
                            int o = map.Owner[i];
                            if (o != lastOwner) { lastOwner = o; ownerKey = OwnerKey(input, o); }
                            h = (h ^ ownerKey) * FnvPrime;
                        }
                    }
                }
            }
            return h;
        }

        private static uint OwnerKey(TerrainBuildInput input, int owner)
        {
            if (owner < 0) return 0u;
            unchecked
            {
                uint k = (uint)owner * 2654435761u;
                if (input.SettlementColors.TryGetValue(owner, out Color32 c))
                    k ^= ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | 1u;
                return k;
            }
        }

        // ------------------------------------------------------------------------------------------------
        // Hashing
        // ------------------------------------------------------------------------------------------------

        /// <summary>Deterministic per-tile hash (stable across runs/platforms).</summary>
        public static uint TileHash(int x, int y, ulong seed)
        {
            unchecked
            {
                ulong k = ((ulong)(uint)x << 32) | (uint)y;
                return (uint)Hashing.Mix64(k ^ (seed * 0x9E3779B97F4A7C15UL));
            }
        }

        private static uint Rehash(uint h)
        {
            unchecked
            {
                h ^= h >> 16; h *= 0x7FEB352Du;
                h ^= h >> 15; h *= 0x846CA68Bu;
                h ^= h >> 16;
                return h;
            }
        }

        /// <summary>Low 24 bits of a hash as a float in [0,1).</summary>
        private static float Hash01(uint h) => (h & 0xFFFFFFu) * (1f / 16777216f);
    }
}
