using System.Collections.Generic;

namespace GodSim.Core.Objectives
{
    /// <summary>Snapshot of one objective's state, for the HUD.</summary>
    public readonly struct ObjectiveStatus
    {
        public readonly ObjectiveDefinition Definition;
        /// <summary>0..1 progress toward the next fulfilment (1 once a one-shot objective is done).</summary>
        public readonly float Progress;
        public readonly bool IsDone;
        /// <summary>How many times it was fulfilled (0 or 1 unless repeatable).</summary>
        public readonly int TimesCompleted;
        /// <summary>True for a secret objective that has not been fulfilled yet (HUD should mask it).</summary>
        public readonly bool IsHidden;

        public ObjectiveStatus(ObjectiveDefinition definition, float progress, int timesCompleted)
        {
            Definition = definition;
            TimesCompleted = timesCompleted < 0 ? 0 : timesCompleted;
            IsDone = TimesCompleted > 0;
            Progress = IsDone && definition != null && !definition.Repeatable ? 1f : MathUtil.Clamp01(MathUtil.Sanitize(progress));
            IsHidden = definition != null && definition.Hidden && !IsDone;
        }

        public string Key => Definition != null ? Definition.Key : "";
        public string Title => Definition != null ? Definition.Title : "";
    }

    /// <summary>Read-only queries over objective progress stored in WorldState.Memory (safe to call from UI every frame).</summary>
    public static class ObjectiveQueries
    {
        /// <summary>Status of every objective in catalog order (hidden ones included, flagged <see cref="ObjectiveStatus.IsHidden"/>).</summary>
        public static IEnumerable<ObjectiveStatus> All(WorldState s)
        {
            var defs = ObjectiveCatalog.All;
            for (int i = 0; i < defs.Count; i++) yield return StatusOf(s, defs[i]);
        }

        /// <summary>Status of every objective the HUD may show (hidden ones only after fulfilment).</summary>
        public static IEnumerable<ObjectiveStatus> Visible(WorldState s)
        {
            var defs = ObjectiveCatalog.All;
            for (int i = 0; i < defs.Count; i++)
            {
                var st = StatusOf(s, defs[i]);
                if (!st.IsHidden) yield return st;
            }
        }

        /// <summary>Status of one objective (default status for an unknown key).</summary>
        public static ObjectiveStatus Status(WorldState s, string key)
        {
            var d = ObjectiveCatalog.Find(key);
            return d == null ? default(ObjectiveStatus) : StatusOf(s, d);
        }

        /// <summary>0..1 progress of an objective (1 once a one-shot objective is done; 0 for unknown keys).</summary>
        public static float Progress(WorldState s, string key)
        {
            var d = ObjectiveCatalog.Find(key);
            return d == null ? 0f : StatusOf(s, d).Progress;
        }

        /// <summary>True once the objective has been fulfilled at least once.</summary>
        public static bool IsDone(WorldState s, string key)
        {
            var d = ObjectiveCatalog.Find(key);
            return d != null && Read(s, d.DoneKey) >= 1f;
        }

        /// <summary>How many times an objective has been fulfilled.</summary>
        public static int TimesCompleted(WorldState s, string key)
        {
            var d = ObjectiveCatalog.Find(key);
            return d == null ? 0 : ToCount(Read(s, d.DoneKey));
        }

        /// <summary>Number of distinct objectives fulfilled at least once.</summary>
        public static int CompletedCount(WorldState s)
        {
            int n = 0;
            var defs = ObjectiveCatalog.All;
            for (int i = 0; i < defs.Count; i++) if (Read(s, defs[i].DoneKey) >= 1f) n++;
            return n;
        }

        /// <summary>Total number of objectives in the catalog.</summary>
        public static int TotalCount => ObjectiveCatalog.All.Count;

        /// <summary>True once any civilization has completed the Ascension Gate (endgame victory; the sandbox keeps running).</summary>
        public static bool IsAscended(WorldState s) => Read(s, ObjectiveKeys.Ascended) >= 1f;

        private static ObjectiveStatus StatusOf(WorldState s, ObjectiveDefinition d) =>
            new ObjectiveStatus(d, Read(s, d.ProgressKey), ToCount(Read(s, d.DoneKey)));

        private static int ToCount(float v) => v <= 0f ? 0 : (v > 1e6f ? 1000000 : (int)v);

        private static float Read(WorldState s, string key)
        {
            if (s == null || key == null) return 0f;
            return s.Memory.TryGetValue(key, out float v) ? MathUtil.Sanitize(v) : 0f;
        }
    }
}
