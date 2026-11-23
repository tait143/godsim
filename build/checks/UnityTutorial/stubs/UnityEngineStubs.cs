// Minimal UnityEngine API stubs for the UnityTutorial compile check ONLY (never copy into Assets/).
// Only members the tutorial uses; every signature mirrors real Unity 6 / 2022.3. Bodies are placeholders.
#pragma warning disable CS0660, CS0661, CS1591
using System;

namespace UnityEngine
{
    public enum HideFlags
    {
        None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8,
        DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61
    }

    public class Object
    {
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object obj) { }
        public static bool operator ==(Object x, Object y) => ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !ReferenceEquals(x, y);
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    public class Component : Object
    {
        public Transform transform => null;
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
    }

    public class MonoBehaviour : Behaviour
    {
        public bool useGUILayout { get; set; }
    }

    public sealed class Camera : Behaviour
    {
        public static Camera main => null;
        public bool orthographic { get; set; }
        public float orthographicSize { get; set; }
        public float fieldOfView { get; set; }
    }

    public struct Vector2
    {
        public float x;
        public float y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static float Distance(Vector3 a, Vector3 b) => 0f;
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static float Angle(Quaternion a, Quaternion b) => 0f;
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
        public float xMin { get => x; set { } }
        public float yMin { get => y; set { } }
        public float xMax { get => x + width; set { } }
        public float yMax { get => y + height; set { } }
        public Vector2 center { get => new Vector2(x + width / 2f, y + height / 2f); set { } }
        public bool Contains(Vector2 point) => false;
    }

    public class RectOffset
    {
        public RectOffset(int left, int right, int top, int bottom) { }
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color clear => new Color(0f, 0f, 0f, 0f);
        public static Color Lerp(Color a, Color b, float t) => a;
    }

    public static class Mathf
    {
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Clamp(float value, float min, float max) => value;
        public static float Clamp01(float value) => value;
        public static float Abs(float f) => Math.Abs(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Round(float f) => (float)Math.Round(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
    }

    public sealed class Screen
    {
        public static int width => 0;
        public static int height => 0;
        public static float dpi => 0f;
    }

    public class Time
    {
        public static float unscaledTime => 0f;
        public static float unscaledDeltaTime => 0f;
    }

    public class PlayerPrefs
    {
        public static void SetInt(string key, int value) { }
        public static int GetInt(string key, int defaultValue) => defaultValue;
        public static void SetString(string key, string value) { }
        public static string GetString(string key, string defaultValue) => defaultValue;
        public static void Save() { }
    }

    public enum TextureFormat { Alpha8 = 1, ARGB4444 = 2, RGB24 = 3, RGBA32 = 4, ARGB32 = 5 }
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }

    public class Texture : Object
    {
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public void SetPixels(Color[] colors) { }
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
    }

    public enum FontStyle { Normal = 0, Bold = 1, Italic = 2, BoldAndItalic = 3 }
    public enum TextAnchor { UpperLeft = 0, UpperCenter = 1, UpperRight = 2, MiddleLeft = 3, MiddleCenter = 4, MiddleRight = 5, LowerLeft = 6, LowerCenter = 7, LowerRight = 8 }
    public enum TextClipping { Overflow = 0, Clip = 1 }

    public sealed class GUIStyleState
    {
        public Texture2D background { get; set; }
        public Color textColor { get; set; }
    }

    public sealed class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public static GUIStyle none => new GUIStyle();
        public GUIStyleState normal { get; set; }
        public GUIStyleState hover { get; set; }
        public GUIStyleState active { get; set; }
        public GUIStyleState focused { get; set; }
        public RectOffset border { get; set; }
        public RectOffset margin { get; set; }
        public RectOffset padding { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public bool wordWrap { get; set; }
        public bool richText { get; set; }
        public TextClipping clipping { get; set; }
        public Vector2 CalcSize(GUIContent content) => default;
        public float CalcHeight(GUIContent content, float width) => 0f;
    }

    public sealed class GUISkin : Object
    {
        public GUIStyle label { get; set; }
    }

    public class GUIContent
    {
        public static GUIContent none = new GUIContent("");
        public GUIContent() { }
        public GUIContent(string text) { }
        public GUIContent(string text, string tooltip) { }
        public string text { get; set; }
    }

    public class GUI
    {
        public static GUISkin skin { get; set; }
        public static Color color { get; set; }
        public static bool enabled { get; set; }
        public static int depth { get; set; }
        public static void Label(Rect position, GUIContent content, GUIStyle style) { }
        public static void Box(Rect position, GUIContent content, GUIStyle style) { }
        public static bool Button(Rect position, GUIContent content, GUIStyle style) => false;
        public static bool Toggle(Rect position, bool value, GUIContent content, GUIStyle style) => value;
        public static void DrawTexture(Rect position, Texture image) { }
    }

    public class GUIUtility
    {
        public static int keyboardControl { get; set; }
    }

    public enum EventType
    {
        MouseDown = 0, MouseUp = 1, MouseMove = 2, MouseDrag = 3, KeyDown = 4, KeyUp = 5, ScrollWheel = 6,
        Repaint = 7, Layout = 8, DragUpdated = 9, DragPerform = 10, DragExited = 15, Ignore = 11, Used = 12,
        ValidateCommand = 13, ExecuteCommand = 14, ContextClick = 16, MouseEnterWindow = 20, MouseLeaveWindow = 21
    }

    public enum KeyCode
    {
        None = 0, Return = 13, Escape = 27, Space = 32, KeypadEnter = 271, F1 = 282
    }

    public sealed class Event
    {
        public static Event current { get; set; }
        public EventType type { get; set; }
        public KeyCode keyCode { get; set; }
        public Vector2 mousePosition { get; set; }
        public void Use() { }
    }

    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DisallowMultipleComponent : Attribute { }

    public abstract class PropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public class HeaderAttribute : PropertyAttribute
    {
        public readonly string header;
        public HeaderAttribute(string header) { this.header = header; }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class TooltipAttribute : PropertyAttribute
    {
        public readonly string tooltip;
        public TooltipAttribute(string tooltip) { this.tooltip = tooltip; }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class RangeAttribute : PropertyAttribute
    {
        public readonly float min;
        public readonly float max;
        public RangeAttribute(float min, float max) { this.min = min; this.max = max; }
    }
}
