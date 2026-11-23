using System;
using System.Collections.Generic;

namespace GodSim.Core.Storytelling
{
    public static partial class WorldEventCatalog
    {
        private static void AddProsperity(List<WorldEventDefinition> list)
        {
            // ---------------------------------------------------------------- Golden Age
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.GoldenAge,
                Title = "Golden Age",
                Description = "A thriving Town+ capital enters a golden age: +0.15 happiness and a large research grant.",
                Category = ChronicleCategory.Civilization,
                Importance = 2,
                CooldownYears = 60f,
                SelectTarget = e =>
                {
                    var cap = StoryUtil.Capital(e.State, e.Civ);
                    if (cap == null || cap.Tier < SettlementTier.Town) return false;
                    if (MathUtil.Sanitize(cap.Happiness) < 0.7f || MathUtil.Sanitize(cap.Unrest) > 0.35f) return false;
                    e.Settlement = cap;
                    return true;
                },
                Weight = e => 1f + (MathUtil.Sanitize(e.Settlement.Happiness) - 0.7f) * 5f,
                Effect = e =>
                {
                    StoryUtil.ShiftMood(e.State, e.Settlement, 0.15f);
                    var ids = e.Civ.SettlementIds;
                    for (int i = 0; i < ids.Count; i++)
                    {
                        var s = StoryUtil.Live(e.State, e.Civ, ids[i]);
                        if (s != null && s != e.Settlement) StoryUtil.ShiftMood(e.State, s, 0.05f);
                    }
                    float r = StoryUtil.ResearchBoost(e.State, e.Civ, 60f, 40f, 1500f);
                    StoryUtil.AddResearch(e.Civ, r);
                    e.Amount = r;
                    return true;
                },
                Templates = new[]
                {
                    "A golden age dawned in {settlement}: under {leader} the {civ} flourished, and its scholars made great strides.",
                    "Poets of {settlement} would later call these the golden years of the {civ}.",
                    "Prosperity and learning blossomed in {settlement}, and the {race} of the {civ} spoke of a golden age.",
                }
            });

            // ---------------------------------------------------------------- Festival of Harvest
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.HarvestFestival,
                Title = "Festival of Harvest",
                Description = "Autumn with a food surplus: a feast eats 8% of the food and lifts spirits (+0.08 happiness).",
                Category = ChronicleCategory.Settlement,
                Importance = 1,
                CooldownYears = 6f,
                SeasonMask = Seasons.Autumn,
                SelectTarget = e =>
                {
                    e.Settlement = BestBy(e, s =>
                    {
                        float food = s.Stockpile.Get(ResourceType.Food);
                        bool surplus = food >= Math.Max(40f, 0.35f * s.Stockpile.Capacity) && s.Stockpile.Net(ResourceType.Food) >= -0.01f;
                        return surplus ? food : float.MinValue;
                    });
                    return e.Settlement != null;
                },
                Effect = e =>
                {
                    e.Amount = StoryUtil.RemoveFraction(e.Settlement, ResourceType.Food, 0.08f);
                    StoryUtil.ShiftMood(e.State, e.Settlement, 0.08f);
                    StoryUtil.ShiftUnrest(e.Settlement, -0.05f);
                    return true;
                },
                Templates = new[]
                {
                    "The granaries of {settlement} overflowed, and the {race} held a great harvest festival.",
                    "Bonfires burned for three nights as {settlement} celebrated a bountiful harvest.",
                    "{leader} opened the stores of {settlement} for a harvest feast remembered for a generation.",
                }
            });

            // ---------------------------------------------------------------- Gold Rush
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.GoldRush,
                Title = "Gold Rush",
                Description = "Prospectors find gold in the hills of a settlement's territory (adds a GoldDeposit tile).",
                Category = ChronicleCategory.World,
                Importance = 2,
                CooldownYears = 50f,
                MinAge = AgeId.Bronze,
                SelectTarget = e =>
                {
                    var map = e.Map;
                    var ids = e.Civ.SettlementIds;
                    for (int i = 0; i < ids.Count; i++)
                    {
                        var s = StoryUtil.Live(e.State, e.Civ, ids[i]);
                        if (s == null) continue;
                        if (map.CountFlagInRadius(s.Center, StoryUtil.ScanRadius(s), TileFlags.GoldDeposit) >= 2) continue;
                        int t = StoryUtil.FindTerritoryTile(map, s, k => IsGoldRushTile(map, k), false);
                        if (t < 0) continue;
                        e.Settlement = s;
                        e.TileIndex = t;
                        return true;
                    }
                    return false;
                },
                Effect = e =>
                {
                    var map = e.Map;
                    int t = e.TileIndex;
                    if (t < 0 || t >= map.TileCount || !IsGoldRushTile(map, t)) return false;
                    map.Set(t, TileFlags.GoldDeposit);
                    map.Resource[t] = Math.Max(MathUtil.Clamp(MathUtil.Sanitize(map.Resource[t]), 0f, 1e6f), e.Rng.Range(250f, 500f));
                    map.MarkTerrainChanged();
                    e.Amount = StoryUtil.AddStock(e.Settlement, ResourceType.Gold, e.Rng.Range(10f, 25f));
                    StoryUtil.ShiftMood(e.State, e.Settlement, 0.03f);
                    return true;
                },
                Templates = new[]
                {
                    "Gold glittered in the hills near {settlement}, and fortune-seekers of the {civ} rushed to stake their claims.",
                    "A shepherd of {settlement} stumbled upon a vein of gold, and the hills rang with picks.",
                    "Word of gold near {settlement} spread through the {civ} like wildfire.",
                }
            });

            // ---------------------------------------------------------------- Wandering Sage
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.WanderingSage,
                Title = "Wandering Sage",
                Description = "A sage teaches the people: a research grant and the Wise trait for one resident.",
                Category = ChronicleCategory.Technology,
                Importance = 1,
                CooldownYears = 20f,
                SelectTarget = e =>
                {
                    e.Settlement = Largest(e, s => StoryUtil.CountResidents(e.State, s, p => p.IsAdult && !p.HasTrait(PersonTraits.Wise)) > 0);
                    return e.Settlement != null;
                },
                Effect = e =>
                {
                    if (StoryUtil.CollectResidents(e.State, e.Settlement, e.People, p => p.IsAdult && !p.HasTrait(PersonTraits.Wise)) == 0) return false;
                    var pupil = StoryUtil.TakeRandom(e.Rng, e.People);
                    pupil.Traits |= PersonTraits.Wise;
                    e.Person = pupil;
                    float r = StoryUtil.ResearchBoost(e.State, e.Civ, 25f, 20f, 600f);
                    StoryUtil.AddResearch(e.Civ, r);
                    e.Amount = r;
                    return true;
                },
                Templates = new[]
                {
                    "A wandering sage came to {settlement} and took {person} as a pupil; the {civ} grew wiser for it.",
                    "An old sage sheltered in {settlement} for a winter, and {person} carried on the teachings.",
                    "Strange learning arrived in {settlement} with a wandering sage, who left {person} wiser than before.",
                }
            });

            // ---------------------------------------------------------------- Master Builder
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.MasterBuilder,
                Title = "Master Builder",
                Description = "A master builder speeds every building under construction in a settlement (+25% progress, capped at 99%).",
                Category = ChronicleCategory.Building,
                Importance = 1,
                CooldownYears = 15f,
                SelectTarget = e =>
                {
                    e.Settlement = BestBy(e, s =>
                    {
                        int n = StoryUtil.CountBuildings(e.State, s, IsBoostableConstruction);
                        return n > 0 ? n : float.MinValue;
                    });
                    return e.Settlement != null;
                },
                Effect = e =>
                {
                    int n = StoryUtil.CollectBuildings(e.State, e.Settlement, e.Buildings, IsBoostableConstruction);
                    for (int i = 0; i < n; i++)
                    {
                        var b = e.Buildings[i];
                        b.ConstructionProgress = MathUtil.Clamp(MathUtil.Sanitize(b.ConstructionProgress) + 0.25f, 0f, MaxBoostedProgress);
                    }
                    e.Count = n;
                    return n > 0;
                },
                Templates = new[]
                {
                    "A master builder arrived in {settlement}, and its scaffolds rose with uncanny speed.",
                    "Under the eye of a master builder, the masons of {settlement} worked as never before.",
                    "The builders of {settlement} learned new tricks from a travelling master, hastening every work under way.",
                }
            });

            // ---------------------------------------------------------------- Bountiful Fishing
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.BountifulFishing,
                Title = "Bountiful Fishing",
                Description = "Shoals of fish fill the nets of a waterside settlement (food, small happiness boost).",
                Category = ChronicleCategory.World,
                Importance = 1,
                CooldownYears = 8f,
                SeasonMask = Seasons.Spring | Seasons.Summer | Seasons.Autumn,
                SelectTarget = e =>
                {
                    e.Settlement = Largest(e, s => s.Stockpile.FreeSpace(ResourceType.Food) >= 10f && StoryUtil.NearWater(e.Map, s.Center, 5));
                    return e.Settlement != null;
                },
                Effect = e =>
                {
                    int pop = StoryUtil.AliveResidents(e.State, e.Settlement);
                    e.Amount = StoryUtil.AddStock(e.Settlement, ResourceType.Food, MathUtil.Clamp(15f + 1.5f * pop, 15f, 150f));
                    StoryUtil.ShiftMood(e.State, e.Settlement, 0.03f);
                    return true;
                },
                Templates = new[]
                {
                    "Great shoals came to the waters of {settlement}, and its fishers hauled in {amount} baskets of fish.",
                    "The nets of {settlement} were never so full; the {race} gave thanks for the bounty of the waters.",
                    "A silver tide of fish filled the shallows near {settlement}.",
                }
            });

            // ---------------------------------------------------------------- Baby Boom
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.BabyBoom,
                Title = "Baby Boom",
                Description = "Good times bring a wave of births in a happy, fed settlement (2-8 newborns).",
                Category = ChronicleCategory.Birth,
                Importance = 1,
                CooldownYears = 20f,
                SelectTarget = e =>
                {
                    e.Settlement = Largest(e, s =>
                        MathUtil.Sanitize(s.Happiness) >= 0.55f && s.Stockpile.Get(ResourceType.Food) >= 30f &&
                        StoryUtil.AliveResidents(e.State, s) >= 6 &&
                        StoryUtil.CountResidents(e.State, s, p => p.Stage == LifeStage.Adult && p.Sex == Sex.Female) >= 2);
                    return e.Settlement != null;
                },
                Effect = e =>
                {
                    var s = e.Settlement;
                    int pop = StoryUtil.AliveResidents(e.State, s);
                    int n = e.Rng.RangeInclusive(2, MathUtil.Clamp(pop / 8, 2, 8));
                    n = Math.Min(n, StoryUtil.RoomForPeople(e.Sim));
                    if (n <= 0) return false;
                    if (StoryUtil.CollectResidents(e.State, s, e.People, p => p.Stage == LifeStage.Adult && p.Sex == Sex.Female) == 0) return false;
                    int born = 0;
                    for (int k = 0; k < n; k++)
                    {
                        var mother = e.People[e.Rng.Range(0, e.People.Count)];
                        var father = StoryUtil.AlivePerson(e.State, mother.SpouseId);
                        e.Ops.CreatePerson(s, mother.Race, e.Rng.Chance(0.5f) ? Sex.Male : Sex.Female, 0f, mother.Id, father != null ? father.Id : -1, true);
                        born++;
                    }
                    e.Count = born;
                    return born > 0;
                },
                Templates = new[]
                {
                    "Cradles filled every home in {settlement}: {count} children were born in a single season.",
                    "In the good years of the {civ}, {settlement} rang with the cries of {count} newborns.",
                    "The midwives of {settlement} could barely keep up as a wave of births swept the town.",
                }
            });

            // ---------------------------------------------------------------- Great Migration
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.GreatMigration,
                Title = "Great Migration",
                Description = "10-20 refugees of the settlement's own race arrive at a small settlement.",
                Category = ChronicleCategory.Settlement,
                Importance = 2,
                CooldownYears = 30f,
                SelectTarget = e =>
                {
                    e.Settlement = BestBy(e, s =>
                    {
                        int pop = StoryUtil.AliveResidents(e.State, s);
                        return pop < 25 ? -pop : float.MinValue; // smallest first
                    });
                    return e.Settlement != null && StoryUtil.RoomForPeople(e.Sim) >= 10;
                },
                Effect = e =>
                {
                    var s = e.Settlement;
                    int n = Math.Min(e.Rng.RangeInclusive(10, 20), StoryUtil.RoomForPeople(e.Sim));
                    if (n < 10) return false;
                    var race = StoryUtil.MajorityRace(e.State, s);
                    var def = StoryUtil.RaceDef(e.Db, race);
                    float adult = def != null ? MathUtil.Clamp(MathUtil.Sanitize(def.AdulthoodAge, 16f), 1f, 200f) : 16f;
                    for (int k = 0; k < n; k++)
                    {
                        float age = e.Rng.Chance(0.7f) ? adult + e.Rng.Range(0f, 25f) : e.Rng.Range(1f, Math.Max(1.5f, adult - 1f));
                        e.Ops.CreatePerson(s, race, e.Rng.Chance(0.5f) ? Sex.Male : Sex.Female, age);
                    }
                    e.Count = n;
                    return true;
                },
                Templates = new[]
                {
                    "{count} weary refugees of the {race}, driven from distant lands, found a new home in {settlement}.",
                    "A long column of wanderers reached {settlement}; {count} of them, kin to the {race} there, chose to stay.",
                    "Fleeing war and hunger, {count} kinsfolk arrived at the gates of {settlement} and were taken in.",
                }
            });

            // ---------------------------------------------------------------- Foreign Caravan
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.TradeCaravan,
                Title = "Foreign Caravan",
                Description = "A caravan from a known, peaceful civilization brings gold and goods (+5 opinion).",
                Category = ChronicleCategory.Diplomacy,
                Importance = 1,
                CooldownYears = 8f,
                SelectTarget = e =>
                {
                    e.OtherCiv = BestPartner(e, r => r.HasContact && !r.AtWar && r.Stance >= DiplomaticStance.Neutral, true);
                    if (e.OtherCiv == null) return false;
                    e.Settlement = Largest(e, null);
                    return e.Settlement != null;
                },
                Weight = e => e.State.Diplomacy.TryGet(e.Civ.Id, e.OtherCiv.Id, out var r) && r.TradeAgreement ? 2f : 1f,
                Effect = e =>
                {
                    e.Amount = StoryUtil.AddStock(e.Settlement, ResourceType.Gold, e.Rng.Range(10f, 30f));
                    StoryUtil.AddStock(e.Settlement, ResourceType.Goods, e.Rng.Range(5f, 15f));
                    StoryUtil.ShiftOpinion(e.State.Diplomacy.Get(e.Civ.Id, e.OtherCiv.Id), 5f);
                    return true;
                },
                Templates = new[]
                {
                    "A caravan of the {other} arrived in {settlement}, laden with wares, and left the markets richer.",
                    "Merchants of the {other} traded fine goods in {settlement}, and friendship between the peoples grew.",
                    "Camels and carts of the {other} crowded the squares of {settlement} for a season of trade.",
                }
            });

            // ---------------------------------------------------------------- Ancient Ruins Discovered
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.AncientRuins,
                Title = "Ancient Ruins Discovered",
                Description = "Explorers excavate a Ruins tile in the territory: research and gold; the ruins are cleared.",
                Category = ChronicleCategory.World,
                Importance = 2,
                CooldownYears = 20f,
                SelectTarget = e =>
                {
                    var map = e.Map;
                    var ids = e.Civ.SettlementIds;
                    for (int i = 0; i < ids.Count; i++)
                    {
                        var s = StoryUtil.Live(e.State, e.Civ, ids[i]);
                        if (s == null) continue;
                        int t = StoryUtil.FindTerritoryTile(map, s, k => map.Has(k, TileFlags.Ruins) && map.BuildingAt[k] < 0, true, 3);
                        if (t < 0) continue;
                        e.Settlement = s;
                        e.TileIndex = t;
                        return true;
                    }
                    return false;
                },
                Effect = e =>
                {
                    var map = e.Map;
                    int t = e.TileIndex;
                    if (t < 0 || t >= map.TileCount || !map.Has(t, TileFlags.Ruins)) return false;
                    float r = StoryUtil.ResearchBoost(e.State, e.Civ, 30f, 25f, 800f);
                    StoryUtil.AddResearch(e.Civ, r);
                    StoryUtil.AddStock(e.Settlement, ResourceType.Gold, e.Rng.Range(20f, 60f));
                    map.Clear(t, TileFlags.Ruins);
                    map.MarkTerrainChanged();
                    e.Amount = r;
                    return true;
                },
                Templates = new[]
                {
                    "Diggers near {settlement} unearthed the ruins of a forgotten people, full of gold and strange inscriptions.",
                    "The scholars of the {civ} pored over relics recovered from ancient ruins near {settlement}.",
                    "Beneath the soil near {settlement} lay the halls of an older age; the {race} learned much from them.",
                }
            });
        }

        // ------------------------------------------------------------------ helpers
        internal const float MaxBoostedProgress = 0.99f;

        internal static bool IsBoostableConstruction(Building b) =>
            !b.IsComplete && !b.IsDestroyed && b.CostPaid && MathUtil.Sanitize(b.ConstructionProgress) < MaxBoostedProgress;

        internal static bool IsGoldRushTile(WorldMap map, int i)
        {
            var b = map.Biomes[i];
            if (b != Biome.Hills && b != Biome.Mountain) return false;
            return (map.Flags[i] & StoryUtil.DepositOrFeature) == 0 && map.BuildingAt[i] < 0;
        }

        /// <summary>Best other alive civ (with settlements) whose relation with e.Civ passes the filter; highest opinion first.</summary>
        internal static Civilization BestPartner(WorldEventContext e, Func<Relation, bool> ok, bool preferTrade)
        {
            Civilization best = null;
            float bestScore = float.MinValue;
            var civs = e.State.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++)
            {
                var c = civs[i];
                if (c == null || !c.IsAlive || c.Id == e.Civ.Id) continue;
                if (!e.State.Diplomacy.TryGet(e.Civ.Id, c.Id, out var r) || !ok(r)) continue;
                if (StoryUtil.AliveSettlementCount(e.State, c) == 0) continue;
                float score = MathUtil.Sanitize(r.Opinion) + (preferTrade && r.TradeAgreement ? 50f : 0f);
                if (score > bestScore) { bestScore = score; best = c; }
            }
            return best;
        }
    }
}
