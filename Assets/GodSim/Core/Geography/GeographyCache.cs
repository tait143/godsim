using System;
using System.Collections.Generic;

namespace GodSim.Core.Geography
{
    /// <summary>
    /// Shared, lazily built <see cref="GeographyIndex"/> per map instance. Maps are held weakly (an index never
    /// references its map), so a discarded world can be collected. The index is rebuilt when the map's
    /// <see cref="WorldMap.TerrainVersion"/> changes, but at most once every <see cref="MinRebuildIntervalTicks"/>
    /// ticks (meanwhile the slightly stale index is returned; tile ids stay valid because map size never changes),
    /// or on the next <see cref="Get"/> after <see cref="Invalidate(WorldMap)"/>. Race renames are carried over
    /// rebuilds. Intended for presentation (UI, chronicle text); the index is derived data and never saved.
    /// </summary>
    public static class GeographyCache
    {
        /// <summary>Minimum ticks between terrain-triggered rebuilds (one year).</summary>
        public const int MinRebuildIntervalTicks = SimTime.TicksPerYear;
        /// <summary>Maximum number of maps cached at once; the oldest entry is evicted beyond this.</summary>
        public const int MaxEntries = 4;

        private sealed class Entry
        {
            public WeakReference<WorldMap> Map;
            public ulong Seed;
            public GeographyIndex Index;
            public long BuiltTick;
            public bool Dirty;
        }

        private static readonly List<Entry> _entries = new List<Entry>();
        private static readonly object _lock = new object();

        /// <summary>Number of full builds performed (diagnostics/tests).</summary>
        public static int BuildCount { get; private set; }

        /// <summary>The index for the state's map, building or rebuilding it if needed. Null for a null state/map.</summary>
        public static GeographyIndex Get(WorldState s)
        {
            if (s == null || s.Map == null) return null;
            var map = s.Map;
            lock (_lock)
            {
                Entry e = FindAndPrune(map);
                if (e == null)
                {
                    if (_entries.Count >= MaxEntries) _entries.RemoveAt(0);
                    e = new Entry { Map = new WeakReference<WorldMap>(map) };
                    _entries.Add(e);
                    Rebuild(e, map, s, null);
                    return e.Index;
                }
                bool stale = e.Index.TerrainVersion != map.TerrainVersion;
                bool due = s.Tick - e.BuiltTick >= MinRebuildIntervalTicks || s.Tick < e.BuiltTick;
                if (e.Dirty || e.Seed != s.Seed || (stale && due)) Rebuild(e, map, s, e.Seed == s.Seed ? e.Index : null);
                return e.Index;
            }
        }

        /// <summary>Forces a rebuild on the next <see cref="Get"/> for this map (bypasses the rebuild interval).</summary>
        public static void Invalidate(WorldMap map)
        {
            if (map == null) return;
            lock (_lock)
            {
                var e = FindAndPrune(map);
                if (e != null) e.Dirty = true;
            }
        }

        /// <summary>Forces a rebuild on the next <see cref="Get"/> for the state's map.</summary>
        public static void Invalidate(WorldState s)
        {
            if (s != null) Invalidate(s.Map);
        }

        /// <summary>Drops every cached index.</summary>
        public static void Clear()
        {
            lock (_lock) _entries.Clear();
        }

        private static Entry FindAndPrune(WorldMap map)
        {
            Entry found = null;
            for (int i = _entries.Count - 1; i >= 0; i--)
            {
                WorldMap m;
                if (!_entries[i].Map.TryGetTarget(out m)) { _entries.RemoveAt(i); continue; }
                if (ReferenceEquals(m, map)) found = _entries[i];
            }
            return found;
        }

        private static void Rebuild(Entry e, WorldMap map, WorldState s, GeographyIndex previous)
        {
            var idx = GeographyIndex.Build(map, s.Seed);
            if (previous != null) idx.CarryOverRenames(previous);
            e.Index = idx;
            e.Seed = s.Seed;
            e.BuiltTick = s.Tick;
            e.Dirty = false;
            BuildCount++;
        }
    }
}
