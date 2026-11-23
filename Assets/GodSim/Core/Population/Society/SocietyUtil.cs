using System;
using System.Collections.Generic;

namespace GodSim.Core.Population
{
    /// <summary>Shared, allocation-free helpers for the society systems (households, validity checks, food math).</summary>
    internal static class SocietyUtil
    {
        /// <summary>Food eaten per person per tick used for the "days of food" estimate.</summary>
        public const float FoodPerPersonPerTick = 0.05f;

        /// <summary>Days of food left in the stockpile: food / max(0.01, population * 0.05).</summary>
        public static float FoodDays(Settlement s)
        {
            float food = MathUtil.Sanitize(s.Stockpile.Get(ResourceType.Food));
            if (food < 0f) food = 0f;
            return food / Math.Max(0.01f, s.Population * FoodPerPersonPerTick);
        }

        /// <summary>True for the jobs that put food on the table (boosted during food emergencies).</summary>
        public static bool IsFoodJob(JobType job) =>
            job == JobType.Gatherer || job == JobType.Hunter || job == JobType.Fisher || job == JobType.Farmer || job == JobType.Herder;

        /// <summary>Clamped, sanitized service coverage (0..2) for a settlement.</summary>
        public static float Coverage(Settlement s, ServiceType svc)
        {
            int i = (int)svc;
            if (i < 0 || i >= s.ServiceCoverage.Length) return 0f;
            return MathUtil.Clamp(MathUtil.Sanitize(s.ServiceCoverage[i]), 0f, 2f);
        }

        /// <summary>Race definition or null when not registered (never throws).</summary>
        public static RaceDefinition RaceOf(GameDatabase db, RaceId race)
        {
            var races = db.Races;
            int i = (int)race;
            return i >= 0 && i < races.Count ? races[i] : null;
        }

        /// <summary>True if the building is a complete, standing dwelling of the given settlement.</summary>
        public static bool IsUsableDwelling(GameDatabase db, Building b, int settlementId)
        {
            if (b == null || b.IsDestroyed || !b.IsComplete || b.SettlementId != settlementId) return false;
            return db.TryGetBuilding(b.Type, out var def) && def.IsDwelling && b.HousingCapacity > 0;
        }

        /// <summary>Free housing of a settlement computed directly from its buildings (fallback when the blackboard is empty).</summary>
        public static int ComputeFreeHousing(WorldState state, GameDatabase db, Settlement s)
        {
            int free = 0;
            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (IsUsableDwelling(db, b, s.Id)) free += b.FreeHousing;
            }
            return free;
        }

        /// <summary>Free housing from the blackboard (<see cref="SocietyKeys.FreeHousing"/>), computed directly if the key is missing.</summary>
        public static int FreeHousingOf(WorldState state, GameDatabase db, Settlement s)
        {
            if (s.Memory.TryGetValue(SocietyKeys.FreeHousing, out float v))
            {
                v = MathUtil.Sanitize(v);
                return v <= 0f ? 0 : (int)Math.Min(v, 1000000f);
            }
            return ComputeFreeHousing(state, db, s);
        }

        /// <summary>
        /// The household anchor of an adult: a married couple living in the same settlement is anchored on
        /// the wife (or the lower id for same-sex couples); everyone else anchors on themselves.
        /// </summary>
        public static int AdultAnchor(Person p, EntityStore<Person> people)
        {
            if (p.SpouseId >= 0)
            {
                var sp = people.Get(p.SpouseId);
                if (sp != null && sp.IsAlive && sp.SettlementId == p.SettlementId && sp.SpouseId == p.Id)
                {
                    if (sp.Sex != p.Sex) return p.Sex == Sex.Female ? p.Id : sp.Id;
                    return Math.Min(p.Id, sp.Id);
                }
            }
            return p.Id;
        }

        /// <summary>
        /// Household anchor of any person. Children follow their mother (or their father when the mother is gone)
        /// as long as that parent lives in the same settlement.
        /// </summary>
        public static int HouseholdAnchor(Person p, EntityStore<Person> people)
        {
            if (p.Stage == LifeStage.Child)
            {
                var m = p.MotherId >= 0 ? people.Get(p.MotherId) : null;
                if (m != null && m.IsAlive && m.SettlementId == p.SettlementId) return AdultAnchor(m, people);
                var f = p.FatherId >= 0 ? people.Get(p.FatherId) : null;
                if (f != null && f.IsAlive && f.SettlementId == p.SettlementId) return AdultAnchor(f, people);
                return p.Id;
            }
            return AdultAnchor(p, people);
        }

        /// <summary>Clamps a value into [0,1], replacing NaN/Infinity with the fallback.</summary>
        public static float Unit(float v, float fallback) => MathUtil.Clamp01(MathUtil.Sanitize(v, fallback));
    }

    /// <summary>One person in a household listing.</summary>
    internal struct SocietyMember
    {
        public Person Person;
        public int Anchor;
        /// <summary>0 = the anchor, 1 = other adult (spouse), 2 = child.</summary>
        public int Rank;
    }

    /// <summary>A contiguous run of <see cref="SocietyMember"/>s sharing an anchor.</summary>
    internal struct SocietyHousehold
    {
        public int Start;
        public int Count;
        public int Anchor;
        public int Homeless;
        public bool HasAdult;
        public bool Locked;
        public float HappinessSum;
        public float AvgHappiness => Count > 0 ? HappinessSum / Count : 0f;
    }

    /// <summary>Reusable buffer that groups people into households without per-run allocations.</summary>
    internal sealed class SocietyHouseholdBuffer
    {
        public readonly List<SocietyMember> Members = new List<SocietyMember>();
        public readonly List<SocietyHousehold> Groups = new List<SocietyHousehold>();
        private static readonly MemberComparer ByAnchor = new MemberComparer();

        public void Clear() { Members.Clear(); Groups.Clear(); }

        public void Add(Person p, int anchor)
        {
            int rank = p.Id == anchor ? 0 : (p.IsAdult ? 1 : 2);
            Members.Add(new SocietyMember { Person = p, Anchor = anchor, Rank = rank });
        }

        /// <summary>Sorts members by (anchor, rank, id) and builds the household runs.</summary>
        public void BuildGroups(int leaderId)
        {
            Groups.Clear();
            if (Members.Count > 1) Members.Sort(ByAnchor);
            int i = 0;
            while (i < Members.Count)
            {
                int anchor = Members[i].Anchor;
                var g = new SocietyHousehold { Start = i, Anchor = anchor };
                while (i < Members.Count && Members[i].Anchor == anchor)
                {
                    var p = Members[i].Person;
                    g.Count++;
                    if (p.HomeId < 0) g.Homeless++;
                    if (p.IsAdult) g.HasAdult = true;
                    if (p.ArmyId >= 0 || p.Id == leaderId) g.Locked = true;
                    g.HappinessSum += SocietyUtil.Unit(p.Happiness, 0.5f);
                    i++;
                }
                Groups.Add(g);
            }
        }

        private sealed class MemberComparer : IComparer<SocietyMember>
        {
            public int Compare(SocietyMember a, SocietyMember b)
            {
                if (a.Anchor != b.Anchor) return a.Anchor < b.Anchor ? -1 : 1;
                if (a.Rank != b.Rank) return a.Rank < b.Rank ? -1 : 1;
                return a.Person.Id.CompareTo(b.Person.Id);
            }
        }
    }
}
