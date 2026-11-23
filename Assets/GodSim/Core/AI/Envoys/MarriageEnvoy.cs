using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// ROYAL MARRIAGES: Friendly (or Allied) civs of the same race, or of races with high mutual affinity
    /// (<see cref="EnvoyTuning.MarriageMinAffinity"/>), may unite their ruling houses once per pair: a strong opinion
    /// impulse and a chronicle line naming both leaders (<c>civ.LeaderId</c> -&gt; <c>Person.Name</c>).
    /// </summary>
    public sealed class MarriageEnvoy
    {
        private readonly EnvoyKeyCache _keys = new EnvoyKeyCache();

        /// <summary>True if the two houses are already united.</summary>
        public bool AreMarried(WorldState state, int a, int b) => state != null && a != b && state.Memory.ContainsKey(_keys.Marriage(a, b));

        /// <summary>Willingness of the pair (0..1); 0 when they are not eligible.</summary>
        public float Willingness(SimContext ctx, Civilization a, Civilization b)
        {
            if (ctx == null || !EnvoyUtil.IsAlive(a) || !EnvoyUtil.IsAlive(b) || a == b) return 0f;
            var r = EnvoyUtil.RelationOf(ctx.State, a.Id, b.Id);
            if (r == null || !r.HasContact || !EnvoyUtil.IsFriendly(r)) return 0f;
            if (AreMarried(ctx.State, a.Id, b.Id)) return 0f;
            var mem = ctx.State.Memory;
            if (mem.ContainsKey(_keys.Betrayal(a.Id, b.Id)) || mem.ContainsKey(_keys.Betrayal(b.Id, a.Id))) return 0f;
            if (a.Race != b.Race)
            {
                if (EnvoyUtil.MutualAffinity(ctx.Db, a.Race, b.Race) < EnvoyTuning.MarriageMinAffinity) return 0f;
                if (EnvoyUtil.Trait(ctx.Db, a, AITrait.Xenophobia) > EnvoyTuning.MarriageMaxXenophobia
                    || EnvoyUtil.Trait(ctx.Db, b, AITrait.Xenophobia) > EnvoyTuning.MarriageMaxXenophobia) return 0f;
            }
            float dip = 0.5f * (EnvoyUtil.Trait(ctx.Db, a, AITrait.Diplomacy) + EnvoyUtil.Trait(ctx.Db, b, AITrait.Diplomacy));
            if (dip < EnvoyTuning.MarriageMinDiplomacy) return 0f;
            return MathUtil.Clamp01(dip * (r.Stance == DiplomaticStance.Allied ? 1.25f : 1f));
        }

        /// <summary>Unites the two houses if eligible. Returns true on a marriage.</summary>
        public bool Marry(SimContext ctx, Civilization a, Civilization b)
        {
            if (Willingness(ctx, a, b) <= 0f) return false;
            var st = ctx.State;
            var r = EnvoyUtil.RelationOf(st, a.Id, b.Id);
            st.Memory[_keys.Marriage(a.Id, b.Id)] = st.Tick;
            EnvoyUtil.AddOpinion(r, EnvoyTuning.MarriageImpulse);
            EnvoyUtil.MarkCooldown(a, EnvoyKeys.MarriageCooldown, st.Tick);
            EnvoyUtil.MarkCooldown(b, EnvoyKeys.MarriageCooldown, st.Tick);
            var ca = EnvoyUtil.Capital(st, a);
            var cb = EnvoyUtil.Capital(st, b);
            string houses = ca != null && cb != null ? ", uniting the royal houses of " + ca.Name + " and " + cb.Name : ", uniting their royal houses";
            EnvoyUtil.Chronicle(ctx, a, "A royal wedding: the heirs of " + EnvoyUtil.LeaderName(st, ctx.Db, a) + " of the " + a.Name + " and "
                + EnvoyUtil.LeaderName(st, ctx.Db, b) + " of the " + b.Name + " were married" + houses + ".", 2);
            return true;
        }
    }
}
