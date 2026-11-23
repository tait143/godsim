using System;
using System.Collections.Generic;

namespace GodSim.Core.Objectives
{
    /// <summary>
    /// Every divine objective in the game, in a fixed, deterministic order (the order objectives are
    /// evaluated and listed in the HUD). Keys are stable: they are stored in save files via WorldState.Memory.
    /// </summary>
    public static class ObjectiveCatalog
    {
        private static readonly List<ObjectiveDefinition> _all = Build();
        private static readonly IReadOnlyList<ObjectiveDefinition> _readOnly = _all.AsReadOnly();
        private static readonly Dictionary<string, ObjectiveDefinition> _byKey = Index(_all);

        /// <summary>All objectives (read-only list, deterministic order).</summary>
        public static IReadOnlyList<ObjectiveDefinition> All => _readOnly;

        /// <summary>Looks up an objective by key, or null.</summary>
        public static ObjectiveDefinition Find(string key)
        {
            if (key == null) return null;
            return _byKey.TryGetValue(key, out var d) ? d : null;
        }

        /// <summary>Index of an objective in <see cref="All"/>, or -1.</summary>
        public static int IndexOf(string key)
        {
            var d = Find(key);
            return d == null ? -1 : _all.IndexOf(d);
        }

        /// <summary>Structural validation of the catalog. Returns human-readable problems (empty = valid).</summary>
        public static List<string> Validate()
        {
            var errors = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _all.Count; i++)
            {
                var d = _all[i];
                if (d == null) { errors.Add("null objective at " + i); continue; }
                if (!IsValidKey(d.Key)) errors.Add("Objective key invalid: '" + d.Key + "'");
                if (!seen.Add(d.Key)) errors.Add("Duplicate objective key: " + d.Key);
                if (string.IsNullOrEmpty(d.Title)) errors.Add("Objective " + d.Key + " has no title");
                if (string.IsNullOrEmpty(d.Description)) errors.Add("Objective " + d.Key + " has no description");
                if (!Enum.IsDefined(typeof(ObjectiveKind), d.Kind)) errors.Add("Objective " + d.Key + " has unknown kind");
                if (!Enum.IsDefined(typeof(ObjectiveCategory), d.Category)) errors.Add("Objective " + d.Key + " has unknown category");
                if (float.IsNaN(d.Target) || float.IsInfinity(d.Target)) errors.Add("Objective " + d.Key + " target not finite");
                else if (d.Kind == ObjectiveKind.AlignmentAtMost ? d.Target >= 0f : d.Target <= 0f) errors.Add("Objective " + d.Key + " target has the wrong sign");
                if ((d.Kind == ObjectiveKind.CivilizationAge || d.Kind == ObjectiveKind.SpawnedCivilizationAge) && !d.AgeFilter.HasValue)
                    errors.Add("Objective " + d.Key + " needs an AgeFilter");
                if (!Finite(d.Reward.Mana) || !Finite(d.Reward.MaxManaBonus) || !Finite(d.Reward.AlignmentNudge) || d.Reward.Mana < 0f || d.Reward.MaxManaBonus < 0f || Math.Abs(d.Reward.AlignmentNudge) > 0.25f)
                    errors.Add("Objective " + d.Key + " reward out of range");
                if (d.Importance < 2 || d.Importance > 3) errors.Add("Objective " + d.Key + " importance must be 2..3");
            }
            return errors;
        }

        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);

        private static readonly string[] ReservedKeys = { "count", "best", "peace", "phoenix", "spawnedbygod" };

        private static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 48) return false;
            for (int i = 0; i < key.Length; i++)
            {
                char c = key[i];
                if (!((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_')) return false;
            }
            for (int i = 0; i < ReservedKeys.Length; i++) if (key == ReservedKeys[i]) return false;
            return true;
        }

        private static Dictionary<string, ObjectiveDefinition> Index(List<ObjectiveDefinition> list)
        {
            var d = new Dictionary<string, ObjectiveDefinition>(StringComparer.Ordinal);
            for (int i = 0; i < list.Count; i++) if (list[i] != null && !d.ContainsKey(list[i].Key)) d[list[i].Key] = list[i];
            return d;
        }

        // ------------------------------------------------------------------ content

        private static ObjectiveDefinition Def(List<ObjectiveDefinition> list, string key, string title, ObjectiveCategory cat, ObjectiveKind kind, float target,
            string description, float mana, float maxMana = 0f, float alignment = 0f, int importance = 2)
        {
            var d = new ObjectiveDefinition(key)
            {
                Title = title,
                Description = description,
                Category = cat,
                Kind = kind,
                Target = target,
                Reward = new ObjectiveReward(mana, maxMana, alignment),
                Importance = importance,
            };
            list.Add(d);
            return d;
        }

        private static ObjectiveDefinition Age(List<ObjectiveDefinition> list, string key, string title, AgeId age, ObjectiveCategory cat, string description,
            float mana, float maxMana, int importance = 2)
        {
            var d = Def(list, key, title, cat, ObjectiveKind.CivilizationAge, 1f, description, mana, maxMana, 0f, importance);
            d.AgeFilter = age;
            return d;
        }

        private static ObjectiveDefinition Cast(List<ObjectiveDefinition> list, string key, string title, GodPowerType power, float times, ObjectiveCategory cat,
            string description, float mana, float maxMana = 0f, float alignment = 0f)
        {
            var d = Def(list, key, title, cat, ObjectiveKind.PowerCast, times, description, mana, maxMana, alignment);
            d.Power = power;
            return d;
        }

        private static ObjectiveDefinition Built(List<ObjectiveDefinition> list, string key, string title, BuildingType type, ObjectiveCategory cat,
            string description, float mana, float maxMana, int importance = 2)
        {
            var d = Def(list, key, title, cat, ObjectiveKind.BuildingCompleted, 1f, description, mana, maxMana, 0f, importance);
            d.Building = type;
            return d;
        }

        private static List<ObjectiveDefinition> Build()
        {
            var l = new List<ObjectiveDefinition>(64);
            const ObjectiveCategory G = ObjectiveCategory.Guidance, P = ObjectiveCategory.Prosperity, F = ObjectiveCategory.Faith,
                W = ObjectiveCategory.Wrath, O = ObjectiveCategory.Wonder, L = ObjectiveCategory.Legacy;

            // ---------------------------------------------------------------- Guidance
            Def(l, "first_miracle", "First Miracle", G, ObjectiveKind.AnyPowerCast, 1f, "Work your first miracle upon the world.", 25f);
            Def(l, "first_light", "First Light", G, ObjectiveKind.SettlementTier, (int)SettlementTier.Hamlet, "Watch a camp grow into a hamlet.", 50f);
            Def(l, "hundred_souls", "Hundred Souls", G, ObjectiveKind.WorldPopulation, 100f, "Let the world hold a hundred mortal souls.", 40f);
            Cast(l, "rain_maker", "Rain Maker", GodPowerType.Rain, 25f, G, "Call down the rain 25 times.", 80f, 10f, 0.02f);
            Cast(l, "green_thumb", "Green Thumb", GodPowerType.PlantForest, 10f, G, "Raise new forests 10 times.", 60f);
            Cast(l, "breath_of_life", "Breath of Life", GodPowerType.SpawnTribe, 1f, G, "Breathe life into a new tribe.", 60f);
            Cast(l, "shaper_of_lands", "Shaper of Lands", GodPowerType.RaiseLand, 15f, G, "Raise the land from the waters 15 times.", 50f);
            Def(l, "first_century", "First Century", G, ObjectiveKind.YearReached, 100f, "Watch over the world for a hundred years.", 60f, 10f);
            Def(l, "weathered", "Weathered the Storm", G, ObjectiveKind.DisastersWitnessed, 10f, "Witness ten disasters, natural or divine.", 50f);
            Def(l, "steward_of_skies", "Steward of the Skies", G, ObjectiveKind.AnyPowerCast, 50f, "Every 50 miracles strengthen your grip on the heavens.", 40f).Repeatable = true;

            // ---------------------------------------------------------------- Prosperity
            Def(l, "village_green", "Village Green", P, ObjectiveKind.SettlementTier, (int)SettlementTier.Village, "A settlement grows into a village.", 60f);
            Def(l, "town_crier", "Town Crier", P, ObjectiveKind.SettlementTier, (int)SettlementTier.Town, "A settlement grows into a town.", 80f, 15f);
            Def(l, "thousand_faithful", "Thousand Faithful", F, ObjectiveKind.WorldPopulation, 1000f, "The world's population reaches one thousand.", 120f, 25f, 0.02f);
            Def(l, "teeming_world", "Teeming World", P, ObjectiveKind.WorldPopulation, 5000f, "The world's population reaches five thousand.", 200f, 50f);
            Def(l, "great_hearth", "Great Hearth", P, ObjectiveKind.SettlementPopulation, 250f, "A single settlement shelters 250 souls.", 120f, 20f);
            Def(l, "many_hearths", "Many Hearths", P, ObjectiveKind.SettlementCount, 10f, "Ten settlements stand at once.", 100f, 15f);
            var realm = Def(l, "realm_of_towns", "Realm of Towns", P, ObjectiveKind.SettlementsAtTier, 5f, "Five settlements stand as towns or greater.", 150f, 25f);
            realm.Tier = SettlementTier.Town;
            Def(l, "seeker_of_truths", "Seeker of Truths", P, ObjectiveKind.TechsKnown, 20f, "A civilization masters twenty technologies.", 120f, 20f);
            Def(l, "thousand_roofs", "A Thousand Roofs", P, ObjectiveKind.BuildingsCompleted, 1000f, "Mortals complete a thousand buildings.", 150f, 25f);
            Def(l, "grand_builder", "Grand Builder", P, ObjectiveKind.BuildingsCompleted, 500f, "Every 500 completed buildings honor their patron.", 50f).Repeatable = true;
            Def(l, "turning_of_ages", "Turning of Ages", P, ObjectiveKind.AgeAdvancements, 10f, "Civilizations advance ten ages between them.", 120f, 20f);
            var dwarf = Def(l, "deep_delvers", "Deep Delvers", P, ObjectiveKind.SettlementTier, (int)SettlementTier.Town, "A dwarven hold grows into a town.", 80f, 10f);
            dwarf.RaceFilter = RaceId.Dwarf;
            var elf = Def(l, "elder_wisdom", "Elder Wisdom", P, ObjectiveKind.CivilizationAge, 1f, "An elven civilization reaches the Renaissance.", 120f, 15f);
            elf.RaceFilter = RaceId.Elf;
            elf.AgeFilter = AgeId.Renaissance;
            var orc = Def(l, "iron_horde", "Iron Horde", W, ObjectiveKind.CivilizationAge, 1f, "An orcish civilization reaches the Iron age.", 60f, 10f);
            orc.RaceFilter = RaceId.Orc;
            orc.AgeFilter = AgeId.Iron;
            var brotherhood = Def(l, "brotherhood_of_iron", "Brotherhood of Iron", P, ObjectiveKind.CivilizationAge, 3f, "Three civilizations stand in the Iron age or later.", 120f, 20f);
            brotherhood.AgeFilter = AgeId.Iron;

            // ---------------------------------------------------------------- Age milestones
            Age(l, "bronze_shepherd", "Bronze Shepherd", AgeId.Bronze, P, "Guide a civilization into the Bronze age.", 60f, 10f);
            Age(l, "iron_will", "Iron Will", AgeId.Iron, P, "Guide a civilization into the Iron age.", 80f, 15f);
            Age(l, "classical_muse", "Classical Muse", AgeId.Classical, P, "Guide a civilization into the Classical age.", 100f, 20f);
            Age(l, "keeper_of_keeps", "Keeper of Keeps", AgeId.Medieval, P, "Guide a civilization into the Medieval age.", 120f, 25f);
            Age(l, "renaissance_patron", "Renaissance Patron", AgeId.Renaissance, P, "Guide a civilization into the Renaissance.", 140f, 30f);
            Age(l, "smoke_and_steel", "Smoke and Steel", AgeId.Industrial, P, "Guide a civilization into the Industrial age.", 160f, 35f);
            Age(l, "modern_marvel", "Modern Marvel", AgeId.Modern, P, "Guide a civilization into the Modern age.", 180f, 40f);
            Age(l, "oracle_of_the_network", "Oracle of the Network", AgeId.Information, L, "Guide a civilization into the Information age.", 200f, 45f);
            Age(l, "threshold_of_heaven", "Threshold of Heaven", AgeId.Ascension, L, "Guide a civilization into the Ascension age.", 250f, 60f, 3);
            var king = Def(l, "kingmaker", "Kingmaker", L, ObjectiveKind.SpawnedCivilizationAge, 1f, "A people you spawned with your own hand reaches the Iron age.", 150f, 30f, 0.03f);
            king.AgeFilter = AgeId.Iron;

            // ---------------------------------------------------------------- Settlement greatness
            Def(l, "city_of_gods", "City of Gods", P, ObjectiveKind.SettlementTier, (int)SettlementTier.City, "A settlement grows into a city.", 150f, 30f);
            Def(l, "teeming_metropolis", "Teeming Metropolis", P, ObjectiveKind.SettlementTier, (int)SettlementTier.Metropolis, "A city swells into a metropolis.", 200f, 40f);
            Def(l, "endless_city", "Endless City", L, ObjectiveKind.SettlementTier, (int)SettlementTier.Megalopolis, "A megalopolis sprawls to the horizon.", 300f, 60f, 0f, 3).Hidden = true;

            // ---------------------------------------------------------------- Wonders
            Def(l, "wonder_of_the_world", "Wonder of the World", O, ObjectiveKind.WondersCompleted, 1f, "Mortals complete their first wonder.", 120f, 25f);
            Def(l, "seven_wonders", "Seven Wonders", O, ObjectiveKind.WondersCompleted, 7f, "Seven wonders rise across the world.", 250f, 50f, 0f, 3);
            Built(l, "racial_pride_human", "Racial Pride: Grand Cathedral", BuildingType.GrandCathedral, O, "Humans raise the Grand Cathedral.", 100f, 20f);
            Built(l, "racial_pride_elf", "Racial Pride: World Tree", BuildingType.WorldTree, O, "Elves grow the World Tree.", 100f, 20f);
            Built(l, "racial_pride_dwarf", "Racial Pride: Deep Forge", BuildingType.DeepForge, O, "Dwarves kindle the Deep Forge.", 100f, 20f);
            Built(l, "racial_pride_orc", "Racial Pride: War Idol", BuildingType.WarIdol, O, "Orcs raise the War Idol.", 100f, 20f);
            Built(l, "racial_pride_saurian", "Racial Pride: Sun Pyramid", BuildingType.SunPyramid, O, "Saurians build the Sun Pyramid.", 100f, 20f);
            Built(l, "keeper_of_scrolls", "Keeper of Scrolls", BuildingType.GreatLibrary, O, "The Great Library opens its doors.", 120f, 20f);
            Built(l, "shadow_of_the_colossus", "Shadow of the Colossus", BuildingType.Colossus, O, "The Colossus towers over a harbor.", 120f, 20f);
            Built(l, "crossroads_of_the_world", "Crossroads of the World", BuildingType.GrandBazaar, O, "The Grand Bazaar fills with traders.", 120f, 20f);
            Built(l, "ascension", "Ascension", BuildingType.AscensionGate, L, "A civilization completes the Ascension Gate: the endgame victory.", 500f, 100f, 3);

            // ---------------------------------------------------------------- Faith
            Def(l, "merciful", "Merciful", F, ObjectiveKind.AlignmentAtLeast, 0.7f, "Become a benevolent god (alignment 0.7 or higher).", 120f, 25f);
            Cast(l, "prophets_legacy", "Prophet's Legacy", GodPowerType.Prophet, 5f, F, "Anoint five prophets among the mortals.", 120f, 20f, 0.03f);
            Def(l, "devoted_flock", "Devoted Flock", F, ObjectiveKind.CivilizationDevotion, 0.9f, "A civilization worships you with total devotion.", 100f, 20f, 0.02f);
            Def(l, "peacekeeper", "Peacekeeper", F, ObjectiveKind.YearsOfPeace, 100f, "Keep the world free of war for a hundred years.", 200f, 40f, 0.05f, 3);
            Def(l, "brimming_chalice", "Brimming Chalice", F, ObjectiveKind.ManaReached, 500f, "Hold 500 mana at once.", 0f, 50f);
            Cast(l, "generous_hand", "Generous Hand", GodPowerType.Bless, 10f, F, "Every ten blessings are remembered.", 30f, 0f, 0.01f).Repeatable = true;
            Cast(l, "healer_of_nations", "Healer of Nations", GodPowerType.Heal, 15f, F, "Heal the sick 15 times.", 80f, 10f, 0.02f);
            Def(l, "many_peoples", "Many Peoples", F, ObjectiveKind.CivilizationsAlive, 8f, "Eight civilizations live at once.", 120f, 20f);
            var five = Def(l, "five_peoples_thrive", "Five Peoples Thrive", L, ObjectiveKind.RacesAlive, 5f, "All five races still live in the year 200.", 250f, 50f, 0.03f, 3);
            five.MinYear = 200;

            // ---------------------------------------------------------------- Wrath
            Def(l, "wrathful", "Wrathful", W, ObjectiveKind.AlignmentAtMost, -0.7f, "Become a wrathful god (alignment -0.7 or lower).", 120f, 25f);
            Def(l, "witness_to_war", "Witness to War", W, ObjectiveKind.BattlesFought, 10f, "Witness ten battles.", 80f, 10f);
            Def(l, "drums_of_war", "Drums of War", W, ObjectiveKind.WarsDeclared, 5f, "Five wars are declared.", 80f, 10f);
            Def(l, "conquerors_witness", "Conqueror's Witness", W, ObjectiveKind.SettlementsCaptured, 5f, "Five settlements fall to conquest.", 100f, 15f);
            Cast(l, "hammer_of_heaven", "Hammer of Heaven", GodPowerType.Meteor, 5f, W, "Hurl five meteors at the world.", 100f, 15f, -0.02f);
            Cast(l, "storm_caller", "Storm Caller", GodPowerType.Lightning, 25f, W, "Strike with lightning 25 times.", 80f, 10f, -0.02f);
            Cast(l, "pestilence", "Pestilence", GodPowerType.Plague, 3f, W, "Unleash plague three times.", 80f, 10f, -0.02f).Hidden = true;
            Def(l, "phoenix_rising", "Phoenix", W, ObjectiveKind.Phoenix, 1f, "A settlement your meteor destroyed is rebuilt nearby within 50 years.", 200f, 30f, 0.02f, 3).Hidden = true;
            Def(l, "twilight_of_nations", "Twilight of Nations", W, ObjectiveKind.CivilizationsCollapsed, 2f, "Two civilizations fade from history.", 100f, 20f, 0f, 3).Hidden = true;

            // ---------------------------------------------------------------- Legacy
            Def(l, "eternal_watch", "Eternal Watch", L, ObjectiveKind.YearReached, 1000f, "Watch over the world for a thousand years.", 300f, 60f, 0f, 3);
            return l;
        }
    }
}
