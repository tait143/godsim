using System;
using System.Collections.Generic;

namespace GodSim.Core.Economy
{
    /// <summary>
    /// Staffed-building production (every 2 ticks). Fully data-driven from <see cref="BuildingDefinition"/>:
    /// <list type="bullet">
    /// <item>Staffing: <c>workerFactor = Σ Skill·(0.8 + 0.4·Experience) / JobSlots</c> over alive, present workers
    /// (capped at JobSlots); <c>efficiency = workerFactor · Condition · powerBonus</c> (×1.25 for Industry buildings
    /// when the settlement's Power coverage is at least 1), clamped to 0..1.5.</item>
    /// <item>Inputs: <c>required = InputsPerWorker · workers · DeltaTicks</c>; the input fraction is the minimum
    /// availability ratio (1 without inputs). Converters also never consume inputs whose outputs have no storage room.</item>
    /// <item>Outputs: <c>OutputsPerWorker · JobSlots · efficiency · fraction · civ.Modifiers.Production[res] ·
    /// Config.ProductionMultiplier · terrainFactor · (Food: Weather.FoodYieldMultiplier) · DeltaTicks</c>. Because
    /// efficiency already contains <c>workers / JobSlots</c>, this equals the sum of each worker's contribution
    /// (linear in staffing; identical to <c>OutputsPerWorker · workers · avgSkillFactor</c>).</item>
    /// <item>Terrain (from <see cref="BuildingDefinition.Placement"/>): Fertile → soil fertility and nearby Farmland;
    /// NearForest + Wood → Trees within 4 (depleted); Near{Ore,Stone,Gold,Coal,Oil} → matching deposits within 3
    /// (depleted, nearest first); NearWater/Coastal/NearRiver + Food → water within 3 (fishing); NearForest + Food →
    /// trees within 5 (hunting). Exhausted extraction gives factor 0 and logs "&lt;Name&gt; exhausted" once.</item>
    /// <item>Workers gain <see cref="ExperienceGainPerTick"/> experience per tick (cap 1).</item>
    /// <item>Idle foraging: unemployed alive adults not in an army add Food to their settlement.</item>
    /// </list>
    /// Writes <see cref="Building.Efficiency"/> for every job building (actual operating rate including input and terrain
    /// limits). Is the sole consumer of <see cref="BuildingDefinition.InputsPerWorker"/>, also for service buildings.
    /// </summary>
    public sealed class ProductionSystem : ISimSystem
    {
        /// <summary>Experience gained per worked tick.</summary>
        public const float ExperienceGainPerTick = 0.002f;
        /// <summary>Food foraged per idle adult per tick up to and including the Bronze age.</summary>
        public const float EarlyForagePerTick = 0.035f;
        /// <summary>Food foraged per idle adult per tick from the Iron age on.</summary>
        public const float LateForagePerTick = 0.015f;
        /// <summary>Output multiplier for Industry buildings in a fully powered settlement.</summary>
        public const float PowerBonus = 1.25f;
        /// <summary>Upper bound of <see cref="Building.Efficiency"/>.</summary>
        public const float MaxEfficiency = 1.5f;

        private const int ExhaustedPruneThreshold = 256;

        private struct OutputPlan
        {
            public int Resource;      // index into ResourceType; -1 = unused entry
            public float Amount;      // planned amount at input fraction 1
            public TileFlags Mask;    // tiles depleted by this output (None = renewable)
            public int Radius;
            public int Harvestable;   // matching tiles in range (extraction only)
            public float Available;   // total remaining amount in range (extraction only)
        }

        private readonly float[] _required = new float[Counts.Resources];
        private OutputPlan[] _plans = new OutputPlan[4];
        private readonly HashSet<int> _exhaustedNotified = new HashSet<int>();
        private readonly List<int> _pruneScratch = new List<int>();
        private TileBounds _changed;

        /// <inheritdoc />
        public string Name => "Economy.Production";

        /// <inheritdoc />
        public int TickInterval => 2;

        /// <inheritdoc />
        public void Initialize(SimContext ctx)
        {
            _exhaustedNotified.Clear();
            _changed.Reset();
        }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.State.Map == null) return;
            int dt = ctx.DeltaTicks;
            if (dt <= 0) return;
            var state = ctx.State;
            _changed.Reset();

            float cfgMult = ctx.Config != null ? MathUtil.Clamp(MathUtil.Sanitize(ctx.Config.ProductionMultiplier, 1f), 0f, 10f) : 1f;
            float weather = MathUtil.Clamp(MathUtil.Sanitize(state.Weather.FoodYieldMultiplier, 1f), 0f, 5f);

            var buildings = state.Buildings.Items;
            for (int i = 0; i < buildings.Count; i++) ProcessBuilding(ctx, buildings[i], dt, cfgMult, weather);

            Forage(state, dt, cfgMult, weather);

            if (_changed.Any)
            {
                state.Map.MarkTerrainChanged();
                if (ctx.Events != null) ctx.Events.Publish(_changed.ToEvent());
            }
            PruneNotified(state);
        }

        // ------------------------------------------------------------------ buildings

        private void ProcessBuilding(SimContext ctx, Building b, int dt, float cfgMult, float weather)
        {
            if (b == null || b.IsDestroyed) return;
            if (!ctx.Db.TryGetBuilding(b.Type, out var def) || !def.HasJobs) return;
            if (!b.IsComplete) { b.Efficiency = 0f; return; }
            var state = ctx.State;
            var s = state.Settlements.Get(b.SettlementId);
            if (s == null || !s.IsAlive || !state.Map.InBounds(b.Origin)) { b.Efficiency = 0f; return; }

            // ---- staffing (and experience)
            int slots = def.JobSlots;
            int workers = 0;
            float skillSum = 0f;
            float xpGain = ExperienceGainPerTick * dt;
            var ids = b.Workers;
            for (int w = 0; w < ids.Count && workers < slots; w++)
            {
                var p = state.People.Get(ids[w]);
                if (p == null || !p.IsAlive || p.ArmyId >= 0 || p.WorkplaceId != b.Id) continue;
                float skill = MathUtil.Clamp(MathUtil.Sanitize(p.Skill, 1f), 0f, 3f);
                float xp = MathUtil.Clamp01(MathUtil.Sanitize(p.Experience));
                skillSum += skill * (0.8f + 0.4f * xp);
                p.Experience = Math.Min(1f, xp + xpGain);
                workers++;
            }
            if (workers == 0) { b.Efficiency = 0f; return; }

            float condition = MathUtil.Clamp01(MathUtil.Sanitize(b.Condition, 1f)); // corrupt (NaN) condition: assume intact
            float power = def.Category == BuildingCategory.Industry && s.ServiceCoverage[(int)ServiceType.Power] >= 1f ? PowerBonus : 1f;
            float eff = MathUtil.Clamp(MathUtil.Sanitize(skillSum / slots * condition * power), 0f, MaxEfficiency);

            var civ = state.Civilizations.Get(s.CivilizationId);
            float[] prodMods = civ != null ? civ.Modifiers.Production : null;
            var stock = s.Stockpile;

            // ---- inputs
            Array.Clear(_required, 0, _required.Length);
            bool hasInputs = false;
            var inputs = def.InputsPerWorker;
            if (inputs != null)
                for (int k = 0; k < inputs.Length; k++)
                {
                    int ri = (int)inputs[k].Type;
                    float a = MathUtil.Sanitize(inputs[k].Amount);
                    if (ri < 0 || ri >= _required.Length || !(a > 0f)) continue;
                    _required[ri] += a * workers * dt;
                    hasInputs = true;
                }
            float fraction = 1f;
            if (hasInputs)
            {
                if (eff <= 0f) fraction = 0f;
                else
                    for (int ri = 0; ri < _required.Length; ri++)
                        if (_required[ri] > 0f) fraction = Math.Min(fraction, stock.Get((ResourceType)ri) / _required[ri]);
                fraction = MathUtil.Clamp01(MathUtil.Sanitize(fraction));
            }

            // ---- plan outputs (terrain factors, extraction availability)
            var map = state.Map;
            int ox = b.Origin.X, oy = b.Origin.Y;
            int bw = MathUtil.Clamp(b.Width, 1, 8), bh = MathUtil.Clamp(b.Height, 1, 8);
            var outputs = def.OutputsPerWorker;
            int outCount = outputs != null ? outputs.Length : 0;
            if (_plans.Length < outCount) _plans = new OutputPlan[Math.Max(outCount, _plans.Length * 2)];
            float baseRate = slots * eff * cfgMult * dt;
            float bestTerrain = 0f;
            int validOutputs = 0;
            float fertCache = -1f;
            for (int k = 0; k < outCount; k++)
            {
                ref OutputPlan plan = ref _plans[k];
                plan = default;
                plan.Resource = -1;
                int ri = (int)outputs[k].Type;
                float a = MathUtil.Sanitize(outputs[k].Amount);
                if (ri < 0 || ri >= Counts.Resources || !(a > 0f)) continue;
                float factor = EvaluateTerrain(map, def.Placement, (ResourceType)ri, ox, oy, bw, bh, ref plan, ref fertCache);
                if (factor > bestTerrain) bestTerrain = factor;
                validOutputs++;
                float mod = prodMods != null && ri < prodMods.Length ? MathUtil.Clamp(MathUtil.Sanitize(prodMods[ri], 1f), 0f, 10f) : 1f;
                float amt = a * baseRate * mod * factor * (ri == (int)ResourceType.Food ? weather : 1f);
                if (plan.Mask != TileFlags.None) amt = Math.Min(amt, plan.Available);
                amt = MathUtil.Sanitize(amt);
                plan.Resource = ri;
                plan.Amount = amt > 0f ? amt : 0f;
            }
            if (validOutputs == 0) bestTerrain = 1f;

            // ---- converters: never burn inputs into full storage
            if (hasInputs && validOutputs > 0 && fraction > 0f)
            {
                float room = 0f;
                for (int k = 0; k < outCount; k++)
                {
                    ref OutputPlan plan = ref _plans[k];
                    if (plan.Resource < 0 || !(plan.Amount > 0f)) continue;
                    room = Math.Max(room, stock.FreeSpace((ResourceType)plan.Resource) / plan.Amount);
                }
                fraction = Math.Min(fraction, MathUtil.Clamp01(MathUtil.Sanitize(room)));
            }

            // ---- consume inputs
            if (hasInputs && fraction > 0f)
                for (int ri = 0; ri < _required.Length; ri++)
                    if (_required[ri] > 0f) stock.ConsumeUpTo((ResourceType)ri, _required[ri] * fraction);

            // ---- produce + deplete
            int extractive = 0, exhausted = 0;
            for (int k = 0; k < outCount; k++)
            {
                ref OutputPlan plan = ref _plans[k];
                if (plan.Resource < 0) continue;
                if (plan.Amount > 0f && fraction > 0f)
                {
                    float stored = stock.Add((ResourceType)plan.Resource, plan.Amount * fraction);
                    if (plan.Mask != TileFlags.None && stored > 0f)
                    {
                        ProductionTerrain.Deplete(map, ox, oy, bw, bh, plan.Radius, plan.Mask, stored, ref _changed, out int cleared);
                        plan.Harvestable -= cleared;
                    }
                }
                if (plan.Mask != TileFlags.None)
                {
                    extractive++;
                    if (plan.Harvestable <= 0) exhausted++;
                }
            }

            b.Efficiency = MathUtil.Clamp(MathUtil.Sanitize(eff * fraction * bestTerrain), 0f, MaxEfficiency);

            if (extractive > 0 && exhausted == extractive)
            {
                if (_exhaustedNotified.Add(b.Id)) s.LogAi((string.IsNullOrEmpty(def.Name) ? b.Type.ToString() : def.Name) + " exhausted");
            }
            else if (_exhaustedNotified.Count > 0)
            {
                _exhaustedNotified.Remove(b.Id);
            }
        }

        /// <summary>Terrain factor for one output; fills extraction info into the plan when the output depletes tiles.</summary>
        private static float EvaluateTerrain(WorldMap map, PlacementRule rule, ResourceType res, int ox, int oy, int w, int h, ref OutputPlan plan, ref float fertCache)
        {
            float factor = 1f;
            bool fertile = (rule & PlacementRule.Fertile) != 0;
            if (fertile)
            {
                if (fertCache < 0f) fertCache = ProductionTerrain.FertilityFactor(map, ox, oy, w, h);
                factor *= fertCache;
            }

            if (res == ResourceType.Food)
            {
                // Farms (Fertile) are judged by soil only; otherwise food comes from fishing or hunting.
                if (!fertile)
                {
                    float best = -1f;
                    if ((rule & (PlacementRule.NearWater | PlacementRule.Coastal | PlacementRule.NearRiver)) != 0)
                        best = ProductionTerrain.FishingFactor(ProductionTerrain.CountWater(map, ox, oy, w, h, ProductionTerrain.FishingRadius));
                    if ((rule & PlacementRule.NearForest) != 0)
                        best = Math.Max(best, ProductionTerrain.HuntingFactor(ProductionTerrain.CountFlag(map, ox, oy, w, h, ProductionTerrain.HuntingRadius, TileFlags.Trees)));
                    if (best >= 0f) factor *= best;
                }
                return factor;
            }

            TileFlags mask;
            int radius;
            if (res == ResourceType.Wood && (rule & PlacementRule.NearForest) != 0)
            {
                mask = TileFlags.Trees;
                radius = ProductionTerrain.WoodRadius;
            }
            else
            {
                mask = ProductionTerrain.DepositFlagsFor(rule, res);
                if (mask == TileFlags.None) mask = ProductionTerrain.DepositFlagsOf(rule); // by-product of the deposit
                radius = ProductionTerrain.DepositRadius;
            }
            if (mask == TileFlags.None) return factor;

            int n = ProductionTerrain.CountHarvestable(map, ox, oy, w, h, radius, mask, out float available);
            plan.Mask = mask;
            plan.Radius = radius;
            plan.Harvestable = n;
            plan.Available = available;
            factor *= mask == TileFlags.Trees ? ProductionTerrain.WoodFactor(n) : ProductionTerrain.DepositFactor(n);
            return factor;
        }

        // ------------------------------------------------------------------ foraging

        private static void Forage(WorldState state, int dt, float cfgMult, float weather)
        {
            var settlements = state.Settlements.Items;
            for (int si = 0; si < settlements.Count; si++)
            {
                var s = settlements[si];
                if (s == null || !s.IsAlive) continue;
                int foragers = 0;
                var residents = s.ResidentIds;
                for (int r = 0; r < residents.Count; r++)
                {
                    var p = state.People.Get(residents[r]);
                    if (p != null && p.IsAlive && p.IsAdult && p.Job == JobType.None && p.ArmyId < 0) foragers++;
                }
                if (foragers == 0) continue;
                var civ = state.Civilizations.Get(s.CivilizationId);
                float rate = civ == null || civ.Age <= AgeId.Bronze ? EarlyForagePerTick : LateForagePerTick;
                float mod = civ != null ? MathUtil.Clamp(MathUtil.Sanitize(civ.Modifiers.Production[(int)ResourceType.Food], 1f), 0f, 10f) : 1f;
                float amount = MathUtil.Sanitize(foragers * rate * weather * mod * cfgMult * dt);
                if (amount > 0f) s.Stockpile.Add(ResourceType.Food, amount);
            }
        }

        // ------------------------------------------------------------------ housekeeping

        private void PruneNotified(WorldState state)
        {
            if (_exhaustedNotified.Count <= ExhaustedPruneThreshold) return;
            _pruneScratch.Clear();
            foreach (int id in _exhaustedNotified)
            {
                var b = state.Buildings.Get(id);
                if (b == null || b.IsDestroyed) _pruneScratch.Add(id);
            }
            for (int i = 0; i < _pruneScratch.Count; i++) _exhaustedNotified.Remove(_pruneScratch[i]);
            _pruneScratch.Clear();
        }
    }
}
