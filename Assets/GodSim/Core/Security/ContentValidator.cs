using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GodSim.Core.Security
{
    /// <summary>
    /// Deep validation of static game content (built-in catalogs today, mods tomorrow), going beyond
    /// <see cref="GameDatabase.Validate"/>. Content is treated as untrusted: every check is null-safe, never throws,
    /// never echoes content text into messages (only enum names, ids and numbers), and is bounded in output size.
    /// <para>Checks performed:</para>
    /// <list type="bullet">
    /// <item><description>structural checks of <see cref="GameDatabase.Validate"/> (reported as errors);</description></item>
    /// <item><description>every BuildingType registered and reachable by some race in some age (MinAge or a tech unlock);</description></item>
    /// <item><description>production-chain closure: every input resource is producible (transitively, from buildings available at or before the consumer's earliest age) for every race allowed to use the consumer;</description></item>
    /// <item><description>every race has Food, Wood and Stone producers and a dwelling in the Dawn age;</description></item>
    /// <item><description>average tech cost non-decreasing by age (warning);</description></item>
    /// <item><description>all numbers finite, non-negative and within sane bounds (cost &lt;= 100000, outputs &lt;= 10 per worker, BuildWork &lt;= 100000, ...);</description></item>
    /// <item><description>enum fields and flag masks hold defined values (undefined values would index out of range elsewhere);</description></item>
    /// <item><description>text bounds (names &lt;= 64, descriptions &lt;= 1000) and no control/bidi/invisible characters;</description></item>
    /// <item><description>civilization name patterns may only use "{0}" (alignment such as "{0,99999999}" would allocate huge strings);</description></item>
    /// <item><description>exactly one race allowed per racial unique; wrath powers cost &gt; 0.</description></item>
    /// </list>
    /// <para>
    /// Placeholder detection: when no building produces any resource (e.g. the shared TestDb stub), the database is
    /// treated as placeholder content; production-chain and Dawn-producer checks and the by-design racial-unique rule
    /// are then reported as warnings instead of errors.
    /// </para>
    /// </summary>
    public static class ContentValidator
    {
        /// <summary>Maximum length of any name-like text field.</summary>
        public const int MaxNameLength = 64;
        /// <summary>Maximum length of any description-like text field.</summary>
        public const int MaxDescriptionLength = 1000;
        /// <summary>Maximum amount of a single building cost entry.</summary>
        public const float MaxCost = 100000f;
        /// <summary>Maximum output of a single resource per worker per tick.</summary>
        public const float MaxOutputPerWorker = 10f;
        /// <summary>Maximum input of a single resource per worker per tick.</summary>
        public const float MaxInputPerWorker = 10f;
        /// <summary>Maximum builder-ticks of labor for one building.</summary>
        public const float MaxBuildWork = 100000f;
        /// <summary>Maximum service points per building per tick.</summary>
        public const float MaxServicePerBuilding = 10000f;
        /// <summary>Maximum storage capacity added by one building.</summary>
        public const float MaxStorageCapacity = 1000000f;
        /// <summary>Maximum gold upkeep per building per tick.</summary>
        public const float MaxUpkeep = 1000f;
        /// <summary>Maximum research cost of one technology.</summary>
        public const float MaxTechCost = 10000000f;
        /// <summary>Maximum mana cost of one god power.</summary>
        public const float MaxManaCost = 100000f;
        /// <summary>Maximum residents of one building.</summary>
        public const int MaxHousing = 1000;
        /// <summary>Maximum job slots of one building.</summary>
        public const int MaxJobSlots = 1000;
        /// <summary>Maximum god power cooldown (1000 years).</summary>
        public const int MaxCooldownTicks = SimTime.TicksPerYear * 1000;

        /// <summary>
        /// Maximum number of techs accepted. GameDatabase.Validate detects prerequisite cycles recursively, so an
        /// unbounded (hostile) tech chain could overflow the stack, which cannot be caught; above this count the
        /// structural pass is skipped and an error is reported instead.
        /// </summary>
        public const int MaxTechCount = 4096;

        private const int Unavailable = int.MaxValue;

        private static readonly BuildingType[] DesignRacialUniques =
        {
            BuildingType.GrandCathedral, BuildingType.WorldTree, BuildingType.DeepForge, BuildingType.WarIdol, BuildingType.SunPyramid
        };
        private static readonly RaceId[] DesignRacialUniqueOwners = { RaceId.Human, RaceId.Elf, RaceId.Dwarf, RaceId.Orc, RaceId.Saurian };
        private static readonly ResourceType[] DawnEssentials = { ResourceType.Food, ResourceType.Wood, ResourceType.Stone };
        private static readonly int AllPlacementBits = AllFlagBits(typeof(PlacementRule));

        /// <summary>Returns every error found (deep checks plus <see cref="GameDatabase.Validate"/>). Empty = valid.</summary>
        public static List<string> Validate(GameDatabase db) => new List<string>(Analyze(db).Errors);

        /// <summary>Runs every check and returns errors and warnings separately. Never throws.</summary>
        public static ContentReport Analyze(GameDatabase db)
        {
            var r = new ContentReport();
            if (db == null) { r.AddError("Game database is missing."); return r; }

            bool structuralOk;
            if (db.Techs.Count > MaxTechCount)
            {
                r.AddError("Too many techs (" + db.Techs.Count + " > " + MaxTechCount + "); structural validation skipped.");
                structuralOk = false;
            }
            else
                structuralOk = Section(r, "structure", () =>
                {
                    foreach (var e in db.Validate()) r.AddError("[structure] " + e);
                });
            if (!structuralOk) Section(r, "registration", () => CheckRegistration(db, r));

            bool realContent = false;
            Section(r, "economy detection", () => { realContent = AnyProduction(db); });

            Section(r, "races", () => CheckRaces(db, r));
            Section(r, "ages", () => CheckAges(db, r));
            Section(r, "buildings", () => CheckBuildings(db, r));
            Section(r, "racial uniques", () => CheckRacialUniques(db, r, realContent));
            Section(r, "techs", () => CheckTechs(db, r));
            Section(r, "god powers", () => CheckGodPowers(db, r));
            Section(r, "economy", () => CheckEconomy(db, r, realContent));
            return r;
        }

        private static bool Section(ContentReport r, string name, Action body)
        {
            try { body(); return true; }
            catch (Exception ex)
            {
                r.AddError("Content validation of " + name + " aborted (" + ex.GetType().Name + "): content is malformed.");
                return false;
            }
        }

        // ================================================================== registration

        private static void CheckRegistration(GameDatabase db, ContentReport r)
        {
            for (int i = 0; i < Counts.Races; i++) if (db.Races[i] == null) r.AddError("Race " + (RaceId)i + " is not registered.");
            for (int i = 0; i < Counts.Ages; i++) if (db.Ages[i] == null) r.AddError("Age " + (AgeId)i + " is not registered.");
            for (int i = 0; i < Counts.Buildings; i++) if (db.Buildings[i] == null) r.AddError("Building " + (BuildingType)i + " is not registered.");
            for (int i = 0; i < Counts.GodPowers; i++) if (db.GodPowers[i] == null) r.AddError("God power " + (GodPowerType)i + " is not registered.");
        }

        // ================================================================== races

        private static void CheckRaces(GameDatabase db, ContentReport r)
        {
            for (int i = 0; i < Counts.Races; i++)
            {
                var d = db.Races[i];
                if (d == null) continue;
                string w = "Race " + (RaceId)i;
                if ((int)d.Id != i) r.AddError(w + ": Id does not match its registry slot.");
                Text(r, w, "Name", d.Name, MaxNameLength, false, false);
                Text(r, w, "PluralName", d.PluralName, MaxNameLength, false, false);
                Text(r, w, "Adjective", d.Adjective, MaxNameLength, false, false);
                Text(r, w, "Description", d.Description, MaxDescriptionLength, true, true);

                Num(r, w, "AdulthoodAge", d.AdulthoodAge, 0.01f, 1000f, true);
                Num(r, w, "ElderAge", d.ElderAge, 0.01f, 10000f, true);
                Num(r, w, "LifeExpectancy", d.LifeExpectancy, 0.01f, 10000f, true);
                if (d.ElderAge <= d.AdulthoodAge) r.AddWarning(w + ": ElderAge should exceed AdulthoodAge.");
                Num(r, w, "FertilityPerYear", d.FertilityPerYear, 0f, 1f, true);
                Int(r, w, "MaxChildren", d.MaxChildren, 0, 100, true);
                Num(r, w, "FoodPerTick", d.FoodPerTick, 0f, 10f, true);
                Num(r, w, "BaseHealth", d.BaseHealth, 0f, 10f, true);
                Num(r, w, "DiseaseResistance", d.DiseaseResistance, 0f, 10f, true);
                Num(r, w, "BuildSpeed", d.BuildSpeed, 0f, 10f, true);
                Num(r, w, "ResearchRate", d.ResearchRate, 0f, 10f, true);
                Num(r, w, "FaithRate", d.FaithRate, 0f, 10f, true);
                Num(r, w, "MilitaryStrength", d.MilitaryStrength, 0f, 10f, true);
                Num(r, w, "TradeAptitude", d.TradeAptitude, 0f, 10f, true);
                Num(r, w, "MoveSpeed", d.MoveSpeed, 0f, 10f, true);
                Arr(r, w, "BiomeHabitability", d.BiomeHabitability, 0f, 1f);
                Arr(r, w, "ProductionMultiplier", d.ProductionMultiplier, 0f, 10f);
                Arr(r, w, "RaceAffinity", d.RaceAffinity, -1f, 1f);
                if (d.BiomeHabitability != null)
                {
                    bool any = false;
                    for (int b = 0; b < d.BiomeHabitability.Length; b++) if (d.BiomeHabitability[b] > 0f) any = true;
                    if (!any) r.AddWarning(w + ": no habitable biome.");
                }

                Num(r, w, "Aggression", d.Aggression, 0f, 1f, false);
                Num(r, w, "Expansionism", d.Expansionism, 0f, 1f, false);
                Num(r, w, "Piety", d.Piety, 0f, 1f, false);
                Num(r, w, "Industriousness", d.Industriousness, 0f, 1f, false);
                Num(r, w, "Curiosity", d.Curiosity, 0f, 1f, false);
                Num(r, w, "Diplomacy", d.Diplomacy, 0f, 1f, false);
                Num(r, w, "Xenophobia", d.Xenophobia, 0f, 1f, false);
                Num(r, w, "Mercantilism", d.Mercantilism, 0f, 1f, false);
                Int(r, w, "MinSettlementSpacing", d.MinSettlementSpacing, 1, 256, true);
                Num(r, w, "Compactness", d.Compactness, 0f, 1f, false);
                Num(r, w, "RoadAffinity", d.RoadAffinity, 0f, 1f, false);

                if (d.Architecture == null) r.AddError(w + ": Architecture is null.");
                else
                {
                    Num(r, w, "Architecture.HeightScale", d.Architecture.HeightScale, 0.01f, 10f, true);
                    Num(r, w, "Architecture.WidthScale", d.Architecture.WidthScale, 0.01f, 10f, true);
                    Num(r, w, "Architecture.Roundness", d.Architecture.Roundness, 0f, 1f, false);
                    if (!Enum.IsDefined(typeof(RoofStyle), d.Architecture.Roof)) r.AddError(w + ": Architecture.Roof is not a defined RoofStyle.");
                    Text(r, w, "Architecture.StyleTag", d.Architecture.StyleTag, MaxNameLength, true, false);
                }

                if (d.Names == null) r.AddError(w + ": Names is null.");
                else
                {
                    NameList(r, w, "Names.PersonPrefixes", d.Names.PersonPrefixes);
                    NameList(r, w, "Names.MaleSuffixes", d.Names.MaleSuffixes);
                    NameList(r, w, "Names.FemaleSuffixes", d.Names.FemaleSuffixes);
                    NameList(r, w, "Names.FamilyNames", d.Names.FamilyNames);
                    NameList(r, w, "Names.SettlementPrefixes", d.Names.SettlementPrefixes);
                    NameList(r, w, "Names.SettlementSuffixes", d.Names.SettlementSuffixes);
                    NameList(r, w, "Names.CivilizationPatterns", d.Names.CivilizationPatterns);
                    var pats = d.Names.CivilizationPatterns;
                    if (pats != null)
                        for (int p = 0; p < pats.Length; p++)
                        {
                            if (pats[p] == null) continue;
                            int placeholders;
                            if (!IsSafeFormatPattern(pats[p], out placeholders))
                                r.AddError(w + ": Names.CivilizationPatterns[" + p + "] may only contain the placeholder {0} (no alignment, format or other indices).");
                            else if (placeholders == 0)
                                r.AddWarning(w + ": Names.CivilizationPatterns[" + p + "] has no {0} placeholder.");
                        }
                }

                BuildingList(r, w, "UniqueBuildings", d.UniqueBuildings);
                BuildingList(r, w, "FavoredBuildings", d.FavoredBuildings);
            }
        }

        private static void NameList(ContentReport r, string w, string field, string[] arr)
        {
            if (arr == null) { r.AddError(w + ": " + field + " is null."); return; }
            if (arr.Length > 4096) { r.AddError(w + ": " + field + " has too many entries (" + arr.Length + ")."); return; }
            for (int i = 0; i < arr.Length; i++)
            {
                if (arr[i] == null) { r.AddWarning(w + ": " + field + "[" + i + "] is null."); continue; }
                Text(r, w, field + "[" + i + "]", arr[i], MaxNameLength, true, false);
            }
        }

        private static void BuildingList(ContentReport r, string w, string field, BuildingType[] arr)
        {
            if (arr == null) { r.AddError(w + ": " + field + " is null."); return; }
            for (int i = 0; i < arr.Length; i++)
                if (!Enum.IsDefined(typeof(BuildingType), arr[i])) r.AddError(w + ": " + field + "[" + i + "] is not a defined BuildingType.");
        }

        /// <summary>
        /// Accepts only literal text, "{{", "}}" and the exact placeholder "{0}". Rejects "{1}", "{0,999999}", "{0:N}",
        /// unbalanced braces, etc. (string.Format alignment can allocate gigabytes; other indices throw).
        /// </summary>
        internal static bool IsSafeFormatPattern(string p, out int placeholders)
        {
            placeholders = 0;
            for (int i = 0; i < p.Length; i++)
            {
                char c = p[i];
                if (c == '{')
                {
                    if (i + 1 < p.Length && p[i + 1] == '{') { i++; continue; }
                    if (i + 2 < p.Length && p[i + 1] == '0' && p[i + 2] == '}') { placeholders++; i += 2; continue; }
                    return false;
                }
                if (c == '}')
                {
                    if (i + 1 < p.Length && p[i + 1] == '}') { i++; continue; }
                    return false;
                }
            }
            return true;
        }

        // ================================================================== ages

        private static void CheckAges(GameDatabase db, ContentReport r)
        {
            var techsPerAge = new int[Counts.Ages];
            var techs = db.Techs;
            for (int t = 0; t < techs.Count; t++)
            {
                var td = techs[t];
                if (td != null && (int)td.Age < Counts.Ages) techsPerAge[(int)td.Age]++;
            }
            for (int i = 0; i < Counts.Ages; i++)
            {
                var d = db.Ages[i];
                if (d == null) continue;
                string w = "Age " + (AgeId)i;
                if ((int)d.Id != i) r.AddError(w + ": Id does not match its registry slot.");
                Text(r, w, "Name", d.Name, MaxNameLength, false, false);
                Text(r, w, "Description", d.Description, MaxDescriptionLength, true, true);
                Text(r, w, "ArchitectureNotes", d.ArchitectureNotes, MaxDescriptionLength, true, true);
                Num(r, w, "ResearchThreshold", d.ResearchThreshold, 0f, 1e12f, true);
                Int(r, w, "RequiredTechsFromPreviousAge", d.RequiredTechsFromPreviousAge, 0, 100000, true);
                if (i == 0 && d.RequiredTechsFromPreviousAge > 0) r.AddWarning(w + ": the first age cannot require techs from a previous age.");
                if (i > 0 && d.RequiredTechsFromPreviousAge > techsPerAge[i - 1])
                    r.AddError(w + ": requires " + d.RequiredTechsFromPreviousAge + " techs from " + (AgeId)(i - 1) + " but only " + techsPerAge[i - 1] + " exist (age unreachable).");
                Int(r, w, "MinPopulation", d.MinPopulation, 0, 10000000, true);
                if (d.MinPopulation > 250000) r.AddWarning(w + ": MinPopulation exceeds the largest allowed MaxPopulation (unreachable).");
                Int(r, w, "MinSettlements", d.MinSettlements, 0, 10000, true);
                Num(r, w, "ProductionBonus", d.ProductionBonus, -1f, 10f, true);
                Num(r, w, "ResearchBonus", d.ResearchBonus, -1f, 10f, true);
                Num(r, w, "HealthBonus", d.HealthBonus, -1f, 10f, true);
                Num(r, w, "LifespanBonusYears", d.LifespanBonusYears, -1000f, 1000f, true);
                Num(r, w, "BaseSoldierStrength", d.BaseSoldierStrength, 0f, 1000f, true);
            }
        }

        // ================================================================== buildings

        private static void CheckBuildings(GameDatabase db, ContentReport r)
        {
            for (int i = 0; i < Counts.Buildings; i++)
            {
                var d = db.Buildings[i];
                if (d == null) continue;
                string w = "Building " + (BuildingType)i;
                if ((int)d.Type != i) r.AddError(w + ": Type does not match its registry slot.");
                Text(r, w, "Name", d.Name, MaxNameLength, false, false);
                Text(r, w, "Description", d.Description, MaxDescriptionLength, true, true);

                if (!Enum.IsDefined(typeof(BuildingCategory), d.Category)) r.AddError(w + ": Category is not defined.");
                if (!Enum.IsDefined(typeof(BuildingShape), d.Shape)) r.AddError(w + ": Shape is not defined.");
                if (!Enum.IsDefined(typeof(JobType), d.Job)) r.AddError(w + ": Job is not defined.");
                if (!Enum.IsDefined(typeof(SettlementTier), d.MinTier)) r.AddError(w + ": MinTier is not defined.");
                if (!Enum.IsDefined(typeof(AgeId), d.MinAge)) r.AddError(w + ": MinAge is not defined.");
                if (!Enum.IsDefined(typeof(AgeId), d.MaxAge)) r.AddError(w + ": MaxAge is not defined.");
                if (((int)d.AllowedRaces & ~(int)RaceMask.All) != 0) r.AddError(w + ": AllowedRaces has undefined bits.");
                if (((int)d.Placement & ~AllPlacementBits) != 0) r.AddError(w + ": Placement has undefined bits.");

                Int(r, w, "MinPopulation", d.MinPopulation, 0, 10000000, true);
                Int(r, w, "MaxPerSettlement", d.MaxPerSettlement, -1, 100000, true);
                Amounts(r, w, "Cost", d.Cost, MaxCost);
                Num(r, w, "BuildWork", d.BuildWork, 0f, MaxBuildWork, true);
                Num(r, w, "MaxCondition", d.MaxCondition, 0.01f, 1000000f, true);
                Int(r, w, "HousingMin", d.HousingMin, 0, MaxHousing, true);
                Int(r, w, "HousingMax", d.HousingMax, 0, MaxHousing, true);
                Int(r, w, "JobSlots", d.JobSlots, 0, MaxJobSlots, true);
                Amounts(r, w, "InputsPerWorker", d.InputsPerWorker, MaxInputPerWorker);
                Amounts(r, w, "OutputsPerWorker", d.OutputsPerWorker, MaxOutputPerWorker);
                if (HasPositive(d.OutputsPerWorker) && !d.HasJobs) r.AddWarning(w + ": has outputs per worker but no jobs, so it never produces.");
                if (HasPositive(d.InputsPerWorker) && !HasPositive(d.OutputsPerWorker)) r.AddWarning(w + ": consumes inputs but produces no resource.");

                if (d.Services == null) r.AddError(w + ": Services is null.");
                else
                {
                    if (d.Services.Length > Counts.Services * 4) r.AddError(w + ": Services has too many entries.");
                    for (int s = 0; s < d.Services.Length; s++)
                    {
                        if (!Enum.IsDefined(typeof(ServiceType), d.Services[s].Type)) r.AddError(w + ": Services[" + s + "] has an undefined ServiceType.");
                        Num(r, w, "Services[" + s + "].Amount", d.Services[s].Amount, 0f, MaxServicePerBuilding, true);
                    }
                }
                Int(r, w, "ServiceRadius", d.ServiceRadius, 0, 128, true);
                Num(r, w, "StorageCapacity", d.StorageCapacity, 0f, MaxStorageCapacity, true);
                Num(r, w, "UpkeepGold", d.UpkeepGold, 0f, MaxUpkeep, true);
                Num(r, w, "Desirability", d.Desirability, -1f, 1f, false);
                Num(r, w, "VisualHeight", d.VisualHeight, 0f, 100f, false);
                if (d.AllowedRaces == RaceMask.None) r.AddError(w + ": AllowedRaces is None, so no race can ever build it.");
            }
        }

        private static void Amounts(ContentReport r, string w, string field, ResourceAmount[] arr, float max)
        {
            if (arr == null) { r.AddError(w + ": " + field + " is null."); return; }
            if (arr.Length > Counts.Resources * 4) { r.AddError(w + ": " + field + " has too many entries (" + arr.Length + ")."); return; }
            for (int i = 0; i < arr.Length; i++)
            {
                if (!Enum.IsDefined(typeof(ResourceType), arr[i].Type)) r.AddError(w + ": " + field + "[" + i + "] has an undefined ResourceType.");
                Num(r, w, field + "[" + i + "].Amount", arr[i].Amount, 0f, max, true);
            }
        }

        private static bool HasPositive(ResourceAmount[] arr)
        {
            if (arr == null) return false;
            for (int i = 0; i < arr.Length; i++) if (arr[i].Amount > 0f) return true;
            return false;
        }

        // ================================================================== racial uniques

        private static void CheckRacialUniques(GameDatabase db, ContentReport r, bool realContent)
        {
            var owner = new int[Counts.Buildings];
            for (int i = 0; i < owner.Length; i++) owner[i] = -1;
            for (int ri = 0; ri < Counts.Races; ri++)
            {
                var race = db.Races[ri];
                if (race == null || race.UniqueBuildings == null) continue;
                for (int k = 0; k < race.UniqueBuildings.Length; k++)
                {
                    int b = (int)race.UniqueBuildings[k];
                    if (b < 0 || b >= Counts.Buildings) continue; // reported by CheckRaces
                    if (owner[b] >= 0 && owner[b] != ri)
                        r.AddError("Building " + (BuildingType)b + " is listed as a unique by both " + (RaceId)owner[b] + " and " + (RaceId)ri + ".");
                    else owner[b] = ri;
                }
            }
            for (int b = 0; b < Counts.Buildings; b++)
            {
                int designIdx = Array.IndexOf(DesignRacialUniques, (BuildingType)b);
                if (owner[b] < 0 && designIdx < 0) continue;
                var def = db.Buildings[b];
                if (def == null) continue;
                int bits = PopCount((int)def.AllowedRaces & (int)RaceMask.All);
                string w = "Racial unique " + (BuildingType)b;
                if (owner[b] >= 0)
                {
                    var o = (RaceId)owner[b];
                    if (bits != 1) r.AddError(w + ": must be allowed for exactly one race (allowed for " + bits + ").");
                    else if (!def.AllowedRaces.Allows(o)) r.AddError(w + ": is listed by " + o + " but allowed for a different race.");
                }
                else
                {
                    var expected = DesignRacialUniqueOwners[designIdx];
                    string msg = null;
                    if (bits != 1) msg = w + ": must be allowed for exactly one race (allowed for " + bits + ").";
                    else if (!def.AllowedRaces.Allows(expected)) msg = w + ": is designed for " + expected + " but allowed for a different race.";
                    if (msg != null) { if (realContent) r.AddError(msg); else r.AddWarning(msg + " (placeholder content)"); }
                }
            }
        }

        private static int PopCount(int v)
        {
            int c = 0;
            while (v != 0) { c += v & 1; v >>= 1; }
            return c;
        }

        // ================================================================== techs

        private static void CheckTechs(GameDatabase db, ContentReport r)
        {
            var techs = db.Techs;
            var sum = new double[Counts.Ages];
            var count = new int[Counts.Ages];
            for (int i = 0; i < techs.Count; i++)
            {
                var t = techs[i];
                if (t == null) { r.AddError("Tech #" + i + " is null."); continue; }
                string w = TechLabel(t, i);
                if (string.IsNullOrEmpty(t.Key)) r.AddError(w + ": Key is empty.");
                else
                {
                    Text(r, w, "Key", t.Key, MaxNameLength, false, false);
                    if (!IsIdentifier(t.Key)) r.AddWarning(w + ": Key should use only [a-z0-9._-].");
                }
                Text(r, w, "Name", t.Name, MaxNameLength, false, false);
                Text(r, w, "Description", t.Description, MaxDescriptionLength, true, true);
                bool ageOk = Enum.IsDefined(typeof(AgeId), t.Age);
                if (!ageOk) r.AddError(w + ": Age is not defined.");
                Num(r, w, "Cost", t.Cost, 0.0001f, MaxTechCost, true);
                if (ageOk && !float.IsNaN(t.Cost) && !float.IsInfinity(t.Cost)) { sum[(int)t.Age] += t.Cost; count[(int)t.Age]++; }
                if (((int)t.AllowedRaces & ~(int)RaceMask.All) != 0) r.AddError(w + ": AllowedRaces has undefined bits.");
                if (t.AllowedRaces == RaceMask.None) r.AddWarning(w + ": AllowedRaces is None, so no race can research it.");

                if (t.PrerequisiteKeys == null) r.AddError(w + ": PrerequisiteKeys is null.");
                else
                    for (int k = 0; k < t.PrerequisiteKeys.Length; k++)
                    {
                        string key = t.PrerequisiteKeys[k];
                        int id = db.TechIdOf(key);
                        if (id < 0) r.AddError(w + ": PrerequisiteKeys[" + k + "] does not name a registered tech.");
                        else if (id == t.Id) r.AddError(w + ": lists itself as a prerequisite.");
                    }
                if (t.Prerequisites == null) r.AddError(w + ": Prerequisites is null.");

                if (t.Effects == null) { r.AddError(w + ": Effects is null."); continue; }
                for (int e = 0; e < t.Effects.Length; e++)
                {
                    var fx = t.Effects[e];
                    string fw = w + ": Effects[" + e + "]";
                    if (!Enum.IsDefined(typeof(TechEffectKind), fx.Kind)) { r.AddError(fw + " has an undefined Kind."); continue; }
                    switch (fx.Kind)
                    {
                        case TechEffectKind.UnlockBuilding:
                            if (!Enum.IsDefined(typeof(BuildingType), fx.Building) || db.Buildings[(int)fx.Building] == null)
                                r.AddError(fw + " unlocks an undefined or unregistered building.");
                            break;
                        case TechEffectKind.ProductionMultiplier:
                            if (!Enum.IsDefined(typeof(ResourceType), fx.Resource)) r.AddError(fw + " targets an undefined ResourceType.");
                            break;
                        case TechEffectKind.ServiceMultiplier:
                            if (!Enum.IsDefined(typeof(ServiceType), fx.Service)) r.AddError(fw + " targets an undefined ServiceType.");
                            break;
                    }
                    if (float.IsNaN(fx.Value) || float.IsInfinity(fx.Value)) r.AddError(fw + " Value is not a finite number.");
                    else if (fx.Kind != TechEffectKind.UnlockBuilding)
                    {
                        float limit = fx.Kind == TechEffectKind.LifespanBonus ? 1000f : 100f;
                        float soft = fx.Kind == TechEffectKind.LifespanBonus ? 100f : 10f;
                        if (Math.Abs(fx.Value) > limit) r.AddError(fw + " Value " + Fmt(fx.Value) + " exceeds +/-" + Fmt(limit) + ".");
                        else if (Math.Abs(fx.Value) > soft) r.AddWarning(fw + " Value " + Fmt(fx.Value) + " is unusually large.");
                    }
                }
            }

            double prevAvg = -1;
            AgeId prevAge = AgeId.Dawn;
            for (int a = 0; a < Counts.Ages; a++)
            {
                if (count[a] == 0) continue;
                double avg = sum[a] / count[a];
                if (prevAvg >= 0 && avg < prevAvg)
                    r.AddWarning("Average tech cost decreases from " + Fmt((float)prevAvg) + " (" + prevAge + ") to " + Fmt((float)avg) + " (" + (AgeId)a + ").");
                prevAvg = avg;
                prevAge = (AgeId)a;
            }
        }

        private static string TechLabel(TechDefinition t, int index)
        {
            string k = t.Key;
            if (!string.IsNullOrEmpty(k) && k.Length <= MaxNameLength && IsIdentifier(k)) return "Tech '" + k + "'";
            return "Tech #" + index;
        }

        private static bool IsIdentifier(string s)
        {
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '.' || c == '_' || c == '-';
                if (!ok) return false;
            }
            return s.Length > 0;
        }

        // ================================================================== god powers

        private static void CheckGodPowers(GameDatabase db, ContentReport r)
        {
            for (int i = 0; i < Counts.GodPowers; i++)
            {
                var p = db.GodPowers[i];
                if (p == null) continue;
                var type = (GodPowerType)i;
                string w = "God power " + type;
                if ((int)p.Type != i) r.AddError(w + ": Type does not match its registry slot.");
                Text(r, w, "Name", p.Name, MaxNameLength, false, false);
                Text(r, w, "Description", p.Description, MaxDescriptionLength, true, true);
                if (!Enum.IsDefined(typeof(GodPowerCategory), p.Category)) r.AddError(w + ": Category is not defined.");
                if (!Enum.IsDefined(typeof(AgeId), p.MinWorldAge)) r.AddError(w + ": MinWorldAge is not defined.");
                Num(r, w, "ManaCost", p.ManaCost, 0f, MaxManaCost, true);
                Int(r, w, "CooldownTicks", p.CooldownTicks, 0, MaxCooldownTicks, true);
                Int(r, w, "MinRadius", p.MinRadius, 0, GodCommandValidator.AbsoluteMaxRadius, true);
                if (p.MaxRadius > GodCommandValidator.AbsoluteMaxRadius)
                    r.AddWarning(w + ": MaxRadius " + p.MaxRadius + " exceeds " + GodCommandValidator.AbsoluteMaxRadius + "; larger radii are always rejected.");
                Num(r, w, "AlignmentDelta", p.AlignmentDelta, -1f, 1f, false);

                bool wrathType = type >= GodPowerType.Lightning;
                if ((p.Category == GodPowerCategory.Wrath || wrathType) && !(p.ManaCost > 0f))
                    r.AddError(w + ": wrath powers must cost mana (ManaCost > 0).");
                if (wrathType && p.Category != GodPowerCategory.Wrath) r.AddWarning(w + ": is a wrath power by design but its Category is " + p.Category + ".");
                if (p.UsesRaceParameter && type != GodPowerType.SpawnTribe) r.AddWarning(w + ": UsesRaceParameter is only meaningful for SpawnTribe.");
                if (!p.UsesRaceParameter && type == GodPowerType.SpawnTribe) r.AddWarning(w + ": SpawnTribe should use the race parameter.");
            }
        }

        // ================================================================== economy (reachability, chains, Dawn essentials)

        private static bool AnyProduction(GameDatabase db)
        {
            for (int b = 0; b < Counts.Buildings; b++)
            {
                var d = db.Buildings[b];
                if (d != null && HasPositive(d.OutputsPerWorker)) return true;
            }
            return false;
        }

        private static void CheckEconomy(GameDatabase db, ContentReport r, bool realContent)
        {
            int B = Counts.Buildings, R = Counts.Races, A = Counts.Ages, RES = Counts.Resources;
            var earliest = ComputeEarliestAges(db);

            // Reachability: some race, some age.
            for (int b = 0; b < B; b++)
            {
                if (db.Buildings[b] == null) continue;
                bool reachable = false;
                for (int ri = 0; ri < R && !reachable; ri++) if (earliest[b, ri] != Unavailable) reachable = true;
                if (!reachable) r.AddError("Building " + (BuildingType)b + " is unreachable: no race can build it in any age.");
            }

            // Dwelling in Dawn for every race (independent of production content).
            for (int ri = 0; ri < R; ri++)
            {
                if (db.Races[ri] == null) continue;
                bool dwelling = false;
                for (int b = 0; b < B && !dwelling; b++)
                {
                    var d = db.Buildings[b];
                    if (d != null && d.IsDwelling && earliest[b, ri] == 0) dwelling = true;
                }
                if (!dwelling) r.AddError("Race " + (RaceId)ri + " has no dwelling available in the Dawn age.");
            }

            if (!realContent)
            {
                r.AddWarning("No building produces any resource: production-chain and Dawn-producer checks skipped (placeholder content).");
                return;
            }

            // producible[ri, age, res] (cumulative: any producer unlocked by that age) and current (not obsolete at that age).
            var cumulative = new bool[R, A, RES];
            var current = new bool[R, A, RES];
            for (int ri = 0; ri < R; ri++)
                for (int a = 0; a < A; a++)
                {
                    ComputeProducible(db, earliest, ri, a, false, cumulative);
                    ComputeProducible(db, earliest, ri, a, true, current);
                }

            // Dawn essentials.
            for (int ri = 0; ri < R; ri++)
            {
                if (db.Races[ri] == null) continue;
                for (int k = 0; k < DawnEssentials.Length; k++)
                {
                    int res = (int)DawnEssentials[k];
                    if (!cumulative[ri, 0, res])
                        r.AddError("Race " + (RaceId)ri + " has no Dawn-age producer of " + DawnEssentials[k] + " (needs a staffed building whose inputs are also available in Dawn).");
                }
            }

            // Production-chain closure, aggregated per (consumer, resource).
            var missing = new StringBuilder();
            var obsolete = new StringBuilder();
            for (int b = 0; b < B; b++)
            {
                var d = db.Buildings[b];
                if (d == null || d.InputsPerWorker == null || !d.HasJobs) continue;
                for (int k = 0; k < d.InputsPerWorker.Length; k++)
                {
                    var input = d.InputsPerWorker[k];
                    int res = (int)input.Type;
                    if (!(input.Amount > 0f) || res < 0 || res >= RES) continue;
                    if (IndexOfType(d.InputsPerWorker, input.Type) < k) continue; // duplicate entry: report once
                    missing.Length = 0;
                    obsolete.Length = 0;
                    for (int ri = 0; ri < R; ri++)
                    {
                        int age = earliest[b, ri];
                        if (age == Unavailable) continue;
                        if (!cumulative[ri, age, res]) Append(missing, ((RaceId)ri) + " (" + (AgeId)age + ")");
                        else if (!current[ri, age, res]) Append(obsolete, ((RaceId)ri) + " (" + (AgeId)age + ")");
                    }
                    if (missing.Length > 0)
                        r.AddError("Building " + (BuildingType)b + " consumes " + input.Type + " but nothing available at or before its age produces it for: " + missing + ".");
                    if (obsolete.Length > 0)
                        r.AddWarning("Building " + (BuildingType)b + " consumes " + input.Type + " but only obsolete producers exist for: " + obsolete + ".");
                }
            }
        }

        private static int IndexOfType(ResourceAmount[] arr, ResourceType t)
        {
            for (int i = 0; i < arr.Length; i++) if (arr[i].Type == t && arr[i].Amount > 0f) return i;
            return -1;
        }

        private static void Append(StringBuilder sb, string s) { if (sb.Length > 0) sb.Append(", "); sb.Append(s); }

        /// <summary>earliest[b, race] = first age the race can build b (MinAge or an UnlockBuilding tech), or Unavailable.</summary>
        private static int[,] ComputeEarliestAges(GameDatabase db)
        {
            int B = Counts.Buildings, R = Counts.Races;
            var e = new int[B, R];
            for (int b = 0; b < B; b++)
            {
                var d = db.Buildings[b];
                for (int ri = 0; ri < R; ri++)
                {
                    e[b, ri] = Unavailable;
                    if (d == null || !d.AllowedRaces.Allows((RaceId)ri)) continue;
                    if (d.MinAge <= d.MaxAge && (int)d.MinAge < Counts.Ages) e[b, ri] = (int)d.MinAge;
                }
            }
            var techs = db.Techs;
            for (int t = 0; t < techs.Count; t++)
            {
                var td = techs[t];
                if (td == null || td.Effects == null || (int)td.Age >= Counts.Ages) continue;
                for (int k = 0; k < td.Effects.Length; k++)
                {
                    var fx = td.Effects[k];
                    if (fx.Kind != TechEffectKind.UnlockBuilding) continue;
                    int b = (int)fx.Building;
                    if (b < 0 || b >= B) continue;
                    var d = db.Buildings[b];
                    if (d == null || td.Age > d.MaxAge) continue;
                    for (int ri = 0; ri < R; ri++)
                    {
                        var race = (RaceId)ri;
                        if (!d.AllowedRaces.Allows(race) || !td.AllowedRaces.Allows(race)) continue;
                        if ((int)td.Age < e[b, ri]) e[b, ri] = (int)td.Age;
                    }
                }
            }
            return e;
        }

        /// <summary>Fixed point: resources producible by a race at an age, from staffed buildings whose inputs are producible.</summary>
        private static void ComputeProducible(GameDatabase db, int[,] earliest, int ri, int age, bool excludeObsolete, bool[,,] set)
        {
            int RES = Counts.Resources;
            if (!excludeObsolete && age > 0)
                for (int res = 0; res < RES; res++) set[ri, age, res] = set[ri, age - 1, res];
            bool changed = true;
            int guard = 0;
            while (changed && guard++ < RES + 2)
            {
                changed = false;
                for (int b = 0; b < Counts.Buildings; b++)
                {
                    var d = db.Buildings[b];
                    if (d == null || !d.HasJobs || d.OutputsPerWorker == null) continue;
                    int e = earliest[b, ri];
                    if (e == Unavailable || e > age) continue;
                    if (excludeObsolete && (int)d.MaxAge < age) continue;
                    bool inputsOk = true;
                    if (d.InputsPerWorker != null)
                        for (int k = 0; k < d.InputsPerWorker.Length && inputsOk; k++)
                        {
                            var input = d.InputsPerWorker[k];
                            int res = (int)input.Type;
                            if (input.Amount > 0f && (res >= RES || !set[ri, age, res])) inputsOk = false;
                        }
                    if (!inputsOk) continue;
                    for (int k = 0; k < d.OutputsPerWorker.Length; k++)
                    {
                        var o = d.OutputsPerWorker[k];
                        int res = (int)o.Type;
                        if (o.Amount > 0f && res < RES && !set[ri, age, res]) { set[ri, age, res] = true; changed = true; }
                    }
                }
            }
        }

        // ================================================================== primitive checks

        private static void Text(ContentReport r, string w, string field, string v, int max, bool allowEmpty, bool multiline)
        {
            if (v == null) { r.AddError(w + ": " + field + " is null."); return; }
            if (v.Length > max) { r.AddError(w + ": " + field + " is too long (" + v.Length + " > " + max + " characters)."); return; }
            if (!InputSanitizer.IsSafeDisplayText(v, multiline)) r.AddError(w + ": " + field + " contains control, bidi or invisible characters.");
            else if (!allowEmpty && v.Trim().Length == 0) r.AddWarning(w + ": " + field + " is empty.");
        }

        private static void Num(ContentReport r, string w, string field, float v, float min, float max, bool errorIfOutOfRange)
        {
            if (float.IsNaN(v) || float.IsInfinity(v)) { r.AddError(w + ": " + field + " is not a finite number."); return; }
            if (v >= min && v <= max) return;
            string msg = w + ": " + field + " = " + Fmt(v) + " is outside [" + Fmt(min) + ", " + Fmt(max) + "].";
            if (errorIfOutOfRange) r.AddError(msg); else r.AddWarning(msg);
        }

        private static void Int(ContentReport r, string w, string field, int v, int min, int max, bool errorIfOutOfRange)
        {
            if (v >= min && v <= max) return;
            string msg = w + ": " + field + " = " + v + " is outside [" + min + ", " + max + "].";
            if (errorIfOutOfRange) r.AddError(msg); else r.AddWarning(msg);
        }

        private static void Arr(ContentReport r, string w, string field, float[] arr, float min, float max)
        {
            if (arr == null) return; // length/null reported by structural validation
            for (int i = 0; i < arr.Length; i++) Num(r, w, field + "[" + i + "]", arr[i], min, max, true);
        }

        private static string Fmt(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        private static int AllFlagBits(Type flagsEnum)
        {
            int bits = 0;
            foreach (var v in Enum.GetValues(flagsEnum)) bits |= Convert.ToInt32(v, CultureInfo.InvariantCulture);
            return bits;
        }
    }
}
