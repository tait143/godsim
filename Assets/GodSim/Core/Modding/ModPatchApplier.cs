using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace GodSim.Core.Modding
{
    /// <summary>
    /// Applies DATA-ONLY mod patches to an unfrozen <see cref="GameDatabase"/>.
    /// <para>Security model: every patchable field is listed explicitly in hand-written switch statements
    /// (no reflection, no dynamic binding, no code). Every number is range-checked against
    /// <see cref="ModHardCaps"/>, every string is sanitized by <see cref="ModText"/>. Known but
    /// non-whitelisted fields (e.g. House housingMin, which protects the 3-5 residents rule) are always errors.</para>
    /// <para>Transactional: each pack is fully validated against shadow copies first, including the
    /// cross-field invariants <see cref="GameDatabase.Freeze"/> enforces; only if there are no errors are the
    /// staged values copied onto the live definitions and new techs registered. On any error nothing changes.</para>
    /// </summary>
    public static class ModPatchApplier
    {
        public const string SectionBuildings = "buildings";
        public const string SectionRaces = "races";
        public const string SectionTechs = "techs";
        public const string SectionAges = "ages";
        public const string SectionGodPowers = "godPowers";
        public const string SectionNewTechs = "newTechs";
        public const string KeyFormatVersion = "formatVersion";

        // ----------------------------------------------------------------- whitelists (JSON keys)
        internal static readonly string[] Sections = { SectionBuildings, SectionRaces, SectionTechs, SectionAges, SectionGodPowers, SectionNewTechs, KeyFormatVersion };

        internal static readonly string[] BuildingFields =
            { "name", "description", "cost", "buildWork", "jobSlots", "inputsPerWorker", "outputsPerWorker", "services", "storageCapacity", "upkeepGold", "desirability", "visualHeight", "minPopulation" };
        internal static readonly string[] BuildingProtected =
            { "type", "category", "shape", "minAge", "maxAge", "allowedRaces", "minTier", "maxPerSettlement", "uniquePerCivilization", "isWonder", "width", "height", "maxCondition", "housingMin", "housingMax", "job", "serviceRadius", "placement", "tintColor", "isDwelling", "hasJobs" };

        internal static readonly string[] RaceFields =
            { "description", "lifeExpectancy", "fertilityPerYear", "baseHealth", "diseaseResistance", "buildSpeed", "researchRate", "faithRate", "militaryStrength", "tradeAptitude", "moveSpeed", "productionMultiplier",
              "aggression", "expansionism", "piety", "industriousness", "curiosity", "diplomacy", "xenophobia", "mercantilism", "names", "bannerColor", "skinColor", "wallColor", "roofColor", "trimColor", "accentColor" };
        internal static readonly string[] RaceProtected =
            { "id", "name", "pluralName", "adjective", "biomeHabitability", "adulthoodAge", "elderAge", "maxChildren", "foodPerTick", "raceAffinity", "minSettlementSpacing", "compactness", "roadAffinity", "architecture", "uniqueBuildings", "favoredBuildings" };
        internal static readonly string[] NameListFields =
            { "personPrefixes", "maleSuffixes", "femaleSuffixes", "familyNames", "settlementPrefixes", "settlementSuffixes", "civilizationPatterns" };

        internal static readonly string[] TechFields = { "name", "description", "cost" };
        internal static readonly string[] TechProtected = { "id", "key", "age", "prerequisites", "prerequisiteKeys", "effects", "allowedRaces" };
        internal static readonly string[] NewTechFields = { "key", "name", "description", "age", "cost", "prerequisites", "effects", "allowedRaces" };
        internal static readonly string[] EffectFields = { "kind", "building", "resource", "service", "value" };

        internal static readonly string[] AgeFields = { "description", "researchThreshold", "minPopulation" };
        internal static readonly string[] AgeProtected =
            { "id", "name", "requiredTechsFromPreviousAge", "minSettlements", "productionBonus", "researchBonus", "lifespanBonusYears", "healthBonus", "baseSoldierStrength", "themeColor", "architectureNotes" };

        internal static readonly string[] GodPowerFields = { "description", "manaCost", "cooldownTicks", "maxRadius" };
        internal static readonly string[] GodPowerProtected =
            { "type", "name", "category", "minRadius", "requiresLandTarget", "requiresSettlementInRange", "alignmentDelta", "usesRaceParameter", "minWorldAge" };

        private static readonly string[] NoFields = new string[0];

        // ----------------------------------------------------------------- public API
        /// <summary>
        /// Validates and (only if fully valid) applies one pack. Throws <see cref="InvalidOperationException"/> if
        /// the database is frozen (mods must be applied before <see cref="GameDatabase.Freeze"/>). Content problems
        /// never throw: they are reported in the returned report, and the database is then left untouched.
        /// </summary>
        public static ModApplyReport Apply(GameDatabase db, ModPack pack, ModApplyOptions options)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (pack == null) throw new ArgumentNullException(nameof(pack));
            if (db.IsFrozen) throw new InvalidOperationException("GameDatabase is frozen; mods must be applied before Freeze().");
            var report = new ModApplyReport(pack.Manifest.Id);
            var session = new Session(db, pack, options ?? new ModApplyOptions(), report);
            try
            {
                session.Stage();
            }
            catch (Exception e)
            {
                // Defensive: a bug while staging must not leave a half-applied pack (nothing was committed yet).
                report.AddError("", null, "", "Internal error while validating the pack (" + e.GetType().Name + "): " + SafeJson.Printable(e.Message));
            }
            if (report.Errors.Count == 0)
            {
                session.Commit();
                report.Applied = true;
            }
            else
            {
                report.Changes.Clear();
                report.AddedTechs.Clear();
            }
            return report;
        }

        /// <summary>
        /// Applies packs in the given order (use <see cref="ModLoader"/>'s order). A pack whose required
        /// dependency was not applied earlier in the list is skipped with an error. When a later pack changes a
        /// field that an earlier pack changed, the later value wins and a conflict warning is added.
        /// </summary>
        public static List<ModApplyReport> ApplyAll(GameDatabase db, IReadOnlyList<ModPack> packs, ModApplyOptions options)
        {
            if (db == null) throw new ArgumentNullException(nameof(db));
            if (db.IsFrozen) throw new InvalidOperationException("GameDatabase is frozen; mods must be applied before Freeze().");
            var reports = new List<ModApplyReport>();
            if (packs == null) return reports;
            var applied = new Dictionary<string, SemVersion>(StringComparer.Ordinal);
            var owners = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var pack in packs)
            {
                if (pack == null) continue;
                string missing = null;
                foreach (var dep in pack.Manifest.Dependencies ?? new List<ModDependency>())
                {
                    if (dep == null || dep.Optional) continue;
                    if (dep.Versions == null) { missing = dep.Id; break; }
                    if (!applied.TryGetValue(dep.Id, out var v) || !dep.Versions.Contains(v)) { missing = dep.Id + " " + dep.Versions; break; }
                }
                if (missing != null)
                {
                    var r = new ModApplyReport(pack.Manifest.Id);
                    r.AddError("", null, "", "Required dependency '" + missing + "' was not applied before this pack; pack skipped.");
                    reports.Add(r);
                    continue;
                }
                var rep = Apply(db, pack, options);
                reports.Add(rep);
                if (!rep.Applied) continue;
                applied[pack.Manifest.Id] = pack.Manifest.Version;
                foreach (var c in rep.Changes)
                {
                    if (owners.TryGetValue(c.Path, out var prev) && !string.Equals(prev, pack.Manifest.Id, StringComparison.Ordinal))
                        rep.AddWarning(new ModDiagnostic { ModId = rep.ModId, Source = c.Source, Line = c.Line, Column = c.Column, Path = c.Path, Message = "Conflict: also changed by mod '" + prev + "'; this later mod wins." });
                    owners[c.Path] = pack.Manifest.Id;
                }
            }
            return reports;
        }

        /// <summary>
        /// Logical field paths a pack targets (without validating), e.g. "buildings.House.buildWork",
        /// "races.Elf.names.personPrefixes", "newTechs.mymod.alchemy". Used for conflict detection.
        /// </summary>
        public static List<string> CollectTargetPaths(ModPack pack)
        {
            var paths = new List<string>();
            if (pack == null) return paths;
            foreach (var doc in pack.Documents)
            {
                var root = doc.Root;
                if (root == null || !root.IsObject) continue;
                for (int i = 0; i < root.Count; i++)
                {
                    string section = root.KeyAt(i);
                    var sec = root.Item(i);
                    if (section == SectionNewTechs)
                    {
                        foreach (var t in sec.Items)
                        {
                            var k = t.Get("key");
                            if (k != null && k.IsString) paths.Add(SectionNewTechs + "." + k.StringValue);
                        }
                        continue;
                    }
                    if (section != SectionBuildings && section != SectionRaces && section != SectionTechs && section != SectionAges && section != SectionGodPowers) continue;
                    if (!sec.IsObject) continue;
                    for (int e = 0; e < sec.Count; e++)
                    {
                        var entry = sec.Item(e);
                        if (!entry.IsObject) continue;
                        string basePath = section + "." + Seg(sec.KeyAt(e));
                        for (int f = 0; f < entry.Count; f++)
                        {
                            string field = entry.KeyAt(f);
                            var fv = entry.Item(f);
                            if (section == SectionRaces && (field == "productionMultiplier" || field == "names") && fv.IsObject)
                            {
                                for (int s = 0; s < fv.Count; s++) paths.Add(basePath + "." + Seg(field) + "." + Seg(fv.KeyAt(s)));
                            }
                            else paths.Add(basePath + "." + Seg(field));
                        }
                    }
                }
            }
            return paths;
        }

        /// <summary>New tech keys must be "&lt;modId&gt;.&lt;suffix&gt;" with suffix [a-z0-9_-]{1,32} (no further dots, so the owner is unambiguous).</summary>
        public static bool IsValidNewTechKey(string modId, string key)
        {
            if (modId == null || key == null) return false;
            if (key.Length <= modId.Length + 1 || !key.StartsWith(modId + ".", StringComparison.Ordinal)) return false;
            int suffixLen = key.Length - modId.Length - 1;
            if (suffixLen > ModHardCaps.MaxTechKeySuffixLength) return false;
            for (int i = modId.Length + 1; i < key.Length; i++)
            {
                char c = key[i];
                bool ok = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return true;
        }

        // =================================================================================== session
        private sealed class Session
        {
            private readonly GameDatabase _db;
            private readonly ModPack _pack;
            private readonly ModApplyOptions _opt;
            private readonly ModApplyReport _r;
            private readonly string _modId;
            private string _src = "";

            private readonly BuildingDefinition[] _buildings = new BuildingDefinition[Counts.Buildings];
            private readonly RaceDefinition[] _races = new RaceDefinition[Counts.Races];
            private readonly bool[] _namesTouched = new bool[Counts.Races];
            private readonly bool[] _archTouched = new bool[Counts.Races];
            private readonly AgeDefinition[] _ages = new AgeDefinition[Counts.Ages];
            private readonly GodPowerDefinition[] _powers = new GodPowerDefinition[Counts.GodPowers];
            private readonly TechDefinition[] _techs;
            private readonly List<TechDefinition> _newTechs = new List<TechDefinition>();
            private readonly Dictionary<string, TechDefinition> _newTechByKey = new Dictionary<string, TechDefinition>(StringComparer.Ordinal);
            private readonly List<ModChange> _changes = new List<ModChange>();          // superseded entries become null
            private readonly Dictionary<string, int> _byPath = new Dictionary<string, int>(StringComparer.Ordinal);

            public Session(GameDatabase db, ModPack pack, ModApplyOptions opt, ModApplyReport report)
            {
                _db = db; _pack = pack; _opt = opt; _r = report;
                _modId = pack.Manifest.Id ?? "";
                _techs = new TechDefinition[db.Techs.Count];
            }

            // ------------------------------------------------------------- staging
            public void Stage()
            {
                if (!ModManifest.IsValidId(_modId)) Error(null, "", "Invalid mod id '" + SafeJson.Printable(_modId) + "'.");
                if (_opt.GameVersion != null && _pack.Manifest.GameVersionRange != null && !_pack.Manifest.GameVersionRange.Contains(_opt.GameVersion))
                    Error(null, "", "Mod requires game version " + _pack.Manifest.GameVersionRange + " but the game is " + _opt.GameVersion + ".");
                foreach (var doc in _pack.Documents)
                {
                    _src = doc.SourceName;
                    var root = doc.Root;
                    if (root == null || !root.IsObject) { Error(root, "", "A patch document must be a JSON object."); continue; }
                    for (int i = 0; i < root.Count; i++)
                    {
                        string key = root.KeyAt(i);
                        var v = root.Item(i);
                        switch (key)
                        {
                            case SectionBuildings: ForEachEntry(v, key, StageBuilding); break;
                            case SectionRaces: ForEachEntry(v, key, StageRace); break;
                            case SectionTechs: ForEachEntry(v, key, StageTech); break;
                            case SectionAges: ForEachEntry(v, key, StageAge); break;
                            case SectionGodPowers: ForEachEntry(v, key, StagePower); break;
                            case SectionNewTechs: StageNewTechs(v); break;
                            case KeyFormatVersion:
                                if (!v.IsNumber || v.NumberValue != ModdingInfo.FormatVersion)
                                    Error(v, key, "Unsupported formatVersion; this game reads format " + ModdingInfo.FormatVersion + ".");
                                break;
                            default: Unknown(v, Seg(key), key, Sections, NoFields); break;
                        }
                    }
                }
                _src = "";
                CheckInvariants();
                if (_r.Errors.Count == 0) foreach (var c in _changes) if (c != null) _r.Changes.Add(c);
            }

            private void ForEachEntry(JsonValue section, string name, Action<string, JsonValue, string> stage)
            {
                if (!section.IsObject) { Error(section, name, "Section must be an object keyed by definition name."); return; }
                for (int i = 0; i < section.Count; i++)
                {
                    string key = section.KeyAt(i);
                    var entry = section.Item(i);
                    string path = name + "." + Seg(key);
                    if (!entry.IsObject) { Error(entry, path, "Entry must be an object of field overrides."); continue; }
                    stage(key, entry, path);
                }
            }

            private void StageBuilding(string key, JsonValue entry, string basePath)
            {
                if (!ModEnumNames.TryGet(ModEnumNames.Buildings, key, out var type)) { Error(entry, basePath, "Unknown building type (names are case-sensitive, e.g. \"House\")."); return; }
                if (!_db.TryGetBuilding(type, out var def)) { Error(entry, basePath, "Building is not registered in the database."); return; }
                var s = _buildings[(int)type];
                if (s == null)
                {
                    s = _buildings[(int)type] = new BuildingDefinition
                    {
                        Type = def.Type, Name = def.Name, Description = def.Description, Cost = def.Cost, BuildWork = def.BuildWork,
                        JobSlots = def.JobSlots, InputsPerWorker = def.InputsPerWorker, OutputsPerWorker = def.OutputsPerWorker,
                        Services = def.Services, StorageCapacity = def.StorageCapacity, UpkeepGold = def.UpkeepGold,
                        Desirability = def.Desirability, VisualHeight = def.VisualHeight, MinPopulation = def.MinPopulation,
                        Job = def.Job, HousingMin = def.HousingMin, HousingMax = def.HousingMax
                    };
                }
                for (int i = 0; i < entry.Count; i++)
                {
                    string f = entry.KeyAt(i);
                    var v = entry.Item(i);
                    string path = basePath + "." + Seg(f);
                    switch (f)
                    {
                        case "name": if (SetText(v, path, ModHardCaps.MaxNameLength, false, true, def.Name, out var name)) s.Name = name; break;
                        case "description": if (SetText(v, path, ModHardCaps.MaxDescriptionLength, true, false, def.Description, out var desc)) s.Description = desc; break;
                        case "cost": if (SetResources(v, path, 0, ModHardCaps.MaxCost, def.Cost, out var cost)) s.Cost = cost; break;
                        case "buildWork": if (SetFloat(v, path, ModHardCaps.MinBuildWork, ModHardCaps.MaxBuildWork, def.BuildWork, out var bw)) s.BuildWork = bw; break;
                        case "jobSlots": if (SetInt(v, path, 0, ModHardCaps.MaxJobSlots, def.JobSlots, out var js)) s.JobSlots = js; break;
                        case "inputsPerWorker": if (SetResources(v, path, 0, ModHardCaps.MaxRate, def.InputsPerWorker, out var inp)) s.InputsPerWorker = inp; break;
                        case "outputsPerWorker": if (SetResources(v, path, 0, ModHardCaps.MaxRate, def.OutputsPerWorker, out var outp)) s.OutputsPerWorker = outp; break;
                        case "services": if (SetServices(v, path, 0, ModHardCaps.MaxRate, def.Services, out var svc)) s.Services = svc; break;
                        case "storageCapacity": if (SetFloat(v, path, 0, ModHardCaps.MaxStorage, def.StorageCapacity, out var st)) s.StorageCapacity = st; break;
                        case "upkeepGold": if (SetFloat(v, path, 0, ModHardCaps.MaxRate, def.UpkeepGold, out var up)) s.UpkeepGold = up; break;
                        case "desirability": if (SetFloat(v, path, -1, 1, def.Desirability, out var ds)) s.Desirability = ds; break;
                        case "visualHeight": if (SetFloat(v, path, ModHardCaps.MinVisualHeight, ModHardCaps.MaxVisualHeight, def.VisualHeight, out var vh)) s.VisualHeight = vh; break;
                        case "minPopulation": if (SetInt(v, path, 0, ModHardCaps.MaxMinPopulation, def.MinPopulation, out var mp)) s.MinPopulation = mp; break;
                        default: Unknown(v, path, f, BuildingFields, BuildingProtected); break;
                    }
                }
            }

            private void StageRace(string key, JsonValue entry, string basePath)
            {
                if (!ModEnumNames.TryGet(ModEnumNames.Races, key, out var id)) { Error(entry, basePath, "Unknown race (one of Human, Elf, Dwarf, Orc, Saurian)."); return; }
                var def = _db.Races[(int)id];
                if (def == null) { Error(entry, basePath, "Race is not registered in the database."); return; }
                int ri = (int)id;
                var s = _races[ri];
                if (s == null)
                {
                    var n = def.Names ?? new NameParts();
                    var a = def.Architecture ?? new ArchitectureStyle();
                    s = _races[ri] = new RaceDefinition
                    {
                        Id = def.Id, Description = def.Description, AdulthoodAge = def.AdulthoodAge, ElderAge = def.ElderAge,
                        LifeExpectancy = def.LifeExpectancy, FertilityPerYear = def.FertilityPerYear,
                        BaseHealth = def.BaseHealth, DiseaseResistance = def.DiseaseResistance, BuildSpeed = def.BuildSpeed,
                        ResearchRate = def.ResearchRate, FaithRate = def.FaithRate, MilitaryStrength = def.MilitaryStrength,
                        TradeAptitude = def.TradeAptitude, MoveSpeed = def.MoveSpeed, ProductionMultiplier = def.ProductionMultiplier,
                        Aggression = def.Aggression, Expansionism = def.Expansionism, Piety = def.Piety, Industriousness = def.Industriousness,
                        Curiosity = def.Curiosity, Diplomacy = def.Diplomacy, Xenophobia = def.Xenophobia, Mercantilism = def.Mercantilism,
                        BannerColor = def.BannerColor, SkinColor = def.SkinColor,
                        Names = new NameParts
                        {
                            PersonPrefixes = n.PersonPrefixes, MaleSuffixes = n.MaleSuffixes, FemaleSuffixes = n.FemaleSuffixes,
                            FamilyNames = n.FamilyNames, SettlementPrefixes = n.SettlementPrefixes, SettlementSuffixes = n.SettlementSuffixes,
                            CivilizationPatterns = n.CivilizationPatterns
                        },
                        Architecture = new ArchitectureStyle { WallColor = a.WallColor, RoofColor = a.RoofColor, TrimColor = a.TrimColor, AccentColor = a.AccentColor }
                    };
                }
                var arch = def.Architecture ?? new ArchitectureStyle();
                for (int i = 0; i < entry.Count; i++)
                {
                    string f = entry.KeyAt(i);
                    var v = entry.Item(i);
                    string path = basePath + "." + Seg(f);
                    float x;
                    uint col;
                    const double A0 = ModHardCaps.MinAptitude, A1 = ModHardCaps.MaxRate;
                    switch (f)
                    {
                        case "description": if (SetText(v, path, ModHardCaps.MaxDescriptionLength, true, false, def.Description, out var desc)) s.Description = desc; break;
                        case "lifeExpectancy": if (SetFloat(v, path, ModHardCaps.MinLifeYears, ModHardCaps.MaxLifeYears, def.LifeExpectancy, out x)) s.LifeExpectancy = x; break;
                        case "fertilityPerYear": if (SetFloat(v, path, 0, 1, def.FertilityPerYear, out x)) s.FertilityPerYear = x; break;
                        case "baseHealth": if (SetFloat(v, path, A0, A1, def.BaseHealth, out x)) s.BaseHealth = x; break;
                        case "diseaseResistance": if (SetFloat(v, path, A0, A1, def.DiseaseResistance, out x)) s.DiseaseResistance = x; break;
                        case "buildSpeed": if (SetFloat(v, path, A0, A1, def.BuildSpeed, out x)) s.BuildSpeed = x; break;
                        case "researchRate": if (SetFloat(v, path, A0, A1, def.ResearchRate, out x)) s.ResearchRate = x; break;
                        case "faithRate": if (SetFloat(v, path, A0, A1, def.FaithRate, out x)) s.FaithRate = x; break;
                        case "militaryStrength": if (SetFloat(v, path, A0, A1, def.MilitaryStrength, out x)) s.MilitaryStrength = x; break;
                        case "tradeAptitude": if (SetFloat(v, path, A0, A1, def.TradeAptitude, out x)) s.TradeAptitude = x; break;
                        case "moveSpeed": if (SetFloat(v, path, A0, A1, def.MoveSpeed, out x)) s.MoveSpeed = x; break;
                        case "productionMultiplier": StageProductionMultiplier(def, s, v, path); break;
                        case "aggression": if (SetFloat(v, path, 0, 1, def.Aggression, out x)) s.Aggression = x; break;
                        case "expansionism": if (SetFloat(v, path, 0, 1, def.Expansionism, out x)) s.Expansionism = x; break;
                        case "piety": if (SetFloat(v, path, 0, 1, def.Piety, out x)) s.Piety = x; break;
                        case "industriousness": if (SetFloat(v, path, 0, 1, def.Industriousness, out x)) s.Industriousness = x; break;
                        case "curiosity": if (SetFloat(v, path, 0, 1, def.Curiosity, out x)) s.Curiosity = x; break;
                        case "diplomacy": if (SetFloat(v, path, 0, 1, def.Diplomacy, out x)) s.Diplomacy = x; break;
                        case "xenophobia": if (SetFloat(v, path, 0, 1, def.Xenophobia, out x)) s.Xenophobia = x; break;
                        case "mercantilism": if (SetFloat(v, path, 0, 1, def.Mercantilism, out x)) s.Mercantilism = x; break;
                        case "names": StageNames(def, s, v, path, ri); break;
                        case "bannerColor": if (SetColor(v, path, def.BannerColor, out col)) s.BannerColor = col; break;
                        case "skinColor": if (SetColor(v, path, def.SkinColor, out col)) s.SkinColor = col; break;
                        case "wallColor": if (SetColor(v, path, arch.WallColor, out col)) { s.Architecture.WallColor = col; _archTouched[ri] = true; } break;
                        case "roofColor": if (SetColor(v, path, arch.RoofColor, out col)) { s.Architecture.RoofColor = col; _archTouched[ri] = true; } break;
                        case "trimColor": if (SetColor(v, path, arch.TrimColor, out col)) { s.Architecture.TrimColor = col; _archTouched[ri] = true; } break;
                        case "accentColor": if (SetColor(v, path, arch.AccentColor, out col)) { s.Architecture.AccentColor = col; _archTouched[ri] = true; } break;
                        default: Unknown(v, path, f, RaceFields, RaceProtected); break;
                    }
                }
            }

            private void StageProductionMultiplier(RaceDefinition def, RaceDefinition s, JsonValue v, string path)
            {
                if (!v.IsObject) { Error(v, path, "Expected an object mapping resource names to multipliers, e.g. {\"Wood\": 1.2}."); return; }
                for (int i = 0; i < v.Count; i++)
                {
                    string res = v.KeyAt(i);
                    var rv = v.Item(i);
                    string p = path + "." + Seg(res);
                    if (!ModEnumNames.TryGet(ModEnumNames.Resources, res, out var rt)) { Error(rv, p, "Unknown resource type."); continue; }
                    int ix = (int)rt;
                    float old = def.ProductionMultiplier != null && ix < def.ProductionMultiplier.Length ? def.ProductionMultiplier[ix] : 1f;
                    if (!SetFloat(rv, p, ModHardCaps.MinAptitude, ModHardCaps.MaxRate, old, out var x)) continue;
                    // copy-on-write: never mutate an array the live definition (or a shared static) still references
                    if (ReferenceEquals(s.ProductionMultiplier, def.ProductionMultiplier) || s.ProductionMultiplier == null || s.ProductionMultiplier.Length != Counts.Resources)
                    {
                        var copy = RaceDefinition.Fill(Counts.Resources, 1f);
                        var src = s.ProductionMultiplier;
                        if (src != null) for (int k = 0; k < src.Length && k < copy.Length; k++) copy[k] = MathUtil.Sanitize(src[k], 1f);
                        s.ProductionMultiplier = copy;
                    }
                    s.ProductionMultiplier[ix] = x;
                }
            }

            private void StageNames(RaceDefinition def, RaceDefinition s, JsonValue v, string path, int ri)
            {
                if (!v.IsObject) { Error(v, path, "Expected an object of name lists, e.g. {\"personPrefixes\": [\"Ae\", \"Ela\"]}."); return; }
                var n = def.Names ?? new NameParts();
                for (int i = 0; i < v.Count; i++)
                {
                    string f = v.KeyAt(i);
                    var lv = v.Item(i);
                    string p = path + "." + Seg(f);
                    string[] list;
                    switch (f)
                    {
                        case "personPrefixes": if (SetNameList(lv, p, ModHardCaps.MaxSyllableLength, false, n.PersonPrefixes, out list)) { s.Names.PersonPrefixes = list; _namesTouched[ri] = true; } break;
                        case "maleSuffixes": if (SetNameList(lv, p, ModHardCaps.MaxSyllableLength, false, n.MaleSuffixes, out list)) { s.Names.MaleSuffixes = list; _namesTouched[ri] = true; } break;
                        case "femaleSuffixes": if (SetNameList(lv, p, ModHardCaps.MaxSyllableLength, false, n.FemaleSuffixes, out list)) { s.Names.FemaleSuffixes = list; _namesTouched[ri] = true; } break;
                        case "familyNames": if (SetNameList(lv, p, ModHardCaps.MaxFamilyNameLength, false, n.FamilyNames, out list)) { s.Names.FamilyNames = list; _namesTouched[ri] = true; } break;
                        case "settlementPrefixes": if (SetNameList(lv, p, ModHardCaps.MaxSyllableLength, false, n.SettlementPrefixes, out list)) { s.Names.SettlementPrefixes = list; _namesTouched[ri] = true; } break;
                        case "settlementSuffixes": if (SetNameList(lv, p, ModHardCaps.MaxSyllableLength, false, n.SettlementSuffixes, out list)) { s.Names.SettlementSuffixes = list; _namesTouched[ri] = true; } break;
                        case "civilizationPatterns": if (SetNameList(lv, p, ModHardCaps.MaxPatternLength, true, n.CivilizationPatterns, out list)) { s.Names.CivilizationPatterns = list; _namesTouched[ri] = true; } break;
                        default: Unknown(lv, p, f, NameListFields, NoFields); break;
                    }
                }
            }

            private void StageTech(string key, JsonValue entry, string basePath)
            {
                var def = _db.TechByKey(key);
                if (def == null)
                {
                    Error(entry, basePath, _newTechByKey.ContainsKey(key ?? "")
                        ? "Techs added by this pack are configured in \"newTechs\", not patched in \"techs\"."
                        : "Unknown tech key.");
                    return;
                }
                if (def.Id < 0 || def.Id >= _techs.Length) { Error(entry, basePath, "Tech is not registered in the database."); return; }
                var s = _techs[def.Id];
                if (s == null) s = _techs[def.Id] = new TechDefinition { Key = def.Key, Name = def.Name, Description = def.Description, Cost = def.Cost, Age = def.Age };
                for (int i = 0; i < entry.Count; i++)
                {
                    string f = entry.KeyAt(i);
                    var v = entry.Item(i);
                    string path = basePath + "." + Seg(f);
                    switch (f)
                    {
                        case "name": if (SetText(v, path, ModHardCaps.MaxNameLength, false, true, def.Name, out var name)) s.Name = name; break;
                        case "description": if (SetText(v, path, ModHardCaps.MaxDescriptionLength, true, false, def.Description, out var desc)) s.Description = desc; break;
                        case "cost": if (SetFloat(v, path, ModHardCaps.MinTechCost, ModHardCaps.MaxCost, def.Cost, out var cost)) s.Cost = cost; break;
                        default: Unknown(v, path, f, TechFields, TechProtected); break;
                    }
                }
            }

            private void StageAge(string key, JsonValue entry, string basePath)
            {
                if (!ModEnumNames.TryGet(ModEnumNames.Ages, key, out var id)) { Error(entry, basePath, "Unknown age (Dawn, Bronze, Iron, Classical, Medieval, Renaissance, Industrial, Modern, Information, Ascension)."); return; }
                var def = _db.Ages[(int)id];
                if (def == null) { Error(entry, basePath, "Age is not registered in the database."); return; }
                var s = _ages[(int)id];
                if (s == null) s = _ages[(int)id] = new AgeDefinition { Id = def.Id, Description = def.Description, ResearchThreshold = def.ResearchThreshold, MinPopulation = def.MinPopulation };
                for (int i = 0; i < entry.Count; i++)
                {
                    string f = entry.KeyAt(i);
                    var v = entry.Item(i);
                    string path = basePath + "." + Seg(f);
                    switch (f)
                    {
                        case "description": if (SetText(v, path, ModHardCaps.MaxDescriptionLength, true, false, def.Description, out var desc)) s.Description = desc; break;
                        case "researchThreshold": if (SetFloat(v, path, 0, ModHardCaps.MaxResearchThreshold, def.ResearchThreshold, out var th)) s.ResearchThreshold = th; break;
                        case "minPopulation": if (SetInt(v, path, 0, ModHardCaps.MaxMinPopulation, def.MinPopulation, out var mp)) s.MinPopulation = mp; break;
                        default: Unknown(v, path, f, AgeFields, AgeProtected); break;
                    }
                }
            }

            private void StagePower(string key, JsonValue entry, string basePath)
            {
                if (!ModEnumNames.TryGet(ModEnumNames.Powers, key, out var type)) { Error(entry, basePath, "Unknown god power type."); return; }
                var def = _db.GodPowers[(int)type];
                if (def == null) { Error(entry, basePath, "God power is not registered in the database."); return; }
                var s = _powers[(int)type];
                if (s == null) s = _powers[(int)type] = new GodPowerDefinition { Type = def.Type, Description = def.Description, ManaCost = def.ManaCost, CooldownTicks = def.CooldownTicks, MinRadius = def.MinRadius, MaxRadius = def.MaxRadius };
                for (int i = 0; i < entry.Count; i++)
                {
                    string f = entry.KeyAt(i);
                    var v = entry.Item(i);
                    string path = basePath + "." + Seg(f);
                    switch (f)
                    {
                        case "description": if (SetText(v, path, ModHardCaps.MaxDescriptionLength, true, false, def.Description, out var desc)) s.Description = desc; break;
                        case "manaCost": if (SetFloat(v, path, 0, ModHardCaps.MaxManaCost, def.ManaCost, out var mc)) s.ManaCost = mc; break;
                        case "cooldownTicks": if (SetInt(v, path, 0, ModHardCaps.MaxCooldownTicks, def.CooldownTicks, out var cd)) s.CooldownTicks = cd; break;
                        case "maxRadius": if (SetInt(v, path, 0, ModHardCaps.MaxRadius, def.MaxRadius, out var mr)) s.MaxRadius = mr; break;
                        default: Unknown(v, path, f, GodPowerFields, GodPowerProtected); break;
                    }
                }
            }

            // ------------------------------------------------------------- new techs
            private void StageNewTechs(JsonValue v)
            {
                const string path = SectionNewTechs;
                if (!_opt.AllowNewTechs) { Error(v, path, "Adding new techs is disabled by the game's mod options."); return; }
                if (!v.IsArray) { Error(v, path, "\"newTechs\" must be an array of tech objects."); return; }
                if (_newTechs.Count + v.Count > ModHardCaps.MaxNewTechsPerPack) { Error(v, path, "At most " + ModHardCaps.MaxNewTechsPerPack + " new techs per pack."); return; }
                for (int i = 0; i < v.Count; i++) StageNewTech(v.Item(i), path + "[" + i + "]");
            }

            private void StageNewTech(JsonValue t, string indexPath)
            {
                if (!t.IsObject) { Error(t, indexPath, "Each new tech must be an object."); return; }
                var keyV = t.Get("key");
                if (keyV == null || !keyV.IsString) { Error(t, indexPath, "New tech requires a string \"key\"."); return; }
                string key = keyV.StringValue;
                if (!IsValidNewTechKey(_modId, key))
                {
                    Error(keyV, indexPath, "New tech key must be \"" + _modId + ".<name>\" where <name> is 1-" + ModHardCaps.MaxTechKeySuffixLength + " of [a-z0-9_-] (got \"" + SafeJson.Printable(key) + "\").");
                    return;
                }
                string path = SectionNewTechs + "." + key;
                if (_db.TechIdOf(key) >= 0 || _newTechByKey.ContainsKey(key)) { Error(keyV, path, "A tech with this key already exists."); return; }

                int errorsBefore = _r.Errors.Count;
                var def = new TechDefinition { Key = key };
                bool hasName = false, hasAge = false, hasCost = false;
                JsonValue prereqs = null;
                var effects = new List<TechEffect>();
                for (int i = 0; i < t.Count; i++)
                {
                    string f = t.KeyAt(i);
                    var v = t.Item(i);
                    string p = path + "." + Seg(f);
                    switch (f)
                    {
                        case "key": break;
                        case "name":
                            if (ReadText(v, p, ModHardCaps.MaxNameLength, false, true, out var name)) { def.Name = name; hasName = true; }
                            break;
                        case "description":
                            if (ReadText(v, p, ModHardCaps.MaxDescriptionLength, true, false, out var desc)) def.Description = desc;
                            break;
                        case "age":
                            if (v.IsString && ModEnumNames.TryGet(ModEnumNames.Ages, v.StringValue, out var age)) { def.Age = age; hasAge = true; }
                            else Error(v, p, "Expected an age name such as \"Bronze\".");
                            break;
                        case "cost":
                            if (ReadFloat(v, p, ModHardCaps.MinTechCost, ModHardCaps.MaxCost, out var cost)) { def.Cost = cost; hasCost = true; }
                            break;
                        case "prerequisites": prereqs = v; break;
                        case "effects": ReadEffects(v, p, effects); break;
                        case "allowedRaces":
                            if (ReadRaceMask(v, p, out var mask)) def.AllowedRaces = mask;
                            break;
                        default: Unknown(v, p, f, NewTechFields, new[] { "id", "prerequisiteKeys" }); break;
                    }
                }
                if (!hasName && t.Get("name") == null) Error(t, path, "New tech requires \"name\".");
                if (!hasAge && t.Get("age") == null) Error(t, path, "New tech requires \"age\".");
                if (!hasCost && t.Get("cost") == null) Error(t, path, "New tech requires \"cost\".");
                def.Effects = effects.ToArray();
                def.PrerequisiteKeys = hasAge ? ReadPrerequisites(prereqs, path + ".prerequisites", def) : new string[0];
                if (_r.Errors.Count != errorsBefore) return;
                _newTechs.Add(def);
                _newTechByKey[key] = def;
                Record(path, t, "(none)", "new tech \"" + def.Name + "\" (" + def.Age + ", cost " + Fmt(def.Cost) + ")");
            }

            private string[] ReadPrerequisites(JsonValue v, string path, TechDefinition def)
            {
                var keys = new List<string>();
                if (v == null) return keys.ToArray();
                if (!v.IsArray) { Error(v, path, "\"prerequisites\" must be an array of tech keys."); return keys.ToArray(); }
                if (v.Count > ModHardCaps.MaxPrerequisites) { Error(v, path, "At most " + ModHardCaps.MaxPrerequisites + " prerequisites."); return keys.ToArray(); }
                foreach (var item in v.Items)
                {
                    if (!item.IsString) { Error(item, path, "Prerequisite must be a tech key string."); continue; }
                    string k = item.StringValue;
                    if (keys.Contains(k)) { Error(item, path, "Duplicate prerequisite \"" + SafeJson.Printable(k) + "\"."); continue; }
                    AgeId preAge;
                    var existing = _db.TechByKey(k);
                    if (existing != null) preAge = existing.Age;
                    else if (_newTechByKey.TryGetValue(k, out var earlier)) preAge = earlier.Age;
                    else
                    {
                        Error(item, path, "Unknown prerequisite \"" + SafeJson.Printable(k) + "\" (must be an existing tech or a new tech defined earlier in this pack).");
                        continue;
                    }
                    if (preAge > def.Age) { Error(item, path, "Prerequisite \"" + SafeJson.Printable(k) + "\" belongs to a later age (" + preAge + ") than this tech (" + def.Age + ")."); continue; }
                    keys.Add(k);
                }
                return keys.ToArray();
            }

            private void ReadEffects(JsonValue v, string path, List<TechEffect> effects)
            {
                if (!v.IsArray) { Error(v, path, "\"effects\" must be an array."); return; }
                if (v.Count > ModHardCaps.MaxEffects) { Error(v, path, "At most " + ModHardCaps.MaxEffects + " effects per tech."); return; }
                for (int i = 0; i < v.Count; i++)
                {
                    var e = v.Item(i);
                    string p = path + "[" + i + "]";
                    if (!e.IsObject) { Error(e, p, "Effect must be an object."); continue; }
                    var kindV = e.Get("kind");
                    if (kindV == null || !kindV.IsString || !ModEnumNames.TryGet(ModEnumNames.EffectKinds, kindV.StringValue, out var kind))
                    {
                        Error(kindV ?? e, p, "Effect requires a valid \"kind\" (e.g. \"ProductionMultiplier\", \"UnlockBuilding\").");
                        continue;
                    }
                    var fx = new TechEffect { Kind = kind };
                    bool needsBuilding = kind == TechEffectKind.UnlockBuilding;
                    bool needsResource = kind == TechEffectKind.ProductionMultiplier;
                    bool needsService = kind == TechEffectKind.ServiceMultiplier;
                    bool needsValue = !needsBuilding;
                    double vmin = ModHardCaps.MinEffectMultiplier, vmax = ModHardCaps.MaxEffectMultiplier;
                    if (kind == TechEffectKind.LifespanBonus) { vmin = ModHardCaps.MinLifespanBonus; vmax = ModHardCaps.MaxLifespanBonus; }
                    else if (kind == TechEffectKind.HealthBonus || kind == TechEffectKind.HappinessBonus) { vmin = -1; vmax = 1; }
                    bool ok = true, hasB = false, hasR = false, hasS = false, hasV = false;
                    for (int k = 0; k < e.Count; k++)
                    {
                        string f = e.KeyAt(k);
                        var fv = e.Item(k);
                        string fp = p + "." + Seg(f);
                        switch (f)
                        {
                            case "kind": break;
                            case "building":
                                if (!needsBuilding) { Error(fv, fp, "\"building\" is not used by effect kind " + kind + "."); ok = false; break; }
                                if (fv.IsString && ModEnumNames.TryGet(ModEnumNames.Buildings, fv.StringValue, out var b)) { fx.Building = b; hasB = true; }
                                else { Error(fv, fp, "Unknown building type."); ok = false; }
                                break;
                            case "resource":
                                if (!needsResource) { Error(fv, fp, "\"resource\" is not used by effect kind " + kind + "."); ok = false; break; }
                                if (fv.IsString && ModEnumNames.TryGet(ModEnumNames.Resources, fv.StringValue, out var r)) { fx.Resource = r; hasR = true; }
                                else { Error(fv, fp, "Unknown resource type."); ok = false; }
                                break;
                            case "service":
                                if (!needsService) { Error(fv, fp, "\"service\" is not used by effect kind " + kind + "."); ok = false; break; }
                                if (fv.IsString && ModEnumNames.TryGet(ModEnumNames.Services, fv.StringValue, out var sv)) { fx.Service = sv; hasS = true; }
                                else { Error(fv, fp, "Unknown service type."); ok = false; }
                                break;
                            case "value":
                                if (!needsValue) { Error(fv, fp, "\"value\" is not used by effect kind " + kind + "."); ok = false; break; }
                                if (ReadFloat(fv, fp, vmin, vmax, out var val)) { fx.Value = val; hasV = true; } else ok = false;
                                break;
                            default: Unknown(fv, fp, f, EffectFields, NoFields); break;
                        }
                    }
                    if (needsBuilding && !hasB && ok) { Error(e, p, "Effect " + kind + " requires \"building\"."); ok = false; }
                    if (needsResource && !hasR && ok) { Error(e, p, "Effect " + kind + " requires \"resource\"."); ok = false; }
                    if (needsService && !hasS && ok) { Error(e, p, "Effect " + kind + " requires \"service\"."); ok = false; }
                    if (needsValue && !hasV && ok) { Error(e, p, "Effect " + kind + " requires \"value\"."); ok = false; }
                    if (ok) effects.Add(fx);
                }
            }

            private bool ReadRaceMask(JsonValue v, string path, out RaceMask mask)
            {
                mask = RaceMask.None;
                if (!v.IsArray || v.Count == 0 || v.Count > Counts.Races) { Error(v, path, "\"allowedRaces\" must be a non-empty array of race names."); return false; }
                foreach (var item in v.Items)
                {
                    if (!item.IsString || !ModEnumNames.TryGet(ModEnumNames.Races, item.StringValue, out var race)) { Error(item, path, "Unknown race."); return false; }
                    mask |= race.ToMask();
                }
                return true;
            }

            // ------------------------------------------------------------- invariants (mirror GameDatabase.Validate)
            private void CheckInvariants()
            {
                for (int i = 0; i < _buildings.Length; i++)
                {
                    var s = _buildings[i];
                    if (s == null) continue;
                    if (s.Job != JobType.None && s.JobSlots < 1)
                        InvariantError("buildings." + s.Type + ".jobSlots", "Building " + s.Type + " employs " + s.Job + " workers, so jobSlots must be at least 1.");
                    if (!(s.BuildWork > 0f)) InvariantError("buildings." + s.Type + ".buildWork", "buildWork must be > 0.");
                }
                for (int i = 0; i < _races.Length; i++)
                {
                    var s = _races[i];
                    if (s == null) continue;
                    if (s.LifeExpectancy <= s.AdulthoodAge)
                        InvariantError("races." + s.Id + ".lifeExpectancy", "lifeExpectancy (" + Fmt(s.LifeExpectancy) + ") must exceed the race's adulthood age (" + Fmt(s.AdulthoodAge) + ").");
                }
                // Age thresholds must stay non-decreasing (only report pairs that involve a patched age).
                for (int i = 1; i < Counts.Ages; i++)
                {
                    if (_ages[i] == null && _ages[i - 1] == null) continue;
                    var cur = _ages[i] ?? _db.Ages[i];
                    var prev = _ages[i - 1] ?? _db.Ages[i - 1];
                    if (cur == null || prev == null) continue;
                    if (cur.ResearchThreshold < prev.ResearchThreshold)
                    {
                        string p = "ages." + (_ages[i] != null ? (AgeId)i : (AgeId)(i - 1)) + ".researchThreshold";
                        InvariantError(p, "Research thresholds must be non-decreasing: " + (AgeId)i + " (" + Fmt(cur.ResearchThreshold) + ") < " + (AgeId)(i - 1) + " (" + Fmt(prev.ResearchThreshold) + ").");
                    }
                }
                for (int i = 0; i < _powers.Length; i++)
                {
                    var s = _powers[i];
                    if (s == null) continue;
                    if (s.MaxRadius < s.MinRadius)
                        InvariantError("godPowers." + s.Type + ".maxRadius", "maxRadius (" + s.MaxRadius + ") must be at least the power's minRadius (" + s.MinRadius + ").");
                }
            }

            private void InvariantError(string path, string message)
            {
                ModDiagnostic d;
                if (_byPath.TryGetValue(path, out int ci) && _changes[ci] != null)
                {
                    var c = _changes[ci];
                    d = new ModDiagnostic { ModId = _modId, Source = c.Source, Line = c.Line, Column = c.Column, Path = path, Message = message };
                }
                else
                    d = new ModDiagnostic { ModId = _modId, Path = path, Message = message };
                _r.AddError(d);
            }

            // ------------------------------------------------------------- commit (cannot fail: everything was validated)
            public void Commit()
            {
                for (int i = 0; i < _buildings.Length; i++)
                {
                    var s = _buildings[i];
                    if (s == null) continue;
                    var d = _db.Buildings[i];
                    d.Name = s.Name; d.Description = s.Description; d.Cost = s.Cost; d.BuildWork = s.BuildWork; d.JobSlots = s.JobSlots;
                    d.InputsPerWorker = s.InputsPerWorker; d.OutputsPerWorker = s.OutputsPerWorker; d.Services = s.Services;
                    d.StorageCapacity = s.StorageCapacity; d.UpkeepGold = s.UpkeepGold; d.Desirability = s.Desirability;
                    d.VisualHeight = s.VisualHeight; d.MinPopulation = s.MinPopulation;
                }
                for (int i = 0; i < _races.Length; i++)
                {
                    var s = _races[i];
                    if (s == null) continue;
                    var d = _db.Races[i];
                    d.Description = s.Description; d.LifeExpectancy = s.LifeExpectancy; d.FertilityPerYear = s.FertilityPerYear;
                    d.BaseHealth = s.BaseHealth; d.DiseaseResistance = s.DiseaseResistance; d.BuildSpeed = s.BuildSpeed;
                    d.ResearchRate = s.ResearchRate; d.FaithRate = s.FaithRate; d.MilitaryStrength = s.MilitaryStrength;
                    d.TradeAptitude = s.TradeAptitude; d.MoveSpeed = s.MoveSpeed; d.ProductionMultiplier = s.ProductionMultiplier;
                    d.Aggression = s.Aggression; d.Expansionism = s.Expansionism; d.Piety = s.Piety; d.Industriousness = s.Industriousness;
                    d.Curiosity = s.Curiosity; d.Diplomacy = s.Diplomacy; d.Xenophobia = s.Xenophobia; d.Mercantilism = s.Mercantilism;
                    d.BannerColor = s.BannerColor; d.SkinColor = s.SkinColor;
                    if (_namesTouched[i])
                    {
                        if (d.Names == null) d.Names = new NameParts();
                        d.Names.PersonPrefixes = s.Names.PersonPrefixes; d.Names.MaleSuffixes = s.Names.MaleSuffixes;
                        d.Names.FemaleSuffixes = s.Names.FemaleSuffixes; d.Names.FamilyNames = s.Names.FamilyNames;
                        d.Names.SettlementPrefixes = s.Names.SettlementPrefixes; d.Names.SettlementSuffixes = s.Names.SettlementSuffixes;
                        d.Names.CivilizationPatterns = s.Names.CivilizationPatterns;
                    }
                    if (_archTouched[i])
                    {
                        if (d.Architecture == null) d.Architecture = new ArchitectureStyle();
                        d.Architecture.WallColor = s.Architecture.WallColor; d.Architecture.RoofColor = s.Architecture.RoofColor;
                        d.Architecture.TrimColor = s.Architecture.TrimColor; d.Architecture.AccentColor = s.Architecture.AccentColor;
                    }
                }
                for (int i = 0; i < _ages.Length; i++)
                {
                    var s = _ages[i];
                    if (s == null) continue;
                    var d = _db.Ages[i];
                    d.Description = s.Description; d.ResearchThreshold = s.ResearchThreshold; d.MinPopulation = s.MinPopulation;
                }
                for (int i = 0; i < _powers.Length; i++)
                {
                    var s = _powers[i];
                    if (s == null) continue;
                    var d = _db.GodPowers[i];
                    d.Description = s.Description; d.ManaCost = s.ManaCost; d.CooldownTicks = s.CooldownTicks; d.MaxRadius = s.MaxRadius;
                }
                for (int i = 0; i < _techs.Length; i++)
                {
                    var s = _techs[i];
                    if (s == null) continue;
                    var d = _db.Tech(i);
                    d.Name = s.Name; d.Description = s.Description; d.Cost = s.Cost;
                }
                foreach (var t in _newTechs)
                {
                    _db.Register(t);
                    _r.AddedTechs.Add(t.Key);
                }
            }

            // ------------------------------------------------------------- typed readers (error on failure)
            private bool ReadFloat(JsonValue v, string path, double min, double max, out float result)
            {
                result = 0f;
                if (!v.IsNumber) { Error(v, path, "Expected a number in [" + Fmt(min) + ", " + Fmt(max) + "] but found " + KindName(v) + "."); return false; }
                double d = v.NumberValue;
                if (double.IsNaN(d) || d < min || d > max) { Error(v, path, "Value " + Fmt(d) + " is outside the allowed range [" + Fmt(min) + ", " + Fmt(max) + "]."); return false; }
                if (d == 0) d = 0; // normalize -0
                result = (float)d;
                if (float.IsNaN(result) || float.IsInfinity(result)) { Error(v, path, "Value is not representable."); return false; }
                return true;
            }

            private bool ReadInt(JsonValue v, string path, int min, int max, out int result)
            {
                result = 0;
                if (!v.IsNumber) { Error(v, path, "Expected an integer in [" + min + ", " + max + "] but found " + KindName(v) + "."); return false; }
                double d = v.NumberValue;
                if (d != Math.Floor(d)) { Error(v, path, "Expected a whole number but found " + Fmt(d) + "."); return false; }
                if (d < min || d > max) { Error(v, path, "Value " + Fmt(d) + " is outside the allowed range [" + min + ", " + max + "]."); return false; }
                result = (int)d;
                if (result == 0) result = 0;
                return true;
            }

            private bool ReadText(JsonValue v, string path, int maxLen, bool newlines, bool required, out string result)
            {
                result = "";
                if (!v.IsString) { Error(v, path, "Expected a string but found " + KindName(v) + "."); return false; }
                string raw = v.StringValue;
                result = ModText.Sanitize(raw, maxLen, newlines);
                if (required && result.Length == 0) { Error(v, path, "Text is empty after sanitization."); return false; }
                if (_opt.WarnOnSanitizedText && !string.Equals(raw, result, StringComparison.Ordinal))
                    Warn(v, path, raw.Length > maxLen ? "Text was truncated to " + maxLen + " characters and sanitized." : "Text was sanitized (control, bidi, invisible or markup characters removed; whitespace normalized).");
                return true;
            }

            private bool ReadColor(JsonValue v, string path, out uint color)
            {
                color = 0;
                if (!v.IsString) { Error(v, path, "Expected a color string \"#RRGGBB\" or \"#RRGGBBAA\"."); return false; }
                string s = v.StringValue;
                if ((s.Length != 7 && s.Length != 9) || s[0] != '#') { Error(v, path, "Expected a color string \"#RRGGBB\" or \"#RRGGBBAA\"."); return false; }
                uint acc = 0;
                for (int i = 1; i < s.Length; i++)
                {
                    char c = s[i];
                    uint d;
                    if (c >= '0' && c <= '9') d = (uint)(c - '0');
                    else if (c >= 'a' && c <= 'f') d = (uint)(c - 'a' + 10);
                    else if (c >= 'A' && c <= 'F') d = (uint)(c - 'A' + 10);
                    else { Error(v, path, "Invalid hex digit in color."); return false; }
                    acc = (acc << 4) | d;
                }
                color = s.Length == 7 ? (acc << 8) | 0xFFu : acc;
                return true;
            }

            private bool ReadResources(JsonValue v, string path, double min, double max, out ResourceAmount[] result)
            {
                result = null;
                if (!v.IsObject) { Error(v, path, "Expected an object mapping resource names to amounts, e.g. {\"Wood\": 10}."); return false; }
                var list = new ResourceAmount[v.Count];
                bool ok = true;
                for (int i = 0; i < v.Count; i++)
                {
                    string key = v.KeyAt(i);
                    var iv = v.Item(i);
                    string p = path + "." + Seg(key);
                    if (!ModEnumNames.TryGet(ModEnumNames.Resources, key, out var rt)) { Error(iv, p, "Unknown resource type."); ok = false; continue; }
                    if (!ReadFloat(iv, p, min, max, out var amount)) { ok = false; continue; }
                    list[i] = new ResourceAmount(rt, amount);
                }
                if (ok) result = list;
                return ok;
            }

            private bool ReadServices(JsonValue v, string path, double min, double max, out ServiceAmount[] result)
            {
                result = null;
                if (!v.IsObject) { Error(v, path, "Expected an object mapping service names to amounts, e.g. {\"Faith\": 1.5}."); return false; }
                var list = new ServiceAmount[v.Count];
                bool ok = true;
                for (int i = 0; i < v.Count; i++)
                {
                    string key = v.KeyAt(i);
                    var iv = v.Item(i);
                    string p = path + "." + Seg(key);
                    if (!ModEnumNames.TryGet(ModEnumNames.Services, key, out var st)) { Error(iv, p, "Unknown service type."); ok = false; continue; }
                    if (!ReadFloat(iv, p, min, max, out var amount)) { ok = false; continue; }
                    list[i] = new ServiceAmount(st, amount);
                }
                if (ok) result = list;
                return ok;
            }

            private bool ReadNameList(JsonValue v, string path, int maxLen, bool patterns, out string[] result)
            {
                result = null;
                if (!v.IsArray || v.Count == 0) { Error(v, path, "Expected a non-empty array of strings."); return false; }
                if (v.Count > ModHardCaps.MaxNameListEntries) { Error(v, path, "At most " + ModHardCaps.MaxNameListEntries + " entries."); return false; }
                var list = new string[v.Count];
                bool ok = true;
                for (int i = 0; i < v.Count; i++)
                {
                    var item = v.Item(i);
                    if (!item.IsString) { Error(item, path, "Expected a string."); ok = false; continue; }
                    string raw = item.StringValue;
                    string clean;
                    if (patterns)
                    {
                        clean = ModText.Sanitize(raw, maxLen, false);
                        if (!ModText.IsValidCivilizationPattern(clean))
                        {
                            Error(item, path, "Civilization pattern \"" + SafeJson.Printable(clean) + "\" must contain exactly one {0} and no other braces.");
                            ok = false; continue;
                        }
                    }
                    else clean = ModText.SanitizeSyllable(raw, maxLen);
                    if (clean.Length == 0) { Error(item, path, "Entry \"" + SafeJson.Printable(raw) + "\" is empty after sanitization (letters, apostrophes, hyphens and spaces only)."); ok = false; continue; }
                    if (_opt.WarnOnSanitizedText && !string.Equals(raw, clean, StringComparison.Ordinal))
                        Warn(item, path, "Entry \"" + SafeJson.Printable(raw) + "\" was sanitized to \"" + SafeJson.Printable(clean) + "\".");
                    list[i] = clean;
                }
                if (ok) result = list;
                return ok;
            }

            // ------------------------------------------------------------- read + record helpers
            private bool SetFloat(JsonValue v, string path, double min, double max, float old, out float value)
            {
                if (!ReadFloat(v, path, min, max, out value)) return false;
                Record(path, v, Fmt(old), Fmt(value));
                return true;
            }

            private bool SetInt(JsonValue v, string path, int min, int max, int old, out int value)
            {
                if (!ReadInt(v, path, min, max, out value)) return false;
                Record(path, v, old.ToString(CultureInfo.InvariantCulture), value.ToString(CultureInfo.InvariantCulture));
                return true;
            }

            private bool SetText(JsonValue v, string path, int maxLen, bool newlines, bool required, string old, out string value)
            {
                if (!ReadText(v, path, maxLen, newlines, required, out value)) return false;
                Record(path, v, Quote(old), Quote(value));
                return true;
            }

            private bool SetColor(JsonValue v, string path, uint old, out uint value)
            {
                if (!ReadColor(v, path, out value)) return false;
                Record(path, v, FmtColor(old), FmtColor(value));
                return true;
            }

            private bool SetResources(JsonValue v, string path, double min, double max, ResourceAmount[] old, out ResourceAmount[] value)
            {
                if (!ReadResources(v, path, min, max, out value)) return false;
                Record(path, v, FmtResources(old), FmtResources(value));
                return true;
            }

            private bool SetServices(JsonValue v, string path, double min, double max, ServiceAmount[] old, out ServiceAmount[] value)
            {
                if (!ReadServices(v, path, min, max, out value)) return false;
                Record(path, v, FmtServices(old), FmtServices(value));
                return true;
            }

            private bool SetNameList(JsonValue v, string path, int maxLen, bool patterns, string[] old, out string[] value)
            {
                if (!ReadNameList(v, path, maxLen, patterns, out value)) return false;
                Record(path, v, FmtList(old), FmtList(value));
                return true;
            }

            private void Record(string path, JsonValue at, string oldValue, string newValue)
            {
                var change = new ModChange { Path = path, OldValue = oldValue, NewValue = newValue, Source = _src, Line = at != null ? at.Line : 0, Column = at != null ? at.Column : 0 };
                if (_byPath.TryGetValue(path, out int prevIndex))
                {
                    var prev = _changes[prevIndex];
                    Warn(at, path, "Set more than once in this pack (previously at " + ModLoadException.Format("", prev.Source, prev.Line, prev.Column).TrimEnd(' ', ':') + "); the later value wins.");
                    _changes[prevIndex] = null; // O(1); compacted when the report is filled
                }
                _byPath[path] = _changes.Count;
                _changes.Add(change);
            }

            private void Unknown(JsonValue at, string path, string field, string[] whitelist, string[] protectedFields)
            {
                string f = field ?? "";
                foreach (var p in protectedFields)
                    if (string.Equals(p, f, StringComparison.OrdinalIgnoreCase))
                    {
                        Error(at, path, "Field '" + SafeJson.Printable(f) + "' is protected and cannot be changed by mods (not on the whitelist).");
                        return;
                    }
                string hint = "";
                foreach (var w in whitelist)
                    if (string.Equals(w, f, StringComparison.OrdinalIgnoreCase)) { hint = " Did you mean '" + w + "'? (keys are case-sensitive)"; break; }
                if (_opt.StrictUnknownFields) Error(at, path, "Unknown field '" + SafeJson.Printable(f) + "'." + hint);
                else Warn(at, path, "Unknown field '" + SafeJson.Printable(f) + "' ignored." + hint);
            }

            private void Error(JsonValue at, string path, string message) => _r.AddError(_src, at, path, message);
            private void Warn(JsonValue at, string path, string message) => _r.AddWarning(_src, at, path, message);
        }

        // ----------------------------------------------------------------- formatting
        /// <summary>Path segment: printable ASCII keys up to 128 chars are used verbatim, anything else is escaped.</summary>
        internal static string Seg(string s)
        {
            if (s == null) return "";
            if (s.Length > 0 && s.Length <= 128)
            {
                bool plain = true;
                for (int i = 0; i < s.Length; i++) if (s[i] < 0x21 || s[i] > 0x7E) { plain = false; break; }
                if (plain) return s;
            }
            return SafeJson.Printable(s);
        }

        private static string KindName(JsonValue v) => v == null ? "nothing" : v.Kind.ToString().ToLowerInvariant();
        private static string Fmt(double d) => JsonValue.FormatNumber(d);
        private static string Fmt(float f) => float.IsNaN(f) || float.IsInfinity(f) ? "0" : f.ToString("R", CultureInfo.InvariantCulture);
        private static string Quote(string s) => "\"" + SafeJson.Printable(s ?? "") + "\"";
        private static string FmtColor(uint c) => "#" + c.ToString("X8", CultureInfo.InvariantCulture);

        private static string FmtResources(ResourceAmount[] a)
        {
            if (a == null || a.Length == 0) return "{}";
            var sb = new StringBuilder("{");
            for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(a[i].Type).Append(": ").Append(Fmt(a[i].Amount)); }
            return sb.Append('}').ToString();
        }

        private static string FmtServices(ServiceAmount[] a)
        {
            if (a == null || a.Length == 0) return "{}";
            var sb = new StringBuilder("{");
            for (int i = 0; i < a.Length; i++) { if (i > 0) sb.Append(", "); sb.Append(a[i].Type).Append(": ").Append(Fmt(a[i].Amount)); }
            return sb.Append('}').ToString();
        }

        private static string FmtList(string[] a)
        {
            if (a == null || a.Length == 0) return "[]";
            var sb = new StringBuilder("[");
            int n = Math.Min(a.Length, 8);
            for (int i = 0; i < n; i++) { if (i > 0) sb.Append(", "); sb.Append(Quote(a[i])); }
            if (a.Length > n) sb.Append(", ... (").Append(a.Length).Append(" total)");
            return sb.Append(']').ToString();
        }
    }
}
