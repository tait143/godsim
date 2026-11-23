using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Snapshot of one settlement's labor market: workforce, idle adults, open job slots per job, builders,
    /// soldiers, jobs in the construction pipeline and housing. Reused between settlements (arrays allocated once).
    /// </summary>
    public sealed class LaborMarketSurvey
    {
        /// <summary>Unfilled slots of complete job buildings, per JobType.</summary>
        public readonly int[] OpenByJob = new int[Counts.Jobs];
        /// <summary>Total slots of complete job buildings, per JobType.</summary>
        public readonly int[] SlotsByJob = new int[Counts.Jobs];
        /// <summary>Residents holding each job (including Builder and Soldier).</summary>
        public readonly int[] WorkersByJob = new int[Counts.Jobs];
        /// <summary>Job slots of buildings under construction or queued, per JobType (future demand).</summary>
        public readonly int[] PipelineByJob = new int[Counts.Jobs];
        /// <summary>Non-destroyed buildings per type (complete or under construction).</summary>
        public readonly int[] ExistingByType = new int[Counts.Buildings];
        /// <summary>Queued (not yet placed) orders per type.</summary>
        public readonly int[] QueuedByType = new int[Counts.Buildings];
        /// <summary>Under construction + queued per type.</summary>
        public readonly int[] PendingByType = new int[Counts.Buildings];

        public int Population, Adults, IdleAdults, Unemployed, Children, Youths;
        public int Builders, DesiredBuilders, Soldiers;
        /// <summary>Open slots of non-military jobs.</summary>
        public int OpenCivilian;
        /// <summary>Open Soldier slots.</summary>
        public int OpenMilitary;
        /// <summary>Pipeline slots of non-military jobs.</summary>
        public int PipelineCivilian;
        public int UnderConstruction, Queued;
        public int HousingCapacity, HousedResidents;
        public float PendingHousing;
        public bool AtWar;
        public float Threat;

        public bool ConstructionActive => UnderConstruction > 0 || Queued > 0 || DesiredBuilders > 0;
        public int BuilderGap => Math.Max(0, DesiredBuilders - Builders);
        public int FreeBeds => Math.Max(0, HousingCapacity - HousedResidents);

        /// <summary>Gathers the snapshot. Allocation-free; tolerant of stale ids and missing definitions.</summary>
        public void Gather(SimContext ctx, Civilization civ, Settlement s)
        {
            Array.Clear(OpenByJob, 0, OpenByJob.Length);
            Array.Clear(SlotsByJob, 0, SlotsByJob.Length);
            Array.Clear(WorkersByJob, 0, WorkersByJob.Length);
            Array.Clear(PipelineByJob, 0, PipelineByJob.Length);
            Array.Clear(ExistingByType, 0, ExistingByType.Length);
            Array.Clear(QueuedByType, 0, QueuedByType.Length);
            Array.Clear(PendingByType, 0, PendingByType.Length);
            Population = Adults = IdleAdults = Unemployed = Children = Youths = 0;
            Builders = DesiredBuilders = Soldiers = 0;
            OpenCivilian = OpenMilitary = PipelineCivilian = UnderConstruction = Queued = 0;
            HousingCapacity = HousedResidents = 0;
            PendingHousing = 0f;
            AtWar = false;
            Threat = 0f;
            if (ctx == null || s == null) return;
            var state = ctx.State;
            var db = ctx.Db;

            float adulthood = 16f;
            if (db != null && (int)s.Race < db.Races.Count && db.Races[(int)s.Race] != null) adulthood = db.Races[(int)s.Race].AdulthoodAge;

            // ---- workforce
            var residents = s.ResidentIds;
            for (int i = 0; i < residents.Count; i++)
            {
                var p = state.People.Get(residents[i]);
                if (p == null || !p.IsAlive) continue;
                Population++;
                if (!p.IsAdult)
                {
                    Children++;
                    if (p.AgeYears >= adulthood - 2f) Youths++;
                    continue;
                }
                Adults++;
                int j = (int)p.Job;
                if (p.Job != JobType.None && j < WorkersByJob.Length) WorkersByJob[j]++;
                else if (p.Job == JobType.None && p.ArmyId < 0 && p.Stage == LifeStage.Adult) IdleAdults++;
            }
            Unemployed = Math.Max(Math.Max(0, s.Unemployed), IdleAdults);
            Builders = WorkersByJob[(int)JobType.Builder];
            Soldiers = Math.Max(Math.Max(0, s.Soldiers), WorkersByJob[(int)JobType.Soldier]);
            DesiredBuilders = Math.Max(0, s.DesiredBuilders);

            // ---- buildings
            if (db != null)
            {
                var ids = s.BuildingIds;
                for (int i = 0; i < ids.Count; i++)
                {
                    var b = state.Buildings.Get(ids[i]);
                    if (b == null || b.IsDestroyed) continue;
                    if (!db.TryGetBuilding(b.Type, out var def)) continue;
                    int t = (int)b.Type;
                    ExistingByType[t]++;
                    if (!b.IsComplete)
                    {
                        UnderConstruction++;
                        PendingByType[t]++;
                        if (def.HasJobs) PipelineByJob[(int)def.Job] += def.JobSlots;
                        if (def.IsDwelling) PendingHousing += (def.HousingMin + def.HousingMax) * 0.5f;
                        continue;
                    }
                    if (def.IsDwelling) { HousingCapacity += b.HousingCapacity; HousedResidents += b.Residents.Count; }
                    if (def.HasJobs)
                    {
                        int jj = (int)def.Job;
                        SlotsByJob[jj] += def.JobSlots;
                        OpenByJob[jj] += Math.Max(0, def.JobSlots - b.Workers.Count);
                    }
                }

                // ---- queue (read only)
                var queue = s.BuildQueue;
                for (int i = 0; i < queue.Count; i++)
                {
                    var o = queue[i];
                    if (o == null || !db.TryGetBuilding(o.Type, out var def)) continue;
                    Queued++;
                    QueuedByType[(int)o.Type]++;
                    PendingByType[(int)o.Type]++;
                    if (def.HasJobs) PipelineByJob[(int)def.Job] += def.JobSlots;
                    if (def.IsDwelling) PendingHousing += (def.HousingMin + def.HousingMax) * 0.5f;
                }
            }

            for (int j = 1; j < OpenByJob.Length; j++)
            {
                if (j == (int)JobType.Soldier) { OpenMilitary += OpenByJob[j]; continue; }
                OpenCivilian += OpenByJob[j];
                PipelineCivilian += PipelineByJob[j];
            }

            // ---- war & threat
            if (civ != null)
            {
                Threat = MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(LaborKeys.CivThreat)));
                AtWar = civ.Mem(LaborKeys.CivWars) > 0.5f;
                var wars = state.Diplomacy.Wars;
                for (int i = 0; i < wars.Count && !AtWar; i++)
                {
                    var w = wars[i];
                    if (w != null && w.IsActive && (w.AttackerId == civ.Id || w.DefenderId == civ.Id)) AtWar = true;
                }
            }
        }
    }
}
