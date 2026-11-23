namespace GodSim.Core.Progression
{
    /// <summary>
    /// Generates research for every alive civilization and learns technologies.
    /// Rate per tick = Σ alive settlements (0.4 × ServiceOutput[Research] + 0.004 × population) × Modifiers.Research ×
    /// Config.ResearchMultiplier, scaled by <c>ctx.DeltaTicks</c>; added to TotalResearch and ResearchPool.
    /// <para>Research target: the AI's <see cref="Civilization.CurrentResearchTechId"/> when it is a valid target
    /// (<see cref="TechQueries.IsValidResearchTarget"/>). Otherwise the deterministic fallback is used: the cheapest
    /// researchable tech, ties broken by lowest id. The fallback is never written into <c>CurrentResearchTechId</c>, which
    /// holds only the AI's explicit choice (the AI keeps any valid choice, so writing the fallback there would stop it from
    /// ever choosing). The field is reset to -1 after a learn, or when the choice is invalid. The tech actually being
    /// pursued is published in <see cref="ProgressionKeys.ResearchTarget"/>, the canonical record for UI, saves and
    /// other modules.</para>
    /// <para>After learning, modifiers are recomputed immediately (<see cref="ModifierSystem.RecomputeFor"/>), so systems
    /// that run before Progression on the next tick read fresh values, and a reloaded world behaves exactly like an
    /// uninterrupted one. <see cref="ProgressionKeys.Dirty"/> is still set as a signal for other modules.</para>
    /// </summary>
    public sealed class ResearchSystem : ISimSystem
    {
        /// <summary>Upper bound of techs learned by one civ in one run.</summary>
        public const int MaxTechsPerRun = 8;
        /// <summary>When nothing is researchable the pool is capped at this many times the age's most expensive tech.</summary>
        public const float IdlePoolCapFactor = 2f;
        /// <summary>Absolute safety cap for TotalResearch (keeps float state finite). Techs costing more are unaffordable.</summary>
        public const float MaxTotalResearch = 1e30f;

        /// <inheritdoc />
        public string Name => "Progression.Research";

        /// <summary>4 ticks; rates are scaled by <c>ctx.DeltaTicks</c>.</summary>
        public int TickInterval => 4;

        /// <summary>
        /// Sanitizes the research state of every civ; no research is generated. On a world that has not ticked yet it also
        /// publishes <see cref="ProgressionKeys.ResearchTarget"/>. On a loaded world the saved value is kept, matching an
        /// uninterrupted run, and the next research run refreshes it.
        /// </summary>
        public void Initialize(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            bool freshWorld = ctx.State.Tick == 0;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null) continue;
                SanitizeCiv(civ);
                if (freshWorld && civ.IsAlive) civ.Memory[ProgressionKeys.ResearchTarget] = TechQueries.EffectiveResearchTarget(civ, ctx.Db);
            }
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null) return;
            int delta = ctx.DeltaTicks < 1 ? 1 : ctx.DeltaTicks;
            var civs = ctx.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var civ = civs[i];
                if (civ == null || !civ.IsAlive) continue;
                SanitizeCiv(civ);
                float perTick = TechQueries.ResearchPerTick(ctx, civ);
                float gain = MathUtil.Sanitize(perTick * delta);
                if (gain < 0f) gain = 0f;
                civ.TotalResearch = MathUtil.Clamp(civ.TotalResearch + gain, 0f, MaxTotalResearch);
                civ.ResearchPool = MathUtil.Clamp(civ.ResearchPool + gain, 0f, MaxTotalResearch);
                civ.Memory[ProgressionKeys.ResearchPerTick] = perTick;
                Advance(ctx, civ);
            }
        }

        /// <summary>Learns as many techs as the pool pays for (bounded by <see cref="MaxTechsPerRun"/>).</summary>
        private static void Advance(SimContext ctx, Civilization civ)
        {
            var db = ctx.Db;
            if (civ.CurrentResearchTechId != -1 && !TechQueries.IsValidResearchTarget(civ, db, civ.CurrentResearchTechId))
                civ.CurrentResearchTechId = -1; // invalid, known, locked or unaffordable: hand the choice back to the AI

            int target = -1;
            bool learnedAny = false;
            for (int learned = 0; learned < MaxTechsPerRun; learned++)
            {
                target = TechQueries.EffectiveResearchTarget(civ, db);
                if (target < 0)
                {
                    float cap = IdlePoolCapFactor * TechQueries.MaxTechCostInAge(db, civ.Age, civ.Race);
                    if (civ.ResearchPool > cap) civ.ResearchPool = cap;
                    break;
                }
                var tech = db.Tech(target);
                if (!TechQueries.IsAffordable(tech)) break; // defensive: valid targets are always affordable
                float cost = TechQueries.SafeCost(tech);
                if (civ.ResearchPool < cost) break;
                Learn(ctx, civ, tech, cost);
                learnedAny = true;
                target = -1;
            }
            if (target < 0) target = TechQueries.EffectiveResearchTarget(civ, db);
            civ.Memory[ProgressionKeys.ResearchTarget] = target;
            if (learnedAny) ModifierSystem.RecomputeFor(civ, db); // modifiers fresh at the tick boundary
        }

        private static void Learn(SimContext ctx, Civilization civ, TechDefinition tech, float cost)
        {
            civ.KnownTechs.Add(tech.Id);
            if (tech.Effects != null)
                for (int e = 0; e < tech.Effects.Length; e++)
                    if (tech.Effects[e].Kind == TechEffectKind.UnlockBuilding) ModifierSystem.Unlock(civ, tech.Effects[e].Building);
            civ.ResearchPool = MathUtil.Clamp(civ.ResearchPool - cost, 0f, MaxTotalResearch);
            civ.Memory[ProgressionKeys.Dirty] = 1f;
            civ.CurrentResearchTechId = -1; // the AI picks the next target

            ctx.Events?.Publish(new TechResearchedEvent { CivilizationId = civ.Id, TechId = tech.Id });
            if (ctx.Ops != null)
            {
                var capital = ctx.State.Settlements.Get(civ.CapitalId);
                bool hasCapital = capital != null && capital.IsAlive;
                ctx.Ops.AddChronicle(ChronicleCategory.Technology,
                    ProgressionText.TheCiv(civ) + " discovered " + ProgressionText.TechName(tech) + ".",
                    civ.Id, hasCapital ? capital.Id : -1, hasCapital ? capital.Center : Vec2i.Invalid, 1);
            }
        }

        private static void SanitizeCiv(Civilization civ)
        {
            civ.TotalResearch = MathUtil.Clamp(MathUtil.Sanitize(civ.TotalResearch), 0f, MaxTotalResearch);
            civ.ResearchPool = MathUtil.Clamp(MathUtil.Sanitize(civ.ResearchPool), 0f, MaxTotalResearch);
        }
    }
}
