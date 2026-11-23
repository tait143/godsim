using System;

namespace GodSim.Core.AI
{
    /// <summary>Coverage snapshot of one service in one settlement, as judged by the infrastructure advisor.</summary>
    public struct InfraCoverage
    {
        /// <summary>Coverage published by the economy (sanitized, 0..3).</summary>
        public float Current;
        /// <summary>Current plus what queued / under-construction providers will add.</summary>
        public float Effective;
        /// <summary>Effective coverage diluted by the projected population growth over the lookahead.</summary>
        public float Projected;
        /// <summary>Service points per tick needed for coverage 1.</summary>
        public float Demand;
        /// <summary>Coverage recoverable just by staffing existing providers.</summary>
        public float StaffGap;
        /// <summary>Civ service modifier applied to provider output.</summary>
        public float Modifier;
    }

    /// <summary>
    /// Reusable, allocation-free per-settlement scan of buildings that matter for infrastructure: counts of existing and
    /// planned (queued or under construction) buildings, staffing, pending service points, industry output and planned
    /// resource production. One instance per advisor; <see cref="Scan"/> overwrites everything.
    /// </summary>
    public sealed class InfraSurvey
    {
        public readonly int[] Existing = new int[Counts.Buildings];
        public readonly int[] Planned = new int[Counts.Buildings];
        public readonly int[] Workers = new int[Counts.Buildings];
        public readonly int[] Slots = new int[Counts.Buildings];
        /// <summary>Complete providers per service (used to detect losses).</summary>
        public readonly int[] ProviderCount = new int[Counts.Services];
        /// <summary>Raw service points per tick that planned providers will add.</summary>
        public readonly float[] PendingPoints = new float[Counts.Services];
        /// <summary>Raw service points per tick lost to understaffed providers.</summary>
        public readonly float[] StaffGapPoints = new float[Counts.Services];
        /// <summary>Expected production per tick of planned buildings, indexed by (int)ResourceType.</summary>
        public readonly float[] PlannedOutput = new float[Counts.Resources];

        public int IndustryCount;
        public int PlannedIndustryCount;
        /// <summary>Output units per tick of complete Industry buildings (actual or assumed staffing).</summary>
        public float IndustryOutput;
        public float PlannedIndustryOutput;

        public int CountWithPlanned(int type) => type >= 0 && type < Existing.Length ? Existing[type] + Planned[type] : 0;

        public void Scan(WorldState state, InfraCatalog cat, Settlement s, float assumedStaffing)
        {
            Array.Clear(Existing, 0, Existing.Length);
            Array.Clear(Planned, 0, Planned.Length);
            Array.Clear(Workers, 0, Workers.Length);
            Array.Clear(Slots, 0, Slots.Length);
            Array.Clear(ProviderCount, 0, ProviderCount.Length);
            Array.Clear(PendingPoints, 0, PendingPoints.Length);
            Array.Clear(StaffGapPoints, 0, StaffGapPoints.Length);
            Array.Clear(PlannedOutput, 0, PlannedOutput.Length);
            IndustryCount = PlannedIndustryCount = 0;
            IndustryOutput = PlannedIndustryOutput = 0f;
            if (state == null || cat == null || s == null) return;
            float assumed = MathUtil.Clamp01(MathUtil.Sanitize(assumedStaffing, 0.5f));

            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                var def = cat.Def(t);
                if (def == null) continue;
                if (b.IsComplete) AddComplete(cat, b, def, t, assumed);
                else AddPlanned(cat, def, t, assumed);
            }
            var queue = s.BuildQueue;
            for (int i = 0; i < queue.Count; i++)
            {
                var o = queue[i];
                if (o == null) continue;
                int t = (int)o.Type;
                var def = cat.Def(t);
                if (def != null) AddPlanned(cat, def, t, assumed);
            }
        }

        private void AddComplete(InfraCatalog cat, Building b, BuildingDefinition def, int t, float assumed)
        {
            Existing[t]++;
            int slots = def.HasJobs ? def.JobSlots : 0;
            int workers = Math.Min(b.Workers.Count, Math.Max(0, slots));
            Workers[t] += workers;
            Slots[t] += slots;
            float unstaffed = slots > 0 ? 1f - workers / (float)slots : 0f;
            for (int si = 0; si < Counts.Services; si++)
            {
                float amt = cat.Service(t, (ServiceType)si);
                if (amt <= 0f) continue;
                ProviderCount[si]++;
                StaffGapPoints[si] += amt * unstaffed;
            }
            if (cat.IsIndustry[t])
            {
                IndustryCount++;
                float staffed = Math.Max(workers, slots * assumed);
                IndustryOutput += cat.OutputPerWorker[t] * staffed;
            }
        }

        private void AddPlanned(InfraCatalog cat, BuildingDefinition def, int t, float assumed)
        {
            Planned[t]++;
            for (int si = 0; si < Counts.Services; si++) PendingPoints[si] += cat.Service(t, (ServiceType)si);
            float staffed = (def.HasJobs ? def.JobSlots : 0) * assumed;
            if (staffed > 0f)
                for (int ri = 0; ri < Counts.Resources; ri++)
                {
                    float o = cat.OutputOf(t, (ResourceType)ri);
                    if (o > 0f) PlannedOutput[ri] += o * staffed;
                }
            if (cat.IsIndustry[t])
            {
                PlannedIndustryCount++;
                PlannedIndustryOutput += cat.OutputPerWorker[t] * staffed;
            }
        }
    }
}
