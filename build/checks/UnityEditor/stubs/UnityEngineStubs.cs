// Compile-only stubs of the UnityEngine members used by Assets/GodSim/Editor.
// Signatures mirror Unity 6 / 2022.3. Bodies are placeholders and never execute. Never copy into Assets/.
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
        public static void DestroyImmediate(Object obj) { }
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
        public static bool operator ==(Object x, Object y) => ReferenceEquals(x, y);
        public static bool operator !=(Object x, Object y) => !ReferenceEquals(x, y);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed class SerializeField : Attribute { }

    public class Component : Object
    {
        public Transform transform => null;
        public GameObject gameObject => null;
    }

    public class Behaviour : Component { }
    public class MonoBehaviour : Behaviour { }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => null;
    }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform => null;
        public string tag { get; set; }
        public Component AddComponent(Type componentType) => null;
        public T AddComponent<T>() where T : Component => null;
        public Component GetComponent(Type type) => null;
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public void LookAt(Vector3 worldPosition) { }
    }

    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }

    public sealed class Camera : Behaviour
    {
        public float fieldOfView { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public CameraClearFlags clearFlags { get; set; }
    }

    public enum LightType { Spot = 0, Directional = 1, Point = 2 }
    public enum LightShadows { None = 0, Hard = 1, Soft = 2 }

    public sealed class Light : Behaviour
    {
        public LightType type { get; set; }
        public LightShadows shadows { get; set; }
        public float shadowStrength { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
    }

    public class AudioBehaviour : Behaviour { }
    public sealed class AudioListener : AudioBehaviour { }

    public sealed class RenderSettings : Object { public static Light sun { get; set; } }

    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
    }

    public struct Quaternion
    {
        public float x, y, z, w;
        public static Quaternion Euler(float x, float y, float z) => default(Quaternion);
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
    }

    public struct Rect
    {
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public float x { get; set; }
        public float y { get; set; }
        public float width { get; set; }
        public float height { get; set; }
        public bool Contains(Vector2 point) => false;
    }

    public struct Mathf
    {
        public static float Max(float a, float b) => a > b ? a : b;
        public static float Clamp(float value, float min, float max) => value;
        public static int Clamp(int value, int min, int max) => value;
        public static float Clamp01(float value) => value;
        public static int RoundToInt(float f) => (int)f;
    }

    public enum TextureFormat { RGBA32 = 4 }
    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }

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
        public void SetPixels32(Color32[] colors) { }
        public void Apply(bool updateMipmaps) { }
    }

    public class Debug
    {
        public static void Log(object message) { }
        public static void Log(object message, Object context) { }
        public static void LogWarning(object message) { }
        public static void LogWarning(object message, Object context) { }
        public static void LogError(object message) { }
        public static void LogError(object message, Object context) { }
    }

    public class Application { public static bool isPlaying => false; }

    public static class JsonUtility { public static T FromJson<T>(string json) => default(T); }

    // ------------------------------------------------------------------ IMGUI
    public enum ScaleMode { StretchToFill = 0, ScaleAndCrop = 1, ScaleToFit = 2 }

    public enum EventType { MouseDown = 0, MouseUp = 1, MouseMove = 2, MouseDrag = 3, KeyDown = 4, KeyUp = 5, ScrollWheel = 6, Repaint = 7, Layout = 8 }

    public sealed class Event
    {
        public static Event current { get; set; }
        public EventType type { get; set; }
        public Vector2 mousePosition { get; set; }
    }

    public class GUIContent
    {
        public GUIContent(string text) { }
    }

    public sealed class GUIStyle { }

    public sealed class GUILayoutOption { }

    public class GUI
    {
        public static void Label(Rect position, string text) { }
        public static void DrawTexture(Rect position, Texture image, ScaleMode scaleMode) { }
        public static void FocusControl(string name) { }
    }

    public class GUILayout
    {
        public static bool Button(string text, params GUILayoutOption[] options) => false;
        public static void Label(string text, GUIStyle style, params GUILayoutOption[] options) { }
        public static GUILayoutOption Width(float width) => null;
        public static GUILayoutOption MinWidth(float minWidth) => null;
        public static GUILayoutOption Height(float height) => null;
        public static GUILayoutOption ExpandWidth(bool expand) => null;
    }

    public class GUILayoutUtility
    {
        public static Rect GetRect(float width, float height, params GUILayoutOption[] options) => default(Rect);
    }

    public class GUIUtility
    {
        public static void ExitGUI() { }
    }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
    }

    public class SceneManager { }
}
