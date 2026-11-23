namespace GodSim.Core.Races
{
    /// <summary>
    /// Humans: balanced, fertile and mercantile settlers of grassland, forest and hills. Grid-like towns
    /// with good roads; timber-frame houses under gable roofs that turn to stone, brick and glass with the ages.
    /// </summary>
    public static class HumanRace
    {
        /// <summary>Creates a new, fully populated Human definition (fresh arrays on every call).</summary>
        public static RaceDefinition Create()
        {
            var r = new RaceDefinition
            {
                Id = RaceId.Human,
                Name = "Human",
                PluralName = "Humans",
                Adjective = "Human",
                Description = RaceLore.Summary(RaceId.Human),
                BannerColor = 0x3B6FD8FFu, // royal blue
                SkinColor = 0xD9A27EFFu,   // warm tan

                // Life cycle: the baseline race. Short-lived but fertile.
                AdulthoodAge = 16f,
                ElderAge = 55f,
                LifeExpectancy = 70f,
                FertilityPerYear = 0.21f, // balance pass 2026-11-19, was 0.30: humans reached 1,300-4,600 by year 100 (target 60-400).
                MaxChildren = 4,          // was 6

                // Physiology & aptitudes: balanced, with a clear edge in trade.
                FoodPerTick = 0.05f,
                BaseHealth = 1.0f,
                DiseaseResistance = 0.95f, // crowded towns
                BuildSpeed = 1.05f,
                ResearchRate = 1.0f,
                FaithRate = 1.0f,
                MilitaryStrength = 1.0f,
                TradeAptitude = 1.25f,
                MoveSpeed = 1.0f,

                // AI personality: moderate in everything, the most mercantile and diplomatic.
                Aggression = 0.45f,
                Expansionism = 0.65f,
                Piety = 0.5f,
                Industriousness = 0.6f,
                Curiosity = 0.6f,
                Diplomacy = 0.7f,
                Xenophobia = 0.2f,
                Mercantilism = 0.8f,

                // Layout: orderly grid towns at a moderate distance.
                MinSettlementSpacing = 14,
                Compactness = 0.55f,
                RoadAffinity = 0.8f,
            };

            var h = new float[Counts.Biomes];
            h[(int)Biome.DeepOcean] = 0f;
            h[(int)Biome.ShallowWater] = 0f;
            h[(int)Biome.Lake] = 0f;
            h[(int)Biome.Beach] = 0.6f;
            h[(int)Biome.Grassland] = 1.0f;
            h[(int)Biome.Savanna] = 0.6f;
            h[(int)Biome.Forest] = 0.8f;
            h[(int)Biome.Jungle] = 0.35f;
            h[(int)Biome.Desert] = 0.25f;
            h[(int)Biome.Swamp] = 0.25f;
            h[(int)Biome.Taiga] = 0.45f;
            h[(int)Biome.Tundra] = 0.2f;
            h[(int)Biome.Hills] = 0.7f;
            h[(int)Biome.Mountain] = 0.2f;
            h[(int)Biome.Snow] = 0.05f;
            h[(int)Biome.Volcanic] = 0.05f;
            r.BiomeHabitability = h;

            var p = RaceDefinition.Fill(Counts.Resources, 1f);
            p[(int)ResourceType.Food] = 1.05f;
            p[(int)ResourceType.Wood] = 1.0f;
            p[(int)ResourceType.Stone] = 1.0f;
            p[(int)ResourceType.Ore] = 1.0f;
            p[(int)ResourceType.Metal] = 1.0f;
            p[(int)ResourceType.Gold] = 1.15f;
            p[(int)ResourceType.Tools] = 1.0f;
            p[(int)ResourceType.Cloth] = 1.1f;
            p[(int)ResourceType.Goods] = 1.15f;
            p[(int)ResourceType.Weapons] = 1.0f;
            p[(int)ResourceType.Fuel] = 1.0f;
            p[(int)ResourceType.Machinery] = 1.05f;
            p[(int)ResourceType.Electronics] = 1.05f;
            r.ProductionMultiplier = p;

            var a = new float[Counts.Races];
            a[(int)RaceId.Human] = 1.0f;
            a[(int)RaceId.Elf] = 0.2f;
            a[(int)RaceId.Dwarf] = 0.35f;
            a[(int)RaceId.Orc] = -0.3f;
            a[(int)RaceId.Saurian] = 0.1f;
            r.RaceAffinity = a;

            r.Architecture = new ArchitectureStyle
            {
                Roof = RoofStyle.Gable,
                HeightScale = 1.0f,
                WidthScale = 1.0f,
                Roundness = 0.1f,
                BuildsUnderground = false,
                BuildsInTrees = false,
                BuildsOnStilts = false,
                WallColor = 0xD8C7A1FFu,   // lime plaster
                RoofColor = 0x8C3B2AFFu,   // red clay tile
                TrimColor = 0x5A3A1EFFu,   // dark oak timber
                AccentColor = 0x3B6FD8FFu, // banner blue
                StyleTag = "timber-frame",
            };

            r.Names = new NameParts
            {
                PersonPrefixes = new[]
                {
                    "aud", "bran", "cor", "dun", "ed", "fal", "gar", "hal", "jas", "kes", "lan", "mar",
                    "nor", "os", "per", "ros", "sel", "tam", "ul", "ver", "wal", "yor", "bel", "hen",
                },
                MaleSuffixes = new[]
                {
                    "ric", "win", "ton", "bert", "mund", "an", "ard", "old", "eth", "ias", "ell", "rin", "dric", "wick",
                },
                FemaleSuffixes = new[]
                {
                    "a", "elle", "wyn", "ine", "issa", "ora", "ia", "ette", "ilde", "ane", "ey", "ina", "eva", "ith",
                },
                FamilyNames = new[]
                {
                    "Ashcombe", "Brackwell", "Coldbrook", "Dunmere", "Eastwold", "Fairholt", "Greywater", "Hollinsby",
                    "Ivesmoor", "Kettlewick", "Larkfield", "Merriden", "Northcote", "Oakhurst", "Pennick", "Quarrington",
                    "Redbarrow", "Saltmarsh", "Thornbury", "Upwell", "Vantress", "Wrenfield", "Yarrowby", "Brightmoor",
                },
                SettlementPrefixes = new[]
                {
                    "ash", "brook", "cold", "elm", "fair", "glen", "hart", "high", "king", "long", "mill", "north",
                    "oak", "red", "salt", "thorn", "west", "wheat", "bram", "fern", "lark", "mead", "wil", "har",
                },
                SettlementSuffixes = new[]
                {
                    "ford", "ton", "bury", "ham", "wick", "field", "stead", "dale", "bridge",
                    "gate", "haven", "mere", "worth", "by", "cross", "moor", "well", "march",
                },
                CivilizationPatterns = new[]
                {
                    "{0} Concord", "Kingdom of {0}", "{0} Commonwealth", "Free Cities of {0}",
                    "{0} Crown", "Republic of {0}", "{0} League", "Duchy of {0}",
                },
            };

            r.UniqueBuildings = new[] { BuildingType.GrandCathedral };
            r.FavoredBuildings = new[]
            {
                BuildingType.Farm, BuildingType.Market, BuildingType.Guildhall, BuildingType.Cathedral, BuildingType.Harbor,
            };
            return r;
        }
    }
}
