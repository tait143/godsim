using System;
using System.Collections.Generic;

namespace GodSim.Core.Diplomacy
{
    /// <summary>
    /// Computes military strength every 6 ticks:
    /// <list type="bullet">
    /// <item><c>Settlement.DefenseStrength = ServiceOutput[Defense] * (1 - siegeDamage) + garrison * soldierStrength</c>,
    /// where the garrison is <c>s.Soldiers</c> minus soldiers away in field armies raised there.</item>
    /// <item><c>Civilization.MilitaryPower</c> = sum of its settlements' DefenseStrength + strength of its field armies.</item>
    /// </list>
    /// Also lets siege damage (<see cref="MilitaryMath.KeySiegeDamage"/>) heal once a settlement is no longer besieged.
    /// </summary>
    public sealed class MilitarySystem : ISimSystem
    {
        /// <summary>Siege damage repaired per tick while not besieged.</summary>
        public const float SiegeRepairPerTick = 0.01f;

        private readonly Dictionary<int, int> _awayBySettlement = new Dictionary<int, int>();
        private readonly HashSet<int> _besieged = new HashSet<int>();

        /// <inheritdoc />
        public string Name => "Diplomacy.Military";

        /// <inheritdoc />
        public int TickInterval => 6;

        /// <summary>
        /// On a world that has not ticked yet (<c>State.Tick == 0</c>), computes DefenseStrength and MilitaryPower so they
        /// are valid before the first run. On a loaded world both are persisted and kept as saved: an uninterrupted run
        /// holds the values of the last Military run until the next one, so recomputing here would diverge.
        /// </summary>
        public void Initialize(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.State.Tick != 0) return;
            Recompute(ctx, 0);
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx) => Recompute(ctx, Math.Max(1, ctx != null ? ctx.DeltaTicks : 1));

        private void Recompute(SimContext ctx, int dt)
        {
            if (ctx == null || ctx.State == null) return;
            var state = ctx.State;
            var db = ctx.Db;

            _awayBySettlement.Clear();
            _besieged.Clear();
            var armies = state.Diplomacy.Armies.Items;
            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                if (!a.IsAlive) continue;
                if (a.HomeSettlementId >= 0)
                {
                    _awayBySettlement.TryGetValue(a.HomeSettlementId, out int n);
                    _awayBySettlement[a.HomeSettlementId] = n + a.SoldierIds.Count;
                }
                if (a.State == ArmyState.Besieging && a.TargetSettlementId >= 0) _besieged.Add(a.TargetSettlementId);
            }

            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) civs[i].MilitaryPower = 0f;

            var settlements = state.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (!s.IsAlive) { s.DefenseStrength = 0f; continue; }
                if (dt > 0 && !_besieged.Contains(s.Id) && s.Memory.TryGetValue(MilitaryMath.KeySiegeDamage, out float dmg))
                {
                    dmg = MathUtil.Sanitize(dmg) - SiegeRepairPerTick * dt;
                    if (dmg <= 0f) s.Memory.Remove(MilitaryMath.KeySiegeDamage);
                    else s.Memory[MilitaryMath.KeySiegeDamage] = MathUtil.Clamp(dmg, 0f, MilitaryMath.MaxSiegeDamage);
                }
                _awayBySettlement.TryGetValue(s.Id, out int away);
                float def = MilitaryMath.UpdateDefense(db, state, s, away);
                var civ = state.Civilizations.Get(s.CivilizationId);
                if (civ != null && civ.IsAlive) civ.MilitaryPower += def;
            }

            for (int i = 0; i < armies.Count; i++)
            {
                var a = armies[i];
                if (!a.IsAlive || a.State == ArmyState.Disbanding) continue;
                var civ = state.Civilizations.Get(a.CivilizationId);
                if (civ != null && civ.IsAlive) civ.MilitaryPower += Math.Max(0f, MathUtil.Sanitize(a.Strength));
            }

            for (int i = 0; i < civs.Count; i++)
                civs[i].MilitaryPower = MathUtil.Clamp(MathUtil.Sanitize(civs[i].MilitaryPower), 0f, 1e9f);
        }
    }
}
