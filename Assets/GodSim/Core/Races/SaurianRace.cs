namespace GodSim.Core.Races
{
    /// <summary>
    /// Saurians: patient, pious, cold-blooded fishers of hot swamp, jungle and beach who lay large clutches.
    /// Stilted villages over the water crowned by stepped pyramids; moderate roads and density.
    /// </summary>
    public static class SaurianRace
    {
        /// <summary>Creates a new, fully populated Saurian definition (fresh arrays on every call).</summary>
        public static RaceDefinition Create()
        {
            var r = new RaceDefinition
            {
                Id = RaceId.Saurian,
                Name = "Saurian",
                PluralName = "Saurians",
                Adjective = "Saurian",
                Description = RaceLore.Summary(RaceId.Saurian),
                BannerColor = 0x8E44ADFFu, // royal purple
                SkinColor = 0x3E8C76FFu,   // teal scales

                // Life cycle: quick to mature, long-lived for their size; MaxChildren counts eggs over a
                // lifetime of clutches.
                AdulthoodAge = 10f,
                ElderAge = 70f,
                LifeExpectancy = 90f,
                FertilityPerYear = 0.09f, // balance pass 2026-11-19, was 0.32 (x clutch 1-3): runaway growth to 25-39k by year 100.
                MaxChildren = 4,          // was 8

                // Physiology & aptitudes: the most devout; cold-blooded and frugal; patient, not fast.
                FoodPerTick = 0.045f,
                BaseHealth = 1.05f,
                DiseaseResistance = 1.1f,
                BuildSpeed = 1.0f,
                ResearchRate = 1.05f, // sun-calendars and astronomy
                FaithRate = 1.35f,
                MilitaryStrength = 1.05f,
                TradeAptitude = 0.95f,
                MoveSpeed = 0.95f,

                // AI personality: the most pious; patient, defensive and fairly open to others.
                Aggression = 0.35f,
                Expansionism = 0.45f,
                Piety = 0.9f,
                Industriousness = 0.6f,
                Curiosity = 0.55f,
                Diplomacy = 0.55f,
                Xenophobia = 0.35f,
                Mercantilism = 0.55f,

                // Layout: balanced density, causeways and canals as often as roads.
                MinSettlementSpacing = 15,
                Compactness = 0.5f,
                RoadAffinity = 0.5f,
            };

            var h = new float[Counts.Biomes];
            h[(int)Biome.DeepOcean] = 0f;
            h[(int)Biome.ShallowWater] = 0f;
            h[(int)Biome.Lake] = 0f;
            h[(int)Biome.Beach] = 0.8f;
            h[(int)Biome.Grassland] = 0.45f;
            h[(int)Biome.Savanna] = 0.5f;
            h[(int)Biome.Forest] = 0.5f;
            h[(int)Biome.Jungle] = 0.9f;
            h[(int)Biome.Desert] = 0.35f;
            h[(int)Biome.Swamp] = 1.0f;
            h[(int)Biome.Taiga] = 0.05f;
            h[(int)Biome.Tundra] = 0.02f;
            h[(int)Biome.Hills] = 0.3f;
            h[(int)Biome.Mountain] = 0.1f;
            h[(int)Biome.Snow] = 0f;        // cold-blooded: uninhabitable
            h[(int)Biome.Volcanic] = 0.3f;  // warm ground
            r.BiomeHabitability = h;

            var p = RaceDefinition.Fill(Counts.Resources, 1f);
            p[(int)ResourceType.Food] = 1.2f; // fishing
            p[(int)ResourceType.Wood] = 0.95f;
            p[(int)ResourceType.Stone] = 1.1f; // pyramid masons
            p[(int)ResourceType.Ore] = 0.85f;
            p[(int)ResourceType.Metal] = 0.9f;
            p[(int)ResourceType.Gold] = 1.2f;
            p[(int)ResourceType.Tools] = 0.95f;
            p[(int)ResourceType.Cloth] = 1.0f;
            p[(int)ResourceType.Goods] = 1.0f;
            p[(int)ResourceType.Weapons] = 0.95f;
            p[(int)ResourceType.Fuel] = 0.9f;
            p[(int)ResourceType.Machinery] = 0.85f;
            p[(int)ResourceType.Electronics] = 0.95f;
            r.ProductionMultiplier = p;

            var a = new float[Counts.Races];
            a[(int)RaceId.Human] = 0.1f;
            a[(int)RaceId.Elf] = 0.1f;
            a[(int)RaceId.Dwarf] = 0.0f;
            a[(int)RaceId.Orc] = -0.1f;
            a[(int)RaceId.Saurian] = 1.0f;
            r.RaceAffinity = a;

            r.Architecture = new ArchitectureStyle
            {
                Roof = RoofStyle.Pyramid,
                HeightScale = 1.1f,
                WidthScale = 1.0f,
                Roundness = 0.3f,
                BuildsUnderground = false,
                BuildsInTrees = false,
                BuildsOnStilts = true,
                WallColor = 0xC8B07AFFu,   // sandstone
                RoofColor = 0x3D7A57FFu,   // jade reed thatch
                TrimColor = 0x5C4A2EFFu,   // mangrove wood
                AccentColor = 0xE3A82BFFu, // sun gold
                StyleTag = "stilts-and-steps",
            };

            r.Names = new NameParts
            {
                PersonPrefixes = new[]
                {
                    "ssar", "ssik", "ixi", "xath", "thiss", "zeth", "qess", "kesh", "shax", "vess", "ysh", "hiss",
                    "tsa", "xil", "sesh", "thax", "zhi", "ossi", "xess", "iss", "sath", "qix", "russ", "nesh",
                },
                MaleSuffixes = new[]
                {
                    "ak", "ix", "oth", "ess", "uxar", "ek", "thak", "isk", "az", "uth", "rix", "oq", "ash", "exis",
                },
                FemaleSuffixes = new[]
                {
                    "issa", "ethi", "ira", "ysse", "xa", "esha", "assi", "eqa", "oxa", "ithe", "ysha", "axi", "ila", "ussa",
                },
                FamilyNames = new[]
                {
                    "Thessaqar", "Xathiss", "Ssikkari", "Qesshar", "Ixtheru", "Zethuss", "Keshari", "Shaxxil",
                    "Vessirix", "Yshathe", "Hissoqa", "Tsavexi", "Xilossa", "Seshkar", "Thaxira", "Zhiquess",
                    "Ossirath", "Xessaru", "Issethix", "Sathiqo", "Qixassa", "Russekh", "Neshoxa", "Ythessi",
                },
                SettlementPrefixes = new[]
                {
                    "ssor", "ixa", "xoss", "qath", "zhe", "keth", "shis", "vex", "ysse", "hess", "tsul", "xan",
                    "sseq", "oth", "seth", "thix", "zaq", "issu", "nixi", "suth", "xel", "quss", "ixo", "yth",
                },
                SettlementSuffixes = new[]
                {
                    "ath", "ixis", "ossa", "qet", "ethe", "uxa", "isst", "aq", "oxar",
                    "esh", "ythe", "uth", "eqis", "ossk", "irrix", "eshta", "ixu", "asq",
                },
                CivilizationPatterns = new[]
                {
                    "{0} Sun-Clutch", "Brood of {0}", "{0} Coil", "Mire Temples of {0}",
                    "{0} Scale Synod", "Patient Ones of {0}", "{0} Basking Court", "Eggwardens of {0}",
                },
            };

            r.UniqueBuildings = new[] { BuildingType.SunPyramid };
            r.FavoredBuildings = new[]
            {
                BuildingType.FishingHut, BuildingType.Shrine, BuildingType.Temple, BuildingType.Harbor, BuildingType.Bathhouse,
            };
            return r;
        }
    }
}
