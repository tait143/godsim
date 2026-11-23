using System;

namespace GodSim.Core.AI
{
    /// <summary>What a defensive building contributes, derived from its definition.</summary>
    public enum DefenseAdvisorRole : byte
    {
        /// <summary>Not a defensive building.</summary>
        None = 0,
        /// <summary>Walls, palisades, towers: Defense service with at most a few soldier posts.</summary>
        Fortification = 1,
        /// <summary>Barracks, castles, bases: four or more soldier posts.</summary>
        Garrison = 2,
        /// <summary>Weapon producers (armory): makes soldiers fight harder.</summary>
        Arms = 3,
    }

    /// <summary>
    /// Data-driven table of every defensive building in the <see cref="GameDatabase"/>, built from
    /// <see cref="BuildingDefinition"/> fields (Defense services, Soldier job slots, Weapons outputs, cost, build work).
    /// Nothing is hard-coded by type, so new or modded military buildings are picked up automatically and the
    /// age ladder (Palisade -> Watchtower -> Stone Wall -> Castle -> Military Base) emerges from availability and
    /// efficiency. Rebuilt only when the database changes; never allocates after construction.
    /// </summary>
    public sealed class DefenseAdvisorCatalog
    {
        /// <summary>Defense value credited per soldier post (soldiers fight at roughly 1 strength each).</summary>
        public const float ValuePerSoldierSlot = 1f;

        private readonly BuildingType[] _types = new BuildingType[Counts.Buildings];
        private readonly DefenseAdvisorRole[] _roles = new DefenseAdvisorRole[Counts.Buildings];
        private readonly float[] _slow = new float[Counts.Buildings];
        private readonly float[] _fast = new float[Counts.Buildings];
        private readonly float[] _maxSlow = new float[4];
        private readonly float[] _maxFast = new float[4];
        private GameDatabase _db;
        private bool _builtFrozen;

        /// <summary>Defense value per building type (Defense service + soldier posts), 0 when not defensive.</summary>
        public readonly float[] DefenseByType = new float[Counts.Buildings];
        /// <summary>Defense service points per building type.</summary>
        public readonly float[] ServiceDefenseByType = new float[Counts.Buildings];
        /// <summary>Soldier job slots per building type.</summary>
        public readonly int[] SoldierSlotsByType = new int[Counts.Buildings];

        /// <summary>Number of candidate defensive buildings.</summary>
        public int Count { get; private set; }

        /// <summary>Building type of candidate <paramref name="i"/>.</summary>
        public BuildingType TypeAt(int i) => _types[i];
        /// <summary>Role of candidate <paramref name="i"/>.</summary>
        public DefenseAdvisorRole RoleAt(int i) => _roles[i];

        /// <summary>
        /// Preference score of candidate <paramref name="i"/> (0..~1.8): long-term value per cost when calm,
        /// value per build work (fast to raise) when danger is imminent, with a small bonus for newer technology.
        /// </summary>
        public float Score(int i, float imminence, GameDatabase db)
        {
            float score = MathUtil.Lerp(_slow[i], _fast[i], imminence);
            if (db != null && db.TryGetBuilding(_types[i], out var def)) score *= 1f + 0.08f * (int)def.MinAge;
            return MathUtil.Sanitize(score);
        }

        /// <summary>Builds the table if the database changed (or is still mutable, e.g. in tests).</summary>
        public void EnsureBuilt(GameDatabase db)
        {
            if (db != null && ReferenceEquals(db, _db) && _builtFrozen && db.IsFrozen) return;
            Rebuild(db);
        }

        /// <summary>Re-derives every candidate from the database definitions.</summary>
        public void Rebuild(GameDatabase db)
        {
            _db = db;
            _builtFrozen = db != null && db.IsFrozen;
            Count = 0;
            for (int i = 0; i < DefenseByType.Length; i++) { DefenseByType[i] = 0f; ServiceDefenseByType[i] = 0f; SoldierSlotsByType[i] = 0; }
            for (int r = 0; r < _maxSlow.Length; r++) { _maxSlow[r] = 0f; _maxFast[r] = 0f; }
            if (db == null) return;

            for (int i = 0; i < Counts.Buildings; i++)
            {
                var type = (BuildingType)i;
                if (type == BuildingType.TownCenter) continue;
                if (!db.TryGetBuilding(type, out var def)) continue;
                if (def.IsWonder || def.UniquePerCivilization || def.IsDwelling) continue;

                float defense = 0f, servicesTotal = 0f;
                var svc = def.Services;
                if (svc != null)
                    for (int k = 0; k < svc.Length; k++)
                    {
                        float a = Math.Max(0f, MathUtil.Sanitize(svc[k].Amount));
                        servicesTotal += a;
                        if (svc[k].Type == ServiceType.Defense) defense += a;
                    }
                int slots = def.Job == JobType.Soldier ? Math.Max(0, def.JobSlots) : 0;
                float weapons = 0f;
                var outs = def.OutputsPerWorker;
                if (outs != null)
                    for (int k = 0; k < outs.Length; k++)
                        if (outs[k].Type == ResourceType.Weapons) weapons += Math.Max(0f, MathUtil.Sanitize(outs[k].Amount));

                bool militaryish = def.Category == BuildingCategory.Military || slots > 0 || (defense > 0f && defense >= 0.5f * servicesTotal);
                var role = DefenseAdvisorRole.None;
                if (slots >= 4) role = DefenseAdvisorRole.Garrison;
                else if (militaryish && (defense > 0f || slots > 0)) role = DefenseAdvisorRole.Fortification;
                else if (weapons > 0f && def.JobSlots > 0) role = DefenseAdvisorRole.Arms;
                if (role == DefenseAdvisorRole.None) continue;

                float value = role == DefenseAdvisorRole.Arms ? weapons * def.JobSlots : defense + slots * ValuePerSoldierSlot;
                if (value <= 0f) continue;
                if (role != DefenseAdvisorRole.Arms)
                {
                    DefenseByType[i] = value;
                    ServiceDefenseByType[i] = defense;
                    SoldierSlotsByType[i] = slots;
                }

                float cost = 0f;
                var c = def.Cost;
                if (c != null)
                    for (int k = 0; k < c.Length; k++)
                        cost += Math.Max(0f, MathUtil.Sanitize(c[k].Amount)) * (c[k].Type == ResourceType.Gold ? 2f : 1f);
                float work = Math.Max(0f, MathUtil.Sanitize(def.BuildWork));
                cost += 0.5f * work;

                int n = Count++;
                _types[n] = type;
                _roles[n] = role;
                _slow[n] = MathUtil.Sanitize(value / (float)Math.Sqrt(Math.Max(1f, cost)));
                _fast[n] = MathUtil.Sanitize(value / (work + 15f));
                int ri = (int)role;
                if (_slow[n] > _maxSlow[ri]) _maxSlow[ri] = _slow[n];
                if (_fast[n] > _maxFast[ri]) _maxFast[ri] = _fast[n];
            }

            for (int n = 0; n < Count; n++)
            {
                int ri = (int)_roles[n];
                _slow[n] = _maxSlow[ri] > 0f ? _slow[n] / _maxSlow[ri] : 0f;
                _fast[n] = _maxFast[ri] > 0f ? _fast[n] / _maxFast[ri] : 0f;
            }
        }
    }
}
