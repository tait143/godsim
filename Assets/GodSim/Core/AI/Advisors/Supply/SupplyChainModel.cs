using System;
using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Static production-chain graph built once from the <see cref="GameDatabase"/> (data-driven, no hard-coded chains):
    /// for every resource, which staffed buildings produce or consume it and at what rate per worker per tick.
    /// Multi-step chains (Ore -> Metal -> Tools/Weapons, Wood+Cloth -> Goods, Metal+Fuel -> Machinery,
    /// Metal+Gold -> Electronics, ...) emerge from following a producer's inputs to their own producers.
    /// Immutable after <see cref="Build"/>; allocation happens only there.
    /// </summary>
    public sealed class SupplyChainModel
    {
        private static readonly int R = Counts.Resources;
        private static readonly int B = Counts.Buildings;
        private static readonly int[] s_empty = new int[0];

        private readonly float[] _produce = new float[Counts.Buildings * Counts.Resources];
        private readonly float[] _consume = new float[Counts.Buildings * Counts.Resources];
        private readonly int[][] _producers = new int[Counts.Resources][];
        private readonly int[][] _consumers = new int[Counts.Resources][];

        /// <summary>Definitions indexed by (int)BuildingType (null when the slot is not registered).</summary>
        public readonly BuildingDefinition[] Definitions = new BuildingDefinition[Counts.Buildings];

        /// <summary>Database the model was built from (null until built).</summary>
        public GameDatabase Source { get; private set; }

        /// <summary>True once <see cref="Build"/> ran.</summary>
        public bool IsBuilt => Source != null;

        /// <summary>Creates an empty model.</summary>
        public SupplyChainModel()
        {
            for (int r = 0; r < R; r++) { _producers[r] = s_empty; _consumers[r] = s_empty; }
        }

        /// <summary>Rebuilds the chain graph from building definitions (only staffed job buildings take part).</summary>
        public void Build(GameDatabase db)
        {
            Array.Clear(_produce, 0, _produce.Length);
            Array.Clear(_consume, 0, _consume.Length);
            Array.Clear(Definitions, 0, Definitions.Length);
            Source = db;
            var prod = new List<int>[R];
            var cons = new List<int>[R];
            for (int r = 0; r < R; r++) { prod[r] = new List<int>(); cons[r] = new List<int>(); }
            if (db != null)
            {
                for (int b = 0; b < B; b++)
                {
                    if (!db.TryGetBuilding((BuildingType)b, out var def) || def == null) continue;
                    Definitions[b] = def;
                    if (!def.HasJobs) continue;
                    Accumulate(def.OutputsPerWorker, _produce, b, prod);
                    Accumulate(def.InputsPerWorker, _consume, b, cons);
                }
            }
            for (int r = 0; r < R; r++) { _producers[r] = prod[r].ToArray(); _consumers[r] = cons[r].ToArray(); }
        }

        private static void Accumulate(ResourceAmount[] flows, float[] table, int b, List<int>[] lists)
        {
            if (flows == null) return;
            for (int i = 0; i < flows.Length; i++)
            {
                int r = (int)flows[i].Type;
                float a = MathUtil.Sanitize(flows[i].Amount);
                if (r < 0 || r >= R || a <= 0f) continue;
                int k = b * R + r;
                if (table[k] <= 0f) lists[r].Add(b);
                table[k] += a;
            }
        }

        /// <summary>Output of resource r per staffed worker per tick for building index b (0 if none).</summary>
        public float ProduceRate(int b, int r) => b < 0 || b >= B || r < 0 || r >= R ? 0f : _produce[b * R + r];

        /// <summary>Input of resource r per staffed worker per tick for building index b (0 if none).</summary>
        public float ConsumeRate(int b, int r) => b < 0 || b >= B || r < 0 || r >= R ? 0f : _consume[b * R + r];

        /// <summary>Building indices producing resource r (ascending enum order; deterministic).</summary>
        public int[] ProducersOf(int r) => r < 0 || r >= R ? s_empty : _producers[r];

        /// <summary>Building indices consuming resource r (ascending enum order; deterministic).</summary>
        public int[] ConsumersOf(int r) => r < 0 || r >= R ? s_empty : _consumers[r];

        /// <summary>Number of production steps from raw extraction to resource r (0 = raw; capped at 6).</summary>
        public int ChainDepth(int r) => Depth(r, 0);

        private int Depth(int r, int guard)
        {
            if (guard > 6) return 6;
            var p = ProducersOf(r);
            int best = int.MaxValue;
            for (int k = 0; k < p.Length; k++)
            {
                int d = 0;
                var def = Definitions[p[k]];
                if (def != null && def.InputsPerWorker != null)
                    for (int i = 0; i < def.InputsPerWorker.Length; i++)
                    {
                        int ir = (int)def.InputsPerWorker[i].Type;
                        if (ir == r) continue;
                        d = Math.Max(d, 1 + Depth(ir, guard + 1));
                    }
                if (d < best) best = d;
            }
            return best == int.MaxValue ? 0 : Math.Min(best, 6);
        }
    }
}
