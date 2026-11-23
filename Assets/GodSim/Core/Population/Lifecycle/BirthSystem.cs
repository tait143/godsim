using System;
using System.Collections.Generic;

namespace GodSim.Core.Population
{
    /// <summary>
    /// Births (every 8 ticks). An eligible mother is a living Female adult (not elder) with
    /// ChildrenCount &lt; race.MaxChildren, Satiation &gt; 0.4 and Health &gt; 0.3 whose husband is alive
    /// in the same settlement. Her yearly chance is race.FertilityPerYear x civ.Modifiers.Growth x
    /// Config.GrowthMultiplier x foodFactor x housingFactor x (0.6 + 0.8 x Happiness) x (1.4 if Fertile),
    /// converted per interval. foodFactor comes from the days of food in stock (&lt;10 days 0.3, &gt;60 days
    /// 1.2, linear between) times a hunger brake from the settlement's smoothed fed ratio
    /// (<see cref="LifecycleRules.HungerFactor"/>: 0 at a ratio of 0.5, 1 when fully fed); housingFactor is
    /// <see cref="LifecycleRules.HousingFactor"/> (1 with a free bed and at most 5% homeless, ramping down to 0.1 at
    /// 20% homeless, x0.3 when no bed is free), times the civ-wide crowding brake
    /// (<see cref="LifecycleRules.CivCrowdingFactor"/>: 0.5 at 750 people). Saurians lay clutches of 1-2; other races have 2% twins.
    /// Settlements with fewer than 2 couples let unpartnered women give birth at 30% of the chance
    /// (father -1). No births happen at or above Config.MaxPopulation. Children are created with
    /// <see cref="WorldOps.CreatePerson"/> (isBirth) and moved into the mother's home when it has room.
    /// </summary>
    public sealed class BirthSystem : ISimSystem
    {
        private readonly List<Person> _residents = new List<Person>(128);
        private readonly RaceDefinition[] _races = new RaceDefinition[Counts.Races];
        private readonly float[] _foodNeed = new float[LifecycleRules.FoodNeedTableSize];

        /// <summary>Creates the system.</summary>
        public BirthSystem() { }

        /// <inheritdoc />
        public string Name => "Population.Births";

        /// <inheritdoc />
        public int TickInterval => 8;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            int dt = Math.Max(1, ctx.DeltaTicks);
            int cap = ctx.Config != null ? ctx.Config.MaxPopulation : int.MaxValue;
            float globalGrowth = ctx.Config != null ? MathUtil.Clamp(MathUtil.Sanitize(ctx.Config.GrowthMultiplier, 1f), 0f, 10f) : 1f;
            if (globalGrowth <= 0f) return;

            int headroom = cap - ctx.State.LivingPopulation; // counted once per run
            if (headroom <= 0) return;

            for (int r = 0; r < _races.Length; r++) _races[r] = LifecycleRules.RaceOf(ctx.Db, (RaceId)r);
            LifecycleRules.FillFoodNeedTable(ctx.Db, _foodNeed);

            var settlements = ctx.State.Settlements.Items;
            int n = settlements.Count;
            if (n == 0) return;
            // Rotate the starting settlement so that, near the cap, no settlement always wins the headroom.
            long round = ctx.Tick / dt;
            int start = (int)(((round % n) + n) % n);
            for (int k = 0; k < n && headroom > 0; k++)
            {
                var s = settlements[(start + k) % n];
                if (s == null || !s.IsAlive) continue;
                headroom = BirthsInSettlement(ctx, s, dt, globalGrowth, headroom);
            }
            _residents.Clear();
        }

        private int BirthsInSettlement(SimContext ctx, Settlement s, int dt, float globalGrowth, int headroom)
        {
            var state = ctx.State;
            var civ = s.CivilizationId >= 0 ? state.Civilizations.Get(s.CivilizationId) : null;
            float civGrowth = civ != null ? MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Growth, 1f), 0f, 10f) : 1f;
            if (civGrowth <= 0f) return headroom;

            // Gather residents, consumption and the number of couples in one pass.
            var people = state.People;
            var ids = s.ResidentIds;
            _residents.Clear();
            float consumption = 0f;
            int couples = 0;
            for (int i = 0; i < ids.Count; i++)
            {
                var p = people.Get(ids[i]);
                if (p == null || !p.IsAlive) continue;
                _residents.Add(p);
                consumption += LifecycleRules.FoodNeedOf(_foodNeed, p);
                if (p.Sex == Sex.Female && LivingHusbandHere(state, p, s.Id) != null) couples++;
            }
            int population = _residents.Count;
            if (population == 0) return headroom;

            float days = LifecycleRules.DaysOfFood(s.Stockpile.Get(ResourceType.Food), consumption);
            float fedRatio = MathUtil.Clamp01(MathUtil.Sanitize(s.Mem(LifecycleMemoryKeys.FoodRatio, 1f), 1f));
            float foodFactor = LifecycleRules.FoodStockFactor(days) * LifecycleRules.HungerFactor(fedRatio);
            float housingFactor = LifecycleRules.HousingFactor(s.Homeless, population, LifecycleRules.FreeBeds(state, s));
            float crowding = LifecycleRules.CivCrowdingFactor(LifecycleRules.CivPopulation(state, civ));
            float settlementFactor = civGrowth * globalGrowth * foodFactor * housingFactor * crowding;
            if (settlementFactor <= 0f) return headroom;
            bool tinyTribe = couples < LifecycleBalance.TinyTribeCouples;

            var rng = ctx.Rng;
            int births = 0;
            for (int i = 0; i < population && headroom > 0; i++) // newborns are appended past 'population'
            {
                var mother = _residents[i];
                if (!mother.IsAlive || mother.Sex != Sex.Female || mother.Stage != LifeStage.Adult) continue;
                int raceIndex = (int)mother.Race;
                if (raceIndex < 0 || raceIndex >= _races.Length) continue;
                var race = _races[raceIndex];
                if (race == null || mother.ChildrenCount >= race.MaxChildren) continue;
                if (!(mother.Satiation > LifecycleBalance.MinMotherSatiation) || !(mother.Health > LifecycleBalance.MinMotherHealth)) continue;

                var father = LivingHusbandHere(state, mother, s.Id);
                float partnerFactor = 1f;
                if (father == null)
                {
                    if (!tinyTribe) continue;
                    partnerFactor = LifecycleBalance.TinyTribeFactor;
                }

                float perYear = MathUtil.Sanitize(race.FertilityPerYear) * settlementFactor * partnerFactor
                                * LifecycleRules.HappinessFactor(mother.Happiness)
                                * (mother.HasTrait(PersonTraits.Fertile) ? LifecycleBalance.FertileTraitFactor : 1f);
                if (!rng.Chance(Rng.PerYearToPerTicks(perYear, dt))) continue;

                int count = mother.Race == RaceId.Saurian
                    ? rng.RangeInclusive(LifecycleBalance.SaurianClutchMin, LifecycleBalance.SaurianClutchMax)
                    : (rng.Chance(LifecycleBalance.TwinChance) ? 2 : 1);
                if (count > headroom) count = headroom;
                int fatherId = father != null ? father.Id : -1;
                for (int c = 0; c < count; c++)
                {
                    var sex = rng.Chance(0.5f) ? Sex.Female : Sex.Male;
                    var child = ctx.Ops.CreatePerson(s, mother.Race, sex, 0f, mother.Id, fatherId, true);
                    if (child == null) continue;
                    MoveIntoMothersHome(ctx, mother, child);
                    headroom--;
                    births++;
                }
            }
            if (births > 0) s.Memory[LifecycleMemoryKeys.TotalBirths] = s.Mem(LifecycleMemoryKeys.TotalBirths) + births;
            return headroom;
        }

        /// <summary>The woman's husband if he is alive, male and living in the same settlement; otherwise null.</summary>
        private static Person LivingHusbandHere(WorldState state, Person woman, int settlementId)
        {
            if (woman.SpouseId < 0) return null;
            var h = state.People.Get(woman.SpouseId);
            if (h == null || !h.IsAlive || h.Sex != Sex.Male || h.SettlementId != settlementId || h.SpouseId != woman.Id) return null;
            return h;
        }

        private static void MoveIntoMothersHome(SimContext ctx, Person mother, Person child)
        {
            if (mother.HomeId < 0) return;
            var home = ctx.State.Buildings.Get(mother.HomeId);
            if (home == null || home.IsDestroyed || !home.IsComplete || home.FreeHousing <= 0) return;
            ctx.Ops.AssignHome(child, home);
        }
    }
}
