// Minimal UnityEngine API stubs for the UnityCinematic compile check ONLY (never copy into Assets/).
// Only members the cinematic code uses; each mirrors the real Unity 6 / 2022.3 signature. Bodies are placeholders.
#pragma warning disable CS0660, CS0661, CS1591, CS0067
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
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object obj) { }
        public static bool operator ==(Object x, Object y) => ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !ReferenceEquals(x, y);
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform => null;
        public T AddComponent<T>() where T : Component => null;
    }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Vector3 eulerAngles { get; set; }
        public Vector3 forward { get; set; }
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation) { }
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled => enabled;
    }

    public class MonoBehaviour : Behaviour { }

    public sealed class Camera : Behaviour
    {
        public static Camera main => null;
        public float fieldOfView { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public RenderTexture targetTexture { get; set; }
        public void Render() { }
        public void CopyFrom(Camera other) { }
    }

    public class Texture : Object { }

    public enum TextureFormat { Alpha8 = 1, ARGB4444 = 2, RGB24 = 3, RGBA32 = 4, ARGB32 = 5 }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public static Texture2D whiteTexture => null;
        public void ReadPixels(Rect source, int destX, int destY, bool recalculateMipMaps) { }
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
    }

    public class RenderTexture : Texture
    {
        public RenderTexture(int width, int height, int depth) { }
        public static RenderTexture active { get; set; }
        public void Release() { }
    }

    public static class ImageConversion
    {
        public static byte[] EncodeToPNG(this Texture2D tex) => new byte[0];
    }

    public static class ScreenCapture
    {
        public static void CaptureScreenshot(string filename) { }
        public static void CaptureScreenshot(string filename, int superSize) { }
    }

    public struct Vector2
    {
        public float x;
        public float y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
    }

    public struct Vector3
    {
        public float x;
        public float y;
        public float z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 forward => new Vector3(0f, 0f, 1f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);
        public float sqrMagnitude => x * x + y * y + z * z;
        public float magnitude => (float)Math.Sqrt(sqrMagnitude);
        public Vector3 normalized => this;
        public void Normalize() { }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a;
        public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime, float maxSpeed, float deltaTime) => target;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion Euler(float x, float y, float z) => default;
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards) => default;
        public static Vector3 operator *(Quaternion rotation, Vector3 point) => point;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color black => new Color(0f, 0f, 0f, 1f);
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
    }

    public static class Mathf
    {
        public const float Infinity = float.PositiveInfinity;
        public const float Deg2Rad = (float)(Math.PI / 180.0);
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Clamp(float value, float min, float max) => value;
        public static int Clamp(int value, int min, int max) => value;
        public static float Clamp01(float value) => value;
        public static float Lerp(float a, float b, float t) => a;
        public static float SmoothStep(float from, float to, float t) => t;
        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime, float maxSpeed, float deltaTime) => target;
        public static float MoveTowards(float current, float target, float maxDelta) => target;
        public static float Repeat(float t, float length) => t;
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Tan(float f) => (float)Math.Tan(f);
        public static float Atan(float f) => (float)Math.Atan(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Round(float f) => (float)Math.Round(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
        public static int FloorToInt(float f) => (int)Math.Floor(f);
    }

    public static class Time
    {
        public static float unscaledDeltaTime => 0f;
        public static float unscaledTime => 0f;
    }

    public static class Screen
    {
        public static int width => 0;
        public static int height => 0;
    }

    public static class Application
    {
        public static string persistentDataPath => "";
    }

    public static class Debug
    {
        public static void LogWarning(object message) { }
        public static void LogException(Exception exception) { }
    }

    public enum TextAnchor
    {
        UpperLeft = 0, UpperCenter = 1, UpperRight = 2, MiddleLeft = 3, MiddleCenter = 4,
        MiddleRight = 5, LowerLeft = 6, LowerCenter = 7, LowerRight = 8
    }

    public sealed class GUIStyleState
    {
        public Color textColor { get; set; }
    }

    public sealed class GUIStyle
    {
        public GUIStyle(GUIStyle other) { }
        public GUIStyleState normal { get; set; }
        public TextAnchor alignment { get; set; }
        public bool wordWrap { get; set; }
        public bool richText { get; set; }
        public int fontSize { get; set; }
    }

    public sealed class GUISkin : Object
    {
        public GUIStyle label { get; set; }
    }

    public class GUI
    {
        public static Color color { get; set; }
        public static int depth { get; set; }
        public static GUISkin skin { get; set; }
        public static void DrawTexture(Rect position, Texture image) { }
        public static void Label(Rect position, string text, GUIStyle style) { }
    }

    public enum EventType
    {
        MouseDown = 0, MouseUp = 1, MouseMove = 2, MouseDrag = 3, KeyDown = 4, KeyUp = 5,
        ScrollWheel = 6, Repaint = 7, Layout = 8
    }

    public sealed class Event
    {
        public static Event current { get; set; }
        public EventType type { get; set; }
    }

    public enum KeyCode
    {
        None = 0, Backspace = 8, Tab = 9, Return = 13, Escape = 27, Space = 32,
        Minus = 45, Equals = 61, LeftBracket = 91, Backslash = 92, RightBracket = 93,
        A = 97, B = 98, C = 99, D = 100, E = 101, F = 102, G = 103, H = 104, I = 105, J = 106, K = 107,
        L = 108, M = 109, N = 110, O = 111, P = 112, Q = 113, R = 114, S = 115, T = 116, U = 117,
        V = 118, W = 119, X = 120, Y = 121, Z = 122,
        UpArrow = 273, DownArrow = 274, RightArrow = 275, LeftArrow = 276, PageUp = 280, PageDown = 281,
        F12 = 293, RightShift = 303, LeftShift = 304, RightControl = 305, LeftControl = 306
    }

    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DisallowMultipleComponent : Attribute { }

    public class PropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public class HeaderAttribute : PropertyAttribute
    {
        public HeaderAttribute(string header) { }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class TooltipAttribute : PropertyAttribute
    {
        public TooltipAttribute(string tooltip) { }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class RangeAttribute : PropertyAttribute
    {
        public RangeAttribute(float min, float max) { }
    }
}
