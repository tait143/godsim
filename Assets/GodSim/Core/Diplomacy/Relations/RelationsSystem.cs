using System;
using System.Collections.Generic;

namespace GodSim.Core.Diplomacy
{
    /// <summary>
    /// Opinion drift, stances (with hysteresis) and trade pacts for every relation between two alive civs
    /// in contact.
    /// <para>Each run the opinion closes <see cref="RelationsTuning.OpinionDriftPerRun"/> of the gap to a
    /// target built from race affinity, trade, shared enemies, shared view of the god, border friction,
    /// xenophobia, war memory and the races' diplomacy personality (see <see cref="ComputeTargetOpinion(WorldState,GameDatabase,Relation)"/>).</para>
    /// <para>Stances follow <see cref="NextStance"/> (never while at war; a war forces <see cref="DiplomaticStance.War"/>).
    /// Every stance change publishes a <see cref="StanceChangedEvent"/>; alliances and trade pacts are chronicled.</para>
    /// </summary>
    public sealed class RelationsSystem : ISimSystem
    {
        private readonly RelationsSettlementIndex _index = new RelationsSettlementIndex();
        private readonly List<Relation> _relations = new List<Relation>();
        private readonly List<War> _activeWars = new List<War>();
        private readonly RelationsKeyCache _keys = new RelationsKeyCache();

        /// <summary>Creates the system.</summary>
        public RelationsSystem() { }

        /// <inheritdoc />
        public string Name => "Diplomacy.Relations";

        /// <inheritdoc />
        public int TickInterval => 24;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            var state = ctx.State;
            _index.Rebuild(state);
            DiplomacyQueries.Snapshot(state, _relations);
            CollectActiveWars(state, _activeWars);
            float alpha = DriftFactor(ctx.DeltaTicks);

            for (int i = 0; i < _relations.Count; i++)
            {
                var r = _relations[i];
                if (!r.HasContact) continue;
                var a = state.Civilizations.Get(r.CivA);
                var b = state.Civilizations.Get(r.CivB);
                if (!DiplomacyQueries.IsAlive(a) || !DiplomacyQueries.IsAlive(b)) continue;
                var ra = DiplomacyQueries.RaceOf(ctx.Db, a.Race);
                var rb = DiplomacyQueries.RaceOf(ctx.Db, b.Race);
                if (ra == null || rb == null) continue;

                int pairs = _index.CountPairsWithin(a.Id, b.Id, RelationsTuning.BorderRange);
                bool sharedEnemy = DiplomacyQueries.ShareEnemy(_activeWars, a.Id, b.Id);
                long lastWar = state.Memory.TryGetValue(_keys.LastWar(a.Id, b.Id), out float lw) ? DiplomacyQueries.ToTick(lw) : -1;
                float target = ComputeTarget(state.Tick, r, a, b, ra, rb, pairs, sharedEnemy, lastWar);

                float opinion = MathUtil.Sanitize(r.Opinion);
                opinion += (target - opinion) * alpha;
                r.Opinion = MathUtil.Clamp(MathUtil.Sanitize(opinion), RelationsTuning.MinOpinion, RelationsTuning.MaxOpinion);

                UpdateTrade(ctx, r, a, b, ra, rb);
                UpdateStance(ctx, r, a, b, ra, rb);
            }
        }

        // ------------------------------------------------------------------ public rules (pure, testable)

        /// <summary>
        /// Stance after applying the opinion thresholds with hysteresis (for relations not at war):
        /// Allied when opinion &gt;= 60 and <paramref name="canAlly"/>, kept until it drops below 45;
        /// Friendly when &gt;= 25 (an alliance or friendship is kept as Friendly until it drops below 15);
        /// Neutral when &gt; -25; Hostile otherwise. A stale War stance is treated as Hostile.
        /// </summary>
        /// <param name="current">Current stance.</param>
        /// <param name="opinion">Current opinion.</param>
        /// <param name="canAlly">Both races' Diplomacy personality exceeds <see cref="RelationsTuning.AllyMinDiplomacy"/>.</param>
        public static DiplomaticStance NextStance(DiplomaticStance current, float opinion, bool canAlly)
        {
            opinion = MathUtil.Sanitize(opinion);
            if (canAlly && (opinion >= RelationsTuning.AllyEnter || (current == DiplomaticStance.Allied && opinion >= RelationsTuning.AllyLeave)))
                return DiplomaticStance.Allied;
            bool wasFriendly = current == DiplomaticStance.Friendly || current == DiplomaticStance.Allied;
            if (opinion >= RelationsTuning.FriendlyEnter || (wasFriendly && opinion >= RelationsTuning.FriendlyLeave))
                return DiplomaticStance.Friendly;
            if (opinion > RelationsTuning.NeutralFloor) return DiplomaticStance.Neutral;
            return DiplomaticStance.Hostile;
        }

        /// <summary>
        /// The opinion a relation currently drifts toward (-100..100). Returns the current opinion when
        /// either civ is missing/dead or a race is not registered. Useful for UI ("why do they hate us").
        /// </summary>
        public static float ComputeTargetOpinion(WorldState state, GameDatabase db, Relation r)
        {
            if (state == null || db == null || r == null) return 0f;
            var a = state.Civilizations.Get(r.CivA);
            var b = state.Civilizations.Get(r.CivB);
            if (!DiplomacyQueries.IsAlive(a) || !DiplomacyQueries.IsAlive(b)) return MathUtil.Sanitize(r.Opinion);
            var ra = DiplomacyQueries.RaceOf(db, a.Race);
            var rb = DiplomacyQueries.RaceOf(db, b.Race);
            if (ra == null || rb == null) return MathUtil.Sanitize(r.Opinion);
            int pairs = DiplomacyQueries.BorderPairs(state, a.Id, b.Id, RelationsTuning.BorderRange);
            bool sharedEnemy = DiplomacyQueries.ShareEnemy(state, a.Id, b.Id);
            long lastWar = DiplomacyQueries.LastWarEndTick(state, a.Id, b.Id);
            return ComputeTarget(state.Tick, r, a, b, ra, rb, pairs, sharedEnemy, lastWar);
        }

        /// <summary>Border friction (0..30) for a number of settlement pairs within <see cref="RelationsTuning.BorderRange"/>.</summary>
        public static float BorderFriction(int closePairs) =>
            closePairs <= 0 ? 0f : Math.Min(RelationsTuning.MaxBorderFriction, closePairs * RelationsTuning.BorderFrictionPerPair);

        // ------------------------------------------------------------------ internals

        internal static float ComputeTarget(long tick, Relation r, Civilization a, Civilization b, RaceDefinition ra, RaceDefinition rb,
            int borderPairs, bool sharedEnemy, long lastWarEndTick)
        {
            float target = RelationsTuning.AffinityWeight * DiplomacyQueries.MutualAffinity(ra, rb);
            if (r.TradeAgreement) target += RelationsTuning.TradeBonus;
            if (sharedEnemy) target += RelationsTuning.SharedEnemyBonus;
            if (DiplomacyQueries.FavorSignsMatch(a.GodFavor, b.GodFavor)) target += RelationsTuning.SharedFavorBonus;
            target -= BorderFriction(borderPairs);
            if (a.Race != b.Race)
                target -= RelationsTuning.XenophobiaWeight * 0.5f * (DiplomacyQueries.Trait(ra.Xenophobia) + DiplomacyQueries.Trait(rb.Xenophobia));
            bool recentWar = r.AtWar || (lastWarEndTick >= 0 && tick - lastWarEndTick < SimTime.YearsToTicks(RelationsTuning.RecentWarYears));
            if (recentWar) target -= RelationsTuning.RecentWarPenalty;
            target += RelationsTuning.DiplomacyWeight * 0.5f * (DiplomacyQueries.Trait(ra.Diplomacy) + DiplomacyQueries.Trait(rb.Diplomacy));
            return MathUtil.Clamp(MathUtil.Sanitize(target), RelationsTuning.MinOpinion, RelationsTuning.MaxOpinion);
        }

        /// <summary>Fraction of the gap closed for a run of deltaTicks (exactly 10% for the 24-tick reference).</summary>
        internal static float DriftFactor(int deltaTicks)
        {
            if (deltaTicks <= 0) return 0f;
            if (deltaTicks == RelationsTuning.OpinionDriftReferenceTicks) return RelationsTuning.OpinionDriftPerRun;
            double keep = Math.Pow(1.0 - RelationsTuning.OpinionDriftPerRun, deltaTicks / (double)RelationsTuning.OpinionDriftReferenceTicks);
            return MathUtil.Clamp01((float)(1.0 - keep));
        }

        internal static void CollectActiveWars(WorldState state, List<War> into)
        {
            into.Clear();
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++) if (wars[i] != null && wars[i].IsActive) into.Add(wars[i]);
        }

        private static void UpdateTrade(SimContext ctx, Relation r, Civilization a, Civilization b, RaceDefinition ra, RaceDefinition rb)
        {
            bool want = r.TradeAgreement;
            if (r.AtWar || r.Opinion < RelationsTuning.TradeOff) want = false;
            else if (!r.TradeAgreement && r.Opinion >= RelationsTuning.TradeOn
                     && 0.5f * (DiplomacyQueries.Trait(ra.Mercantilism) + DiplomacyQueries.Trait(rb.Mercantilism)) >= RelationsTuning.TradeMinMercantilism)
                want = true;
            if (want == r.TradeAgreement) return;
            r.TradeAgreement = want;
            if (ctx.Ops == null) return;
            ctx.Ops.AddChronicle(ChronicleCategory.Diplomacy,
                want ? "The " + a.Name + " and the " + b.Name + " signed a trade pact."
                     : "The trade pact between the " + a.Name + " and the " + b.Name + " lapsed.",
                a.Id, -1, Vec2i.Invalid, 1);
        }

        private static void UpdateStance(SimContext ctx, Relation r, Civilization a, Civilization b, RaceDefinition ra, RaceDefinition rb)
        {
            DiplomaticStance next;
            if (r.AtWar) next = DiplomaticStance.War;
            else
            {
                bool canAlly = DiplomacyQueries.Trait(ra.Diplomacy) > RelationsTuning.AllyMinDiplomacy
                               && DiplomacyQueries.Trait(rb.Diplomacy) > RelationsTuning.AllyMinDiplomacy;
                next = NextStance(r.Stance, r.Opinion, canAlly);
            }
            if (next == r.Stance) return;
            var old = r.Stance;
            r.Stance = next;
            ctx.Events?.Publish(new StanceChangedEvent { CivA = r.CivA, CivB = r.CivB, OldStance = old, NewStance = next });
            if (ctx.Ops == null) return;
            if (next == DiplomaticStance.Allied)
                ctx.Ops.AddChronicle(ChronicleCategory.Diplomacy, "The " + a.Name + " and the " + b.Name + " forged an alliance.", a.Id, -1, Vec2i.Invalid, 1);
            else if (old == DiplomaticStance.Allied)
                ctx.Ops.AddChronicle(ChronicleCategory.Diplomacy, "The alliance between the " + a.Name + " and the " + b.Name + " dissolved.", a.Id, -1, Vec2i.Invalid, 1);
        }
    }
}
