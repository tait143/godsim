using System;
using System.Text;

namespace GodSim.Core.Geography
{
    /// <summary>
    /// Deterministic atlas naming. <see cref="Compose"/> is pure: the same inputs and Rng state always give the
    /// same phrase. Patterns combine curated, original word tables (adjectives by climate and kind, nouns by kind
    /// and size, "of ..." abstractions, generated syllable roots, compound continent names).
    /// Phrases carry their English article ("the Ashen Peaks"); <see cref="GeoRegion.Name"/> is the phrase
    /// without it. Every phrase is at most <see cref="MaxLength"/> characters.
    /// </summary>
    public static class PlaceNames
    {
        public const int MaxLength = 40;
        private const int Attempts = 16;

        // ------------------------------------------------------------------ word tables (original, curated)

        private static readonly string[] AdjCommon =
        {
            "Ashen", "Whispering", "Sunken", "Gilded", "Silent", "Shattered", "Hollow", "Weeping", "Amber", "Iron",
            "Verdant", "Crimson", "Pale", "Shrouded", "Forgotten", "Thundering", "Starlit", "Emerald", "Sable",
            "Gloaming", "Wandering", "Moonlit", "Howling", "Misty", "Elder", "Lonely", "Dreaming", "Restless",
            "Broken", "Twilight", "Hallowed", "Murmuring", "Veiled", "Sleeping", "Silver", "Bitter", "Golden",
            "Wild", "Ancient", "Lost", "Hidden", "Painted", "Windswept", "Opal", "Obsidian", "Copper", "Grey",
            "Raven", "Dim", "Far", "Sorrowing", "Hushed", "Fabled", "Wraith", "Cobalt", "Bright", "Old"
        };
        private static readonly string[] AdjCold =
        {
            "Frozen", "Rime", "Frostbitten", "Hoarfrost", "Bleak", "Glacial", "Winter", "White", "Shivering",
            "Icebound", "Snowbound", "Pale", "Howling", "Frostfell", "Brittle", "Silent"
        };
        private static readonly string[] AdjHot =
        {
            "Sunscorched", "Burning", "Scorched", "Blazing", "Sunlit", "Ember", "Saffron", "Dusty", "Searing",
            "Sunbaked", "Bronze", "Vermilion", "Gilded", "Amber", "Golden", "Shimmering"
        };
        private static readonly string[] AdjMountain =
        {
            "Jagged", "Towering", "Grim", "Stormcrowned", "Skyward", "Granite", "Thundering", "Broken", "Iron",
            "Titan", "Shattered", "Craggy", "Lofty", "Cloudpiercing"
        };
        private static readonly string[] AdjHills =
        {
            "Rolling", "Barrow", "Green", "Gentle", "Windy", "Weathered", "Lonely", "Heather", "Mossy", "Stony", "Sleeping"
        };
        private static readonly string[] AdjWood =
        {
            "Tangled", "Elder", "Murmuring", "Deep", "Mossy", "Shadowed", "Ancient", "Rustling", "Lichen", "Thorny",
            "Whispering", "Brooding", "Hollow", "Emerald", "Silent"
        };
        private static readonly string[] AdjDesert =
        {
            "Shifting", "Thirsting", "Bone", "Endless", "Burning", "Glass", "Salt", "Red", "Mirage", "Sunscorched",
            "Barren", "Howling", "Copper", "Ochre"
        };
        private static readonly string[] AdjWet =
        {
            "Rotting", "Drowned", "Murky", "Weeping", "Fetid", "Brackish", "Misty", "Croaking", "Sodden", "Lantern",
            "Sunken", "Grey", "Gloomy", "Black"
        };
        private static readonly string[] AdjWater =
        {
            "Sapphire", "Endless", "Shimmering", "Restless", "Stormy", "Glass", "Tidal", "Azure", "Cobalt", "Brine",
            "Dark", "Cerulean", "Silver", "Moonlit", "Sunken", "Grey", "Emerald", "Still", "Mirror", "Deep"
        };
        private static readonly string[] AdjPlains =
        {
            "Golden", "Windswept", "Rolling", "Endless", "Amber", "Green", "Waving", "Sunlit", "Wide", "Open",
            "Gentle", "Swaying", "Bright", "Wildflower"
        };
        private static readonly string[] AdjVolcano =
        {
            "Smoldering", "Burning", "Cinder", "Ashen", "Molten", "Wrathful", "Smoking", "Black", "Fuming",
            "Scorched", "Sulfur", "Obsidian"
        };
        private static readonly string[] AdjRiver =
        {
            "Winding", "Silver", "Swift", "Laughing", "Lazy", "Wandering", "Singing", "Rushing", "Clear", "Bright",
            "Murmuring", "Twisting", "Cold", "Amber"
        };

        private static readonly string[] OfWater =
        {
            "Storms", "Tides", "Pearls", "Mists", "Drowned Bells", "Serpents", "Sorrows", "Whispers", "Stars",
            "Lanterns", "Glass", "Salt", "Echoes", "Dawn", "Ghosts", "Lost Ships", "Gulls", "Kings"
        };
        private static readonly string[] OfLand =
        {
            "Embers", "Shadows", "Kings", "Ghosts", "Bones", "Lanterns", "Silence", "Thorns", "Ash", "Glass",
            "Songs", "Dreams", "Ravens", "Wolves", "Giants", "Sorrows", "Whispers", "Stars", "Echoes", "Dawn",
            "Crowns", "Spears", "Old Gods", "Lost Souls"
        };

        private static readonly string[] RootPre =
        {
            "Al", "Bel", "Cor", "Dun", "Eld", "Fal", "Gor", "Hal", "Ith", "Kar", "Lor", "Mor", "Nar", "Or", "Pel",
            "Quel", "Ras", "Sil", "Tor", "Ul", "Val", "Wyn", "Yr", "Zan", "Ash", "Bry", "Cael", "Dra", "Ess", "Fen",
            "Gal", "Hes", "Iv", "Jor", "Kel", "Lys", "Mal", "Nev", "Oth", "Pyr", "Rhun", "Sar", "Thal", "Vey",
            "Ar", "Bram", "Cen", "Del", "Em", "Ost"
        };
        private static readonly string[] RootSuf =
        {
            "ador", "eth", "ion", "mar", "oth", "ura", "is", "en", "wyn", "dra", "esh", "ann", "ir", "ova", "und",
            "ar", "el", "os", "ith", "orn", "ane", "ys", "athe", "ock", "iel", "umar", "enor", "avel", "ost", "ul"
        };

        private static readonly string[] CompoundPre =
        {
            "Thorn", "Raven", "Iron", "Elder", "Stone", "Wyrm", "Star", "Ash", "Mist", "Oak", "Bright", "Grey",
            "Storm", "Dusk", "Dawn", "High", "Wolf", "Hollow", "Silver", "Briar", "Crow", "Fell", "Amber", "Black"
        };
        private static readonly string[] CompoundPreCold = { "Frost", "Rime", "Winter", "White", "Snow", "Ice" };
        private static readonly string[] CompoundPreHot = { "Sun", "Ember", "Gold", "Flame", "Saffron", "Bronze" };
        private static readonly string[] CompoundSuf =
        {
            "reach", "mark", "vale", "wold", "gard", "land", "haven", "march", "crest", "holm", "moor", "fall",
            "shire", "rest", "hold", "ward", "dale", "weald", "spire", "fast"
        };

        private static readonly string[] Qualifiers =
        {
            "Lesser", "Greater", "Northern", "Southern", "Eastern", "Western", "Upper", "Lower", "Outer", "Inner", "Old", "Far"
        };

        // ------------------------------------------------------------------ patterns ({A} adjective, {R} root, {O} "of" noun, {C} compound)

        private static readonly string[] PContinent = { "{C}", "{C}", "{C}", "{R}", "the {A} Reach", "the {A} Expanse", "the {A} Lands" };
        private static readonly string[] PIslandTiny = { "{A} Skerry", "{A} Rock", "the {A} Islet", "{R} Rock" };
        private static readonly string[] PIslandSmall = { "the {A} Isle", "the Isle of {R}", "{R} Isle", "the Isle of {O}", "{A} Island" };
        private static readonly string[] PIslandLarge = { "{C}", "{R}", "the Isle of {R}", "the {A} Isle", "{R} Island" };
        private static readonly string[] POcean = { "the {A} Ocean", "the {A} Ocean", "the {A} Deeps", "the Ocean of {O}", "the {R} Ocean" };
        private static readonly string[] PSea = { "the {A} Sea", "the Sea of {O}", "the {A} Waters", "the {R} Sea", "the {A} Sea" };
        private static readonly string[] PBaySmall = { "{A} Cove", "the {A} Inlet", "{R} Cove", "{A} Bay" };
        private static readonly string[] PBay = { "{A} Bay", "the Bay of {O}", "the {A} Sound", "{R} Bay", "{A} Bay" };
        private static readonly string[] PBayLarge = { "the {A} Gulf", "the Gulf of {O}", "the Gulf of {R}", "{A} Bay" };
        private static readonly string[] PLakeTiny = { "{A} Pool", "{A} Tarn", "{A} Mere" };
        private static readonly string[] PLake = { "{A} Mere", "Lake {R}", "the {A} Lake", "{A} Water", "Lake {R}" };
        private static readonly string[] PLakeLarge = { "Lake {R}", "the {A} Lake", "the {A} Mere", "Lake {R}", "the Lake of {O}" };
        private static readonly string[] PMountainSmall = { "the {A} Crags", "the {A} Tors", "the {A} Fangs", "the {A} Horns", "the {A} Peaks" };
        private static readonly string[] PMountain =
        {
            "the {A} Peaks", "the {A} Mountains", "the {A} Spires", "the {R} Range", "the {A} Teeth", "the {A} Heights",
            "the Crown of {O}", "the {R} Mountains"
        };
        private static readonly string[] PHills = { "the {A} Hills", "the {A} Downs", "the {A} Knolls", "the {A} Wolds", "the {A} Barrows", "the {A} Fells", "the {R} Hills" };
        private static readonly string[] PHillsLarge = { "the {A} Highlands", "the {A} Hills", "the {A} Downs", "the {A} Wolds", "the {R} Highlands" };
        private static readonly string[] PForestSmall = { "the {A} Thicket", "the {A} Holt", "the {A} Grove", "the {A} Copse" };
        private static readonly string[] PForest = { "the {A} Wood", "the {A} Wilds", "the {A} Forest", "the {A} Weald", "the {A} Greenwood", "the {R} Woods", "the Forest of {O}" };
        private static readonly string[] PJungle = { "the {A} Jungle", "the {A} Tangle", "the {A} Wilds", "the {A} Canopy", "the {A} Rainwood", "the {R} Jungle" };
        private static readonly string[] PTaiga = { "the {A} Pinewood", "the {A} Taiga", "the {A} Firs", "the {A} Pines", "the {A} Timberland", "the {R} Pinewood" };
        private static readonly string[] PDesert = { "the {A} Expanse", "the {A} Wastes", "the {A} Sands", "the {A} Dunes", "the {A} Barrens", "the Desert of {O}", "the {R} Desert" };
        private static readonly string[] PSwamp = { "the {A} Fen", "the {A} Mire", "the {A} Bog", "the {A} Morass", "the {A} Marshes", "the {A} Swamp", "the {R} Fens" };
        private static readonly string[] PMarsh = { "the {A} Marsh", "the {A} Fen", "the {A} Reeds", "the {A} Sloughs", "the {A} Bog" };
        private static readonly string[] PTundra = { "the {A} Tundra", "the {A} Barrens", "the {A} Frostlands", "the {A} Wastes", "the {A} Flats", "the {R} Tundra" };
        private static readonly string[] PIce = { "the {A} Icefields", "the {A} Glacier", "the {A} Snows", "the {A} Wastes" };
        private static readonly string[] PPlains = { "the {A} Plains", "the {A} Meadows", "the {A} Fields", "the {A} Prairie", "the {A} Grasslands", "the {A} Vale", "the {R} Plains" };
        private static readonly string[] PSavanna = { "the {A} Savanna", "the {A} Veld", "the {A} Steppe", "the {A} Plains", "the {R} Steppe" };
        private static readonly string[] PVolcano = { "Mount {R}", "the {A} Caldera", "the {A} Forge", "the {A} Cinderlands", "the {A} Furnace", "Mount {R}" };
        private static readonly string[] PRiverSmall = { "{A} Brook", "{A} Creek", "the {A} Stream", "the {A} Run", "{R} Brook" };
        private static readonly string[] PRiver = { "the {A} River", "the River {R}", "the {R}", "the {A} Water", "the River {R}" };
        private static readonly string[] PRiverLarge = { "the River {R}", "the Great {R}", "the {R} River", "the {A} River" };

        // Native (race-language) patterns: {R} is built from the race's NameParts syllables.
        private static readonly string[][] PNative =
        {
            new[] { "{R}", "{R}", "the {R} Lands" },            // Continent
            new[] { "{R}", "the Isle of {R}", "{R} Isle" },         // Island
            new[] { "the {R} Ocean", "the Ocean of {R}" },      // Ocean
            new[] { "the {R} Sea", "the Sea of {R}" },          // Sea
            new[] { "{R} Bay", "the Bay of {R}" },              // Bay
            new[] { "Lake {R}", "{R} Mere" },                   // Lake
            new[] { "the {R} Peaks", "the {R} Mountains", "the {R} Range" }, // MountainRange
            new[] { "the {R} Hills", "the {R} Downs" },         // Hills
            new[] { "the {R} Woods", "the {R} Forest" },        // Forest
            new[] { "the {R} Jungle", "the {R} Wilds" },        // Jungle
            new[] { "the {R} Pinewood", "the {R} Taiga" },      // Taiga
            new[] { "the {R} Sands", "the {R} Desert" },        // Desert
            new[] { "the {R} Fens", "the {R} Mire" },           // Swamp
            new[] { "the {R} Marsh", "the {R} Reeds" },         // Marsh
            new[] { "the {R} Tundra", "the {R} Barrens" },      // Tundra
            new[] { "the River {R}", "the {R}" },               // River
            new[] { "the {R} Plains", "the {R} Steppe" },       // Plains
            new[] { "Mount {R}", "the {R} Caldera" },           // Volcano
        };

        // ------------------------------------------------------------------ public API

        /// <summary>Per-region naming seed: <c>Combine(Combine(worldSeed, centroid), kind)</c>. Stable across rebuilds and saves.</summary>
        public static ulong RegionSeed(ulong worldSeed, Vec2i centroid, RegionKind kind)
        {
            ulong packed = ((ulong)(uint)centroid.X << 32) | (uint)centroid.Y;
            return Hashing.Combine(Hashing.Combine(worldSeed, packed), (ulong)kind + 1UL);
        }

        /// <summary>
        /// Composes one candidate phrase (with article, e.g. "the Ashen Peaks", "Lake Orneth", "Northreach").
        /// Pure: depends only on the arguments and the Rng state. <paramref name="northSouth"/>/<paramref name="eastWest"/>
        /// (-1, 0, 1) hint the region's position on the map for compound continent names.
        /// </summary>
        public static string Compose(RegionKind kind, int tileCount, float latitude, Biome dominantBiome, Rng rng, int northSouth = 0, int eastWest = 0)
        {
            if (rng == null) rng = new Rng(Hashing.Combine((ulong)kind, (ulong)tileCount));
            bool cold = latitude >= 0.72f || dominantBiome == Biome.Snow || dominantBiome == Biome.Tundra || dominantBiome == Biome.Taiga;
            bool hot = !cold && (dominantBiome == Biome.Desert || dominantBiome == Biome.Volcanic ||
                                 (latitude <= 0.3f && (dominantBiome == Biome.Jungle || dominantBiome == Biome.Savanna || dominantBiome == Biome.Beach)));
            string[] patterns = PatternsFor(kind, tileCount, dominantBiome);
            string phrase = null;
            for (int t = 0; t < 4; t++)
            {
                phrase = Expand(patterns[rng.Range(0, patterns.Length)], kind, cold, hot, northSouth, eastWest, null, rng);
                if (!HasRepeatedStem(phrase)) break;
            }
            return phrase;
        }

        /// <summary>True when two words share their first 5 letters ("Frostfell Frostlands"), which reads badly.</summary>
        private static bool HasRepeatedStem(string s)
        {
            for (int a = 0; a < s.Length; a++)
            {
                if (!IsWordStart(s, a)) continue;
                for (int b = a + 1; b < s.Length; b++)
                {
                    if (!IsWordStart(s, b)) continue;
                    int k = 0;
                    while (k < 5 && b + k < s.Length && s[a + k] == s[b + k] && s[a + k] != ' ') k++;
                    if (k >= 5) return true;
                }
            }
            return false;
        }

        private static bool IsWordStart(string s, int i) => char.IsUpper(s[i]) && (i == 0 || s[i - 1] == ' ');

        /// <summary>
        /// Gives a region a native-language name built from the race's <see cref="NameParts"/> syllables (generic
        /// syllables when <paramref name="race"/> is null), keeps it unique within the index, applies it to the region
        /// and returns the new <see cref="GeoRegion.Name"/>. With a null <paramref name="rng"/> the result is seeded by
        /// (region seed, race) and therefore reproducible after a rebuild. Returns null for an unknown region.
        /// Renames are not persisted: re-run Rename after loading a save if they must survive.
        /// </summary>
        public static string Rename(GeographyIndex idx, int regionId, RaceDefinition race, Rng rng)
        {
            var r = idx?.Get(regionId);
            if (r == null) return null;
            if (rng == null) rng = new Rng(Hashing.Combine(r.NameSeed, race != null ? (ulong)race.Id + 1UL : 0UL));
            string[] patterns = PNative[(int)r.Kind];
            if (r.Kind == RegionKind.Island && r.TileCount <= 8) patterns = new[] { "{R} Rock", "{R} Skerry" };
            string first = null;
            for (int a = 0; a < Attempts; a++)
            {
                string cand = Expand(patterns[rng.Range(0, patterns.Length)], r.Kind, false, false, 0, 0, race, rng);
                if (!Fits(cand)) continue;
                if (first == null) first = cand;
                if (!idx.IsNameTaken(StripArticle(cand), r.Id)) { Apply(idx, r, cand, race); return r.Name; }
            }
            Apply(idx, r, Dedupe(idx, r, first ?? "the " + KindWord(r.Kind), rng), race);
            return r.Name;
        }

        /// <summary>Removes a leading "the " (any case).</summary>
        public static string StripArticle(string phrase)
        {
            if (phrase == null) return "";
            if (phrase.Length > 4 && phrase.StartsWith("the ", StringComparison.OrdinalIgnoreCase)) return phrase.Substring(4);
            return phrase;
        }

        /// <summary>Generic English word for a kind ("Peaks", "Lake", ...); used for fallbacks and UI legends.</summary>
        public static string KindWord(RegionKind kind)
        {
            switch (kind)
            {
                case RegionKind.Continent: return "Land";
                case RegionKind.Island: return "Isle";
                case RegionKind.Ocean: return "Ocean";
                case RegionKind.Sea: return "Sea";
                case RegionKind.Bay: return "Bay";
                case RegionKind.Lake: return "Lake";
                case RegionKind.MountainRange: return "Peaks";
                case RegionKind.Hills: return "Hills";
                case RegionKind.Forest: return "Wood";
                case RegionKind.Jungle: return "Jungle";
                case RegionKind.Taiga: return "Pinewood";
                case RegionKind.Desert: return "Wastes";
                case RegionKind.Swamp: return "Fen";
                case RegionKind.Marsh: return "Marsh";
                case RegionKind.Tundra: return "Tundra";
                case RegionKind.River: return "River";
                case RegionKind.Plains: return "Plains";
                case RegionKind.Volcano: return "Caldera";
                default: return "Land";
            }
        }

        // ------------------------------------------------------------------ index naming

        /// <summary>Names every region of a freshly built index, biggest first, so large regions keep their first choice.</summary>
        internal static void AssignAll(GeographyIndex idx)
        {
            int count = idx.Regions.Count;
            var order = new GeoRegion[count];
            for (int i = 0; i < count; i++) order[i] = idx.Regions[i];
            Array.Sort(order, CompareNamingPriority);
            for (int k = 0; k < count; k++)
            {
                var r = order[k];
                var rng = new Rng(r.NameSeed);
                int ns = r.Centroid.Y > idx.Height * 0.62f ? 1 : (r.Centroid.Y < idx.Height * 0.38f ? -1 : 0);
                int ew = r.Centroid.X > idx.Width * 0.62f ? 1 : (r.Centroid.X < idx.Width * 0.38f ? -1 : 0);
                string chosen = null, first = null;
                for (int a = 0; a < Attempts; a++)
                {
                    string cand = Compose(r.Kind, r.TileCount, r.Latitude, r.DominantBiome, rng, ns, ew);
                    if (!Fits(cand)) continue;
                    if (first == null) first = cand;
                    if (!idx.IsNameTaken(StripArticle(cand), r.Id)) { chosen = cand; break; }
                }
                if (chosen == null) chosen = Dedupe(idx, r, first ?? "the " + KindWord(r.Kind), rng);
                idx.SetName(r, chosen);
            }
        }

        /// <summary>Size first, then position and kind: independent of region ids, so unchanged regions keep their names.</summary>
        private static int CompareNamingPriority(GeoRegion a, GeoRegion b)
        {
            int c = b.TileCount.CompareTo(a.TileCount);
            if (c != 0) return c;
            c = a.Centroid.Y.CompareTo(b.Centroid.Y);
            if (c != 0) return c;
            c = a.Centroid.X.CompareTo(b.Centroid.X);
            if (c != 0) return c;
            c = ((int)a.Kind).CompareTo((int)b.Kind);
            return c != 0 ? c : a.Id.CompareTo(b.Id);
        }

        private static void Apply(GeographyIndex idx, GeoRegion r, string phrase, RaceDefinition race)
        {
            idx.SetName(r, phrase);
            r.NativeRace = race != null ? (int)race.Id : -1;
            r.Renamed = true;
        }

        private static bool Fits(string phrase) => !string.IsNullOrEmpty(phrase) && phrase.Length <= MaxLength && StripArticle(phrase).Length > 0;

        /// <summary>Alternates for a colliding name: qualifiers ("the Lesser Ashen Peaks"), then numerals, then kind + id.</summary>
        private static string Dedupe(GeographyIndex idx, GeoRegion r, string first, Rng rng)
        {
            int start = rng.Range(0, Qualifiers.Length);
            for (int k = 0; k < Qualifiers.Length; k++)
            {
                string cand = Qualify(first, Qualifiers[(start + k) % Qualifiers.Length]);
                if (Fits(cand) && !idx.IsNameTaken(StripArticle(cand), r.Id)) return cand;
            }
            int limit = idx.Regions.Count + 2;
            for (int k = 2; k <= limit; k++)
            {
                string cand = first + " " + Roman(k);
                if (!Fits(cand)) break;
                if (!idx.IsNameTaken(StripArticle(cand), r.Id)) return cand;
            }
            return KindWord(r.Kind) + " " + r.Id; // ids are unique; generated names never end in a bare number
        }

        private static string Qualify(string phrase, string q) =>
            phrase.StartsWith("the ", StringComparison.Ordinal) ? "the " + q + " " + phrase.Substring(4) : q + " " + phrase;

        private static string Roman(int v)
        {
            if (v <= 0 || v >= 4000) return v.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int[] vals = { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
            string[] syms = { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
            var sb = new StringBuilder();
            for (int i = 0; i < vals.Length; i++) while (v >= vals[i]) { sb.Append(syms[i]); v -= vals[i]; }
            return sb.ToString();
        }

        // ------------------------------------------------------------------ composition internals

        private static string[] PatternsFor(RegionKind kind, int size, Biome dominant)
        {
            switch (kind)
            {
                case RegionKind.Continent: return PContinent;
                case RegionKind.Island: return size <= 8 ? PIslandTiny : (size <= 80 ? PIslandSmall : PIslandLarge);
                case RegionKind.Ocean: return POcean;
                case RegionKind.Sea: return PSea;
                case RegionKind.Bay: return size < 20 ? PBaySmall : (size < 150 ? PBay : PBayLarge);
                case RegionKind.Lake: return size < 6 ? PLakeTiny : (size < 120 ? PLake : PLakeLarge);
                case RegionKind.MountainRange: return size < 30 ? PMountainSmall : PMountain;
                case RegionKind.Hills: return size >= 120 ? PHillsLarge : PHills;
                case RegionKind.Forest: return size < 30 ? PForestSmall : PForest;
                case RegionKind.Jungle: return PJungle;
                case RegionKind.Taiga: return PTaiga;
                case RegionKind.Desert: return PDesert;
                case RegionKind.Swamp: return PSwamp;
                case RegionKind.Marsh: return PMarsh;
                case RegionKind.Tundra: return dominant == Biome.Snow ? PIce : PTundra;
                case RegionKind.River: return size < 15 ? PRiverSmall : (size < 80 ? PRiver : PRiverLarge);
                case RegionKind.Plains: return dominant == Biome.Savanna ? PSavanna : PPlains;
                case RegionKind.Volcano: return PVolcano;
                default: return PContinent;
            }
        }

        private static string[] KindAdjectives(RegionKind kind)
        {
            switch (kind)
            {
                case RegionKind.MountainRange: return AdjMountain;
                case RegionKind.Hills: return AdjHills;
                case RegionKind.Forest: case RegionKind.Jungle: case RegionKind.Taiga: return AdjWood;
                case RegionKind.Desert: return AdjDesert;
                case RegionKind.Swamp: case RegionKind.Marsh: return AdjWet;
                case RegionKind.Ocean: case RegionKind.Sea: case RegionKind.Bay: case RegionKind.Lake: return AdjWater;
                case RegionKind.Plains: return AdjPlains;
                case RegionKind.Volcano: return AdjVolcano;
                case RegionKind.River: return AdjRiver;
                case RegionKind.Tundra: return AdjCold;
                default: return null;
            }
        }

        private static string Adjective(RegionKind kind, bool cold, bool hot, Rng rng)
        {
            int roll = rng.Range(0, 10);
            var own = KindAdjectives(kind);
            string[] table;
            if (own != null && roll < 5) table = own;
            else if (cold && roll < 8) table = AdjCold;
            else if (hot && roll < 8) table = AdjHot;
            else table = AdjCommon;
            return table[rng.Range(0, table.Length)];
        }

        private static string Expand(string pattern, RegionKind kind, bool cold, bool hot, int ns, int ew, RaceDefinition race, Rng rng)
        {
            var sb = new StringBuilder(MaxLength + 8);
            for (int i = 0; i < pattern.Length; i++)
            {
                char ch = pattern[i];
                if (ch != '{' || i + 2 >= pattern.Length || pattern[i + 2] != '}') { sb.Append(ch); continue; }
                char token = pattern[i + 1];
                i += 2;
                switch (token)
                {
                    case 'A': sb.Append(Adjective(kind, cold, hot, rng)); break;
                    case 'O':
                        var of = kind.IsWaterKind() ? OfWater : OfLand;
                        sb.Append(of[rng.Range(0, of.Length)]);
                        break;
                    case 'C': sb.Append(Compound(cold, hot, ns, ew, rng)); break;
                    default: sb.Append(race != null ? NativeRoot(race, rng) : Root(rng)); break; // 'R'
                }
            }
            return sb.ToString();
        }

        private static string Root(Rng rng) =>
            Capitalize(RootPre[rng.Range(0, RootPre.Length)] + RootSuf[rng.Range(0, RootSuf.Length)]);

        private static string Compound(bool cold, bool hot, int ns, int ew, Rng rng)
        {
            string pre;
            int roll = rng.Range(0, 10);
            if (roll < 3 && (ns != 0 || ew != 0))
            {
                bool useNs = ns != 0 && (ew == 0 || rng.Range(0, 2) == 0);
                pre = useNs ? (ns > 0 ? "North" : "South") : (ew > 0 ? "East" : "West");
            }
            else if (roll < 6 && cold) pre = CompoundPreCold[rng.Range(0, CompoundPreCold.Length)];
            else if (roll < 6 && hot) pre = CompoundPreHot[rng.Range(0, CompoundPreHot.Length)];
            else pre = CompoundPre[rng.Range(0, CompoundPre.Length)];
            return pre + CompoundSuf[rng.Range(0, CompoundSuf.Length)];
        }

        private static string NativeRoot(RaceDefinition race, Rng rng)
        {
            var n = race.Names;
            string[] pre = null, suf = null;
            if (n != null)
            {
                bool hasSet = n.SettlementPrefixes != null && n.SettlementPrefixes.Length > 0;
                bool hasPer = n.PersonPrefixes != null && n.PersonPrefixes.Length > 0;
                pre = hasSet && (!hasPer || rng.Range(0, 2) == 0) ? n.SettlementPrefixes : (hasPer ? n.PersonPrefixes : null);
                bool hasSetS = n.SettlementSuffixes != null && n.SettlementSuffixes.Length > 0;
                string[] personSuf = rng.Range(0, 2) == 0 ? n.MaleSuffixes : n.FemaleSuffixes;
                bool hasPerS = personSuf != null && personSuf.Length > 0;
                suf = hasSetS && (!hasPerS || rng.Range(0, 2) == 0) ? n.SettlementSuffixes : (hasPerS ? personSuf : null);
            }
            if (pre == null) pre = RootPre;
            if (suf == null) suf = RootSuf;
            string root = Capitalize((pre[rng.Range(0, pre.Length)] ?? "") + (suf[rng.Range(0, suf.Length)] ?? "")).Trim();
            if (root.Length > 18) root = root.Substring(0, 18);
            return root.Length == 0 ? Root(rng) : root;
        }

        private static string Capitalize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder(s.ToLowerInvariant());
            sb[0] = char.ToUpperInvariant(sb[0]);
            return sb.ToString();
        }
    }
}
