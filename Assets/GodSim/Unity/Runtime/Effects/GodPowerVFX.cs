using System;
using GodSim.Core;
using GodSim.Core.Composition;
using GodSim.Unity.Effects;
using GodSim.Unity.Units;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    /// <summary>
    /// Visuals for <see cref="GodPowerCastEvent"/> (successful casts) and <see cref="DisasterEvent"/>:
    /// lightning (jagged LineRenderer bolt + flash light), meteor (streak from the sky, explosion, rock dust,
    /// shake), earthquake (dust + shake), plague (green mist), rain/flood (splashes), bless/heal/inspire
    /// (golden sparkles), fertility/plant forest (green motes), spawn tribe (pillar of light), raise/lower
    /// land (rock dust), wildfire, drought (heat haze), volcano and storms.
    /// Events are queued by the EventBus handlers and played in LateUpdate (bounded per frame); a disaster
    /// that merely echoes a god power just cast at the same place is not played twice.
    /// All particle systems come from the shared, code-configured <see cref="VfxPool"/>; bolts, meteors and
    /// flash lights are pooled here.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GodPowerVFX : SimulationBoundBehaviour
    {
        public static GodPowerVFX Instance { get; private set; }

        /// <summary>Camera shake request (0..1+ intensity). The camera controller subscribes; remember to unsubscribe.</summary>
        public static event Action<float> CameraShake;

        [Header("General")]
        public float ShakeScale = 1f;
        [Range(1, 32)] public int MaxEventsPerFrame = 6;
        public bool UseLights = true;

        [Header("Lightning")]
        [Tooltip("Bolt start height above the target (world units).")]
        public float BoltHeight = 26f;
        public float BoltWidth = 0.35f;
        public Color BoltColor = new Color(0.85f, 0.9f, 1f, 1f);

        [Header("Meteor")]
        public float MeteorDistance = 48f;
        public float MeteorFlightSeconds = 0.8f;

        private const int BoltSegments = 14;
        private const int MaxBolts = 6;
        private const int MaxMeteors = 4;
        private const int MaxFlashes = 6;

        private struct Pending
        {
            public bool IsPower;
            public bool Skip;
            public GodPowerType Power;
            public DisasterType Disaster;
            public Vec2i Target;
            public int Radius;
            public float Severity;
            public long Tick;
        }

        private struct RecentCast
        {
            public bool Valid;
            public long Tick;
            public DisasterType Kind;
            public Vec2i Pos;
            public int Radius;
        }

        private sealed class Bolt
        {
            public LineRenderer Line;
            public Vector3[] Points = new Vector3[BoltSegments];
            public Vector3 From, To;
            public float Start, Duration, NextRejag, Width;
            public uint Seed;
            public bool Active;
        }

        private sealed class Meteor
        {
            public LineRenderer Line;
            public Vector3[] Points = new Vector3[2];
            public Vector3 From, To;
            public float Start, Duration, Size, Severity;
            public int Trail = -1;
            public bool Active;
        }

        private sealed class FlashSlot
        {
            public Light Light;
            public float Start, Duration, Peak;
            public bool Active;
        }

        private readonly Pending[] _queue = new Pending[64];
        private int _qHead, _qCount;
        private readonly RecentCast[] _recent = new RecentCast[8];
        private int _recentNext;
        private readonly Bolt[] _bolts = new Bolt[MaxBolts];
        private readonly Meteor[] _meteors = new Meteor[MaxMeteors];
        private readonly FlashSlot[] _flashes = new FlashSlot[MaxFlashes];
        private Transform _root;
        private MapFrame _frame;
        private uint _rng = 0x9E3779B9u;
        private Action<GodPowerCastEvent> _onCast;
        private Action<DisasterEvent> _onDisaster;
        private GodSimulation _subscribed;

        /// <summary>Drops stale CameraShake subscribers when domain reload is disabled (Enter Play Mode Options).</summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            CameraShake = null;
            Instance = null;
        }

        private void Awake()
        {
            _onCast = OnCast;
            _onDisaster = OnDisaster;
        }

        protected override void OnEnable()
        {
            if (Instance != null && Instance != this) Debug.LogWarning("Multiple GodPowerVFX instances; the last enabled one is GodPowerVFX.Instance.", this);
            Instance = this;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            DeactivateAll();
            if (Instance == this) Instance = null;
        }

        private void OnDestroy()
        {
            if (_root != null) Destroy(_root.gameObject);
        }

        protected override void OnSimulationBound(GodSimulation sim)
        {
            _frame = MapFrame.From(sim.State.Map);
            ClearQueue();
            DeactivateAll();
            if (sim.Events != null)
            {
                sim.Events.Subscribe(_onCast);
                sim.Events.Subscribe(_onDisaster);
                _subscribed = sim;
            }
        }

        protected override void OnSimulationUnbound(GodSimulation old)
        {
            if (_subscribed != null && _subscribed.Events != null)
            {
                _subscribed.Events.Unsubscribe(_onCast);
                _subscribed.Events.Unsubscribe(_onDisaster);
            }
            _subscribed = null;
            ClearQueue();
            DeactivateAll();
            VfxPool pool = VfxPool.Instance;
            if (pool != null) pool.StopAll(); // effects of the old world must not linger in the new one
        }

        private void ClearQueue()
        {
            _qHead = 0;
            _qCount = 0;
            for (int i = 0; i < _recent.Length; i++) _recent[i].Valid = false;
        }

        // ------------------------------------------------------------------ event intake (sim thread = main thread)

        private long CurrentTick => _subscribed != null && _subscribed.State != null ? _subscribed.State.Tick : 0L;

        private void OnCast(GodPowerCastEvent e)
        {
            if (!e.Success) return;
            long tick = CurrentTick;
            DisasterType echo;
            if (DisasterEquivalent(e.Power, out echo))
            {
                _recent[_recentNext] = new RecentCast { Valid = true, Tick = tick, Kind = echo, Pos = e.Target, Radius = e.Radius };
                _recentNext = (_recentNext + 1) % _recent.Length;
                // A disaster echo that was published before the cast event: drop it.
                for (int i = 0; i < _qCount; i++)
                {
                    int qi = (_qHead + i) % _queue.Length;
                    if (!_queue[qi].IsPower && _queue[qi].Disaster == echo && Near(_queue[qi].Target, e.Target, e.Radius) && Math.Abs(_queue[qi].Tick - tick) <= 1)
                        _queue[qi].Skip = true;
                }
            }
            Enqueue(new Pending { IsPower = true, Power = e.Power, Target = e.Target, Radius = e.Radius, Severity = 1f, Tick = tick });
        }

        private void OnDisaster(DisasterEvent e)
        {
            long tick = CurrentTick;
            for (int i = 0; i < _recent.Length; i++)
            {
                if (_recent[i].Valid && _recent[i].Kind == e.Type && Math.Abs(_recent[i].Tick - tick) <= 1 && Near(_recent[i].Pos, e.Position, _recent[i].Radius))
                    return;
            }
            Enqueue(new Pending { IsPower = false, Disaster = e.Type, Target = e.Position, Radius = e.Radius, Severity = MathUtil.Clamp(MathUtil.Sanitize(e.Severity, 1f), 0.1f, 3f), Tick = tick });
        }

        private static bool Near(Vec2i a, Vec2i b, int radius) => a.ChebyshevTo(b) <= Math.Max(1, radius) + 1;

        private void Enqueue(Pending p)
        {
            if (_qCount >= _queue.Length) { _qHead = (_qHead + 1) % _queue.Length; _qCount--; }
            _queue[(_qHead + _qCount) % _queue.Length] = p;
            _qCount++;
        }

        private static bool DisasterEquivalent(GodPowerType p, out DisasterType d)
        {
            switch (p)
            {
                case GodPowerType.Lightning: d = DisasterType.Lightning; return true;
                case GodPowerType.Wildfire: d = DisasterType.Wildfire; return true;
                case GodPowerType.Earthquake: d = DisasterType.Earthquake; return true;
                case GodPowerType.Meteor: d = DisasterType.Meteor; return true;
                case GodPowerType.Plague: d = DisasterType.Plague; return true;
                case GodPowerType.Flood: d = DisasterType.Flood; return true;
                case GodPowerType.Drought: d = DisasterType.Drought; return true;
                default: d = DisasterType.Famine; return false;
            }
        }

        // ------------------------------------------------------------------ frame

        private void LateUpdate()
        {
            bool bound = EnsureBound();
            if (bound)
            {
                WorldMap map = Sim.State.Map;
                if (!ReferenceEquals(map, _frame.Map)) _frame = MapFrame.From(map);
                int budget = Mathf.Max(1, MaxEventsPerFrame);
                while (_qCount > 0 && budget-- > 0)
                {
                    Pending p = _queue[_qHead];
                    _qHead = (_qHead + 1) % _queue.Length;
                    _qCount--;
                    if (p.Skip || !map.InBounds(p.Target)) continue;
                    if (p.IsPower) PlayPower(map, p); else PlayDisaster(map, p);
                }
            }
            float now = Time.time;
            UpdateBolts(now);
            UpdateMeteors(now, bound ? Sim.State.Map : null);
            UpdateFlashes(now);
        }

        private static void Shake(float amount)
        {
            Action<float> h = CameraShake;
            if (h != null && amount > 0f) h(amount);
        }

        private float Tile => _frame.TileSize > 0f ? _frame.TileSize : 1f;

        private Vector3 W(WorldMap map, Vec2i t) => GodSimCoords.TileToWorld(map, t);

        private float AreaSize(int radius) => Mathf.Clamp(radius, 1, 12) * Tile;

        /// <summary>Bursts spread over the area: one at the center plus a few at random points (bigger radii).</summary>
        private void AreaBurst(VfxPool pool, WorldMap map, VfxKind kind, Vec2i c, int radius, float size, Color tint)
        {
            pool.Burst(kind, W(map, c), size, tint);
            int extra = Mathf.Clamp(radius / 2, 0, 4);
            for (int i = 0; i < extra; i++)
            {
                float a = UnitMath.NextUnit(ref _rng) * UnitMath.TwoPi;
                float r = (0.35f + 0.65f * UnitMath.NextUnit(ref _rng)) * radius;
                float x = c.X + Mathf.Cos(a) * r, y = c.Y + Mathf.Sin(a) * r;
                pool.Burst(kind, GodSimCoords.TileToWorld(map, x, y), size * 0.7f, tint);
            }
        }

        private void PlayPower(WorldMap map, Pending p)
        {
            VfxPool pool = VfxPool.GetOrCreate();
            if (pool == null) return;
            Vector3 pos = W(map, p.Target);
            float area = AreaSize(p.Radius);
            Color white = Color.white;
            switch (p.Power)
            {
                case GodPowerType.Bless:
                case GodPowerType.BountifulHarvest:
                    pool.Play(VfxKind.Sparkles, pos, area, 1.8f, white);
                    Flash(pos + Vector3.up * Tile, new Color(1f, 0.85f, 0.4f, 1f), 2.5f, area * 2.5f, 0.9f);
                    break;
                case GodPowerType.Heal:
                    pool.Play(VfxKind.Sparkles, pos, area, 1.8f, new Color(1f, 1f, 0.85f, 1f));
                    pool.Play(VfxKind.Motes, pos, area * 0.8f, 1.5f, new Color(1f, 1f, 1f, 1f));
                    break;
                case GodPowerType.Inspire:
                    pool.Play(VfxKind.Sparkles, pos, area, 1.6f, new Color(0.75f, 0.9f, 1f, 1f));
                    break;
                case GodPowerType.Prophet:
                    pool.Play(VfxKind.LightPillar, pos, Tile, 1.2f, white);
                    pool.Play(VfxKind.Sparkles, pos, Tile * 1.5f, 1.5f, white);
                    Flash(pos + Vector3.up * 2f * Tile, new Color(1f, 0.9f, 0.6f, 1f), 3f, 10f * Tile, 1f);
                    break;
                case GodPowerType.Rain:
                    pool.Play(VfxKind.Splash, pos, area, 2.5f, white);
                    pool.Play(VfxKind.Mist, pos, area, 2f, new Color(0.6f, 0.75f, 1f, 0.6f));
                    break;
                case GodPowerType.Flood:
                    pool.Play(VfxKind.Splash, pos, area * 1.3f, 3.5f, white);
                    AreaBurst(pool, map, VfxKind.Splash, p.Target, p.Radius, area, white);
                    pool.Play(VfxKind.Mist, pos, area * 1.2f, 3f, new Color(0.55f, 0.7f, 1f, 0.8f));
                    break;
                case GodPowerType.Fertility:
                case GodPowerType.PlantForest:
                    pool.Play(VfxKind.Motes, pos, area, 2f, white);
                    break;
                case GodPowerType.RevealOre:
                    pool.Play(VfxKind.Sparkles, pos, area, 1.5f, new Color(0.85f, 0.85f, 0.95f, 1f));
                    break;
                case GodPowerType.SpawnTribe:
                    pool.Play(VfxKind.LightPillar, pos, Tile * 1.4f, 1.6f, white);
                    pool.Burst(VfxKind.Sparkles, pos, Tile * 2f);
                    Flash(pos + Vector3.up * 2f * Tile, new Color(1f, 0.92f, 0.7f, 1f), 4f, 14f * Tile, 1.4f);
                    break;
                case GodPowerType.RaiseLand:
                case GodPowerType.LowerLand:
                    AreaBurst(pool, map, VfxKind.RockDust, p.Target, p.Radius, area, white);
                    Shake(0.25f * ShakeScale);
                    break;
                case GodPowerType.Lightning:
                    StrikeLightning(pos, 1f, true);
                    break;
                case GodPowerType.Wildfire:
                    PlayWildfire(pool, map, p.Target, p.Radius, area);
                    break;
                case GodPowerType.Earthquake:
                    PlayEarthquake(pool, map, p.Target, p.Radius, area, 1f);
                    break;
                case GodPowerType.Meteor:
                    LaunchMeteor(pos, Mathf.Max(area, 1.5f * Tile), 1f);
                    break;
                case GodPowerType.Plague:
                    pool.Play(VfxKind.Mist, pos, area, 4f, white);
                    AreaBurst(pool, map, VfxKind.Mist, p.Target, p.Radius, area, white);
                    break;
                case GodPowerType.Drought:
                    pool.Play(VfxKind.HeatHaze, pos, area * 0.6f, 4f, white);
                    pool.Burst(VfxKind.Dust, pos, area, new Color(1f, 0.85f, 0.6f, 1f));
                    break;
            }
        }

        private void PlayDisaster(WorldMap map, Pending p)
        {
            VfxPool pool = VfxPool.GetOrCreate();
            if (pool == null) return;
            Vector3 pos = W(map, p.Target);
            float area = AreaSize(p.Radius) * Mathf.Lerp(0.8f, 1.3f, Mathf.Clamp01(p.Severity * 0.5f));
            Color white = Color.white;
            switch (p.Disaster)
            {
                case DisasterType.Lightning:
                    StrikeLightning(pos, Mathf.Clamp(p.Severity, 0.5f, 1.5f), true);
                    break;
                case DisasterType.Storm:
                    for (int i = 0; i < 3; i++)
                    {
                        float a = UnitMath.NextUnit(ref _rng) * UnitMath.TwoPi;
                        float r = UnitMath.NextUnit(ref _rng) * Mathf.Max(1, p.Radius);
                        StrikeLightning(GodSimCoords.TileToWorld(map, p.Target.X + Mathf.Cos(a) * r, p.Target.Y + Mathf.Sin(a) * r), 0.8f, i == 0);
                    }
                    pool.Play(VfxKind.Splash, pos, area, 3f, white);
                    break;
                case DisasterType.Wildfire:
                    PlayWildfire(pool, map, p.Target, p.Radius, area);
                    break;
                case DisasterType.Earthquake:
                    PlayEarthquake(pool, map, p.Target, p.Radius, area, p.Severity);
                    break;
                case DisasterType.Flood:
                    pool.Play(VfxKind.Splash, pos, area * 1.3f, 3.5f, white);
                    AreaBurst(pool, map, VfxKind.Splash, p.Target, p.Radius, area, white);
                    pool.Play(VfxKind.Mist, pos, area * 1.2f, 3f, new Color(0.55f, 0.7f, 1f, 0.8f));
                    break;
                case DisasterType.Drought:
                    pool.Play(VfxKind.HeatHaze, pos, area * 0.6f, 4f, white);
                    break;
                case DisasterType.Plague:
                    pool.Play(VfxKind.Mist, pos, area, 4f, white);
                    break;
                case DisasterType.Meteor:
                    LaunchMeteor(pos, Mathf.Max(area, 1.5f * Tile), p.Severity);
                    break;
                case DisasterType.Volcano:
                    pool.Burst(VfxKind.Explosion, pos, area);
                    pool.Play(VfxKind.Smoke, pos, area * 1.5f, 6f, new Color(0.6f, 0.55f, 0.5f, 1f));
                    pool.Play(VfxKind.Embers, pos, area, 4f, white);
                    AreaBurst(pool, map, VfxKind.RockDust, p.Target, p.Radius, area, white);
                    Flash(pos + Vector3.up * Tile, new Color(1f, 0.45f, 0.15f, 1f), 6f, area * 5f, 1.5f);
                    Shake(0.8f * p.Severity * ShakeScale);
                    break;
                case DisasterType.Famine:
                    pool.Burst(VfxKind.Dust, pos, area * 0.8f, new Color(0.7f, 0.65f, 0.5f, 0.7f));
                    break;
            }
        }

        private void PlayWildfire(VfxPool pool, WorldMap map, Vec2i c, int radius, float area)
        {
            Vector3 pos = W(map, c);
            pool.Burst(VfxKind.Flame, pos, area * 0.8f);
            pool.Burst(VfxKind.Embers, pos, area);
            pool.Play(VfxKind.Smoke, pos, area, 2.5f, Color.white);
            Flash(pos + Vector3.up * Tile, new Color(1f, 0.55f, 0.2f, 1f), 3f, area * 3f, 0.8f);
        }

        private void PlayEarthquake(VfxPool pool, WorldMap map, Vec2i c, int radius, float area, float severity)
        {
            AreaBurst(pool, map, VfxKind.Dust, c, Math.Max(radius, 4), area, Color.white);
            AreaBurst(pool, map, VfxKind.RockDust, c, radius, area * 0.7f, Color.white);
            Shake(0.7f * Mathf.Clamp(severity, 0.3f, 2f) * ShakeScale);
        }

        // ------------------------------------------------------------------ lightning

        /// <summary>Strikes a jagged bolt at a ground position (also used by <see cref="WeatherVFX"/> storms).</summary>
        public void StrikeLightning(Vector3 ground, float intensity, bool shake)
        {
            Bolt b = AcquireBolt();
            if (b == null) return;
            float now = Time.time;
            float tile = Tile;
            b.To = ground;
            float ox = (UnitMath.NextUnit(ref _rng) - 0.5f) * 6f * tile, oz = (UnitMath.NextUnit(ref _rng) - 0.5f) * 6f * tile;
            b.From = ground + new Vector3(ox, BoltHeight * tile, oz);
            b.Start = now;
            b.Duration = 0.35f;
            b.NextRejag = now + 0.09f;
            b.Width = BoltWidth * tile * Mathf.Clamp(intensity, 0.3f, 2f);
            b.Seed = _rng ^ 0x5bd1e995u;
            b.Active = true;
            Jag(b);
            b.Line.widthMultiplier = b.Width;
            b.Line.enabled = true;

            Flash(ground + Vector3.up * 2f * tile, new Color(0.75f, 0.85f, 1f, 1f), 7f * intensity, 16f * tile, 0.35f);
            VfxPool pool = VfxPool.GetOrCreate();
            if (pool != null)
            {
                pool.Burst(VfxKind.Clash, ground, tile * 1.2f, new Color(0.8f, 0.9f, 1f, 1f));
                pool.Burst(VfxKind.Dust, ground, tile, Color.white);
            }
            if (shake) Shake(0.2f * intensity * ShakeScale);
        }

        private void Jag(Bolt b)
        {
            uint s = b.Seed;
            Vector3 d = b.To - b.From;
            float len = d.magnitude;
            float amp = len * 0.06f;
            for (int i = 0; i < BoltSegments; i++)
            {
                float t = i / (float)(BoltSegments - 1);
                Vector3 p = b.From + d * t;
                if (i > 0 && i < BoltSegments - 1)
                {
                    float taper = Mathf.Sin(t * Mathf.PI);
                    p.x += (UnitMath.NextUnit(ref s) - 0.5f) * 2f * amp * taper;
                    p.z += (UnitMath.NextUnit(ref s) - 0.5f) * 2f * amp * taper;
                }
                b.Points[i] = p;
            }
            b.Seed = s;
            b.Line.positionCount = BoltSegments;
            b.Line.SetPositions(b.Points);
        }

        private Bolt AcquireBolt()
        {
            EnsureRoot();
            Bolt oldest = null;
            for (int i = 0; i < _bolts.Length; i++)
            {
                if (_bolts[i] == null || _bolts[i].Line == null) _bolts[i] = CreateBolt(i);
                if (!_bolts[i].Active) return _bolts[i];
                if (oldest == null || _bolts[i].Start < oldest.Start) oldest = _bolts[i];
            }
            return oldest;
        }

        private Bolt CreateBolt(int i)
        {
            var go = new GameObject("Bolt " + i);
            go.transform.SetParent(_root, false);
            LineRenderer lr = go.AddComponent<LineRenderer>();
            ConfigureLine(lr);
            lr.positionCount = BoltSegments;
            lr.enabled = false;
            return new Bolt { Line = lr };
        }

        private void ConfigureLine(LineRenderer lr)
        {
            VfxPool pool = VfxPool.GetOrCreate();
            if (pool != null) lr.sharedMaterial = pool.AdditiveMaterial;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
        }

        private void UpdateBolts(float now)
        {
            for (int i = 0; i < _bolts.Length; i++)
            {
                Bolt b = _bolts[i];
                if (b == null || !b.Active || b.Line == null) continue;
                float t = (now - b.Start) / b.Duration;
                if (t >= 1f) { b.Active = false; b.Line.enabled = false; continue; }
                if (now >= b.NextRejag) { b.NextRejag = now + 0.09f; Jag(b); }
                float flicker = 0.6f + 0.4f * Mathf.Abs(Mathf.Sin(now * 60f + i));
                float a = (1f - t) * flicker;
                Color c0 = BoltColor; c0.a = a;
                Color c1 = BoltColor; c1.a = a * 0.8f;
                b.Line.startColor = c0;
                b.Line.endColor = c1;
                b.Line.widthMultiplier = b.Width * (1f - 0.5f * t);
            }
        }

        // ------------------------------------------------------------------ meteor

        private void LaunchMeteor(Vector3 target, float size, float severity)
        {
            EnsureRoot();
            Meteor m = null;
            for (int i = 0; i < _meteors.Length; i++)
            {
                if (_meteors[i] == null || _meteors[i].Line == null)
                {
                    var go = new GameObject("Meteor " + i);
                    go.transform.SetParent(_root, false);
                    LineRenderer lr = go.AddComponent<LineRenderer>();
                    ConfigureLine(lr);
                    lr.positionCount = 2;
                    lr.enabled = false;
                    _meteors[i] = new Meteor { Line = lr };
                }
                if (!_meteors[i].Active) { m = _meteors[i]; break; }
            }
            if (m == null) { Impact(target, size, severity); return; }

            float tile = Tile;
            Vector3 dir = new Vector3(-0.35f + (UnitMath.NextUnit(ref _rng) - 0.5f) * 0.3f, 1f, 0.25f + (UnitMath.NextUnit(ref _rng) - 0.5f) * 0.3f).normalized;
            m.From = target + dir * MeteorDistance * tile;
            m.To = target;
            m.Start = Time.time;
            m.Duration = Mathf.Max(0.2f, MeteorFlightSeconds);
            m.Size = size;
            m.Severity = Mathf.Clamp(severity, 0.3f, 3f);
            m.Active = true;
            m.Line.widthMultiplier = 0.9f * tile * Mathf.Sqrt(m.Size / tile);
            m.Line.startColor = new Color(1f, 0.9f, 0.6f, 1f);
            m.Line.endColor = new Color(1f, 0.35f, 0.05f, 0f);
            m.Line.enabled = true;
            VfxPool pool = VfxPool.GetOrCreate();
            m.Trail = pool != null ? pool.Play(VfxKind.Embers, m.From, tile * 1.5f, m.Duration) : -1;
        }

        private void UpdateMeteors(float now, WorldMap map)
        {
            VfxPool pool = VfxPool.Instance;
            for (int i = 0; i < _meteors.Length; i++)
            {
                Meteor m = _meteors[i];
                if (m == null || !m.Active || m.Line == null) continue;
                float t = Mathf.Clamp01((now - m.Start) / m.Duration);
                float eased = t * t * (0.6f + 0.4f * t);
                Vector3 head = Vector3.Lerp(m.From, m.To, eased);
                Vector3 back = (m.From - m.To).normalized;
                float tailLen = Mathf.Min(12f * Tile, (head - m.From).magnitude);
                m.Points[0] = head;
                m.Points[1] = head + back * tailLen;
                m.Line.SetPositions(m.Points);
                if (pool != null) pool.Move(m.Trail, head);
                if (t >= 1f)
                {
                    m.Active = false;
                    m.Line.enabled = false;
                    if (pool != null) pool.Stop(m.Trail);
                    m.Trail = -1;
                    if (map != null) Impact(m.To, m.Size, m.Severity);
                }
            }
        }

        private void Impact(Vector3 pos, float size, float severity)
        {
            VfxPool pool = VfxPool.GetOrCreate();
            if (pool != null)
            {
                pool.Burst(VfxKind.Explosion, pos, size);
                pool.Burst(VfxKind.RockDust, pos, size * 1.3f);
                pool.Burst(VfxKind.Embers, pos, size);
                pool.Play(VfxKind.Smoke, pos, size, 3f, Color.white);
            }
            Flash(pos + Vector3.up * Tile, new Color(1f, 0.6f, 0.25f, 1f), 8f * severity, size * 6f, 0.7f);
            Shake(0.9f * severity * ShakeScale);
        }

        // ------------------------------------------------------------------ flash lights

        private void Flash(Vector3 pos, Color color, float intensity, float range, float duration)
        {
            if (!UseLights) return;
            EnsureRoot();
            FlashSlot f = null, oldest = null;
            for (int i = 0; i < _flashes.Length; i++)
            {
                if (_flashes[i] == null || _flashes[i].Light == null)
                {
                    var go = new GameObject("Flash " + i);
                    go.transform.SetParent(_root, false);
                    Light l = go.AddComponent<Light>();
                    l.type = LightType.Point;
                    l.shadows = LightShadows.None;
                    l.enabled = false;
                    _flashes[i] = new FlashSlot { Light = l };
                }
                if (!_flashes[i].Active) { f = _flashes[i]; break; }
                if (oldest == null || _flashes[i].Start < oldest.Start) oldest = _flashes[i];
            }
            if (f == null) f = oldest;
            if (f == null) return;
            f.Light.transform.position = pos;
            f.Light.color = color;
            f.Light.range = Mathf.Max(0.5f, range);
            f.Light.intensity = intensity;
            f.Light.enabled = true;
            f.Start = Time.time;
            f.Duration = Mathf.Max(0.05f, duration);
            f.Peak = intensity;
            f.Active = true;
        }

        private void UpdateFlashes(float now)
        {
            for (int i = 0; i < _flashes.Length; i++)
            {
                FlashSlot f = _flashes[i];
                if (f == null || !f.Active || f.Light == null) continue;
                float t = (now - f.Start) / f.Duration;
                if (t >= 1f) { f.Active = false; f.Light.enabled = false; continue; }
                f.Light.intensity = f.Peak * (1f - t) * (1f - t);
            }
        }

        private void EnsureRoot()
        {
            if (_root != null) return;
            var go = new GameObject("GodSim Power VFX");
            _root = go.transform;
            _root.SetParent(transform, false);
        }

        private void DeactivateAll()
        {
            for (int i = 0; i < _bolts.Length; i++) if (_bolts[i] != null) { _bolts[i].Active = false; if (_bolts[i].Line != null) _bolts[i].Line.enabled = false; }
            VfxPool pool = VfxPool.Instance;
            for (int i = 0; i < _meteors.Length; i++)
            {
                Meteor m = _meteors[i];
                if (m == null) continue;
                m.Active = false;
                if (m.Line != null) m.Line.enabled = false;
                if (pool != null) pool.Stop(m.Trail);
                m.Trail = -1;
            }
            for (int i = 0; i < _flashes.Length; i++) if (_flashes[i] != null) { _flashes[i].Active = false; if (_flashes[i].Light != null) _flashes[i].Light.enabled = false; }
        }
    }
}
