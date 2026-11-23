namespace GodSim.Core.Races
{
    /// <summary>
    /// Orcs: short-lived, hyper-fertile, hardy warriors of savanna, tundra, desert and hills. Aggressive
    /// expansionists; sprawling clan-camps with few roads; hide-and-timber lodges under spiked cone roofs.
    /// </summary>
    public static class OrcRace
    {
        /// <summary>Creates a new, fully populated Orc definition (fresh arrays on every call).</summary>
        public static RaceDefinition Create()
        {
            var r = new RaceDefinition
            {
                Id = RaceId.Orc,
                Name = "Orc",
                PluralName = "Orcs",
                Adjective = "Orcish",
                Description = RaceLore.Summary(RaceId.Orc),
                BannerColor = 0xB3261EFFu, // war red
                SkinColor = 0x6F8A3AFFu,   // olive green

                // Life cycle: grow up fast, die young, breed prolifically.
                AdulthoodAge = 12f,
                ElderAge = 38f,
                LifeExpectancy = 50f,
                FertilityPerYear = 0.22f, // balance pass 2026-11-19, was 0.45: orcs reached ~10k by year 100 (target 60-400).
                MaxChildren = 5,          // was 8

                // Physiology & aptitudes: hungry, tough and the strongest fighters; poor scholars and traders.
                FoodPerTick = 0.06f,
                BaseHealth = 1.25f,
                DiseaseResistance = 1.25f,
                BuildSpeed = 0.95f,
                ResearchRate = 0.75f,
                FaithRate = 0.9f,
                MilitaryStrength = 1.4f,
                TradeAptitude = 0.7f,
                MoveSpeed = 1.1f,

                // AI personality: the most aggressive and expansionist; little patience for diplomacy.
                Aggression = 0.9f,
                Expansionism = 0.85f,
                Piety = 0.4f,
                Industriousness = 0.5f,
                Curiosity = 0.3f,
                Diplomacy = 0.2f,
                Xenophobia = 0.6f,
                Mercantilism = 0.25f,

                // Layout: loose, sprawling camps planted close together.
                MinSettlementSpacing = 12,
                Compactness = 0.2f,
                RoadAffinity = 0.2f,
            };

            var h = new float[Counts.Biomes];
            h[(int)Biome.DeepOcean] = 0f;
            h[(int)Biome.ShallowWater] = 0f;
            h[(int)Biome.Lake] = 0f;
            h[(int)Biome.Beach] = 0.3f;
            h[(int)Biome.Grassland] = 0.65f;
            h[(int)Biome.Savanna] = 1.0f;
            h[(int)Biome.Forest] = 0.4f;
            h[(int)Biome.Jungle] = 0.3f;
            h[(int)Biome.Desert] = 0.6f;
            h[(int)Biome.Swamp] = 0.35f;
            h[(int)Biome.Taiga] = 0.5f;
            h[(int)Biome.Tundra] = 0.7f;
            h[(int)Biome.Hills] = 0.8f;
            h[(int)Biome.Mountain] = 0.35f;
            h[(int)Biome.Snow] = 0.2f;
            h[(int)Biome.Volcanic] = 0.4f;
            r.BiomeHabitability = h;

            var p = RaceDefinition.Fill(Counts.Resources, 1f);
            p[(int)ResourceType.Food] = 0.9f;
            p[(int)ResourceType.Wood] = 1.05f;
            p[(int)ResourceType.Stone] = 1.0f;
            p[(int)ResourceType.Ore] = 1.1f;
            p[(int)ResourceType.Metal] = 1.0f;
            p[(int)ResourceType.Gold] = 0.85f;
            p[(int)ResourceType.Tools] = 0.95f;
            p[(int)ResourceType.Cloth] = 0.85f;
            p[(int)ResourceType.Goods] = 0.8f;
            p[(int)ResourceType.Weapons] = 1.4f;
            p[(int)ResourceType.Fuel] = 1.05f;
            p[(int)ResourceType.Machinery] = 0.9f;
            p[(int)ResourceType.Electronics] = 0.75f;
            r.ProductionMultiplier = p;

            var a = new float[Counts.Races];
            a[(int)RaceId.Human] = -0.25f;
            a[(int)RaceId.Elf] = -0.6f;
            a[(int)RaceId.Dwarf] = -0.4f;
            a[(int)RaceId.Orc] = 1.0f;
            a[(int)RaceId.Saurian] = -0.1f;
            r.RaceAffinity = a;

            r.Architecture = new ArchitectureStyle
            {
                Roof = RoofStyle.Cone,
                HeightScale = 0.9f,
                WidthScale = 1.1f,
                Roundness = 0.6f,
                BuildsUnderground = false,
                BuildsInTrees = false,
                BuildsOnStilts = false,
                WallColor = 0x8A6A48FFu,   // tanned hide
                RoofColor = 0x5B4530FFu,   // smoked hide
                TrimColor = 0xE6D9BCFFu,   // bone
                AccentColor = 0xB3261EFFu, // war red
                StyleTag = "hide-and-timber",
            };

            r.Names = new NameParts
            {
                PersonPrefixes = new[]
                {
                    "agh", "brug", "dhak", "gharz", "ghor", "grak", "hruk", "jag", "krug", "lug", "mog", "og",
                    "rukh", "shug", "thrag", "vrak", "zug", "barg", "drub", "gruz", "kaz", "murg", "orsk", "yagh",
                },
                MaleSuffixes = new[]
                {
                    "ash", "ok", "uk", "gar", "dush", "nak", "rog", "tuk", "ar", "uz", "mok", "zag", "brak", "rum",
                },
                FemaleSuffixes = new[]
                {
                    "a", "ga", "ra", "sha", "ka", "ush", "gra", "zha", "na", "ukka", "hra", "rha", "ruga", "dza",
                },
                FamilyNames = new[]
                {
                    "Agrakk", "Brugzah", "Ghorbash", "Grakthar", "Hrukmog", "Jagrush", "Krugdakk", "Lugdrakk",
                    "Mogzak", "Ogrukk", "Rukhtar", "Shugrah", "Thragnash", "Vrakkul", "Zugmar", "Bargrash",
                    "Drubnak", "Gruzhak", "Kazgor", "Murgdush", "Orskar", "Yaghruk", "Gharzok", "Dhakmorr",
                },
                SettlementPrefixes = new[]
                {
                    "bagh", "drog", "gash", "grul", "khar", "mug", "nag", "rakh", "skur", "tharg", "urz", "zhak",
                    "brag", "ghul", "krak", "lurg", "mozg", "ogr", "ruz", "shak", "thok", "ulg", "yarg", "dhur",
                },
                SettlementSuffixes = new[]
                {
                    "gor", "mash", "dush", "rok", "zag", "ugh", "grut", "tuk", "nar",
                    "drok", "muk", "thar", "akk", "rash", "zog", "uz", "krul", "ghast",
                },
                CivilizationPatterns = new[]
                {
                    "{0} Horde", "Warclans of {0}", "{0} Blood-Host", "Great Khanate of {0}",
                    "{0} Skullbanner", "Iron Tusks of {0}", "{0} Warband", "Free Clans of {0}",
                },
            };

            r.UniqueBuildings = new[] { BuildingType.WarIdol };
            r.FavoredBuildings = new[]
            {
                BuildingType.Barracks, BuildingType.Armory, BuildingType.HuntingLodge, BuildingType.Watchtower, BuildingType.Arena,
            };
            return r;
        }
    }
}
