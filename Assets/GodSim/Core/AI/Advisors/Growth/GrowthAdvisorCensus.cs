using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// One-pass snapshot of a settlement's demography, housing stock, pending dwellings and water supply, gathered by
    /// <see cref="GrowthAdvisor"/>. Reused between settlements and runs: arrays are allocated once, Collect allocates nothing.
    /// </summary>
    public sealed class GrowthAdvisorCensus
    {
        /// <summary>Non-destroyed buildings plus queued orders, per type.</summary>
        public readonly int[] Existing = new int[Counts.Buildings];

        public int Population, Children, Adults, Elders;
        /// <summary>Adult (non-elder) women below their race's child limit.</summary>
        public int FertileWomen;
        /// <summary>Fertile women whose living spouse is in the same settlement.</summary>
        public int Couples;
        /// <summary>Residents without a standing home.</summary>
        public int Homeless;
        /// <summary>Residents born after the last trend sample.</summary>
        public int NewbornsSinceSample;

        public int CompleteDwellings, EmptyDwellings, HousingCapacity;
        /// <summary>Free beds computed from complete dwellings (fallback when population did not publish pop.freeHousing).</summary>
        public int ComputedFreeBeds;
        /// <summary>Dwellings under construction or queued.</summary>
        public int PendingDwellings;
        /// <summary>Expected beds of pending dwellings (rolled capacity when placed, else the definition mean, ~4).</summary>
        public float PendingBeds;

        /// <summary>A town center stands, is under construction or is queued.</summary>
        public bool HasTownCenter;

        public float WaterPointsComplete, WaterPointsPending;
        /// <summary>Pending buildings whose main service is water.</summary>
        public int WaterBuildingsPending;

        public void Clear()
        {
            Array.Clear(Existing, 0, Existing.Length);
            Population = Children = Adults = Elders = FertileWomen = Couples = Homeless = NewbornsSinceSample = 0;
            CompleteDwellings = EmptyDwellings = HousingCapacity = ComputedFreeBeds = PendingDwellings = 0;
            PendingBeds = 0f;
            HasTownCenter = false;
            WaterPointsComplete = WaterPointsPending = 0f;
            WaterBuildingsPending = 0;
        }

        /// <summary>Gathers the snapshot. Residents with BirthTick &gt; <paramref name="newbornAfterTick"/> count as newborns.</summary>
        public void Collect(WorldState state, GameDatabase db, Settlement s, long newbornAfterTick)
        {
            Clear();
            if (state == null || db == null || s == null) return;

            var res = s.ResidentIds;
            for (int i = 0; i < res.Count; i++)
            {
                var p = state.People.Get(res[i]);
                if (p == null || !p.IsAlive) continue;
                Population++;
                if (p.BirthTick > newbornAfterTick) NewbornsSinceSample++;
                if (p.HomeId < 0) Homeless++;
                else
                {
                    var home = state.Buildings.Get(p.HomeId);
                    if (home == null || home.IsDestroyed) Homeless++;
                }
                if (p.Stage == LifeStage.Child) { Children++; continue; }
                if (p.Stage == LifeStage.Elder) { Elders++; continue; }
                Adults++;
                if (p.Sex != Sex.Female) continue;
                var race = RaceOf(db, p.Race);
                int maxChildren = race != null ? race.MaxChildren : 5;
                if (p.ChildrenCount >= maxChildren) continue;
                FertileWomen++;
                if (p.SpouseId >= 0)
                {
                    var spouse = state.People.Get(p.SpouseId);
                    if (spouse != null && spouse.IsAlive && spouse.SettlementId == s.Id) Couples++;
                }
            }

            var ids = s.BuildingIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var b = state.Buildings.Get(ids[i]);
                if (b == null || b.IsDestroyed) continue;
                int t = (int)b.Type;
                if (t < 0 || t >= Counts.Buildings) continue;
                Existing[t]++;
                if (b.Type == BuildingType.TownCenter) HasTownCenter = true;
                if (!db.TryGetBuilding(b.Type, out var def)) continue;
                float water = GrowthAdvisorModel.WaterAmount(def);
                if (b.IsComplete)
                {
                    if (def.IsDwelling)
                    {
                        CompleteDwellings++;
                        int cap = Math.Max(0, b.HousingCapacity);
                        HousingCapacity += cap;
                        ComputedFreeBeds += Math.Max(0, cap - b.Residents.Count);
                        if (b.Residents.Count == 0 && cap > 0) EmptyDwellings++;
                    }
                    WaterPointsComplete += water;
                }
                else
                {
                    if (def.IsDwelling)
                    {
                        PendingDwellings++;
                        PendingBeds += b.HousingCapacity > 0 ? b.HousingCapacity : GrowthAdvisorModel.ExpectedBeds(def);
                    }
                    AddPendingWater(def, water);
                }
            }

            var queue = s.BuildQueue;
            for (int i = 0; i < queue.Count; i++)
            {
                var o = queue[i];
                if (o == null) continue;
                int t = (int)o.Type;
                if (t < 0 || t >= Counts.Buildings) continue;
                Existing[t]++;
                if (o.Type == BuildingType.TownCenter) HasTownCenter = true;
                if (!db.TryGetBuilding(o.Type, out var def)) continue;
                if (def.IsDwelling) { PendingDwellings++; PendingBeds += GrowthAdvisorModel.ExpectedBeds(def); }
                AddPendingWater(def, GrowthAdvisorModel.WaterAmount(def));
            }
        }

        private void AddPendingWater(BuildingDefinition def, float water)
        {
            if (water <= 0f) return;
            WaterPointsPending += water;
            if (GrowthAdvisorModel.IsPrimaryWater(def)) WaterBuildingsPending++;
        }

        internal static RaceDefinition RaceOf(GameDatabase db, RaceId race)
        {
            int i = (int)race;
            return db != null && i >= 0 && i < db.Races.Count ? db.Races[i] : null;
        }
    }
}
