using System;

namespace GodSim.Core.Population
{
    /// <summary>
    /// Aging and natural mortality (every 8 ticks). Advances <see cref="Person.AgeYears"/> by
    /// DeltaTicks / <see cref="SimTime.TicksPerYear"/>, updates <see cref="Person.Stage"/>, retires
    /// soldiers who become elders, and rolls natural deaths from <see cref="LifecycleRules.MortalityPerYear"/>
    /// using the effective life expectancy L = race.LifeExpectancy + civ.Modifiers.LifespanBonusYears.
    /// Nobody lives past 1.5 x L. Deaths go through <see cref="WorldOps.KillPerson"/> (OldAge from
    /// 0.6 x L, otherwise Disease). O(people), allocation-free.
    /// </summary>
    public sealed class AgingSystem : ISimSystem
    {
        private readonly RaceDefinition[] _races = new RaceDefinition[Counts.Races];

        /// <summary>Creates the system.</summary>
        public AgingSystem() { }

        /// <inheritdoc />
        public string Name => "Population.Aging";

        /// <inheritdoc />
        public int TickInterval => 8;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            int dt = Math.Max(1, ctx.DeltaTicks);
            float step = dt / (float)SimTime.TicksPerYear;
            for (int r = 0; r < _races.Length; r++) _races[r] = LifecycleRules.RaceOf(ctx.Db, (RaceId)r);

            var state = ctx.State;
            var rng = ctx.Rng;
            var people = state.People.Items;
            int cachedCivId = int.MinValue;
            Civilization civ = null;
            int cachedSettlementId = int.MinValue;
            Settlement settlement = null;

            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (p == null || !p.IsAlive) continue;
                int raceIndex = (int)p.Race;
                if (raceIndex < 0 || raceIndex >= _races.Length) continue;
                var race = _races[raceIndex];
                if (race == null) continue;

                if (p.CivilizationId != cachedCivId)
                {
                    cachedCivId = p.CivilizationId;
                    civ = cachedCivId >= 0 ? state.Civilizations.Get(cachedCivId) : null;
                }
                float lifeExpectancy = LifecycleRules.EffectiveLifeExpectancy(race, civ);
                float maxAge = lifeExpectancy * LifecycleBalance.MaxAgeFactor;

                float age = MathUtil.Sanitize(p.AgeYears);
                if (age < 0f) age = 0f;
                age += step;
                if (age >= maxAge)
                {
                    p.AgeYears = maxAge;
                    UpdateStage(ctx, p, race);
                    ctx.Ops.KillPerson(p, DeathCause.OldAge);
                    continue;
                }
                p.AgeYears = age;
                UpdateStage(ctx, p, race);

                float coverage = 0f;
                if (p.Stage == LifeStage.Child)
                {
                    if (p.SettlementId != cachedSettlementId)
                    {
                        cachedSettlementId = p.SettlementId;
                        settlement = cachedSettlementId >= 0 ? state.Settlements.Get(cachedSettlementId) : null;
                    }
                    if (settlement != null) coverage = settlement.ServiceCoverage[(int)ServiceType.Health];
                }

                float hazard = LifecycleRules.MortalityPerYear(p.Stage, age, lifeExpectancy, p.Health, p.Traits, coverage);
                if (rng.Chance(Rng.PerYearToPerTicks(hazard, dt)))
                    ctx.Ops.KillPerson(p, LifecycleRules.NaturalDeathCause(age, lifeExpectancy));
            }
        }

        private static void UpdateStage(SimContext ctx, Person p, RaceDefinition race)
        {
            var stage = WorldOps.StageFor(race, p.AgeYears);
            if (stage == p.Stage) return;
            p.Stage = stage;
            if (stage == LifeStage.Elder && p.Job == JobType.Soldier) ctx.Ops.ClearJob(p); // veterans retire
        }
    }
}
