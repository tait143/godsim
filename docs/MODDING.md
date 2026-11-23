# GodSim Modding Guide (data packs)

GodSim mods are **data packs**: JSON files that adjust whitelisted numbers and texts of the built-in
definitions and can add new technologies. Mods never contain code. Scripts, DLLs and asset bundles are
never loaded. Everything a mod supplies is parsed by a hardened parser, range-checked, sanitized and
applied all-or-nothing **before** the game database is frozen.

Implementation: `Assets/GodSim/Core/Modding/` (namespace `GodSim.Core.Modding`).
Format version: **1**. Game version checked by `gameVersionRange`: **0.1.0** (`ModdingInfo.GameVersion`).

---

## 1. Pack layout

```
<mods root>/
  better_houses/            <- pack folder (should equal the mod id)
    mod.json                <- manifest (required)
    data/                   <- patch documents (optional)
      buildings.json
      techs.json
```

- Only direct child folders of the mods root are packs. Folders starting with `.` are ignored.
- Folder and file names must be plain names: `[A-Za-z0-9_.-]`, no path separators, no `..`, no
  leading or trailing dot, and no Windows device names (`CON`, `NUL`, `COM1`...). Data files end in `.json`.
- By default every `data/*.json` is applied in ordinal file-name order. `dataFiles` in mod.json sets an
  explicit order (and only those files are read).
- Symbolic links, junctions, empty or special files (FIFOs, devices) are refused.
- Any other file is ignored. Code files (`.dll .exe .so .cs .js .lua .py .sh ...`) produce a warning.

## 2. mod.json

```json
{
  "id": "better_houses",
  "name": "Better Houses",
  "version": "1.2.0",
  "author": "Ann Example",
  "description": "Cheaper, faster houses.\nSecond line.",
  "gameVersionRange": ">=0.1.0 <1.0.0",
  "loadOrder": 10,
  "dependencies": [
    "core_fixes",
    { "id": "pretty_names", "version": "^1.0.0", "optional": true }
  ],
  "dataFiles": ["buildings.json", "techs.json"]
}
```

| Key | Required | Rules |
|---|---|---|
| `id` | yes | `^[a-z0-9_.-]{3,48}$`, at least one letter or digit, unique across installed packs |
| `name` | yes | sanitized text, max 64 chars, not empty |
| `version` | yes | semver 2.0.0 (`1.2.3`, `1.2.3-beta.1+build.5`), components <= 999,999,999 |
| `author` | no | sanitized text, max 64 |
| `description` | no | sanitized text, max 1024, newlines kept |
| `gameVersionRange` | no (default `*`) | see version ranges below |
| `loadOrder` | no (default 0) | integer -10,000..10,000. Lower loads first. |
| `dependencies` | no | max 32. A string id, or `{id, version, optional}` |
| `dataFiles` | no | max 64 plain file names inside `data/` |

Unknown manifest keys produce warnings.

**Version ranges:** `*` (any version), `1.2.3` or `=1.2.3` (exact), `>`, `>=`, `<`, `<=`, `^1.2.3`
(`>=1.2.3 <2.0.0`, for 0.x versions `^0.2.3` means `<0.3.0`), `~1.2.3` (`<1.3.0`). Space-separated
comparators must all match. `||` separates alternatives. Pre-releases compare by plain semver precedence.

## 3. Patch documents (data/*.json)

Each document is one JSON object. Sections are optional and may appear in any document:

```json
{
  "formatVersion": 1,
  "buildings": {
    "House":  { "buildWork": 25, "cost": { "Wood": 8, "Stone": 2 }, "description": "A sturdy home." },
    "Farm":   { "outputsPerWorker": { "Food": 0.4 }, "jobSlots": 6 },
    "Temple": { "services": { "Faith": 3 }, "upkeepGold": 0.1 }
  },
  "races": {
    "Elf": {
      "lifeExpectancy": 400,
      "researchRate": 1.4,
      "productionMultiplier": { "Wood": 1.5 },
      "aggression": 0.1,
      "bannerColor": "#33CC66",
      "names": {
        "personPrefixes": ["Ae", "Ela", "Fae"],
        "civilizationPatterns": ["The {0} Court", "Grove of {0}"]
      }
    }
  },
  "techs":     { "bronze.casting": { "cost": 250, "description": "Copper and tin." } },
  "ages":      { "Bronze": { "researchThreshold": 500, "minPopulation": 20 } },
  "godPowers": { "Lightning": { "manaCost": 20, "cooldownTicks": 48, "maxRadius": 4 } },
  "newTechs": [
    {
      "key": "better_houses.masonry",
      "name": "Advanced Masonry",
      "description": "Stone houses for everyone.",
      "age": "Bronze",
      "cost": 300,
      "prerequisites": ["bronze.casting"],
      "allowedRaces": ["Human", "Dwarf"],
      "effects": [
        { "kind": "BuildSpeedMultiplier", "value": 0.1 },
        { "kind": "ProductionMultiplier", "resource": "Stone", "value": 0.15 },
        { "kind": "UnlockBuilding", "building": "Library" }
      ]
    }
  ]
}
```

- Entry names are exact, case-sensitive enum names (`House`, `Elf`, `Bronze`, `Lightning`, `Wood`,
  `Faith`). Numeric names (`"1"`) and flag combinations (`"House, Well"`) are rejected.
- Tech entries are addressed by their key (`"bronze.casting"`).
- Field keys are camelCase and case-sensitive.
- Resource and service maps (`cost`, `inputsPerWorker`, `outputsPerWorker`, `services`) **replace** the
  whole list.
- `productionMultiplier` patches single resources. Each name list in `names` replaces that one list.
- Colors are `"#RRGGBB"` (alpha FF) or `"#RRGGBBAA"`.
- Integers must be whole numbers.

## 4. Whitelist (everything else is protected)

Every range is inclusive. A value outside its range is an **error**, and the value is never clamped.

### BuildingDefinition (`buildings.<BuildingType>`)
| JSON key | C# field | Type / range |
|---|---|---|
| `name` | Name | text 1..48 |
| `description` | Description | text 0..1024 (newlines allowed) |
| `cost` | Cost | `{Resource: 0..100000}` |
| `buildWork` | BuildWork | 0.1..100000 |
| `jobSlots` | JobSlots | int 0..100 (>= 1 if the building has a job) |
| `inputsPerWorker` | InputsPerWorker | `{Resource: 0..10}` per tick |
| `outputsPerWorker` | OutputsPerWorker | `{Resource: 0..10}` per tick |
| `services` | Services | `{Service: 0..10}` per tick |
| `storageCapacity` | StorageCapacity | 0..100000 |
| `upkeepGold` | UpkeepGold | 0..10 per tick |
| `desirability` | Desirability | -1..1 |
| `visualHeight` | VisualHeight | 0.1..20 |
| `minPopulation` | MinPopulation | int 0..1,000,000 |

Protected (always an error): `type, category, shape, minAge, maxAge, allowedRaces, minTier,
maxPerSettlement, uniquePerCivilization, isWonder, width, height, maxCondition, housingMin, housingMax,
job, serviceRadius, placement, tintColor`. **`housingMin`/`housingMax` are protected so the core rule
"a House holds 3-5 residents" cannot be broken.**

### RaceDefinition (`races.<RaceId>`)
| JSON key | Type / range |
|---|---|
| `description` | text 0..1024 |
| `lifeExpectancy` | 1..2000 years, and greater than the race's adulthood age |
| `fertilityPerYear` | 0..1 |
| `baseHealth`, `diseaseResistance`, `buildSpeed`, `researchRate`, `faithRate`, `militaryStrength`, `tradeAptitude`, `moveSpeed` | 0.01..10 |
| `productionMultiplier` | `{Resource: 0.01..10}` |
| `aggression`, `expansionism`, `piety`, `industriousness`, `curiosity`, `diplomacy`, `xenophobia`, `mercantilism` | 0..1 |
| `names.personPrefixes`, `maleSuffixes`, `femaleSuffixes`, `settlementPrefixes`, `settlementSuffixes` | 1..128 entries, each 1..24 chars (letters, combining marks, `'`, `-`, space) |
| `names.familyNames` | 1..128 entries, each 1..32 chars (same alphabet) |
| `names.civilizationPatterns` | 1..128 entries, each <= 64 chars, exactly one `{0}` and no other braces |
| `bannerColor`, `skinColor`, `wallColor`, `roofColor`, `trimColor`, `accentColor` | color |

Protected: `id, name, pluralName, adjective, biomeHabitability, adulthoodAge, elderAge, maxChildren,
foodPerTick, raceAffinity, minSettlementSpacing, compactness, roadAffinity, architecture,
uniqueBuildings, favoredBuildings`.

### TechDefinition (`techs.<key>`)
| JSON key | Type / range |
|---|---|
| `name` | text 1..48 |
| `description` | text 0..1024 |
| `cost` | 1..100000 |

Protected: `id, key, age, prerequisites, prerequisiteKeys, effects, allowedRaces`.

### New techs (`newTechs: [...]`, at most 256 per pack)
| JSON key | Required | Rules |
|---|---|---|
| `key` | yes | `"<modId>.<name>"`, where `<name>` is `[a-z0-9_-]{1,32}` with no further dots. Must not exist yet. |
| `name` | yes | text 1..48 |
| `description` | no | text 0..1024 |
| `age` | yes | age name |
| `cost` | yes | 1..100000 |
| `prerequisites` | no | max 16 keys. Each must be an existing tech or a new tech defined **earlier in the same pack**, from the same or an earlier age. This rule makes cycles impossible. |
| `allowedRaces` | no (all) | non-empty array of race names |
| `effects` | no | max 8 effects (see below) |

Effect kinds: `UnlockBuilding` (`building`, no `value`); `ProductionMultiplier` (`resource`, `value`);
`ServiceMultiplier` (`service`, `value`); `AllProductionMultiplier`, `ResearchMultiplier`,
`GrowthMultiplier`, `MilitaryMultiplier`, `BuildSpeedMultiplier`, `FaithMultiplier`,
`StorageMultiplier`, `TradeMultiplier` and `MoveSpeedMultiplier` (`value`). Each has these limits:
multipliers -0.9..2 (that is -90%..+200%), `LifespanBonus` -50..100 years, and `HealthBonus` /
`HappinessBonus` -1..1. A field that an effect kind does not use is an error.

### AgeDefinition (`ages.<AgeId>`)
| JSON key | Type / range |
|---|---|
| `description` | text 0..1024 |
| `researchThreshold` | 0..10,000,000. Thresholds must stay non-decreasing across ages. |
| `minPopulation` | int 0..1,000,000 |

Protected: `id, name, requiredTechsFromPreviousAge, minSettlements, productionBonus, researchBonus,
lifespanBonusYears, healthBonus, baseSoldierStrength, themeColor, architectureNotes`.

### GodPowerDefinition (`godPowers.<GodPowerType>`)
| JSON key | Type / range |
|---|---|
| `description` | text 0..1024 |
| `manaCost` | 0..100000 |
| `cooldownTicks` | int 0..9600 (100 years) |
| `maxRadius` | int 0..32, and >= the power's minRadius |

Protected: `type, name, category, minRadius, requiresLandTarget, requiresSettlementInRange,
alignmentDelta, usesRaceParameter, minWorldAge`.

**Unknown keys** (typos, unsupported sections) are errors by default (`ModApplyOptions.StrictUnknownFields = true`).
When the option is false they are warnings and are ignored. The message suggests the right spelling when
only the letter case differs (`BuildWork` gives "did you mean 'buildWork'?"). Protected keys are always
errors, whatever the option says.

## 5. Limits

| Limit | Value |
|---|---|
| JSON file size | 2 MB (`JsonLimits.MaxInputBytes`, loader `MaxFileBytes`) |
| mod.json size | 64 KB |
| Pack size / all packs | 16 MB / 64 MB |
| Packs / data files per pack / directory entries | 64 / 64 / 1024 |
| Nesting depth | 32 |
| String length | 4096 UTF-16 units |
| Array items / object members | 10,000 / 1,000 |
| Values per document | 200,000 |
| Number literal length | 64 chars |
| Diagnostics kept per report | 500 errors + 500 warnings |

JSON is strict RFC 8259. These are rejected: comments, trailing commas, single quotes, unquoted keys,
duplicate keys, `NaN`/`Infinity`, leading zeros, numbers that overflow a double, invalid escapes, raw
control characters inside strings, lone surrogates (raw or `\uXXXX`) and invalid UTF-8. One leading UTF-8 BOM
is allowed. A BOM anywhere else is an error.

## 6. Security model

1. **Data only.** No code of any kind is loaded, compiled, reflected or evaluated. Fields are bound by
   hand-written `switch` statements over an explicit whitelist. There is no reflection, no
   `Enum.Parse` and no general-purpose deserializer.
2. **Sandboxed IO.** `ModLoader` is the only class that touches the file system. It reads only
   `<root>/<folder>/mod.json` and `<root>/<folder>/data/<file>.json`. It validates names, checks that
   every full path stays inside the root, and refuses symlinks, junctions and special files. It caps every
   read before allocating and detects files that grow while being read. Directory listings are capped.
3. **Hardened parser.** `SafeJson` runs in linear time with bounded recursion and enforces every limit
   while parsing. It throws only `ModLoadException`, with `file:line:col`. Duplicate-key detection sorts
   the keys instead of hashing them, so attacker-chosen keys cannot cause a hash flood.
4. **Validated values.** Every number is range-checked against `ModHardCaps` (options can only tighten
   the caps). NaN and Infinity cannot occur. Integers are checked before they are cast, so they cannot
   overflow. Enum names are matched exactly. Cross-field invariants that `GameDatabase.Freeze` enforces
   are checked up front: building jobs have slots, life expectancy is above adulthood, age thresholds
   are monotonic, `maxRadius >= minRadius`, and tech prerequisites are resolvable, acyclic and not
   from a later age.
5. **Sanitized text.** All text loses C0/C1 controls, bidi overrides and isolates (Trojan Source),
   zero-width and invisible fillers, BOMs, tag characters, noncharacters, lone surrogates and `<` `>`
   (no rich-text markup injection). Text also keeps at most two combining marks per base character
   (anti-Zalgo), gets its whitespace normalized and is length-capped without splitting surrogate pairs.
   Name syllables use an allow-list (letters, marks, `'`, `-`, space). Civilization patterns must contain
   exactly one `{0}`, so `string.Format` can never throw or read extra arguments (`{1}` and `{0,99999999}`
   are rejected). Diagnostics escape and truncate any echoed text.
6. **Transactional.** Each pack is staged on shadow copies and validated completely. Only then is it
   committed: fields are copied onto the live definitions and new techs are registered. If any error
   occurs, nothing changes. Arrays are always replaced and never mutated in place, so shared statics
   such as `ResourceAmount.None` are safe.
7. **Frozen afterwards.** Mods apply only to an unfrozen database (`Apply` throws
   `InvalidOperationException` if `db.IsFrozen`). After `db.Freeze()` nothing can change the definitions.

## 7. Integration (composition root)

```csharp
var db = new GameDatabase();
RaceCatalog.Register(db); AgeCatalog.Register(db); TechCatalog.Register(db);
BuildingCatalog.Register(db); GodPowerCatalog.Register(db);

var load = new ModLoader(new ModLoaderOptions { GameVersion = ModdingInfo.GameVersion }).LoadAll(modsRoot);
foreach (var e in load.Errors) log.Warn(e.ToString());      // rejected packs are skipped, never fatal
foreach (var w in load.Warnings) log.Info(w.ToString());
var reports = ModPatchApplier.ApplyAll(db, load.Packs, new ModApplyOptions { StrictUnknownFields = true });
foreach (var r in reports) log.Info(r.ToString());           // r.Applied == false -> pack changed nothing

var problems = ContentValidator.Validate(db);                // Security module, before Freeze
db.Freeze();
```

`LoadSelected(root, enabledFolderNames)` loads only the packs the player enabled. To build packs in
memory (tests, tools, downloaded content) use `ModPack.FromJson(manifestJson, documents...)` or
`ModPatchDocument.Parse`. `ModLoader.Resolve(packs, options, result)` orders packs and checks
dependencies without any IO.

**Load order:** dependencies always load first. Among the rest, packs load by `loadOrder`, then by
`id` (ordinal). When two packs set the same field, the later pack wins. The loader records this in
`ModLoadResult.Conflicts` and as a warning, and `ApplyAll` adds a warning to the later report. If one
pack sets the same field twice, the last document wins and you get a warning.

## 8. Troubleshooting

| Message | Cause / fix |
|---|---|
| `x.json:3:8: Invalid literal; expected 'true'` | JSON syntax error at line 3, column 8. Check for typos, missing quotes or commas. |
| `Trailing comma in object` | Remove the comma before `}` or `]`. |
| `Duplicate key "buildWork"` | The same key appears twice in one object. |
| `Unknown field 'BuildWork'. Did you mean 'buildWork'?` | Keys are camelCase and case-sensitive. |
| `Field 'housingMin' is protected` | The field is not on the whitelist (section 4) and cannot be modded. |
| `Unknown building type` | Use the exact enum name (`House`, not `house` or `1`). |
| `Value 1e9 is outside the allowed range [0.1, 100000]` | Respect the hard caps in section 4. Values are never clamped. |
| `Expected a whole number` | Integer fields such as `jobSlots` and `cooldownTicks` take no fractions. |
| `lifeExpectancy ... must exceed the race's adulthood age` | Raise the value. |
| `Research thresholds must be non-decreasing` | An age cannot need less research than the age before it. |
| `New tech key must be "<id>.<name>"` | Prefix new tech keys with your mod id, e.g. `my_mod.alchemy` (no extra dots, lowercase). |
| `Unknown prerequisite` | The prerequisite must already exist, or be defined earlier in the same pack. |
| `Prerequisite ... belongs to a later age` | A tech cannot require a tech from a later age. |
| `Civilization pattern ... must contain exactly one {0}` | Use, for example, `"The {0} Empire"`. |
| `Entry ... is empty after sanitization` | Name syllables may contain only letters, `'`, `-` and spaces. |
| `Text was sanitized` (warning) | Control, bidi, invisible or `<` `>` characters were removed. |
| `Missing required dependency 'x'` | Install the dependency, or mark it `"optional": true`. |
| `Dependency cycle detected: a -> b -> a` | Remove one of the dependencies. |
| `Requires game version ...` | Update `gameVersionRange`, or use a matching game version. |
| `Path escapes the mods directory` / `Invalid pack folder name` | Use plain folder and file names. No `..`, separators, links or device names. |
| `File is empty (or not a regular file)` | Empty files, FIFOs and devices are refused. |
| `Mod 'x' was not applied` | One error blocks the whole pack (all-or-nothing). Fix every listed error. |
