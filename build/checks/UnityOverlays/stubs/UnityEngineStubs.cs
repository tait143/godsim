// Compile-only stand-ins for the UnityEngine members used by Assets/GodSim/Unity/Runtime/Overlays.
// Signatures mirror Unity 6 / 2022.3 exactly; bodies are placeholders. NEVER copy into Assets/.
#pragma warning disable CS0067, CS0660, CS0661, CS1591
using System;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object obj) { }
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
        public static bool operator ==(Object x, Object y) => ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !ReferenceEquals(x, y);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    public class Component : Object
    {
        public GameObject gameObject => null;
        public Transform transform => null;
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled => enabled;
    }

    public class MonoBehaviour : Behaviour { }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { this.name = name; }
        public Transform transform => null;
        public int layer { get; set; }
        public T AddComponent<T>() where T : Component => null;
    }

    public class Transform : Component
    {
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public sealed class Mesh : Object
    {
        public Mesh() { }
        public void Clear() { }
        public void SetVertices(List<Vector3> inVertices) { }
        public void SetUVs(int channel, List<Vector2> uvs) { }
        public void SetColors(List<Color32> inColors) { }
        public void SetTriangles(List<int> triangles, int submesh, bool calculateBounds = true, int baseVertex = 0) { }
        public void RecalculateBounds() { }
    }

    public sealed class MeshFilter : Component
    {
        public Mesh sharedMesh { get; set; }
    }

    public class Renderer : Component
    {
        public bool enabled { get; set; }
        public Material sharedMaterial { get; set; }
        public Rendering.ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
    }

    public class MeshRenderer : Renderer { }

    public class Shader : Object { }

    public class Material : Object
    {
        public Material(Material source) { }
        public Texture mainTexture { get; set; }
        public Color color { get; set; }
        public int renderQueue { get; set; }
    }

    public class Texture : Object
    {
        public virtual int width { get; set; }
        public virtual int height { get; set; }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public int anisoLevel { get; set; }
    }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain, bool linear) { }
        public static Texture2D whiteTexture => null;
        public void SetPixels32(Color32[] colors) { }
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
    }

    public enum TextureFormat { Alpha8 = 1, ARGB4444 = 2, RGB24 = 3, RGBA32 = 4, ARGB32 = 5 }
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }

    [Flags]
    public enum HideFlags
    {
        None = 0, HideInHierarchy = 1, HideInInspector = 2, DontSaveInEditor = 4, NotEditable = 8,
        DontSaveInBuild = 16, DontUnloadUnusedAsset = 32, DontSave = 52, HideAndDontSave = 61,
    }

    public enum KeyCode
    {
        None = 0,
        F1 = 282, F2 = 283, F3 = 284, F4 = 285, F5 = 286, F6 = 287, F7 = 288, F8 = 289,
        F9 = 290, F10 = 291, F11 = 292, F12 = 293,
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static implicit operator Color32(Color c) => new Color32((byte)(c.r * 255f), (byte)(c.g * 255f), (byte)(c.b * 255f), (byte)(c.a * 255f));
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public enum GradientMode { Blend = 0, Fixed = 1, PerceptualBlend = 2 }

    public struct GradientColorKey
    {
        public Color color;
        public float time;
        public GradientColorKey(Color col, float time) { color = col; this.time = time; }
    }

    public struct GradientAlphaKey
    {
        public float alpha;
        public float time;
        public GradientAlphaKey(float alpha, float time) { this.alpha = alpha; this.time = time; }
    }

    public class Gradient
    {
        public Gradient() { }
        public GradientMode mode { get; set; }
        public void SetKeys(GradientColorKey[] colorKeys, GradientAlphaKey[] alphaKeys) { }
        public Color Evaluate(float time) => default(Color);
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public struct Matrix4x4
    {
        public static Matrix4x4 Scale(Vector3 vector) => default(Matrix4x4);
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
        public static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
        public static int Clamp(int value, int min, int max) => value < min ? min : (value > max ? max : value);
        public static float Max(float a, float b) => a > b ? a : b;
    }

    public class Time
    {
        public static float unscaledTime => 0f;
    }

    public sealed class Screen
    {
        public static int height => 1080;
    }

    public class Debug
    {
        public static void LogWarning(object message) { }
        public static void LogException(Exception exception) { }
    }

    public enum FontStyle { Normal = 0, Bold = 1, Italic = 2, BoldAndItalic = 3 }

    public enum TextAnchor
    {
        UpperLeft = 0, UpperCenter = 1, UpperRight = 2, MiddleLeft = 3, MiddleCenter = 4, MiddleRight = 5,
        LowerLeft = 6, LowerCenter = 7, LowerRight = 8,
    }

    public class GUIStyleState
    {
        public Color textColor { get; set; }
    }

    public sealed class GUIStyle
    {
        public GUIStyle() { }
        public GUIStyle(GUIStyle other) { }
        public GUIStyleState normal { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public TextAnchor alignment { get; set; }
        public bool wordWrap { get; set; }
    }

    public sealed class GUISkin : Object
    {
        public GUIStyle label { get; set; }
    }

    public class GUI
    {
        public static Color color { get; set; }
        public static int depth { get; set; }
        public static Matrix4x4 matrix { get; set; }
        public static GUISkin skin { get; set; }
        public static void Label(Rect position, string text, GUIStyle style) { }
        public static void DrawTexture(Rect position, Texture image) { }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public abstract class PropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, Inherited = true, AllowMultiple = false)]
    public class TooltipAttribute : PropertyAttribute
    {
        public readonly string tooltip;
        public TooltipAttribute(string tooltip) { this.tooltip = tooltip; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class DisallowMultipleComponent : Attribute { }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 }
}
