// Minimal UnityEngine API surface used by Assets/GodSim/Unity/Runtime/Units + Effects.
// Compile-check only: member names/signatures match Unity 2022.3 / Unity 6; bodies are dummies.
// Never copy into Assets/.
using System;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public TooltipAttribute(string tooltip) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public sealed class HeaderAttribute : Attribute { public HeaderAttribute(string header) { } }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public RangeAttribute(float min, float max) { } }

    public enum FindObjectsSortMode { None = 0, InstanceID = 1 }

    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object obj) { }
        public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) where T : Object => new T[0];
        public static bool operator ==(Object x, Object y) => ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !ReferenceEquals(x, y);
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
        public T GetComponent<T>() => default;
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled => true;
    }

    public class MonoBehaviour : Behaviour { }

    public class ScriptableObject : Object { }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform => null;
        public int layer { get; set; }
        public T AddComponent<T>() where T : Component => null;
        public T GetComponent<T>() => default;
        public void SetActive(bool value) { }
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 forward { get; set; }
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public sealed class Camera : Behaviour
    {
        public static Camera main => null;
    }

    public enum LightType { Spot = 0, Directional = 1, Point = 2 }
    public enum LightShadows { None = 0, Hard = 1, Soft = 2 }

    public sealed class Light : Behaviour
    {
        public LightType type { get; set; }
        public Color color { get; set; }
        public float intensity { get; set; }
        public float range { get; set; }
        public LightShadows shadows { get; set; }
    }

    public sealed class LightProbeProxyVolume : Behaviour { }

    public sealed class RenderSettings : Object
    {
        public static Light sun { get; set; }
        public static Color ambientLight { get; set; }
        public static Color ambientEquatorColor { get; set; }
        public static Color ambientGroundColor { get; set; }
        public static Rendering.AmbientMode ambientMode { get; set; }
        public static Color fogColor { get; set; }
        public static float fogDensity { get; set; }
    }

    public sealed class Shader : Object
    {
        public static Shader Find(string name) => null;
        public static int PropertyToID(string name) => 0;
        public bool isSupported => true;
    }

    public class Texture : Object
    {
        public TextureWrapMode wrapMode { get; set; }
    }

    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }
    public enum TextureFormat { Alpha8 = 1, RGB24 = 3, RGBA32 = 4 }

    public sealed class Texture2D : Texture
    {
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public void SetPixels32(Color32[] colors) { }
        public void Apply(bool updateMipmaps = true, bool makeNoLongerReadable = false) { }
    }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public Material(Material source) { }
        public Texture mainTexture { get; set; }
        public bool enableInstancing { get; set; }
        public bool HasProperty(string name) => false;
        public void SetTexture(string name, Texture value) { }
    }

    public sealed class MaterialPropertyBlock
    {
        public void SetVectorArray(int nameID, Vector4[] values) { }
    }

    public sealed class Mesh : Object
    {
        public Vector3[] vertices { get; set; }
        public Color[] colors { get; set; }
        public int[] triangles { get; set; }
        public void RecalculateNormals() { }
        public void RecalculateBounds() { }
    }

    public sealed class Graphics
    {
        public static void DrawMeshInstanced(Mesh mesh, int submeshIndex, Material material, Matrix4x4[] matrices,
            int count = 1023, MaterialPropertyBlock properties = null,
            Rendering.ShadowCastingMode castShadows = Rendering.ShadowCastingMode.On, bool receiveShadows = true,
            int layer = 0, Camera camera = null,
            Rendering.LightProbeUsage lightProbeUsage = Rendering.LightProbeUsage.BlendProbes,
            LightProbeProxyVolume lightProbeProxyVolume = null) { }
    }

    public class Renderer : Component
    {
        public bool enabled { get; set; }
        public Material sharedMaterial { get; set; }
        public Rendering.ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
    }

    public sealed class MeshRenderer : Renderer { }

    public sealed class LineRenderer : Renderer
    {
        public int positionCount { get; set; }
        public float widthMultiplier { get; set; }
        public Color startColor { get; set; }
        public Color endColor { get; set; }
        public bool useWorldSpace { get; set; }
        public int numCapVertices { get; set; }
        public void SetPositions(Vector3[] positions) { }
    }

    public sealed class Font : Object
    {
        public Material material { get; set; }
    }

    public enum TextAnchor { UpperLeft = 0, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum TextAlignment { Left = 0, Center = 1, Right = 2 }

    public sealed class TextMesh : Component
    {
        public string text { get; set; }
        public Font font { get; set; }
        public int fontSize { get; set; }
        public float characterSize { get; set; }
        public TextAnchor anchor { get; set; }
        public TextAlignment alignment { get; set; }
        public Color color { get; set; }
        public bool richText { get; set; }
    }

    public sealed class Resources
    {
        public static T GetBuiltinResource<T>(string path) where T : Object => null;
    }

    public sealed class Debug
    {
        public static void LogWarning(object message, Object context) { }
        public static void LogException(Exception exception, Object context) { }
    }

    public sealed class Time
    {
        public static float time => 0f;
        public static float deltaTime => 0f;
        public static float unscaledTime => 0f;
        public static int frameCount => 0;
    }

    // ---------------------------------------------------------------- math

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized => this;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => a;
        public static Vector3 Cross(Vector3 lhs, Vector3 rhs) => lhs;
        public static float Dot(Vector3 lhs, Vector3 rhs) => 0f;
        public static Vector3 operator +(Vector3 a, Vector3 b) => a;
        public static Vector3 operator -(Vector3 a, Vector3 b) => a;
        public static Vector3 operator *(Vector3 a, float d) => a;
        public static Vector3 operator *(float d, Vector3 a) => a;
        public static Vector3 operator /(Vector3 a, float d) => a;
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion Euler(float x, float y, float z) => default;
    }

    public struct Matrix4x4
    {
        public float m00;
        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s) => default;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color Lerp(Color a, Color b, float t) => a;
        public static Color operator *(Color a, Color b) => a;
        public static Color operator *(Color a, float b) => a;
        public static Color operator +(Color a, Color b) => a;
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Mathf
    {
        public const float PI = 3.14159274f;
        public const float Deg2Rad = PI / 180f;
        public const float Rad2Deg = 57.29578f;
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Sqrt(float f) => (float)Math.Sqrt(f);
        public static float Abs(float f) => Math.Abs(f);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Floor(float f) => (float)Math.Floor(f);
        public static float Exp(float power) => (float)Math.Exp(power);
        public static float Log(float f) => (float)Math.Log(f);
        public static float Pow(float f, float p) => (float)Math.Pow(f, p);
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Clamp(float value, float min, float max) => value;
        public static int Clamp(int value, int min, int max) => value;
        public static float Clamp01(float value) => value;
        public static float Lerp(float a, float b, float t) => a;
        public static float LerpAngle(float a, float b, float t) => a;
        public static float SmoothStep(float from, float to, float t) => from;
        public static float MoveTowards(float current, float target, float maxDelta) => current;
        public static float Repeat(float t, float length) => t;
        public static int CeilToInt(float f) => (int)Math.Ceiling(f);
        public static int RoundToInt(float f) => (int)Math.Round(f);
    }

    // ---------------------------------------------------------------- particles

    public enum ParticleSystemStopBehavior { StopEmittingAndClear = 0, StopEmitting = 1 }
    public enum ParticleSystemSimulationSpace { Local = 0, World = 1, Custom = 2 }
    public enum ParticleSystemScalingMode { Hierarchy = 0, Local = 1, Shape = 2 }
    public enum ParticleSystemShapeType { Sphere = 0, Hemisphere = 2, Cone = 4, Box = 5, Circle = 10 }
    public enum ParticleSystemRenderMode { Billboard = 0, Stretch = 1, HorizontalBillboard = 2, VerticalBillboard = 3, Mesh = 4, None = 5 }

    public sealed class ParticleSystemRenderer : Renderer
    {
        public ParticleSystemRenderMode renderMode { get; set; }
        public float lengthScale { get; set; }
        public float velocityScale { get; set; }
        public float maxParticleSize { get; set; }
    }

    public struct GradientColorKey
    {
        public Color color; public float time;
        public GradientColorKey(Color col, float time) { color = col; this.time = time; }
    }

    public struct GradientAlphaKey
    {
        public float alpha; public float time;
        public GradientAlphaKey(float alpha, float time) { this.alpha = alpha; this.time = time; }
    }

    public sealed class Gradient
    {
        public void SetKeys(GradientColorKey[] colorKeys, GradientAlphaKey[] alphaKeys) { }
    }

    public struct Keyframe
    {
        public float time, value;
        public Keyframe(float time, float value) { this.time = time; this.value = value; }
    }

    public sealed class AnimationCurve
    {
        public AnimationCurve(params Keyframe[] keys) { }
    }

    public sealed class ParticleSystem : Component
    {
        public struct MinMaxCurve
        {
            public MinMaxCurve(float constant) { }
            public MinMaxCurve(float min, float max) { }
            public MinMaxCurve(float multiplier, AnimationCurve curve) { }
            public static implicit operator MinMaxCurve(float constant) => new MinMaxCurve(constant);
        }

        public struct MinMaxGradient
        {
            public MinMaxGradient(Color color) { }
            public MinMaxGradient(Color min, Color max) { }
            public MinMaxGradient(Gradient gradient) { }
            public static implicit operator MinMaxGradient(Color color) => new MinMaxGradient(color);
        }

        public struct MainModule
        {
            public float duration { get; set; }
            public bool loop { get; set; }
            public bool playOnAwake { get; set; }
            public ParticleSystemSimulationSpace simulationSpace { get; set; }
            public ParticleSystemScalingMode scalingMode { get; set; }
            public int maxParticles { get; set; }
            public MinMaxCurve startLifetime { get; set; }
            public MinMaxCurve startSpeed { get; set; }
            public MinMaxCurve startSize { get; set; }
            public float startSizeMultiplier { get; set; }
            public MinMaxGradient startColor { get; set; }
            public MinMaxCurve gravityModifier { get; set; }
        }

        public struct EmissionModule
        {
            public bool enabled { get; set; }
            public MinMaxCurve rateOverTime { get; set; }
            public float rateOverTimeMultiplier { get; set; }
        }

        public struct ShapeModule
        {
            public bool enabled { get; set; }
            public ParticleSystemShapeType shapeType { get; set; }
            public float radius { get; set; }
            public float angle { get; set; }
            public Vector3 scale { get; set; }
        }

        public struct ColorOverLifetimeModule
        {
            public bool enabled { get; set; }
            public MinMaxGradient color { get; set; }
        }

        public struct SizeOverLifetimeModule
        {
            public bool enabled { get; set; }
            public MinMaxCurve size { get; set; }
        }

        public MainModule main => default;
        public EmissionModule emission => default;
        public ShapeModule shape => default;
        public ColorOverLifetimeModule colorOverLifetime => default;
        public SizeOverLifetimeModule sizeOverLifetime => default;

        public void Play() { }
        public void Stop(bool withChildren, ParticleSystemStopBehavior stopBehavior) { }
        public void Clear(bool withChildren) { }
        public void Emit(int count) { }
        public bool IsAlive(bool withChildren) => false;
    }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 }
    public enum LightProbeUsage { Off = 0, BlendProbes = 1, UseProxyVolume = 2, CustomProvided = 4 }
    public enum AmbientMode { Skybox = 0, Trilight = 1, Flat = 3, Custom = 4 }
}

namespace UnityEngine
{
    public enum RuntimeInitializeLoadType { AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4 }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public sealed class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
}
