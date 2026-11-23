using System;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>
    /// The Defense advisor (id "defense", every 12 ticks): a per-settlement threat model that makes civilizations
    /// fortify ahead of danger, mobilise when an enemy approaches, never abandon a besieged town, rebuild after
    /// lost battles, and demobilise / favour butter over guns once peace returns.
    /// <para>
    /// Threat combines hostile neighbours (war, Hostile stance, aggressive races, bad opinion) by distance, hostile
    /// armies within 30 tiles and their approach trend, recent battles here (from <see cref="BattleEvent"/>), the
    /// civ-wide shock of a lost settlement, arms races and frontier exposure. Vote strength is
    /// threat x (1 - defense ratio) x personality (Aggression and Xenophobia raise it, Diplomacy lowers peacetime votes).
    /// Building choice is data-driven (<see cref="DefenseAdvisorCatalog"/>) and always gated by
    /// <see cref="GameDatabase.IsBuildingAvailable"/>, tier, population and per-settlement limits; quick builds are
    /// preferred when danger is imminent, efficient strongholds when it is distant.
    /// </para>
    /// </summary>
    public sealed class DefenseAdvisor : AdvisorSystemBase
    {
        /// <summary>Advisor id used in memory keys and logs.</summary>
        public const string Id = "defense";

        private readonly DefenseAdvisorCatalog _catalog = new DefenseAdvisorCatalog();
        private readonly DefenseAdvisorThreatModel _model = new DefenseAdvisorThreatModel();
        private readonly int[] _owned = new int[Counts.Buildings];
        private readonly int[] _pending = new int[Counts.Buildings];
        private readonly float[] _score = new float[Counts.Buildings];
        private readonly float[] _bestByRole = new float[4];
        private readonly StringBuilder _sb = new StringBuilder(96);
        private readonly Action<BattleEvent> _onBattle;
        private readonly Action<SettlementCapturedEvent> _onCaptured;
        private readonly Action<BuildingDestroyedEvent> _onDestroyed;
        private DefenseAdvisorThreat _t;
        private float _builtDefense, _plannedDefense;
        private int _soldierSlots;
        private EventBus _bus;
        private WorldState _state;

        /// <summary>Creates the advisor.</summary>
        public DefenseAdvisor()
        {
            _onBattle = OnBattle;
            _onCaptured = OnCaptured;
            _onDestroyed = OnBuildingDestroyed;
        }

        /// <inheritdoc />
        public override string AdvisorId => Id;
        /// <inheritdoc />
        public override int TickInterval => 12;

        /// <summary>The latest assessment (of the last settlement advised); for debugging and tests.</summary>
        public DefenseAdvisorThreat LastAssessment => _t;

        /// <inheritdoc />
        public override void Initialize(SimContext ctx)
        {
            if (_bus != null)
            {
                _bus.Unsubscribe(_onBattle);
                _bus.Unsubscribe(_onCaptured);
                _bus.Unsubscribe(_onDestroyed);
            }
            _bus = ctx?.Events;
            _state = ctx?.State;
            if (_bus != null)
            {
                _bus.Subscribe(_onBattle);
                _bus.Subscribe(_onCaptured);
                _bus.Subscribe(_onDestroyed);
            }
            if (ctx?.Db != null) _catalog.EnsureBuilt(ctx.Db);
        }

        /// <inheritdoc />
        protected override void Advise(SimContext ctx, Civilization civ, Settlement s)
        {
            var db = ctx.Db;
            var state = ctx.State;
            if (db == null || state == null) return;
            if (_state == null) _state = state;
            _catalog.EnsureBuilt(db);

            float aggression = AdvisorBoard.Trait(civ, db, AITrait.Aggression);
            float xenophobia = AdvisorBoard.Trait(civ, db, AITrait.Xenophobia);
            float diplomacy = AdvisorBoard.Trait(civ, db, AITrait.Diplomacy);

            SurveyBuildings(state, s);
            _model.Assess(ctx, civ, s, xenophobia, _builtDefense, _plannedDefense, true, out _t);
            var t = _t;

            // ---------------------------------------------------------------- mobilisation alert (hysteresis)
            int dt = Math.Max(1, ctx.DeltaTicks);
            float decay = (float)Math.Pow(DefenseAdvisorKeys.AlertDecayPer12, dt / 12.0);
            float alert = MathUtil.Clamp01(Math.Max(t.Imminence, MathUtil.Sanitize(s.Mem(DefenseAdvisorKeys.Alert)) * decay));
            if (alert < 0.01f) s.Memory.Remove(DefenseAdvisorKeys.Alert); else s.Memory[DefenseAdvisorKeys.Alert] = alert;

            // ---------------------------------------------------------------- personality & need
            bool peace = !t.AtWar && t.Imminence < 0.15f && !t.Besieged;
            float pers = 0.8f + 0.3f * aggression + 0.2f * xenophobia - (peace ? 0.35f : 0.05f) * diplomacy;
            if (t.Imminence > 0.5f) pers = Math.Max(pers, 0.75f); // even pacifists man the walls
            pers = MathUtil.Clamp(pers, 0.35f, 1.4f);

            float margin = 1f + 0.25f * xenophobia + 0.15f * aggression;
            float cover = MathUtil.Clamp01(t.DefenseRatio / margin);
            float need = MathUtil.Clamp01(t.Threat * (1f - cover));
            float buildStrength = MathUtil.Clamp01(need * pers * 1.15f);

            int freeSlots = _soldierSlots - Math.Max(0, s.Soldiers);
            float soldierBase = Math.Max(Math.Max(s.Soldiers, _soldierSlots), 4);
            bool weaponsShort = s.Stockpile.Get(ResourceType.Weapons) < 0.5f * soldierBase;

            float fortW = buildStrength;
            float garW = buildStrength * (0.7f + 0.5f * aggression) * (freeSlots >= 3 ? 0.55f : 1f);
            float armsW = buildStrength * 0.6f * (weaponsShort ? 1f : 0.35f) * (_soldierSlots > 0 || s.Soldiers > 0 ? 1f : 0.5f);

            bool butter = peace && t.Frontier < 0.3f && t.Threat < 0.15f;
            float butterW = 0f;
            if (butter)
                butterW = (0.1f + 0.2f * diplomacy) * (1f - 0.7f * aggression) * (1f - t.Frontier / 0.3f) * (t.OurDefense > 0f ? 1f : 0.5f);

            // ---------------------------------------------------------------- candidate scoring
            for (int r = 0; r < _bestByRole.Length; r++) _bestByRole[r] = 0f;
            int n = _catalog.Count;
            for (int i = 0; i < n; i++)
            {
                _score[i] = -1f;
                var type = _catalog.TypeAt(i);
                if (!db.IsBuildingAvailable(civ, type)) continue;
                if (!db.TryGetBuilding(type, out var def)) continue;
                if (s.Tier < def.MinTier || s.Population < def.MinPopulation) continue;
                int ti = (int)type;
                if (def.MaxPerSettlement >= 0 && _owned[ti] + _pending[ti] >= def.MaxPerSettlement) continue;
                if (_pending[ti] >= 2) continue; // already raising these; let them finish first
                float sc = _catalog.Score(i, t.Imminence, db);
                if (_catalog.RoleAt(i) == DefenseAdvisorRole.Arms) sc *= InputFactor(s, def);
                _score[i] = sc;
                int ri = (int)_catalog.RoleAt(i);
                if (sc > _bestByRole[ri]) _bestByRole[ri] = sc;
            }

            // ---------------------------------------------------------------- build votes
            string reason = null;
            float bestFort = 0f, topVote = 0f;
            BuildingType topType = BuildingType.TownCenter;
            for (int i = 0; i < n; i++)
            {
                var type = _catalog.TypeAt(i);
                var role = _catalog.RoleAt(i);
                if (butter)
                {
                    // guns vs butter: in a calm interior, gently argue against military spending
                    if (butterW > 0.005f && db.IsBuildingAvailable(civ, type))
                        VoteBuild(s, civ, type, -butterW * (role == DefenseAdvisorRole.Arms ? 0.5f : 1f));
                    continue;
                }
                float sc = _score[i];
                if (sc <= 0f) continue;
                float best = _bestByRole[(int)role];
                if (best <= 0f) continue;
                float rel = sc / best;
                if (rel < 0.25f) continue;
                float roleW = role == DefenseAdvisorRole.Fortification ? fortW : role == DefenseAdvisorRole.Garrison ? garW : armsW;
                float v = MathUtil.Clamp01(roleW * rel * (float)Math.Sqrt(rel));
                if (v < 0.01f) continue;
                if (role == DefenseAdvisorRole.Fortification && v > bestFort) bestFort = v;
                if (v >= 0.5f && reason == null) reason = BuildReason(db, state, t);
                if (v > topVote) { topVote = v; topType = type; }
                VoteBuild(s, civ, type, v, v >= 0.5f ? reason : null);
            }
            if (reason != null) LogStrongVote(s, civ, ctx.Tick, topType, topVote, reason);

            // ---------------------------------------------------------------- soldier & builder votes
            float soldier = 0f;
            if (!peace || t.Imminence > 0f) soldier = t.Imminence * (1f - 0.5f * cover) * pers;
            if (t.AtWar) soldier += 0.12f + 0.15f * t.Frontier;
            if (freeSlots > 0 && t.Imminence > 0.3f) soldier += 0.15f; // empty posts while danger nears
            if (alert > t.Imminence) soldier = Math.Max(soldier, 0.45f * alert * pers); // stand down gradually
            if (t.Besieged) soldier = Math.Max(soldier, 0.8f);                           // never abandon a besieged town
            else if (peace && alert < 0.12f)
            {
                // demobilisation: the danger has passed, send soldiers back to the fields
                soldier = -(0.12f + 0.15f * diplomacy) * (1f - 0.6f * aggression) * (1f - 0.7f * t.Frontier) * (s.Soldiers > 0 ? 1f : 0.5f);
            }
            if (Math.Abs(soldier) > 0.005f) VoteJob(s, civ, JobType.Soldier, MathUtil.Clamp(soldier, -1f, 1f));
            if (t.Imminence > 0.45f && bestFort > 0.3f) VoteJob(s, civ, JobType.Builder, 0.3f * t.Imminence);

            // ---------------------------------------------------------------- urgency & explanation
            float u = Math.Max(need, t.Imminence * (1f - 0.6f * cover));
            if (t.Besieged) u = Math.Max(u, 0.9f);
            Urgency(s, u);
            s.Memory[DefenseAdvisorKeys.Threat] = t.Threat;
            s.Memory[DefenseAdvisorKeys.Ratio] = t.DefenseRatio;
            s.Memory[DefenseAdvisorKeys.Frontier] = t.Frontier;
            s.Memory[DefenseAdvisorKeys.Imminence] = t.Imminence;
        }

        // ==================================================================== helpers
        /// <summary>
        /// The board logs only votes that are strong after per-cycle scaling; a strong per-cycle decision of this
        /// 12-tick advisor (two half-votes) would stay invisible, so it is logged here, at most once per council cycle.
        /// </summary>
        private void LogStrongVote(Settlement s, Civilization civ, long tick, BuildingType type, float strength, string reason)
        {
            float w = AdvisorBoard.Weight(civ, Id);
            float perCycle = strength * w;
            if (perCycle < 0.5f || perCycle * CycleScale >= 0.5f) return; // weak, or already logged by the board
            float last = s.Mem(DefenseAdvisorKeys.LogTick, -1e9f);
            if (tick - last < 24f && tick >= last) return;
            s.Memory[DefenseAdvisorKeys.LogTick] = tick;
            var sb = _sb;
            sb.Length = 0;
            int hundredths = MathUtil.RoundToInt(MathUtil.Clamp(perCycle, 0f, 3f) * 100f);
            sb.Append('[').Append(Id).Append("] +").Append(hundredths / 100).Append('.');
            int frac = hundredths % 100;
            if (frac < 10) sb.Append('0');
            sb.Append(frac).Append(' ').Append(type.ToString()).Append(": ").Append(reason);
            s.LogAi(sb.ToString());
        }

        /// <summary>Counts owned / pending buildings by type and estimates built and planned defense.</summary>
        private void SurveyBuildings(WorldState state, Settlement s)
        {
            Array.Clear(_owned, 0, _owned.Length);
            Array.Clear(_pending, 0, _pending.Length);
            float built = 0f, planned = 0f;
            int slots = 0;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int ti = (int)b.Type;
                if (ti < 0 || ti >= _owned.Length) continue;
                _owned[ti]++;
                float dv = _catalog.DefenseByType[ti];
                if (b.IsComplete)
                {
                    int sl = _catalog.SoldierSlotsByType[ti];
                    slots += sl;
                    float svc = _catalog.ServiceDefenseByType[ti];
                    float eff = sl > 0 ? (b.Efficiency > 0f ? MathUtil.Clamp01(b.Efficiency) : 0.5f) : MathUtil.Clamp01(b.Condition);
                    built += svc * eff;
                }
                else
                {
                    _pending[ti]++;
                    planned += dv;
                }
            }
            var q = s.BuildQueue;
            for (int i = 0; i < q.Count; i++)
            {
                var o = q[i];
                if (o == null) continue;
                int ti = (int)o.Type;
                if (ti < 0 || ti >= _pending.Length) continue;
                _pending[ti]++;
                planned += _catalog.DefenseByType[ti];
            }
            _builtDefense = MathUtil.Sanitize(built);
            _plannedDefense = MathUtil.Sanitize(planned);
            _soldierSlots = slots;
        }

        /// <summary>0.35 if any production input is missing with no income, else 1 (no armory without metal).</summary>
        private static float InputFactor(Settlement s, BuildingDefinition def)
        {
            var inputs = def.InputsPerWorker;
            if (inputs == null) return 1f;
            for (int k = 0; k < inputs.Length; k++)
            {
                var res = inputs[k].Type;
                if (s.Stockpile.Get(res) < 5f && s.Stockpile.Net(res) <= 0f) return 0.35f;
            }
            return 1f;
        }

        /// <summary>Short player-facing explanation, e.g. "Orc army 14 tiles away, approaching, defense 0.4x".</summary>
        private string BuildReason(GameDatabase db, WorldState state, in DefenseAdvisorThreat t)
        {
            var sb = _sb;
            sb.Length = 0;
            if (t.Besieged)
            {
                sb.Append("besieged by ").Append(RaceAdjective(db, state, t.NearestArmyCivId)).Append(" army");
            }
            else if (t.NearestArmyDist >= 0 && t.ArmyThreat >= t.SettlementThreat && t.ArmyThreat >= t.BattleThreat)
            {
                sb.Append(RaceAdjective(db, state, t.NearestArmyCivId)).Append(" army ").Append(t.NearestArmyDist).Append(" tiles away");
                if (t.ArmyEtaTicks >= 0 && t.ArmyEtaTicks < 400) sb.Append(", ~").Append(t.ArmyEtaTicks).Append(" days out");
                else if (t.ArmyApproaching) sb.Append(", approaching");
            }
            else if (t.BattleRecency > 0f && t.BattleThreat >= t.SettlementThreat)
            {
                sb.Append("battle here ").Append(Math.Max(0, t.TicksSinceBattle)).Append(" days ago");
                if (t.BattleLost) sb.Append(", lost");
                int losses = MathUtil.RoundToInt(t.BattleLosses);
                if (losses > 0) sb.Append(", ").Append(losses).Append(" fallen");
            }
            else if (t.HostileCivId >= 0 && t.SettlementThreat > 0.05f)
            {
                string adj = RaceAdjective(db, state, t.HostileCivId);
                if (t.HostileAtWar) sb.Append("at war with ").Append(adj).Append("s ").Append(t.HostileDist).Append(" tiles away");
                else sb.Append("hostile ").Append(adj).Append(" neighbours ").Append(t.HostileDist).Append(" tiles away");
            }
            else if (t.ShockThreat > 0.05f) sb.Append("a settlement was lost");
            else if (t.Frontier > 0.5f) sb.Append("frontier settlement");
            else if (t.AtWar) sb.Append("at war");
            else sb.Append("precaution");
            sb.Append(", defense ");
            int tenths = MathUtil.RoundToInt(MathUtil.Clamp(t.DefenseRatio, 0f, 5f) * 10f);
            sb.Append(tenths / 10).Append('.').Append(tenths % 10).Append('x');
            return sb.ToString();
        }

        private static string RaceAdjective(GameDatabase db, WorldState state, int civId)
        {
            var c = civId >= 0 && state != null ? state.Civilizations.Get(civId) : null;
            if (c == null) return "Enemy";
            int r = (int)c.Race;
            if (db != null && r >= 0 && r < db.Races.Count && db.Races[r] != null && !string.IsNullOrEmpty(db.Races[r].Adjective)) return db.Races[r].Adjective;
            return c.Race.ToString();
        }

        // ==================================================================== events
        private void OnBattle(BattleEvent e)
        {
            var state = _state;
            if (state == null) return;
            var s = e.SettlementId >= 0 ? state.Settlements.Get(e.SettlementId) : null;
            if (s != null && s.IsAlive)
            {
                RecordBattle(state, s, e);
                return;
            }
            // field battle: the nearest settlement of each side within 16 tiles remembers it
            RecordNearest(state, e, e.DefenderCivId);
            RecordNearest(state, e, e.AttackerCivId);
        }

        private static void RecordNearest(WorldState state, BattleEvent e, int civId)
        {
            if (civId < 0) return;
            var items = state.Settlements.Items;
            Settlement best = null;
            int bestD = 16 * 16 + 1;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (s == null || !s.IsAlive || s.CivilizationId != civId) continue;
                int d = s.Center.DistanceSqTo(e.Position);
                if (d < bestD) { bestD = d; best = s; }
            }
            if (best != null) RecordBattle(state, best, e);
        }

        private static void RecordBattle(WorldState state, Settlement s, BattleEvent e)
        {
            int ourLosses;
            bool lost;
            if (s.CivilizationId == e.AttackerCivId) { ourLosses = e.AttackerLosses; lost = !e.AttackerWon; }
            else { ourLosses = e.DefenderLosses; lost = e.AttackerWon; }
            Remember(state, s, Math.Max(0, ourLosses), lost);
        }

        private static void Remember(WorldState state, Settlement s, float losses, bool lost)
        {
            long tick = state.Tick;
            float prevTick = s.Mem(DefenseAdvisorKeys.BattleTick, -1f);
            float carry = 0f;
            bool prevLost = false;
            if (prevTick >= 0f)
            {
                float rec = MathUtil.Clamp01(1f - Math.Max(0f, tick - prevTick) / DefenseAdvisorKeys.BattleMemoryTicks);
                carry = rec * Math.Max(0f, MathUtil.Sanitize(s.Mem(DefenseAdvisorKeys.BattleLosses)));
                prevLost = rec > 0.5f && s.Mem(DefenseAdvisorKeys.BattleLost) > 0.5f;
            }
            s.Memory[DefenseAdvisorKeys.BattleTick] = tick;
            s.Memory[DefenseAdvisorKeys.BattleLosses] = MathUtil.Clamp(carry + losses, 0f, 1e6f);
            if (lost || prevLost) s.Memory[DefenseAdvisorKeys.BattleLost] = 1f; else s.Memory.Remove(DefenseAdvisorKeys.BattleLost);
        }

        private void OnCaptured(SettlementCapturedEvent e)
        {
            var state = _state;
            if (state == null) return;
            var old = state.Civilizations.Get(e.OldCivilizationId);
            if (old != null && old.IsAlive) old.Memory[DefenseAdvisorKeys.CivShockTick] = state.Tick;
        }

        private void OnBuildingDestroyed(BuildingDestroyedEvent e)
        {
            if (e.Cause != DestroyCause.War) return;
            var state = _state;
            if (state == null || e.SettlementId < 0) return;
            var s = state.Settlements.Get(e.SettlementId);
            if (s == null || !s.IsAlive) return;
            Remember(state, s, 0.5f, false); // razed by an enemy: counts as a (small) battle
        }
    }
}
