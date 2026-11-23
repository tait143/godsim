// Minimal UnityEngine API stubs for compile checks ONLY (never copy into Assets/).
// Every member mirrors the real Unity 6 / 2022.3 signature; bodies are placeholders.
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
        public string name { get; set; }
        public HideFlags hideFlags { get; set; }
        public static void Destroy(Object obj) { }
        public static void DontDestroyOnLoad(Object target) { }
        public static bool operator ==(Object x, Object y) => ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !ReferenceEquals(x, y);
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    public sealed class GameObject : Object { }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
    }

    public class Transform : Component
    {
        public Transform parent { get; set; }
        public Transform root => null;
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
    }

    public class MonoBehaviour : Behaviour { }

    public class ScriptableObject : Object { }

    public sealed class Shader : Object
    {
        public static Shader Find(string name) => null;
        public bool isSupported => true;
    }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public Color color { get; set; }
        public bool enableInstancing { get; set; }
        public int renderQueue { get; set; }
        public bool HasProperty(string name) => false;
        public void SetFloat(string name, float value) { }
        public void SetColor(string name, Color value) { }
        public void EnableKeyword(string keyword) { }
        public void DisableKeyword(string keyword) { }
        public void SetOverrideTag(string tag, string val) { }
    }

    public struct Color : IEquatable<Color>
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public bool Equals(Color other) => r == other.r && g == other.g && b == other.b && a == other.a;
        public override bool Equals(object other) => other is Color c && Equals(c);
        public override int GetHashCode() => 0;
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color32(Color c) => new Color32();
        public static implicit operator Color(Color32 c) => new Color();
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
    }

    public struct Mathf
    {
        public static float Clamp(float value, float min, float max) => value;
        public static int Clamp(int value, int min, int max) => value;
        public static int FloorToInt(float f) => (int)Math.Floor(f);
    }

    public class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
        public static void LogError(object message) { }
    }

    public class Application
    {
        public static string persistentDataPath => "";
    }

    public class Time
    {
        public static float deltaTime => 0f;
    }

    public enum ShadowQuality { Disable = 0, HardOnly = 1, All = 2 }

    public sealed class QualitySettings : Object
    {
        public static ShadowQuality shadows { get; set; }
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
    public sealed class CreateAssetMenuAttribute : Attribute
    {
        public string menuName { get; set; }
        public string fileName { get; set; }
        public int order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DisallowMultipleComponent : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class DefaultExecutionOrder : Attribute
    {
        public DefaultExecutionOrder(int order) { }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public abstract class PropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed class SerializeField : Attribute { }

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

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class MinAttribute : PropertyAttribute
    {
        public MinAttribute(float min) { }
    }
}

namespace UnityEngine.Rendering
{
    public abstract class RenderPipelineAsset : ScriptableObject { }

    public sealed class GraphicsSettings : Object
    {
        public static RenderPipelineAsset currentRenderPipeline => null;
    }

    public enum BlendMode
    {
        Zero = 0, One = 1, DstColor = 2, SrcColor = 3, OneMinusDstColor = 4, SrcAlpha = 5,
        OneMinusSrcColor = 6, DstAlpha = 7, OneMinusDstAlpha = 8, SrcAlphaSaturate = 9, OneMinusSrcAlpha = 10
    }

    public enum RenderQueue
    {
        Background = 1000, Geometry = 2000, AlphaTest = 2450, GeometryLast = 2500, Transparent = 3000, Overlay = 4000
    }
}
