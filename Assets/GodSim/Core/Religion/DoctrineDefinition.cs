using System;

namespace GodSim.Core.Religion
{
    /// <summary>
    /// Static data for one doctrine (registered once in <see cref="DoctrineCatalog"/>, read-only at runtime).
    /// Effect values are amounts per <see cref="ReligionSystem"/> run (48 ticks) and are deliberately tiny:
    /// the system sums them per settlement / civilization and then clamps every total to
    /// <see cref="ReligionTuning.MaxNudgePerRun"/> so the faith complements, never fights, the Divine
    /// module's FaithSystem and the Population module's wellbeing drift.
    /// </summary>
    public sealed class DoctrineDefinition
    {
        private readonly float[] _weights = new float[DoctrineSelector.TraitCount];

        internal DoctrineDefinition(DoctrineId id, DoctrineAxis axis, string key, string name)
        {
            Id = id;
            Axis = axis;
            Key = key ?? "";
            Name = name ?? "";
        }

        /// <summary>Stable id (persisted as a float in civ memory).</summary>
        public DoctrineId Id { get; }
        /// <summary>Axis this doctrine belongs to.</summary>
        public DoctrineAxis Axis { get; }
        /// <summary>Stable lowercase key, e.g. "sun_worship".</summary>
        public string Key { get; }
        /// <summary>Display name, e.g. "Sun Worship".</summary>
        public string Name { get; }
        /// <summary>One-sentence UI description.</summary>
        public string Description { get; internal set; } = "";
        /// <summary>Short evocative phrase used in chronicle text, e.g. "the unblinking eye of the sun".</summary>
        public string Tenet { get; internal set; } = "";

        // ------------------------------------------------------------------ settlement nudges (per run)
        /// <summary>Added to settlement (and resident) Faith each run.</summary>
        public float FaithNudge { get; internal set; }
        /// <summary>Added to settlement (and resident) Happiness each run.</summary>
        public float HappinessNudge { get; internal set; }
        /// <summary>Added to settlement Unrest each run (negative calms).</summary>
        public float UnrestNudge { get; internal set; }

        // ------------------------------------------------------------------ devotion response (per run)
        /// <summary>Devotion change per run per unit of positive GodFavor (response to blessings).</summary>
        public float BlessingDevotion { get; internal set; }
        /// <summary>Devotion change per run per unit of negative GodFavor (response to wrath; may be positive = fear).</summary>
        public float WrathDevotion { get; internal set; }
        /// <summary>Unconditional devotion change per run.</summary>
        public float FlatDevotion { get; internal set; }

        // ------------------------------------------------------------------ other effects
        /// <summary>Multiplier on heresy growth (1 = neutral, &lt;1 resists).</summary>
        public float HeresyMultiplier { get; internal set; } = 1f;
        /// <summary>Research points per tick added to the civ pool per point of settlement Faith service output.</summary>
        public float ResearchPerFaith { get; internal set; }
        /// <summary>Flat research points per tick per living settlement.</summary>
        public float ResearchPerSettlement { get; internal set; }
        /// <summary>Fractional defense bonus (0.1 = +10%) published as <c>rel.defenseBonus</c>.</summary>
        public float DefenseBonus { get; internal set; }
        /// <summary>Extra Faith per run at the holy site.</summary>
        public float HolySiteFaith { get; internal set; }
        /// <summary>Extra Happiness per run in non-holy settlements from pilgrimages (requires a holy site).</summary>
        public float PilgrimageHappiness { get; internal set; }
        /// <summary>0..1 resistance to foreign conversion pressure.</summary>
        public float ConversionResistance { get; internal set; }
        /// <summary>Multiplier on the conversion pressure this faith exerts on neighbours.</summary>
        public float MissionaryZeal { get; internal set; } = 1f;

        // ------------------------------------------------------------------ selection
        /// <summary>Constant term of the selection score.</summary>
        public float BaseWeight { get; internal set; }

        /// <summary>Selection weight of one trait (score = Base + sum(weight * trait) + tiny jitter).</summary>
        public float Weight(DoctrineTrait trait)
        {
            int i = (int)trait;
            return i >= 0 && i < _weights.Length ? _weights[i] : 0f;
        }

        internal DoctrineDefinition W(DoctrineTrait trait, float weight)
        {
            int i = (int)trait;
            if (i >= 0 && i < _weights.Length) _weights[i] = weight;
            return this;
        }

        /// <summary>Selection score for a feature vector (length <see cref="DoctrineSelector.TraitCount"/>).</summary>
        public float Score(float[] features)
        {
            float s = BaseWeight;
            if (features == null) return s;
            int n = Math.Min(features.Length, _weights.Length);
            for (int i = 0; i < n; i++) s += _weights[i] * MathUtil.Sanitize(features[i]);
            return s;
        }

        public override string ToString() => Name;
    }
}
