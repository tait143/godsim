using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Composition;
using GodSim.Unity.Effects;
using GodSim.Unity.Units;
using UnityEngine;
using UnityEngine.Rendering;

namespace GodSim.Unity
{
    /// <summary>
    /// Draws every alive <see cref="Army"/> as a formation of GPU-instanced soldiers
    /// (min(SoldierIds.Count, <see cref="MaxSoldiersPerArmy"/>)) around <see cref="Army.Position"/>, smoothly
    /// interpolated between ticks, plus a banner pole tinted with the civilization color. Besieging armies
    /// jitter in place, retreating armies loosen their ranks, and each <see cref="BattleEvent"/> puffs dust
    /// and clash sparks through the shared <see cref="VfxPool"/>.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ArmyRenderer : SimulationBoundBehaviour
    {
        public static ArmyRenderer Instance { get; private set; }

        [Header("Formation")]
        [Range(1, 40)] public int MaxSoldiersPerArmy = 40;
        [Tooltip("Soldier height in tiles.")]
        public float SoldierHeight = 0.32f;
        [Tooltip("Spacing between soldiers in tiles.")]
        public float Spacing = 0.3f;
        [Tooltip("Banner height in tiles.")]
        public float BannerHeight = 0.9f;
        [Tooltip("Amplitude (tiles) of the besieging jitter.")]
        public float SiegeJitter = 0.05f;
        [Tooltip("Jumps longer than this many tiles snap instead of interpolating.")]
        public float SnapDistance = 8f;
        public bool CastShadows = true;

        [Header("Battles")]
        public bool BattlePuffs = true;
        public float BattlePuffScale = 1f;

        private const int MaxArmiesDrawn = 512;

        private sealed class ArmyVisual
        {
            public int Id;
            public int ToTileX, ToTileY;
            public float FromX, FromY, ToX, ToY, CurX, CurY;
            public float StartTime, Duration;
            public float Yaw;
            public int SeenFrame;
        }

        private readonly Dictionary<int, ArmyVisual> _visuals = new Dictionary<int, ArmyVisual>();
        private readonly Stack<ArmyVisual> _visualPool = new Stack<ArmyVisual>();
        private readonly List<int> _stale = new List<int>();
        private InstanceBuffer _soldiers;
        private InstanceBuffer _banners;
        private Mesh _soldierMesh;
        private Mesh _bannerMesh;
        private MapFrame _frame;
        private Action<BattleEvent> _onBattle;
        private readonly BattleEvent[] _battles = new BattleEvent[32];
        private int _battleHead, _battleCount;
        private GodSimulation _subscribed;

        /// <summary>Soldier instances drawn last frame.</summary>
        public int DrawnSoldiers { get; private set; }

        private void Awake()
        {
            _soldiers = new InstanceBuffer(MaxArmiesDrawn * 40);
            _banners = new InstanceBuffer(MaxArmiesDrawn);
            _onBattle = OnBattle;
        }

        protected override void OnEnable()
        {
            if (Instance != null && Instance != this) Debug.LogWarning("Multiple ArmyRenderer instances; the last enabled one is ArmyRenderer.Instance.", this);
            Instance = this;
            base.OnEnable();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Instance == this) Instance = null;
        }

        private void OnDestroy()
        {
            if (_soldierMesh != null) Destroy(_soldierMesh);
            if (_bannerMesh != null) Destroy(_bannerMesh);
        }

        protected override void OnSimulationBound(GodSimulation sim)
        {
            _frame = MapFrame.From(sim.State.Map);
            ClearVisuals();
            if (sim.Events != null && _onBattle != null)
            {
                sim.Events.Subscribe(_onBattle);
                _subscribed = sim;
            }
        }

        protected override void OnSimulationUnbound(GodSimulation old)
        {
            if (_subscribed != null && _subscribed.Events != null && _onBattle != null) _subscribed.Events.Unsubscribe(_onBattle);
            _subscribed = null;
            ClearVisuals();
        }

        private void ClearVisuals()
        {
            foreach (KeyValuePair<int, ArmyVisual> kv in _visuals) _visualPool.Push(kv.Value);
            _visuals.Clear();
            _battleHead = 0;
            _battleCount = 0;
            DrawnSoldiers = 0;
        }

        private void OnBattle(BattleEvent e)
        {
            if (!BattlePuffs) return;
            if (_battleCount >= _battles.Length) { _battleHead = (_battleHead + 1) % _battles.Length; _battleCount--; }
            _battles[(_battleHead + _battleCount) % _battles.Length] = e;
            _battleCount++;
        }

        private void LateUpdate()
        {
            if (!EnsureBound()) { DrawnSoldiers = 0; return; }
            WorldState state = Sim.State;
            if (!ReferenceEquals(state.Map, _frame.Map)) _frame = MapFrame.From(state.Map);
            if (_soldierMesh == null) _soldierMesh = UnitMeshes.CreateSoldier();
            if (_bannerMesh == null) _bannerMesh = UnitMeshes.CreateBanner();

            ProcessBattles(state.Map);
            DrawArmies(state);
        }

        private void ProcessBattles(WorldMap map)
        {
            int budget = 4;
            while (_battleCount > 0 && budget-- > 0)
            {
                BattleEvent e = _battles[_battleHead];
                _battleHead = (_battleHead + 1) % _battles.Length;
                _battleCount--;
                if (!map.InBounds(e.Position)) continue;
                VfxPool pool = VfxPool.GetOrCreate();
                if (pool == null) continue;
                float losses = Mathf.Max(0f, e.AttackerLosses) + Mathf.Max(0f, e.DefenderLosses);
                float scale = Mathf.Clamp(1f + Mathf.Log(1f + losses) * 0.35f, 1f, 3f) * BattlePuffScale * _frame.TileSize;
                Vector3 pos = GodSimCoords.TileToWorld(map, e.Position);
                pool.Burst(VfxKind.Dust, pos, scale * 1.4f);
                pool.Burst(VfxKind.Clash, pos + new Vector3(0f, 0.25f * _frame.TileSize, 0f), scale);
            }
        }

        private void DrawArmies(WorldState state)
        {
            WorldMap map = state.Map;
            float now = Time.time;
            float dt = Time.deltaTime;
            int frameNo = Time.frameCount;
            GodSimRunner runner = Runner;
            float tps = runner != null ? runner.TicksPerSecond : 0f;
            float tickDuration = tps > 0.01f ? Mathf.Clamp(1f / tps, 0.04f, 1f) : 0.5f;
            float tile = _frame.TileSize;
            float soldierScale = Mathf.Max(0.01f, SoldierHeight) * tile;
            float bannerScale = Mathf.Max(0.01f, BannerHeight) * tile;
            float spacing = Mathf.Max(0.05f, Spacing);
            int maxSoldiers = Mathf.Clamp(MaxSoldiersPerArmy, 1, 40);
            float turnLerp = 1f - Mathf.Exp(-dt * 6f);

            _soldiers.Clear();
            _banners.Clear();

            IReadOnlyList<Army> armies = state.Diplomacy.Armies.Items;
            int drawnArmies = 0;
            for (int ai = 0; ai < armies.Count && drawnArmies < MaxArmiesDrawn; ai++)
            {
                Army a = armies[ai];
                if (a == null || !a.IsAlive) continue;
                drawnArmies++;

                ArmyVisual vis = GetVisual(a, now);
                vis.SeenFrame = frameNo;
                if (a.Position.X != vis.ToTileX || a.Position.Y != vis.ToTileY)
                {
                    vis.FromX = vis.CurX; vis.FromY = vis.CurY;
                    vis.ToTileX = a.Position.X; vis.ToTileY = a.Position.Y;
                    vis.ToX = a.Position.X; vis.ToY = a.Position.Y;
                    vis.StartTime = now;
                    vis.Duration = tickDuration;
                    float jx = vis.ToX - vis.FromX, jy = vis.ToY - vis.FromY;
                    if (jx * jx + jy * jy > SnapDistance * SnapDistance) { vis.FromX = vis.ToX; vis.FromY = vis.ToY; }
                }
                float t = vis.Duration > 0f ? Mathf.Clamp01((now - vis.StartTime) / vis.Duration) : 1f;
                vis.CurX = vis.FromX + (vis.ToX - vis.FromX) * t;
                vis.CurY = vis.FromY + (vis.ToY - vis.FromY) * t;
                bool moving = t < 1f && (vis.ToX != vis.FromX || vis.ToY != vis.FromY);

                // Facing: travel direction, else toward the siege target / destination.
                float fdx = 0f, fdy = 0f;
                if (moving) { fdx = vis.ToX - vis.FromX; fdy = vis.ToY - vis.FromY; }
                else if (a.State == ArmyState.Besieging && a.TargetSettlementId >= 0)
                {
                    Settlement target = state.Settlements.Get(a.TargetSettlementId);
                    if (target != null) { fdx = target.Center.X - vis.CurX; fdy = target.Center.Y - vis.CurY; }
                }
                else if (a.Destination != a.Position && a.Destination.IsValid)
                {
                    fdx = a.Destination.X - vis.CurX; fdy = a.Destination.Y - vis.CurY;
                }
                if (fdx * fdx + fdy * fdy > 1e-6f)
                {
                    Vector3 wd = _frame.TileDirToWorld(fdx, fdy);
                    vis.Yaw = Mathf.LerpAngle(vis.Yaw, UnitMath.YawOf(wd.x, wd.z), turnLerp);
                }

                Civilization civ = state.Civilizations.Get(a.CivilizationId);
                Color civColor = civ != null ? GodSimPalette.FromRgba(civ.Color) : new Color(0.7f, 0.7f, 0.7f, 1f);
                civColor.a = 1f;

                // Formation axes in tile space.
                float yawRad = vis.Yaw * Mathf.Deg2Rad;
                Vector2 fwd = _frame.WorldDirToTile(Mathf.Sin(yawRad), Mathf.Cos(yawRad));
                Vector2 right = _frame.WorldDirToTile(Mathf.Cos(yawRad), -Mathf.Sin(yawRad));

                int count = Mathf.Min(a.SoldierIds.Count, maxSoldiers);
                if (count <= 0) count = 1; // a standard-bearer for an army being raised / the last survivor
                int cols = Mathf.Clamp(Mathf.CeilToInt(Mathf.Sqrt(count * 1.5f)), 1, 8);
                int rows = (count + cols - 1) / cols;
                bool besieging = a.State == ArmyState.Besieging;
                bool retreating = a.State == ArmyState.Retreating || a.State == ArmyState.Disbanding;
                float sp = retreating ? spacing * 1.6f : spacing;
                float morale = Mathf.Clamp01(a.Morale);

                for (int k = 0; k < count; k++)
                {
                    int r = k / cols, c = k % cols;
                    int inRow = r == rows - 1 ? count - r * cols : cols;
                    float lateral = (c - (inRow - 1) * 0.5f) * sp;
                    float back = (r - (rows - 1) * 0.5f) * sp;
                    uint h = UnitMath.Hash(a.Id, k);
                    lateral += UnitMath.Signed(h) * 0.18f * sp;
                    back += UnitMath.Signed(h >> 8) * 0.18f * sp;
                    if (retreating) { lateral += UnitMath.Signed(h >> 4) * 0.5f * sp; back += UnitMath.Signed(h >> 12) * 0.5f * sp; }
                    float yawJitter = 0f;
                    if (besieging)
                    {
                        float ph = UnitMath.Unit(h >> 2) * UnitMath.TwoPi;
                        lateral += Mathf.Sin(now * 7.3f + ph) * SiegeJitter;
                        back += Mathf.Sin(now * 5.1f + ph * 1.7f) * SiegeJitter;
                        yawJitter = Mathf.Sin(now * 3.7f + ph) * 12f;
                    }
                    float fx = vis.CurX + right.x * lateral - fwd.x * back;
                    float fy = vis.CurY + right.y * lateral - fwd.y * back;
                    Vector3 pos = GodSimCoords.TileToWorld(map, fx, fy);
                    if (moving) pos.y += Mathf.Abs(Mathf.Sin(now * 10f + k * 1.3f)) * 0.06f * soldierScale;
                    Matrix4x4 m = Matrix4x4.TRS(pos, Quaternion.Euler(0f, vis.Yaw + yawJitter, 0f), new Vector3(soldierScale, soldierScale, soldierScale));
                    float shade = 0.85f + 0.2f * UnitMath.Unit(h >> 16) * (0.6f + 0.4f * morale);
                    _soldiers.Add(m, new Color(civColor.r * shade, civColor.g * shade, civColor.b * shade, 1f));
                }

                // Banner at the front-left of the formation.
                float bLat = -((cols - 1) * 0.5f + 0.8f) * sp;
                float bBack = -(rows - 1) * 0.5f * sp;
                float bx = vis.CurX + right.x * bLat - fwd.x * bBack;
                float by = vis.CurY + right.y * bLat - fwd.y * bBack;
                Vector3 bpos = GodSimCoords.TileToWorld(map, bx, by);
                float flutter = Mathf.Sin(now * 2.7f + a.Id * 0.73f) * 10f;
                Matrix4x4 bm = Matrix4x4.TRS(bpos, Quaternion.Euler(0f, vis.Yaw + 90f + flutter, 0f), new Vector3(bannerScale, bannerScale, bannerScale));
                _banners.Add(bm, civColor);
            }

            // Recycle visuals of armies that disappeared.
            _stale.Clear();
            foreach (KeyValuePair<int, ArmyVisual> kv in _visuals) if (kv.Value.SeenFrame != frameNo) _stale.Add(kv.Key);
            for (int i = 0; i < _stale.Count; i++)
            {
                ArmyVisual v;
                if (_visuals.TryGetValue(_stale[i], out v)) { _visualPool.Push(v); _visuals.Remove(_stale[i]); }
            }

            ShadowCastingMode shadows = CastShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            Material mat = MaterialLibrary.VertexColorInstanced;
            DrawnSoldiers = _soldiers.Count;
            _soldiers.Draw(_soldierMesh, mat, shadows, true, gameObject.layer);
            _banners.Draw(_bannerMesh, mat, shadows, true, gameObject.layer);
        }

        private ArmyVisual GetVisual(Army a, float now)
        {
            ArmyVisual v;
            if (_visuals.TryGetValue(a.Id, out v)) return v;
            v = _visualPool.Count > 0 ? _visualPool.Pop() : new ArmyVisual();
            v.Id = a.Id;
            v.ToTileX = a.Position.X; v.ToTileY = a.Position.Y;
            v.FromX = v.ToX = v.CurX = a.Position.X;
            v.FromY = v.ToY = v.CurY = a.Position.Y;
            v.StartTime = now;
            v.Duration = 0f;
            float fdx = a.Destination.IsValid ? a.Destination.X - a.Position.X : 0f;
            float fdy = a.Destination.IsValid ? a.Destination.Y - a.Position.Y : 0f;
            v.Yaw = fdx * fdx + fdy * fdy > 1e-6f ? UnitMath.YawOf(_frame.TileDirToWorld(fdx, fdy).x, _frame.TileDirToWorld(fdx, fdy).z) : UnitMath.Unit(UnitMath.Hash(a.Id)) * 360f;
            _visuals[a.Id] = v;
            return v;
        }
    }
}
