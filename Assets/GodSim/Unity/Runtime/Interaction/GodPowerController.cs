using System;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// God-power targeting and casting.
    /// <list type="bullet">
    /// <item>Hotkeys: 1-9, 0 select the first ten powers (enum order); Shift+1-9 the rest. Pressing the hotkey
    /// of the selected power deselects it. Ignored while Ctrl or Alt is held.</item>
    /// <item>Radius: [ and ] or Ctrl+wheel, clamped to the power's MinRadius..MaxRadius.</item>
    /// <item>Esc or a right click (press + release without dragging, so right-drag still rotates the camera) cancels.</item>
    /// <item>Left click casts through <c>GodSimulation.TryCastPower</c>; holding the button repeats terraform
    /// powers (RaiseLand, LowerLand, PlantForest) at most every 0.15 s until a cast fails or the button is released.</item>
    /// <item>A terrain-following circle previews the radius, colored by category (blessing gold, shaping green,
    /// creation cyan, wrath red) and grey while the power is unaffordable or cooling down.</item>
    /// </list>
    /// Mouse input is ignored while the pointer is over the HUD.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-50)]
    [AddComponentMenu("GodSim/God Power Controller")]
    public sealed class GodPowerController : MonoBehaviour
    {
        /// <summary>The active controller (last enabled), or null.</summary>
        public static GodPowerController Instance { get; private set; }

        private const float RepeatInterval = 0.15f;
        private const int CircleSegments = 64;
        private const int AbsoluteMaxRadius = 64;
        private const int GreyState = 4;

        private static readonly KeyCode[] DigitKeys =
        {
            KeyCode.Alpha1, KeyCode.Alpha2, KeyCode.Alpha3, KeyCode.Alpha4, KeyCode.Alpha5,
            KeyCode.Alpha6, KeyCode.Alpha7, KeyCode.Alpha8, KeyCode.Alpha9, KeyCode.Alpha0
        };

        [SerializeField] private Color _blessingColor = new Color(1f, 0.82f, 0.25f, 1f);
        [SerializeField] private Color _shapingColor = new Color(0.35f, 0.9f, 0.4f, 1f);
        [SerializeField] private Color _creationColor = new Color(0.3f, 0.9f, 1f, 1f);
        [SerializeField] private Color _wrathColor = new Color(1f, 0.25f, 0.2f, 1f);
        [SerializeField] private Color _unavailableColor = new Color(0.55f, 0.55f, 0.55f, 1f);
        [SerializeField] private float _lineWidth = 0.12f;
        [SerializeField] private float _lift = 0.15f;
        [Tooltip("A right click that moves more than this many pixels is a camera drag, not a cancel.")]
        [SerializeField] private float _rightClickMaxPixels = 6f;

        private bool _hasPower;
        private GodPowerType _power;
        private int _radius = 2;
        private RaceId _spawnRace = RaceId.Human;

        private bool _leftHeld;
        private bool _repeatBlocked;
        private float _lastCastTime = -1000f;
        private bool _rightPending;
        private Vector2 _rightDownPos;
        private float _wheelAccum;
        private GodSimulation _lastSim;

        private LineRenderer _circle;
        private readonly Material[] _materials = new Material[5];
        private readonly Color[] _colors = new Color[5];
        private int _colorState = -1;
        private float _lastWidthScale = -1f;
        private float[] _cos;
        private float[] _sin;
        private bool _previewValid;
        private Vec2i _previewTile;
        private int _previewRadius;
        private int _previewVersion;
        private WorldMap _previewMap;

        /// <summary>Frame on which targeting last owned the mouse (selected, cast or cancelled).</summary>
        internal int LastActiveFrame = -10;

        /// <summary>Raised after a successful cast with the power and target tile.</summary>
        public event Action<GodPowerType, Vec2i> CastSucceeded;
        /// <summary>Raised with a human-readable reason when a cast is rejected.</summary>
        public event Action<string> CastFailed;

        /// <summary>True while a power is selected for targeting.</summary>
        public bool HasPower => _hasPower;
        /// <summary>The selected power (meaningful only while <see cref="HasPower"/>).</summary>
        public GodPowerType SelectedPower => _power;

        /// <summary>Cast radius in tiles; clamped to the selected power's MinRadius..MaxRadius.</summary>
        public int Radius
        {
            get { return _radius; }
            set { _radius = ClampRadius(_power, _hasPower, value, InteractionUtil.Simulation); }
        }

        /// <summary>Race spawned by <see cref="GodPowerType.SpawnTribe"/>. Invalid values are ignored.</summary>
        public RaceId SpawnRace
        {
            get { return _spawnRace; }
            set { if ((int)value >= 0 && (int)value < Counts.Races) _spawnRace = value; }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        private void Awake()
        {
            _cos = new float[CircleSegments];
            _sin = new float[CircleSegments];
            for (int i = 0; i < CircleSegments; i++)
            {
                float a = i * (2f * Mathf.PI / CircleSegments);
                _cos[i] = Mathf.Cos(a);
                _sin[i] = Mathf.Sin(a);
            }
            _colors[(int)GodPowerCategory.Blessing] = _blessingColor;
            _colors[(int)GodPowerCategory.Shaping] = _shapingColor;
            _colors[(int)GodPowerCategory.Creation] = _creationColor;
            _colors[(int)GodPowerCategory.Wrath] = _wrathColor;
            _colors[GreyState] = _unavailableColor;
            _circle = InteractionUtil.CreateLine(transform, "PowerTargetCircle", MaterialFor(GreyState), _unavailableColor, _lineWidth);
            _circle.positionCount = CircleSegments;
        }

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("GodPowerController: more than one active instance; the latest enabled one is used.");
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            _leftHeld = _rightPending = false;
            if (_circle != null) _circle.enabled = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Selects a power for targeting (the radius is clamped to its range).</summary>
        public void SelectPower(GodPowerType p)
        {
            if ((int)p < 0 || (int)p >= Counts.GodPowers) return;
            _power = p;
            _hasPower = true;
            _leftHeld = false;
            _rightPending = false;
            _wheelAccum = 0f;
            _previewValid = false;
            _colorState = -1;
            LastActiveFrame = Time.frameCount;
            _radius = ClampRadius(p, true, _radius, InteractionUtil.Simulation);
        }

        /// <summary>Leaves targeting mode.</summary>
        public void ClearPower()
        {
            if (!_hasPower) return;
            _hasPower = false;
            _leftHeld = false;
            _rightPending = false;
            LastActiveFrame = Time.frameCount;
            if (_circle != null) _circle.enabled = false;
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            GodSimulation sim = InteractionUtil.Simulation;
            if (sim != _lastSim)
            {
                _lastSim = sim;
                _previewValid = false;
                _colorState = -1;
                if (_hasPower) _radius = ClampRadius(_power, true, _radius, sim);
            }

            HandleHotkeys();
            if (_hasPower)
            {
                LastActiveFrame = Time.frameCount;
                HandleRadius(sim);
                HandleCancel();
            }
            if (_hasPower) HandleCasting(sim);
            UpdatePreview(sim);
        }

        private void HandleHotkeys()
        {
            if (InteractionUtil.CtrlHeld || InteractionUtil.AltHeld) return;
            bool shift = InteractionUtil.ShiftHeld;
            for (int i = 0; i < DigitKeys.Length; i++)
            {
                if (!InputAdapter.GetKeyDown(DigitKeys[i])) continue;
                int index = shift ? (i < 9 ? 10 + i : -1) : i;
                if (index < 0 || index >= EnumUtil.AllGodPowers.Length) return;
                GodPowerType p = EnumUtil.AllGodPowers[index];
                if (_hasPower && _power == p) ClearPower();
                else SelectPower(p);
                return;
            }
        }

        private void HandleRadius(GodSimulation sim)
        {
            int delta = 0;
            if (InputAdapter.GetKeyDown(KeyCode.LeftBracket)) delta--;
            if (InputAdapter.GetKeyDown(KeyCode.RightBracket)) delta++;
            if (InteractionUtil.CtrlHeld && !InputAdapter.PointerOverUI)
            {
                _wheelAccum = Mathf.Clamp(_wheelAccum + InputAdapter.ScrollDelta, -10f, 10f);
                while (_wheelAccum >= 1f) { delta++; _wheelAccum -= 1f; }
                while (_wheelAccum <= -1f) { delta--; _wheelAccum += 1f; }
            }
            else _wheelAccum = 0f;
            if (delta != 0) _radius = ClampRadius(_power, true, _radius + delta, sim);
        }

        private void HandleCancel()
        {
            if (InputAdapter.GetKeyDown(KeyCode.Escape)) { ClearPower(); return; }
            Vector2 mouse = InputAdapter.MousePosition;
            if (InputAdapter.MouseDown(1) && !InputAdapter.PointerOverUI && InteractionUtil.PointerInScreen(mouse))
            {
                _rightPending = true;
                _rightDownPos = mouse;
            }
            if (!_rightPending) return;
            float max = _rightClickMaxPixels;
            if ((mouse - _rightDownPos).sqrMagnitude > max * max) _rightPending = false; // became a camera drag
            else if (InputAdapter.MouseUp(1) || !InputAdapter.MouseHeld(1))
            {
                _rightPending = false;
                ClearPower();
            }
        }

        private void HandleCasting(GodSimulation sim)
        {
            Vec2i tile;
            Vector3 point;
            if (InputAdapter.MouseDown(0))
            {
                _leftHeld = false;
                if (InputAdapter.PointerOverUI || !InteractionUtil.PointerInScreen(InputAdapter.MousePosition)) return;
                _leftHeld = true;
                _repeatBlocked = false;
                if (InteractionUtil.TryGetPointerTile(out tile, out point)) Cast(sim, tile);
                return;
            }
            if (!_leftHeld) return;
            if (!InputAdapter.MouseHeld(0)) { _leftHeld = false; return; }
            if (_repeatBlocked || !IsTerraform(_power) || InputAdapter.PointerOverUI) return;
            if (Time.unscaledTime - _lastCastTime < RepeatInterval) return;
            if (InteractionUtil.TryGetPointerTile(out tile, out point)) Cast(sim, tile);
        }

        private void Cast(GodSimulation sim, Vec2i tile)
        {
            GodPowerType p = _power;
            _lastCastTime = Time.unscaledTime;
            LastActiveFrame = Time.frameCount;
            if (sim == null)
            {
                _repeatBlocked = true;
                CastFailed?.Invoke("The world is not ready yet.");
                return;
            }
            _radius = ClampRadius(p, true, _radius, sim);
            int parameter = p == GodPowerType.SpawnTribe ? (int)_spawnRace : 0;
            string error = null;
            bool ok;
            try
            {
                ok = sim.TryCastPower(p, tile, _radius, parameter, out error);
            }
            catch (Exception e)
            {
                ok = false;
                error = e.Message;
                Debug.LogException(e);
            }
            if (ok)
            {
                _previewValid = false; // terrain / mana may have changed
                CastSucceeded?.Invoke(p, tile);
            }
            else
            {
                _repeatBlocked = true;
                CastFailed?.Invoke(string.IsNullOrEmpty(error) ? "The power could not be cast." : error);
            }
        }

        // ------------------------------------------------------------------ preview

        private void UpdatePreview(GodSimulation sim)
        {
            if (_circle == null) return;
            WorldState state = sim != null ? sim.State : null;
            WorldMap map = state != null ? state.Map : null;
            Vec2i tile;
            Vector3 point;
            if (!_hasPower || map == null || !InteractionUtil.TryGetPointerTile(out tile, out point))
            {
                if (_circle.enabled) _circle.enabled = false;
                return;
            }

            if (!_previewValid || tile != _previewTile || _radius != _previewRadius || map != _previewMap || map.TerrainVersion != _previewVersion)
            {
                _previewValid = true;
                _previewTile = tile;
                _previewRadius = _radius;
                _previewMap = map;
                _previewVersion = map.TerrainVersion;
                RebuildCircle(map, tile, _radius);
            }

            int colorState = ColorStateFor(sim, state);
            if (colorState != _colorState)
            {
                _colorState = colorState;
                Material m = MaterialFor(colorState);
                if (m != null) _circle.sharedMaterial = m;
                Color c = _colors[colorState];
                _circle.startColor = c;
                _circle.endColor = c;
            }

            float scale = InteractionUtil.ZoomWidthScale();
            if (Mathf.Abs(scale - _lastWidthScale) > 0.01f)
            {
                _lastWidthScale = scale;
                _circle.widthMultiplier = _lineWidth * scale;
            }
            if (!_circle.enabled) _circle.enabled = true;
        }

        private void RebuildCircle(WorldMap map, Vec2i tile, int radius)
        {
            Vector3 c = GodSimCoords.TileToWorld(map, tile);
            float r = (radius + 0.5f) * GodSimCoords.TileSize;
            if (_circle.positionCount != CircleSegments) _circle.positionCount = CircleSegments;
            for (int i = 0; i < CircleSegments; i++)
            {
                float x = c.x + _cos[i] * r;
                float z = c.z + _sin[i] * r;
                float y = InteractionUtil.SurfaceAt(map, x, z) + _lift;
                _circle.SetPosition(i, new Vector3(x, y, z));
            }
        }

        private int ColorStateFor(GodSimulation sim, WorldState state)
        {
            GodPowerDefinition def = Definition(sim, _power);
            if (def == null || state == null) return GreyState;
            int p = (int)_power;
            GodState god = state.God;
            if (god == null) return GreyState;
            if (god.Mana < def.ManaCost) return GreyState;
            if (p < god.CooldownUntil.Length && state.Tick < god.CooldownUntil[p]) return GreyState;
            int cat = (int)def.Category;
            return cat >= 0 && cat < GreyState ? cat : GreyState;
        }

        private Material MaterialFor(int colorState)
        {
            Material m = _materials[colorState];
            if (m == null)
            {
                m = MaterialLibrary.Unlit(_colors[colorState]);
                _materials[colorState] = m;
            }
            return m;
        }

        // ------------------------------------------------------------------ helpers

        private static GodPowerDefinition Definition(GodSimulation sim, GodPowerType p)
        {
            GameDatabase db = sim != null ? sim.Db : null;
            if (db == null) return null;
            var powers = db.GodPowers;
            int i = (int)p;
            return powers != null && i >= 0 && i < powers.Count ? powers[i] : null;
        }

        private static int ClampRadius(GodPowerType p, bool hasPower, int value, GodSimulation sim)
        {
            int min = 0, max = AbsoluteMaxRadius;
            if (hasPower)
            {
                GodPowerDefinition def = Definition(sim, p);
                if (def != null)
                {
                    min = Mathf.Max(0, def.MinRadius);
                    max = Mathf.Clamp(def.MaxRadius, min, AbsoluteMaxRadius);
                }
            }
            return Mathf.Clamp(value, min, max);
        }

        private static bool IsTerraform(GodPowerType p)
        {
            return p == GodPowerType.RaiseLand || p == GodPowerType.LowerLand || p == GodPowerType.PlantForest;
        }
    }
}
