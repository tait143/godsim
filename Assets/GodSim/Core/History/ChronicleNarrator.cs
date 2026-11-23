using System;
using System.Collections.Generic;
using System.Text;

namespace GodSim.Core.History
{
    /// <summary>
    /// Turns the <see cref="HistoryRecorder.Eras"/> timeline plus the persistent <c>WorldState.Chronicle</c> into
    /// readable prose: per-civilization age eras ("The Age of Bronze: the Dwarves of Karak founded 3 cities; war
    /// with the Orcs ended in 412."), per-century world summaries, a bounded full narration and epitaphs.
    /// Pure and read-only. Phrasing variety is deterministic: each choice is hashed from the tick (never ctx.Rng),
    /// so the same history always reads the same. All outputs are length-bounded. Meant for UI/tool time, not per tick.
    /// </summary>
    public static class ChronicleNarrator
    {
        /// <summary>Upper bound on <see cref="EraSummary.Text"/> length.</summary>
        public const int MaxSummaryLength = 480;
        /// <summary>Upper bound on epitaph length.</summary>
        public const int MaxEpitaphLength = 320;
        public const int TicksPerCentury = SimTime.TicksPerYear * 100;
        private const int MaxWarClauses = 3;
        private const int MaxCivClausesPerCentury = 4;
        private const int MaxErasPerCiv = 64;

        private static readonly string[][] FoundedForms = { new[] { "founded ", "city", "cities" }, new[] { "raised ", "new settlement", "new settlements" }, new[] { "built ", "town", "towns" } };
        private static readonly string[][] TechForms = { new[] { "mastered ", "discovery", "discoveries" }, new[] { "learned ", "new art", "new arts" }, new[] { "uncovered ", "secret", "secrets" } };
        private static readonly string[][] CaptureForms = { new[] { "conquered ", "settlement", "settlements" }, new[] { "seized ", "town", "towns" } };
        private static readonly string[] QuietForms = { "lived quietly", "kept to their hearths", "endured without great deeds" };
        private static readonly string[] WarAttackForms = { "went to war with {O} in {Y}", "marched against {O} in {Y}", "took up arms against {O} in {Y}" };
        private static readonly string[] WarDefendForms = { "were attacked by {O} in {Y}", "faced the armies of {O} in {Y}" };
        private static readonly string[] PeaceForms = { "war with {O} ended in {Y}", "made peace with {O} in {Y}", "laid down arms with {O} in {Y}" };
        private static readonly string[] AgeUpForms = { "entered the {A} in {Y}", "rose into the {A} in {Y}", "had reached the {A} by {Y}" };
        private static readonly string[] QuietCenturyForms = { "A quiet century passed.", "The years passed without great deeds.", "Seasons turned and little changed." };
        private static readonly string[] EpitaphOpenings = { "Here lie ", "So ended ", "Remember ", "Mourn " };
        private static readonly string[] EpitaphClosings =
        {
            "Their fires are cold, but their names endure.",
            "Dust remembers what the living forget.",
            "May the gods be kinder to their children.",
            "Their roads now lead only to ruins.",
            "The wind still carries their songs."
        };

        // ================================================================== civ eras
        /// <summary>
        /// Splits one civilization's timeline into its ages (one <see cref="EraSummary"/> per age it lived through,
        /// oldest first). Returns an empty list if the civ is unknown to both the state and the timeline.
        /// </summary>
        public static List<EraSummary> CivEras(WorldState state, GameDatabase db, IReadOnlyList<TimelineEvent> timeline, int civId)
        {
            var result = new List<EraSummary>();
            if (state == null) return result;
            var civ = state.Civilizations.Get(civId);
            int n = timeline != null ? timeline.Count : 0;

            AgeId age = civ != null ? civ.Age : AgeId.Dawn;
            long start = civ != null ? civ.FoundedTick : 0;
            bool any = civ != null, ageKnown = false;
            for (int i = 0; i < n; i++)
            {
                var e = timeline[i];
                if (e.CivId != civId && e.OtherCivId != civId) continue;
                if (!any) { any = true; start = e.Tick; }
                if (!ageKnown && e.Kind == TimelineEventKind.AgeAdvanced && e.CivId == civId) { age = ToAge(e.SubjectId); ageKnown = true; }
            }
            if (!any) return result;

            var t = new Tally();
            for (int i = 0; i < n; i++)
            {
                var e = timeline[i];
                bool mine = e.CivId == civId;
                if (!mine && e.OtherCivId != civId) continue;
                switch (e.Kind)
                {
                    case TimelineEventKind.CivilizationFounded: if (mine) start = Math.Min(start, e.Tick); break;
                    case TimelineEventKind.SettlementFounded: if (mine) t.Founded++; break;
                    case TimelineEventKind.SettlementCaptured: if (mine) t.Captured++; else t.Lost++; break;
                    case TimelineEventKind.TechResearched: if (mine) t.Techs++; break;
                    case TimelineEventKind.WarDeclared:
                    case TimelineEventKind.PeaceSigned:
                        t.AddWar(e.Kind == TimelineEventKind.PeaceSigned, mine, mine ? e.OtherCivId : e.CivId, e.Tick);
                        break;
                    case TimelineEventKind.CivilizationCollapsed:
                        if (mine) { t.Collapsed = true; t.Conqueror = e.OtherCivId; t.EndTick = e.Tick; }
                        else { t.Destroyed++; t.DestroyedCiv = e.CivId; }
                        break;
                    case TimelineEventKind.AgeAdvanced:
                        if (!mine) break;
                        t.AgeUp = true; t.NewAge = ToAge(e.Value); t.EndTick = e.Tick;
                        if (result.Count < MaxErasPerCiv - 1) result.Add(BuildCivEra(state, db, civId, age, start, t));
                        age = t.NewAge; start = e.Tick;
                        t.Reset();
                        break;
                }
            }
            if (!t.Collapsed) t.EndTick = civ != null && !civ.IsAlive && civ.CollapsedTick >= 0 ? civ.CollapsedTick : state.Tick;
            result.Add(BuildCivEra(state, db, civId, age, start, t));
            return result;
        }

        private static EraSummary BuildCivEra(WorldState state, GameDatabase db, int civId, AgeId age, long start, Tally t)
        {
            ulong seed = Seed(start, civId);
            var sb = new StringBuilder(160);
            string title = AgeTitle(db, age);
            sb.Append(title).Append(": ").Append(CivPhrase(state, db, civId)).Append(' ');

            int deeds = AppendDeeds(sb, seed, t, state, db);
            if (deeds == 0 && t.Wars.Count == 0) { sb.Append(Pick(seed, 7, QuietForms)); deeds = 1; }
            AppendWars(sb, seed, t, state, db, deeds > 0);
            AppendEnding(sb, seed, t, state, db, true);
            sb.Append('.');
            return new EraSummary
            {
                CivId = civId, Age = age, StartTick = start, EndTick = Math.Max(start, t.EndTick),
                Title = title, Text = Bound(sb.ToString(), MaxSummaryLength),
            };
        }

        // ================================================================== world centuries
        /// <summary>
        /// One <see cref="EraSummary"/> per century that has timeline events or legendary (importance 3) chronicle
        /// entries, oldest first, limited to the latest <paramref name="maxCenturies"/>. Civs are listed by ascending id.
        /// </summary>
        public static List<EraSummary> Centuries(WorldState state, GameDatabase db, IReadOnlyList<TimelineEvent> timeline, int maxCenturies = 32)
        {
            var result = new List<EraSummary>();
            if (state == null) return result;
            maxCenturies = MathUtil.Clamp(maxCenturies, 1, 1000);
            int n = timeline != null ? timeline.Count : 0;
            var chron = state.Chronicle.Entries;

            var centuries = new List<int>();
            for (int i = 0; i < n; i++) AddDistinct(centuries, CenturyOf(timeline[i].Tick));
            for (int i = 0; i < chron.Count; i++) if (chron[i].Importance >= 3) AddDistinct(centuries, CenturyOf(chron[i].Tick));
            centuries.Sort();
            int first = Math.Max(0, centuries.Count - maxCenturies);

            var civIds = new List<int>();
            var tallies = new List<Tally>();
            for (int ci = first; ci < centuries.Count; ci++)
            {
                int c = centuries[ci];
                long from = (long)c * TicksPerCentury, to = from + TicksPerCentury;
                civIds.Clear();
                for (int i = 0; i < n; i++)
                {
                    var e = timeline[i];
                    if (e.Tick >= from && e.Tick < to && e.CivId >= 0) AddDistinct(civIds, e.CivId);
                }
                civIds.Sort();
                while (tallies.Count < civIds.Count) tallies.Add(new Tally());
                for (int k = 0; k < civIds.Count; k++) tallies[k].Reset();
                AgeId topAge = AgeId.Dawn;
                for (int i = 0; i < n; i++)
                {
                    var e = timeline[i];
                    if (e.Tick < from || e.Tick >= to || e.CivId < 0) continue;
                    var t = tallies[civIds.BinarySearch(e.CivId)];
                    switch (e.Kind)
                    {
                        case TimelineEventKind.SettlementFounded: t.Founded++; break;
                        case TimelineEventKind.SettlementCaptured: t.Captured++; break;
                        case TimelineEventKind.TechResearched: t.Techs++; break;
                        case TimelineEventKind.WarDeclared: t.AddWar(false, true, e.OtherCivId, e.Tick); break;
                        case TimelineEventKind.PeaceSigned: t.AddWar(true, true, e.OtherCivId, e.Tick); break;
                        case TimelineEventKind.CivilizationCollapsed: t.Collapsed = true; t.Conqueror = e.OtherCivId; t.EndTick = e.Tick; break;
                        case TimelineEventKind.AgeAdvanced:
                            t.AgeUp = true; t.NewAge = ToAge(e.Value); t.EndTick = e.Tick;
                            if (t.NewAge > topAge) topAge = t.NewAge;
                            break;
                    }
                }

                ulong seed = Seed(from, c);
                var sb = new StringBuilder(256);
                string title = "Years " + (c * 100) + "-" + (c * 100 + 99);
                sb.Append(title).Append(": ");
                int clauses = 0, skipped = 0;
                for (int k = 0; k < civIds.Count; k++)
                {
                    var t = tallies[k];
                    if (!t.HasAnything) continue;
                    if (clauses >= MaxCivClausesPerCentury) { skipped++; continue; }
                    if (clauses > 0) sb.Append("; ");
                    ulong cs = Hashing.Combine(seed, (ulong)(uint)civIds[k]);
                    sb.Append(CivPhrase(state, db, civIds[k])).Append(' ');
                    int deeds = AppendDeeds(sb, cs, t, state, db);
                    AppendWars(sb, cs, t, state, db, deeds > 0);
                    AppendEnding(sb, cs, t, state, db, deeds > 0 || t.Wars.Count > 0);
                    clauses++;
                }
                if (skipped > 0) sb.Append("; and ").Append(skipped).Append(skipped == 1 ? " other people stirred" : " other peoples stirred");
                if (clauses > 0) sb.Append('.');

                int legends = 0;
                for (int i = 0; i < chron.Count && legends < 2; i++)
                {
                    var ce = chron[i];
                    if (ce.Importance < 3 || ce.Tick < from || ce.Tick >= to || string.IsNullOrEmpty(ce.Text)) continue;
                    if (ce.Category == ChronicleCategory.Civilization || ce.Category == ChronicleCategory.Age) continue; // already narrated
                    sb.Append(legends == 0 ? (clauses > 0 ? " Legends recall: " : "Legends recall: ") : " ").Append(Bound(ce.Text, 120));
                    legends++;
                }
                if (clauses == 0 && legends == 0) sb.Append(Pick(seed, 3, QuietCenturyForms));

                result.Add(new EraSummary
                {
                    Century = c, Age = topAge, StartTick = from, EndTick = to - 1,
                    Title = title, Text = Bound(sb.ToString(), MaxSummaryLength),
                });
            }
            return result;
        }

        /// <summary>The whole world history (century summaries, one per line), bounded to <paramref name="maxChars"/>.</summary>
        public static string Narrate(WorldState state, GameDatabase db, IReadOnlyList<TimelineEvent> timeline, int maxChars = 4000)
        {
            maxChars = MathUtil.Clamp(maxChars, 64, 200000);
            var eras = Centuries(state, db, timeline, 1000);
            if (eras.Count == 0) return "The world is young; no deeds have yet been recorded.";
            var sb = new StringBuilder(Math.Min(maxChars, 4096));
            // Keep the most recent centuries if everything does not fit: walk backwards to find the first that fits.
            int firstIdx = eras.Count - 1, total = eras[firstIdx].Text.Length;
            while (firstIdx > 0 && total + 1 + eras[firstIdx - 1].Text.Length <= maxChars) { firstIdx--; total += 1 + eras[firstIdx].Text.Length; }
            for (int i = firstIdx; i < eras.Count; i++)
            {
                if (i > firstIdx) sb.Append('\n');
                sb.Append(eras[i].Text);
            }
            return Bound(sb.ToString(), maxChars);
        }

        // ================================================================== epitaphs
        /// <summary>A short deterministic epitaph for a (fallen) civilization; "" for null. Living civs get a note instead.</summary>
        public static string Epitaph(Civilization civ) => BuildEpitaph(civ, null, -1, null);

        /// <summary>Richer epitaph using race/age names from the database and the timeline (settlements founded, conqueror).</summary>
        public static string Epitaph(Civilization civ, WorldState state, GameDatabase db, IReadOnlyList<TimelineEvent> timeline)
        {
            if (civ == null) return "";
            int founded = 0;
            string conqueror = null;
            int n = timeline != null ? timeline.Count : 0;
            for (int i = 0; i < n; i++)
            {
                var e = timeline[i];
                if (e.CivId != civ.Id) continue;
                if (e.Kind == TimelineEventKind.SettlementFounded) founded++;
                else if (e.Kind == TimelineEventKind.CivilizationCollapsed && e.OtherCivId >= 0) conqueror = OtherPhrase(state, db, e.OtherCivId);
            }
            return BuildEpitaph(civ, db, n > 0 ? founded : -1, conqueror);
        }

        private static string BuildEpitaph(Civilization civ, GameDatabase db, int founded, string conqueror)
        {
            if (civ == null) return "";
            string name = string.IsNullOrEmpty(civ.Name) ? "a nameless people" : "the " + civ.Name;
            if (civ.IsAlive) return Bound("No epitaph is carved for " + name + "; they still endure.", MaxEpitaphLength);

            long end = civ.CollapsedTick >= 0 ? civ.CollapsedTick : civ.AgeEnteredTick;
            ulong seed = Hashing.Combine((ulong)(uint)civ.Id, (ulong)end);
            int years = (int)(Math.Max(0L, end - civ.FoundedTick) / SimTime.TicksPerYear);
            string span = years < 1 ? "less than a year" : (years == 1 ? "a single year" : years + " years");

            var sb = new StringBuilder(200);
            sb.Append(Pick(seed, 1, EpitaphOpenings)).Append(name)
              .Append(" (years ").Append(SimTime.Year(civ.FoundedTick)).Append('-').Append(SimTime.Year(end)).Append("), ")
              .Append(Article(RaceAdjective(db, civ.Race))).Append(' ').Append(RaceAdjective(db, civ.Race)).Append(" people who endured ").Append(span)
              .Append(" and reached the ").Append(AgeLabel(db, civ.Age));
            int techs = civ.KnownTechs.Count;
            if (founded > 0) sb.Append(", founding ").Append(Count(founded, "settlement", "settlements"));
            if (techs > 0) sb.Append(founded > 0 ? " and mastering " : ", mastering ").Append(Count(techs, "art", "arts"));
            if (!string.IsNullOrEmpty(conqueror)) sb.Append(", until they fell to ").Append(conqueror);
            sb.Append(". ").Append(Pick(seed, 2, EpitaphClosings));
            return Bound(sb.ToString(), MaxEpitaphLength);
        }

        // ================================================================== phrase building
        private static int AppendDeeds(StringBuilder sb, ulong seed, Tally t, WorldState state, GameDatabase db)
        {
            int count = 0;
            int total = (t.Founded > 0 ? 1 : 0) + (t.Captured > 0 ? 1 : 0) + (t.Lost > 0 ? 1 : 0) + (t.Techs > 0 ? 1 : 0) + (t.Destroyed > 0 ? 1 : 0);
            if (t.Founded > 0) Deed(sb, ref count, total, Form(PickForm(seed, 11, FoundedForms), t.Founded));
            if (t.Captured > 0) Deed(sb, ref count, total, Form(PickForm(seed, 12, CaptureForms), t.Captured));
            if (t.Destroyed > 0) Deed(sb, ref count, total, "brought ruin to " + OtherPhrase(state, db, t.DestroyedCiv));
            if (t.Lost > 0) Deed(sb, ref count, total, "lost " + Count(t.Lost, "settlement", "settlements") + " to their foes");
            if (t.Techs > 0) Deed(sb, ref count, total, Form(PickForm(seed, 13, TechForms), t.Techs));
            return count;
        }

        private static void Deed(StringBuilder sb, ref int count, int total, string text)
        {
            if (count > 0) sb.Append(count == total - 1 ? " and " : ", ");
            sb.Append(text);
            count++;
        }

        /// <param name="separated">True when something already follows the subject (clauses start with "; ").
        /// False: the first clause continues the subject directly ("the Elves went to war with ...").</param>
        private static void AppendWars(StringBuilder sb, ulong seed, Tally t, WorldState state, GameDatabase db, bool separated)
        {
            for (int i = 0; i < t.Wars.Count; i++)
            {
                var w = t.Wars[i];
                bool sep = separated || i > 0;
                string text;
                if (w.Peace && !sep) text = "made peace with {O} in {Y}";
                else
                {
                    text = Pick(Hashing.Combine(seed, (ulong)w.Tick), 21 + i, w.Peace ? PeaceForms : (w.Attacker ? WarAttackForms : WarDefendForms));
                    // Every form except "war with X ended" needs the subject repeated after a separator.
                    if (sep && !text.StartsWith("war with", StringComparison.Ordinal)) text = "they " + text;
                }
                text = text.Replace("{O}", OtherPhrase(state, db, w.Other)).Replace("{Y}", SimTime.Year(w.Tick).ToString());
                if (sep) sb.Append("; ");
                sb.Append(text);
            }
            if (t.WarOverflow > 0) sb.Append("; and ").Append(Count(t.WarOverflow, "other conflict", "other conflicts")).Append(" followed");
        }

        private static void AppendEnding(StringBuilder sb, ulong seed, Tally t, WorldState state, GameDatabase db, bool separated)
        {
            string year = SimTime.Year(t.EndTick).ToString();
            string text = null;
            if (t.Collapsed)
                text = t.Conqueror >= 0 ? "fell to " + OtherPhrase(state, db, t.Conqueror) + " in " + year : "faded from history in " + year;
            else if (t.AgeUp)
                text = Pick(seed, 31, AgeUpForms).Replace("{A}", AgeLabel(db, t.NewAge)).Replace("{Y}", year);
            if (text == null) return;
            if (separated) sb.Append("; they ");
            sb.Append(text);
        }

        /// <summary>"the Dwarves of Karak Realm" (or "the Dwarves" if unnamed; "a forgotten people" if unknown).</summary>
        public static string CivPhrase(WorldState state, GameDatabase db, int civId)
        {
            var civ = state != null && civId >= 0 ? state.Civilizations.Get(civId) : null;
            if (civ == null) return "a forgotten people";
            string plural = RacePlural(db, civ.Race);
            return string.IsNullOrEmpty(civ.Name) ? "the " + plural : "the " + plural + " of " + civ.Name;
        }

        /// <summary>Short reference to another civ: "the Orcs" when it is the only civ of its race, else "the {Name}".</summary>
        public static string OtherPhrase(WorldState state, GameDatabase db, int civId)
        {
            var civ = state != null && civId >= 0 ? state.Civilizations.Get(civId) : null;
            if (civ == null) return "a forgotten people";
            int sameRace = 0;
            var civs = state.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) if (civs[i].Race == civ.Race) sameRace++;
            if (sameRace <= 1 || string.IsNullOrEmpty(civ.Name)) return "the " + RacePlural(db, civ.Race);
            return "the " + civ.Name;
        }

        /// <summary>"The Age of Bronze" (or "The Bronze Age" when the age name already contains "Age").</summary>
        public static string AgeTitle(GameDatabase db, AgeId age)
        {
            string name = AgeName(db, age);
            return name.IndexOf("Age", StringComparison.Ordinal) >= 0 ? "The " + name : "The Age of " + name;
        }

        private static string AgeLabel(GameDatabase db, AgeId age)
        {
            string name = AgeName(db, age);
            return name.IndexOf("Age", StringComparison.Ordinal) >= 0 ? name : name + " Age";
        }

        private static string AgeName(GameDatabase db, AgeId age)
        {
            int i = (int)age;
            if (db != null && i >= 0 && i < db.Ages.Count && db.Ages[i] != null && !string.IsNullOrEmpty(db.Ages[i].Name)) return db.Ages[i].Name;
            return age.ToString();
        }

        private static string RacePlural(GameDatabase db, RaceId race)
        {
            int i = (int)race;
            if (db != null && i >= 0 && i < db.Races.Count && db.Races[i] != null && !string.IsNullOrEmpty(db.Races[i].PluralName)) return db.Races[i].PluralName;
            return race + "s";
        }

        private static string RaceAdjective(GameDatabase db, RaceId race)
        {
            int i = (int)race;
            if (db != null && i >= 0 && i < db.Races.Count && db.Races[i] != null && !string.IsNullOrEmpty(db.Races[i].Adjective)) return db.Races[i].Adjective;
            return race.ToString();
        }

        private static string Article(string word) =>
            !string.IsNullOrEmpty(word) && "AEIOUaeiou".IndexOf(word[0]) >= 0 ? "an" : "a";

        private static string Count(int n, string singular, string plural) => n == 1 ? "a " + singular : n + " " + plural;

        private static string Form(string[] form, int n) => form[0] + Count(n, form[1], form[2]);

        private static ulong Seed(long tick, int salt) => Hashing.Combine((ulong)tick, (ulong)(uint)salt);

        private static string Pick(ulong seed, int salt, string[] options) =>
            options[(int)(Hashing.Combine(seed, (ulong)salt) % (ulong)options.Length)];

        private static string[] PickForm(ulong seed, int salt, string[][] options) =>
            options[(int)(Hashing.Combine(seed, (ulong)salt) % (ulong)options.Length)];

        private static string Bound(string s, int max)
        {
            if (s == null) return "";
            if (s.Length <= max) return s;
            return max <= 3 ? s.Substring(0, max) : s.Substring(0, max - 3) + "...";
        }

        private static int CenturyOf(long tick) => tick <= 0 ? 0 : (int)Math.Min(int.MaxValue, tick / TicksPerCentury);

        private static AgeId ToAge(int v) => (AgeId)MathUtil.Clamp(v, 0, Counts.Ages - 1);

        private static void AddDistinct(List<int> list, int v) { if (!list.Contains(v)) list.Add(v); }

        // ================================================================== tally
        private struct WarNote
        {
            public bool Peace;
            public bool Attacker;
            public int Other;
            public long Tick;
        }

        private sealed class Tally
        {
            public int Founded, Captured, Lost, Techs, Destroyed, DestroyedCiv = -1, WarOverflow, Conqueror = -1;
            public bool AgeUp, Collapsed;
            public AgeId NewAge;
            public long EndTick;
            public readonly List<WarNote> Wars = new List<WarNote>(MaxWarClauses);

            public bool HasAnything => Founded > 0 || Captured > 0 || Lost > 0 || Techs > 0 || Destroyed > 0 || Wars.Count > 0 || AgeUp || Collapsed;

            public void AddWar(bool peace, bool attacker, int other, long tick)
            {
                if (Wars.Count >= MaxWarClauses) { WarOverflow++; return; }
                Wars.Add(new WarNote { Peace = peace, Attacker = attacker, Other = other, Tick = tick });
            }

            public void Reset()
            {
                Founded = Captured = Lost = Techs = Destroyed = WarOverflow = 0;
                DestroyedCiv = -1; Conqueror = -1;
                AgeUp = Collapsed = false;
                NewAge = AgeId.Dawn;
                EndTick = 0;
                Wars.Clear();
            }
        }
    }
}
