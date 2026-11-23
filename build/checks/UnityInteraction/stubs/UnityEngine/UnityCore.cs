// Compile-only stubs of the UnityEngine members used by Assets/GodSim/Unity/Runtime/Interaction.
// Signatures mirror Unity 6000.0 / 2022.3. Bodies are placeholders (never executed).
using System;

namespace UnityEngine
{
    public class Object
    {
        public string name { get => throw null; set => throw null; }
        public static void Destroy(Object obj) => throw null;
        public static bool operator ==(Object x, Object y) => throw null;
        public static bool operator !=(Object x, Object y) => throw null;
        public static implicit operator bool(Object exists) => throw null;
        public override bool Equals(object other) => throw null;
        public override int GetHashCode() => throw null;
    }

    public class Component : Object
    {
        public Transform transform => throw null;
        public GameObject gameObject => throw null;
        public T GetComponent<T>() => throw null;
        public T GetComponentInChildren<T>() => throw null;
    }

    public class Behaviour : Component
    {
        public bool enabled { get => throw null; set => throw null; }
        public bool isActiveAndEnabled => throw null;
    }

    public class MonoBehaviour : Behaviour { }

    public sealed class GameObject : Object
    {
        public GameObject(string name) => throw null;
        public Transform transform => throw null;
        public string tag { get => throw null; set => throw null; }
        public T AddComponent<T>() where T : Component => throw null;
    }

    public class Transform : Component
    {
        public Vector3 position { get => throw null; set => throw null; }
        public Quaternion rotation { get => throw null; set => throw null; }
        public Vector3 forward { get => throw null; set => throw null; }
        public void SetParent(Transform parent, bool worldPositionStays) => throw null;
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation) => throw null;
        public bool IsChildOf(Transform parent) => throw null;
    }

    public sealed class Camera : Behaviour
    {
        public static Camera main => throw null;
        public float nearClipPlane { get => throw null; set => throw null; }
        public float farClipPlane { get => throw null; set => throw null; }
        public Ray ScreenPointToRay(Vector3 pos) => throw null;
    }

    public class Material : Object { }

    public class Renderer : Component
    {
        public bool enabled { get => throw null; set => throw null; }
        public Material sharedMaterial { get => throw null; set => throw null; }
        public Rendering.ShadowCastingMode shadowCastingMode { get => throw null; set => throw null; }
        public bool receiveShadows { get => throw null; set => throw null; }
    }

    public sealed class LineRenderer : Renderer
    {
        public int positionCount { get => throw null; set => throw null; }
        public bool loop { get => throw null; set => throw null; }
        public bool useWorldSpace { get => throw null; set => throw null; }
        public float widthMultiplier { get => throw null; set => throw null; }
        public Color startColor { get => throw null; set => throw null; }
        public Color endColor { get => throw null; set => throw null; }
        public int numCornerVertices { get => throw null; set => throw null; }
        public int numCapVertices { get => throw null; set => throw null; }
        public void SetPosition(int index, Vector3 position) => throw null;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
    }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public static Vector2 zero => default;
        public float sqrMagnitude => x * x + y * y;
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => default;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion Euler(float x, float y, float z) => throw null;
        public static Quaternion operator *(Quaternion lhs, Quaternion rhs) => throw null;
        public static Vector3 operator *(Quaternion rotation, Vector3 point) => throw null;
    }

    public struct Ray
    {
        public Ray(Vector3 origin, Vector3 direction) => throw null;
        public Vector3 origin { get => throw null; set => throw null; }
        public Vector3 direction { get => throw null; set => throw null; }
    }

    public struct RaycastHit
    {
        public Vector3 point { get => throw null; set => throw null; }
    }

    public enum QueryTriggerInteraction { UseGlobal = 0, Ignore = 1, Collide = 2 }

    public struct LayerMask
    {
        public int value { get => throw null; set => throw null; }
        public static implicit operator int(LayerMask mask) => throw null;
        public static implicit operator LayerMask(int intVal) => throw null;
    }

    public class Physics
    {
        public const int DefaultRaycastLayers = -5;
        public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => throw null;
    }

    public struct Mathf
    {
        public const float PI = 3.14159274F;
        public const float Deg2Rad = PI * 2F / 360F;
        public static float Sin(float f) => throw null;
        public static float Cos(float f) => throw null;
        public static float Sqrt(float f) => throw null;
        public static float Abs(float f) => throw null;
        public static float Min(float a, float b) => throw null;
        public static float Max(float a, float b) => throw null;
        public static int Max(int a, int b) => throw null;
        public static float Pow(float f, float p) => throw null;
        public static float Exp(float power) => throw null;
        public static float Log(float f) => throw null;
        public static float Floor(float f) => throw null;
        public static int FloorToInt(float f) => throw null;
        public static float Clamp(float value, float min, float max) => throw null;
        public static int Clamp(int value, int min, int max) => throw null;
        public static float Clamp01(float value) => throw null;
        public static float Lerp(float a, float b, float t) => throw null;
        public static float LerpUnclamped(float a, float b, float t) => throw null;
        public static float InverseLerp(float a, float b, float value) => throw null;
        public static float SmoothStep(float from, float to, float t) => throw null;
        public static float PerlinNoise(float x, float y) => throw null;
    }

    public class Time
    {
        public static float unscaledDeltaTime => throw null;
        public static float unscaledTime => throw null;
        public static int frameCount => throw null;
    }

    public sealed class Screen
    {
        public static int width => throw null;
        public static int height => throw null;
    }

    public class Application
    {
        public static bool isFocused => throw null;
    }

    public class Debug
    {
        public static void LogWarning(object message) => throw null;
        public static void LogException(Exception exception) => throw null;
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

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public class HeaderAttribute : Attribute { public HeaderAttribute(string header) { } }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class TooltipAttribute : Attribute { public TooltipAttribute(string tooltip) { } }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DisallowMultipleComponent : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string menuName) { } }
}

namespace UnityEngine.Rendering
{
    public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 }
}
