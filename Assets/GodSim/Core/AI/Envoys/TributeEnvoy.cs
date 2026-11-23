using System;

namespace GodSim.Core.AI
{
    /// <summary>Outcome of a tribute decision.</summary>
    public enum TributeOutcome : byte { None, Paid, Refused }

    /// <summary>
    /// TRIBUTE: a weak civ in contact with a much stronger (<see cref="EnvoyTuning.TributeStrengthRatio"/>x), aggressive
    /// civ that it is on bad terms with buys peace: it pays gold (or food when its vault is empty) from capital to
    /// capital, gains opinion and extends the truce to <c>max(current, tick + 3 years)</c>. Proud races (Orcs,
    /// Dwarves) and very aggressive rulers refuse unless desperate (<see cref="EnvoyTuning.DesperateStrengthRatio"/>x),
    /// and their defiance is chronicled.
    /// </summary>
    public sealed class TributeEnvoy
    {
        /// <summary>True if this payer would rather refuse than bow to a bully this much stronger.</summary>
        public static bool Refuses(GameDatabase db, Civilization payer, float strengthRatio)
        {
            if (payer == null) return true;
            bool desperate = strengthRatio >= EnvoyTuning.DesperateStrengthRatio;
            if (desperate) return false;
            if (EnvoyUtil.IsProudRace(payer.Race)) return true;
            return EnvoyUtil.Trait(db, payer, AITrait.Aggression) > EnvoyTuning.TributeMaxPayerAggression;
        }

        /// <summary>The most threatening bully this civ should pay (or -1). Deterministic: largest ratio, lowest id on ties.</summary>
        public int ChooseBully(SimContext ctx, Civilization payer, out float ratio)
        {
            ratio = 0f;
            if (ctx == null || !EnvoyUtil.IsAlive(payer)) return -1;
            var state = ctx.State;
            long renewBefore = state.Tick + SimTime.YearsToTicks(EnvoyTuning.TributeRenewYears);
            var civs = state.Civilizations.Items;
            int best = -1;
            for (int i = 0; i < civs.Count; i++)
            {
                var bully = civs[i];
                if (bully == payer || !EnvoyUtil.IsAlive(bully)) continue;
                var r = EnvoyUtil.RelationOf(state, payer.Id, bully.Id);
                if (r == null || !r.HasContact || r.AtWar || r.Stance >= DiplomaticStance.Friendly) continue;
                if (EnvoyUtil.Opinion(r) >= 0f) continue;
                if (r.TruceUntilTick > renewBefore) continue; // peace already bought
                float rr = EnvoyUtil.StrengthRatio(bully, payer);
                if (rr < EnvoyTuning.TributeStrengthRatio) continue;
                if (EnvoyUtil.Trait(ctx.Db, bully, AITrait.Aggression) < EnvoyTuning.TributeMinBullyAggression) continue;
                if (rr <= ratio) continue;
                ratio = rr;
                best = bully.Id;
            }
            return best;
        }

        /// <summary>Pays (or refuses) tribute if a bully looms and the payer is off cooldown. No random roll.</summary>
        public TributeOutcome TryPay(SimContext ctx, Civilization payer)
        {
            if (ctx == null || !EnvoyUtil.IsAlive(payer)) return TributeOutcome.None;
            if (!EnvoyUtil.CooldownReady(payer, EnvoyKeys.TributeCooldown, EnvoyTuning.TributeCooldownYears, ctx.Tick)) return TributeOutcome.None;
            int bullyId = ChooseBully(ctx, payer, out float ratio);
            if (bullyId < 0) return TributeOutcome.None;
            var bully = ctx.State.Civilizations.Get(bullyId);
            if (Refuses(ctx.Db, payer, ratio))
            {
                EnvoyUtil.MarkCooldown(payer, EnvoyKeys.TributeCooldown, ctx.Tick);
                EnvoyUtil.Chronicle(ctx, payer, "The " + EnvoyUtil.PeopleOf(ctx.State, ctx.Db, payer) + " refused to bow before the "
                    + EnvoyUtil.Plural(ctx.Db, bully) + ", and sent their envoys home empty-handed.", 1);
                return TributeOutcome.Refused;
            }
            return Pay(ctx, payer, bully) > 0f ? TributeOutcome.Paid : TributeOutcome.None;
        }

        /// <summary>Moves the tribute, adds opinion, extends the truce and chronicles it. Returns the gold-equivalent paid.</summary>
        public float Pay(SimContext ctx, Civilization payer, Civilization bully)
        {
            if (ctx == null || !EnvoyUtil.IsAlive(payer) || !EnvoyUtil.IsAlive(bully) || payer == bully) return 0f;
            var state = ctx.State;
            var from = EnvoyUtil.Capital(state, payer);
            var to = EnvoyUtil.Capital(state, bully);
            var r = EnvoyUtil.RelationOf(state, payer.Id, bully.Id);
            if (from == null || to == null || r == null || r.AtWar) return 0f;

            // Gold first; grain makes up what the payer lacks or the bully's full vault cannot hold.
            float gold = from.Stockpile.Get(ResourceType.Gold);
            float wanted = Math.Max(EnvoyTuning.TributeMinGold * 0.5f, gold * EnvoyTuning.TributeShare);
            float goldPaid = gold >= EnvoyTuning.TributeMinGold ? EnvoyUtil.Transfer(from, to, ResourceType.Gold, wanted) : 0f;
            float grainPaid = 0f;
            if (goldPaid < wanted - 0.5f)
            {
                float food = from.Stockpile.Get(ResourceType.Food);
                float grain = food * EnvoyTuning.TributeShare;
                if (gold >= EnvoyTuning.TributeMinGold) grain = Math.Min(grain, (wanted - goldPaid) * EnvoyTuning.FoodPerGold);
                if (food >= EnvoyTuning.TributeMinFood) grainPaid = EnvoyUtil.Transfer(from, to, ResourceType.Food, grain);
            }
            float value = goldPaid + grainPaid / EnvoyTuning.FoodPerGold;
            if (!(value > 0.5f)) return 0f;
            string what;
            if (goldPaid >= 0.5f && grainPaid >= 0.5f) what = MathUtil.RoundToInt(goldPaid) + " gold and " + MathUtil.RoundToInt(grainPaid) + " sacks of grain";
            else if (goldPaid >= 0.5f) what = MathUtil.RoundToInt(goldPaid) + " gold";
            else what = MathUtil.RoundToInt(grainPaid) + " sacks of grain";

            float t = MathUtil.InverseLerp(EnvoyTuning.TributeMinGold * 0.5f, 25f, value);
            EnvoyUtil.AddOpinion(r, MathUtil.Lerp(EnvoyTuning.TributeMinImpulse, EnvoyTuning.TributeMaxImpulse, t));
            r.TruceUntilTick = Math.Max(r.TruceUntilTick, state.Tick + SimTime.YearsToTicks(EnvoyTuning.TributeTruceYears));
            EnvoyUtil.MarkCooldown(payer, EnvoyKeys.TributeCooldown, state.Tick);
            payer.Memory[EnvoyKeys.TributeTo] = bully.Id;
            EnvoyUtil.Chronicle(ctx, payer, "Fearing the " + EnvoyUtil.Plural(ctx.Db, bully) + ", the " + EnvoyUtil.PeopleOf(state, ctx.Db, payer)
                + " paid a tribute of " + what + " to " + to.Name + " to buy three years of peace.", 1);
            return value;
        }
    }
}
