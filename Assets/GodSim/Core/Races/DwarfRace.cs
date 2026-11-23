namespace GodSim.Core.Races
{
    /// <summary>
    /// Dwarves: stubborn, industrious miners, smiths and masons of hills and mountains. Dense, well-roaded
    /// holds of carved stone, half-sunk into the ground under flat roofs; strong defenders, slow expanders.
    /// </summary>
    public static class DwarfRace
    {
        /// <summary>Creates a new, fully populated Dwarf definition (fresh arrays on every call).</summary>
        public static RaceDefinition Create()
        {
            var r = new RaceDefinition
            {
                Id = RaceId.Dwarf,
                Name = "Dwarf",
                PluralName = "Dwarves",
                Adjective = "Dwarven",
                Description = RaceLore.Summary(RaceId.Dwarf),
                BannerColor = 0xC8741EFFu, // forge amber
                SkinColor = 0xC07A5AFFu,   // ruddy, weathered

                // Life cycle: long-lived, few children.
                AdulthoodAge = 30f,
                ElderAge = 150f,
                LifeExpectancy = 200f,
                FertilityPerYear = 0.14f, // balance pass 2026-11-19, was 0.10: dwarves stalled at ~20 people (or collapsed) in Dawn/Bronze.
                MaxChildren = 6,          // was 4

                // Physiology & aptitudes: the best builders; sturdy, slow on the march, staunch defenders.
                FoodPerTick = 0.05f,
                BaseHealth = 1.15f,
                DiseaseResistance = 1.15f,
                BuildSpeed = 1.3f,
                ResearchRate = 0.95f,
                FaithRate = 0.9f,
                MilitaryStrength = 1.15f,
                TradeAptitude = 1.05f,
                MoveSpeed = 0.85f,

                // AI personality: the most industrious; stubborn, inward-looking, cautious expanders.
                Aggression = 0.4f,
                Expansionism = 0.35f,
                Piety = 0.45f,
                Industriousness = 0.95f,
                Curiosity = 0.5f,
                Diplomacy = 0.4f,
                Xenophobia = 0.55f,
                Mercantilism = 0.65f,

                // Layout: the densest towns, laid out along tunnels and straight roads.
                MinSettlementSpacing = 16,
                Compactness = 0.85f,
                RoadAffinity = 0.6f,
            };

            var h = new float[Counts.Biomes];
            h[(int)Biome.DeepOcean] = 0f;
            h[(int)Biome.ShallowWater] = 0f;
            h[(int)Biome.Lake] = 0f;
            h[(int)Biome.Beach] = 0.15f;
            h[(int)Biome.Grassland] = 0.45f;
            h[(int)Biome.Savanna] = 0.3f;
            h[(int)Biome.Forest] = 0.4f;
            h[(int)Biome.Jungle] = 0.1f;
            h[(int)Biome.Desert] = 0.3f;
            h[(int)Biome.Swamp] = 0.05f;
            h[(int)Biome.Taiga] = 0.7f;
            h[(int)Biome.Tundra] = 0.5f;
            h[(int)Biome.Hills] = 1.0f;
            h[(int)Biome.Mountain] = 0.9f;
            h[(int)Biome.Snow] = 0.35f;
            h[(int)Biome.Volcanic] = 0.45f; // forge-heat and rich veins
            r.BiomeHabitability = h;

            var p = RaceDefinition.Fill(Counts.Resources, 1f);
            p[(int)ResourceType.Food] = 0.9f;
            p[(int)ResourceType.Wood] = 0.8f;
            p[(int)ResourceType.Stone] = 1.4f;
            p[(int)ResourceType.Ore] = 1.5f;
            p[(int)ResourceType.Metal] = 1.4f;
            p[(int)ResourceType.Gold] = 1.2f;
            p[(int)ResourceType.Tools] = 1.35f;
            p[(int)ResourceType.Cloth] = 0.9f;
            p[(int)ResourceType.Goods] = 1.0f;
            p[(int)ResourceType.Weapons] = 1.2f;
            p[(int)ResourceType.Fuel] = 1.15f;
            p[(int)ResourceType.Machinery] = 1.2f;
            p[(int)ResourceType.Electronics] = 0.95f;
            r.ProductionMultiplier = p;

            var a = new float[Counts.Races];
            a[(int)RaceId.Human] = 0.35f;
            a[(int)RaceId.Elf] = -0.3f;
            a[(int)RaceId.Dwarf] = 1.0f;
            a[(int)RaceId.Orc] = -0.45f; // old grudges run a little deeper on this side
            a[(int)RaceId.Saurian] = 0.05f;
            r.RaceAffinity = a;

            r.Architecture = new ArchitectureStyle
            {
                Roof = RoofStyle.Flat,
                HeightScale = 0.75f,
                WidthScale = 1.25f,
                Roundness = 0.1f,
                BuildsUnderground = true,
                BuildsInTrees = false,
                BuildsOnStilts = false,
                WallColor = 0x7A7268FFu,   // granite
                RoofColor = 0x45403BFFu,   // slate
                TrimColor = 0xB87333FFu,   // copper
                AccentColor = 0xC8741EFFu, // forge amber
                StyleTag = "carved-stone",
            };

            r.Names = new NameParts
            {
                PersonPrefixes = new[]
                {
                    "bod", "brak", "dag", "drum", "gor", "grod", "khel", "kor", "nar", "orm", "rok", "rur", "skar",
                    "tor", "ulf", "vog", "zar", "bruk", "dek", "gund", "hrok", "krag", "torv", "brun", "dolg", "ferg",
                },
                MaleSuffixes = new[]
                {
                    "in", "ar", "ek", "rim", "gar", "rak", "bek", "din", "grum", "ok", "ur", "dal", "gin", "kul",
                },
                FemaleSuffixes = new[]
                {
                    "da", "hild", "ra", "dis", "gret", "una", "ika", "bryn", "rid", "ga", "unn", "dra", "esk", "rka",
                },
                FamilyNames = new[]
                {
                    "Brakkund", "Dolgrim", "Dekkarth", "Grodmar", "Hrokvald", "Khelbrand", "Korrdun", "Kragmund",
                    "Narvok", "Ormgrund", "Rurbek", "Skarrhal", "Torvhelm", "Ulfgrak", "Voggrim", "Zardekk",
                    "Bruckhorn", "Gundbarr", "Dolkhar", "Fergstan", "Brunndal", "Barrok", "Stagdrum", "Thrundek",
                },
                SettlementPrefixes = new[]
                {
                    "brod", "drak", "dunn", "gral", "grun", "hald", "kel", "krog", "orth", "rakk", "skel", "stad",
                    "thrum", "ulm", "vald", "zund", "bekk", "dorn", "gath", "hrim", "kurn", "tarn", "kald", "ost",
                },
                SettlementSuffixes = new[]
                {
                    "hold", "heim", "gard", "dum", "grund", "mar", "delve", "forge", "deep",
                    "dar", "borg", "rak", "thun", "zak", "dun", "vik", "hal", "drum",
                },
                CivilizationPatterns = new[]
                {
                    "{0} Thanedom", "Clans of {0}", "{0} Deepcrown", "Iron Moot of {0}",
                    "Halls of {0}", "{0} Anvil Pact", "Deep Throne of {0}", "{0} Stonebound",
                },
            };

            r.UniqueBuildings = new[] { BuildingType.DeepForge };
            r.FavoredBuildings = new[]
            {
                BuildingType.Mine, BuildingType.StoneQuarry, BuildingType.Smelter, BuildingType.Blacksmith, BuildingType.StoneWall,
            };
            return r;
        }
    }
}
