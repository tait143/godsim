// Compile-check stubs: ONLY the UnityEngine members used by Assets/GodSim/Unity/Runtime/Buildings.
// Signatures mirror Unity 2022.3 / Unity 6. Minimal in-memory geometry supports the headless batching check.
using System;
using System.Collections.Generic;
using Numerics = System.Numerics;

namespace UnityEngine.Rendering
{
    public enum IndexFormat { UInt16 = 0, UInt32 = 1 }
    public enum VertexAttribute { Color }
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
        public Transform transform => gameObject?.transform;
        public GameObject gameObject { get; internal set; }
    }

    public class ScriptableObject : Object { }
    public class TextAsset : Object { public string text => null; }
    public static class JsonUtility { public static T FromJson<T>(string json) => default; }
    public static class Resources { public static T Load<T>(string path) where T : Object => null; }

    public class Behaviour : Component { public bool enabled { get; set; } }
    public class MonoBehaviour : Behaviour { }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { this.name = name; transform = new Transform { gameObject = this }; }
        public Transform transform { get; }
        public int layer { get; set; }
        public bool activeSelf { get; private set; } = true;
        public void SetActive(bool value) { activeSelf = value; }
        public T AddComponent<T>() where T : Component
        {
            var component = (T)Activator.CreateInstance(typeof(T));
            component.gameObject = this;
            return component;
        }
    }

    public class Transform : Component
    {
        private Transform _parent;
        public Vector3 position
        {
            get => localToWorldMatrix.MultiplyPoint3x4(Vector3.zero);
            set => localPosition = _parent == null ? value : _parent.worldToLocalMatrix.MultiplyPoint3x4(value);
        }
        public Vector3 localPosition { get; set; }
        public Quaternion localRotation { get; set; } = Quaternion.identity;
        public Vector3 localScale { get; set; } = Vector3.one;
        private Numerics.Quaternion WorldRotation => _parent == null ? localRotation.Value : _parent.WorldRotation * localRotation.Value;
        public Matrix4x4 localToWorldMatrix
        {
            get
            {
                var local = Matrix4x4.TRS(localPosition, localRotation, localScale);
                return _parent == null ? local : _parent.localToWorldMatrix * local;
            }
        }
        public Matrix4x4 worldToLocalMatrix => localToWorldMatrix.inverse;
        public void SetParent(Transform parent, bool worldPositionStays)
        {
            if (worldPositionStays)
            {
                var local = parent == null ? localToWorldMatrix : parent.worldToLocalMatrix * localToWorldMatrix;
                if (!Numerics.Matrix4x4.Decompose(local.Value, out var scale, out var rotation, out var translation))
                    throw new InvalidOperationException("The new local transform cannot be represented by translation, rotation and scale.");
                localPosition = new Vector3(translation);
                localRotation = new Quaternion(rotation);
                localScale = new Vector3(scale);
            }
            _parent = parent;
        }
        public void SetPositionAndRotation(Vector3 position, Quaternion rotation)
        {
            this.position = position;
            localRotation = _parent == null ? rotation : new Quaternion(Numerics.Quaternion.Inverse(_parent.WorldRotation) * rotation.Value);
        }
    }

    public sealed class Mesh : Object
    {
        public Mesh() { }
        private readonly List<Vector3> _vertices = new List<Vector3>(), _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<Color32> _colors = new List<Color32>();
        private readonly List<int> _indices = new List<int>();
        public int vertexCount => _vertices.Count;
        public IndexFormat indexFormat { get; set; }
        public Bounds bounds { get; set; }
        public bool HasVertexAttribute(VertexAttribute attribute) => attribute == VertexAttribute.Color && _colors.Count != 0;
        public void Clear() { _vertices.Clear(); _normals.Clear(); _uvs.Clear(); _colors.Clear(); _indices.Clear(); bounds = default; }
        private static void Copy<T>(List<T> source, List<T> target) { target.Clear(); target.AddRange(source); }
        public void GetVertices(List<Vector3> positions) => Copy(_vertices, positions);
        public void GetNormals(List<Vector3> normals) => Copy(_normals, normals);
        public void GetUVs(int channel, List<Vector2> uvs) => Copy(_uvs, uvs);
        public void SetUVs(int channel, List<Vector2> uvs) => Copy(uvs, _uvs);
        public void GetColors(List<Color> colors)
        {
            colors.Clear();
            foreach (var c in _colors) colors.Add(new Color(c.r/255f, c.g/255f, c.b/255f, c.a/255f));
        }
        public void GetTriangles(List<int> indices, int submesh) => Copy(_indices, indices);
        public void SetVertices(List<Vector3> inVertices) => Copy(inVertices, _vertices);
        public void SetNormals(List<Vector3> inNormals) => Copy(inNormals, _normals);
        public void SetColors(List<Color32> inColors) => Copy(inColors, _colors);
        public void SetTriangles(List<int> triangles, int submesh, bool calculateBounds = true, int baseVertex = 0)
        {
            Copy(triangles, _indices);
            if (calculateBounds) RecalculateBounds();
        }
        public void RecalculateBounds()
        {
            if (_vertices.Count == 0) { bounds = default; return; }
            var min = _vertices[0].Value;
            var max = min;
            foreach (var vertex in _vertices)
            {
                min = Numerics.Vector3.Min(min, vertex.Value);
                max = Numerics.Vector3.Max(max, vertex.Value);
            }
            bounds = new Bounds { min = new Vector3(min), max = new Vector3(max) };
        }
        public void UploadMeshData(bool markNoLongerReadable) { }
    }

    public class Material : Object { public bool HasProperty(int nameID) => false; }
    public class Shader : Object { public static int PropertyToID(string name) => 0; }

    public sealed class MaterialPropertyBlock
    {
        public void Clear() { }
        public void SetColor(int nameID, Color value) { }
        public void SetFloat(int nameID, float value) { }
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
        internal Vector3(Numerics.Vector3 value) : this(value.X, value.Y, value.Z) { }
        internal Numerics.Vector3 Value => new Numerics.Vector3(x, y, z);
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
        internal Quaternion(Numerics.Quaternion value) { x = value.X; y = value.Y; z = value.Z; w = value.W; }
        internal Numerics.Quaternion Value => new Numerics.Quaternion(x, y, z, w);
        public static Quaternion identity => new Quaternion(Numerics.Quaternion.Identity);
        public static Quaternion Euler(float x, float y, float z) => new Quaternion(
            Numerics.Quaternion.CreateFromYawPitchRoll(y * Mathf.Deg2Rad, x * Mathf.Deg2Rad, z * Mathf.Deg2Rad));
    }

    public struct Vector2 { public float x, y; }
    public struct Bounds { public Vector3 min { get; set; } public Vector3 max { get; set; } }
    public struct Matrix4x4
    {
        internal readonly Numerics.Matrix4x4 Value;
        private Matrix4x4(Numerics.Matrix4x4 value) { Value = value; }
        // Numerics transforms row vectors, so Unity's A * B is stored as B * A.
        public static Matrix4x4 TRS(Vector3 position, Quaternion rotation, Vector3 scale) => new Matrix4x4(
            Numerics.Matrix4x4.CreateScale(scale.Value) * Numerics.Matrix4x4.CreateFromQuaternion(rotation.Value)
                * Numerics.Matrix4x4.CreateTranslation(position.Value));
        public Matrix4x4 inverse => Numerics.Matrix4x4.Invert(Value, out var result) ? new Matrix4x4(result) : default;
        public Matrix4x4 transpose => new Matrix4x4(Numerics.Matrix4x4.Transpose(Value));
        public Vector3 MultiplyPoint3x4(Vector3 point) => new Vector3(Numerics.Vector3.Transform(point.Value, Value));
        public Vector3 MultiplyVector(Vector3 vector) => new Vector3(Numerics.Vector3.TransformNormal(vector.Value, Value));
        public static Matrix4x4 operator *(Matrix4x4 lhs, Matrix4x4 rhs) => new Matrix4x4(rhs.Value * lhs.Value);
    }
    public enum ColorSpace { Gamma, Linear }
    public static class QualitySettings { public static ColorSpace activeColorSpace => ColorSpace.Gamma; }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; a = 1f; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public Color linear => this;
        public static Color operator *(Color a, Color b) => new Color(a.r*b.r, a.g*b.g, a.b*b.b, a.a*b.a);
        public static implicit operator Color32(Color color) => new Color32((byte)(Mathf.Clamp(color.r,0,1)*255f),
            (byte)(Mathf.Clamp(color.g,0,1)*255f), (byte)(Mathf.Clamp(color.b,0,1)*255f), (byte)(Mathf.Clamp(color.a,0,1)*255f));
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
