using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Text.Encodings.Web;
using System.Text.Json;
using GodSim.Core;

namespace GodSim.ContentReport
{
    /// <summary>
    /// Dumps the whole database as JSON for the web viewer and external tools. Definitions are written
    /// generically from their public fields (so new contract fields appear automatically): enums as names,
    /// flag enums as name arrays, colors as "#RRGGBBAA", and enum-indexed float arrays as objects keyed by enum name.
    /// Field order follows declaration order, so output is deterministic.
    /// </summary>
    internal static class JsonExport
    {
        public const int SchemaVersion = 1;

        /// <summary>Definitions only (used for the content fingerprint).</summary>
        public static byte[] Definitions(ContentModel m) => Write(m, false);

        /// <summary>Definitions plus derived analysis (availability, findings).</summary>
        public static byte[] Full(ContentModel m) => Write(m, true);

        private static byte[] Write(ContentModel m, bool analysis)
        {
            using (var ms = new MemoryStream())
            {
                var opts = new JsonWriterOptions { Indented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
                using (var w = new Utf8JsonWriter(ms, opts))
                {
                    w.WriteStartObject();
                    w.WriteString("generator", "GodSim.ContentReport");
                    w.WriteNumber("schemaVersion", SchemaVersion);
                    w.WriteNumber("ticksPerYear", SimTime.TicksPerYear);
                    w.WriteBoolean("frozen", m.Load.Frozen);
                    WriteList(w, "races", m.Races);
                    WriteList(w, "ages", m.Ages);
                    WriteList(w, "techs", m.Techs);
                    WriteList(w, "buildings", m.Buildings);
                    WriteList(w, "godPowers", m.Powers);
                    if (analysis) WriteAnalysis(w, m);
                    w.WriteEndObject();
                }
                return ms.ToArray();
            }
        }

        private static void WriteList(Utf8JsonWriter w, string name, IList list)
        {
            w.WriteStartArray(name);
            foreach (var o in list) WriteValue(w, o, null);
            w.WriteEndArray();
        }

        private static void WriteAnalysis(Utf8JsonWriter w, ContentModel m)
        {
            w.WriteStartObject("analysis");

            w.WriteStartObject("buildingFirstAge");
            foreach (var b in m.Buildings)
            {
                w.WriteStartObject(b.Type.ToString());
                for (int r = 0; r < m.NumRaces; r++)
                {
                    int a = m.BuildingFirstAge[r, (int)b.Type];
                    if (a < 0) w.WriteNull(((RaceId)r).ToString()); else w.WriteString(((RaceId)r).ToString(), ((AgeId)a).ToString());
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();

            w.WriteStartObject("techFirstAge");
            foreach (var t in m.Techs)
            {
                w.WriteStartObject(t.Key);
                for (int r = 0; r < m.NumRaces; r++)
                {
                    int a = m.TechFirstAge[r, t.Id];
                    if (a < 0) w.WriteNull(((RaceId)r).ToString()); else w.WriteString(((RaceId)r).ToString(), ((AgeId)a).ToString());
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();

            w.WriteStartObject("resourceFirstAge");
            for (int res = 0; res < Counts.Resources; res++)
            {
                w.WriteStartObject(((ResourceType)res).ToString());
                for (int r = 0; r < m.NumRaces; r++)
                {
                    int a = m.ResourceFirstAge[r, res];
                    if (a < 0) w.WriteNull(((RaceId)r).ToString()); else w.WriteString(((RaceId)r).ToString(), ((AgeId)a).ToString());
                }
                w.WriteEndObject();
            }
            w.WriteEndObject();

            w.WriteStartArray("findings");
            foreach (var f in m.Findings)
            {
                w.WriteStartObject();
                w.WriteString("severity", f.Severity.ToString().ToLowerInvariant());
                w.WriteString("area", f.Area);
                w.WriteString("message", f.Message);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteNumber("errors", m.ErrorCount);
            w.WriteNumber("warnings", m.WarningCount);
            w.WriteEndObject();
        }

        private static void WriteValue(Utf8JsonWriter w, object v, FieldInfo field)
        {
            if (v == null) { w.WriteNullValue(); return; }
            var t = v.GetType();
            if (v is string s) { w.WriteStringValue(s); return; }
            if (v is bool bo) { w.WriteBooleanValue(bo); return; }
            if (t.IsEnum)
            {
                if (t.IsDefined(typeof(FlagsAttribute), false))
                {
                    w.WriteStartArray();
                    ulong bits = Convert.ToUInt64(v);
                    foreach (var name in Enum.GetNames(t))
                    {
                        ulong fv = Convert.ToUInt64(Enum.Parse(t, name));
                        if (fv != 0 && (bits & fv) == fv && IsSingleBit(fv)) w.WriteStringValue(name);
                    }
                    w.WriteEndArray();
                }
                else w.WriteStringValue(v.ToString());
                return;
            }
            if (v is uint u && field != null && field.Name.EndsWith("Color", StringComparison.Ordinal)) { w.WriteStringValue("#" + u.ToString("X8")); return; }
            if (v is float f) { WriteNumber(w, f); return; }
            if (v is double d) { WriteNumber(w, d); return; }
            if (v is int i) { w.WriteNumberValue(i); return; }
            if (v is uint ui) { w.WriteNumberValue(ui); return; }
            if (v is long l) { w.WriteNumberValue(l); return; }
            if (v is byte by) { w.WriteNumberValue(by); return; }
            if (v is float[] fa && field != null)
            {
                var keyEnum = IndexEnum(field.Name, fa.Length);
                if (keyEnum != null)
                {
                    w.WriteStartObject();
                    var names = Enum.GetNames(keyEnum);
                    for (int k = 0; k < fa.Length; k++) { w.WritePropertyName(k < names.Length ? names[k] : k.ToString()); WriteNumber(w, fa[k]); }
                    w.WriteEndObject();
                    return;
                }
            }
            if (v is TechEffect te) { WriteEffect(w, te); return; }
            if (v is Array arr)
            {
                w.WriteStartArray();
                foreach (var e in arr) WriteValue(w, e, null);
                w.WriteEndArray();
                return;
            }
            w.WriteStartObject();
            foreach (var fi in t.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                w.WritePropertyName(CamelCase(fi.Name));
                WriteValue(w, fi.GetValue(v), fi);
            }
            w.WriteEndObject();
        }

        /// <summary>Tech effects carry only the fields their kind uses.</summary>
        private static void WriteEffect(Utf8JsonWriter w, TechEffect e)
        {
            w.WriteStartObject();
            w.WriteString("kind", e.Kind.ToString());
            if (e.Kind == TechEffectKind.UnlockBuilding) w.WriteString("building", e.Building.ToString());
            else
            {
                if (e.Kind == TechEffectKind.ProductionMultiplier) w.WriteString("resource", e.Resource.ToString());
                if (e.Kind == TechEffectKind.ServiceMultiplier) w.WriteString("service", e.Service.ToString());
                w.WritePropertyName("value");
                WriteNumber(w, e.Value);
            }
            w.WriteEndObject();
        }

        private static Type IndexEnum(string fieldName, int length)
        {
            switch (fieldName)
            {
                case "BiomeHabitability": return length == Counts.Biomes ? typeof(Biome) : null;
                case "ProductionMultiplier": return length == Counts.Resources ? typeof(ResourceType) : null;
                case "RaceAffinity": return length == Counts.Races ? typeof(RaceId) : null;
                default: return null;
            }
        }

        private static bool IsSingleBit(ulong v) => (v & (v - 1)) == 0;

        private static void WriteNumber(Utf8JsonWriter w, double v)
        {
            if (double.IsNaN(v) || double.IsInfinity(v)) w.WriteNullValue();
            else w.WriteNumberValue(Math.Round(v, 6));
        }

        private static void WriteNumber(Utf8JsonWriter w, float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) w.WriteNullValue();
            else w.WriteNumberValue(v);
        }

        private static string CamelCase(string s) => string.IsNullOrEmpty(s) || char.IsLower(s[0]) ? s : char.ToLowerInvariant(s[0]) + s.Substring(1);
    }
}
