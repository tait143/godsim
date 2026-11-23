using System;

namespace GodSim.Core.AI
{
    /// <summary>Why a gift was sent.</summary>
    public enum GiftMotive : byte { None, Befriend, Appease }

    /// <summary>
    /// GIFTS: a rich, diplomatic civ sends gold from its capital to the capital of a contacted civ it wants to
    /// befriend (opinion just short of friendship) or appease (a much stronger, cool neighbour). At most
    /// <see cref="EnvoyTuning.GiftMaxShare"/> of the stock is given; the opinion impulse grows with the gift
    /// (<see cref="EnvoyTuning.GiftMinImpulse"/>..<see cref="EnvoyTuning.GiftMaxImpulse"/>). Large gifts are chronicled.
    /// </summary>
    public sealed class GiftEnvoy
    {
        private readonly EnvoyKeyCache _keys = new EnvoyKeyCache();

        /// <summary>Gift propensity of a civ (0..1): mostly Diplomacy, partly Mercantilism, damped by Aggression.</summary>
        public static float Propensity(GameDatabase db, Civilization civ)
        {
            float dip = EnvoyUtil.Trait(db, civ, AITrait.Diplomacy);
            float merc = EnvoyUtil.Trait(db, civ, AITrait.Mercantilism);
            float agg = EnvoyUtil.Trait(db, civ, AITrait.Aggression);
            return MathUtil.Clamp01(0.55f * dip + 0.45f * merc - 0.25f * Math.Max(0f, agg - 0.5f));
        }

        /// <summary>Opinion impulse for a gift of the given size (8..20).</summary>
        public static float ImpulseFor(float amount)
        {
            float t = MathUtil.InverseLerp(EnvoyTuning.GiftMinAmount, EnvoyTuning.GiftGrandAmount, MathUtil.Sanitize(amount));
            return MathUtil.Lerp(EnvoyTuning.GiftMinImpulse, EnvoyTuning.GiftMaxImpulse, t);
        }

        /// <summary>True when the civ is rich and diplomatic enough to give, and off cooldown.</summary>
        public bool WantsToGive(SimContext ctx, Civilization giver)
        {
            if (ctx == null || !EnvoyUtil.IsAlive(giver)) return false;
            if (!EnvoyUtil.CooldownReady(giver, EnvoyKeys.GiftCooldown, EnvoyTuning.GiftCooldownYears, ctx.Tick)) return false;
            var cap = EnvoyUtil.Capital(ctx.State, giver);
            if (cap == null || cap.Stockpile.Get(ResourceType.Gold) < EnvoyTuning.GiftMinStock) return false;
            return Propensity(ctx.Db, giver) >= EnvoyTuning.GiftMinPropensity;
        }

        /// <summary>Best gift target (or -1) and the motive. Deterministic: highest score, lowest id on ties.</summary>
        public int ChooseTarget(SimContext ctx, Civilization giver, out GiftMotive motive)
        {
            motive = GiftMotive.None;
            if (ctx == null || !EnvoyUtil.IsAlive(giver)) return -1;
            var state = ctx.State;
            var civs = state.Civilizations.Items;
            float xeno = EnvoyUtil.Trait(ctx.Db, giver, AITrait.Xenophobia);
            int best = -1;
            float bestScore = 0f;
            for (int i = 0; i < civs.Count; i++)
            {
                var other = civs[i];
                if (other == giver || !EnvoyUtil.IsAlive(other)) continue;
                var r = EnvoyUtil.RelationOf(state, giver.Id, other.Id);
                if (r == null || !r.HasContact || r.AtWar) continue;
                if (EnvoyUtil.Capital(state, other) == null) continue;
                // Never court the one who betrayed you.
                if (state.Memory.ContainsKey(_keys.Betrayal(other.Id, giver.Id))) continue;
                float op = EnvoyUtil.Opinion(r);
                float ratio = EnvoyUtil.StrengthRatio(other, giver);
                float score = 0f;
                GiftMotive m = GiftMotive.None;
                if (ratio >= EnvoyTuning.AppeaseStrengthRatio && op < 10f && op > -80f)
                {
                    m = GiftMotive.Appease;
                    score = 1f + Math.Min(3f, ratio - 1f) + (10f - op) * 0.02f;
                }
                else if (op >= EnvoyTuning.BefriendMinOpinion && op < EnvoyTuning.BefriendMaxOpinion)
                {
                    m = GiftMotive.Befriend;
                    // Closest to the friendship threshold first; kin and coalition partners preferred.
                    score = 0.5f + (op - EnvoyTuning.BefriendMinOpinion) / 55f + 0.5f * EnvoyUtil.MutualAffinity(ctx.Db, giver.Race, other.Race);
                    if (other.Race != giver.Race) score -= 0.5f * xeno;
                    float coal = giver.Mem(EnvoyKeys.Coalition, -1f);
                    if (coal >= 0f && MathUtil.Approximately(coal, other.Mem(EnvoyKeys.Coalition, -2f))) score += 0.5f;
                }
                if (m == GiftMotive.None || score <= bestScore) continue;
                bestScore = score;
                best = other.Id;
                motive = m;
            }
            return best;
        }

        /// <summary>
        /// Sends a gift if the civ wants to (no random roll; the system gates that). Returns the gold moved (0 if none).
        /// </summary>
        public float TryGive(SimContext ctx, Civilization giver)
        {
            if (!WantsToGive(ctx, giver)) return 0f;
            int targetId = ChooseTarget(ctx, giver, out var motive);
            if (targetId < 0) return 0f;
            return Give(ctx, giver, ctx.State.Civilizations.Get(targetId), motive);
        }

        /// <summary>Moves the gold, applies the impulse, sets the cooldown and chronicles large gifts.</summary>
        public float Give(SimContext ctx, Civilization giver, Civilization receiver, GiftMotive motive)
        {
            if (ctx == null || !EnvoyUtil.IsAlive(giver) || !EnvoyUtil.IsAlive(receiver) || giver == receiver) return 0f;
            var state = ctx.State;
            var from = EnvoyUtil.Capital(state, giver);
            var to = EnvoyUtil.Capital(state, receiver);
            var r = EnvoyUtil.RelationOf(state, giver.Id, receiver.Id);
            if (from == null || to == null || r == null) return 0f;
            float amount = from.Stockpile.Get(ResourceType.Gold) * EnvoyTuning.GiftMaxShare;
            if (amount < EnvoyTuning.GiftMinAmount) return 0f;
            float moved = EnvoyUtil.Transfer(from, to, ResourceType.Gold, amount);
            if (moved < EnvoyTuning.GiftMinAmount * 0.5f)
            {
                // Receiver's vault is full: nothing worth sending; refund handled by Transfer, undo the partial move.
                if (moved > 0f) EnvoyUtil.Transfer(to, from, ResourceType.Gold, moved);
                return 0f;
            }
            EnvoyUtil.AddOpinion(r, ImpulseFor(moved));
            EnvoyUtil.MarkCooldown(giver, EnvoyKeys.GiftCooldown, state.Tick);
            if (moved >= EnvoyTuning.GiftChronicleAmount)
            {
                string court = receiver.Race != giver.Race
                    ? "the " + EnvoyUtil.Adjective(ctx.Db, receiver) + " court"
                    : "the court of " + to.Name;
                string tail = motive == GiftMotive.Appease ? ", hoping to stay their hand." : ".";
                EnvoyUtil.Chronicle(ctx, giver, "The " + EnvoyUtil.PeopleOf(state, ctx.Db, giver) + " sent a chest of gold to " + court + tail, 1);
            }
            else EnvoyUtil.Log(state, giver, "Envoys: sent " + MathUtil.RoundToInt(moved) + " gold to the " + receiver.Name + ".");
            return moved;
        }
    }
}
