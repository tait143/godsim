// Minimal UnityEngine API stubs for the HUD compile check ONLY (never shipped, never under Assets/).
// Signatures mirror Unity 2022.3 / Unity 6 for exactly the members the HUD uses. Bodies are dummies.
#pragma warning disable CS0626, CS0660, CS0661
using System;

namespace UnityEngine
{
    [Flags]
    public enum HideFlags
    {
        None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8,
        DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61
    }

    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object obj) { }
        public static T Instantiate<T>(T original) where T : Object => original;
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
        public static bool operator ==(Object x, Object y) => ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !ReferenceEquals(x, y);
    }

    public class Component : Object { }
    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled => enabled;
    }
    public class MonoBehaviour : Behaviour { }
    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => null;
    }

    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public class PropertyAttribute : Attribute { }
    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class TooltipAttribute : PropertyAttribute { public readonly string tooltip; public TooltipAttribute(string tooltip) { this.tooltip = tooltip; } }
    [AttributeUsage(AttributeTargets.Class, Inherited = false)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string menuName) { } }

    public sealed class ExitGUIException : Exception { }

    public enum RuntimeInitializeLoadType { AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4 }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => new Vector2(0f, 0f);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
        public float xMax => x + width;
        public float yMax => y + height;
        public bool Contains(Vector2 point) => point.x >= x && point.x < xMax && point.y >= y && point.y < yMax;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color black => new Color(0f, 0f, 0f, 1f);
        public static Color clear => new Color(0f, 0f, 0f, 0f);
        public static Color Lerp(Color a, Color b, float t) => a;
        public static implicit operator Color32(Color c) => new Color32(0, 0, 0, 0);
        public static implicit operator Color(Color32 c) => new Color(0f, 0f, 0f, 0f);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Mathf
    {
        public static float Round(float f) => f;
        public static int RoundToInt(float f) => 0;
        public static int FloorToInt(float f) => 0;
        public static float Floor(float f) => f;
        public static float Ceil(float f) => f;
        public static float Max(float a, float b) => a;
        public static int Max(int a, int b) => a;
        public static float Min(float a, float b) => a;
        public static int Min(int a, int b) => a;
        public static float Clamp(float value, float min, float max) => value;
        public static int Clamp(int value, int min, int max) => value;
        public static float Clamp01(float value) => value;
        public static float Abs(float f) => f;
        public static float Sqrt(float f) => f;
        public static float Pow(float f, float p) => f;
        public static float Log10(float f) => f;
    }

    public static class Screen
    {
        public static int width => 0;
        public static int height => 0;
        public static float dpi => 0f;
    }

    public class Time
    {
        public static float unscaledTime => 0f;
        public static float unscaledDeltaTime => 0f;
        public static int frameCount => 0;
    }

    public class PlayerPrefs
    {
        public static bool HasKey(string key) => false;
        public static float GetFloat(string key, float defaultValue) => defaultValue;
        public static void SetFloat(string key, float value) { }
        public static void Save() { }
    }

    public class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public class Application
    {
        public static void Quit() { }
    }

    public class Input
    {
        public static Vector3 mousePosition => default(Vector3);
        public static bool GetMouseButton(int button) => false;
    }

    public enum TextureFormat { Alpha8 = 1, ARGB4444 = 2, RGB24 = 3, RGBA32 = 4, ARGB32 = 5 }
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }
    public enum ScaleMode { StretchToFill = 0, ScaleAndCrop = 1, ScaleToFit = 2 }
    public enum FontStyle { Normal = 0, Bold = 1, Italic = 2, BoldAndItalic = 3 }
    public enum TextAnchor { UpperLeft = 0, UpperCenter = 1, UpperRight = 2, MiddleLeft = 3, MiddleCenter = 4, MiddleRight = 5, LowerLeft = 6, LowerCenter = 7, LowerRight = 8 }
    public enum TextClipping { Overflow = 0, Clip = 1 }

    public class Texture : Object
    {
        public virtual int width { get; set; }
        public virtual int height { get; set; }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public void SetPixels32(Color32[] colors, int miplevel = 0) { }
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
    }

    public sealed class RectOffset
    {
        public RectOffset() { }
        public RectOffset(int left, int right, int top, int bottom) { this.left = left; this.right = right; this.top = top; this.bottom = bottom; }
        public int left { get; set; }
        public int right { get; set; }
        public int top { get; set; }
        public int bottom { get; set; }
    }

    public sealed class GUIStyleState
    {
        public Texture2D background { get; set; }
        public Color textColor { get; set; }
    }

    public class GUIContent
    {
        public GUIContent() { }
        public GUIContent(string text) { this.text = text; }
        public GUIContent(string text, string tooltip) { this.text = text; this.tooltip = tooltip; }
        public string text { get; set; }
        public string tooltip { get; set; }
        public static GUIContent none = new GUIContent("");
    }

    public sealed class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public string name { get; set; }
        public GUIStyleState normal { get; set; }
        public GUIStyleState hover { get; set; }
        public GUIStyleState active { get; set; }
        public GUIStyleState focused { get; set; }
        public GUIStyleState onNormal { get; set; }
        public GUIStyleState onHover { get; set; }
        public GUIStyleState onActive { get; set; }
        public GUIStyleState onFocused { get; set; }
        public RectOffset border { get; set; }
        public RectOffset margin { get; set; }
        public RectOffset padding { get; set; }
        public RectOffset overflow { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public bool wordWrap { get; set; }
        public bool richText { get; set; }
        public TextClipping clipping { get; set; }
        public float fixedWidth { get; set; }
        public float fixedHeight { get; set; }
        public void Draw(Rect position, bool isHover, bool isActive, bool on, bool hasKeyboardFocus) { }
        public void Draw(Rect position, GUIContent content, bool isHover, bool isActive, bool on, bool hasKeyboardFocus) { }
        public Vector2 CalcSize(GUIContent content) => default(Vector2);
        public float CalcHeight(GUIContent content, float width) => 0f;
    }

    public sealed class GUISettings
    {
        public Color cursorColor { get; set; }
        public Color selectionColor { get; set; }
    }

    public sealed class GUISkin : ScriptableObject
    {
        public GUIStyle box { get; set; }
        public GUIStyle label { get; set; }
        public GUIStyle textField { get; set; }
        public GUIStyle button { get; set; }
        public GUIStyle horizontalSlider { get; set; }
        public GUIStyle horizontalSliderThumb { get; set; }
        public GUIStyle verticalScrollbar { get; set; }
        public GUIStyle verticalScrollbarThumb { get; set; }
        public GUIStyle verticalScrollbarUpButton { get; set; }
        public GUIStyle verticalScrollbarDownButton { get; set; }
        public GUIStyle scrollView { get; set; }
        public GUISettings settings => null;
    }

    public enum EventType
    {
        MouseDown = 0, MouseUp = 1, MouseMove = 2, MouseDrag = 3, KeyDown = 4, KeyUp = 5, ScrollWheel = 6,
        Repaint = 7, Layout = 8, DragUpdated = 9, DragPerform = 10, Ignore = 11, Used = 12,
        ValidateCommand = 13, ExecuteCommand = 14, DragExited = 15, ContextClick = 16,
        MouseEnterWindow = 20, MouseLeaveWindow = 21
    }

    public enum KeyCode
    {
        None = 0, Return = 13, Escape = 27, Space = 32, Plus = 43, Minus = 45,
        Alpha0 = 48, Alpha1 = 49, Alpha2 = 50, Alpha3 = 51, Alpha4 = 52, Alpha5 = 53, Alpha6 = 54, Alpha7 = 55, Alpha8 = 56, Alpha9 = 57,
        Equals = 61, KeypadMinus = 269, KeypadPlus = 270, KeypadEnter = 271, F1 = 282, F3 = 284
    }

    public sealed class Event
    {
        public static Event current { get; set; }
        public EventType type { get; set; }
        public EventType rawType => type;
        public Vector2 mousePosition { get; set; }
        public int button { get; set; }
        public KeyCode keyCode { get; set; }
        public bool shift { get; set; }
        public void Use() { }
    }

    public class GUIUtility
    {
        public static int hotControl { get; set; }
        public static int keyboardControl { get; set; }
    }

    public class GUI
    {
        public static GUISkin skin { get; set; }
        public static Color color { get; set; }
        public static Color backgroundColor { get; set; }
        public static Color contentColor { get; set; }
        public static bool enabled { get; set; }
        public static bool Button(Rect position, GUIContent content, GUIStyle style) => false;
        public static bool Toggle(Rect position, bool value, GUIContent content, GUIStyle style) => value;
        public static string TextField(Rect position, string text, int maxLength, GUIStyle style) => text;
        public static float HorizontalSlider(Rect position, float value, float leftValue, float rightValue, GUIStyle slider, GUIStyle thumb) => value;
        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode, bool alphaBlend) { }
        public static Vector2 BeginScrollView(Rect position, Vector2 scrollPosition, Rect viewRect, bool alwaysShowHorizontal, bool alwaysShowVertical, GUIStyle horizontalScrollbar, GUIStyle verticalScrollbar) => scrollPosition;
        public static void EndScrollView() { }
        public static void SetNextControlName(string name) { }
        public static string GetNameOfFocusedControl() => "";
    }
}

namespace UnityEngine.InputSystem
{
    public abstract class InputControl { }

    public abstract class InputControl<TValue> : InputControl where TValue : struct
    {
        public TValue ReadValue() => default(TValue);
    }

    public class InputDevice : InputControl { }

    public class Pointer : InputDevice
    {
        public static Pointer current { get; }
        public Controls.Vector2Control position { get; }
    }

    public class Mouse : Pointer
    {
        public new static Mouse current { get; }
        public Controls.ButtonControl leftButton { get; }
        public Controls.ButtonControl rightButton { get; }
        public Controls.ButtonControl middleButton { get; }
    }
}

namespace UnityEngine.InputSystem.Controls
{
    public class Vector2Control : InputControl<Vector2> { }
    public class AxisControl : InputControl<float> { }
    public class ButtonControl : AxisControl { public bool isPressed => false; }
}
