using System;
using System.Collections.Generic;

namespace GodSim.Core.Diplomacy
{
    /// <summary>
    /// Field armies: raising (per civ, every 12 ticks, staggered by civ id), A* pathing, marching
    /// (every tick), field battles, sieges, conquest/razing, retreat and disbanding.
    /// <para>
    /// Per tick: (1) civilizations due for a decision may raise one army for one of their active wars;
    /// (2) every army is validated (civ alive, war active, home owned, target still the enemy's) and,
    /// every 12 ticks staggered by army id, its strength is recomputed from living soldiers and the
    /// retreat rules are checked; (3) hostile armies within 2 tiles engage and hold position;
    /// (4) free armies advance 0.25 * MoveSpeed movement points per tick along their path (tile cost =
    /// Biome.MoveCost, halved on roads); (5) engaged pairs fight a field-battle round every 6 ticks;
    /// (6) armies besieging their target fight a siege round every 6 ticks (staggered by army id).
    /// </para>
    /// All persistent state lives in <see cref="Army"/>, <see cref="War"/> and the settlement blackboard
    /// (<see cref="MilitaryMath.KeyArmyId"/> etc.); the fields of this class are caches only.
    /// </summary>
    public sealed partial class ArmySystem : ISimSystem
    {
        /// <summary>Ticks between heavy decisions of one civilization / one army.</summary>
        public const int DecisionInterval = 12;
        /// <summary>Ticks between combat rounds of one battle or siege.</summary>
        public const int CombatInterval = 6;
        /// <summary>Chebyshev distance at which hostile armies engage.</summary>
        public const int EngageRange = 2;
        /// <summary>Chebyshev distance to a target's center at which an army besieges it (also "arrived home").</summary>
        public const int SiegeRange = 2;
        /// <summary>A* expansion budget per path query.</summary>
        public const int MaxPathExpansions = 30000;
        /// <summary>Movement points per tick at MoveSpeed 1 (one grassland tile costs 1).</summary>
        public const float BaseMoveSpeed = 0.25f;
        /// <summary>Armies retreat below this morale.</summary>
        public const float RetreatMorale = 0.3f;
        /// <summary>Armies retreat when fewer than this fraction of their initial soldiers remain.</summary>
        public const float RetreatSoldierFraction = 0.3f;
        /// <summary>Regroup time of a settlement after its army came home.</summary>
        public const int RestTicksAfterCampaign = 48;
        /// <summary>Regroup time of a settlement after its army retreated.</summary>
        public const int RestTicksAfterRetreat = 96;
        /// <summary>Target candidates path-tested per decision.</summary>
        public const int MaxTargetAttempts = 3;

        /// <summary>Ticks a budget-exhausted (very long) route is skipped before being searched again.</summary>
        public const int NoPathRetryTicks = 240;
        /// <summary>Minimum ticks between two chronicled non-decisive battles of the same civilization.</summary>
        public const int BattleChronicleCooldown = 48;

        private const int MaxUnreachableCache = 4096;

        // ---- caches (never persisted; rebuilt on demand)
        private WorldMap _map;
        private GridPathfinder _pathfinder;
        private readonly Func<int, float> _costFn;
        private readonly Dictionary<long, int> _unreachable = new Dictionary<long, int>();
        private readonly Dictionary<int, War> _activeWars = new Dictionary<int, War>();
        private bool _warfare = true;
        private bool _lastSearchHitBudget;
        private int _aliveArmies;

        // ---- reusable buffers (no per-tick allocations)
        private readonly List<Army> _armies = new List<Army>();
        private readonly List<Army> _sorted = new List<Army>();
        private readonly List<Army> _pairA = new List<Army>();
        private readonly List<Army> _pairB = new List<Army>();
        private readonly List<Army> _defenders = new List<Army>();
        private readonly HashSet<int> _engaged = new HashSet<int>();
        private readonly HashSet<int> _fought = new HashSet<int>();
        private readonly List<Vec2i> _pathBuffer = new List<Vec2i>();
        private readonly List<Person> _garrison = new List<Person>();
        private readonly List<Person> _civilians = new List<Person>();
        private readonly List<Person> _raise = new List<Person>();
        private readonly List<int> _idBuffer = new List<int>();
        private readonly List<Settlement> _candidates = new List<Settlement>();
        private readonly List<float> _candidateScores = new List<float>();

        /// <summary>Creates the system (public parameterless ctor for composition).</summary>
        public ArmySystem() { _costFn = TileCost; }

        /// <inheritdoc />
        public string Name => "Diplomacy.Armies";

        /// <inheritdoc />
        public int TickInterval => 1;

        /// <summary>Expansions used by the last path query (diagnostics).</summary>
        public int LastPathExpansions => _pathfinder != null ? _pathfinder.LastExpansions : 0;

        /// <inheritdoc />
        public void Initialize(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.State.Map == null) return;
            EnsureMap(ctx.State.Map);
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.State.Map == null || ctx.Ops == null) return;
            var state = ctx.State;
            EnsureMap(state.Map);
            long tick = state.Tick;
            int dt = Math.Max(1, ctx.DeltaTicks);
            _warfare = ctx.Config == null || ctx.Config.EnableWarfare;
            RefreshWars(state);
            CountAliveArmies(state);

            if (_warfare && _activeWars.Count > 0) DecideCivilizations(ctx, tick);

            _armies.Clear();
            var items = state.Diplomacy.Armies.Items;
            for (int i = 0; i < items.Count; i++) if (items[i].IsAlive) _armies.Add(items[i]);
            if (_armies.Count == 0) return;

            for (int i = 0; i < _armies.Count; i++) Upkeep(ctx, _armies[i], tick);
            DetectEngagements(ctx, tick);
            for (int i = 0; i < _armies.Count; i++)
            {
                var a = _armies[i];
                if (a.IsAlive && !_engaged.Contains(a.Id)) Move(ctx, a, dt);
            }
            ResolveFieldBattles(ctx);
            ResolveSieges(ctx, tick);
        }

        // =============================================================== caches
        private void EnsureMap(WorldMap map)
        {
            if (map == null) return;
            if (_map == map && _pathfinder != null) return;
            _map = map;
            _pathfinder = new GridPathfinder(map.Width, map.Height);
            _unreachable.Clear();
        }

        private float TileCost(int index) => MilitaryMath.TileMoveCost(_map, index);

        private void RefreshWars(WorldState state)
        {
            _activeWars.Clear();
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w != null && w.IsActive && !_activeWars.ContainsKey(w.Id)) _activeWars.Add(w.Id, w);
            }
        }

        private void CountAliveArmies(WorldState state)
        {
            _aliveArmies = 0;
            var items = state.Diplomacy.Armies.Items;
            for (int i = 0; i < items.Count; i++) if (items[i].IsAlive) _aliveArmies++;
        }

        private War ActiveWar(int warId) => _warfare && _activeWars.TryGetValue(warId, out var w) ? w : null;

        private static bool Due(long tick, int id, int interval)
        {
            long m = (tick + id) % interval;
            if (m < 0) m += interval;
            return m == 0;
        }

        // =============================================================== per-army upkeep
        private void Upkeep(SimContext ctx, Army a, long tick)
        {
            if (!a.IsAlive) return;
            var state = ctx.State;
            var civ = state.Civilizations.Get(a.CivilizationId);
            if (civ == null || !civ.IsAlive || a.SoldierIds.Count == 0) { Disband(ctx, a); return; }

            if (!_map.InBounds(a.Position)) a.Position = _map.ClampToBounds(a.Position);
            a.Morale = MathUtil.Clamp01(MathUtil.Sanitize(a.Morale, 0.5f));
            a.MoveProgress = MathUtil.Clamp(MathUtil.Sanitize(a.MoveProgress), 0f, 16f);

            bool decision = Due(tick, a.Id, DecisionInterval);
            if (decision && RecomputeStrength(ctx, a, civ) == 0) { Disband(ctx, a); return; }

            var home = HomeOf(state, a);
            if (a.TargetSettlementId < 0 || a.State == ArmyState.Retreating) { UpkeepReturning(ctx, a, civ, home, decision); return; }

            var war = ActiveWar(a.WarId);
            if (war == null) { SendHome(ctx, a, civ, false); return; }
            if (home == null) { SendHome(ctx, a, civ, true); return; }
            var target = ValidTarget(state, a, war);
            if (target == null)
            {
                if (!Retarget(ctx, a, civ, war)) SendHome(ctx, a, civ, false);
                return;
            }
            if (decision && ShouldRetreat(state, a, home)) { SendHome(ctx, a, civ, true); return; }

            if (a.Position.ChebyshevTo(target.Center) <= SiegeRange)
            {
                if (a.State != ArmyState.Besieging)
                {
                    a.State = ArmyState.Besieging;
                    a.Destination = target.Center;
                    a.Path.Clear();
                    a.PathIndex = 0;
                    a.MoveProgress = 0f;
                }
                return;
            }

            if (a.State != ArmyState.Marching) a.State = ArmyState.Marching;
            bool needPath = a.Path.Count == 0 || a.Destination != target.Center || (decision && a.PathIndex >= a.Path.Count - 1);
            if (needPath)
            {
                a.Destination = target.Center;
                if (!PathTo(a, target.Center)) SendHome(ctx, a, civ, false); // unreachable target
            }
        }

        private void UpkeepReturning(SimContext ctx, Army a, Civilization civ, Settlement home, bool decision)
        {
            if (a.TargetSettlementId >= 0) { SendHome(ctx, a, civ, true); return; } // normalize a retreat that still names a target
            if (a.State != ArmyState.Marching && a.State != ArmyState.Retreating) a.State = ArmyState.Marching;
            if (home != null)
            {
                if (a.Destination != home.Center || a.Path.Count == 0)
                {
                    a.Destination = home.Center;
                    if (!PathTo(a, home.Center)) { Disband(ctx, a); return; }
                }
            }
            else if (decision || a.Path.Count == 0)
            {
                // Home lost (captured/destroyed): fall back to the nearest settlement we still own.
                var refuge = ctx.Ops.NearestSettlementOf(civ.Id, a.Position);
                if (refuge == null) { Disband(ctx, a); return; }
                if (a.Destination != refuge.Center || a.Path.Count == 0)
                {
                    a.Destination = refuge.Center;
                    if (!PathTo(a, refuge.Center)) { Disband(ctx, a); return; }
                }
            }

            if (a.Position.ChebyshevTo(a.Destination) <= SiegeRange) { Disband(ctx, a); return; }
            if (decision && a.PathIndex >= a.Path.Count - 1 && !PathTo(a, a.Destination)) Disband(ctx, a);
        }

        private static Settlement HomeOf(WorldState state, Army a)
        {
            var h = state.Settlements.Get(a.HomeSettlementId);
            return h != null && h.IsAlive && h.CivilizationId == a.CivilizationId ? h : null;
        }

        private static int EnemyOf(War war, int civId)
        {
            if (war == null) return -1;
            if (war.AttackerId == civId) return war.DefenderId;
            if (war.DefenderId == civId) return war.AttackerId;
            return -1;
        }

        private static Settlement ValidTarget(WorldState state, Army a, War war)
        {
            var t = state.Settlements.Get(a.TargetSettlementId);
            if (t == null || !t.IsAlive) return null;
            int enemy = EnemyOf(war, a.CivilizationId);
            return enemy >= 0 && t.CivilizationId == enemy ? t : null;
        }

        /// <summary>Recomputes Strength = living soldiers * soldierStrength * Morale; returns the living count.</summary>
        private static int RecomputeStrength(SimContext ctx, Army a, Civilization civ)
        {
            var state = ctx.State;
            int living = MilitaryMath.LivingSoldiers(state, a);
            var home = state.Settlements.Get(a.HomeSettlementId);
            Stockpile supply = home != null && home.IsAlive && home.CivilizationId == a.CivilizationId ? home.Stockpile : null;
            int supplied = home != null ? Math.Max(home.Soldiers, living) : living;
            float per = MilitaryMath.SoldierStrength(ctx.Db, civ, supply, supplied);
            a.Strength = MathUtil.Clamp(MathUtil.Sanitize(living * per * MathUtil.Clamp01(a.Morale)), 0f, 1e9f);
            return living;
        }

        private static bool ShouldRetreat(WorldState state, Army a, Settlement home)
        {
            if (a.Morale < RetreatMorale) return true;
            if (home != null && home.Memory.TryGetValue(MilitaryMath.KeyArmyId, out float id) && !float.IsNaN(id) && (int)id == a.Id)
            {
                float initial = MathUtil.Sanitize(home.Mem(MilitaryMath.KeyArmyInitialSoldiers));
                if (initial > 0f && MilitaryMath.LivingSoldiers(state, a) < RetreatSoldierFraction * initial) return true;
            }
            return false;
        }

        /// <summary>Clears the target and walks to the home settlement (or the nearest own one); disbands when that is impossible.</summary>
        private void SendHome(SimContext ctx, Army a, Civilization civ, bool retreat)
        {
            if (!a.IsAlive) return;
            a.TargetSettlementId = -1;
            a.State = retreat ? ArmyState.Retreating : ArmyState.Marching;
            a.MoveProgress = 0f;
            var dest = HomeOf(ctx.State, a);
            if (dest == null && civ != null) dest = ctx.Ops.NearestSettlementOf(civ.Id, a.Position);
            if (dest == null) { Disband(ctx, a); return; }
            a.Destination = dest.Center;
            if (a.Position.ChebyshevTo(dest.Center) <= SiegeRange) { Disband(ctx, a); return; }
            if (!PathTo(a, dest.Center)) Disband(ctx, a);
        }

        /// <summary>Disbands through WorldOps; soldiers keep their jobs and walk back into town.</summary>
        private void Disband(SimContext ctx, Army a)
        {
            if (a == null || !a.IsAlive) return;
            var state = ctx.State;
            bool retreated = a.State == ArmyState.Retreating;
            var ids = a.SoldierIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = state.People.Get(ids[i]);
                if (p == null || !p.IsAlive) continue;
                var b = p.HomeId >= 0 ? state.Buildings.Get(p.HomeId) : null;
                if (b != null && !b.IsDestroyed) p.Position = b.Center;
                else { var s = state.Settlements.Get(p.SettlementId); if (s != null) p.Position = s.Center; }
            }
            var home = state.Settlements.Get(a.HomeSettlementId);
            if (home != null)
            {
                if (home.Memory.TryGetValue(MilitaryMath.KeyArmyId, out float v) && (float.IsNaN(v) || (int)v == a.Id))
                {
                    home.Memory.Remove(MilitaryMath.KeyArmyId);
                    home.Memory.Remove(MilitaryMath.KeyArmyInitialSoldiers);
                }
                if (home.IsAlive && home.CivilizationId == a.CivilizationId)
                    home.Memory[MilitaryMath.KeyRestUntil] = state.Tick + (retreated ? RestTicksAfterRetreat : RestTicksAfterCampaign);
            }
            ctx.Ops.DisbandArmy(a);
            if (_aliveArmies > 0) _aliveArmies--;
        }

        // =============================================================== pathing & movement
        private bool FindPath(Vec2i from, Vec2i goal, List<Vec2i> outPath)
        {
            outPath.Clear();
            _lastSearchHitBudget = false;
            if (_map == null || _pathfinder == null) return false;
            var start = _map.ClampToBounds(from);
            var g = MilitaryMath.PassableNear(_map, goal);
            if (!g.IsValid) return false;
            if (start == g) { outPath.Add(start); return true; }
            if (_pathfinder.FindPath(start, g, _costFn, outPath, true, MaxPathExpansions) && outPath.Count > 0) return true;
            _lastSearchHitBudget = _pathfinder.LastExpansions > MaxPathExpansions;
            outPath.Clear();
            return false;
        }

        /// <summary>
        /// Path query from a settlement to a target. A search that exhausted the reachable area proves the
        /// pair disconnected for this terrain version and is cached in memory (re-searching would give the
        /// same answer, so the cache never changes decisions). A search that ran out of budget is recorded
        /// on the source's blackboard (persistent, so save/load stays in step) and retried after
        /// <see cref="NoPathRetryTicks"/>.
        /// </summary>
        private bool FindPathCached(Settlement source, Settlement target, long tick, List<Vec2i> outPath)
        {
            long key = ((long)source.Id << 32) | (uint)target.Id;
            if (_unreachable.TryGetValue(key, out int version) && version == _map.TerrainVersion) { outPath.Clear(); return false; }
            if (FindPath(source.Center, target.Center, outPath)) return true;
            if (_lastSearchHitBudget)
            {
                source.Memory[MilitaryMath.KeyNoPathTarget] = target.Id;
                source.Memory[MilitaryMath.KeyNoPathUntil] = tick + NoPathRetryTicks;
                return false;
            }
            if (_unreachable.Count >= MaxUnreachableCache) _unreachable.Clear();
            _unreachable[key] = _map.TerrainVersion;
            return false;
        }

        private bool PathTo(Army a, Vec2i goal)
        {
            if (!FindPath(a.Position, goal, _pathBuffer)) return false;
            a.Path.Clear();
            a.Path.AddRange(_pathBuffer);
            a.PathIndex = 0;
            return true;
        }

        private void Move(SimContext ctx, Army a, int dt)
        {
            if (a.State != ArmyState.Marching && a.State != ArmyState.Retreating) return;
            if (a.PathIndex < 0) a.PathIndex = 0;
            if (a.Path.Count == 0 || a.PathIndex >= a.Path.Count - 1) { a.MoveProgress = 0f; return; }
            var civ = ctx.State.Civilizations.Get(a.CivilizationId);
            if (civ == null) return;
            float speed = MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.MoveSpeed, 1f), 0.1f, 5f);
            a.MoveProgress = MathUtil.Clamp(MathUtil.Sanitize(a.MoveProgress) + BaseMoveSpeed * speed * dt, 0f, 16f);

            bool moved = false, repathed = false;
            int guard = 64;
            while (a.IsAlive && a.PathIndex < a.Path.Count - 1 && guard-- > 0)
            {
                var next = a.Path[a.PathIndex + 1];
                float cost = _map.InBounds(next) ? MilitaryMath.TileMoveCost(_map, _map.Index(next)) : float.PositiveInfinity;
                if (float.IsInfinity(cost) || next.ChebyshevTo(a.Position) > 1)
                {
                    // Terrain changed (flood, lowered land) or the stored path is stale: re-plan once.
                    if (repathed || !Repath(ctx, a, civ)) break;
                    repathed = true;
                    continue;
                }
                if (a.MoveProgress + 1e-5f < cost) break;
                a.MoveProgress -= cost;
                a.PathIndex++;
                a.Position = next;
                moved = true;
            }
            if (!a.IsAlive) return;
            if (a.PathIndex >= a.Path.Count - 1) a.MoveProgress = 0f;
            if (moved) SyncSoldiers(ctx.State, a);
        }

        /// <summary>Re-plans toward the current goal. A campaign cut off from its target turns home; a homeward army that cannot get home disbands.</summary>
        private bool Repath(SimContext ctx, Army a, Civilization civ)
        {
            if (a.TargetSettlementId >= 0 && a.State != ArmyState.Retreating)
            {
                var t = ctx.State.Settlements.Get(a.TargetSettlementId);
                if (t != null && t.IsAlive && PathTo(a, t.Center)) { a.Destination = t.Center; return true; }
                SendHome(ctx, a, civ, false);
                return a.IsAlive && a.Path.Count > 1;
            }
            if (PathTo(a, a.Destination)) return true;
            Disband(ctx, a);
            return false;
        }

        private static void SyncSoldiers(WorldState state, Army a)
        {
            var ids = a.SoldierIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = state.People.Get(ids[i]);
                if (p != null && p.IsAlive) p.Position = a.Position;
            }
        }
    }
}
