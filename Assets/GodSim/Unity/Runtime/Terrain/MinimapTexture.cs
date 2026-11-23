using System;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Whole-map minimap (one texel per tile, point filtered, pixel (x,y) = tile (x,y)): biome color with elevation
    /// shading, rivers/roads/fields/buildings, fire/scorch/ruins, territory tint (borders stronger) and settlements as
    /// bright dots (capitals with a gold core). Refreshes can be spread over frames with <see cref="BeginRefresh"/> /
    /// <see cref="Step"/>; the texture object stays the same across refreshes and map-size changes (Reinitialize),
    /// so HUD references remain valid.
    /// </summary>
    public sealed class MinimapTexture
    {
        private static readonly Color RiverColor = new Color(0.25f, 0.50f, 0.85f, 1f);
        private static readonly Color RoadColor = new Color(0.70f, 0.58f, 0.40f, 1f);
        private static readonly Color FarmColor = new Color(0.80f, 0.74f, 0.33f, 1f);
        private static readonly Color BuildingColor = new Color(0.88f, 0.84f, 0.76f, 1f);
        private static readonly Color RuinsColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        private static readonly Color CharcoalColor = new Color(0.15f, 0.13f, 0.12f, 1f);
        private static readonly Color FireColor = new Color(1.00f, 0.45f, 0.08f, 1f);
        private static readonly Color ForestShade = new Color(0.10f, 0.28f, 0.10f, 1f);
        private static readonly Color32 White = new Color32(255, 255, 255, 255);
        private static readonly Color32 Gold = new Color32(255, 214, 80, 255);

        private Texture2D _texture;
        private Color32[] _pixels;
        private int _width, _height;
        private int _nextRow = -1;

        /// <summary>The minimap texture (null until the first refresh).</summary>
        public Texture2D Texture => _texture;

        /// <summary>True while a spread-out refresh is in progress.</summary>
        public bool IsRefreshing => _nextRow >= 0;

        /// <summary>Full, immediate refresh.</summary>
        public void Rebuild(WorldState state, TerrainBuildInput input)
        {
            if (!BeginRefresh(state)) return;
            Step(state, input, int.MaxValue);
        }

        /// <summary>Starts a refresh (resizing the texture if the map size changed). Returns false if there is no map.</summary>
        public bool BeginRefresh(WorldState state)
        {
            if (state == null || state.Map == null) { _nextRow = -1; return false; }
            EnsureSize(state.Map.Width, state.Map.Height);
            _nextRow = 0;
            return true;
        }

        /// <summary>Paints up to <paramref name="maxRows"/> rows; on the last row paints settlements and uploads. Returns true when complete.</summary>
        public bool Step(WorldState state, TerrainBuildInput input, int maxRows)
        {
            if (_nextRow < 0) return true;
            if (state == null || state.Map == null || input == null || state.Map.Width != _width || state.Map.Height != _height)
            {
                _nextRow = -1;
                return true;
            }
            WorldMap map = state.Map;
            int end = (int)Math.Min((long)_nextRow + Math.Max(1, maxRows), _height);
            for (int y = _nextRow; y < end; y++) PaintRow(map, input, y);
            _nextRow = end;
            if (_nextRow < _height) return false;

            PaintSettlements(state, input);
            _texture.SetPixels32(_pixels);
            _texture.Apply(false, false);
            _nextRow = -1;
            return true;
        }

        private void EnsureSize(int w, int h)
        {
            if (_texture == null)
            {
                _texture = new Texture2D(w, h, TextureFormat.RGBA32, false);
                _texture.name = "GodSim Minimap";
                _texture.filterMode = FilterMode.Point;
                _texture.wrapMode = TextureWrapMode.Clamp;
            }
            else if (_texture.width != w || _texture.height != h)
            {
                _texture.Reinitialize(w, h);
            }
            if (_pixels == null || _pixels.Length != w * h) _pixels = new Color32[w * h];
            _width = w;
            _height = h;
        }

        private void PaintRow(WorldMap map, TerrainBuildInput input, int y)
        {
            int w = map.Width;
            float sea = MathUtil.Sanitize(map.SeaLevel);
            Color32[] biomeColors = input.BiomeColors;
            int lastOwner = int.MinValue;
            bool ownerHasColor = false;
            Color ownerColor = default(Color);

            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                Biome biome = map.Biomes[i];
                int bi = (int)biome;
                Color c = bi < biomeColors.Length ? (Color)biomeColors[bi] : new Color(0.5f, 0.5f, 0.5f, 1f);
                float e = MathUtil.Sanitize(map.Elevation[i]);

                if (biome.IsWater())
                {
                    float depth = MathUtil.Clamp01((sea - e) / Math.Max(0.05f, sea + 1f));
                    float k = MathUtil.Lerp(1.05f, 0.65f, depth);
                    c = new Color(c.r * k, c.g * k, c.b * k, 1f);
                }
                else
                {
                    float k = 0.88f + 0.22f * MathUtil.Clamp01((e - sea) / Math.Max(0.05f, 1f - sea));
                    c = new Color(c.r * k, c.g * k, c.b * k, 1f);
                    TileFlags f = map.Flags[i];
                    if ((f & TileFlags.Trees) != 0) c = Color.Lerp(c, ForestShade, 0.35f);
                    if ((f & TileFlags.Farmland) != 0) c = Color.Lerp(c, FarmColor, 0.6f);
                    if ((f & TileFlags.River) != 0) c = RiverColor;
                    if ((f & TileFlags.Road) != 0) c = RoadColor;
                    if (map.BuildingAt[i] >= 0) c = Color.Lerp(c, BuildingColor, 0.75f);
                    if ((f & TileFlags.Ruins) != 0) c = Color.Lerp(c, RuinsColor, 0.6f);
                    if ((f & TileFlags.Scorched) != 0) c = Color.Lerp(c, CharcoalColor, 0.7f);
                    if ((f & TileFlags.Burning) != 0) c = Color.Lerp(c, FireColor, 0.75f);

                    if (input.ShowTerritory)
                    {
                        int owner = map.Owner[i];
                        if (owner != lastOwner)
                        {
                            lastOwner = owner;
                            ownerHasColor = false;
                            if (owner >= 0 && input.SettlementColors.TryGetValue(owner, out Color32 oc))
                            {
                                ownerHasColor = true;
                                ownerColor = oc;
                            }
                        }
                        if (ownerHasColor)
                        {
                            bool border = Foreign(map, x + 1, y, owner) || Foreign(map, x - 1, y, owner)
                                       || Foreign(map, x, y + 1, owner) || Foreign(map, x, y - 1, owner);
                            c = Color.Lerp(c, ownerColor, border ? 0.7f : 0.3f);
                        }
                    }
                }
                c.a = 1f;
                _pixels[i] = c;
            }
        }

        private static bool Foreign(WorldMap map, int x, int y, int owner)
        {
            if (!map.InBounds(x, y)) return false;
            int i = y * map.Width + x;
            return map.Owner[i] != owner && !map.Biomes[i].IsWater();
        }

        private void PaintSettlements(WorldState state, TerrainBuildInput input)
        {
            var items = state.Settlements.Items;
            int baseRadius = Math.Max(1, _width / 192);
            for (int s = 0; s < items.Count; s++)
            {
                Settlement st = items[s];
                if (st == null || !st.IsAlive) continue;
                Color civ = input.SettlementColors.TryGetValue(st.Id, out Color32 sc) ? (Color)sc : new Color(0.9f, 0.9f, 0.9f, 1f);
                Color32 ring = Color.Lerp(civ, new Color(1f, 1f, 1f, 1f), 0.45f);
                Color32 core = st.IsCapital ? Gold : White;
                int r = baseRadius + (st.Tier >= SettlementTier.Town ? 1 : 0) + (st.Tier >= SettlementTier.Metropolis ? 1 : 0);
                int cx = st.Center.X, cy = st.Center.Y;
                int r2 = r * r + r;
                for (int dy = -r; dy <= r; dy++)
                {
                    int py = cy + dy;
                    if (py < 0 || py >= _height) continue;
                    for (int dx = -r; dx <= r; dx++)
                    {
                        int px = cx + dx;
                        if (px < 0 || px >= _width) continue;
                        int d2 = dx * dx + dy * dy;
                        if (d2 > r2) continue;
                        _pixels[py * _width + px] = d2 <= (r > 1 ? 1 : 0) ? core : ring;
                    }
                }
            }
        }

        /// <summary>Destroys the texture.</summary>
        public void Release()
        {
            if (_texture != null) UnityEngine.Object.Destroy(_texture);
            _texture = null;
            _pixels = null;
            _nextRow = -1;
        }
    }
}
