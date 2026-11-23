using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>Read-only UI/debug queries over the learned council (not for per-tick use: may allocate).</summary>
    public static class AILearningQueries
    {
        /// <summary>The n known advisors the civ heeds most (highest learned weight first; ties by table order).</summary>
        public static List<string> TopAdvisors(Civilization civ, int n)
        {
            var result = new List<string>();
            TopAdvisors(civ, n, result);
            return result;
        }

        /// <summary>Fills <paramref name="results"/> (cleared) with the top n advisor ids; returns the count. Only advisors with a learned weight are ranked.</summary>
        public static int TopAdvisors(Civilization civ, int n, List<string> results)
        {
            if (results == null) return 0;
            results.Clear();
            if (civ == null || n <= 0) return 0;
            var domains = AdvisorDomains.All;
            while (results.Count < n)
            {
                AdvisorDomain best = null;
                float bestW = float.MinValue;
                for (int i = 0; i < domains.Count; i++)
                {
                    var d = domains[i];
                    if (!civ.Memory.ContainsKey(d.WeightKey) || results.Contains(d.Id)) continue;
                    float w = AdvisorBoard.Weight(civ, d.Id);
                    if (w > bestW) { bestW = w; best = d; }
                }
                if (best == null) break;
                results.Add(best.Id);
            }
            return results.Count;
        }

        /// <summary>Council title of an advisor ("Treasurer"), or the capitalised id for unknown advisors.</summary>
        public static string Title(string advisorId)
        {
            var d = AdvisorDomains.Find(advisorId);
            if (d != null) return d.Title;
            if (string.IsNullOrEmpty(advisorId)) return "Advisor";
            return char.ToUpperInvariant(advisorId[0]) + advisorId.Substring(1);
        }

        /// <summary>One line for the UI, e.g. "The Council heeds the Treasurer and the Granary-keeper".</summary>
        public static string CouncilSummary(Civilization civ, int n = 2)
        {
            var top = TopAdvisors(civ, n);
            if (top.Count == 0) return "The Council heeds all advisors equally";
            var sb = new StringBuilder("The Council heeds ");
            for (int i = 0; i < top.Count; i++)
            {
                if (i > 0) sb.Append(i == top.Count - 1 ? " and " : ", ");
                sb.Append("the ").Append(Title(top[i]));
            }
            return sb.ToString();
        }

        /// <summary>Last weight change applied to an advisor (0 if never judged).</summary>
        public static float LastChange(Civilization civ, string advisorId)
        {
            var d = AdvisorDomains.Find(advisorId);
            return civ == null || d == null ? 0f : MathUtil.Sanitize(civ.Mem(d.DeltaKey));
        }

        /// <summary>Why the advisor's weight last changed.</summary>
        public static LearningReason LastReason(Civilization civ, string advisorId)
        {
            var d = AdvisorDomains.Find(advisorId);
            if (civ == null || d == null) return LearningReason.None;
            int r = (int)MathUtil.Sanitize(civ.Mem(d.ReasonKey));
            return r >= 0 && r <= (int)LearningReason.Drift ? (LearningReason)r : LearningReason.None;
        }

        /// <summary>Stored scorecard metric (0.5 when never evaluated).</summary>
        public static float Score(Civilization civ, AIMetric metric) => AIScorecard.Get(civ, metric);
    }
}
