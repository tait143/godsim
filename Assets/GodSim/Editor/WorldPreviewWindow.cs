using System;
using System.Collections.Generic;
using System.Reflection;
using GodSim.Core;
using GodSim.Core.World;
using UnityEditor;
using UnityEngine;

namespace GodSim.Unity.Editor
{
    /// <summary>
    /// Generates worlds with the real WorldGenerator and shows a biome map (optional hill shading, rivers and
    /// start locations) plus biome/deposit statistics. Lets designers browse seeds/presets without entering Play Mode.
    /// </summary>
    public sealed class WorldPreviewWindow : EditorWindow
    {
        private static readonly Color32[] BiomeColors =
        {
            new Color32(18, 42, 88, 255),    // DeepOcean
            new Color32(40, 92, 148, 255),   // ShallowWater
            new Color32(56, 118, 170, 255),  // Lake
            new Color32(224, 208, 152, 255), // Beach
            new Color32(118, 170, 74, 255),  // Grassland
            new Color32(188, 178, 94, 255),  // Savanna
            new Color32(46, 112, 52, 255),   // Forest
            new Color32(26, 90, 42, 255),    // Jungle
            new Color32(228, 198, 122, 255), // Desert
            new Color32(76, 96, 62, 255),    // Swamp
            new Color32(70, 106, 84, 255),   // Taiga
            new Color32(152, 162, 142, 255), // Tundra
            new Color32(134, 128, 82, 255),  // Hills
            new Color32(122, 114, 106, 255), // Mountain
            new Color32(240, 244, 248, 255), // Snow
            new Color32(72, 46, 42, 255),    // Volcanic
        };
        private static readonly Color32 RiverColor = new Color32(64, 140, 220, 255);
        private static readonly Color32 UnknownColor = new Color32(255, 0, 255, 255);

        [SerializeField] private long _seed = 20261119;
        [SerializeField] private int _width = 192;
        [SerializeField] private int _height = 192;
        [SerializeField] private WorldPreset _preset = WorldPreset.Continents;
        [SerializeField] private bool _elevationShading = true;
        [SerializeField] private bool _showRivers = true;
        [SerializeField] private bool _showStartLocations = true;
        [SerializeField] private bool _showStats = true;
        [SerializeField] private Vector2 _scroll;

        [NonSerialized] private Texture2D _texture;
        [NonSerialized] private Color32[] _pixels;
        [NonSerialized] private WorldMap _map;
        [NonSerialized] private RaceId[] _races;
        [NonSerialized] private Vec2i[] _starts;
        [NonSerialized] private string _startStatus;
        [NonSerialized] private string _error;
        [NonSerialized] private double _genMs;
        [NonSerialized] private int[] _biomeCounts;
        [NonSerialized] private int _landTiles, _riverTiles, _coastalTiles, _treeTiles, _fertileTiles;
        [NonSerialized] private int _ore, _stone, _gold, _coal, _oil;
        [NonSerialized] private GameDatabase _db;
        [NonSerialized] private ulong _generatedSeed;

        [MenuItem("GodSim/World Preview", false, 10)]
        public static void Open()
        {
            var window = GetWindow<WorldPreviewWindow>("World Preview");
            window.minSize = new Vector2(360f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            titleContent = new GUIContent("World Preview");
            wantsMouseMove = true;
        }

        private void OnDisable() { ReleaseTexture(); }

        private void OnDestroy() { ReleaseTexture(); }

        private void ReleaseTexture()
        {
            if (_texture != null) DestroyImmediate(_texture);
            _texture = null;
        }

        // ------------------------------------------------------------------ GUI
        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawSettings();
            if (!string.IsNullOrEmpty(_error)) EditorGUILayout.HelpBox(_error, MessageType.Error);
            if (_map != null && _texture != null)
            {
                DrawPreview();
                if (_showStats) DrawStatistics();
            }
            else if (string.IsNullOrEmpty(_error))
            {
                EditorGUILayout.HelpBox("Press Generate to build a world with the current seed, size and preset.", MessageType.Info);
            }
            EditorGUILayout.EndScrollView();

            if (Event.current != null && Event.current.type == EventType.MouseMove) Repaint();
        }

        private void DrawSettings()
        {
            EditorGUILayout.LabelField("World", EditorStyles.boldLabel);
            _seed = EditorGUILayout.LongField("Seed", _seed);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Randomize Seed"))
            {
                _seed = (long)GodSimEditorUtil.RandomSeed();
                GUI.FocusControl(null);
                Generate();
                GUIUtility.ExitGUI(); // layout changed mid-event
            }
            if (GUILayout.Button("Load From Settings"))
            {
                GUI.FocusControl(null);
                LoadFromSettings();
                GUIUtility.ExitGUI();
            }
            EditorGUILayout.EndHorizontal();

            int min = WorldMap.MinSize * 4, max = WorldMap.MaxSize;
            _width = EditorGUILayout.IntSlider("Width", _width, min, max);
            _height = EditorGUILayout.IntSlider("Height", _height, min, max);
            _preset = (WorldPreset)EditorGUILayout.EnumPopup("Preset", _preset);
            if ((long)_width * _height > 512L * 512L)
                EditorGUILayout.HelpBox("Large map (" + _width + "x" + _height + "): generation may take several seconds.", MessageType.Warning);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Display", EditorStyles.boldLabel);
            bool shading = EditorGUILayout.Toggle("Elevation Shading", _elevationShading);
            bool rivers = EditorGUILayout.Toggle("River Overlay", _showRivers);
            bool starts = EditorGUILayout.Toggle("Start Locations", _showStartLocations);
            _showStats = EditorGUILayout.Toggle("Statistics", _showStats);
            if (shading != _elevationShading || rivers != _showRivers || starts != _showStartLocations)
            {
                _elevationShading = shading;
                _showRivers = rivers;
                _showStartLocations = starts;
                if (_map != null) RenderPreview();
            }

            EditorGUILayout.Space();
            if (GUILayout.Button("Generate", GUILayout.Height(28f)))
            {
                Generate();
                GUIUtility.ExitGUI();
            }
        }

        private void DrawPreview()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Seed " + _generatedSeed + "  -  " + _map.Width + "x" + _map.Height + "  -  generated in " + _genMs.ToString("0") + " ms",
                EditorStyles.miniLabel);

            float available = Mathf.Max(64f, position.width - 24f);
            float aspect = _map.Height / (float)_map.Width;
            float w = available, h = available * aspect;
            if (h > 900f) { h = 900f; w = h / aspect; }
            Rect rect = GUILayoutUtility.GetRect(w, h, GUILayout.ExpandWidth(false));
            GUI.DrawTexture(rect, _texture, ScaleMode.StretchToFill);

            // Hover read-out (texture row 0 = map y 0 = bottom of the image).
            Event e = Event.current;
            string hover = " ";
            if (e != null && rect.Contains(e.mousePosition) && rect.width > 0f && rect.height > 0f)
            {
                int tx = Mathf.Clamp((int)((e.mousePosition.x - rect.x) / rect.width * _map.Width), 0, _map.Width - 1);
                int ty = Mathf.Clamp(_map.Height - 1 - (int)((e.mousePosition.y - rect.y) / rect.height * _map.Height), 0, _map.Height - 1);
                int i = _map.Index(tx, ty);
                hover = "(" + tx + ", " + ty + ")  " + _map.Biomes[i] + "  elev " + _map.Elevation[i].ToString("0.00")
                    + "  moist " + _map.Moisture[i].ToString("0.00") + "  temp " + _map.Temperature[i].ToString("0.00")
                    + "  fert " + _map.Fertility[i].ToString("0.00") + (_map.Has(i, TileFlags.River) ? "  river" : "");
            }
            EditorGUILayout.LabelField(hover, EditorStyles.miniLabel);
        }

        private void DrawStatistics()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Statistics", EditorStyles.boldLabel);
            int total = _map.TileCount;
            EditorGUILayout.LabelField("Land / Water", Pct(_landTiles, total) + " / " + Pct(total - _landTiles, total));
            EditorGUILayout.LabelField("River / Coastal tiles", _riverTiles + " / " + _coastalTiles);
            EditorGUILayout.LabelField("Woodland / Fertile tiles", _treeTiles + " / " + _fertileTiles);
            EditorGUILayout.LabelField("Deposits", "ore " + _ore + ", stone " + _stone + ", gold " + _gold + ", coal " + _coal + ", oil " + _oil);

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Biomes", EditorStyles.boldLabel);
            var order = new List<int>();
            for (int b = 0; b < _biomeCounts.Length; b++) if (_biomeCounts[b] > 0) order.Add(b);
            order.Sort((x, y) => _biomeCounts[y] != _biomeCounts[x] ? _biomeCounts[y].CompareTo(_biomeCounts[x]) : x.CompareTo(y));
            for (int k = 0; k < order.Count; k++)
            {
                int b = order[k];
                Rect row = EditorGUILayout.GetControlRect();
                var swatch = new Rect(row.x, row.y + 2f, 14f, row.height - 4f);
                EditorGUI.DrawRect(swatch, ToColor(ColorOf((Biome)b)));
                float frac = _biomeCounts[b] / (float)total;
                var bar = new Rect(row.x + row.width * 0.62f, row.y + 4f, row.width * 0.38f * frac, row.height - 8f);
                EditorGUI.DrawRect(bar, new Color(0.35f, 0.55f, 0.85f, 0.8f));
                var label = new Rect(row.x + 20f, row.y, row.width * 0.62f - 20f, row.height);
                GUI.Label(label, ((Biome)b) + "  " + _biomeCounts[b] + " (" + (frac * 100f).ToString("0.0") + "%)");
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Start Locations", EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(_startStatus)) EditorGUILayout.LabelField(_startStatus, EditorStyles.wordWrappedLabel);
            if (_starts != null && _races != null)
            {
                for (int i = 0; i < _starts.Length; i++)
                {
                    string race = i < _races.Length ? _races[i].ToString() : "#" + i;
                    Vec2i p = _starts[i];
                    string where = _map.InBounds(p) ? "(" + p.X + ", " + p.Y + ") " + _map.BiomeAt(p) : "not found";
                    EditorGUILayout.LabelField(race, where);
                }
            }
        }

        private static string Pct(int n, int total) => total > 0 ? (n * 100f / total).ToString("0.0") + "%" : "-";

        // ------------------------------------------------------------------ generation
        private void LoadFromSettings()
        {
            var settings = GodSimEditorUtil.FindSettings();
            if (settings == null)
            {
                _error = "No GodSimSettings asset found. Use GodSim/Create Settings Asset first.";
                return;
            }
            SimConfig cfg = GodSimEditorUtil.ConfigFrom(settings);
            _seed = unchecked((long)cfg.Seed);
            _width = cfg.MapWidth;
            _height = cfg.MapHeight;
            _preset = cfg.Preset;
            _error = null;
            Generate();
        }

        private void Generate()
        {
            _error = null;
            var cfg = new SimConfig { Seed = unchecked((ulong)_seed), MapWidth = _width, MapHeight = _height, Preset = _preset };
            var settings = GodSimEditorUtil.FindSettings();
            if (settings != null) cfg.StartingRaces = (RaceId[])GodSimEditorUtil.ConfigFrom(settings).StartingRaces.Clone();
            cfg.Sanitize();

            try
            {
                EditorUtility.DisplayProgressBar("GodSim World Preview", "Generating " + cfg.MapWidth + "x" + cfg.MapHeight + " world...", 0.2f);
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var rng = new Rng(cfg.Seed);
                _map = WorldGenerator.Generate(WorldGenSettings.FromConfig(cfg), rng);
                _genMs = watch.Elapsed.TotalMilliseconds;
                _generatedSeed = cfg.Seed;
                _races = cfg.StartingRaces;

                EditorUtility.DisplayProgressBar("GodSim World Preview", "Computing statistics...", 0.6f);
                ComputeStatistics();

                EditorUtility.DisplayProgressBar("GodSim World Preview", "Finding start locations...", 0.7f);
                FindStartLocations(rng);

                EditorUtility.DisplayProgressBar("GodSim World Preview", "Rendering preview...", 0.9f);
                RenderPreview();
            }
            catch (Exception ex)
            {
                _map = null;
                ReleaseTexture();
                _error = "World generation failed: " + ex.Message;
                Debug.LogError(GodSimEditorUtil.LogPrefix + "World preview generation failed: " + ex);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
            Repaint();
        }

        private void ComputeStatistics()
        {
            _biomeCounts = new int[Math.Max(Counts.Biomes, BiomeColors.Length)];
            _landTiles = _riverTiles = _coastalTiles = _treeTiles = _fertileTiles = 0;
            _ore = _stone = _gold = _coal = _oil = 0;
            int n = _map.TileCount;
            for (int i = 0; i < n; i++)
            {
                int b = (int)_map.Biomes[i];
                if (b >= 0 && b < _biomeCounts.Length) _biomeCounts[b]++;
                if (!_map.IsWater(i)) _landTiles++;
                TileFlags f = _map.Flags[i];
                if ((f & TileFlags.River) != 0) _riverTiles++;
                if ((f & TileFlags.Coastal) != 0) _coastalTiles++;
                if ((f & TileFlags.Trees) != 0) _treeTiles++;
                if ((f & TileFlags.FertileSoil) != 0) _fertileTiles++;
                if ((f & TileFlags.OreDeposit) != 0) _ore++;
                if ((f & TileFlags.StoneDeposit) != 0) _stone++;
                if ((f & TileFlags.GoldDeposit) != 0) _gold++;
                if ((f & TileFlags.CoalDeposit) != 0) _coal++;
                if ((f & TileFlags.OilDeposit) != 0) _oil++;
            }
        }

        /// <summary>Calls GodSim.Core.World.StartLocationFinder.Find via reflection when that module is present.</summary>
        private void FindStartLocations(Rng rng)
        {
            _starts = null;
            Type finder = typeof(WorldGenerator).Assembly.GetType("GodSim.Core.World.StartLocationFinder", false);
            MethodInfo find = finder == null ? null : finder.GetMethod("Find", BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(WorldMap), typeof(GameDatabase), typeof(IReadOnlyList<RaceId>), typeof(Rng) }, null);
            if (find == null)
            {
                _startStatus = "StartLocationFinder.Find is not available in GodSim.Core.";
                return;
            }
            try
            {
                if (_db == null) _db = GodSimEditorUtil.BuildDatabase();
                object result = find.Invoke(null, new object[] { _map, _db, (IReadOnlyList<RaceId>)_races, rng });
                var list = new List<Vec2i>();
                if (result is IEnumerable<Vec2i> points) foreach (var p in points) list.Add(p);
                _starts = list.ToArray();
                int found = 0;
                for (int i = 0; i < _starts.Length; i++) if (_map.InBounds(_starts[i])) found++;
                _startStatus = found + " of " + _races.Length + " start locations found.";
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                _startStatus = "Start location search failed: " + inner.Message;
                Debug.LogWarning(GodSimEditorUtil.LogPrefix + "StartLocationFinder.Find failed: " + inner);
            }
        }

        // ------------------------------------------------------------------ rendering
        private void RenderPreview()
        {
            int w = _map.Width, h = _map.Height, n = w * h;
            if (_texture == null || _texture.width != w || _texture.height != h)
            {
                ReleaseTexture();
                _texture = new Texture2D(w, h, TextureFormat.RGBA32, false)
                {
                    name = "GodSim World Preview",
                    hideFlags = HideFlags.HideAndDontSave,
                    filterMode = FilterMode.Point,
                    wrapMode = TextureWrapMode.Clamp
                };
            }
            if (_pixels == null || _pixels.Length != n) _pixels = new Color32[n];

            float sea = _map.SeaLevel;
            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    int i = y * w + x;
                    Color32 c = ColorOf(_map.Biomes[i]);
                    bool water = _map.IsWater(i);
                    if (_showRivers && !water && _map.Has(i, TileFlags.River)) c = RiverColor;
                    if (_elevationShading)
                    {
                        float e = Sanitize(_map.Elevation[i]);
                        if (water)
                        {
                            float depth = Mathf.Clamp01((sea - e) / Mathf.Max(0.05f, sea + 1f));
                            c = Scale(c, 1.15f - 0.55f * depth);
                        }
                        else
                        {
                            // Hill shading lit from the north-west plus a gentle brightness ramp with altitude.
                            float ex = Sanitize(_map.Elevation[y * w + Math.Min(w - 1, x + 1)]) - Sanitize(_map.Elevation[y * w + Math.Max(0, x - 1)]);
                            float ey = Sanitize(_map.Elevation[Math.Min(h - 1, y + 1) * w + x]) - Sanitize(_map.Elevation[Math.Max(0, y - 1) * w + x]);
                            float height01 = Mathf.Clamp01((e - sea) / Mathf.Max(0.05f, 1f - sea));
                            float shade = 0.9f + 0.25f * height01 + (ex - ey) * 4f;
                            c = Scale(c, Mathf.Clamp(shade, 0.55f, 1.35f));
                        }
                    }
                    _pixels[i] = c;
                }
            }

            if (_showStartLocations && _starts != null) DrawStartMarkers(w, h);

            _texture.SetPixels32(_pixels);
            _texture.Apply(false);
        }

        private void DrawStartMarkers(int w, int h)
        {
            int r = Math.Max(2, Math.Max(w, h) / 96);
            for (int s = 0; s < _starts.Length; s++)
            {
                Vec2i p = _starts[s];
                if (!_map.InBounds(p)) continue;
                Color32 fill = new Color32(255, 255, 255, 255);
                if (_db != null && _races != null && s < _races.Length)
                {
                    int ri = (int)_races[s];
                    if (ri >= 0 && ri < _db.Races.Count && _db.Races[ri] != null) fill = FromRgba(_db.Races[ri].BannerColor);
                }
                for (int dy = -r - 1; dy <= r + 1; dy++)
                {
                    for (int dx = -r - 1; dx <= r + 1; dx++)
                    {
                        int x = p.X + dx, y = p.Y + dy;
                        if (x < 0 || y < 0 || x >= w || y >= h) continue;
                        bool border = Math.Abs(dx) == r + 1 || Math.Abs(dy) == r + 1;
                        _pixels[y * w + x] = border ? new Color32(0, 0, 0, 255) : fill;
                    }
                }
            }
        }

        private static float Sanitize(float v) => float.IsNaN(v) || float.IsInfinity(v) ? 0f : v;

        private static Color32 ColorOf(Biome b)
        {
            int i = (int)b;
            return i >= 0 && i < BiomeColors.Length ? BiomeColors[i] : UnknownColor;
        }

        private static Color32 Scale(Color32 c, float f) =>
            new Color32(ToByte(c.r * f), ToByte(c.g * f), ToByte(c.b * f), c.a);

        private static byte ToByte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v), 0, 255);

        private static Color32 FromRgba(uint rgba) =>
            new Color32((byte)(rgba >> 24), (byte)(rgba >> 16), (byte)(rgba >> 8), 255);

        private static Color ToColor(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, 1f);
    }
}
