using System;
using System.Collections.Generic;

namespace GodSim.Core.Population
{
    /// <summary>
    /// Moves every resident's happiness, health and faith smoothly toward targets derived from food,
    /// housing, services, civilization modifiers, war, age and traits; aggregates settlement averages
    /// (<see cref="Settlement.Happiness"/>, <see cref="Settlement.Health"/>, <see cref="Settlement.Faith"/>),
    /// accumulates or decays <see cref="Settlement.Unrest"/>, and rolls epidemics.
    /// <para>
    /// Happiness target = 0.35 + 0.2*satiation + (housed ? 0.15 : -0.2) + 0.1*min(1, Entertainment)
    /// + 0.08*min(1, Faith) + 0.07*min(1, Health) + civ HappinessBonus - 0.08 at war, adjusted by traits.
    /// Health target comes from satiation, Health and Water coverage (+ civ HealthBonus), housing, race, traits
    /// and age. Faith moves toward min(1, Faith coverage) * civ.Devotion.
    /// </para>
    /// <para>
    /// Epidemics: per-year chance 0.02 + 0.06*density (crowding = population/(buildings+1), worsened by low Health
    /// and Water coverage), scaled by <see cref="SimConfig.DisasterFrequency"/>. An outbreak kills ~2-8% of the
    /// residents (<see cref="DeathCause.Disease"/>), weighted toward the sick, publishes a Plague
    /// <see cref="DisasterEvent"/> and writes the chronicle.
    /// </para>
    /// </summary>
    public sealed class WellbeingSystem : ISimSystem
    {
        /// <summary>Run interval in ticks.</summary>
        public const int Interval = 6;
        /// <summary>Settlement happiness below which unrest grows.</summary>
        public const float UnrestThreshold = 0.35f;
        /// <summary>Fraction of the gap to the happiness target closed per tick.</summary>
        public const float HappinessRate = 0.03f;
        /// <summary>Fraction of the gap to the health target closed per tick.</summary>
        public const float HealthRate = 0.015f;
        /// <summary>Fraction of the gap to the faith target closed per tick.</summary>
        public const float FaithRate = 0.01f;
        /// <summary>Base yearly epidemic chance for any settlement.</summary>
        public const float EpidemicBaseChance = 0.02f;
        /// <summary>Extra yearly epidemic chance at full density.</summary>
        public const float EpidemicDensityChance = 0.06f;
        /// <summary>Smallest population that can sustain an epidemic.</summary>
        public const int EpidemicMinPopulation = 8;
        /// <summary>Years of herd immunity after an outbreak.</summary>
        public const float EpidemicCooldownYears = 5f;
        /// <summary>Lowest share of residents an outbreak kills.</summary>
        public const float MinOutbreakKillFraction = 0.02f;
        /// <summary>Highest share of residents an outbreak kills.</summary>
        public const float MaxOutbreakKillFraction = 0.08f;

        private struct VictimKey
        {
            public Person P;
            public double Key;
        }

        private readonly HashSet<int> _civsAtWar = new HashSet<int>();
        private readonly List<VictimKey> _victimKeys = new List<VictimKey>();
        private readonly List<Person> _victims = new List<Person>();
        private static readonly VictimComparer HighestKeyFirst = new VictimComparer();

        /// <summary>Creates the system.</summary>
        public WellbeingSystem() { }

        /// <inheritdoc />
        public string Name => "Population.Wellbeing";

        /// <inheritdoc />
        public int TickInterval => Interval;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            int dt = Math.Max(1, ctx.DeltaTicks);
            CollectWars(ctx.State);
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                UpdateSettlement(ctx, s, dt);
                TryEpidemic(ctx, s, dt);
            }
            _civsAtWar.Clear();
        }

        // ------------------------------------------------------------------ targets (public for tests/UI)

        /// <summary>Happiness a person drifts toward, clamped to [0,1].</summary>
        public static float HappinessTarget(Person p, Settlement s, Civilization civ, bool atWar)
        {
            float sat = SocietyUtil.Unit(p.Satiation, 1f);
            bool housed = p.HomeId >= 0;
            float ent = Math.Min(1f, SocietyUtil.Coverage(s, ServiceType.Entertainment));
            float faith = Math.Min(1f, SocietyUtil.Coverage(s, ServiceType.Faith));
            float health = Math.Min(1f, SocietyUtil.Coverage(s, ServiceType.Health));
            float t = 0.35f + 0.2f * sat + (housed ? 0.15f : -0.2f) + 0.1f * ent + 0.08f * faith + 0.07f * health;
            if (civ != null) t += MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.HappinessBonus), -1f, 1f);
            if (atWar) t -= 0.08f;

            var traits = p.Traits;
            if ((traits & PersonTraits.Devout) != 0) t += 0.06f * faith - 0.04f * (1f - faith);
            if ((traits & PersonTraits.Greedy) != 0) t += 0.04f * Math.Min(1f, SocietyUtil.Coverage(s, ServiceType.Commerce)) - 0.04f;
            if ((traits & PersonTraits.Charismatic) != 0) t += 0.03f;
            if ((traits & PersonTraits.Wise) != 0) t += 0.03f;
            if ((traits & PersonTraits.Hero) != 0) t += 0.05f;
            if ((traits & PersonTraits.Sickly) != 0) t -= 0.03f;
            if ((traits & PersonTraits.Brave) != 0 && atWar) t += 0.05f;
            if ((traits & PersonTraits.Lazy) != 0 && p.IsAdult) t += p.Job == JobType.None ? 0.03f : -0.03f;
            return SocietyUtil.Unit(t, 0.5f);
        }

        /// <summary>Health a person drifts toward, clamped to [0,1].</summary>
        public static float HealthTarget(Person p, Settlement s, Civilization civ, RaceDefinition race)
        {
            float sat = SocietyUtil.Unit(p.Satiation, 1f);
            float healthCov = Math.Min(1f, SocietyUtil.Coverage(s, ServiceType.Health));
            float waterCov = Math.Min(1f, SocietyUtil.Coverage(s, ServiceType.Water));
            float t = 0.40f + 0.30f * sat + 0.12f * healthCov + 0.08f * waterCov + (p.HomeId >= 0 ? 0.06f : -0.10f);
            if (civ != null) t += MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.HealthBonus), -1f, 1f);
            float lifespan = 70f, adulthood = 16f;
            if (race != null)
            {
                t += 0.25f * (MathUtil.Clamp(MathUtil.Sanitize(race.BaseHealth, 1f), 0f, 3f) - 1f);
                lifespan = MathUtil.Sanitize(race.LifeExpectancy, 70f);
                adulthood = MathUtil.Sanitize(race.AdulthoodAge, 16f);
            }
            if (civ != null) lifespan += MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.LifespanBonusYears), -1000f, 1000f);
            lifespan = Math.Max(Math.Max(1f, adulthood + 1f), lifespan);

            var traits = p.Traits;
            if ((traits & PersonTraits.Hardy) != 0) t += 0.08f;
            if ((traits & PersonTraits.Strong) != 0) t += 0.04f;
            if ((traits & PersonTraits.Sickly) != 0) t -= 0.12f;

            float age = Math.Max(0f, MathUtil.Sanitize(p.AgeYears));
            float ratio = age / lifespan;
            if (ratio > 0.6f) t -= 0.45f * MathUtil.SmoothStep(0.6f, 1.15f, ratio);
            if (age < 3f) t -= 0.05f * (1f - age / 3f);
            return SocietyUtil.Unit(t, 0.8f);
        }

        /// <summary>Faith a person drifts toward: min(1, Faith coverage) * civ.Devotion (Devout people a little higher).</summary>
        public static float FaithTarget(Person p, Settlement s, Civilization civ)
        {
            float cov = Math.Min(1f, SocietyUtil.Coverage(s, ServiceType.Faith));
            float devotion = civ != null ? SocietyUtil.Unit(civ.Devotion, 0.5f) : 0.5f;
            float t = cov * devotion;
            if (p.HasTrait(PersonTraits.Devout)) t = t * 1.25f + 0.1f;
            return SocietyUtil.Unit(t, 0.5f);
        }

        /// <summary>
        /// Epidemic density factor 0..1: crowding (population per building) worsened by poor Health and Water coverage.
        /// </summary>
        public static float DensityFactor(Settlement s)
        {
            float crowd = s.Population / (s.BuildingIds.Count + 1f);
            float crowdF = MathUtil.Clamp01((crowd - 2f) / 6f);
            float healthLack = 1f - Math.Min(1f, SocietyUtil.Coverage(s, ServiceType.Health));
            float waterLack = 1f - Math.Min(1f, SocietyUtil.Coverage(s, ServiceType.Water));
            return MathUtil.Clamp01(crowdF * (0.4f + 0.45f * healthLack + 0.15f * waterLack));
        }

        /// <summary>Per-year epidemic chance for a settlement before config scaling: 0.02 + 0.06 * density.</summary>
        public static float EpidemicChancePerYear(Settlement s) => EpidemicBaseChance + EpidemicDensityChance * DensityFactor(s);

        // ------------------------------------------------------------------ per settlement

        /// <summary>Updates every resident of one settlement and its aggregates.</summary>
        public void UpdateSettlement(SimContext ctx, Settlement s, int deltaTicks)
        {
            var state = ctx.State;
            var people = state.People;
            var civ = state.Civilizations.Get(s.CivilizationId);
            bool atWar = civ != null && _civsAtWar.Contains(civ.Id);
            float aH = Alpha(HappinessRate, deltaTicks);
            float aL = Alpha(HealthRate, deltaTicks);
            float aF = Alpha(FaithRate, deltaTicks);

            RaceDefinition cachedRace = null;
            RaceId cachedRaceId = s.Race;
            bool raceCached = false;

            var residents = s.ResidentIds;
            for (int i = 0; i < residents.Count; i++)
            {
                var p = people.Get(residents[i]);
                if (p == null || !p.IsAlive) continue;
                if (!raceCached || p.Race != cachedRaceId) { cachedRace = SocietyUtil.RaceOf(ctx.Db, p.Race); cachedRaceId = p.Race; raceCached = true; }

                float h = SocietyUtil.Unit(p.Happiness, 0.5f);
                float l = SocietyUtil.Unit(p.Health, 1f);
                float f = SocietyUtil.Unit(p.Faith, 0.5f);
                h += (HappinessTarget(p, s, civ, atWar) - h) * aH;
                l += (HealthTarget(p, s, civ, cachedRace) - l) * aL;
                f += (FaithTarget(p, s, civ) - f) * aF;
                p.Happiness = SocietyUtil.Unit(h, 0.5f);
                p.Health = SocietyUtil.Unit(l, 1f);
                p.Faith = SocietyUtil.Unit(f, 0.5f);
            }
            Aggregate(ctx, s, deltaTicks, true);
        }

        /// <summary>Recomputes the settlement averages; optionally advances unrest.</summary>
        private static void Aggregate(SimContext ctx, Settlement s, int deltaTicks, bool advanceUnrest)
        {
            var people = ctx.State.People;
            var residents = s.ResidentIds;
            double sumH = 0, sumL = 0, sumF = 0;
            int n = 0;
            for (int i = 0; i < residents.Count; i++)
            {
                var p = people.Get(residents[i]);
                if (p == null || !p.IsAlive) continue;
                sumH += p.Happiness;
                sumL += p.Health;
                sumF += p.Faith;
                n++;
            }
            if (n > 0)
            {
                s.Happiness = SocietyUtil.Unit((float)(sumH / n), 0.5f);
                s.Health = SocietyUtil.Unit((float)(sumL / n), 1f);
                s.Faith = SocietyUtil.Unit((float)(sumF / n), 0.5f);
            }
            if (!advanceUnrest) return;
            float unrest = SocietyUtil.Unit(s.Unrest, 0f);
            float hap = s.Happiness;
            if (n == 0) unrest -= 0.01f * deltaTicks;
            else if (hap < UnrestThreshold) unrest += (UnrestThreshold - hap) * 0.04f * deltaTicks;
            else unrest -= (0.003f + 0.02f * (hap - UnrestThreshold)) * deltaTicks;
            s.Unrest = SocietyUtil.Unit(unrest, 0f);
        }

        private static float Alpha(float ratePerTick, int ticks) => 1f - (float)Math.Pow(1.0 - ratePerTick, Math.Max(1, ticks));

        private void CollectWars(WorldState state)
        {
            _civsAtWar.Clear();
            var wars = state.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var w = wars[i];
                if (w == null || !w.IsActive) continue;
                _civsAtWar.Add(w.AttackerId);
                _civsAtWar.Add(w.DefenderId);
            }
        }

        // ------------------------------------------------------------------ epidemics

        private void TryEpidemic(SimContext ctx, Settlement s, int deltaTicks)
        {
            var cfg = ctx.Config;
            float freq = cfg == null ? 1f : (cfg.EnableDisasters ? MathUtil.Clamp(MathUtil.Sanitize(cfg.DisasterFrequency, 1f), 0f, 10f) : 0f);
            if (freq <= 0f || s.Population < EpidemicMinPopulation) return;
            if (s.Memory.TryGetValue(SocietyKeys.LastEpidemicTick, out float last))
            {
                long since = ctx.Tick - (long)MathUtil.Sanitize(last, -1e9f);
                if (since >= 0 && since < SimTime.YearsToTicks(EpidemicCooldownYears)) return;
            }
            float density = DensityFactor(s);
            float perYear = MathUtil.Clamp01((EpidemicBaseChance + EpidemicDensityChance * density) * freq);
            if (!ctx.Rng.Chance(Rng.PerYearToPerTicks(perYear, deltaTicks))) return;

            float fraction = ctx.Rng.Range(MinOutbreakKillFraction, 0.06f) + 0.02f * density;
            fraction *= 1f - 0.3f * Math.Min(1f, SocietyUtil.Coverage(s, ServiceType.Health));
            TriggerOutbreak(ctx, s, fraction);
        }

        /// <summary>
        /// Starts an outbreak in a settlement right now. Kills round(population * killFraction) residents
        /// (killFraction is clamped to [0.01, 0.08]; at least one), chosen by weighted sampling that favors the
        /// sick, the old and the sickly; weakens survivors; publishes a Plague <see cref="DisasterEvent"/>;
        /// writes a chronicle entry (importance 1). Returns the number of deaths.
        /// </summary>
        public int TriggerOutbreak(SimContext ctx, Settlement s, float killFraction)
        {
            if (ctx == null || ctx.State == null || ctx.Ops == null || s == null || !s.IsAlive) return 0;
            var people = ctx.State.People;
            var rng = ctx.Rng;
            float fraction = MathUtil.Clamp(MathUtil.Sanitize(killFraction, MinOutbreakKillFraction), 0.01f, MaxOutbreakKillFraction);

            _victimKeys.Clear();
            var residents = s.ResidentIds;
            for (int i = 0; i < residents.Count; i++)
            {
                var p = people.Get(residents[i]);
                if (p == null || !p.IsAlive) continue;
                float frailty = 1.1f - SocietyUtil.Unit(p.Health, 1f);
                double w = frailty * frailty;
                if (p.HasTrait(PersonTraits.Sickly)) w *= 2.0;
                if (p.HasTrait(PersonTraits.Hardy)) w *= 0.5;
                if (p.Stage == LifeStage.Elder) w *= 1.6;
                else if (p.AgeYears < 5f) w *= 1.3;
                var race = SocietyUtil.RaceOf(ctx.Db, p.Race);
                if (race != null) w /= MathUtil.Clamp(MathUtil.Sanitize(race.DiseaseResistance, 1f), 0.2f, 5f);
                if (w < 1e-4) w = 1e-4;
                // Efraimidis-Spirakis weighted sampling without replacement: key = ln(u) / w, keep the largest.
                double u = 1.0 - rng.NextDouble(); // (0, 1]
                _victimKeys.Add(new VictimKey { P = p, Key = Math.Log(u) / w });
            }
            int pop = _victimKeys.Count;
            if (pop == 0) return 0;
            int deaths = MathUtil.RoundToInt(pop * fraction);
            if (deaths < 1) deaths = 1;
            int cap = (int)Math.Ceiling(pop * MaxOutbreakKillFraction);
            if (deaths > cap) deaths = cap;
            if (_victimKeys.Count > 1) _victimKeys.Sort(HighestKeyFirst);
            _victims.Clear();
            for (int i = 0; i < deaths && i < _victimKeys.Count; i++) _victims.Add(_victimKeys[i].P);
            _victimKeys.Clear();

            for (int i = 0; i < _victims.Count; i++) ctx.Ops.KillPerson(_victims[i], DeathCause.Disease);
            _victims.Clear();

            float severity = MathUtil.Clamp01(fraction / MaxOutbreakKillFraction);
            for (int i = 0; i < residents.Count; i++)
            {
                var p = people.Get(residents[i]);
                if (p == null || !p.IsAlive) continue;
                float hit = (0.05f + 0.15f * severity) * (p.HasTrait(PersonTraits.Sickly) ? 1.5f : 1f) * (p.HasTrait(PersonTraits.Hardy) ? 0.5f : 1f);
                p.Health = SocietyUtil.Unit(p.Health - hit, 0.5f);
                p.Happiness = SocietyUtil.Unit(p.Happiness - 0.05f - 0.05f * severity, 0.5f);
            }
            Aggregate(ctx, s, 1, false);

            s.Memory[SocietyKeys.LastEpidemicTick] = ctx.Tick;
            s.Memory[SocietyKeys.EpidemicDeaths] = s.Mem(SocietyKeys.EpidemicDeaths) + deaths;
            ctx.Events?.Publish(new DisasterEvent { Type = DisasterType.Plague, Position = s.Center, Radius = 4, Severity = severity });
            ctx.Ops.AddChronicle(ChronicleCategory.Disaster,
                "A plague swept through " + s.Name + ", claiming " + deaths + (deaths == 1 ? " life." : " lives."),
                s.CivilizationId, s.Id, s.Center, 1);
            return deaths;
        }

        private sealed class VictimComparer : IComparer<VictimKey>
        {
            public int Compare(VictimKey a, VictimKey b)
            {
                if (a.Key != b.Key) return a.Key > b.Key ? -1 : 1;
                return a.P.Id.CompareTo(b.P.Id);
            }
        }
    }
}
