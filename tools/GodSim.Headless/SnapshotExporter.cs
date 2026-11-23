using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using GodSim.Core;
using GodSim.Core.Composition;

namespace GodSim.Headless
{
    /// <summary>
    /// Writes the "godsim.snapshot/1" JSON document consumed by the web viewer (tools/WebViewer).
    /// The schema is a contract with the viewer team: do not rename, remove or retype fields.
    /// Enum values are written as names, colors as "#rrggbb", floats rounded to 3 decimals,
    /// NaN/Infinity as 0. Everything is streamed through a Utf8JsonWriter (tile arrays are large).
    /// </summary>
    public static class SnapshotExporter
    {
        public const string Schema = "godsim.snapshot/1";
        public const string Generator = "GodSim.Headless";
        /// <summary>Game version written into meta.gameVersion.</summary>
        public const string GameVersion = "0.1.0";

        private const int FlushThreshold = 1 << 16;

        /// <summary>Exports to a file atomically (temp file + rename). IO failures propagate as IOException/UnauthorizedAccessException.</summary>
        public static void ExportToFile(GodSimulation sim, IReadOnlyList<HistorySample> history, string path)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            ExportToFile(sim.State, sim.Db, sim.Config, history, path);
        }

        public static void ExportToFile(WorldState state, GameDatabase db, SimConfig config, IReadOnlyList<HistorySample> history, string path)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path required", nameof(path));
            string full = Path.GetFullPath(path);
            string dir = Path.GetDirectoryName(full);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            string tmp = full + ".tmp";
            try
            {
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16))
                {
                    Write(fs, state, db, config, history);
                    fs.Flush(true);
                }
                File.Move(tmp, full, true);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }

        public static void Write(Stream stream, GodSimulation sim, IReadOnlyList<HistorySample> history)
        {
            if (sim == null) throw new ArgumentNullException(nameof(sim));
            Write(stream, sim.State, sim.Db, sim.Config, history);
        }

        /// <summary>Streams the snapshot document into <paramref name="stream"/>.</summary>
        public static void Write(Stream stream, WorldState state, GameDatabase db, SimConfig config, IReadOnlyList<HistorySample> history)
        {
            if (stream == null) throw new ArgumentNullException(nameof(stream));
            if (state == null) throw new ArgumentNullException(nameof(state));
            var opts = new JsonWriterOptions { Indented = false, SkipValidation = false };
            using (var w = new Utf8JsonWriter(stream, opts))
            {
                w.WriteStartObject();
                w.WriteString("schema", Schema);
                WriteMeta(w, state, config);
                WriteEnums(w);
                WriteTiles(w, state.Map);
                var tally = WorldQueries.TallyCivs(state);
                WriteCivilizations(w, state, tally);
                WriteSettlements(w, state);
                WriteBuildings(w, state, db);
                WriteArmies(w, state);
                WriteWars(w, state);
                WriteChronicle(w, state);
                WriteHistory(w, history);
                WriteGod(w, state.God);
                WriteStats(w, state.Stats);
                w.WriteEndObject();
                w.Flush();
            }
        }

        // ------------------------------------------------------------------ sections
        private static void WriteMeta(Utf8JsonWriter w, WorldState s, SimConfig config)
        {
            w.WriteStartObject("meta");
            w.WriteNumber("seed", s.Seed);
            w.WriteNumber("tick", s.Tick);
            w.WriteNumber("year", s.Year);
            w.WriteNumber("width", s.Map.Width);
            w.WriteNumber("height", s.Map.Height);
            w.WriteString("preset", (config != null ? config.Preset : WorldPreset.Continents).ToString());
            w.WriteString("generator", Generator);
            w.WriteString("gameVersion", GameVersion);
            w.WriteEndObject();
        }

        private static void WriteEnums(Utf8JsonWriter w)
        {
            w.WriteStartObject("enums");
            WriteNames<Biome>(w, "biomes");
            WriteNames<BuildingType>(w, "buildingTypes");
            WriteNames<BuildingCategory>(w, "categories");
            WriteNames<AgeId>(w, "ages");
            WriteNames<RaceId>(w, "races");
            WriteNames<SettlementTier>(w, "tiers");
            WriteNames<ChronicleCategory>(w, "chronicleCategories");
            w.WriteEndObject();
        }

        private static void WriteTiles(Utf8JsonWriter w, WorldMap map)
        {
            int n = map.Width * map.Height;
            w.WriteStartObject("tiles");

            w.WriteStartArray("biome");
            for (int i = 0; i < n; i++) { w.WriteNumberValue((int)map.Biomes[i]); MaybeFlush(w); }
            w.WriteEndArray();

            w.WriteStartArray("elevation");
            for (int i = 0; i < n; i++) { w.WriteNumberValue(Elevation255(map.Elevation[i])); MaybeFlush(w); }
            w.WriteEndArray();

            w.WriteStartArray("flags");
            for (int i = 0; i < n; i++) { w.WriteNumberValue((int)(ushort)map.Flags[i]); MaybeFlush(w); }
            w.WriteEndArray();

            w.WriteStartArray("owner");
            for (int i = 0; i < n; i++) { int o = map.Owner[i]; w.WriteNumberValue(o < 0 ? -1 : o); MaybeFlush(w); }
            w.WriteEndArray();

            w.WriteEndObject();
        }

        private static void WriteCivilizations(Utf8JsonWriter w, WorldState s, Dictionary<int, CivTally> tally)
        {
            w.WriteStartArray("civilizations");
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                tally.TryGetValue(c.Id, out var t);
                w.WriteStartObject();
                w.WriteNumber("id", c.Id);
                w.WriteString("name", c.Name ?? "");
                w.WriteString("race", c.Race.ToString());
                w.WriteString("color", ColorHex(c.Color));
                w.WriteString("age", c.Age.ToString());
                w.WriteBoolean("alive", c.IsAlive);
                w.WriteNumber("population", t.Population);
                w.WriteStartArray("settlements");
                for (int k = 0; k < c.SettlementIds.Count; k++)
                {
                    var st = s.Settlements.Get(c.SettlementIds[k]);
                    if (st != null && st.IsAlive && st.CivilizationId == c.Id) w.WriteNumberValue(st.Id);
                }
                w.WriteEndArray();
                w.WriteNumber("capitalId", c.CapitalId);
                w.WriteNumber("techs", c.KnownTechs.Count);
                WriteFloat(w, "totalResearch", c.TotalResearch);
                WriteFloat(w, "devotion", c.Devotion);
                WriteFloat(w, "godFavor", c.GodFavor);
                WriteFloat(w, "militaryPower", c.MilitaryPower);
                w.WriteEndObject();
                MaybeFlush(w);
            }
            w.WriteEndArray();
        }

        private static void WriteSettlements(Utf8JsonWriter w, WorldState s)
        {
            w.WriteStartArray("settlements");
            var items = s.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var st = items[i];
                if (!st.IsAlive) continue;
                int buildings = 0;
                for (int k = 0; k < st.BuildingIds.Count; k++)
                {
                    var b = s.Buildings.Get(st.BuildingIds[k]);
                    if (b != null && !b.IsDestroyed) buildings++;
                }
                w.WriteStartObject();
                w.WriteNumber("id", st.Id);
                w.WriteString("name", st.Name ?? "");
                w.WriteNumber("civId", st.CivilizationId);
                w.WriteString("race", st.Race.ToString());
                w.WriteNumber("x", st.Center.X);
                w.WriteNumber("y", st.Center.Y);
                w.WriteString("tier", st.Tier.ToString());
                w.WriteNumber("population", st.Population);
                w.WriteNumber("homeless", st.Homeless);
                w.WriteNumber("unemployed", st.Unemployed);
                WriteFloat(w, "happiness", st.Happiness);
                WriteFloat(w, "health", st.Health);
                w.WriteBoolean("isCapital", st.IsCapital);
                w.WriteNumber("buildingCount", buildings);
                w.WriteNumber("territoryRadius", st.TerritoryRadius);
                w.WriteStartObject("stockpile");
                var res = EnumUtil.AllResources;
                for (int r = 0; r < res.Length; r++) WriteFloat(w, res[r].ToString(), st.Stockpile.Get(res[r]));
                w.WriteEndObject();
                w.WriteEndObject();
                MaybeFlush(w);
            }
            w.WriteEndArray();
        }

        private static void WriteBuildings(Utf8JsonWriter w, WorldState s, GameDatabase db)
        {
            w.WriteStartArray("buildings");
            var items = s.Buildings.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var b = items[i];
                if (b.IsDestroyed) continue;
                BuildingDefinition def = null;
                if (db != null) db.TryGetBuilding(b.Type, out def);
                w.WriteStartObject();
                w.WriteNumber("id", b.Id);
                w.WriteString("type", b.Type.ToString());
                if (def != null) w.WriteString("category", def.Category.ToString()); else w.WriteNull("category");
                if (def != null) w.WriteString("shape", def.Shape.ToString()); else w.WriteNull("shape");
                w.WriteNumber("x", b.Origin.X);
                w.WriteNumber("y", b.Origin.Y);
                w.WriteNumber("w", b.Width);
                w.WriteNumber("h", b.Height);
                w.WriteNumber("settlementId", b.SettlementId);
                w.WriteNumber("civId", b.CivilizationId);
                w.WriteString("styleRace", b.StyleRace.ToString());
                w.WriteString("styleAge", b.StyleAge.ToString());
                w.WriteBoolean("complete", b.IsComplete);
                WriteFloat(w, "progress", b.ConstructionProgress);
                w.WriteNumber("residents", b.Residents.Count);
                w.WriteNumber("capacity", b.HousingCapacity);
                w.WriteNumber("workers", b.Workers.Count);
                WriteFloat(w, "condition", b.Condition);
                w.WriteEndObject();
                MaybeFlush(w);
            }
            w.WriteEndArray();
        }

        private static void WriteArmies(Utf8JsonWriter w, WorldState s)
        {
            w.WriteStartArray("armies");
            var items = s.Diplomacy.Armies.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var a = items[i];
                if (!a.IsAlive) continue;
                w.WriteStartObject();
                w.WriteNumber("id", a.Id);
                w.WriteNumber("civId", a.CivilizationId);
                w.WriteNumber("x", a.Position.X);
                w.WriteNumber("y", a.Position.Y);
                WriteFloat(w, "strength", a.Strength);
                w.WriteNumber("soldiers", a.SoldierIds.Count);
                w.WriteString("state", a.State.ToString());
                w.WriteNumber("targetSettlementId", a.TargetSettlementId);
                w.WriteEndObject();
                MaybeFlush(w);
            }
            w.WriteEndArray();
        }

        private static void WriteWars(Utf8JsonWriter w, WorldState s)
        {
            w.WriteStartArray("wars");
            var wars = s.Diplomacy.Wars;
            for (int i = 0; i < wars.Count; i++)
            {
                var war = wars[i];
                if (war == null) continue;
                w.WriteStartObject();
                w.WriteNumber("id", war.Id);
                w.WriteNumber("attackerId", war.AttackerId);
                w.WriteNumber("defenderId", war.DefenderId);
                w.WriteNumber("startYear", SimTime.Year(war.StartTick));
                w.WriteNumber("endYear", war.EndTick >= 0 ? SimTime.Year(war.EndTick) : -1);
                w.WriteBoolean("active", war.IsActive);
                WriteFloat(w, "warScore", war.WarScore);
                w.WriteString("casusBelli", war.CasusBelli ?? "");
                w.WriteEndObject();
                MaybeFlush(w);
            }
            w.WriteEndArray();
        }

        private static void WriteChronicle(Utf8JsonWriter w, WorldState s)
        {
            w.WriteStartArray("chronicle");
            var entries = s.Chronicle.Entries;
            for (int i = 0; i < entries.Count; i++)
            {
                var e = entries[i];
                bool hasPos = e.Position.IsValid && s.Map.InBounds(e.Position);
                w.WriteStartObject();
                w.WriteNumber("tick", e.Tick);
                w.WriteNumber("year", SimTime.Year(e.Tick));
                w.WriteString("category", e.Category.ToString());
                w.WriteString("text", e.Text ?? "");
                w.WriteNumber("civId", e.CivilizationId);
                w.WriteNumber("settlementId", e.SettlementId);
                w.WriteNumber("x", hasPos ? e.Position.X : -1);
                w.WriteNumber("y", hasPos ? e.Position.Y : -1);
                w.WriteNumber("importance", e.Importance);
                w.WriteEndObject();
                MaybeFlush(w);
            }
            w.WriteEndArray();
        }

        private static void WriteHistory(Utf8JsonWriter w, IReadOnlyList<HistorySample> history)
        {
            w.WriteStartArray("history");
            if (history != null)
            {
                for (int i = 0; i < history.Count; i++)
                {
                    var h = history[i];
                    if (h == null) continue;
                    w.WriteStartObject();
                    w.WriteNumber("year", h.Year);
                    w.WriteNumber("totalPopulation", h.TotalPopulation);
                    w.WriteStartArray("civs");
                    for (int k = 0; k < h.Civs.Count; k++)
                    {
                        var c = h.Civs[k];
                        w.WriteStartObject();
                        w.WriteNumber("id", c.Id);
                        w.WriteNumber("population", c.Population);
                        w.WriteString("age", c.Age.ToString());
                        w.WriteNumber("settlements", c.Settlements);
                        w.WriteNumber("buildings", c.Buildings);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                    MaybeFlush(w);
                }
            }
            w.WriteEndArray();
        }

        private static void WriteGod(Utf8JsonWriter w, GodState g)
        {
            w.WriteStartObject("god");
            WriteFloat(w, "mana", g.Mana);
            WriteFloat(w, "maxMana", g.MaxMana);
            WriteFloat(w, "alignment", g.Alignment);
            w.WriteNumber("powersCast", g.PowersCast);
            w.WriteEndObject();
        }

        private static void WriteStats(Utf8JsonWriter w, WorldStats st)
        {
            w.WriteStartObject("stats");
            w.WriteNumber("totalBirths", st.TotalBirths);
            w.WriteNumber("totalDeaths", st.TotalDeaths);
            w.WriteNumber("buildingsCompleted", st.BuildingsCompleted);
            w.WriteNumber("buildingsDestroyed", st.BuildingsDestroyed);
            w.WriteNumber("settlementsFounded", st.SettlementsFounded);
            w.WriteNumber("warsDeclared", st.WarsDeclared);
            w.WriteNumber("battlesFought", st.BattlesFought);
            w.WriteNumber("peakPopulation", st.PeakPopulation);
            w.WriteEndObject();
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Maps elevation -1..1 to 0..255 as round((e+1)*127.5), clamped; NaN maps to sea-level-ish 128.</summary>
        public static int Elevation255(float e)
        {
            double v = float.IsNaN(e) || float.IsInfinity(e) ? 0.0 : e;
            int r = (int)Math.Round((v + 1.0) * 127.5, MidpointRounding.AwayFromZero);
            return r < 0 ? 0 : (r > 255 ? 255 : r);
        }

        /// <summary>0xRRGGBBAA -> "#rrggbb".</summary>
        public static string ColorHex(uint rgba) => "#" + ((rgba >> 8) & 0xFFFFFFu).ToString("x6");

        /// <summary>Rounds to 3 decimals; NaN/Infinity become 0 (JSON cannot represent them).</summary>
        public static double Round3(float v)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) return 0.0;
            double r = Math.Round((double)v, 3, MidpointRounding.AwayFromZero);
            return r == 0.0 ? 0.0 : r; // normalizes -0
        }

        private static void WriteFloat(Utf8JsonWriter w, string name, float v) => w.WriteNumber(name, Round3(v));

        private static void WriteNames<T>(Utf8JsonWriter w, string property) where T : struct, Enum
        {
            w.WriteStartArray(property);
            var names = NamesByIndex<T>();
            for (int i = 0; i < names.Length; i++) w.WriteStringValue(names[i]);
            w.WriteEndArray();
        }

        /// <summary>Enum names indexed by numeric value (gaps, if any, become the number as text).</summary>
        public static string[] NamesByIndex<T>() where T : struct, Enum
        {
            var values = Enum.GetValues(typeof(T));
            int max = -1;
            foreach (var v in values) { int iv = Convert.ToInt32(v); if (iv > max) max = iv; }
            var names = new string[max + 1];
            for (int i = 0; i <= max; i++) names[i] = i.ToString();
            foreach (var v in values) { int iv = Convert.ToInt32(v); if (iv >= 0) names[iv] = v.ToString(); }
            return names;
        }

        private static void MaybeFlush(Utf8JsonWriter w)
        {
            if (w.BytesPending > FlushThreshold) w.Flush();
        }
    }
}
