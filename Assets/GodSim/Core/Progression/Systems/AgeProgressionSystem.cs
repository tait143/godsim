namespace GodSim.Core.Progression
{
    /// <summary>
    /// Advances civilizations through the ages. A civ below Ascension enters the next age when ALL of:
    /// TotalResearch ≥ ResearchThreshold; techs known from its current age ≥ RequiredTechsFromPreviousAge, capped at the
    /// techs of that age the civ knows or can still obtain (<see cref="TechQueries.TechsAvailableToCiv"/>);
    /// population ≥ MinPopulation; and alive settlements ≥ MinSettlements.
    /// At most one age per check. Writes <see cref="ProgressionKeys.AgeProgress"/> (0..1) for the UI.
    /// Modifiers are recomputed immediately after an advance (<see cref="ModifierSystem.RecomputeFor"/>), and
    /// <see cref="ProgressionKeys.Dirty"/> is set as a signal for other modules.
    /// </summary>
    public sealed class AgeProgressionSystem : ISimSystem
    {
        /// <inheritdoc />
        public string Name => "Progression.Ages";

        /// <summary>24 ticks (one season).</summary>
        public int TickInterval => 24;

        /// <summary>
        /// Never advances. On a world that has not ticked yet it publishes <see cref="ProgressionKeys.AgeProgress"/> for every
        /// alive civ. On a loaded world the saved value is kept, matching an uninterrupted run, and the next check refreshes it.
        /// </summary>
        public void Initialize(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            if (ctx.State.Tick != 0) return;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                civ.Memory[ProgressionKeys.AgeProgress] = TechQueries.ProgressToNextAge(civ, ctx.Db, ctx.State);
            }
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                var check = TechQueries.EvaluateNextAge(civ, ctx.Db, ctx.State);
                if (civ.Age < AgeId.Ascension && check.AllMet)
                {
                    Advance(ctx, civ, check.NextAge);
                    check = TechQueries.EvaluateNextAge(civ, ctx.Db, ctx.State);
                }
                civ.Memory[ProgressionKeys.AgeProgress] = check.Progress;
            }
        }

        private static void Advance(SimContext ctx, Civilization civ, AgeId next)
        {
            var old = civ.Age;
            civ.Age = next;
            civ.AgeEnteredTick = ctx.State.Tick;
            ModifierSystem.RecomputeFor(civ, ctx.Db); // modifiers fresh at the tick boundary
            civ.Memory[ProgressionKeys.Dirty] = 1f;
            ctx.Events?.Publish(new AgeAdvancedEvent { CivilizationId = civ.Id, OldAge = old, NewAge = next });
            if (ctx.Ops != null)
            {
                var capital = ctx.State.Settlements.Get(civ.CapitalId);
                bool hasCapital = capital != null && capital.IsAlive;
                ctx.Ops.AddChronicle(ChronicleCategory.Age,
                    ProgressionText.TheCiv(civ) + " have entered the " + ProgressionText.AgeName(ctx.Db.Ages[(int)next], next) + ".",
                    civ.Id, hasCapital ? capital.Id : -1, hasCapital ? capital.Center : Vec2i.Invalid, 3);
            }
        }
    }
}
