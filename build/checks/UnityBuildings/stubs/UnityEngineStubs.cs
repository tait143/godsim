// Compile-check stubs: ONLY the UnityEngine members used by Assets/GodSim/Unity/Runtime/Buildings.
// Signatures mirror Unity 2022.3 / Unity 6. Bodies are placeholders; never ship these.
using System;
using System.Collections.Generic;

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16 = 0, UInt32 = 1 }
    public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 }
}

namespace UnityEngine
{
    using UnityEngine.Rendering;

    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object obj) { }
        public static void DestroyImmediate(Object obj) { }
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
        public static bool operator ==(Object x, Object y) => ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !ReferenceEquals(x, y);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
    }

    public class Behaviour : Component { public bool enabled { get; set; } }
    public class MonoBehaviour : Behaviour { }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform => null;
        public int layer { get; set; }
        public bool activeSelf => false;
        public void SetActive(bool value) { }
        public T AddComponent<T>() where T : Component => null;
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Vector3 localScale { get; set; }
        public void SetParent(Transform parent, bool worldPositionStays) { }
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation) { }
    }

    public sealed class Mesh : Object
    {
        public Mesh() { }
        public IndexFormat indexFormat { get; set; }
        public void SetVertices(List<Vector3> inVertices) { }
        public void SetNormals(List<Vector3> inNormals) { }
        public void SetColors(List<Color32> inColors) { }
        public void SetTriangles(List<int> triangles, int submesh, bool calculateBounds = true, int baseVertex = 0) { }
        public void RecalculateBounds() { }
        public void UploadMeshData(bool markNoLongerReadable) { }
    }

    public class Material : Object { }
    public class Shader : Object { public static int PropertyToID(string name) => 0; }

    public sealed class MaterialPropertyBlock
    {
        public void Clear() { }
        public void SetColor(int nameID, Color value) { }
    }

    public sealed class MeshFilter : Component { public Mesh sharedMesh { get; set; } }

    public class Renderer : Component
    {
        public bool enabled { get; set; }
        public Material sharedMaterial { get; set; }
        public ShadowCastingMode shadowCastingMode { get; set; }
        public void SetPropertyBlock(MaterialPropertyBlock properties) { }
    }

    public class MeshRenderer : Renderer { }

    public class Collider : Component { public bool enabled { get; set; } }

    public class BoxCollider : Collider
    {
        public Vector3 center { get; set; }
        public Vector3 size { get; set; }
    }

    public sealed class Camera : Behaviour { public static Camera main => null; }

    public struct LayerMask { public static int NameToLayer(string layerName) => -1; }

    public sealed class Time { public static float unscaledTime => 0f; }
    public sealed class Application { public static bool isPlaying => true; }
    public sealed class Debug { public static void LogWarning(object message) { } }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class PropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class TooltipAttribute : PropertyAttribute { public TooltipAttribute(string tooltip) { } }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed class DisallowMultipleComponent : Attribute { }

    public enum RuntimeInitializeLoadType
    {
        AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    public struct Mathf
    {
        public const float PI = 3.14159274f;
        public const float Deg2Rad = PI * 2f / 360f;
        public static float Sin(float f) => (float)Math.Sin(f);
        public static float Cos(float f) => (float)Math.Cos(f);
        public static float Abs(float f) => Math.Abs(f);
        public static float Min(float a, float b) => a < b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
        public static float Clamp(float value, float min, float max) => value < min ? min : value > max ? max : value;
        public static float Repeat(float t, float length) => t - (float)Math.Floor(t / length) * length;
        public static float DeltaAngle(float current, float target)
        {
            float d = Repeat(target - current, 360f);
            if (d > 180f) d -= 360f;
            return d;
        }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 down => new Vector3(0f, -1f, 0f);
        public static Vector3 left => new Vector3(-1f, 0f, 0f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);
        public static Vector3 forward => new Vector3(0f, 0f, 1f);
        public static Vector3 back => new Vector3(0f, 0f, -1f);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { float m = (float)Math.Sqrt(sqrMagnitude); return m > 1e-5f ? new Vector3(x / m, y / m, z / m) : zero; } }
        public static Vector3 Cross(Vector3 lhs, Vector3 rhs) =>
            new Vector3(lhs.y * rhs.z - lhs.z * rhs.y, lhs.z * rhs.x - lhs.x * rhs.z, lhs.x * rhs.y - lhs.y * rhs.x);
        public static float Dot(Vector3 lhs, Vector3 rhs) => lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion Euler(float x, float y, float z) => default;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color Lerp(Color a, Color b, float t)
        {
            t = t < 0f ? 0f : t > 1f ? 1f : t;
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }
        public static Color operator *(Color a, float b) => new Color(a.r * b, a.g * b, a.b * b, a.a * b);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }
}
