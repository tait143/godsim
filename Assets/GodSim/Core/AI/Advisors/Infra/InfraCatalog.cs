using System.Collections.Generic;

namespace GodSim.Core.AI
{
    /// <summary>
    /// Data-driven view of the building catalog for the infrastructure advisor, built once per <see cref="GameDatabase"/>.
    /// Everything comes from <see cref="BuildingDefinition"/> fields: which buildings provide which service
    /// (def.Services), which produce which resource (def.OutputsPerWorker), which burn inputs (def.InputsPerWorker),
    /// which are Industry, and which Commerce providers are late-age transport hubs. Lists are in enum order, so every
    /// iteration over them is deterministic.
    /// </summary>
    public sealed class InfraCatalog
    {
        /// <summary>The database this catalog was built from (rebuilt when the context's database changes).</summary>
        public readonly GameDatabase Db;
        /// <summary>Non-wonder buildings providing a service, indexed by (int)ServiceType.</summary>
        public readonly List<BuildingType>[] ProvidersOf;
        /// <summary>Non-wonder buildings producing a resource, indexed by (int)ResourceType.</summary>
        public readonly List<BuildingType>[] ProducersOf;
        /// <summary>Commerce providers available from the hub age on (RailStation, Airport...).</summary>
        public readonly List<BuildingType> Hubs = new List<BuildingType>();
        /// <summary>Category == Industry (these get the +25% production bonus at full power).</summary>
        public readonly bool[] IsIndustry;
        /// <summary>Has any positive InputsPerWorker (e.g. a PowerPlant burning Fuel).</summary>
        public readonly bool[] NeedsInputs;
        /// <summary>Sum of OutputsPerWorker amounts (units per worker per tick).</summary>
        public readonly float[] OutputPerWorker;

        private readonly BuildingDefinition[] _defs;
        private readonly float[] _service;      // [type * Services + svc]
        private readonly float[] _serviceTotal; // per type
        private readonly float[] _output;       // [type * Resources + res]

        public InfraCatalog(GameDatabase db, AgeId hubMinAge)
        {
            Db = db;
            int nb = Counts.Buildings, ns = Counts.Services, nr = Counts.Resources;
            _defs = new BuildingDefinition[nb];
            _service = new float[nb * ns];
            _serviceTotal = new float[nb];
            _output = new float[nb * nr];
            IsIndustry = new bool[nb];
            NeedsInputs = new bool[nb];
            OutputPerWorker = new float[nb];
            ProvidersOf = new List<BuildingType>[ns];
            for (int i = 0; i < ns; i++) ProvidersOf[i] = new List<BuildingType>();
            ProducersOf = new List<BuildingType>[nr];
            for (int i = 0; i < nr; i++) ProducersOf[i] = new List<BuildingType>();
            if (db == null) return;

            for (int t = 0; t < nb; t++)
            {
                var type = (BuildingType)t;
                if (!db.TryGetBuilding(type, out var def) || def == null) continue;
                _defs[t] = def;
                IsIndustry[t] = def.Category == BuildingCategory.Industry;

                var svcs = def.Services;
                if (svcs != null)
                    for (int k = 0; k < svcs.Length; k++)
                    {
                        int si = (int)svcs[k].Type;
                        if (si < 0 || si >= ns) continue;
                        float amt = Positive(svcs[k].Amount);
                        _service[t * ns + si] += amt;
                        _serviceTotal[t] += amt;
                    }
                var outs = def.OutputsPerWorker;
                if (outs != null)
                    for (int k = 0; k < outs.Length; k++)
                    {
                        int ri = (int)outs[k].Type;
                        if (ri < 0 || ri >= nr) continue;
                        float amt = Positive(outs[k].Amount);
                        _output[t * nr + ri] += amt;
                        OutputPerWorker[t] += amt;
                    }
                var ins = def.InputsPerWorker;
                if (ins != null)
                    for (int k = 0; k < ins.Length; k++)
                        if (Positive(ins[k].Amount) > 0f) NeedsInputs[t] = true;

                // Wonders, dwellings and the town center are other advisors' business (and are never "more of the same").
                bool candidate = !def.IsWonder && !def.UniquePerCivilization && !def.IsDwelling && type != BuildingType.TownCenter;
                if (!candidate) continue;
                for (int si = 0; si < ns; si++) if (_service[t * ns + si] > 0f) ProvidersOf[si].Add(type);
                for (int ri = 0; ri < nr; ri++) if (_output[t * nr + ri] > 0f) ProducersOf[ri].Add(type);
                if (_service[t * ns + (int)ServiceType.Commerce] > 0f && def.MinAge >= hubMinAge) Hubs.Add(type);
            }
        }

        /// <summary>Definition of a building type, or null.</summary>
        public BuildingDefinition Def(int type) => type >= 0 && type < _defs.Length ? _defs[type] : null;

        /// <summary>Service points per tick a (fully staffed) building of this type provides.</summary>
        public float Service(int type, ServiceType svc)
        {
            int si = (int)svc;
            if (type < 0 || type >= _defs.Length || si < 0 || si >= Counts.Services) return 0f;
            return _service[type * Counts.Services + si];
        }

        /// <summary>Fraction (0..1) of a building's total service points that belong to one service.</summary>
        public float ServiceShare(int type, ServiceType svc)
        {
            if (type < 0 || type >= _defs.Length) return 0f;
            float total = _serviceTotal[type];
            return total > 0f ? Service(type, svc) / total : 0f;
        }

        /// <summary>Units per worker per tick of one resource produced by a building type.</summary>
        public float OutputOf(int type, ResourceType res)
        {
            int ri = (int)res;
            if (type < 0 || type >= _defs.Length || ri < 0 || ri >= Counts.Resources) return 0f;
            return _output[type * Counts.Resources + ri];
        }

        private static float Positive(float v)
        {
            v = MathUtil.Sanitize(v);
            return v > 0f ? v : 0f;
        }
    }
}
