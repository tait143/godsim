using System;
namespace GodSim.Core.Economy
{
    /// <summary>
    /// Computes each living settlement's <see cref="Settlement.ServiceOutput"/> (raw points per tick) and
    /// <see cref="Settlement.ServiceCoverage"/> (per capita, 1 = fully served, clamped to 0..3) from its
    /// complete buildings' <see cref="BuildingDefinition.Services"/>, and turns Commerce output into gold.
    /// <para>output[svc] = Σ amount × staffing × Condition × Modifiers.Service[svc] (× Modifiers.Faith for Faith);
    /// Defense additionally gets Soldiers × 1.5 × Modifiers.Military.</para>
    /// </summary>
    public sealed class ServiceSystem : ISimSystem
    {
        /// <summary>Defense points contributed per soldier (before the Military modifier).</summary>
        public const float DefensePerSoldier = 1.5f;
        /// <summary>Gold produced per Commerce point per tick (before the Trade modifier).</summary>
        public const float GoldPerCommercePoint = 0.02f;
        /// <summary>Upper bound for coverage values.</summary>
        public const float MaxCoverage = 3f;

        /// <inheritdoc />
        public string Name => "Economy.Services";

        /// <inheritdoc />
        public int TickInterval => 6;

        /// <summary>Computes outputs and coverage right away (no gold) on a world that has not ticked yet, so readers have data before the first run.</summary>
        public void Initialize(SimContext ctx)
        {
            if (!EconomyServicesShared.IsUsable(ctx)) return;
            // Outputs/coverage are persisted: a loaded world keeps the values of the last Services run, like an uninterrupted run.
            if (ctx.State.Tick != 0) return;
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                ComputeServices(ctx.State, ctx.Db, s);
            }
        }

        /// <summary>Recomputes outputs/coverage and credits commerce gold for DeltaTicks ticks.</summary>
        public void Tick(SimContext ctx)
        {
            if (!EconomyServicesShared.IsUsable(ctx)) return;
            int dt = EconomyServicesShared.Delta(ctx, TickInterval);
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                ComputeServices(ctx.State, ctx.Db, s);

                var mods = EconomyServicesShared.ModifiersOf(ctx.State, s);
                float gold = s.ServiceOutput[(int)ServiceType.Commerce] * GoldPerCommercePoint * dt
                             * EconomyServicesShared.Multiplier(mods.Trade);
                gold = EconomyServicesShared.NonNegative(gold);
                float stored = gold > 0f ? s.Stockpile.Add(ResourceType.Gold, gold) : 0f;
                s.Memory[EconomyServiceKeys.CommerceGold] = stored;
            }
        }

        /// <summary>
        /// Recomputes <see cref="Settlement.ServiceOutput"/> and <see cref="Settlement.ServiceCoverage"/> for one
        /// settlement. Also sets <see cref="Building.Efficiency"/> (staffing × condition) on pure service buildings
        /// (buildings with services but no production inputs/outputs; production buildings belong to the
        /// production system).
        /// </summary>
        public static void ComputeServices(WorldState state, GameDatabase db, Settlement s)
        {
            if (state == null || db == null || s == null) return;
            var output = s.ServiceOutput;
            for (int i = 0; i < output.Length; i++) output[i] = 0f;

            var mods = EconomyServicesShared.ModifiersOf(state, s);
            float faithMod = EconomyServicesShared.Multiplier(mods.Faith);
            var ids = s.BuildingIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var b = EconomyServicesShared.ActiveBuilding(state, db, s, ids[k], out var def);
                if (b == null) continue;
                var services = def.Services;
                if (services == null || services.Length == 0) continue;

                float factor = EconomyServicesShared.Staffing(def, b) * EconomyServicesShared.Condition01(b);
                bool producesGoods = (def.OutputsPerWorker != null && def.OutputsPerWorker.Length > 0)
                                     || (def.InputsPerWorker != null && def.InputsPerWorker.Length > 0);
                if (!producesGoods) b.Efficiency = factor;
                // Service buildings that consume inputs (PowerPlant fuel, DataCenter electronics) only serve as well as
                // they were supplied: ProductionSystem folds the input shortfall into Building.Efficiency.
                else if (def.InputsPerWorker != null && def.InputsPerWorker.Length > 0)
                    factor = Math.Min(factor, MathUtil.Clamp01(MathUtil.Sanitize(b.Efficiency)));
                if (factor <= 0f) continue;

                for (int j = 0; j < services.Length; j++)
                {
                    int svc = (int)services[j].Type;
                    if (svc < 0 || svc >= output.Length) continue;
                    float amount = EconomyServicesShared.NonNegative(services[j].Amount);
                    if (amount <= 0f) continue;
                    float mult = svc < mods.Service.Length ? EconomyServicesShared.Multiplier(mods.Service[svc]) : 1f;
                    if (svc == (int)ServiceType.Faith) mult *= faithMod;
                    output[svc] += amount * factor * mult;
                }
            }

            int defense = (int)ServiceType.Defense;
            int soldiers = s.Soldiers > 0 ? s.Soldiers : 0;
            output[defense] += soldiers * DefensePerSoldier * EconomyServicesShared.Multiplier(mods.Military);

            int population = s.Population;
            var coverage = s.ServiceCoverage;
            for (int i = 0; i < output.Length; i++)
            {
                output[i] = EconomyServicesShared.NonNegative(output[i]);
                if (i < coverage.Length) coverage[i] = CoverageFor(output[i], population, (ServiceType)i);
            }
        }

        /// <summary>coverage = output / max(1, population × need[svc]), clamped to 0..<see cref="MaxCoverage"/>.</summary>
        public static float CoverageFor(float output, int population, ServiceType service)
        {
            float pop = population > 0 ? population : 0;
            float demand = pop * ServiceNeeds.For(service);
            if (demand < 1f) demand = 1f;
            float c = MathUtil.Sanitize(output) / demand;
            return MathUtil.Clamp(MathUtil.Sanitize(c), 0f, MaxCoverage);
        }
    }
}
