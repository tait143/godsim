using System.Collections.Generic;
using GodSim.Core;
using UnityEngine;

namespace GodSim.Unity
{
    /// <summary>
    /// Ring buffer of per-civilization population samples, one per in-game year (newest overwrites oldest).
    /// All series share the ring index, so sample i of every series belongs to the same year; a civilization
    /// that did not exist yet (or has collapsed) stores NaN, which the chart renders as a gap.
    /// </summary>
    internal sealed class PopulationHistory
    {
        public const int Capacity = 300;
        public const int MaxSeries = 24;

        public sealed class Series
        {
            public int CivId;
            public Color32 Color;
            public readonly float[] Values = new float[Capacity];
        }

        private readonly int[] _years = new int[Capacity];
        private readonly List<Series> _series = new List<Series>(8);
        private readonly Dictionary<int, int> _popByCiv = new Dictionary<int, int>();
        private int _head;   // next write slot
        private int _count;

        /// <summary>Increments on every change (chart redraw trigger).</summary>
        public int Version { get; private set; }
        public int Count => _count;
        public IReadOnlyList<Series> All => _series;
        public int LastYear { get; private set; } = -1;

        public void Clear()
        {
            _series.Clear();
            _head = 0;
            _count = 0;
            LastYear = -1;
            Version++;
        }

        /// <summary>Ring slot of the i-th sample (0 = oldest).</summary>
        public int Slot(int i) => (_head - _count + i + Capacity * 2) % Capacity;
        public int YearAt(int i) => _years[Slot(i)];

        /// <summary>Records one sample for the state's current year (ignored if that year is already sampled).</summary>
        public void Sample(WorldState state)
        {
            if (state == null) return;
            int year = state.Year;
            if (year == LastYear) return;
            LastYear = year;

            _popByCiv.Clear();
            var settlements = state.Settlements.Items;
            for (int i = 0; i < settlements.Count; i++)
            {
                var s = settlements[i];
                if (s == null || !s.IsAlive) continue;
                _popByCiv.TryGetValue(s.CivilizationId, out int p);
                _popByCiv[s.CivilizationId] = p + s.Population;
            }

            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (c == null || !c.IsAlive || Find(c.Id) != null || _series.Count >= MaxSeries) continue;
                var ns = new Series { CivId = c.Id, Color = HudDraw.FromRgba32(c.Color) };
                for (int k = 0; k < Capacity; k++) ns.Values[k] = float.NaN;
                _series.Add(ns);
            }

            int slot = _head;
            _years[slot] = year;
            for (int i = 0; i < _series.Count; i++)
            {
                var se = _series[i];
                var civ = state.Civilizations.Get(se.CivId);
                if (civ == null || !civ.IsAlive) { se.Values[slot] = float.NaN; continue; }
                se.Color = HudDraw.FromRgba32(civ.Color);
                _popByCiv.TryGetValue(se.CivId, out int pop);
                se.Values[slot] = pop;
            }
            _head = (_head + 1) % Capacity;
            if (_count < Capacity) _count++;
            Version++;
        }

        public Series Find(int civId)
        {
            for (int i = 0; i < _series.Count; i++) if (_series[i].CivId == civId) return _series[i];
            return null;
        }

        /// <summary>Largest finite value in the window (at least 1).</summary>
        public float Max()
        {
            float m = 1f;
            for (int s = 0; s < _series.Count; s++)
            {
                var v = _series[s].Values;
                for (int i = 0; i < _count; i++)
                {
                    float x = v[Slot(i)];
                    if (x > m) m = x; // NaN compares false
                }
            }
            return m;
        }
    }
}
