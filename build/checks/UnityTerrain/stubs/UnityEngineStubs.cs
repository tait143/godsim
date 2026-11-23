// Minimal UnityEngine API stubs for the UnityTerrain compile check. Only members used by
// Assets/GodSim/Unity/Runtime/Terrain are declared, with the same signatures as Unity 2022.3 / Unity 6.
// Bodies are placeholders (compile-only). NEVER copy into Assets/.
using System;
using System.Collections.Generic;
#pragma warning disable CS0169, CS0067

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16 = 0, UInt32 = 1 }
    public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 }
}

namespace UnityEngine
{
    using UnityEngine.Rendering;

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DisallowMultipleComponent : Attribute { }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public class HeaderAttribute : Attribute { public readonly string header; public HeaderAttribute(string header) { this.header = header; } }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public class TooltipAttribute : Attribute { public readonly string tooltip; public TooltipAttribute(string tooltip) { this.tooltip = tooltip; } }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class RangeAttribute : Attribute { public readonly float min; public readonly float max; public RangeAttribute(float min, float max) { this.min = min; this.max = max; } }

    public enum RuntimeInitializeLoadType { AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4 }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }

    public static class Debug
    {
        public static void LogWarning(object message) { }
    }

    public static class Time
    {
        public static float deltaTime => 0f;
        public static float unscaledDeltaTime => 0f;
    }

    public struct LayerMask
    {
        private int m_Mask;
        public int value { get { return m_Mask; } set { m_Mask = value; } }
        public static implicit operator int(LayerMask mask) => mask.m_Mask;
        public static implicit operator LayerMask(int intVal) { LayerMask m; m.m_Mask = intVal; return m; }
    }

    // ------------------------------------------------------------------ math

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static Vector3 Cross(Vector3 lhs, Vector3 rhs) =>
            new Vector3(lhs.y * rhs.z - lhs.z * rhs.y, lhs.z * rhs.x - lhs.x * rhs.z, lhs.x * rhs.y - lhs.y * rhs.x);
        public static float Dot(Vector3 lhs, Vector3 rhs) => lhs.x * rhs.x + lhs.y * rhs.y + lhs.z * rhs.z;
        public Vector3 normalized
        {
            get { float m = (float)Math.Sqrt(x * x + y * y + z * z); return m > 1E-05f ? this / m : new Vector3(0f, 0f, 0f); }
        }
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion Euler(float x, float y, float z) => default(Quaternion);
    }

    public struct Matrix4x4
    {
        public float m00, m10, m20, m30, m01, m11, m21, m31, m02, m12, m22, m32, m03, m13, m23, m33;
        public static Matrix4x4 TRS(Vector3 pos, Quaternion q, Vector3 s) => default(Matrix4x4);
    }

    public struct Bounds
    {
        private Vector3 m_Center, m_Extents;
        public Bounds(Vector3 center, Vector3 size) { m_Center = center; m_Extents = size * 0.5f; }
        public Vector3 center { get { return m_Center; } set { m_Center = value; } }
        public Vector3 size { get { return m_Extents * 2f; } set { m_Extents = value * 0.5f; } }
        public Vector3 min => m_Center - m_Extents;
        public Vector3 max => m_Center + m_Extents;
        public void SetMinMax(Vector3 min, Vector3 max) { m_Extents = (max - min) * 0.5f; m_Center = min + m_Extents; }
        public void Encapsulate(Bounds bounds) { }
        public float SqrDistance(Vector3 point) => 0f;
    }

    public struct Plane
    {
        private Vector3 m_Normal;
        private float m_Distance;
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public static Color operator *(Color a, float b) => new Color(a.r * b, a.g * b, a.b * b, a.a * b);
        public static Color Lerp(Color a, Color b, float t)
        {
            t = t < 0f ? 0f : (t > 1f ? 1f : t);
            return new Color(a.r + (b.r - a.r) * t, a.g + (b.g - a.g) * t, a.b + (b.b - a.b) * t, a.a + (b.a - a.a) * t);
        }
        public static implicit operator Color32(Color c) => new Color32(B(c.r), B(c.g), B(c.b), B(c.a));
        private static byte B(float v) => (byte)Math.Round((v < 0f ? 0f : (v > 1f ? 1f : v)) * 255f);
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
    }

    // ------------------------------------------------------------------ objects

    public class Object
    {
        public string name { get; set; }
        public static void Destroy(Object obj) { }
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

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled => true;
    }

    public class MonoBehaviour : Behaviour { }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform => null;
        public int layer { get; set; }
        public bool activeSelf => true;
        public void SetActive(bool value) { }
        public T AddComponent<T>() where T : Component => null;
    }

    public sealed class Camera : Behaviour
    {
        public static Camera main => null;
    }

    public class Material : Object
    {
        public Material(Material source) { }
        public bool enableInstancing { get; set; }
    }

    public sealed class Mesh : Object
    {
        public Mesh() { }
        public IndexFormat indexFormat { get; set; }
        public Bounds bounds { get; set; }
        public void Clear() { }
        public void Clear(bool keepVertexLayout) { }
        public void MarkDynamic() { }
        public void SetVertices(List<Vector3> inVertices) { }
        public void SetNormals(List<Vector3> inNormals) { }
        public void SetColors(List<Color32> inColors) { }
        public void SetTriangles(List<int> triangles, int submesh, bool calculateBounds = true, int baseVertex = 0) { }
    }

    public class Renderer : Component
    {
        public Material sharedMaterial { get; set; }
        public ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
    }

    public class MeshRenderer : Renderer { }

    public sealed class MeshFilter : Component
    {
        public Mesh sharedMesh { get; set; }
    }

    public class Collider : Component { }

    public sealed class MeshCollider : Collider
    {
        public Mesh sharedMesh { get; set; }
    }

    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }
    public enum TextureFormat { RGBA32 = 4 }

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
        public bool Reinitialize(int width, int height) => true;
        public void SetPixels32(Color32[] colors) { }
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { }
    }

    public static class GeometryUtility
    {
        public static void CalculateFrustumPlanes(Camera camera, Plane[] planes) { }
        public static bool TestPlanesAABB(Plane[] planes, Bounds bounds) => true;
    }

    public struct RenderParams
    {
        public RenderParams(Material mat) { layer = 0; worldBounds = default(Bounds); shadowCastingMode = ShadowCastingMode.On; receiveShadows = true; material = mat; }
        public Material material { get; set; }
        public int layer { get; set; }
        public Bounds worldBounds { get; set; }
        public ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
    }

    public sealed class Graphics
    {
        public static void RenderMeshInstanced<T>(in RenderParams rparams, Mesh mesh, int submeshIndex, T[] instanceData, int instanceCount = -1, int startInstance = 0) where T : unmanaged { }
    }
}
