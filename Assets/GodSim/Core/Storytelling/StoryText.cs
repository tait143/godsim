using System.Text;

namespace GodSim.Core.Storytelling
{
    /// <summary>Fills chronicle templates. Unknown placeholders are left untouched (and rejected by catalog validation).</summary>
    public static class StoryText
    {
        /// <summary>Every placeholder a template may use.</summary>
        public static readonly string[] Placeholders =
        {
            "{settlement}", "{civ}", "{race}", "{leader}", "{other}", "{othersettlement}", "{person}", "{amount}", "{count}"
        };

        /// <summary>Replaces placeholders with names from the event context (safe fallbacks for missing targets).</summary>
        public static string Format(string template, WorldEventContext e)
        {
            if (string.IsNullOrEmpty(template)) return "";
            if (e == null || e.Sim == null || e.Sim.State == null) return template;
            var st = e.State;
            var civ = e.Civ;
            var sb = new StringBuilder(template, template.Length + 96);
            string settlement = e.Settlement != null ? e.Settlement.Name : StoryUtil.Capital(st, civ)?.Name;
            sb.Replace("{settlement}", Safe(settlement, "the heartland"));
            sb.Replace("{civ}", Safe(civ?.Name, "a forgotten people"));
            sb.Replace("{race}", RaceName(e.Db, civ != null ? civ.Race : RaceId.Human));
            sb.Replace("{leader}", LeaderName(st, civ));
            sb.Replace("{othersettlement}", Safe(e.OtherSettlement?.Name, "a distant town"));
            sb.Replace("{other}", Safe(e.OtherCiv?.Name, "a distant people"));
            sb.Replace("{person}", Safe(e.Person?.Name, "a wanderer"));
            sb.Replace("{amount}", MathUtil.RoundToInt(MathUtil.Clamp(MathUtil.Sanitize(e.Amount), 0f, 1e9f)).ToString());
            sb.Replace("{count}", (e.Count < 0 ? 0 : e.Count).ToString());
            return sb.ToString();
        }

        /// <summary>True if every {token} in the template is a known placeholder and braces are balanced.</summary>
        public static bool UsesOnlyKnownPlaceholders(string template)
        {
            if (template == null) return false;
            int i = 0;
            while (i < template.Length)
            {
                int open = template.IndexOf('{', i);
                int close = template.IndexOf('}', i);
                if (open < 0) return close < 0;
                if (close < open) return false;
                close = template.IndexOf('}', open);
                if (close < 0) return false;
                string token = template.Substring(open, close - open + 1);
                bool known = false;
                for (int k = 0; k < Placeholders.Length; k++) if (Placeholders[k] == token) { known = true; break; }
                if (!known) return false;
                i = close + 1;
            }
            return true;
        }

        internal static string RaceName(GameDatabase db, RaceId race)
        {
            var def = StoryUtil.RaceDef(db, race);
            return def != null && !string.IsNullOrEmpty(def.PluralName) ? def.PluralName : race + "s";
        }

        internal static string LeaderName(WorldState st, Civilization civ)
        {
            var p = civ != null ? StoryUtil.AlivePerson(st, civ.LeaderId) : null;
            return p != null && !string.IsNullOrEmpty(p.Name) ? p.Name : "the elders";
        }

        private static string Safe(string s, string fallback) => string.IsNullOrEmpty(s) ? fallback : s;
    }
}
