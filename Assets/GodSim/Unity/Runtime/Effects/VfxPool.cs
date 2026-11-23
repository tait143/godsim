using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity.Effects
{
    /// <summary>
    /// Shared pool of code-configured <see cref="ParticleSystem"/>s for every GodSim effect
    /// (<see cref="VfxKind"/>). Emitters are created lazily up to <see cref="VfxDef.PoolCap"/> per kind and
    /// reused forever; bursts recycle the oldest emitter of their kind when the pool is exhausted.
    /// Also owns the procedural soft-particle texture and the alpha / additive particle materials.
    /// Created on demand by <see cref="GetOrCreate"/> (or add it to a scene object).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VfxPool : MonoBehaviour
    {
        public static VfxPool Instance { get; private set; }

        [Tooltip("Optional alpha-blended particle material (texture + vertex color). Built in code when empty.")]
        public Material AlphaMaterialOverride;
        [Tooltip("Optional additive particle material. Built in code when empty.")]
        public Material AdditiveMaterialOverride;
        [Tooltip("Global multiplier on emitted particle counts.")]
        [Range(0.1f, 2f)] public float Density = 1f;

        private sealed class Emitter
        {
            public ParticleSystem Ps;
            public Transform T;
            public VfxDef Def;
            public int Slot;
            public int Generation;
            public bool InUse;
            public bool Continuous;
            public bool Stopping;
            public float StartedAt;
            public float StopAt;
        }

        private readonly List<Emitter> _all = new List<Emitter>(128);
        private readonly List<Emitter>[] _byKind = new List<Emitter>[VfxPresets.Count];
        private Texture2D _texture;
        private Material _alpha;
        private Material _additive;
        private bool _ownsAlpha, _ownsAdditive;
        private static bool _quitting;

        /// <summary>The live pool, creating a "GodSim VFX Pool" object when none exists (null while quitting).</summary>
        public static VfxPool GetOrCreate()
        {
            if (Instance != null) return Instance;
            if (_quitting) return null;
            var go = new GameObject("GodSim VFX Pool");
            return go.AddComponent<VfxPool>();
        }

        /// <summary>Resets statics when domain reload is disabled (Enter Play Mode Options).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _quitting = false;
            Instance = null;
        }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Debug.LogWarning("Duplicate VfxPool destroyed.", this);
                Destroy(this);
                return;
            }
            Instance = this;
            for (int i = 0; i < _byKind.Length; i++) _byKind[i] = new List<Emitter>(8);
        }

        private void OnApplicationQuit() { _quitting = true; }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
            for (int i = 0; i < _all.Count; i++) if (_all[i].Ps != null) Destroy(_all[i].Ps.gameObject);
            _all.Clear();
            if (_ownsAlpha && _alpha != null) Destroy(_alpha);
            if (_ownsAdditive && _additive != null && _additive != _alpha) Destroy(_additive);
            if (_texture != null) Destroy(_texture);
        }

        // ------------------------------------------------------------------ materials

        /// <summary>Alpha-blended, vertex-colored particle material (also used for line effects).</summary>
        public Material AlphaMaterial
        {
            get
            {
                if (AlphaMaterialOverride != null) return AlphaMaterialOverride;
                if (_alpha == null)
                {
                    _alpha = CreateMaterial("GodSim Particles Alpha", "Sprites/Default", "Legacy Shaders/Particles/Alpha Blended", "Mobile/Particles/Alpha Blended");
                    _ownsAlpha = _alpha != null;
                    if (_alpha == null) _alpha = MaterialLibrary.Unlit(new Color(1f, 1f, 1f, 0.6f));
                }
                return _alpha;
            }
        }

        /// <summary>Additive particle material (falls back to the alpha material when no additive shader is available).</summary>
        public Material AdditiveMaterial
        {
            get
            {
                if (AdditiveMaterialOverride != null) return AdditiveMaterialOverride;
                if (_additive == null)
                {
                    _additive = CreateMaterial("GodSim Particles Additive", "Legacy Shaders/Particles/Additive", "Mobile/Particles/Additive", null);
                    _ownsAdditive = _additive != null;
                    if (_additive == null) _additive = AlphaMaterial;
                }
                return _additive;
            }
        }

        private Material CreateMaterial(string name, string a, string b, string c)
        {
            Shader s = Find(a);
            if (s == null && b != null) s = Find(b);
            if (s == null && c != null) s = Find(c);
            if (s == null) return null;
            var m = new Material(s) { name = name };
            Texture2D tex = SoftTexture();
            m.mainTexture = tex;
            if (m.HasProperty("_BaseMap")) m.SetTexture("_BaseMap", tex);
            return m;
        }

        private static Shader Find(string name)
        {
            Shader s = Shader.Find(name);
            return s != null && s.isSupported ? s : null;
        }

        private Texture2D SoftTexture()
        {
            if (_texture != null) return _texture;
            const int n = 32;
            var px = new Color32[n * n];
            for (int y = 0; y < n; y++)
            {
                for (int x = 0; x < n; x++)
                {
                    float dx = (x + 0.5f) / n * 2f - 1f, dy = (y + 0.5f) / n * 2f - 1f;
                    float r = Mathf.Clamp01(Mathf.Sqrt(dx * dx + dy * dy));
                    float a = (1f - r) * (1f - r);
                    px[y * n + x] = new Color32(255, 255, 255, (byte)(a * 255f));
                }
            }
            _texture = new Texture2D(n, n, TextureFormat.RGBA32, false) { name = "GodSim Soft Particle", wrapMode = TextureWrapMode.Clamp };
            _texture.SetPixels32(px);
            _texture.Apply(false, true);
            return _texture;
        }

        // ------------------------------------------------------------------ emitters

        /// <summary>Creates a standalone (not pooled) system of a kind, e.g. camera-following rain. Caller owns it.</summary>
        public ParticleSystem CreateDedicated(VfxKind kind, string name, Transform parent)
        {
            VfxDef def = VfxPresets.Get(kind);
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : transform, false);
            go.transform.localRotation = Quaternion.Euler(def.EmitUp ? -90f : 90f, 0f, 0f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            Configure(ps, def);
            var em = ps.emission;
            em.enabled = true;
            em.rateOverTimeMultiplier = 0f;
            ps.Play();
            return ps;
        }

        /// <summary>One-shot burst of a kind at a position; <paramref name="size"/> is in world units (1 = one tile).</summary>
        public void Burst(VfxKind kind, Vector3 position, float size) { Burst(kind, position, size, Color.white, -1); }

        /// <summary>One-shot burst tinted by <paramref name="tint"/> (multiplied into the preset colors).</summary>
        public void Burst(VfxKind kind, Vector3 position, float size, Color tint, int countOverride = -1)
        {
            Emitter e = Acquire(kind, true);
            if (e == null) return;
            Place(e, position, size, tint);
            e.Continuous = false;
            e.Stopping = true;
            var em = e.Ps.emission;
            em.enabled = false;
            float area = Mathf.Clamp(size * size, 0.5f, 6f);
            int count = countOverride > 0 ? countOverride : Mathf.Max(1, Mathf.RoundToInt(e.Def.Burst * area * Density));
            e.Ps.Play();
            e.Ps.Emit(Mathf.Min(count, e.Def.MaxParticles));
            // Stop emitting right away: live particles finish their life, then IsAlive turns false and the slot frees.
            e.Ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        /// <summary>
        /// Starts a continuous emitter; it stops by itself after <paramref name="duration"/> seconds
        /// (&lt;= 0: until <see cref="Stop"/>). Returns a handle, or -1 when this kind's pool is exhausted.
        /// </summary>
        public int Play(VfxKind kind, Vector3 position, float size, float duration) => Play(kind, position, size, duration, Color.white);

        public int Play(VfxKind kind, Vector3 position, float size, float duration, Color tint)
        {
            Emitter e = Acquire(kind, false);
            if (e == null) return -1;
            Place(e, position, size, tint);
            e.Continuous = true;
            e.Stopping = false;
            e.StopAt = duration > 0f ? Time.time + duration : -1f;
            var em = e.Ps.emission;
            em.enabled = true;
            em.rateOverTimeMultiplier = e.Def.Rate * Mathf.Clamp(size * size, 0.5f, 6f) * Density;
            e.Ps.Play();
            return Handle(e);
        }

        /// <summary>Stops emitting (live particles finish their life). Safe with stale / -1 handles.</summary>
        public void Stop(int handle)
        {
            Emitter e = Resolve(handle);
            if (e != null) BeginStop(e);
        }

        /// <summary>Moves a live emitter. Returns false when the handle is stale.</summary>
        public bool Move(int handle, Vector3 position)
        {
            Emitter e = Resolve(handle);
            if (e == null) return false;
            e.T.position = position;
            return true;
        }

        /// <summary>True while the handle's emitter is still emitting.</summary>
        public bool IsPlaying(int handle)
        {
            Emitter e = Resolve(handle);
            return e != null && !e.Stopping;
        }

        /// <summary>Stops and clears every pooled emitter (new world).</summary>
        public void StopAll()
        {
            for (int i = 0; i < _all.Count; i++)
            {
                Emitter e = _all[i];
                if (!e.InUse || e.Ps == null) continue;
                e.Ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                e.InUse = false;
                e.Generation++;
            }
        }

        private static int Handle(Emitter e) => (e.Slot << 8) | (e.Generation & 0xFF);

        private Emitter Resolve(int handle)
        {
            if (handle < 0) return null;
            int slot = handle >> 8;
            if (slot < 0 || slot >= _all.Count) return null;
            Emitter e = _all[slot];
            if (!e.InUse || (e.Generation & 0xFF) != (handle & 0xFF) || e.Ps == null) return null;
            return e;
        }

        private Emitter Acquire(VfxKind kind, bool recycleOldest)
        {
            int k = (int)kind;
            if (k < 0 || k >= _byKind.Length) return null;
            List<Emitter> list = _byKind[k];
            for (int i = list.Count - 1; i >= 0; i--)
            {
                Emitter e = list[i];
                if (e.Ps == null) { list.RemoveAt(i); continue; }
                if (!e.InUse) return Take(e);
            }
            VfxDef def = VfxPresets.Get(kind);
            if (list.Count < Mathf.Max(1, def.PoolCap)) return Take(Create(def, list));
            if (!recycleOldest) return null;
            Emitter oldest = null;
            for (int i = 0; i < list.Count; i++)
            {
                Emitter e = list[i];
                if (e.Continuous && !e.Stopping) continue; // never steal a running continuous effect
                if (oldest == null || e.StartedAt < oldest.StartedAt) oldest = e;
            }
            if (oldest == null) return null;
            oldest.Ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            return Take(oldest);
        }

        private Emitter Take(Emitter e)
        {
            e.InUse = true;
            e.Generation++;
            e.StartedAt = Time.time;
            e.StopAt = -1f;
            return e;
        }

        private Emitter Create(VfxDef def, List<Emitter> list)
        {
            var go = new GameObject("VFX " + def.Kind);
            go.transform.SetParent(transform, false);
            go.transform.localRotation = Quaternion.Euler(def.EmitUp ? -90f : 90f, 0f, 0f);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            Configure(ps, def);
            var e = new Emitter { Ps = ps, T = go.transform, Def = def, Slot = _all.Count };
            _all.Add(e);
            list.Add(e);
            return e;
        }

        private void Place(Emitter e, Vector3 position, float size, Color tint)
        {
            if (size < 0.01f) size = 0.01f;
            VfxDef def = e.Def;
            e.T.position = position;
            var main = e.Ps.main;
            main.startSizeMultiplier = 1f;
            main.startSize = new ParticleSystem.MinMaxCurve(def.SizeMin * size, def.SizeMax * size);
            float speedScale = Mathf.Sqrt(size);
            main.startSpeed = new ParticleSystem.MinMaxCurve(def.SpeedMin * speedScale, def.SpeedMax * speedScale);
            main.startColor = new ParticleSystem.MinMaxGradient(def.ColorA * tint, def.ColorB * tint);
            var shape = e.Ps.shape;
            shape.radius = Mathf.Max(0.01f, def.Radius * size);
            if (def.Shape == ParticleSystemShapeType.Box) shape.scale = new Vector3(def.BoxSize.x * size, def.BoxSize.y * size, def.BoxSize.z);
        }

        /// <summary>Applies a preset to a freshly added system (stopped, world space, not looping on awake).</summary>
        public void Configure(ParticleSystem ps, VfxDef def)
        {
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = true;
            main.duration = 1f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.scalingMode = ParticleSystemScalingMode.Local;
            main.maxParticles = Mathf.Max(8, def.MaxParticles);
            main.startLifetime = new ParticleSystem.MinMaxCurve(def.LifeMin, def.LifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(def.SpeedMin, def.SpeedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(def.SizeMin, def.SizeMax);
            main.startColor = new ParticleSystem.MinMaxGradient(def.ColorA, def.ColorB);
            main.gravityModifier = new ParticleSystem.MinMaxCurve(def.Gravity);

            var em = ps.emission;
            em.enabled = false;
            em.rateOverTime = new ParticleSystem.MinMaxCurve(def.Rate);

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = def.Shape;
            shape.radius = def.Radius;
            shape.angle = def.Angle;
            if (def.Shape == ParticleSystemShapeType.Box) shape.scale = def.BoxSize;

            var col = ps.colorOverLifetime;
            col.enabled = true;
            var g = new Gradient();
            if (def.Fade == VfxFade.InOut)
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.2f), new GradientAlphaKey(0.8f, 0.7f), new GradientAlphaKey(0f, 1f) });
            else
                g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                    new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.85f, 0.5f), new GradientAlphaKey(0f, 1f) });
            col.color = new ParticleSystem.MinMaxGradient(g);

            var sol = ps.sizeOverLifetime;
            if (def.Growth == VfxGrowth.Constant) sol.enabled = false;
            else
            {
                sol.enabled = true;
                AnimationCurve curve = def.Growth == VfxGrowth.Grow
                    ? new AnimationCurve(new Keyframe(0f, 0.45f), new Keyframe(1f, 1f))
                    : new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0.1f));
                sol.size = new ParticleSystem.MinMaxCurve(1f, curve);
            }

            var psr = ps.GetComponent<ParticleSystemRenderer>();
            if (psr != null)
            {
                psr.sharedMaterial = def.Additive ? AdditiveMaterial : AlphaMaterial;
                psr.renderMode = def.Stretch ? ParticleSystemRenderMode.Stretch : ParticleSystemRenderMode.Billboard;
                if (def.Stretch) { psr.lengthScale = def.LengthScale; psr.velocityScale = def.VelocityScale; }
                psr.maxParticleSize = def.MaxScreenSize;
                psr.shadowCastingMode = ShadowCastingMode.Off;
                psr.receiveShadows = false;
            }
        }

        private void BeginStop(Emitter e)
        {
            if (e.Stopping) return;
            e.Stopping = true;
            e.StartedAt = Time.time;
            e.Ps.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void Update()
        {
            float now = Time.time;
            for (int i = 0; i < _all.Count; i++)
            {
                Emitter e = _all[i];
                if (!e.InUse) continue;
                if (e.Ps == null) { e.InUse = false; continue; }
                if (e.Continuous && !e.Stopping && e.StopAt > 0f && now >= e.StopAt) BeginStop(e);
                if (e.Stopping && now - e.StartedAt > 0.1f && !e.Ps.IsAlive(true))
                {
                    e.InUse = false;
                    e.Generation++;
                }
            }
        }
    }
}
