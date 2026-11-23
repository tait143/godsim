using System;
using System.Collections.Generic;

namespace GodSim.Core.Population
{
    /// <summary>
    /// Staffs a settlement's workplaces. The workforce is every alive adult resident (elders included,
    /// except as soldiers; children never work; people away with an army keep their post). Workplaces are
    /// complete, standing buildings with <see cref="BuildingDefinition.Job"/>/<see cref="BuildingDefinition.JobSlots"/>.
    /// Builders (job <see cref="JobType.Builder"/>, no workplace) are kept at <see cref="Settlement.DesiredBuilders"/>.
    /// <para>
    /// Allocation is priority weighted: weight(job) = JobPriority[job] when positive, else 1, times 3 for food
    /// jobs while the stockpile holds fewer than 15 days of food. Open slots are filled in descending weight
    /// (round-robin across buildings of equal weight), first from the unemployed, then by moving workers out of
    /// strictly lower-weight jobs, at most 15% of the workforce per run. Existing workers keep their posts unless
    /// their building lost the slot; people who already did a job are preferred for it and keep their experience.
    /// </para>
    /// Writes <see cref="Settlement.Unemployed"/> and <see cref="Settlement.Soldiers"/>, and grows job experience.
    /// </summary>
    public sealed class JobAssignmentSystem : ISimSystem
    {
        /// <summary>Run interval in ticks.</summary>
        public const int Interval = 6;
        /// <summary>Largest share of the workforce moved between jobs in one run.</summary>
        public const float MaxChurnFraction = 0.15f;
        /// <summary>Below this many days of food, food jobs get the emergency boost.</summary>
        public const float FoodEmergencyDays = 15f;
        /// <summary>Weight multiplier for food jobs during a food emergency.</summary>
        public const float FoodEmergencyBoost = 3f;
        /// <summary>Years of continuous work to reach full experience (1.0).</summary>
        public const float YearsToMastery = 3f;

        private struct Candidate
        {
            public Person P;
            public JobType PrevJob;
            public float PrevExperience;
            public float Score;
            public float Martial;
            public bool Elder;
            public bool Used;
        }

        private struct Mover
        {
            public Person P;
            public float Weight;
            public bool Used;
        }

        private struct Demand
        {
            public Building Workplace; // null = builders
            public JobType Job;
            public float Weight;
            public int Open;
            public int Order;
        }

        private readonly float[] _weights = new float[Counts.Jobs];
        private readonly List<Building> _workplaces = new List<Building>();
        private readonly List<Person> _employed = new List<Person>();
        private readonly List<Person> _builders = new List<Person>();
        private readonly List<Candidate> _candidates = new List<Candidate>();
        private readonly List<int> _generalOrder = new List<int>();
        private readonly List<int> _martialOrder = new List<int>();
        private readonly List<int> _withPrevJob = new List<int>();
        private readonly List<Mover> _movers = new List<Mover>();
        private readonly List<Demand> _demands = new List<Demand>();
        private readonly CandidateOrder _byScore;
        private readonly CandidateOrder _byMartial;
        private static readonly MoverComparer MoversFirst = new MoverComparer();
        private static readonly DemandComparer DemandsFirst = new DemandComparer();
        private static readonly BuilderReleaseComparer BuildersToRelease = new BuilderReleaseComparer();

        private int _generalCursor, _martialCursor, _moverCursor, _churnLeft;

        /// <summary>Creates the system.</summary>
        public JobAssignmentSystem()
        {
            _byScore = new CandidateOrder(_candidates, false);
            _byMartial = new CandidateOrder(_candidates, true);
        }

        /// <inheritdoc />
        public string Name => "Population.Jobs";

        /// <inheritdoc />
        public int TickInterval => Interval;

        /// <inheritdoc />
        public void Initialize(SimContext ctx) { }

        /// <inheritdoc />
        public void Tick(SimContext ctx)
        {
            if (ctx == null || ctx.State == null || ctx.Db == null || ctx.Ops == null) return;
            int dt = Math.Max(1, ctx.DeltaTicks);
            var settlements = ctx.State.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                UpdateSettlement(ctx, s, dt);
            }
        }

        /// <summary>Weight used to rank a job in a settlement (priority or 1, with the food emergency boost).</summary>
        public static float JobWeight(Settlement s, JobType job, bool foodEmergency)
        {
            int j = (int)job;
            float p = j >= 0 && j < s.JobPriority.Length ? MathUtil.Sanitize(s.JobPriority[j]) : 0f;
            float w = p > 0f ? Math.Min(p, 1000f) : 1f;
            if (foodEmergency && SocietyUtil.IsFoodJob(job)) w *= FoodEmergencyBoost;
            return w;
        }

        /// <summary>True when the settlement's food stockpile covers fewer than <see cref="FoodEmergencyDays"/> days.</summary>
        public static bool IsFoodEmergency(Settlement s) => SocietyUtil.FoodDays(s) < FoodEmergencyDays;

        /// <summary>Runs the full job pass for one settlement.</summary>
        public void UpdateSettlement(SimContext ctx, Settlement s, int deltaTicks)
        {
            var state = ctx.State;
            var db = ctx.Db;
            var ops = ctx.Ops;
            var people = state.People;
            ResetBuffers();

            bool emergency = IsFoodEmergency(s);
            for (int j = 0; j < _weights.Length; j++) _weights[j] = JobWeight(s, (JobType)j, emergency);

            // 1) Workplaces: drop dangling worker entries.
            var bids = s.BuildingIds;
            for (int i = 0; i < bids.Count; i++)
            {
                var b = state.Buildings.Get(bids[i]);
                if (b == null) continue;
                if (!IsWorkplace(db, b, s.Id))
                {
                    if (b.Workers.Count > 0) ReleaseAll(ops, people, b);
                    continue;
                }
                RemoveDangling(b, people);
                _workplaces.Add(b);
            }

            // 2) People: validate posts, collect candidates (unemployed), builders and employed.
            int workforce = 0;
            var residents = s.ResidentIds;
            for (int i = 0; i < residents.Count; i++)
            {
                var p = people.Get(residents[i]);
                if (p == null || !p.IsAlive || p.SettlementId != s.Id) continue;
                if (!p.IsAdult)
                {
                    if (p.Job != JobType.None || p.WorkplaceId >= 0) ops.ClearJob(p);
                    continue;
                }
                if (p.ArmyId >= 0) continue; // campaigning: keep the post, not part of the job market
                workforce++;
                p.Experience = SocietyUtil.Unit(p.Experience, 0f); // keeps the sort comparers consistent
                bool elder = p.Stage == LifeStage.Elder;
                JobType job = p.Job;
                if (job == JobType.None)
                {
                    if (p.WorkplaceId >= 0) ops.ClearJob(p);
                    AddCandidate(p, JobType.None, 0f);
                    continue;
                }
                if (job == JobType.Builder && p.WorkplaceId < 0)
                {
                    _builders.Add(p);
                    continue;
                }
                if (job == JobType.Soldier && elder) { Release(ops, p); continue; }
                // Every other job needs a valid workplace of the matching job type in this settlement.
                var w = p.WorkplaceId >= 0 ? state.Buildings.Get(p.WorkplaceId) : null;
                if (w == null || !IsWorkplace(db, w, s.Id) || db.Building(w.Type).Job != job || !w.Workers.Contains(p.Id))
                {
                    Release(ops, p);
                    continue;
                }
                _employed.Add(p);
            }

            // 3) Buildings that lost slots release their least experienced workers; excess builders too.
            for (int i = 0; i < _workplaces.Count; i++)
            {
                var b = _workplaces[i];
                int slots = db.Building(b.Type).JobSlots;
                int guard = 0;
                while (b.Workers.Count > slots && guard++ < 1024)
                {
                    var worst = LeastExperienced(b, people);
                    if (worst == null) break;
                    Release(ops, worst);
                }
            }
            int desiredBuilders = MathUtil.Clamp(s.DesiredBuilders, 0, workforce);
            if (_builders.Count > desiredBuilders)
            {
                _builders.Sort(BuildersToRelease);
                for (int i = desiredBuilders; i < _builders.Count; i++) Release(ops, _builders[i]);
                _builders.RemoveRange(desiredBuilders, _builders.Count - desiredBuilders);
            }

            // 4) Demands (open slots) and movers (employed people who may switch to a higher-weight job).
            for (int i = 0; i < _workplaces.Count; i++)
            {
                var b = _workplaces[i];
                var def = db.Building(b.Type);
                int open = def.JobSlots - b.Workers.Count;
                if (open > 0) _demands.Add(new Demand { Workplace = b, Job = def.Job, Weight = _weights[(int)def.Job], Open = open, Order = b.Id });
            }
            int builderOpen = desiredBuilders - _builders.Count;
            if (builderOpen > 0) _demands.Add(new Demand { Workplace = null, Job = JobType.Builder, Weight = _weights[(int)JobType.Builder], Open = builderOpen, Order = -1 });

            for (int i = 0; i < _employed.Count; i++)
            {
                var p = _employed[i];
                if (p.Job == JobType.None) continue; // released in step 3 (now a candidate)
                _movers.Add(new Mover { P = p, Weight = _weights[(int)p.Job] });
            }
            for (int i = 0; i < _builders.Count; i++) _movers.Add(new Mover { P = _builders[i], Weight = _weights[(int)JobType.Builder] });

            if (_demands.Count > 0)
            {
                _churnLeft = (int)Math.Floor(workforce * MaxChurnFraction);
                if (_churnLeft < 1 && emergency && workforce > 0) _churnLeft = 1; // a starving camp may always move one hand to food
                PrepareOrders();
                Allocate(ops);
            }

            // 5) Statistics and experience.
            float expGain = deltaTicks / (YearsToMastery * SimTime.TicksPerYear);
            int unemployed = 0, soldiers = 0, openJobs = 0;
            for (int i = 0; i < residents.Count; i++)
            {
                var p = people.Get(residents[i]);
                if (p == null || !p.IsAlive) continue;
                if (p.Job == JobType.Soldier) soldiers++;
                if (!p.IsAdult) continue;
                if (p.Job == JobType.None) { if (p.ArmyId < 0) unemployed++; continue; }
                float rate = expGain;
                if (p.HasTrait(PersonTraits.Clever)) rate *= 1.25f;
                if (p.HasTrait(PersonTraits.Lazy)) rate *= 0.75f;
                p.Experience = MathUtil.Clamp01(MathUtil.Sanitize(p.Experience) + rate);
            }
            for (int i = 0; i < _demands.Count; i++) openJobs += Math.Max(0, _demands[i].Open);
            s.Unemployed = unemployed;
            s.Soldiers = soldiers;
            s.Memory[SocietyKeys.Workforce] = workforce;
            s.Memory[SocietyKeys.OpenJobs] = openJobs;
            s.Memory[SocietyKeys.FoodEmergency] = emergency ? 1f : 0f;
            ResetBuffers();
        }

        // ------------------------------------------------------------------ allocation

        private void Allocate(WorldOps ops)
        {
            if (_demands.Count > 1) _demands.Sort(DemandsFirst);
            int gi = 0;
            while (gi < _demands.Count)
            {
                int gj = gi;
                float weight = _demands[gi].Weight;
                while (gj < _demands.Count && _demands[gj].Weight == weight) gj++;
                bool progress = true;
                while (progress)
                {
                    progress = false;
                    for (int k = gi; k < gj; k++)
                    {
                        var d = _demands[k];
                        if (d.Open <= 0) continue;
                        if (FillOne(ops, d)) { d.Open--; progress = true; }
                        else d.Open = -d.Open; // unfillable this run (negative = remaining unfilled)
                        _demands[k] = d;
                    }
                }
                for (int k = gi; k < gj; k++) { var d = _demands[k]; if (d.Open < 0) { d.Open = -d.Open; _demands[k] = d; } }
                gi = gj;
            }
        }

        private bool FillOne(WorldOps ops, Demand d)
        {
            int ci = TakeCandidate(d.Job);
            if (ci >= 0)
            {
                var c = _candidates[ci];
                bool restore = c.PrevJob == d.Job && c.PrevJob != JobType.None;
                if (ops.AssignJob(c.P, d.Workplace, d.Job))
                {
                    if (restore) c.P.Experience = MathUtil.Clamp01(c.PrevExperience);
                    return true;
                }
                c.Used = false; // give the person back to the pool
                _candidates[ci] = c;
                _generalCursor = 0;
                _martialCursor = 0;
                return false;
            }
            if (_churnLeft <= 0) return false;
            int mi = TakeMover(d.Job, d.Weight);
            if (mi < 0) return false;
            var m = _movers[mi];
            if (ops.AssignJob(m.P, d.Workplace, d.Job))
            {
                _churnLeft--;
                return true;
            }
            m.Used = false;
            _movers[mi] = m;
            return false;
        }

        private int TakeCandidate(JobType job)
        {
            bool soldier = job == JobType.Soldier;
            for (int i = 0; i < _withPrevJob.Count; i++)
            {
                int ci = _withPrevJob[i];
                var c = _candidates[ci];
                if (c.Used || c.PrevJob != job || (soldier && c.Elder)) continue;
                c.Used = true;
                _candidates[ci] = c;
                return ci;
            }
            var order = soldier ? _martialOrder : _generalOrder;
            int cursor = soldier ? _martialCursor : _generalCursor;
            while (cursor < order.Count && _candidates[order[cursor]].Used) cursor++;
            if (soldier) _martialCursor = cursor; else _generalCursor = cursor;
            if (cursor >= order.Count) return -1;
            int idx = order[cursor];
            var pick = _candidates[idx];
            pick.Used = true;
            _candidates[idx] = pick;
            return idx;
        }

        private int TakeMover(JobType job, float weight)
        {
            bool soldier = job == JobType.Soldier;
            while (_moverCursor < _movers.Count && _movers[_moverCursor].Used) _moverCursor++;
            for (int i = _moverCursor; i < _movers.Count; i++)
            {
                var m = _movers[i];
                if (m.Weight >= weight) return -1; // sorted ascending: nobody below this can be moved up
                if (m.Used || m.P.Job == job || !m.P.IsAlive) continue;
                if (soldier && m.P.Stage == LifeStage.Elder) continue;
                m.Used = true;
                _movers[i] = m;
                return i;
            }
            return -1;
        }

        private void PrepareOrders()
        {
            for (int i = 0; i < _candidates.Count; i++)
            {
                _generalOrder.Add(i);
                if (!_candidates[i].Elder) _martialOrder.Add(i);
                if (_candidates[i].PrevJob != JobType.None) _withPrevJob.Add(i);
            }
            if (_generalOrder.Count > 1) _generalOrder.Sort(_byScore);
            if (_martialOrder.Count > 1) _martialOrder.Sort(_byMartial);
            if (_movers.Count > 1) _movers.Sort(MoversFirst);
            _generalCursor = _martialCursor = _moverCursor = 0;
        }

        // ------------------------------------------------------------------ helpers

        private void AddCandidate(Person p, JobType prevJob, float prevExp)
        {
            float health = SocietyUtil.Unit(p.Health, 1f);
            float skill = MathUtil.Clamp(MathUtil.Sanitize(p.Skill, 1f), 0f, 3f);
            bool elder = p.Stage == LifeStage.Elder;
            float score = skill + 0.2f * health;
            if (p.HasTrait(PersonTraits.Clever)) score += 0.15f;
            if (p.HasTrait(PersonTraits.Strong)) score += 0.05f;
            if (p.HasTrait(PersonTraits.Hardy)) score += 0.05f;
            if (p.HasTrait(PersonTraits.Lazy)) score -= 0.25f;
            if (p.HasTrait(PersonTraits.Sickly)) score -= 0.1f;
            if (elder) score -= 0.15f;
            float martial = 0.5f * skill + 0.3f * health;
            if (p.HasTrait(PersonTraits.Strong)) martial += 0.4f;
            if (p.HasTrait(PersonTraits.Brave)) martial += 0.4f;
            if (p.HasTrait(PersonTraits.Hero)) martial += 0.6f;
            if (p.HasTrait(PersonTraits.Hardy)) martial += 0.15f;
            if (p.HasTrait(PersonTraits.Sickly)) martial -= 0.3f;
            if (p.HasTrait(PersonTraits.Lazy)) martial -= 0.15f;
            _candidates.Add(new Candidate { P = p, PrevJob = prevJob, PrevExperience = prevExp, Score = score, Martial = martial, Elder = elder });
        }

        private void Release(WorldOps ops, Person p)
        {
            JobType prev = p.Job;
            float exp = SocietyUtil.Unit(p.Experience, 0f);
            ops.ClearJob(p);
            AddCandidate(p, prev, exp);
        }

        private static bool IsWorkplace(GameDatabase db, Building b, int settlementId)
        {
            if (b.IsDestroyed || !b.IsComplete || b.SettlementId != settlementId) return false;
            return db.TryGetBuilding(b.Type, out var def) && def.HasJobs;
        }

        private static Person LeastExperienced(Building b, EntityStore<Person> people)
        {
            Person worst = null;
            for (int i = 0; i < b.Workers.Count; i++)
            {
                var p = people.Get(b.Workers[i]);
                // Never strip a soldier who is away campaigning; foreign entries are fixed by their own settlement's pass.
                if (p == null || p.ArmyId >= 0 || p.SettlementId != b.SettlementId) continue;
                if (worst == null || p.Experience < worst.Experience || (p.Experience == worst.Experience && p.Id > worst.Id)) worst = p;
            }
            return worst;
        }

        /// <summary>Removes worker ids that no longer point back at this building (dead, missing, duplicated).</summary>
        private static void RemoveDangling(Building b, EntityStore<Person> people)
        {
            var list = b.Workers;
            for (int i = list.Count - 1; i >= 0; i--)
            {
                int pid = list[i];
                bool duplicate = false;
                for (int j = 0; j < i; j++) if (list[j] == pid) { duplicate = true; break; }
                var p = people.Get(pid);
                // No WorldOps handle exists for an entry whose person does not reference this building: drop it.
                if (duplicate || p == null || !p.IsAlive || p.WorkplaceId != b.Id) list.RemoveAt(i);
            }
        }

        private static void ReleaseAll(WorldOps ops, EntityStore<Person> people, Building b)
        {
            var list = b.Workers;
            int guard = 0;
            while (list.Count > 0 && guard++ < 1024)
            {
                var p = people.Get(list[list.Count - 1]);
                if (p != null && p.WorkplaceId == b.Id) ops.ClearJob(p);
                else list.RemoveAt(list.Count - 1);
            }
        }

        private void ResetBuffers()
        {
            _workplaces.Clear();
            _employed.Clear();
            _builders.Clear();
            _candidates.Clear();
            _generalOrder.Clear();
            _martialOrder.Clear();
            _withPrevJob.Clear();
            _movers.Clear();
            _demands.Clear();
            _generalCursor = _martialCursor = _moverCursor = _churnLeft = 0;
        }

        // ------------------------------------------------------------------ comparers (allocated once)

        private sealed class CandidateOrder : IComparer<int>
        {
            private readonly List<Candidate> _list;
            private readonly bool _martial;
            public CandidateOrder(List<Candidate> list, bool martial) { _list = list; _martial = martial; }
            public int Compare(int a, int b)
            {
                var ca = _list[a];
                var cb = _list[b];
                float sa = _martial ? ca.Martial : ca.Score;
                float sb = _martial ? cb.Martial : cb.Score;
                if (sa != sb) return sa > sb ? -1 : 1;
                return ca.P.Id.CompareTo(cb.P.Id);
            }
        }

        private sealed class MoverComparer : IComparer<Mover>
        {
            public int Compare(Mover a, Mover b)
            {
                if (a.Weight != b.Weight) return a.Weight < b.Weight ? -1 : 1;
                if (a.P.Experience != b.P.Experience) return a.P.Experience < b.P.Experience ? -1 : 1;
                return a.P.Id.CompareTo(b.P.Id);
            }
        }

        private sealed class DemandComparer : IComparer<Demand>
        {
            public int Compare(Demand a, Demand b)
            {
                if (a.Weight != b.Weight) return a.Weight > b.Weight ? -1 : 1;
                return a.Order.CompareTo(b.Order);
            }
        }

        private sealed class BuilderReleaseComparer : IComparer<Person>
        {
            // Keep the most experienced builders: sort so the ones to keep come first.
            public int Compare(Person a, Person b)
            {
                if (a.Experience != b.Experience) return a.Experience > b.Experience ? -1 : 1;
                float sa = MathUtil.Sanitize(a.Skill, 1f), sb = MathUtil.Sanitize(b.Skill, 1f);
                if (sa != sb) return sa > sb ? -1 : 1;
                return a.Id.CompareTo(b.Id);
            }
        }
    }
}
