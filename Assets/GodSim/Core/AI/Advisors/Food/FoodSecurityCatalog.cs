using System;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Data-driven index of food-relevant building definitions: every building whose workers output Food (producers) and
    /// every storage building (capacity). Rebuilt only when the database changes (or while it is still mutable).
    /// </summary>
    public sealed class FoodSecurityCatalog
    {
        public readonly BuildingType[] Producers = new BuildingType[Counts.Buildings];
        public int ProducerCount;
        public readonly BuildingType[] Storage = new BuildingType[Counts.Buildings];
        public int StorageCount;

        /// <summary>Net food per staffed worker per tick (outputs minus food inputs), by building index.</summary>
        public readonly float[] FoodPerWorker = new float[Counts.Buildings];
        /// <summary>Non-food outputs per worker (secondary value, e.g. a pasture's cloth), by building index.</summary>
        public readonly float[] ExtraPerWorker = new float[Counts.Buildings];
        /// <summary>Multiplier &lt;= 1 penalising producers that consume non-food inputs.</summary>
        public readonly float[] InputFactor = new float[Counts.Buildings];
        public readonly bool[] IsProducer = new bool[Counts.Buildings];
        public readonly bool[] IsStorage = new bool[Counts.Buildings];
        /// <summary>Jobs that staff at least one food producer.</summary>
        public readonly bool[] IsFoodJob = new bool[Counts.Jobs];

        private GameDatabase _db;
        private bool _builtFrozen;

        public void Ensure(GameDatabase db)
        {
            if (db == null) { Clear(); _db = null; return; }
            if (ReferenceEquals(db, _db) && _builtFrozen) return;
            Rebuild(db);
        }

        private void Clear()
        {
            ProducerCount = StorageCount = 0;
            Array.Clear(FoodPerWorker, 0, FoodPerWorker.Length);
            Array.Clear(ExtraPerWorker, 0, ExtraPerWorker.Length);
            Array.Clear(IsProducer, 0, IsProducer.Length);
            Array.Clear(IsStorage, 0, IsStorage.Length);
            Array.Clear(IsFoodJob, 0, IsFoodJob.Length);
            for (int i = 0; i < InputFactor.Length; i++) InputFactor[i] = 1f;
        }

        private void Rebuild(GameDatabase db)
        {
            Clear();
            _db = db;
            _builtFrozen = db.IsFrozen;
            var defs = db.Buildings;
            int n = Math.Min(defs.Count, Counts.Buildings);
            for (int i = 0; i < n; i++)
            {
                var d = defs[i];
                if (d == null) continue;
                if (d.Category == BuildingCategory.Storage && d.StorageCapacity > 0f && !d.IsWonder)
                {
                    IsStorage[i] = true;
                    Storage[StorageCount++] = (BuildingType)i;
                }
                if (!d.HasJobs || d.IsWonder || d.UniquePerCivilization) continue;
                float food = 0f, extra = 0f, otherIn = 0f;
                var outs = d.OutputsPerWorker;
                if (outs != null)
                    for (int k = 0; k < outs.Length; k++)
                    {
                        float a = Math.Max(0f, MathUtil.Sanitize(outs[k].Amount));
                        if (outs[k].Type == ResourceType.Food) food += a; else extra += a;
                    }
                var ins = d.InputsPerWorker;
                if (ins != null)
                    for (int k = 0; k < ins.Length; k++)
                    {
                        float a = Math.Max(0f, MathUtil.Sanitize(ins[k].Amount));
                        if (ins[k].Type == ResourceType.Food) food -= a; else otherIn += a;
                    }
                if (food <= 1e-4f) continue;
                IsProducer[i] = true;
                FoodPerWorker[i] = food;
                ExtraPerWorker[i] = extra;
                InputFactor[i] = food / (food + 0.5f * otherIn);
                Producers[ProducerCount++] = (BuildingType)i;
                IsFoodJob[(int)d.Job] = true;
            }
        }
    }
}
