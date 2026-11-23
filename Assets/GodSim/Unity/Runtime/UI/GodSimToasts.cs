using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Transient on-screen notifications ("toasts"), drawn by <see cref="GodSimHUD"/> under the top bar.
    /// Safe to call from any main-thread code at any time, even when no HUD exists yet (messages wait in a
    /// small bounded queue). Identical messages that are still visible are merged with a repeat counter.
    /// </summary>
    public static class GodSimToasts
    {
        private sealed class Toast
        {
            public string Message;
            public readonly GUIContent Content = new GUIContent();
            public Color Color;
            public float Shown;      // unscaled time when it became visible (or was refreshed)
            public float Seconds;
            public int Repeats;
            public float Height;     // measured height (0 = needs measurement)
            public float MeasuredWidth;
        }

        /// <summary>Maximum toasts visible at once; extra ones wait in the queue.</summary>
        private const int MaxVisible = 5;
        private const int MaxQueued = 24;
        private const int MaxLength = 220;

        private static readonly Toast[] Visible = new Toast[MaxVisible];
        private static readonly Toast[] Queue = new Toast[MaxQueued];
        private static int _visibleCount;
        private static int _queueHead, _queueCount;

        /// <summary>Shows a notification for <paramref name="seconds"/> (clamped 0.5..30) in the given accent color.</summary>
        public static void Show(string message, Color color, float seconds = 4f)
        {
            if (string.IsNullOrEmpty(message)) return;
            if (message.Length > MaxLength) message = message.Substring(0, MaxLength) + "...";
            seconds = MathUtil.Clamp(MathUtil.Sanitize(seconds, 4f), 0.5f, 30f);
            float now = Now();

            for (int i = 0; i < _visibleCount; i++)
            {
                var v = Visible[i];
                if (string.Equals(v.Message, message, System.StringComparison.Ordinal))
                {
                    v.Repeats++;
                    v.Content.text = message + "  (x" + (v.Repeats + 1) + ")";
                    v.Shown = now;
                    v.Seconds = Mathf.Max(v.Seconds, seconds);
                    v.Height = 0f;
                    return;
                }
            }

            if (_queueCount == MaxQueued) { _queueHead = (_queueHead + 1) % MaxQueued; _queueCount--; } // drop oldest
            int slot = (_queueHead + _queueCount) % MaxQueued;
            var t = Queue[slot] ?? (Queue[slot] = new Toast());
            t.Message = message;
            t.Content.text = message;
            t.Color = color;
            t.Seconds = seconds;
            t.Repeats = 0;
            t.Height = 0f;
            _queueCount++;
            Promote(now);
        }

        /// <summary>Removes every visible and queued toast (e.g. when a new world starts).</summary>
        public static void Clear()
        {
            for (int i = 0; i < _visibleCount; i++) Visible[i] = null;
            _visibleCount = 0;
            _queueCount = 0;
        }

        internal static int VisibleCount => _visibleCount;

        private static float Now()
        {
            return Time.unscaledTime;
        }

        private static void Promote(float now)
        {
            while (_visibleCount < MaxVisible && _queueCount > 0)
            {
                var q = Queue[_queueHead];
                Queue[_queueHead] = null;
                _queueHead = (_queueHead + 1) % MaxQueued;
                _queueCount--;
                if (q == null) continue;
                q.Shown = now;
                Visible[_visibleCount++] = q;
            }
        }

        /// <summary>Expires old toasts and promotes queued ones. Called by the HUD once per frame.</summary>
        internal static void Update()
        {
            float now = Now();
            int w = 0;
            for (int i = 0; i < _visibleCount; i++)
            {
                var v = Visible[i];
                if (v != null && now - v.Shown < v.Seconds) Visible[w++] = v;
            }
            for (int i = w; i < _visibleCount; i++) Visible[i] = null;
            _visibleCount = w;
            Promote(now);
        }

        /// <summary>Draws the stack centered at <paramref name="centerX"/> starting at <paramref name="top"/>. Non-interactive.</summary>
        internal static void Draw(HudSkin skin, float centerX, float top, float maxWidth)
        {
            if (_visibleCount == 0 || skin == null || !HudDraw.IsRepaint) return;
            float now = Now();
            float width = Mathf.Min(maxWidth, skin.U(460f));
            float y = top;
            float accentW = Mathf.Max(2f, skin.U(4f));
            for (int i = 0; i < _visibleCount; i++)
            {
                var t = Visible[i];
                if (t == null) continue;
                if (t.Height <= 0f || t.MeasuredWidth != width)
                {
                    t.Height = Mathf.Max(skin.U(30f), skin.Toast.CalcHeight(t.Content, width));
                    t.MeasuredWidth = width;
                }
                float age = now - t.Shown;
                float alpha = Mathf.Clamp01(Mathf.Min(age / 0.18f, (t.Seconds - age) / 0.6f));
                float slide = (1f - Mathf.Clamp01(age / 0.18f)) * skin.U(-10f);
                var r = new Rect(centerX - width * 0.5f, y + slide, width, t.Height);
                Color prev = GUI.color;
                Color prevContent = GUI.contentColor;
                GUI.color = new Color(prev.r, prev.g, prev.b, prev.a * alpha);
                GUI.contentColor = Color.Lerp(HudSkin.Text, t.Color, 0.35f);
                skin.Toast.Draw(r, t.Content, false, false, false, false);
                GUI.contentColor = prevContent;
                HudDraw.Paint(new Rect(r.x + skin.U(4f), r.y + skin.U(5f), accentW, r.height - skin.U(10f)), skin.BarFill, t.Color);
                GUI.color = prev;
                y += (t.Height + skin.Gap) * Mathf.Max(0.2f, alpha);
            }
        }
    }
}
