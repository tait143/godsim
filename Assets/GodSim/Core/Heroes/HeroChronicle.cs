using System.Text;

namespace GodSim.Core.Heroes
{
    /// <summary>Pure text builders for hero chronicle entries (rise, deeds, biography). Deterministic, no state.</summary>
    public static class HeroChronicle
    {
        /// <summary>"Aldric Stonebrow rose to renown as the Architect of Grimhold."</summary>
        public static string RiseText(string name, HeroRole role, string placeName)
        {
            var sb = new StringBuilder(96);
            sb.Append(string.IsNullOrEmpty(name) ? "A promising soul" : name);
            sb.Append(" rose to renown as the ").Append(HeroTitles.RoleName(role));
            if (!string.IsNullOrEmpty(placeName)) sb.Append(" of ").Append(placeName);
            sb.Append('.');
            return sb.ToString();
        }

        /// <summary>
        /// Biography, e.g. "Aldric Stonebrow, the Architect of Grimhold, died aged 81; he raised 23 buildings."
        /// <paramref name="name"/> null = the person is no longer known (e.g. removed before detection); ageYears &lt; 0, cause null
        /// and sex null mean unknown.
        /// </summary>
        public static string Biography(string name, HeroRole role, string placeName, float ageYears, DeathCause? cause, Sex? sex, int deeds, bool legendary)
        {
            var sb = new StringBuilder(160);
            bool known = !string.IsNullOrEmpty(name);
            if (known) sb.Append(name).Append(", the ");
            else sb.Append("The ");
            sb.Append(HeroTitles.RoleName(role));
            if (!string.IsNullOrEmpty(placeName)) sb.Append(" of ").Append(placeName);
            if (known) sb.Append(',');
            sb.Append(' ').Append(DeathVerb(cause));
            float age = MathUtil.Sanitize(ageYears, -1f);
            if (age >= 0f) sb.Append(" aged ").Append(MathUtil.FloorToInt(MathUtil.Clamp(age, 0f, 100000f)));
            if (deeds > 0) sb.Append("; ").Append(DeedsPhrase(role, deeds, sex));
            sb.Append('.');
            if (legendary) sb.Append(" Their legend will be told for ages.");
            return sb.ToString();
        }

        /// <summary>"he raised 23 buildings", "she wrote 1 treatise".</summary>
        public static string DeedsPhrase(HeroRole role, int deeds, Sex? sex)
        {
            string pr = !sex.HasValue ? "they" : (sex.Value == Sex.Male ? "he" : "she");
            bool one = deeds == 1;
            switch (role)
            {
                case HeroRole.General: return pr + " won honour in " + deeds + (one ? " battle" : " battles");
                case HeroRole.Sage: return pr + " wrote " + deeds + (one ? " treatise" : " treatises");
                case HeroRole.Prophet: return pr + " preached for " + deeds + (one ? " year" : " years");
                case HeroRole.Architect: return pr + " raised " + deeds + (one ? " building" : " buildings");
                case HeroRole.Explorer: return pr + " made " + deeds + (one ? " discovery" : " discoveries");
                case HeroRole.MerchantPrince: return pr + " led " + deeds + (one ? " great venture" : " great ventures");
                case HeroRole.Healer: return pr + " tended the sick for " + deeds + (one ? " year" : " years");
                case HeroRole.Bard: return pr + " composed " + deeds + (one ? " song" : " songs");
                default: return pr + " performed " + deeds + (one ? " deed" : " deeds");
            }
        }

        /// <summary>Death verb by cause ("died", "fell in battle"). Unknown cause = "passed from the world".</summary>
        public static string DeathVerb(DeathCause? cause)
        {
            if (!cause.HasValue) return "passed from the world";
            switch (cause.Value)
            {
                case DeathCause.Violence: return "fell in battle";
                case DeathCause.Disease: return "succumbed to sickness";
                case DeathCause.Starvation: return "died of hunger";
                case DeathCause.Disaster: return "perished in calamity";
                case DeathCause.DivineWrath: return "was struck down by the god";
                case DeathCause.Accident: return "died by misfortune";
                case DeathCause.Exposure: return "died of exposure";
                default: return "died";
            }
        }

        /// <summary>"Kara the Explorer found ancient ruins beyond Oakford."</summary>
        public static string DiscoveryText(string name, TileFlags found, string placeName)
        {
            string what;
            switch (found)
            {
                case TileFlags.Ruins: what = "ancient ruins"; break;
                case TileFlags.OreDeposit: what = "a vein of ore"; break;
                case TileFlags.GoldDeposit: what = "a seam of gold"; break;
                case TileFlags.StoneDeposit: what = "a fine stone outcrop"; break;
                default: what = "something remarkable"; break;
            }
            var sb = new StringBuilder(96);
            sb.Append(string.IsNullOrEmpty(name) ? "An explorer" : name + " the Explorer");
            sb.Append(" found ").Append(what);
            if (!string.IsNullOrEmpty(placeName)) sb.Append(" beyond ").Append(placeName);
            sb.Append('.');
            return sb.ToString();
        }
    }
}
