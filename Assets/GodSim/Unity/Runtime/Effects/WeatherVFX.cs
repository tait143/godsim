using GodSim.Core;
using GodSim.Core.Composition;
using GodSim.Unity.Effects;
using GodSim.Unity.Units;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    /// <summary>
    /// Weather visuals driven by <see cref="WorldState.Weather"/>:
    /// camera-following rain (RainTicksRemaining &gt; 0 or Rainfall &gt; 1.2, heavier in storms), snow in
    /// Winter over cold biomes, storm darkening + occasional lightning, drought heat haze, and per-season
    /// ambient light / fog color through <see cref="RenderSettings"/> (scaled by
    /// <see cref="DayNightCycle.Daylight"/>). Exposes <see cref="SunDimming"/> for the day/night light.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WeatherVFX : SimulationBoundBehaviour
    {
        public static WeatherVFX Instance { get; private set; }

        [Header("Precipitation")]
        [Tooltip("Emitter height above the camera focus (world units).")]
        public float EmitterHeight = 18f;
        public float MinArea = 24f;
        public float MaxArea = 120f;
        public float MaxRainRate = 2500f;
        public float MaxSnowRate = 900f;
        [Tooltip("Seconds to fade precipitation in/out.")]
        public float FadeSeconds = 2f;

        [Header("Storms & drought")]
        public float StormLightningPerSecond = 0.3f;
        public float MaxHazeRate = 8f;

        [Header("Ambient & fog")]
        public bool ControlAmbient = true;
        public bool ForceTrilightAmbient = true;
        public Color DayAmbient = new Color(0.6f, 0.63f, 0.68f, 1f);
        public Color NightAmbient = new Color(0.12f, 0.14f, 0.22f, 1f);
        public bool ControlFog = true;
        public Color DayFog = new Color(0.72f, 0.8f, 0.88f, 1f);
        public Color NightFog = new Color(0.08f, 0.1f, 0.16f, 1f);
        public float BaseFogDensity = 0.004f;
        public Color DroughtTint = new Color(1f, 0.78f, 0.52f, 1f);
        public Color StormTint = new Color(0.55f, 0.58f, 0.64f, 1f);

        /// <summary>0..1 dimming of direct sunlight from storms/overcast (read by <see cref="DayNightCycle"/>).</summary>
        public float SunDimming { get; private set; }
        public float RainIntensity { get; private set; }
        public float SnowIntensity { get; private set; }
        public float DroughtIntensity { get; private set; }

        private Transform _rig;
        private ParticleSystem _rain, _snow, _haze;
        private MapFrame _frame;
        private float _coldFraction;
        private float _nextColdCheck;
        private Color _ambient, _fog;
        private bool _colorsInit;
        private float _flash;
        private uint _rng = 0xA341316Cu;

        protected override void OnEnable()
        {
            if (Instance != null && Instance != this) Debug.LogWarning("Multiple WeatherVFX instances; the last enabled one is WeatherVFX.Instance.", this);
            Instance = this;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            SetRate(_rain, 0f);
            SetRate(_snow, 0f);
            SetRate(_haze, 0f);
            if (Instance == this) Instance = null;
        }

        private void OnDestroy()
        {
            if (_rig != null) Destroy(_rig.gameObject);
        }

        protected override void OnSimulationBound(GodSimulation sim)
        {
            _frame = MapFrame.From(sim.State.Map);
            _nextColdCheck = 0f;
            RainIntensity = SnowIntensity = DroughtIntensity = 0f;
            if (_rain != null) _rain.Clear(true);
            if (_snow != null) _snow.Clear(true);
            if (_haze != null) _haze.Clear(true);
        }

        protected override void OnSimulationUnbound(GodSimulation old)
        {
            SetRate(_rain, 0f);
            SetRate(_snow, 0f);
            SetRate(_haze, 0f);
            SunDimming = 0f;
        }

        private void EnsureSystems()
        {
            if (_rig != null && _rain != null && _snow != null && _haze != null) return;
            VfxPool pool = VfxPool.GetOrCreate();
            if (pool == null) return;
            if (_rig == null)
            {
                var go = new GameObject("GodSim Weather");
                _rig = go.transform;
                _rig.SetParent(transform, false);
            }
            if (_rain == null) _rain = pool.CreateDedicated(VfxKind.Rain, "Rain", _rig);
            if (_snow == null) _snow = pool.CreateDedicated(VfxKind.Snow, "Snow", _rig);
            if (_haze == null) _haze = pool.CreateDedicated(VfxKind.HeatHaze, "Heat Haze", _rig);
        }

        private static void SetRate(ParticleSystem ps, float rate)
        {
            if (ps == null) return;
            var em = ps.emission;
            em.rateOverTimeMultiplier = rate;
        }

        private void LateUpdate()
        {
            float dt = Time.deltaTime;
            if (!EnsureBound()) { FadeOut(dt); return; }
            WorldState state = Sim.State;
            if (!ReferenceEquals(state.Map, _frame.Map)) { _frame = MapFrame.From(state.Map); _nextColdCheck = 0f; }
            Camera cam = Camera.main;
            if (cam == null) return;
            EnsureSystems();
            if (_rain == null) return;

            WeatherState w = state.Weather;
            Season season = state.Season;
            Vector3 focus = MapFrame.CameraFocus(cam, _frame.Origin.y);
            float camHeight = Mathf.Max(1f, cam.transform.position.y - focus.y);
            float area = Mathf.Clamp(camHeight * 1.2f, MinArea, MaxArea);

            if (Time.unscaledTime >= _nextColdCheck)
            {
                _nextColdCheck = Time.unscaledTime + 0.5f;
                _coldFraction = SampleCold(state.Map, focus, area, w.TemperatureOffset);
            }

            // Targets.
            float storm = Mathf.Clamp01(MathUtil.Sanitize(w.StormIntensity));
            float rainfall = MathUtil.Sanitize(w.Rainfall, 1f);
            bool raining = w.RainTicksRemaining > 0 || rainfall > 1.2f || storm > 0.3f;
            float precip = raining ? Mathf.Clamp01(0.35f + (rainfall - 1f) * 0.8f + storm * 0.6f) : 0f;
            bool winter = season == Season.Winter;
            float cold = winter ? _coldFraction : 0f;
            float snowTarget = cold > 0.05f ? Mathf.Clamp01(cold * (0.45f + 0.55f * precip)) : 0f;
            float rainTarget = precip * (1f - cold);
            float droughtTarget = w.DroughtTicksRemaining > 0 ? 1f : 0f;

            float step = dt / Mathf.Max(0.1f, FadeSeconds);
            RainIntensity = Mathf.MoveTowards(RainIntensity, rainTarget, step);
            SnowIntensity = Mathf.MoveTowards(SnowIntensity, snowTarget, step);
            DroughtIntensity = Mathf.MoveTowards(DroughtIntensity, droughtTarget, step * 0.5f);
            SunDimming = Mathf.MoveTowards(SunDimming, Mathf.Clamp01(storm * 0.6f + precip * 0.25f), step);

            // Camera-following emitters (world-space particles, so drops keep falling where they spawned).
            float areaScale = Mathf.Clamp((area * area) / (40f * 40f), 0.4f, 6f);
            _rig.position = new Vector3(focus.x, focus.y, focus.z);
            Vector3 top = new Vector3(focus.x, focus.y + EmitterHeight, focus.z);
            _rain.transform.position = top;
            _snow.transform.position = top;
            _haze.transform.position = focus;
            var rs = _rain.shape; rs.scale = new Vector3(area, area, 0.5f);
            var ss = _snow.shape; ss.scale = new Vector3(area, area, 0.5f);
            var hs = _haze.shape; hs.radius = area * 0.35f;
            SetRate(_rain, MaxRainRate * RainIntensity * areaScale);
            SetRate(_snow, MaxSnowRate * SnowIntensity * areaScale);
            SetRate(_haze, MaxHazeRate * DroughtIntensity * Mathf.Sqrt(areaScale));

            // Storm lightning (visual only; uses GodPowerVFX bolts when present).
            if (storm > 0.55f && dt > 0f && UnitMath.NextUnit(ref _rng) < StormLightningPerSecond * storm * dt)
            {
                _flash = 1f;
                GodPowerVFX vfx = GodPowerVFX.Instance;
                if (vfx != null)
                {
                    float ox = (UnitMath.NextUnit(ref _rng) - 0.5f) * area * 0.8f;
                    float oz = (UnitMath.NextUnit(ref _rng) - 0.5f) * area * 0.8f;
                    Vector2 tile = _frame.ClampTile(_frame.WorldToTile(new Vector3(focus.x + ox, 0f, focus.z + oz)));
                    vfx.StrikeLightning(GodSimCoords.TileToWorld(state.Map, tile.x, tile.y), 0.6f, false);
                }
            }
            _flash = Mathf.MoveTowards(_flash, 0f, dt * 4f);

            ApplyAmbient(season, storm, precip, dt);
        }

        private void FadeOut(float dt)
        {
            if (_rain == null) return;
            float step = dt / Mathf.Max(0.1f, FadeSeconds);
            RainIntensity = Mathf.MoveTowards(RainIntensity, 0f, step);
            SnowIntensity = Mathf.MoveTowards(SnowIntensity, 0f, step);
            DroughtIntensity = Mathf.MoveTowards(DroughtIntensity, 0f, step);
            SetRate(_rain, MaxRainRate * RainIntensity);
            SetRate(_snow, MaxSnowRate * SnowIntensity);
            SetRate(_haze, MaxHazeRate * DroughtIntensity);
        }

        private float SampleCold(WorldMap map, Vector3 focus, float area, float tempOffset)
        {
            Vector2 c = _frame.WorldToTile(focus);
            float stepTiles = Mathf.Max(1f, area / 5f / Mathf.Max(0.01f, _frame.TileSize));
            int cold = 0, total = 0;
            for (int j = -2; j <= 2; j++)
            {
                for (int i = -2; i <= 2; i++)
                {
                    int x = Mathf.RoundToInt(c.x + i * stepTiles), y = Mathf.RoundToInt(c.y + j * stepTiles);
                    if (!map.InBounds(x, y)) continue;
                    int idx = map.Index(x, y);
                    total++;
                    Biome b = map.Biomes[idx];
                    float temp = MathUtil.Sanitize(map.Temperature[idx], 0.5f) + MathUtil.Sanitize(tempOffset) * 0.3f;
                    if (b == Biome.Snow || b == Biome.Tundra || b == Biome.Taiga || b == Biome.Mountain || temp < 0.35f) cold++;
                }
            }
            return total > 0 ? cold / (float)total : 0f;
        }

        private void ApplyAmbient(Season season, float storm, float precip, float dt)
        {
            if (!ControlAmbient && !ControlFog) return;
            DayNightCycle dnc = DayNightCycle.Instance;
            float day = dnc != null && dnc.isActiveAndEnabled ? dnc.Daylight : 1f;
            Color seasonTint = GodSimPalette.Season(season);
            float overcast = Mathf.Clamp01(storm * 0.8f + precip * 0.3f);

            Color amb = Color.Lerp(NightAmbient, DayAmbient, day) * seasonTint;
            amb = Color.Lerp(amb, amb * StormTint, overcast);
            amb = Color.Lerp(amb, amb * DroughtTint, 0.5f * DroughtIntensity);
            amb += new Color(0.45f, 0.5f, 0.65f, 0f) * _flash;
            amb.a = 1f;

            Color fog = Color.Lerp(NightFog, DayFog, day) * seasonTint;
            fog = Color.Lerp(fog, StormTint * Mathf.Lerp(0.25f, 1f, day), overcast);
            fog = Color.Lerp(fog, fog * DroughtTint, 0.6f * DroughtIntensity);
            fog.a = 1f;

            float k = _colorsInit ? 1f - Mathf.Exp(-dt * 2f) : 1f;
            _ambient = Color.Lerp(_ambient, amb, k);
            _fog = Color.Lerp(_fog, fog, k);
            _colorsInit = true;

            if (ControlAmbient)
            {
                if (ForceTrilightAmbient && RenderSettings.ambientMode == AmbientMode.Skybox) RenderSettings.ambientMode = AmbientMode.Trilight;
                RenderSettings.ambientLight = _ambient;
                if (RenderSettings.ambientMode == AmbientMode.Trilight)
                {
                    RenderSettings.ambientEquatorColor = _ambient * 0.8f;
                    RenderSettings.ambientGroundColor = _ambient * 0.55f;
                }
            }
            if (ControlFog)
            {
                RenderSettings.fogColor = _fog;
                float winter = season == Season.Winter ? 0.5f : 0f;
                RenderSettings.fogDensity = Mathf.Max(0f, BaseFogDensity) * (1f + storm * 2f + precip + winter + DroughtIntensity * 0.5f);
            }
        }
    }
}
