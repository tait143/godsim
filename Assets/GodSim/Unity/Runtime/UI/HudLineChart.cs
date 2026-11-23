using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// CPU-rasterized line chart into a cached <see cref="Texture2D"/> (no GL, no per-frame cost): one texel
    /// column per sample. Re-rendered only when the history version changes (at most a few times per second).
    /// </summary>
    internal sealed class HudLineChart
    {
        public const int Width = PopulationHistory.Capacity;
        public const int Height = 96;

        private Texture2D _tex;
        private Color32[] _px;
        private int _renderedVersion = -1;
        private static readonly Color32 Clear = new Color32(0, 0, 0, 0);
        private static readonly Color32 Grid = new Color32(255, 255, 255, 22);

        public Texture2D Texture => _tex;
        public float MaxValue { get; private set; } = 1f;

        /// <summary>Re-renders when the history changed. Returns true when the texture was updated.</summary>
        public bool Render(PopulationHistory h, bool force = false)
        {
            if (h == null) return false;
            if (!force && h.Version == _renderedVersion && _tex != null) return false;
            _renderedVersion = h.Version;
            if (_tex == null)
            {
                _tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
                _tex.hideFlags = HideFlags.HideAndDontSave;
                _tex.filterMode = FilterMode.Bilinear;
                _tex.wrapMode = TextureWrapMode.Clamp;
                _px = new Color32[Width * Height];
            }

            for (int i = 0; i < _px.Length; i++) _px[i] = Clear;
            for (int g = 1; g < 4; g++)
            {
                int gy = g * (Height - 1) / 4;
                for (int x = 0; x < Width; x++) _px[gy * Width + x] = Grid;
            }

            float max = NiceCeil(h.Max());
            MaxValue = max;
            int n = h.Count;
            int x0 = Width - n; // right-align: newest sample at the right edge
            var series = h.All;
            for (int s = 0; s < series.Count; s++)
            {
                var se = series[s];
                var vals = se.Values;
                int px = -1, py = -1;
                for (int i = 0; i < n; i++)
                {
                    float v = vals[h.Slot(i)];
                    if (float.IsNaN(v)) { px = -1; continue; }
                    int x = x0 + i;
                    int y = MathUtil.Clamp((int)(v / max * (Height - 3)) + 1, 0, Height - 1);
                    if (px >= 0) Line(px, py, x, y, se.Color); else Plot(x, y, se.Color);
                    px = x; py = y;
                }
            }
            _tex.SetPixels32(_px);
            _tex.Apply(false, false);
            return true;
        }

        public void Dispose()
        {
            if (_tex != null) Object.Destroy(_tex);
            _tex = null;
            _px = null;
            _renderedVersion = -1;
        }

        private static float NiceCeil(float v)
        {
            if (!(v > 1f)) return 1f;
            float mag = Mathf.Pow(10f, Mathf.Floor(Mathf.Log10(v)));
            float n = v / mag;
            float nice = n <= 1f ? 1f : n <= 2f ? 2f : n <= 2.5f ? 2.5f : n <= 5f ? 5f : 10f;
            return nice * mag;
        }

        private void Plot(int x, int y, Color32 c)
        {
            if (x < 0 || x >= Width) return;
            if (y >= 0 && y < Height) _px[y * Width + x] = c;
            if (y + 1 >= 0 && y + 1 < Height) _px[(y + 1) * Width + x] = c; // 2px thick
        }

        private void Line(int x0, int y0, int x1, int y1, Color32 c)
        {
            int dx = System.Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
            int dy = -System.Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;
            for (int guard = 0; guard < 4096; guard++)
            {
                Plot(x0, y0, c);
                if (x0 == x1 && y0 == y1) break;
                int e2 = 2 * err;
                if (e2 >= dy) { err += dy; x0 += sx; }
                if (e2 <= dx) { err += dx; y0 += sy; }
            }
        }
    }
}
