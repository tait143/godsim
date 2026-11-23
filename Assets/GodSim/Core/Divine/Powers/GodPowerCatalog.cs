namespace GodSim.Core.Divine
{
    /// <summary>
    /// Static data for all nineteen divine powers (costs, cooldowns, radii, alignment shifts).
    /// Registered once into the <see cref="GameDatabase"/> at startup, before it is frozen.
    /// Effects live in <see cref="GodPowerEffects"/>; execution rules live in <see cref="GodCommandSystem"/>.
    /// </summary>
    public static class GodPowerCatalog
    {
        /// <summary>Registers a definition for every <see cref="GodPowerType"/> value.</summary>
        public static void Register(GameDatabase db)
        {
            Guard.NotNull(db, nameof(db));
            var all = CreateAll();
            for (int i = 0; i < all.Length; i++) db.Register(all[i]);
        }

        /// <summary>Builds fresh definitions for every power, indexed by (int)GodPowerType.</summary>
        public static GodPowerDefinition[] CreateAll()
        {
            var defs = new GodPowerDefinition[Counts.GodPowers];

            // ------------------------------------------------------------- blessings
            Add(defs, GodPowerType.Bless, "Bless", GodPowerCategory.Blessing, 20f, 24, 3, 10, 0.03f,
                "A warm golden light washes over the land. Mortals within it feel lighter of heart and stronger of body, and whisper thanks to the sky.");
            Add(defs, GodPowerType.Rain, "Rain", GodPowerCategory.Blessing, 15f, 12, 4, 12, 0.01f,
                "Gather the clouds and let them weep. Parched soil drinks deep, fires hiss into smoke, and droughts are broken.");
            Add(defs, GodPowerType.Fertility, "Fertility", GodPowerCategory.Blessing, 30f, 48, 3, 8, 0.03f,
                "Breathe life into soil and womb alike. Fields grow rich and dark, and cradles fill in every nearby village.");
            Add(defs, GodPowerType.Inspire, "Inspire", GodPowerCategory.Blessing, 40f, 96, 2, 8, 0.02f,
                "Send visions to the scholars and dreamers. Ideas that would take a generation arrive in a single sleepless night.",
                requiresSettlement: true);
            Add(defs, GodPowerType.Heal, "Heal", GodPowerCategory.Blessing, 25f, 24, 2, 8, 0.03f,
                "Lay an unseen hand upon the sick and wounded. Fevers break, bones knit, and the dying rise from their beds.");
            Add(defs, GodPowerType.BountifulHarvest, "Bountiful Harvest", GodPowerCategory.Blessing, 30f, 48, 3, 8, 0.02f,
                "Granaries overflow with grain that no farmer sowed. A feast season the elders will speak of for decades.");
            Add(defs, GodPowerType.Prophet, "Prophet", GodPowerCategory.Blessing, 80f, 480, 0, 4, 0.04f,
                "Choose a mortal and speak through them. A prophet rises, heroic and magnetic, and the faithful flock to hear your word.",
                requiresSettlement: true);

            // ------------------------------------------------------------- shaping
            Add(defs, GodPowerType.RaiseLand, "Raise Land", GodPowerCategory.Shaping, 10f, 2, 1, 6, 0f,
                "Lift the earth from below. Seas retreat into new shores, plains swell into hills, and hills crown themselves with snow.");
            Add(defs, GodPowerType.LowerLand, "Lower Land", GodPowerCategory.Shaping, 10f, 2, 1, 6, 0f,
                "Press the world down with a single finger. Mountains slump, valleys sink, and whatever stood too low is claimed by the sea.");
            Add(defs, GodPowerType.PlantForest, "Plant Forest", GodPowerCategory.Shaping, 8f, 4, 1, 6, 0.005f,
                "Scatter seeds from the heavens. Saplings burst from bare earth and grow to ancient woods before the season turns.");
            Add(defs, GodPowerType.RevealOre, "Reveal Ore", GodPowerCategory.Shaping, 35f, 96, 1, 5, 0f,
                "Crack open the hillsides to show what lies within: veins of ore, seams of good stone, and the glint of gold.");
            Add(defs, GodPowerType.SpawnTribe, "Spawn Tribe", GodPowerCategory.Creation, 150f, 960, 0, 0, 0.05f,
                "Shape a people from clay and starlight. A new tribe of the chosen race awakens on open land, devoted to its maker.",
                requiresLand: true, usesRace: true);

            // ------------------------------------------------------------- wrath
            Add(defs, GodPowerType.Lightning, "Lightning", GodPowerCategory.Wrath, 5f, 1, 0, 1, -0.01f,
                "Hurl a bolt from the storm. It strikes the unlucky dead and sets roofs ablaze.");
            Add(defs, GodPowerType.Wildfire, "Wildfire", GodPowerCategory.Wrath, 25f, 24, 1, 5, -0.04f,
                "Kindle the dry brush with a breath of divine fire. Forests and fields become a roaring sea of flame.");
            Add(defs, GodPowerType.Earthquake, "Earthquake", GodPowerCategory.Wrath, 60f, 192, 3, 8, -0.06f,
                "Shake the bones of the world. Walls crack, towers topple, and the proud are humbled amid the rubble.");
            Add(defs, GodPowerType.Meteor, "Meteor", GodPowerCategory.Wrath, 120f, 480, 2, 6, -0.08f,
                "Pull a burning star from the firmament. It leaves a smoking crater, a ruined town, and sometimes a gift of rare metal.");
            Add(defs, GodPowerType.Plague, "Plague", GodPowerCategory.Wrath, 70f, 288, 2, 8, -0.08f,
                "Loose a pestilence upon the towns. The sick fill the streets and many will never rise again.");
            Add(defs, GodPowerType.Flood, "Flood", GodPowerCategory.Wrath, 50f, 192, 3, 8, -0.05f,
                "Command the rivers and the sea to rise. Low fields drown, houses wash away, and the unwary are swept off.");
            Add(defs, GodPowerType.Drought, "Drought", GodPowerCategory.Wrath, 45f, 192, 4, 12, -0.04f,
                "Turn your face away and withhold the rain. The land cracks, wells run dry, and harvests wither.");

            return defs;
        }

        private static void Add(GodPowerDefinition[] defs, GodPowerType type, string name, GodPowerCategory category,
            float mana, int cooldown, int minRadius, int maxRadius, float alignment, string description,
            bool requiresLand = false, bool requiresSettlement = false, bool usesRace = false)
        {
            defs[(int)type] = new GodPowerDefinition
            {
                Type = type,
                Name = name,
                Description = description,
                Category = category,
                ManaCost = mana,
                CooldownTicks = cooldown,
                MinRadius = minRadius,
                MaxRadius = maxRadius,
                RequiresLandTarget = requiresLand,
                RequiresSettlementInRange = requiresSettlement,
                AlignmentDelta = alignment,
                UsesRaceParameter = usesRace,
                MinWorldAge = AgeId.Dawn,
            };
        }
    }
}
