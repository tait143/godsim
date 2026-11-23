using System;

namespace GodSim.Core.Religion
{
    /// <summary>
    /// Pure, deterministic doctrine selection: score = BaseWeight + sum(weight * trait) + a tiny jitter hashed
    /// from (world seed, civ id, doctrine) so that two civs of the same race may still differ slightly.
    /// The jitter (+-<see cref="ReligionTuning.SelectionJitter"/>) never overrides a clear personality lean.
    /// </summary>
    public static class DoctrineSelector
    {
        /// <summary>Length of a feature vector (number of <see cref="DoctrineTrait"/> values).</summary>
        public const int TraitCount = 14;

        /// <summary>
        /// Fills <paramref name="into"/> (length &gt;= <see cref="TraitCount"/>) with 0..1 traits. Race and god may be
        /// null (neutral 0.5 personality, neutral god). Returns the array for chaining.
        /// </summary>
        public static float[] Features(RaceDefinition race, GodState god, bool sacredLand, float[] into = null)
        {
            if (into == null || into.Length < TraitCount) into = new float[TraitCount];
            float piety = Unit(race != null ? race.Piety : 0.5f);
            float aggression = Unit(race != null ? race.Aggression : 0.5f);
            float curiosity = Unit(race != null ? race.Curiosity : 0.5f);
            float mercantilism = Unit(race != null ? race.Mercantilism : 0.5f);
            float alignment = god != null ? MathUtil.Clamp(MathUtil.Sanitize(god.Alignment), -1f, 1f) : 0f;
            int casts = god != null ? Math.Max(0, god.PowersCast) : 0;
            float activity = MathUtil.Clamp01(casts / ReligionTuning.ActivitySaturationCasts);
            float neutrality = 1f - Math.Abs(alignment);

            into[(int)DoctrineTrait.Piety] = piety;
            into[(int)DoctrineTrait.Aggression] = aggression;
            into[(int)DoctrineTrait.Curiosity] = curiosity;
            into[(int)DoctrineTrait.Mercantilism] = mercantilism;
            into[(int)DoctrineTrait.Gentleness] = 1f - aggression;
            into[(int)DoctrineTrait.Tradition] = 1f - curiosity;
            into[(int)DoctrineTrait.Frugality] = 1f - mercantilism;
            into[(int)DoctrineTrait.Skepticism] = 1f - piety;
            into[(int)DoctrineTrait.Benevolence] = Math.Max(0f, alignment);
            into[(int)DoctrineTrait.Wrath] = Math.Max(0f, -alignment);
            into[(int)DoctrineTrait.Activity] = activity;
            into[(int)DoctrineTrait.Aloofness] = (1f - activity) * neutrality;
            into[(int)DoctrineTrait.Ambivalence] = activity * neutrality;
            into[(int)DoctrineTrait.SacredLand] = sacredLand ? 1f : 0f;
            return into;
        }

        /// <summary>Deterministic jitter in [-SelectionJitter, +SelectionJitter] for one civ and doctrine.</summary>
        public static float Jitter(ulong seed, int civId, DoctrineId id)
        {
            ulong h = Hashing.Combine(Hashing.Combine(seed, unchecked((ulong)(uint)civId)), 0x5E1D0C7Bu + (ulong)id);
            float u = (h >> 40) * (1f / 16777216f); // [0,1)
            return (u * 2f - 1f) * ReligionTuning.SelectionJitter;
        }

        /// <summary>Full selection score of one doctrine for one civ.</summary>
        public static float Score(DoctrineDefinition def, float[] features, ulong seed, int civId)
        {
            if (def == null) return float.MinValue;
            return MathUtil.Sanitize(def.Score(features)) + Jitter(seed, civId, def.Id);
        }

        /// <summary>
        /// Picks the best-scoring doctrine of an axis. <paramref name="exclude"/> (-1 = none) is skipped, which is
        /// how a Reformation forces a change. Ties resolve to the lowest id. Pure and deterministic.
        /// </summary>
        public static DoctrineId Select(DoctrineAxis axis, float[] features, ulong seed, int civId, int exclude = -1)
        {
            var options = DoctrineCatalog.OfAxis(axis);
            DoctrineDefinition best = null;
            float bestScore = float.MinValue;
            for (int i = 0; i < options.Count; i++)
            {
                var d = options[i];
                if ((int)d.Id == exclude) continue;
                float s = Score(d, features, seed, civId);
                if (best == null || s > bestScore) { best = d; bestScore = s; }
            }
            if (best == null) best = options.Count > 0 ? options[0] : DoctrineCatalog.All[0];
            return best.Id;
        }

        private static float Unit(float v) => MathUtil.Clamp01(MathUtil.Sanitize(v, 0.5f));
    }
}
