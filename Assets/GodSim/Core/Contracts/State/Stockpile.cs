using System;

namespace GodSim.Core
{
    /// <summary>
    /// Settlement resource storage with per-resource capacity and automatic flow accounting:
    /// every Add/Consume is tallied into the current-period counters so the AI can read average
    /// income/expense without each module reporting it. The economy module calls
    /// <see cref="RollStatistics"/> once per tick to fold the counters into moving averages.
    /// </summary>
    public sealed class Stockpile
    {
        public const float DefaultCapacity = 150f;

        private readonly float[] _amounts = new float[Counts.Resources];
        private readonly float[] _producedThisPeriod = new float[Counts.Resources];
        private readonly float[] _consumedThisPeriod = new float[Counts.Resources];

        /// <summary>Exponential moving average of production per tick.</summary>
        public readonly float[] AvgIncome = new float[Counts.Resources];
        /// <summary>Exponential moving average of consumption per tick.</summary>
        public readonly float[] AvgExpense = new float[Counts.Resources];

        /// <summary>Per-resource capacity (base + storage buildings, set by economy).</summary>
        public float Capacity = DefaultCapacity;

        public float Get(ResourceType t) => _amounts[(int)t];
        public float this[ResourceType t] => _amounts[(int)t];
        public float Net(ResourceType t) => AvgIncome[(int)t] - AvgExpense[(int)t];
        public float FreeSpace(ResourceType t) => Math.Max(0f, Capacity - _amounts[(int)t]);

        /// <summary>Adds up to the free capacity; returns the amount actually stored.</summary>
        public float Add(ResourceType t, float amount)
        {
            if (!(amount > 0f) || float.IsInfinity(amount)) return 0f;
            int i = (int)t;
            float stored = Math.Min(amount, Math.Max(0f, Capacity - _amounts[i]));
            _amounts[i] += stored;
            _producedThisPeriod[i] += stored;
            return stored;
        }

        public bool Has(ResourceType t, float amount) => _amounts[(int)t] + 1e-4f >= amount;

        /// <summary>Consumes exactly the amount or nothing.</summary>
        public bool TryConsume(ResourceType t, float amount)
        {
            if (amount <= 0f) return true;
            int i = (int)t;
            if (_amounts[i] + 1e-4f < amount) return false;
            _amounts[i] = Math.Max(0f, _amounts[i] - amount);
            _consumedThisPeriod[i] += amount;
            return true;
        }

        /// <summary>Consumes as much as available up to amount; returns the amount consumed.</summary>
        public float ConsumeUpTo(ResourceType t, float amount)
        {
            if (!(amount > 0f)) return 0f;
            int i = (int)t;
            float taken = Math.Min(amount, _amounts[i]);
            _amounts[i] -= taken;
            _consumedThisPeriod[i] += taken;
            return taken;
        }

        public bool CanAfford(ResourceAmount[] costs, float multiplier = 1f)
        {
            if (costs == null) return true;
            for (int i = 0; i < costs.Length; i++) if (!Has(costs[i].Type, costs[i].Amount * multiplier)) return false;
            return true;
        }

        /// <summary>Atomically pays all costs or nothing.</summary>
        public bool TryPay(ResourceAmount[] costs, float multiplier = 1f)
        {
            if (!CanAfford(costs, multiplier)) return false;
            if (costs != null) for (int i = 0; i < costs.Length; i++) TryConsume(costs[i].Type, costs[i].Amount * multiplier);
            return true;
        }

        /// <summary>Raw set without flow accounting (persistence/tests/divine effects).</summary>
        public void SetRaw(ResourceType t, float amount) => _amounts[(int)t] = Math.Max(0f, MathUtil.Sanitize(amount));

        /// <summary>Clamps every amount to capacity (after capacity shrinks). Returns total lost.</summary>
        public float ClampToCapacity()
        {
            float lost = 0f;
            for (int i = 0; i < _amounts.Length; i++)
                if (_amounts[i] > Capacity) { lost += _amounts[i] - Capacity; _amounts[i] = Capacity; }
            return lost;
        }

        /// <summary>Folds this period's flows into moving averages (alpha = smoothing 0..1) and resets counters.</summary>
        public void RollStatistics(float alpha = 0.1f)
        {
            for (int i = 0; i < _amounts.Length; i++)
            {
                float inc = AvgIncome[i] + (_producedThisPeriod[i] - AvgIncome[i]) * alpha;
                float exp = AvgExpense[i] + (_consumedThisPeriod[i] - AvgExpense[i]) * alpha;
                // Snap tiny values to zero: decaying EMAs otherwise get stuck in the (very slow) subnormal float range.
                AvgIncome[i] = inc < 1e-6f ? 0f : inc;
                AvgExpense[i] = exp < 1e-6f ? 0f : exp;
                _producedThisPeriod[i] = 0f;
                _consumedThisPeriod[i] = 0f;
            }
        }

        /// <summary>Current-period (not yet rolled) produced amount. Exposed for persistence/hashing.</summary>
        public float PeriodProduced(ResourceType t) => _producedThisPeriod[(int)t];

        /// <summary>Current-period (not yet rolled) consumed amount. Exposed for persistence/hashing.</summary>
        public float PeriodConsumed(ResourceType t) => _consumedThisPeriod[(int)t];

        /// <summary>Restores current-period flow counters (persistence only).</summary>
        public void SetPeriodFlows(ResourceType t, float produced, float consumed)
        {
            _producedThisPeriod[(int)t] = Math.Max(0f, MathUtil.Sanitize(produced));
            _consumedThisPeriod[(int)t] = Math.Max(0f, MathUtil.Sanitize(consumed));
        }

        public float Total()
        {
            float s = 0f;
            for (int i = 0; i < _amounts.Length; i++) s += _amounts[i];
            return s;
        }
    }
}
