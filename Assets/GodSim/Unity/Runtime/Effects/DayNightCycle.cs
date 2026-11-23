using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Rotates the main directional light (RenderSettings.sun, else the first directional light in the scene,
    /// optionally a created one) through a day/night cycle. The cycle length is in real seconds and scales
    /// gently (square root, clamped) with the runner's game speed; it freezes while the game is paused.
    /// Color temperature runs warm at sunrise/sunset, neutral at noon and cool (moonlight) at night.
    /// <see cref="DayPhase"/> (0 = midnight, 0.25 sunrise, 0.5 noon, 0.75 sunset) and <see cref="Daylight"/>
    /// drive <see cref="PeopleRenderer"/> schedules and <see cref="WeatherVFX"/> ambient light.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DayNightCycle : MonoBehaviour
    {
        public static DayNightCycle Instance { get; private set; }

        [Tooltip("Light to drive. Found automatically when empty.")]
        public Light Sun;
        public bool CreateSunIfMissing = true;

        [Header("Timing")]
        [Tooltip("Real seconds per full day at the reference game speed.")]
        public float BaseCycleSeconds = 180f;
        [Tooltip("Ticks per second at which the cycle takes exactly BaseCycleSeconds.")]
        public float ReferenceTicksPerSecond = 4f;
        public float MinSpeedFactor = 0.6f;
        public float MaxSpeedFactor = 3f;
        [Tooltip("Cycle speed factor while paused (0 = frozen).")]
        public float PausedSpeedFactor = 0f;
        [Range(0f, 1f)] public float StartPhase = 0.33f;

        [Header("Sun & moon")]
        [Range(10f, 89f)] public float MaxSunElevation = 62f;
        [Tooltip("Compass heading (degrees) of the noon sun.")]
        public float Azimuth = 30f;
        public float SunIntensity = 1.15f;
        public float MoonIntensity = 0.2f;
        public Color MoonColor = new Color(0.55f, 0.65f, 1f, 1f);
        [Tooltip("Kelvin at sunrise/sunset and at noon.")]
        public float HorizonKelvin = 2300f;
        public float NoonKelvin = 6300f;

        /// <summary>0 = midnight, 0.25 = sunrise, 0.5 = noon, 0.75 = sunset.</summary>
        public float DayPhase { get; private set; }

        /// <summary>0 at night .. 1 in full daylight.</summary>
        public float Daylight { get; private set; }

        public bool IsNight => Daylight < 0.15f;

        private float _nextSearch;
        private float _firstSearch = -1f;
        private bool _createdSun;

        /// <summary>Gentle game-speed scaling: sqrt(tps / reference), clamped.</summary>
        public static float SpeedFactorFor(float ticksPerSecond, float referenceTps, float min, float max)
        {
            if (!(ticksPerSecond > 0f)) return 0f;
            if (!(referenceTps > 0f)) referenceTps = 1f;
            return Mathf.Clamp(Mathf.Sqrt(ticksPerSecond / referenceTps), min, max);
        }

        private void Awake()
        {
            DayPhase = Mathf.Repeat(StartPhase, 1f);
        }

        private void OnEnable()
        {
            if (Instance != null && Instance != this) Debug.LogWarning("Multiple DayNightCycle instances; the last enabled one is DayNightCycle.Instance.", this);
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void OnDestroy()
        {
            if (_createdSun && Sun != null) Destroy(Sun.gameObject);
        }

        private void Update()
        {
            GodSimRunner runner = GodSimRunner.Instance;
            float factor;
            if (runner == null) factor = 1f;
            else if (runner.Paused || runner.Simulation == null) factor = PausedSpeedFactor;
            else factor = SpeedFactorFor(runner.TicksPerSecond, ReferenceTicksPerSecond, MinSpeedFactor, MaxSpeedFactor);

            float cycle = Mathf.Max(5f, BaseCycleSeconds);
            DayPhase = Mathf.Repeat(DayPhase + Time.deltaTime * factor / cycle, 1f);

            float sunH = Mathf.Sin((DayPhase - 0.25f) * 6.28318530718f); // -1..1, 0 at the horizon
            Daylight = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((sunH + 0.08f) / 0.33f));

            if (Sun == null) FindSun();
            if (Sun != null) ApplyLight(sunH);
        }

        private void ApplyLight(float sunH)
        {
            float dim = 0f;
            WeatherVFX weather = WeatherVFX.Instance;
            if (weather != null && weather.isActiveAndEnabled) dim = weather.SunDimming;

            float elevation, yaw, intensity;
            Color color;
            if (sunH >= 0f)
            {
                float up = Mathf.Clamp01(sunH);
                elevation = Mathf.Lerp(3f, MaxSunElevation, up);
                yaw = Azimuth + (DayPhase - 0.5f) * 220f;
                float kelvin = Mathf.Lerp(HorizonKelvin, NoonKelvin, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(up / 0.6f)));
                color = KelvinToColor(kelvin);
                intensity = SunIntensity * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(up / 0.18f));
            }
            else
            {
                float down = Mathf.Clamp01(-sunH);
                elevation = Mathf.Lerp(12f, 48f, down);
                float nightPhase = DayPhase < 0.5f ? DayPhase + 1f : DayPhase; // 0.75 .. 1.25 continuous through midnight
                yaw = Azimuth + 180f + (nightPhase - 1f) * 200f;
                color = MoonColor;
                intensity = MoonIntensity * Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((down - 0.04f) / 0.2f));
            }

            intensity *= 1f - 0.7f * Mathf.Clamp01(dim);
            Transform t = Sun.transform;
            t.rotation = Quaternion.Euler(elevation, yaw, 0f);
            Sun.color = color;
            Sun.intensity = Mathf.Max(0f, intensity);
        }

        private void FindSun()
        {
            float now = Time.unscaledTime;
            if (now < _nextSearch) return;
            _nextSearch = now + 2f;
            if (_firstSearch < 0f) _firstSearch = now;

            Light s = RenderSettings.sun;
            if (s != null && s.type == LightType.Directional) { Sun = s; return; }
            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null && lights[i].type == LightType.Directional) { Sun = lights[i]; return; }
            }
            if (CreateSunIfMissing && now - _firstSearch >= 1f)
            {
                var go = new GameObject("GodSim Sun");
                Light l = go.AddComponent<Light>();
                l.type = LightType.Directional;
                l.shadows = LightShadows.Soft;
                Sun = l;
                _createdSun = true;
                if (RenderSettings.sun == null) RenderSettings.sun = l;
            }
            else if (CreateSunIfMissing)
            {
                _nextSearch = _firstSearch + 1f;
            }
        }

        /// <summary>Approximate blackbody color (Tanner Helland fit), 1000..40000 K.</summary>
        public static Color KelvinToColor(float kelvin)
        {
            float t = Mathf.Clamp(kelvin, 1000f, 40000f) / 100f;
            float r, g, b;
            if (t <= 66f) r = 255f;
            else r = 329.698727446f * Mathf.Pow(t - 60f, -0.1332047592f);
            if (t <= 66f) g = 99.4708025861f * Mathf.Log(t) - 161.1195681661f;
            else g = 288.1221695283f * Mathf.Pow(t - 60f, -0.0755148492f);
            if (t >= 66f) b = 255f;
            else if (t <= 19f) b = 0f;
            else b = 138.5177312231f * Mathf.Log(t - 10f) - 305.0447927307f;
            return new Color(Mathf.Clamp01(r / 255f), Mathf.Clamp01(g / 255f), Mathf.Clamp01(b / 255f), 1f);
        }
    }
}
