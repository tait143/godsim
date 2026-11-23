using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Snapshot of a settlement's buildings, queue and workforce gathered once per governor run (and
    /// updated incrementally when the governor queues an order). Reused between settlements: all arrays
    /// are allocated once.
    /// </summary>
    public sealed class SettlementSurvey
    {
        /// <summary>Non-destroyed buildings per type (complete or under construction).</summary>
        public readonly int[] Existing = new int[Counts.Buildings];
        /// <summary>Complete buildings per type.</summary>
        public readonly int[] Complete = new int[Counts.Buildings];
        /// <summary>Placed but incomplete buildings per type.</summary>
        public readonly int[] UnderConstruction = new int[Counts.Buildings];
        /// <summary>Queued (not yet placed) orders per type.</summary>
        public readonly int[] Queued = new int[Counts.Buildings];
        /// <summary>Resources committed to queued orders and placed-but-unpaid buildings.</summary>
        public readonly float[] Reserved = new float[Counts.Resources];
        /// <summary>Resources committed to placed-but-unpaid buildings only (construction pays these first).</summary>
        public readonly float[] ReservedPlaced = new float[Counts.Resources];
        /// <summary>Complete + pending buildings with a net positive output, per resource.</summary>
        public readonly int[] Producers = new int[Counts.Resources];
        /// <summary>Estimated output per tick of complete buildings (by their staffing), per resource.</summary>
        public readonly float[] EstimatedOutput = new float[Counts.Resources];
        /// <summary>Service points of pending (queued or under construction) buildings.</summary>
        public readonly float[] PendingService = new float[Counts.Services];
        /// <summary>Net output of pending buildings at full staffing, per resource.</summary>
        public readonly float[] PendingOutput = new float[Counts.Resources];

        /// <summary>Residents.</summary>
        public int Population;
        /// <summary>Adult (and elder) residents.</summary>
        public int Adults;
        /// <summary>Adults without a job and not in an army.</summary>
        public int IdleAdults;
        /// <summary>Residents with any job (0 means job assignment has not run yet).</summary>
        public int Employed;
        /// <summary>Total housing capacity of complete dwellings.</summary>
        public int HousingCapacity;
        /// <summary>Residents assigned to complete dwellings.</summary>
        public int HousedResidents;
        /// <summary>Expected capacity of queued and under-construction dwellings.</summary>
        public float PendingHousing;
        /// <summary>Unfilled job slots in complete buildings.</summary>
        public int OpenJobSlots;
        /// <summary>Job slots of pending buildings.</summary>
        public int PendingJobSlots;
        /// <summary>Gold upkeep per tick of complete buildings.</summary>
        public float GoldUpkeep;
        /// <summary>Defense value of complete buildings (defense service + soldier slots).</summary>
        public float DefenseCapacity;
        /// <summary>Defense value of pending buildings.</summary>
        public float PendingDefense;
        /// <summary>Storage capacity of pending buildings.</summary>
        public float PendingStorage;
        /// <summary>Orders in the queue (all origins).</summary>
        public int QueueCount;
        /// <summary>Buildings under construction (all types).</summary>
        public int UnderConstructionTotal;
        /// <summary>True if a non-destroyed Town Center exists (complete or under construction).</summary>
        public bool HasTownCenter;

        private BuildingProfile[] _profiles;

        /// <summary>Gathers the snapshot. <paramref name="profiles"/> is indexed by (int)BuildingType (entries may be null).</summary>
        public void Collect(WorldState state, GameDatabase db, Settlement s, BuildingProfile[] profiles)
        {
            Clear();
            _profiles = profiles;
            if (state == null || s == null) return;

            // Workforce
            Population = s.Population;
            var res = s.ResidentIds;
            for (int i = 0; i < res.Count; i++)
            {
                var person = state.People.Get(res[i]);
                if (person == null || !person.IsAlive) continue;
                if (person.Job != JobType.None) Employed++;
                if (!person.IsAdult) continue;
                Adults++;
                if (person.Job == JobType.None && person.ArmyId < 0) IdleAdults++;
            }

            // Buildings (before job assignment has ever run, complete job buildings are assumed staffed)
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= Counts.Buildings) continue;
                Existing[t]++;
                var p = profiles != null && t < profiles.Length ? profiles[t] : null;
                if (b.Type == BuildingType.TownCenter) HasTownCenter = true;
                if (b.IsComplete)
                {
                    Complete[t]++;
                    if (p == null) continue;
                    var def = p.Def;
                    if (def.IsDwelling)
                    {
                        HousingCapacity += Math.Max(0, b.HousingCapacity);
                        HousedResidents += Math.Min(b.Residents.Count, Math.Max(0, b.HousingCapacity));
                    }
                    if (def.HasJobs)
                    {
                        OpenJobSlots += Math.Max(0, def.JobSlots - b.Workers.Count);
                        float staffed = Employed == 0 ? 1f : b.Workers.Count / (float)def.JobSlots;
                        AddEstimatedOutput(p, MathUtil.Clamp01(staffed));
                    }
                    else AddEstimatedOutput(p, 1f);
                    GoldUpkeep += Math.Max(0f, MathUtil.Sanitize(def.UpkeepGold));
                    DefenseCapacity += p.Defense;
                    AddProducer(p);
                }
                else
                {
                    UnderConstruction[t]++;
                    UnderConstructionTotal++;
                    if (p == null) continue;
                    if (!b.CostPaid) { Reserve(p.Def.Cost); Add(ReservedPlaced, p.Def.Cost); }
                    AddPending(p, b.HousingCapacity > 0 ? b.HousingCapacity : -1f);
                }
            }

            // Queue
            var queue = s.BuildQueue;
            QueueCount = queue.Count;
            for (int i = 0; i < queue.Count; i++)
            {
                var o = queue[i];
                if (o == null) continue;
                int t = (int)o.Type;
                if (t < 0 || t >= Counts.Buildings) continue;
                Queued[t]++;
                var p = profiles != null && t < profiles.Length ? profiles[t] : null;
                if (p == null) continue;
                Reserve(p.Def.Cost);
                AddPending(p, -1f);
            }

        }

        /// <summary>Records an order the governor just queued (keeps the snapshot consistent for the next pick).</summary>
        public void RegisterQueued(BuildingProfile p)
        {
            if (p == null) return;
            Queued[(int)p.Type]++;
            QueueCount++;
            Reserve(p.Def.Cost);
            AddPending(p, -1f);
        }

        /// <summary>Total of a type that exists or is queued (used for MaxPerSettlement).</summary>
        public int CountWithQueue(BuildingType t) => Existing[(int)t] + Queued[(int)t];

        /// <summary>Unemployed workers: the job module's count, or idle adults before job assignment has run.</summary>
        public int Unemployed(Settlement s)
        {
            int u = s != null ? Math.Max(0, s.Unemployed) : 0;
            if (u == 0 && Employed == 0) u = IdleAdults;
            return u;
        }

        private void Clear()
        {
            Array.Clear(Existing, 0, Existing.Length);
            Array.Clear(Complete, 0, Complete.Length);
            Array.Clear(UnderConstruction, 0, UnderConstruction.Length);
            Array.Clear(Queued, 0, Queued.Length);
            Array.Clear(Reserved, 0, Reserved.Length);
            Array.Clear(ReservedPlaced, 0, ReservedPlaced.Length);
            Array.Clear(Producers, 0, Producers.Length);
            Array.Clear(EstimatedOutput, 0, EstimatedOutput.Length);
            Array.Clear(PendingService, 0, PendingService.Length);
            Array.Clear(PendingOutput, 0, PendingOutput.Length);
            Population = Adults = IdleAdults = Employed = HousingCapacity = HousedResidents = 0;
            OpenJobSlots = PendingJobSlots = QueueCount = UnderConstructionTotal = 0;
            PendingHousing = GoldUpkeep = DefenseCapacity = PendingDefense = PendingStorage = 0f;
            HasTownCenter = false;
        }

        /// <summary>
        /// Fills <paramref name="into"/> with the resources an order of priority <paramref name="priority"/> must
        /// leave untouched: unpaid placed buildings plus queued orders the planner will serve first (priority &gt;= it).
        /// </summary>
        public void FillReservedAhead(Settlement s, float priority, float[] into)
        {
            if (into == null) return;
            Array.Copy(ReservedPlaced, into, Math.Min(into.Length, ReservedPlaced.Length));
            if (s == null || _profiles == null) return;
            var queue = s.BuildQueue;
            for (int i = 0; i < queue.Count; i++)
            {
                var o = queue[i];
                if (o == null || !(o.Priority >= priority)) continue;
                int t = (int)o.Type;
                if (t < 0 || t >= _profiles.Length || _profiles[t] == null) continue;
                Add(into, _profiles[t].Def.Cost);
            }
        }

        private void Reserve(ResourceAmount[] cost) => Add(Reserved, cost);

        private static void Add(float[] into, ResourceAmount[] cost)
        {
            if (cost == null) return;
            for (int i = 0; i < cost.Length; i++)
            {
                int r = (int)cost[i].Type;
                if (r < 0 || r >= into.Length) continue;
                float a = MathUtil.Sanitize(cost[i].Amount);
                if (a > 0f) into[r] += a;
            }
        }

        private void AddPending(BuildingProfile p, float rolledHousing)
        {
            var def = p.Def;
            if (def.IsDwelling) PendingHousing += rolledHousing > 0f ? rolledHousing : (p.ExpectedHousing > 0f ? p.ExpectedHousing : 4f);
            if (def.HasJobs) PendingJobSlots += def.JobSlots;
            for (int sv = 0; sv < Counts.Services; sv++) PendingService[sv] += p.Service[sv];
            for (int r = 0; r < Counts.Resources; r++) if (p.NetOutput[r] > 0f) PendingOutput[r] += p.NetOutput[r];
            PendingDefense += p.Defense;
            PendingStorage += p.Storage;
            AddProducer(p);
        }

        private void AddProducer(BuildingProfile p)
        {
            for (int r = 0; r < Counts.Resources; r++) if (p.NetOutput[r] > 0f) Producers[r]++;
        }

        private void AddEstimatedOutput(BuildingProfile p, float staffing)
        {
            if (staffing <= 0f) return;
            for (int r = 0; r < Counts.Resources; r++) if (p.NetOutput[r] > 0f) EstimatedOutput[r] += p.NetOutput[r] * staffing;
        }
    }
}
