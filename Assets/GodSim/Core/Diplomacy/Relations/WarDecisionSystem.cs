using System;
using System.Collections.Generic;

namespace GodSim.Core.Diplomacy
{
    /// <summary>
    /// Decides when wars start and end. Battles, sieges and conquest belong to the military module, which
    /// updates <see cref="War.WarScore"/>, losses and <see cref="Civilization.MilitaryPower"/>; this system
    /// only reads those.
    /// <para>Each run: (1) repairs relations pointing at missing/ended wars; (2) ends active wars on collapse
    /// of a side, a decisive score (with tribute), exhaustion, the duration cap or a stalemate; (3) prunes old
    /// inactive wars; (4) if <see cref="SimConfig.EnableWarfare"/>, lets aggressive civs declare war, and
    /// calls the defender's allies to arms.</para>
    /// </summary>
    public sealed class WarDecisionSystem : ISimSystem
    {
        private readonly RelationsSettlementIndex _index = new RelationsSettlementIndex();
        private readonly List<Relation> _relations = new List<Relation>();
        private readonly RelationsKeyCache _keys = new RelationsKeyCache();

        /// <summary>Creates the system.</summary>
        public WarDecisionSystem() { }

        /// <inheritdoc />
        public string Name => "Diplomacy.WarDecision";

        /// <inheritdoc />
        public int TickInterval => 48;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            var state = ctx.State;
            _index.Rebuild(state);
            DiplomacyQueries.Snapshot(state, _relations);
            RepairRelations(ctx);
            ProcessPeace(ctx);
            PruneWars(state);
            if (ctx.Config == null || ctx.Config.EnableWarfare) ProcessDeclarations(ctx);
        }

        // ================================================================== consistency

        private void RepairRelations(SimContext ctx)
        {
            var dip = ctx.State.Diplomacy;
            for (int i = 0; i < _relations.Count; i++)
            {
                var r = _relations[i];
                if (r.WarId >= 0)
                {
                    var w = dip.GetWar(r.WarId);
                    if (w != null && w.IsActive)
                    {
                        if (r.Stance != DiplomaticStance.War) SetStance(ctx, r, DiplomaticStance.War);
                        r.TradeAgreement = false;
                        continue;
                    }
                    r.WarId = -1;
                }
                if (r.Stance == DiplomaticStance.War) SetStance(ctx, r, DiplomaticStance.Hostile);
            }
        }

        // ================================================================== peace

        private enum PeaceReason { Collapse, Decisive, Exhaustion, Duration, Stalemate }

        private void ProcessPeace(SimContext ctx)
        {
            var state = ctx.State;
            var wars = state.Diplomacy.Wars;
            long tick = state.Tick;
            int dt = Math.Max(1, ctx.DeltaTicks);
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null) continue;
                if (!w.IsActive)
                {
                    // Ended elsewhere (e.g. WorldOps.CollapseCivilization) since our last run: remember it.
                    if (w.EndTick >= 0 && w.EndTick >= tick - dt) RememberEndedWar(state, w);
                    continue;
                }
                var att = state.Civilizations.Get(w.AttackerId);
                var def = state.Civilizations.Get(w.DefenderId);
                bool attGone = !DiplomacyQueries.IsAlive(att) || _index.SettlementCount(att.Id) == 0;
                bool defGone = !DiplomacyQueries.IsAlive(def) || _index.SettlementCount(def.Id) == 0;
                if (attGone || defGone) { EndWar(ctx, w, att, def, PeaceReason.Collapse); continue; }

                float score = MathUtil.Clamp(MathUtil.Sanitize(w.WarScore), -100f, 100f);
                long duration = tick - w.StartTick;
                float initialPower = InitialPower(state, w, att, def);
                float losses = Math.Max(0, w.AttackerLosses) + (float)Math.Max(0, w.DefenderLosses);

                if (Math.Abs(score) >= RelationsTuning.DecisiveScore) EndWar(ctx, w, att, def, PeaceReason.Decisive);
                else if (losses > RelationsTuning.ExhaustionLossFraction * initialPower) EndWar(ctx, w, att, def, PeaceReason.Exhaustion);
                else if (duration > SimTime.YearsToTicks(RelationsTuning.MaxWarYears)) EndWar(ctx, w, att, def, PeaceReason.Duration);
                else if (duration > SimTime.YearsToTicks(RelationsTuning.StalemateYears) && Math.Abs(score) < RelationsTuning.StalemateScore
                         && ctx.Rng.Chance(Rng.PerYearToPerTicks(RelationsTuning.StalematePeaceChancePerYear, dt)))
                    EndWar(ctx, w, att, def, PeaceReason.Stalemate);
            }
        }

        private float InitialPower(WorldState state, War w, Civilization att, Civilization def)
        {
            string key = _keys.InitialPower(w.Id);
            if (state.Memory.TryGetValue(key, out float p) && !float.IsNaN(p) && !float.IsInfinity(p))
                return Math.Max(RelationsTuning.MinInitialPower, p);
            // War created elsewhere (or an old save): start tracking from now.
            p = CombinedPower(att, def);
            state.Memory[key] = p;
            return p;
        }

        private static float CombinedPower(Civilization att, Civilization def)
        {
            float a = att != null ? Math.Max(0f, MathUtil.Sanitize(att.MilitaryPower)) : 0f;
            float d = def != null ? Math.Max(0f, MathUtil.Sanitize(def.MilitaryPower)) : 0f;
            return Math.Max(RelationsTuning.MinInitialPower, MathUtil.Sanitize(a + d, RelationsTuning.MinInitialPower));
        }

        private void EndWar(SimContext ctx, War w, Civilization att, Civilization def, PeaceReason reason)
        {
            var state = ctx.State;
            long tick = state.Tick;
            w.IsActive = false;
            w.EndTick = tick;

            string tributeText = "";
            if (reason == PeaceReason.Decisive)
            {
                bool attackerWon = w.WarScore > 0f;
                var winner = attackerWon ? att : def;
                var loser = attackerWon ? def : att;
                float paid = PayTribute(state, winner, loser);
                if (paid > 0.01f) tributeText = " and paid " + MathUtil.RoundToInt(paid) + " gold in tribute";
            }

            if (state.Diplomacy.TryGet(w.AttackerId, w.DefenderId, out var r))
            {
                if (r.WarId == w.Id || r.WarId < 0)
                {
                    r.WarId = -1;
                    r.TradeAgreement = false;
                    r.TruceUntilTick = Math.Max(r.TruceUntilTick, tick + SimTime.YearsToTicks(RelationsTuning.TruceYears));
                    SetStance(ctx, r, DiplomaticStance.Hostile);
                }
            }
            RememberEndedWar(state, w);

            ctx.Events?.Publish(new PeaceSignedEvent { WarId = w.Id, AttackerId = w.AttackerId, DefenderId = w.DefenderId, WarScore = w.WarScore });
            if (ctx.Ops != null)
            {
                string an = att != null ? att.Name : "a forgotten people";
                string dn = def != null ? def.Name : "a forgotten people";
                string text;
                switch (reason)
                {
                    case PeaceReason.Collapse:
                        bool attGone = !DiplomacyQueries.IsAlive(att) || _index.SettlementCount(att.Id) == 0;
                        text = "The war between the " + an + " and the " + dn + " ended with the fall of the " + (attGone ? an : dn) + ".";
                        break;
                    case PeaceReason.Decisive:
                        bool attackerWon = w.WarScore > 0f;
                        text = "The " + (attackerWon ? dn : an) + " sued the " + (attackerWon ? an : dn) + " for peace" + tributeText + ".";
                        break;
                    case PeaceReason.Exhaustion:
                        text = "Exhausted by their losses, the " + an + " and the " + dn + " made peace.";
                        break;
                    case PeaceReason.Duration:
                        text = "After decades of fighting, the " + an + " and the " + dn + " laid down their arms.";
                        break;
                    default:
                        text = "The war between the " + an + " and the " + dn + " ended in stalemate.";
                        break;
                }
                ctx.Ops.AddChronicle(ChronicleCategory.War, text, w.AttackerId, -1, Vec2i.Invalid, 2);
            }
        }

        /// <summary>Moves 30% of the loser capital's gold into the winner capital (only what fits; never destroys gold).</summary>
        private static float PayTribute(WorldState state, Civilization winner, Civilization loser)
        {
            if (winner == null || loser == null) return 0f;
            var to = state.Settlements.Get(winner.CapitalId);
            var from = state.Settlements.Get(loser.CapitalId);
            if (to == null || from == null || to == from || !to.IsAlive || !from.IsAlive) return 0f;
            float amount = from.Stockpile.Get(ResourceType.Gold) * RelationsTuning.TributeFraction;
            amount = Math.Min(amount, to.Stockpile.FreeSpace(ResourceType.Gold));
            amount = MathUtil.Sanitize(amount);
            if (amount <= 0f) return 0f;
            float taken = from.Stockpile.ConsumeUpTo(ResourceType.Gold, amount);
            return to.Stockpile.Add(ResourceType.Gold, taken);
        }

        private void RememberEndedWar(WorldState state, War w)
        {
            if (w.AttackerId == w.DefenderId) return;
            long end = w.EndTick >= 0 ? w.EndTick : state.Tick;
            string key = _keys.LastWar(w.AttackerId, w.DefenderId);
            if (!state.Memory.TryGetValue(key, out float prev) || DiplomacyQueries.ToTick(prev) < end) state.Memory[key] = end;
            state.Memory.Remove(_keys.InitialPower(w.Id));
        }

        private void PruneWars(WorldState state)
        {
            var wars = state.Diplomacy.Wars;
            int inactive = 0;
            for (int i = 0; i < wars.Count; i++) if (wars[i] == null || !wars[i].IsActive) inactive++;
            while (inactive > RelationsTuning.MaxInactiveWars)
            {
                int oldest = -1;
                for (int i = 0; i < wars.Count; i++)
                {
                    var w = wars[i];
                    if (w == null) { oldest = i; break; }
                    if (w.IsActive) continue;
                    if (oldest < 0) { oldest = i; continue; }
                    var o = wars[oldest];
                    if (w.EndTick < o.EndTick || (w.EndTick == o.EndTick && w.Id < o.Id)) oldest = i;
                }
                if (oldest < 0) break;
                if (wars[oldest] != null) state.Memory.Remove(_keys.InitialPower(wars[oldest].Id));
                wars.RemoveAt(oldest);
                inactive--;
            }
        }

        // ================================================================== declarations

        private void ProcessDeclarations(SimContext ctx)
        {
            var state = ctx.State;
            long tick = state.Tick;
            for (int i = 0; i < _relations.Count; i++)
            {
                var r = _relations[i];
                if (!r.HasContact || r.AtWar || r.TruceUntilTick > tick || r.Stance == DiplomaticStance.Allied) continue;
                var a = state.Civilizations.Get(r.CivA);
                var b = state.Civilizations.Get(r.CivB);
                if (!CanWage(a) || !CanWage(b)) continue;
                if (TryDeclare(ctx, r, a, b)) continue;
                TryDeclare(ctx, r, b, a);
            }
        }

        private bool CanWage(Civilization c) =>
            DiplomacyQueries.IsAlive(c) && c.Age >= AgeId.Bronze && _index.SettlementCount(c.Id) > 0;

        private bool TryDeclare(SimContext ctx, Relation r, Civilization aggressor, Civilization target)
        {
            var state = ctx.State;
            var race = DiplomacyQueries.RaceOf(ctx.Db, aggressor.Race);
            if (race == null) return false;
            float aggression = DiplomacyQueries.Trait(race.Aggression);
            if (aggression <= 0f) return false;

            float opinion = MathUtil.Sanitize(r.Opinion);
            bool hatred = opinion < RelationsTuning.HatredOpinion && aggression > RelationsTuning.HatredMinAggression;
            bool opportunism = aggression > RelationsTuning.OpportunistMinAggression && opinion < RelationsTuning.OpportunistOpinion
                               && DiplomacyQueries.StrengthRatio(aggressor, target) > RelationsTuning.OpportunistStrengthRatio;
            if (!hatred && !opportunism) return false;
            if (DiplomacyQueries.ActiveWarCount(state, aggressor.Id) >= RelationsTuning.MaxActiveWarsToDeclare) return false;

            float chance = Rng.PerYearToPerTicks(RelationsTuning.DeclarationChancePerYear * aggression, Math.Max(1, ctx.DeltaTicks));
            if (!ctx.Rng.Chance(chance)) return false;

            DeclareWar(ctx, r, aggressor, target, ChooseCasusBelli(state, aggressor, target));
            CallAlliesToArms(ctx, aggressor, target);
            return true;
        }

        private string ChooseCasusBelli(WorldState state, Civilization aggressor, Civilization target)
        {
            if (DiplomacyQueries.FavorSignsDiffer(aggressor.GodFavor, target.GodFavor)) return RelationsTuning.CasusHolyWar;
            if (_index.CountPairsWithin(aggressor.Id, target.Id, RelationsTuning.BorderRange) > 0) return RelationsTuning.CasusBorderDispute;
            if (state.Memory.ContainsKey(_keys.LastWar(aggressor.Id, target.Id))) return RelationsTuning.CasusOldGrudge;
            return RelationsTuning.CasusConquest;
        }

        /// <summary>
        /// Allies of the defender (Allied stance, in contact with the attacker, not allied with the attacker
        /// themselves) declare on the attacker. A call to arms honors the alliance over any truce with the attacker
        /// and over the ally's own war count; it does not cascade to the attacker's allies.
        /// </summary>
        private void CallAlliesToArms(SimContext ctx, Civilization attacker, Civilization defender)
        {
            var state = ctx.State;
            for (int i = 0; i < _relations.Count; i++)
            {
                var r = _relations[i];
                if (r.Stance != DiplomaticStance.Allied || r.AtWar) continue;
                int allyId;
                if (r.CivA == defender.Id) allyId = r.CivB;
                else if (r.CivB == defender.Id) allyId = r.CivA;
                else continue;
                if (allyId == attacker.Id) continue;
                var ally = state.Civilizations.Get(allyId);
                if (!DiplomacyQueries.IsAlive(ally) || _index.SettlementCount(ally.Id) == 0) continue;
                if (!state.Diplomacy.TryGet(ally.Id, attacker.Id, out var ra)) continue;
                if (!ra.HasContact || ra.AtWar || ra.Stance == DiplomaticStance.Allied) continue;
                DeclareWar(ctx, ra, ally, attacker, RelationsTuning.CasusCallToArms);
            }
        }

        private void DeclareWar(SimContext ctx, Relation r, Civilization attacker, Civilization defender, string casusBelli)
        {
            var state = ctx.State;
            var war = new War
            {
                Id = state.Ids.War(),
                AttackerId = attacker.Id,
                DefenderId = defender.Id,
                StartTick = state.Tick,
                CasusBelli = casusBelli ?? RelationsTuning.CasusConquest,
            };
            state.Diplomacy.Wars.Add(war);
            r.WarId = war.Id;
            r.TradeAgreement = false;
            state.Memory[_keys.InitialPower(war.Id)] = CombinedPower(attacker, defender);
            state.Stats.WarsDeclared++;
            ctx.Events?.Publish(new WarDeclaredEvent { WarId = war.Id, AttackerId = attacker.Id, DefenderId = defender.Id });
            SetStance(ctx, r, DiplomaticStance.War);
            if (ctx.Ops != null)
            {
                var cap = state.Settlements.Get(defender.CapitalId);
                ctx.Ops.AddChronicle(ChronicleCategory.War,
                    "The " + attacker.Name + " declared war on the " + defender.Name + " (" + war.CasusBelli + ").",
                    attacker.Id, cap != null ? cap.Id : -1, cap != null ? cap.Center : Vec2i.Invalid, 2);
            }
        }

        private static void SetStance(SimContext ctx, Relation r, DiplomaticStance stance)
        {
            if (r.Stance == stance) return;
            var old = r.Stance;
            r.Stance = stance;
            ctx.Events?.Publish(new StanceChangedEvent { CivA = r.CivA, CivB = r.CivB, OldStance = old, NewStance = stance });
        }
    }
}
