using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>Allocation-free IMGUI drawing helpers shared by the HUD panels.</summary>
    internal static class HudDraw
    {
        private static readonly GUIContent Empty = new GUIContent();

        public static bool IsRepaint => Event.current != null && Event.current.type == EventType.Repaint;

        /// <summary>Draws a style's background (9-slice) without creating a control. Repaint only.</summary>
        public static void Paint(Rect r, GUIStyle s)
        {
            if (s == null || !IsRepaint) return;
            s.Draw(r, false, false, false, false);
        }

        /// <summary>Draws a style's background tinted by <paramref name="tint"/>. Repaint only.</summary>
        public static void Paint(Rect r, GUIStyle s, Color tint)
        {
            if (s == null || !IsRepaint) return;
            Color prev = GUI.color;
            GUI.color = new Color(prev.r * tint.r, prev.g * tint.g, prev.b * tint.b, prev.a * tint.a);
            s.Draw(r, false, false, false, false);
            GUI.color = prev;
        }

        public static void Label(Rect r, GUIContent c, GUIStyle s)
        {
            if (c == null || !IsRepaint) return;
            s.Draw(r, c, false, false, false, false);
        }

        public static void Label(Rect r, GUIContent c, GUIStyle s, Color color)
        {
            if (c == null || !IsRepaint) return;
            Color prev = GUI.contentColor;
            GUI.contentColor = color;
            s.Draw(r, c, false, false, false, false);
            GUI.contentColor = prev;
        }

        /// <summary>Horizontal progress bar 0..1.</summary>
        public static void Bar(HudSkin skin, Rect r, float t, Color fill)
        {
            if (!IsRepaint) return;
            skin.BarBack.Draw(r, false, false, false, false);
            t = MathUtil.Clamp01(MathUtil.Sanitize(t));
            if (t <= 0f) return;
            float minW = skin.BarFill.border.left + skin.BarFill.border.right;
            float w = Mathf.Max(Mathf.Min(minW, r.width), r.width * t);
            Paint(new Rect(r.x, r.y, w, r.height), skin.BarFill, fill);
        }

        /// <summary>Bar with a centered origin for signed values in -1..1 (e.g. god favor).</summary>
        public static void SignedBar(HudSkin skin, Rect r, float v, Color neg, Color pos)
        {
            if (!IsRepaint) return;
            skin.BarBack.Draw(r, false, false, false, false);
            v = MathUtil.Clamp(MathUtil.Sanitize(v), -1f, 1f);
            float half = r.width * 0.5f;
            float w = Mathf.Abs(v) * half;
            if (w >= 1f)
            {
                var fr = v >= 0f ? new Rect(r.x + half, r.y, w, r.height) : new Rect(r.x + half - w, r.y, w, r.height);
                Paint(fr, skin.White, v >= 0f ? pos : neg);
            }
            Paint(new Rect(r.x + half - 0.5f, r.y, 1f, r.height), skin.White, new Color(1f, 1f, 1f, 0.45f));
        }

        /// <summary>Bar with a label drawn on top (centered).</summary>
        public static void Bar(HudSkin skin, Rect r, float t, Color fill, GUIContent text)
        {
            Bar(skin, r, t, fill);
            if (text != null && IsRepaint) skin.LabelCenterSmall.Draw(r, text, false, false, false, false);
        }

        public static void Swatch(HudSkin skin, Rect r, Color c) => Paint(r, skin.Swatch, c);

        public static void HLine(HudSkin skin, float x, float y, float w, Color c) => Paint(new Rect(x, y, w, Mathf.Max(1f, Mathf.Round(skin.Scale * 0.75f))), skin.White, c);

        /// <summary>A checkbox with a label; returns the new value. Uses a real toggle control for input.</summary>
        public static bool Check(HudSkin skin, Rect r, bool value, GUIContent label)
        {
            float box = Mathf.Round(14f * skin.Scale);
            bool nv = GUI.Toggle(r, value, Empty, skin.Row);
            var br = new Rect(r.x + skin.Gap, r.y + (r.height - box) * 0.5f, box, box);
            Paint(br, skin.CheckBox);
            if (nv)
            {
                float inset = Mathf.Round(3f * skin.Scale);
                Paint(new Rect(br.x + inset, br.y + inset, box - inset * 2f, box - inset * 2f), skin.CheckMark);
            }
            Label(new Rect(br.xMax + skin.Gap * 1.5f, r.y, r.width - box - skin.Gap * 3f, r.height), label, skin.Label);
            return nv;
        }

        /// <summary>Unpacks a 0xRRGGBBAA color.</summary>
        public static Color FromRgba(uint rgba)
        {
            return new Color(((rgba >> 24) & 0xFF) / 255f, ((rgba >> 16) & 0xFF) / 255f, ((rgba >> 8) & 0xFF) / 255f, (rgba & 0xFF) / 255f);
        }

        public static Color32 FromRgba32(uint rgba)
        {
            return new Color32((byte)((rgba >> 24) & 0xFF), (byte)((rgba >> 16) & 0xFF), (byte)((rgba >> 8) & 0xFF), 255);
        }

        public static Color WithAlpha(Color c, float a) { c.a = a; return c; }

        /// <summary>Color for a chronicle category (also used for filter chips).</summary>
        public static Color CategoryColor(ChronicleCategory c)
        {
            switch (c)
            {
                case ChronicleCategory.World: return new Color(0.75f, 0.78f, 0.82f, 1f);
                case ChronicleCategory.Birth: return new Color(0.55f, 0.88f, 0.62f, 1f);
                case ChronicleCategory.Death: return new Color(0.62f, 0.60f, 0.66f, 1f);
                case ChronicleCategory.Building: return new Color(0.86f, 0.70f, 0.48f, 1f);
                case ChronicleCategory.Settlement: return new Color(0.50f, 0.80f, 0.95f, 1f);
                case ChronicleCategory.Civilization: return new Color(0.98f, 0.86f, 0.42f, 1f);
                case ChronicleCategory.Age: return HudSkin.Gold;
                case ChronicleCategory.Technology: return HudSkin.Research;
                case ChronicleCategory.War: return HudSkin.Bad;
                case ChronicleCategory.Diplomacy: return new Color(0.55f, 0.72f, 1f, 1f);
                case ChronicleCategory.Divine: return HudSkin.Faith;
                case ChronicleCategory.Disaster: return new Color(1f, 0.55f, 0.25f, 1f);
                default: return HudSkin.Text;
            }
        }

        public static Color PowerCategoryColor(GodPowerCategory c)
        {
            switch (c)
            {
                case GodPowerCategory.Blessing: return new Color(0.55f, 0.88f, 0.62f, 1f);
                case GodPowerCategory.Shaping: return new Color(0.86f, 0.70f, 0.48f, 1f);
                case GodPowerCategory.Creation: return HudSkin.Research;
                case GodPowerCategory.Wrath: return HudSkin.Bad;
                default: return HudSkin.Text;
            }
        }
    }
}
