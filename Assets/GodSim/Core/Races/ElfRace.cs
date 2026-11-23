namespace GodSim.Core.Races
{
    /// <summary>
    /// Elves: long-lived, slow-breeding scholars and priests of forest and jungle. Peaceful but
    /// xenophobic; organic, widely spaced groves with few roads; living-wood halls built up in the trees.
    /// </summary>
    public static class ElfRace
    {
        /// <summary>Creates a new, fully populated Elf definition (fresh arrays on every call).</summary>
        public static RaceDefinition Create()
        {
            var r = new RaceDefinition
            {
                Id = RaceId.Elf,
                Name = "Elf",
                PluralName = "Elves",
                Adjective = "Elven",
                Description = RaceLore.Summary(RaceId.Elf),
                BannerColor = 0x2FA84FFFu, // leaf green
                SkinColor = 0xF0DEC4FFu,   // pale ivory

                // Life cycle: very long lives, very few children.
                AdulthoodAge = 30f,       // balance pass 2026-11-19, was 40: 40 years of child mortality left ~1.1 surviving children per mother.
                ElderAge = 280f,
                LifeExpectancy = 350f,
                FertilityPerYear = 0.085f, // was 0.05: elves stalled at ~20-60 people for 200 years.
                MaxChildren = 5,          // was 3

                // Physiology & aptitudes: research, faith and health; frugal eaters, slow builders.
                FoodPerTick = 0.04f,
                BaseHealth = 1.2f,
                DiseaseResistance = 1.3f,
                BuildSpeed = 0.85f,
                ResearchRate = 1.3f,
                FaithRate = 1.25f,
                MilitaryStrength = 0.9f,
                TradeAptitude = 0.85f,
                MoveSpeed = 1.1f,

                // AI personality: peaceful, curious, devout and the most wary of outsiders.
                Aggression = 0.2f,
                Expansionism = 0.3f,
                Piety = 0.75f,
                Industriousness = 0.45f,
                Curiosity = 0.85f,
                Diplomacy = 0.5f,
                Xenophobia = 0.75f,
                Mercantilism = 0.3f,

                // Layout: scattered groves far apart, winding paths instead of roads.
                MinSettlementSpacing = 18,
                Compactness = 0.25f,
                RoadAffinity = 0.1f,
            };

            var h = new float[Counts.Biomes];
            h[(int)Biome.DeepOcean] = 0f;
            h[(int)Biome.ShallowWater] = 0f;
            h[(int)Biome.Lake] = 0f;
            h[(int)Biome.Beach] = 0.3f;
            h[(int)Biome.Grassland] = 0.7f;
            h[(int)Biome.Savanna] = 0.3f;
            h[(int)Biome.Forest] = 1.0f;
            h[(int)Biome.Jungle] = 0.8f;
            h[(int)Biome.Desert] = 0.05f;
            h[(int)Biome.Swamp] = 0.4f;
            h[(int)Biome.Taiga] = 0.6f;
            h[(int)Biome.Tundra] = 0.15f;
            h[(int)Biome.Hills] = 0.45f;
            h[(int)Biome.Mountain] = 0.15f;
            h[(int)Biome.Snow] = 0.05f;
            h[(int)Biome.Volcanic] = 0.02f;
            r.BiomeHabitability = h;

            var p = RaceDefinition.Fill(Counts.Resources, 1f);
            p[(int)ResourceType.Food] = 1.1f;
            p[(int)ResourceType.Wood] = 1.3f;
            p[(int)ResourceType.Stone] = 0.85f;
            p[(int)ResourceType.Ore] = 0.7f;
            p[(int)ResourceType.Metal] = 0.8f;
            p[(int)ResourceType.Gold] = 0.9f;
            p[(int)ResourceType.Tools] = 0.9f;
            p[(int)ResourceType.Cloth] = 1.2f;
            p[(int)ResourceType.Goods] = 1.05f;
            p[(int)ResourceType.Weapons] = 0.85f;
            p[(int)ResourceType.Fuel] = 0.8f;
            p[(int)ResourceType.Machinery] = 0.8f;
            p[(int)ResourceType.Electronics] = 1.1f;
            r.ProductionMultiplier = p;

            var a = new float[Counts.Races];
            a[(int)RaceId.Human] = 0.15f;
            a[(int)RaceId.Elf] = 1.0f;
            a[(int)RaceId.Dwarf] = -0.3f;
            a[(int)RaceId.Orc] = -0.6f;
            a[(int)RaceId.Saurian] = 0.05f;
            r.RaceAffinity = a;

            r.Architecture = new ArchitectureStyle
            {
                Roof = RoofStyle.Leaf,
                HeightScale = 1.3f,
                WidthScale = 0.85f,
                Roundness = 0.85f,
                BuildsUnderground = false,
                BuildsInTrees = true,
                BuildsOnStilts = false,
                WallColor = 0xE9E4CFFFu,   // pale birch
                RoofColor = 0x4E8F3AFFu,   // living leaf
                TrimColor = 0xA8894AFFu,   // amber bark
                AccentColor = 0xBFD8E6FFu, // moon silver
                StyleTag = "living-wood",
            };

            r.Names = new NameParts
            {
                PersonPrefixes = new[]
                {
                    "ael", "cyrel", "elow", "fael", "ilv", "laer", "lior", "mael", "nael", "orin", "rhil", "sael",
                    "thael", "uval", "yl", "ser", "eir", "talan", "sil", "naer", "aun", "evan", "vael", "lum",
                },
                MaleSuffixes = new[]
                {
                    "rion", "aris", "nir", "an", "ius", "en", "dhel", "ar", "ith", "valen", "oth", "is", "iden", "orn",
                },
                FemaleSuffixes = new[]
                {
                    "wen", "iel", "ia", "sha", "ria", "ae", "thiel", "isse", "ara", "lune", "yra", "esti", "aya", "nia",
                },
                FamilyNames = new[]
                {
                    "Aelvaris", "Cyrelinth", "Elowmire", "Faelorin", "Ilvanthe", "Laerisse", "Liorvane", "Maelith",
                    "Naelyndra", "Orinvael", "Rhilaeth", "Saelvorn", "Thaelune", "Uvalis", "Ylvaren", "Seraviel",
                    "Eirlinde", "Talavyn", "Silvaine", "Naerith", "Aunvelis", "Evandril", "Vaelora", "Lumiren",
                },
                SettlementPrefixes = new[]
                {
                    "aelin", "cirra", "elar", "fenel", "galen", "ilmar", "lauren", "lisse", "mirae", "nimra", "olvar", "rhovan",
                    "sylv", "thalor", "ulve", "valin", "yvae", "seren", "amra", "ovel", "taelin", "ithra", "elys", "solae",
                },
                SettlementSuffixes = new[]
                {
                    "dell", "wynd", "vale", "endra", "iel", "anthe", "losse", "aleth", "elis",
                    "ora", "yndel", "lin", "issar", "evel", "ion", "ynn", "aira", "thel",
                },
                CivilizationPatterns = new[]
                {
                    "{0} Conclave", "Court of {0}", "{0} Greenward", "Circle of {0}",
                    "{0} Sylvan Accord", "Children of {0}", "{0} Choir", "Houses of {0}",
                },
            };

            r.UniqueBuildings = new[] { BuildingType.WorldTree };
            r.FavoredBuildings = new[]
            {
                BuildingType.Library, BuildingType.Temple, BuildingType.Herbalist, BuildingType.WoodcutterCamp, BuildingType.Observatory,
            };
            return r;
        }
    }
}
