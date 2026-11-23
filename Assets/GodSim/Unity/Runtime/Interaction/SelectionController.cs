using System;
using GodSim.Core;
using GodSim.Core.Composition;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Click selection of world objects. A left click (when no god power is selected and the pointer is not
    /// over the HUD) picks the tile under the cursor, then resolves, in priority order: the building on that
    /// tile (<c>map.BuildingAt</c>; its settlement becomes the selected settlement), else the nearest living
    /// army within 1.5 tiles (SelectedTile then tracks the army; no settlement), else the settlement owning
    /// the tile (<c>map.Owner</c>, -1 when unclaimed). Clicking off the map
    /// or pressing Esc clears the selection. Hover and selection are outlined on the terrain with
    /// LineRenderer squares (building footprints are outlined whole). Selected ids are re-validated every
    /// frame (destroyed / dead entities are dropped) and a new simulation clears the selection.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(-40)]
    [AddComponentMenu("GodSim/Selection Controller")]
    public sealed class SelectionController : MonoBehaviour
    {
        /// <summary>The active controller (last enabled), or null.</summary>
        public static SelectionController Instance { get; private set; }

        private const float ArmyPickRadius = 1.5f;

        [SerializeField] private Color _hoverColor = new Color(1f, 1f, 1f, 1f);
        [SerializeField] private Color _selectionColor = new Color(1f, 0.82f, 0.2f, 1f);
        [SerializeField] private float _hoverWidth = 0.06f;
        [SerializeField] private float _selectionWidth = 0.11f;
        [SerializeField] private float _lift = 0.08f;

        private bool _hasSelection;
        private Vec2i _tile = Vec2i.Invalid;
        private int _settlementId = -1;
        private int _buildingId = -1;
        private int _armyId = -1;

        private bool _hasHover;
        private Vec2i _hoverTile = Vec2i.Invalid;

        private GodSimulation _lastSim;

        private LineRenderer _hoverLine;
        private LineRenderer _selectionLine;
        private RectCache _hoverRect;
        private RectCache _selectionRect;
        private float _lastWidthScale = -1f;

        /// <summary>Raised whenever the selected tile / building / army / settlement changes.</summary>
        public event Action SelectionChanged;

        /// <summary>True when something (at least a tile) is selected.</summary>
        public bool HasSelection => _hasSelection;
        /// <summary>The selected tile (for an army: its current position). Invalid when nothing is selected.</summary>
        public Vec2i SelectedTile => _tile;
        /// <summary>Selected settlement id, or -1.</summary>
        public int SelectedSettlementId => _settlementId;
        /// <summary>Selected building id, or -1.</summary>
        public int SelectedBuildingId => _buildingId;
        /// <summary>Selected army id, or -1.</summary>
        public int SelectedArmyId => _armyId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() { Instance = null; }

        private void Awake()
        {
            _hoverLine = InteractionUtil.CreateLine(transform, "HoverOutline", MaterialLibrary.Unlit(_hoverColor), _hoverColor, _hoverWidth);
            _selectionLine = InteractionUtil.CreateLine(transform, "SelectionOutline", MaterialLibrary.Unlit(_selectionColor), _selectionColor, _selectionWidth);
            _hoverRect.Invalidate();
            _selectionRect.Invalidate();
        }

        private void OnEnable()
        {
            if (Instance != null && Instance != this)
                Debug.LogWarning("SelectionController: more than one active instance; the latest enabled one is used.");
            Instance = this;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
            _hasHover = false;
            if (_hoverLine != null) _hoverLine.enabled = false;
            if (_selectionLine != null) _selectionLine.enabled = false;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // ------------------------------------------------------------------ public API

        /// <summary>Selects a settlement (and its center tile). Ignored for unknown / dead settlements.</summary>
        public void SelectSettlement(int id)
        {
            WorldState state = InteractionUtil.State;
            if (state == null) return;
            Settlement s = state.Settlements.Get(id);
            if (s == null || !s.IsAlive) return;
            Set(state.Map.ClampToBounds(s.Center), id, -1, -1);
        }

        /// <summary>Selects a tile and whatever stands on it (building, army, owning settlement).</summary>
        public void SelectTile(Vec2i t)
        {
            WorldState state = InteractionUtil.State;
            if (state == null || state.Map == null || !state.Map.InBounds(t)) return;
            SelectAt(state, t);
        }

        /// <summary>Clears the selection (raises <see cref="SelectionChanged"/> if something was selected).</summary>
        public void Clear()
        {
            if (!_hasSelection && _settlementId < 0 && _buildingId < 0 && _armyId < 0) return;
            _hasSelection = false;
            _tile = Vec2i.Invalid;
            _settlementId = _buildingId = _armyId = -1;
            _selectionRect.Invalidate();
            SelectionChanged?.Invoke();
        }

        /// <summary>The tile under the pointer this frame (false over the HUD, off the map, or with no world).</summary>
        public bool TryGetHoveredTile(out Vec2i tile)
        {
            tile = _hasHover ? _hoverTile : Vec2i.Invalid;
            return _hasHover;
        }

        // ------------------------------------------------------------------ frame

        private void Update()
        {
            GodSimulation sim = InteractionUtil.Simulation;
            if (sim != _lastSim)
            {
                _lastSim = sim;
                Clear();
            }
            WorldState state = sim != null ? sim.State : null;
            WorldMap map = state != null ? state.Map : null;
            if (map == null)
            {
                _hasHover = false;
                HideLines();
                return;
            }

            Vector3 point;
            _hasHover = InteractionUtil.TryGetPointerTile(out _hoverTile, out point);
            if (!_hasHover) _hoverTile = Vec2i.Invalid;

            bool powerActive = InteractionUtil.PowerTargetingActive;
            if (!powerActive)
            {
                if (InputAdapter.MouseDown(0) && !InputAdapter.PointerOverUI && InteractionUtil.PointerInScreen(InputAdapter.MousePosition))
                {
                    if (_hasHover) SelectAt(state, _hoverTile);
                    else Clear();
                }
                else if (InputAdapter.GetKeyDown(KeyCode.Escape))
                {
                    Clear();
                }
            }

            Revalidate(state);
            UpdateLines(state, map, powerActive);
        }

        private void SelectAt(WorldState state, Vec2i t)
        {
            WorldMap map = state.Map;
            int idx = map.Index(t);

            int bId = map.BuildingAt[idx];
            if (bId >= 0)
            {
                Building b = state.Buildings.Get(bId);
                if (b != null && !b.IsDestroyed)
                {
                    Set(t, AliveSettlement(state, b.SettlementId), bId, -1);
                    return;
                }
            }

            Army army = NearestArmy(state, t);
            if (army != null)
            {
                Set(army.Position, -1, -1, army.Id);
                return;
            }

            Set(t, AliveSettlement(state, map.Owner[idx]), -1, -1);
        }

        private void Set(Vec2i tile, int settlementId, int buildingId, int armyId)
        {
            if (_hasSelection && _tile == tile && _settlementId == settlementId && _buildingId == buildingId && _armyId == armyId) return;
            _hasSelection = true;
            _tile = tile;
            _settlementId = settlementId;
            _buildingId = buildingId;
            _armyId = armyId;
            _selectionRect.Invalidate();
            SelectionChanged?.Invoke();
        }

        /// <summary>Drops references to entities that died / were destroyed; tracks the selected army.</summary>
        private void Revalidate(WorldState state)
        {
            if (!_hasSelection) return;
            bool changed = false;
            if (_buildingId >= 0)
            {
                Building b = state.Buildings.Get(_buildingId);
                if (b == null || b.IsDestroyed) { _buildingId = -1; changed = true; }
            }
            if (_armyId >= 0)
            {
                Army a = state.Diplomacy.Armies.Get(_armyId);
                if (a == null || !a.IsAlive) { _armyId = -1; changed = true; }
                else if (a.Position != _tile && state.Map.InBounds(a.Position)) _tile = a.Position; // follows silently
            }
            if (_settlementId >= 0)
            {
                Settlement s = state.Settlements.Get(_settlementId);
                if (s == null || !s.IsAlive) { _settlementId = -1; changed = true; }
            }
            if (changed)
            {
                _selectionRect.Invalidate();
                SelectionChanged?.Invoke();
            }
        }

        private static Army NearestArmy(WorldState state, Vec2i t)
        {
            var armies = state.Diplomacy.Armies.Items;
            Army best = null;
            int bestD2 = int.MaxValue;
            const float maxD2 = ArmyPickRadius * ArmyPickRadius;
            for (int i = 0; i < armies.Count; i++)
            {
                Army a = armies[i];
                if (a == null || !a.IsAlive) continue;
                int d2 = a.Position.DistanceSqTo(t);
                if (d2 > maxD2) continue;
                if (d2 < bestD2 || (d2 == bestD2 && best != null && a.Id < best.Id))
                {
                    best = a;
                    bestD2 = d2;
                }
            }
            return best;
        }

        private static int AliveSettlement(WorldState state, int id)
        {
            if (id < 0) return -1;
            Settlement s = state.Settlements.Get(id);
            return s != null && s.IsAlive ? id : -1;
        }

        // ------------------------------------------------------------------ outlines

        private void UpdateLines(WorldState state, WorldMap map, bool powerActive)
        {
            float scale = InteractionUtil.ZoomWidthScale();
            if (Mathf.Abs(scale - _lastWidthScale) > 0.01f)
            {
                _lastWidthScale = scale;
                if (_hoverLine != null) _hoverLine.widthMultiplier = _hoverWidth * scale;
                if (_selectionLine != null) _selectionLine.widthMultiplier = _selectionWidth * scale;
            }

            // Hover (hidden while a god power shows its own targeting preview).
            if (_hoverLine != null)
            {
                bool show = _hasHover && !powerActive;
                if (show)
                {
                    int x0, y0, w, h;
                    RectFor(state, _hoverTile, map.BuildingAt[map.Index(_hoverTile)], out x0, out y0, out w, out h);
                    if (_hoverRect.Changed(map, x0, y0, w, h)) InteractionUtil.DrawTileRect(_hoverLine, map, x0, y0, w, h, _lift);
                }
                if (_hoverLine.enabled != show) _hoverLine.enabled = show;
            }

            if (_selectionLine != null)
            {
                bool show = _hasSelection && map.InBounds(_tile);
                if (show)
                {
                    int bId = _buildingId;
                    if (bId < 0 && _armyId < 0 && _settlementId >= 0 && map.BuildingAt[map.Index(_tile)] < 0)
                    {
                        // Settlement-only selection: outline the town center when it exists.
                        Settlement s = state.Settlements.Get(_settlementId);
                        if (s != null && s.TownCenterId >= 0 && s.Center == _tile) bId = s.TownCenterId;
                    }
                    int x0, y0, w, h;
                    RectFor(state, _tile, bId, out x0, out y0, out w, out h);
                    if (_selectionRect.Changed(map, x0, y0, w, h)) InteractionUtil.DrawTileRect(_selectionLine, map, x0, y0, w, h, _lift * 1.5f);
                }
                if (_selectionLine.enabled != show) _selectionLine.enabled = show;
            }
        }

        private static void RectFor(WorldState state, Vec2i tile, int buildingId, out int x0, out int y0, out int w, out int h)
        {
            x0 = tile.X; y0 = tile.Y; w = 1; h = 1;
            if (buildingId < 0) return;
            Building b = state.Buildings.Get(buildingId);
            if (b == null || b.IsDestroyed) return;
            x0 = b.Origin.X; y0 = b.Origin.Y;
            w = b.Width < 1 ? 1 : b.Width;
            h = b.Height < 1 ? 1 : b.Height;
        }

        private void HideLines()
        {
            if (_hoverLine != null && _hoverLine.enabled) _hoverLine.enabled = false;
            if (_selectionLine != null && _selectionLine.enabled) _selectionLine.enabled = false;
        }

        /// <summary>Remembers what an outline currently shows so it is rebuilt only on change.</summary>
        private struct RectCache
        {
            private bool _valid;
            private WorldMap _map;
            private int _version, _x, _y, _w, _h;

            public void Invalidate() { _valid = false; _map = null; }

            public bool Changed(WorldMap map, int x, int y, int w, int h)
            {
                if (_valid && _map == map && _version == map.TerrainVersion && _x == x && _y == y && _w == w && _h == h) return false;
                _valid = true; _map = map; _version = map.TerrainVersion; _x = x; _y = y; _w = w; _h = h;
                return true;
            }
        }
    }
}
