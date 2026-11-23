using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.Religion
{
    /// <summary>
    /// Read-only helpers over the Religion blackboard (<see cref="ReligionKeys"/>) for UI, tools and other modules.
    /// Every method is null-safe and never throws on corrupt memory values.
    /// </summary>
    public static class ReligionQueries
    {
        /// <summary>True once the civilization has founded its faith.</summary>
        public static bool HasFaith(Civilization civ)
        {
            if (civ == null) return false;
            return civ.Memory.TryGetValue(ReligionKeys.Founded, out float v) && v > 0.5f && !float.IsNaN(v);
        }

        /// <summary>The faith's (generated, never stored) name, or "" when the civ has no faith.</summary>
        public static string FaithName(WorldState state, GameDatabase db, Civilization civ)
        {
            if (!HasFaith(civ)) return "";
            return ReligionNames.FaithName(civ.Id, state != null ? state.Seed : 0UL, RaceOf(db, civ));
        }

        /// <summary>The doctrine held on one axis, or null (no faith / corrupt value).</summary>
        public static DoctrineDefinition Doctrine(Civilization civ, DoctrineAxis axis)
        {
            if (civ == null || !civ.Memory.TryGetValue(DoctrineCatalog.MemoryKey(axis), out float v)) return null;
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f || v > 255f) return null;
            if (!DoctrineCatalog.TryGet((int)v, out var def) || def.Axis != axis) return null;
            return def;
        }

        /// <summary>True when the civ's faith holds the given doctrine.</summary>
        public static bool Holds(Civilization civ, DoctrineId id)
        {
            var def = DoctrineCatalog.Get(id);
            if (def == null) return false;
            var held = Doctrine(civ, def.Axis);
            return held != null && held.Id == id;
        }

        /// <summary>All doctrines held (axis order; empty when no faith). Allocates: UI use only.</summary>
        public static IEnumerable<DoctrineDefinition> Doctrines(Civilization civ)
        {
            var list = new List<DoctrineDefinition>(DoctrineCatalog.AxisCount);
            if (!HasFaith(civ)) return list;
            for (int a = 0; a < DoctrineCatalog.AxisCount; a++)
            {
                var d = Doctrine(civ, (DoctrineAxis)a);
                if (d != null) list.Add(d);
            }
            return list;
        }

        /// <summary>Holy site settlement id, -1 when none.</summary>
        public static int HolySite(Civilization civ)
        {
            if (!HasFaith(civ)) return -1;
            float v = civ.Mem(ReligionKeys.HolySite, -1f);
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 0f || v > int.MaxValue) return -1;
            return (int)v;
        }

        /// <summary>True when the settlement is its civilization's holy site.</summary>
        public static bool IsHolySite(WorldState state, Settlement s)
        {
            if (state == null || s == null || !s.IsAlive) return false;
            var civ = state.Civilizations.Get(s.CivilizationId);
            return civ != null && HolySite(civ) == s.Id;
        }

        /// <summary>Heresy 0..1 (0 when no faith).</summary>
        public static float Heresy(Civilization civ)
        {
            if (!HasFaith(civ)) return 0f;
            return MathUtil.Clamp01(MathUtil.Sanitize(civ.Mem(ReligionKeys.Heresy)));
        }

        /// <summary>Civ id this faith calls a holy war against, -1 when none.</summary>
        public static int HolyWarTarget(Civilization civ)
        {
            if (civ == null || !civ.Memory.TryGetValue(ReligionKeys.HolyWarTarget, out float v)) return -1;
            if (float.IsNaN(v) || float.IsInfinity(v) || v < 1f || v > int.MaxValue) return -1;
            return (int)v;
        }

        /// <summary>Tick the faith was founded, -1 when none.</summary>
        public static long FoundedTick(Civilization civ)
        {
            if (!HasFaith(civ)) return -1;
            float v = MathUtil.Sanitize(civ.Mem(ReligionKeys.FoundedTick, -1f), -1f);
            return v < 0f ? -1 : (long)v;
        }

        /// <summary>Short heresy mood word for UI ("devout", "uneasy", "restless", "schismatic").</summary>
        public static string HeresyMood(float heresy)
        {
            heresy = MathUtil.Clamp01(MathUtil.Sanitize(heresy));
            if (heresy < 0.2f) return "devout";
            if (heresy < 0.45f) return "uneasy";
            if (heresy < ReligionTuning.SchismThreshold) return "restless";
            return "schismatic";
        }

        /// <summary>
        /// Multi-line, human-readable summary for the UI: name, doctrines, holy site, heresy and holy war.
        /// Returns a short "no organized faith" line for civs without a faith.
        /// </summary>
        public static string Describe(WorldState state, GameDatabase db, Civilization civ)
        {
            if (civ == null) return "";
            var race = RaceOf(db, civ);
            if (!HasFaith(civ))
            {
                string people = race != null && !string.IsNullOrEmpty(race.PluralName) ? "The " + race.PluralName + " of the " + civ.Name : "The " + civ.Name;
                return people + " have no organized faith yet.";
            }
            var sb = new StringBuilder(256);
            sb.Append(FaithName(state, db, civ)).Append('\n');
            var view = Doctrine(civ, DoctrineAxis.View);
            if (view != null) sb.Append("They revere the god as ").Append(view.Tenet).Append(" (").Append(view.Name).Append(").\n");
            for (int a = 0; a < DoctrineCatalog.AxisCount; a++)
            {
                var d = Doctrine(civ, (DoctrineAxis)a);
                if (d == null) continue;
                sb.Append(DoctrineCatalog.AxisName((DoctrineAxis)a)).Append(": ").Append(d.Name).Append(" - ").Append(d.Description).Append('\n');
            }
            int holy = HolySite(civ);
            var site = holy >= 0 && state != null ? state.Settlements.Get(holy) : null;
            sb.Append("Holy site: ").Append(site != null && site.IsAlive ? site.Name : "none").Append('\n');
            float heresy = Heresy(civ);
            sb.Append("Heresy: ").Append(MathUtil.RoundToInt(heresy * 100f)).Append("% (").Append(HeresyMood(heresy)).Append(')');
            int target = HolyWarTarget(civ);
            var foe = target >= 0 && state != null ? state.Civilizations.Get(target) : null;
            if (foe != null && foe.IsAlive) sb.Append("\nHoly war called against the ").Append(foe.Name).Append('.');
            return sb.ToString();
        }

        /// <summary>Race definition of a civ from the database, or null (no throw).</summary>
        internal static RaceDefinition RaceOf(GameDatabase db, Civilization civ)
        {
            if (db == null || civ == null) return null;
            int i = (int)civ.Race;
            var races = db.Races;
            return i >= 0 && i < races.Count ? races[i] : null;
        }
    }
}
