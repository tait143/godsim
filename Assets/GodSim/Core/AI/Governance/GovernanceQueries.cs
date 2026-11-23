using System;
using System.Text;

namespace GodSim.Core.AI
{
    /// <summary>Read-only accessors over the "gov.*" civ memory keys, for other AI code and the UI.</summary>
    public static class GovernanceQueries
    {
        /// <summary>True once the governance system has given the civ a government.</summary>
        public static bool HasGovernment(Civilization civ) => civ != null && civ.Memory.ContainsKey(GovernanceKeys.Type);

        /// <summary>Current government (Tribal Council when unset).</summary>
        public static GovernmentType Government(Civilization civ) =>
            civ == null ? GovernmentType.TribalCouncil : GovernmentCatalog.FromFloat(civ.Mem(GovernanceKeys.Type));

        public static GovernmentDefinition Definition(Civilization civ) => GovernmentCatalog.Get(Government(civ));

        /// <summary>Regime stability 0..1 (0.7 when unset).</summary>
        public static float Stability(Civilization civ) => civ == null ? 0.7f : MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(GovernanceKeys.Stability, 0.7f), 0.7f));

        public static float Tax(Civilization civ) => civ == null ? 0f : MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(GovernanceKeys.Tax, 0.1f), 0.1f));

        public static float Conscription(Civilization civ) => civ == null ? 0f : MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(GovernanceKeys.Conscription, 0.05f), 0.05f));

        public static FaithPolicy Faith(Civilization civ)
        {
            if (civ == null) return FaithPolicy.Tolerant;
            float v = MathUtil.Sanitize(civ.Mem(GovernanceKeys.FaithPolicy, (float)(int)FaithPolicy.Tolerant), 1f);
            return (FaithPolicy)MathUtil.Clamp(MathUtil.RoundToInt(v), 0, 3);
        }

        /// <summary>Tick the current government took power.</summary>
        public static long Since(Civilization civ) => civ == null ? 0L : (long)MathUtil.Sanitize(civ.Mem(GovernanceKeys.Since));

        public static float YearsInPower(Civilization civ, long nowTick) => Math.Max(0f, SimTime.TicksToYears(nowTick - Since(civ)));

        public static int Revolutions(Civilization civ) => civ == null ? 0 : Math.Max(0, (int)MathUtil.Sanitize(civ.Mem(GovernanceKeys.Revolutions)));

        /// <summary>Human-readable stability band.</summary>
        public static string StabilityLabel(float stability)
        {
            if (stability < GovernanceModel.RevolutionThreshold) return "on the brink of revolution";
            if (stability < GovernanceModel.UnrestThreshold) return "in turmoil";
            if (stability < 0.4f) return "restless";
            if (stability < 0.7f) return "stable";
            return "secure";
        }

        public static string FaithLabel(FaithPolicy p)
        {
            switch (p)
            {
                case FaithPolicy.Secular: return "Secular";
                case FaithPolicy.Tolerant: return "Tolerant";
                case FaithPolicy.StateFaith: return "State Faith";
                default: return "Zealotry";
            }
        }

        /// <summary>
        /// One-line summary for the UI, e.g.
        /// "Merchant Republic (ruled by a Doge since year 12) - stable 64% - tax 22%, conscription 5%, faith: Tolerant".
        /// Allocates a string; call from UI, not per-tick code.
        /// </summary>
        public static string Describe(Civilization civ)
        {
            if (civ == null) return "";
            var def = Definition(civ);
            float st = Stability(civ);
            var sb = new StringBuilder(128);
            sb.Append(def.Name);
            if (HasGovernment(civ))
            {
                sb.Append(" (ruled by ");
                sb.Append(def.RulerTitle.EndsWith("Council") ? "the " : "a ");
                sb.Append(def.RulerTitle).Append(" since year ").Append(SimTime.Year(Since(civ))).Append(')');
            }
            sb.Append(" - ").Append(StabilityLabel(st)).Append(' ').Append(MathUtil.RoundToInt(st * 100f)).Append('%');
            sb.Append(" - tax ").Append(MathUtil.RoundToInt(Tax(civ) * 100f)).Append('%');
            sb.Append(", conscription ").Append(MathUtil.RoundToInt(Conscription(civ) * 100f)).Append('%');
            sb.Append(", faith: ").Append(FaithLabel(Faith(civ)));
            int rev = Revolutions(civ);
            if (rev > 0) sb.Append(" - ").Append(rev).Append(rev == 1 ? " revolution" : " revolutions");
            return sb.ToString();
        }
    }
}
