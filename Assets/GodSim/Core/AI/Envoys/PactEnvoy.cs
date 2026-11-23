using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// NON-AGGRESSION PACTS &amp; BETRAYAL. Friendly civs with no grievances (no betrayal either way, no war in the
    /// last <see cref="EnvoyTuning.WarGrievanceYears"/> years) swear a pact for <see cref="EnvoyTuning.PactYears"/>
    /// years: the truce is extended to the pact's end and <c>state.Memory["env.pact.lo.hi"]</c> remembers it. Pacts are
    /// renewed quietly while the friendship lasts.
    /// <para>A war declaration while a pact holds (<see cref="OnWarDeclared"/>) is a betrayal: it is chronicled,
    /// <c>state.Memory["env.betray.&lt;betrayer&gt;.&lt;betrayed&gt;"]</c> is set, the betrayed and every friend of theirs
    /// take an immediate opinion shock toward the betrayer, and <see cref="ApplyGrudges"/> keeps a fading penalty
    /// alive for <see cref="EnvoyTuning.BetrayalYears"/> years.</para>
    /// </summary>
    public sealed class PactEnvoy
    {
        private readonly EnvoyKeyCache _keys = new EnvoyKeyCache();

        /// <summary>True if a pact between the two civs is in force at <paramref name="tick"/>.</summary>
        public bool HasPact(WorldState state, int a, int b, long tick)
        {
            if (state == null || a == b) return false;
            return EnvoyUtil.ReadTick(state.Memory, _keys.Pact(a, b)) > tick;
        }

        /// <summary>True if either civ betrayed the other within memory or they fought recently.</summary>
        public bool HasGrievance(WorldState state, int a, int b, long tick)
        {
            if (state == null) return true;
            long years = SimTime.YearsToTicks(EnvoyTuning.BetrayalYears);
            long ab = EnvoyUtil.ReadTick(state.Memory, _keys.Betrayal(a, b));
            long ba = EnvoyUtil.ReadTick(state.Memory, _keys.Betrayal(b, a));
            if ((ab >= 0 && tick - ab < years) || (ba >= 0 && tick - ba < years)) return true;
            long war = EnvoyUtil.ReadTick(state.Memory, _keys.LastWar(a, b));
            return war >= 0 && tick - war < SimTime.YearsToTicks(EnvoyTuning.WarGrievanceYears);
        }

        /// <summary>Pact willingness of a civ (0..1); 0 when it is too aggressive or undiplomatic to bind itself.</summary>
        public static float Willingness(GameDatabase db, Civilization c)
        {
            float dip = EnvoyUtil.Trait(db, c, AITrait.Diplomacy);
            float agg = EnvoyUtil.Trait(db, c, AITrait.Aggression);
            if (dip < EnvoyTuning.PactMinDiplomacy || agg > EnvoyTuning.PactMaxAggression) return 0f;
            return MathUtil.Clamp01(0.7f * dip + 0.3f * (1f - agg));
        }

        /// <summary>True if the two civs could sign (or renew) a pact right now.</summary>
        public bool CanSign(SimContext ctx, Civilization a, Civilization b)
        {
            if (ctx == null || !EnvoyUtil.IsAlive(a) || !EnvoyUtil.IsAlive(b) || a == b) return false;
            var r = EnvoyUtil.RelationOf(ctx.State, a.Id, b.Id);
            if (r == null || !r.HasContact || !EnvoyUtil.IsFriendly(r) || EnvoyUtil.Opinion(r) < EnvoyTuning.PactMinOpinion) return false;
            if (HasGrievance(ctx.State, a.Id, b.Id, ctx.Tick)) return false;
            return Willingness(ctx.Db, a) > 0f && Willingness(ctx.Db, b) > 0f;
        }

        /// <summary>
        /// Signs a new pact or renews one about to expire. Returns true if a pact was signed or renewed.
        /// Only new pacts are chronicled and give an opinion impulse.
        /// </summary>
        public bool Sign(SimContext ctx, Civilization a, Civilization b)
        {
            if (!CanSign(ctx, a, b)) return false;
            var state = ctx.State;
            long tick = state.Tick;
            string key = _keys.Pact(a.Id, b.Id);
            long until = EnvoyUtil.ReadTick(state.Memory, key);
            bool active = until > tick;
            if (active && until - tick > SimTime.YearsToTicks(EnvoyTuning.PactRenewYears)) return false;
            long newUntil = tick + SimTime.YearsToTicks(EnvoyTuning.PactYears);
            var r = EnvoyUtil.RelationOf(state, a.Id, b.Id);
            r.TruceUntilTick = Math.Max(r.TruceUntilTick, newUntil);
            state.Memory[key] = newUntil;
            EnvoyUtil.MarkCooldown(a, EnvoyKeys.PactCooldown, tick);
            EnvoyUtil.MarkCooldown(b, EnvoyKeys.PactCooldown, tick);
            if (!active)
            {
                EnvoyUtil.AddOpinion(r, EnvoyTuning.PactImpulse);
                EnvoyUtil.Chronicle(ctx, a, "The " + a.Name + " and the " + b.Name + " swore a ten-year pact of non-aggression.", 1);
            }
            return true;
        }

        /// <summary>Called for every war declaration. Returns true if it broke a pact (a betrayal).</summary>
        public bool OnWarDeclared(SimContext ctx, int attackerId, int defenderId)
        {
            if (ctx == null || ctx.State == null || attackerId == defenderId || attackerId < 0 || defenderId < 0) return false;
            var state = ctx.State;
            long tick = state.Tick;
            string pactKey = _keys.Pact(attackerId, defenderId);
            if (EnvoyUtil.ReadTick(state.Memory, pactKey) <= tick) { state.Memory.Remove(pactKey); return false; }
            state.Memory.Remove(pactKey);
            state.Memory[_keys.Betrayal(attackerId, defenderId)] = tick;

            var betrayer = state.Civilizations.Get(attackerId);
            var victim = state.Civilizations.Get(defenderId);
            EnvoyUtil.AddOpinion(EnvoyUtil.RelationOf(state, attackerId, defenderId), EnvoyTuning.BetrayalShock);
            PunishWithFriends(state, attackerId, defenderId, EnvoyTuning.BetrayalFriendShock);
            if (EnvoyUtil.IsAlive(betrayer))
            {
                betrayer.Memory[EnvoyKeys.PactCooldown] = tick;
                bool kin = state.Memory.ContainsKey(_keys.Marriage(attackerId, defenderId));
                string vn = victim != null ? victim.Name : "their neighbours";
                EnvoyUtil.Chronicle(ctx, betrayer, "The " + betrayer.Name + " broke their sacred pact and fell upon the " + vn
                    + (kin ? ", their own kin by marriage" : "") + ". Such treachery will not be forgotten.", 2);
            }
            return true;
        }

        /// <summary>
        /// Applies the lasting, fading penalty of every remembered betrayal (betrayed and all their friends toward the
        /// betrayer) and forgets betrayals older than <see cref="EnvoyTuning.BetrayalYears"/>. Iterates civ pairs by index.
        /// </summary>
        public void ApplyGrudges(SimContext ctx)
        {
            if (ctx == null || ctx.State == null) return;
            var state = ctx.State;
            long tick = state.Tick;
            long span = Math.Max(1, SimTime.YearsToTicks(EnvoyTuning.BetrayalYears));
            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var betrayer = civs[i];
                for (int j = 0; j < civs.Count; j++)
                {
                    if (i == j) continue;
                    var victim = civs[j];
                    string key = _keys.Betrayal(betrayer.Id, victim.Id);
                    long when = EnvoyUtil.ReadTick(state.Memory, key);
                    if (when < 0) continue;
                    long age = tick - when;
                    if (age >= span || !EnvoyUtil.IsAlive(betrayer)) { state.Memory.Remove(key); continue; }
                    if (age <= 0 || !EnvoyUtil.IsAlive(victim)) continue; // shock already applied this tick
                    float fade = 1f - age / (float)span;
                    EnvoyUtil.AddOpinion(EnvoyUtil.RelationOf(state, betrayer.Id, victim.Id), EnvoyTuning.BetrayalGrudge * fade);
                    PunishWithFriends(state, betrayer.Id, victim.Id, EnvoyTuning.BetrayalFriendGrudge * fade);
                }
            }
        }

        /// <summary>Every civ friendly with the victim (not the betrayer) sours on the betrayer.</summary>
        private static void PunishWithFriends(WorldState state, int betrayerId, int victimId, float impulse)
        {
            var civs = state.Civilizations.Items;
            for (int k = 0; k < civs.Count; k++)
            {
                var f = civs[k];
                if (f.Id == betrayerId || f.Id == victimId || !EnvoyUtil.IsAlive(f)) continue;
                if (!EnvoyUtil.IsFriendly(EnvoyUtil.RelationOf(state, f.Id, victimId))) continue;
                EnvoyUtil.AddOpinion(EnvoyUtil.RelationOf(state, f.Id, betrayerId), impulse);
            }
        }
    }
}
