// Minimal UnityEngine API stubs for the UnityAudio compile check ONLY (never copied into Assets/).
// Each member mirrors the real Unity 2022.3 / Unity 6 signature; only members GodSimAudio uses are present.
using System;
using System.Collections;

namespace UnityEngine
{
    public class Object
    {
        public static void Destroy(Object obj) { }
        public static T FindFirstObjectByType<T>() where T : Object => null;
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

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) => null;
    }

    public sealed class Coroutine { }

    public sealed class GameObject : Object
    {
        public GameObject(string name) { }
        public Transform transform => null;
        public T AddComponent<T>() where T : Component => null;
    }

    public class Transform : Component
    {
        public Vector3 position { get; set; }
        public Vector3 forward { get; }
        public void SetParent(Transform parent, bool worldPositionStays) { }
    }

    public sealed class Camera : Behaviour { public static Camera main => null; }

    public class AudioBehaviour : Behaviour { }

    public sealed class AudioListener : AudioBehaviour { }

    public enum AudioRolloffMode { Logarithmic = 0, Linear = 1, Custom = 2 }

    public sealed class AudioSource : AudioBehaviour
    {
        public AudioClip clip { get; set; }
        public float volume { get; set; }
        public float pitch { get; set; }
        public bool loop { get; set; }
        public bool playOnAwake { get; set; }
        public float spatialBlend { get; set; }
        public AudioRolloffMode rolloffMode { get; set; }
        public float minDistance { get; set; }
        public float maxDistance { get; set; }
        public float dopplerLevel { get; set; }
        public float spread { get; set; }
        public int priority { get; set; }
        public bool ignoreListenerPause { get; set; }
        public bool isPlaying => false;
        public void Play() { }
        public void Stop() { }
    }

    public sealed class AudioClip : Object
    {
        public delegate void PCMReaderCallback(float[] data);
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream) => null;
        public static AudioClip Create(string name, int lengthSamples, int channels, int frequency, bool stream, PCMReaderCallback pcmreadercallback) => null;
        public bool SetData(float[] data, int offsetSamples) => true;
    }

    public enum RuntimePlatform { WindowsPlayer = 2, LinuxPlayer = 13, WebGLPlayer = 17 }

    public static class Application { public static RuntimePlatform platform => RuntimePlatform.LinuxPlayer; }

    public static class Time
    {
        public static float unscaledTime => 0f;
        public static float unscaledDeltaTime => 0f;
        public static float realtimeSinceStartup => 0f;
    }

    public static class PlayerPrefs
    {
        public static float GetFloat(string key, float defaultValue) => defaultValue;
        public static void SetFloat(string key, float value) { }
        public static int GetInt(string key, int defaultValue) => defaultValue;
        public static void SetInt(string key, int value) { }
        public static void Save() { }
    }

    public static class Debug
    {
        public static void Log(object message) { }
        public static void LogWarning(object message) { }
    }

    public struct Mathf
    {
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Clamp(float value, float min, float max) => value < min ? min : (value > max ? max : value);
        public static float Clamp01(float value) => value < 0f ? 0f : (value > 1f ? 1f : value);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float value) => a != b ? Clamp01((value - a) / (b - a)) : 0f;
        public static float MoveTowards(float current, float target, float maxDelta) => Math.Abs(target - current) <= maxDelta ? target : current + Math.Sign(target - current) * maxDelta;
    }

    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public float sqrMagnitude => x * x + y * y + z * z;
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
    }

    public class PropertyAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Field)] public sealed class SerializeField : Attribute { }
    [AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : PropertyAttribute { public RangeAttribute(float min, float max) { } }
    [AttributeUsage(AttributeTargets.Field)] public class TooltipAttribute : PropertyAttribute { public TooltipAttribute(string tooltip) { } }
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)] public class HeaderAttribute : PropertyAttribute { public HeaderAttribute(string header) { } }
    [AttributeUsage(AttributeTargets.Class)] public sealed class DisallowMultipleComponent : Attribute { }
    [AttributeUsage(AttributeTargets.Class)] public sealed class AddComponentMenu : Attribute { public AddComponentMenu(string menuName) { } }
    [AttributeUsage(AttributeTargets.Class)] public class DefaultExecutionOrder : Attribute { public DefaultExecutionOrder(int order) { } }

    public enum RuntimeInitializeLoadType { AfterSceneLoad = 0, BeforeSceneLoad = 1, AfterAssembliesLoaded = 2, BeforeSplashScreen = 3, SubsystemRegistration = 4 }

    [AttributeUsage(AttributeTargets.Method)]
    public class RuntimeInitializeOnLoadMethodAttribute : Attribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { }
    }
}
