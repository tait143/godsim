using System;

namespace GodSim.Core.Progression
{
    /// <summary>Chronicle/UI phrasing helpers shared by the progression systems.</summary>
    internal static class ProgressionText
    {
        /// <summary>"The X" for a civilization, without doubling an existing leading "The ".</summary>
        public static string TheCiv(Civilization civ)
        {
            string name = civ == null || string.IsNullOrEmpty(civ.Name) ? "unknown people" : civ.Name;
            return name.StartsWith("The ", StringComparison.Ordinal) ? name : "The " + name;
        }

        /// <summary>Display name of an age ("Iron Age"), using the definition name when present.</summary>
        public static string AgeName(AgeDefinition def, AgeId id)
        {
            string name = def != null && !string.IsNullOrEmpty(def.Name) ? def.Name : id.ToString();
            if (name.StartsWith("The ", StringComparison.Ordinal)) name = name.Substring(4);
            return name.EndsWith("Age", StringComparison.OrdinalIgnoreCase) ? name : name + " Age";
        }

        /// <summary>Display name of a tech, falling back to its key.</summary>
        public static string TechName(TechDefinition tech)
        {
            if (tech == null) return "a forgotten art";
            return !string.IsNullOrEmpty(tech.Name) ? tech.Name : (tech.Key ?? "an unnamed art");
        }
    }
}
