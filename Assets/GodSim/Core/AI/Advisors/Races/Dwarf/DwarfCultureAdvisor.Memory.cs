using System;

namespace GodSim.Core.AI
{
    // Long memory: the Book of Grudges, the DeepForge bookkeeping and the rebuild-in-stone reflex after disasters.
    public sealed partial class DwarfCultureAdvisor
    {
        private SimContext _ctx;
        private EventBus _bus;
        private readonly Action<BattleEvent> _onBattle;
        private readonly Action<BuildingDestroyedEvent> _onBuildingDestroyed;
        private readonly Action<SettlementCapturedEvent> _onCaptured;
        private readonly Action<DisasterEvent> _onDisaster;

        public DwarfCultureAdvisor()
        {
            _onBattle = OnBattle;
            _onBuildingDestroyed = OnBuildingDestroyed;
            _onCaptured = OnSettlementCaptured;
            _onDisaster = OnDisaster;
        }

        private void SubscribeEvents(SimContext ctx)
        {
            _ctx = ctx;
            var bus = ctx?.Events;
            if (bus == _bus) return;
            if (_bus != null)
            {
                _bus.Unsubscribe(_onBattle);
                _bus.Unsubscribe(_onBuildingDestroyed);
                _bus.Unsubscribe(_onCaptured);
                _bus.Unsubscribe(_onDisaster);
            }
            _bus = bus;
            if (bus == null) return;
            bus.Subscribe(_onBattle);
            bus.Subscribe(_onBuildingDestroyed);
            bus.Subscribe(_onCaptured);
            bus.Subscribe(_onDisaster);
        }

        // ------------------------------------------------------------------ public grudge API
        /// <summary>Current grudge (0..1) a dwarf civ holds against another civ.</summary>
        public static float Grudge(Civilization dwarves, int offenderCivId)
        {
            if (dwarves == null || offenderCivId < 0) return 0f;
            return MathUtil.Clamp01(MathUtil.Sanitize(dwarves.Mem(DwarfCultureKeys.GrudgeKey(offenderCivId))));
        }

        /// <summary>Writes a wrong into the Book of Grudges (dwarf civs only; capped at 1).</summary>
        public static void RecordGrudge(Civilization dwarves, int offenderCivId, float amount)
        {
            if (dwarves == null || dwarves.Race != RaceId.Dwarf || offenderCivId < 0 || offenderCivId == dwarves.Id) return;
            amount = MathUtil.Sanitize(amount);
            if (amount <= 0f) return;
            string key = DwarfCultureKeys.GrudgeKey(offenderCivId);
            dwarves.Memory[key] = MathUtil.Clamp01(MathUtil.Sanitize(dwarves.Mem(key)) + amount);
        }

        // ------------------------------------------------------------------ event handlers (never throw)
        private void OnBattle(BattleEvent e)
        {
            var state = _ctx?.State;
            if (state == null) return;
            var atk = state.Civilizations.Get(e.AttackerCivId);
            var def = state.Civilizations.Get(e.DefenderCivId);
            if (atk == null || def == null) return;
            if (def.Race == RaceId.Dwarf)
            {
                float losses = MathUtil.Clamp(e.DefenderLosses * 0.02f, 0f, 0.3f);
                RecordGrudge(def, atk.Id, (e.AttackerWon ? DwarfCultureKeys.GrudgeBattleLost : DwarfCultureKeys.GrudgeAttacked) + losses);
            }
            if (atk.Race == RaceId.Dwarf && !e.AttackerWon)
                RecordGrudge(atk, def.Id, DwarfCultureKeys.GrudgeBattleLost + MathUtil.Clamp(e.AttackerLosses * 0.02f, 0f, 0.3f));
        }

        private void OnBuildingDestroyed(BuildingDestroyedEvent e)
        {
            if (e.Cause != DestroyCause.War) return;
            var state = _ctx?.State;
            if (state == null) return;
            var s = state.Settlements.Get(e.SettlementId);
            if (s == null) return;
            var civ = state.Civilizations.Get(s.CivilizationId);
            if (civ == null || civ.Race != RaceId.Dwarf) return;
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null || !w.IsActive) continue;
                if (w.AttackerId == civ.Id) RecordGrudge(civ, w.DefenderId, DwarfCultureKeys.GrudgeBuildingLost);
                else if (w.DefenderId == civ.Id) RecordGrudge(civ, w.AttackerId, DwarfCultureKeys.GrudgeBuildingLost);
            }
        }

        private void OnSettlementCaptured(SettlementCapturedEvent e)
        {
            var state = _ctx?.State;
            if (state == null) return;
            RecordGrudge(state.Civilizations.Get(e.OldCivilizationId), e.NewCivilizationId, DwarfCultureKeys.GrudgeSettlementLost);
        }

        private void OnDisaster(DisasterEvent e)
        {
            if (e.Type != DisasterType.Earthquake && e.Type != DisasterType.Wildfire) return;
            var state = _ctx?.State;
            if (state == null) return;
            float sev = MathUtil.Clamp(MathUtil.Sanitize(e.Severity, 0.5f), 0.2f, 1f);
            if (e.Type == DisasterType.Wildfire) sev *= 0.5f;
            int radius = MathUtil.Clamp(e.Radius, 0, 64);
            var items = state.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var s = items[i];
                if (!s.IsAlive) continue;
                var civ = state.Civilizations.Get(s.CivilizationId);
                if (civ == null || civ.Race != RaceId.Dwarf) continue;
                int reach = radius + s.TerritoryRadius + 2;
                if (s.Center.DistanceSqTo(e.Position) > reach * reach) continue;
                float prev = s.Mem(DwarfCultureKeys.QuakeSeverity);
                s.Memory[DwarfCultureKeys.QuakeTick] = state.Tick;
                s.Memory[DwarfCultureKeys.QuakeSeverity] = Math.Max(MathUtil.Clamp01(MathUtil.Sanitize(prev)), sev);
            }
        }

        // ------------------------------------------------------------------ civ-level pass (once per run per civ)
        private void EnsureCivPass(SimContext ctx, Civilization civ)
        {
            long tick = ctx.Tick;
            if (civ.Memory.TryGetValue(DwarfCultureKeys.LastCivPass, out float last) && (long)last == tick) return;
            long delta = civ.Memory.ContainsKey(DwarfCultureKeys.LastCivPass) ? tick - (long)last : ctx.DeltaTicks;
            if (delta < 1) delta = 1;
            if (delta > 10 * SimTime.TicksPerYear) delta = 10 * SimTime.TicksPerYear;
            civ.Memory[DwarfCultureKeys.LastCivPass] = tick;

            DecayGrudges(ctx.State, civ, delta);
            UpdateForge(ctx.State, civ);
        }

        private static void DecayGrudges(WorldState state, Civilization civ, long delta)
        {
            double halfLife = DwarfCultureKeys.GrudgeHalfLifeYears * SimTime.TicksPerYear;
            float factor = (float)Math.Pow(0.5, delta / halfLife);
            int maxId = Math.Min(state.Ids.NextCivilizationId, 4096);
            for (int id = 0; id < maxId; id++)
            {
                if (id == civ.Id) continue;
                string key = DwarfCultureKeys.GrudgeKey(id);
                if (!civ.Memory.TryGetValue(key, out float g)) continue;
                var offender = state.Civilizations.Get(id);
                g = MathUtil.Clamp01(MathUtil.Sanitize(g)) * factor;
                if (offender == null || !offender.IsAlive) g *= factor; // the foe is gone: the grudge fades into legend
                if (g < DwarfCultureKeys.GrudgeForgetThreshold) civ.Memory.Remove(key);
                else civ.Memory[key] = g;
            }
        }

        private static void UpdateForge(WorldState state, Civilization civ)
        {
            int forgeState = 0;
            int seat = -1;
            var capital = state.Settlements.Get(civ.CapitalId);
            if (capital != null && capital.IsAlive && CountMountains(state.Map, capital) > 0) seat = capital.Id;
            var ids = civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s == null || !s.IsAlive) continue;
                for (int b = 0; b < s.BuildingIds.Count; b++)
                {
                    var bld = state.Buildings.Get(s.BuildingIds[b]);
                    if (bld == null || bld.IsDestroyed || bld.Type != BuildingType.DeepForge) continue;
                    forgeState = Math.Max(forgeState, bld.IsComplete ? 3 : 2);
                }
                for (int q = 0; q < s.BuildQueue.Count; q++)
                    if (s.BuildQueue[q] != null && s.BuildQueue[q].Type == BuildingType.DeepForge) forgeState = Math.Max(forgeState, 1);
                if (seat < 0 && CountMountains(state.Map, s) > 0) seat = s.Id;
            }
            if (seat < 0) seat = civ.CapitalId;
            civ.Memory[DwarfCultureKeys.ForgeState] = forgeState;
            civ.Memory[DwarfCultureKeys.ForgeSeat] = seat;
        }

        // ------------------------------------------------------------------ settlement-level memory effects
        private float AdviseGrudges(WorldState state)
        {
            float threat = 0f;
            int maxId = Math.Min(state.Ids.NextCivilizationId, 4096);
            for (int id = 0; id < maxId; id++)
            {
                if (id == _civ.Id) continue;
                if (!_civ.Memory.TryGetValue(DwarfCultureKeys.GrudgeKey(id), out float g) || g <= 0f) continue;
                var offender = state.Civilizations.Get(id);
                if (offender == null || !offender.IsAlive) continue;
                float near = Proximity(state, offender, _s.Center);
                if (state.Diplomacy.AreAtWar(_civ.Id, id)) near = Math.Max(near, 0.6f);
                threat = Math.Max(threat, MathUtil.Clamp01(MathUtil.Sanitize(g)) * near);
            }
            if (threat < 0.02f) return 0f;

            Add(BuildingType.Barracks, 0.85f * threat, RGrudgeBarracks);
            if (IsAvailable(BuildingType.StoneWall)) Add(BuildingType.StoneWall, 0.7f * threat, RGrudgeWall);
            else Add(BuildingType.Palisade, 0.6f * threat, RGrudgeWall);
            Add(BuildingType.Watchtower, 0.6f * threat, RGrudgeTower);
            Add(BuildingType.Armory, 0.6f * threat, RGrudgeArmory);
            Add(BuildingType.Castle, 0.5f * threat, RGrudgeWall);
            Add(BuildingType.MilitaryBase, 0.6f * threat, RGrudgeBarracks);
            _jv[(int)JobType.Soldier] += 0.8f * threat;
            _jv[(int)JobType.Smith] += 0.25f * threat;
            return MathUtil.Clamp01(threat);
        }

        private static float Proximity(WorldState state, Civilization offender, Vec2i from)
        {
            int best = int.MaxValue;
            var ids = offender.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = state.Settlements.Get(ids[i]);
                if (s == null || !s.IsAlive) continue;
                int d = s.Center.DistanceSqTo(from);
                if (d < best) best = d;
            }
            if (best == int.MaxValue) return 0f;
            float dist = (float)Math.Sqrt(best);
            return 1f - MathUtil.InverseLerp(DwarfCultureKeys.GrudgeNearTiles, DwarfCultureKeys.GrudgeFarTiles, dist);
        }

        private float AdviseRebuildInStone(long tick)
        {
            if (!_s.Memory.TryGetValue(DwarfCultureKeys.QuakeTick, out float qt)) return 0f;
            float elapsed = tick - qt;
            if (elapsed < 0f || elapsed > DwarfCultureKeys.RebuildWindowTicks)
            {
                _s.Memory.Remove(DwarfCultureKeys.QuakeTick);
                _s.Memory.Remove(DwarfCultureKeys.QuakeSeverity);
                return 0f;
            }
            float w = MathUtil.Clamp01(MathUtil.Sanitize(_s.Mem(DwarfCultureKeys.QuakeSeverity))) * (1f - elapsed / DwarfCultureKeys.RebuildWindowTicks);
            if (w <= 0f) return 0f;
            if (_stone > 0) Add(BuildingType.StoneQuarry, 0.9f * w, RQuake);
            if (IsAvailable(BuildingType.StoneWall)) Add(BuildingType.StoneWall, 0.5f * w, RQuake);
            Add(BuildingType.Palisade, -0.4f * w, RQuakeStakes);
            Add(BuildingType.Storehouse, 0.3f * w, RQuake);
            _jv[(int)JobType.Quarrier] += 0.8f * w;
            _jv[(int)JobType.Builder] += 0.5f * w;
            _jv[(int)JobType.Woodcutter] -= 0.3f * w;
            return 0.4f + 0.5f * w;
        }
    }
}
