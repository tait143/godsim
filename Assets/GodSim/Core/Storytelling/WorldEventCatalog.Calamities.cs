using System;
using System.Collections.Generic;

namespace GodSim.Core.Storytelling
{
    public static partial class WorldEventCatalog
    {
        private static readonly ResourceType[] Lootable =
        {
            ResourceType.Food, ResourceType.Wood, ResourceType.Gold, ResourceType.Goods,
            ResourceType.Tools, ResourceType.Metal, ResourceType.Cloth, ResourceType.Weapons
        };

        private static void AddCalamities(List<WorldEventDefinition> list)
        {
            // ---------------------------------------------------------------- Bandit Raid
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.BanditRaid,
                Title = "Bandit Raid",
                Description = "Bandits raid the outermost settlement: 2-3 stockpiles lose 10-25%; 1-3 deaths if it has no soldiers.",
                Category = ChronicleCategory.War,
                Importance = 1,
                CooldownYears = 15f,
                BaseWeight = 0.8f,
                SelectTarget = e =>
                {
                    var cap = StoryUtil.Capital(e.State, e.Civ);
                    e.Settlement = BestBy(e, s =>
                    {
                        if (LootTotal(s) < 20f) return float.MinValue;
                        if (cap == null || s == cap) return 0f; // the capital is only raided when nothing else qualifies
                        return 1f + cap.Center.DistanceSqTo(s.Center);
                    });
                    return e.Settlement != null;
                },
                Weight = e =>
                {
                    float w = StoryUtil.SoldierCount(e.State, e.Settlement) == 0 ? 1.5f : 0.6f;
                    if (StoryUtil.HasCompleteBuilding(e.State, e.Settlement, BuildingType.Palisade) ||
                        StoryUtil.HasCompleteBuilding(e.State, e.Settlement, BuildingType.StoneWall) ||
                        StoryUtil.HasCompleteBuilding(e.State, e.Settlement, BuildingType.Watchtower)) w *= 0.5f;
                    return w;
                },
                Effect = e =>
                {
                    var s = e.Settlement;
                    e.Resources.Clear();
                    for (int i = 0; i < Lootable.Length; i++) if (s.Stockpile.Get(Lootable[i]) >= 1f) e.Resources.Add(Lootable[i]);
                    if (e.Resources.Count == 0) return false;
                    e.Rng.Shuffle(e.Resources);
                    int kinds = Math.Min(e.Resources.Count, e.Rng.RangeInclusive(2, 3));
                    float stolen = 0f;
                    for (int i = 0; i < kinds; i++) stolen += StoryUtil.RemoveFraction(s, e.Resources[i], e.Rng.Range(0.10f, 0.25f));
                    e.Amount = stolen;
                    if (StoryUtil.SoldierCount(e.State, s) == 0)
                    {
                        int pop = StoryUtil.CollectResidents(e.State, s, e.People, p => p.Id != e.Civ.LeaderId);
                        int deaths = Math.Min(e.Rng.RangeInclusive(1, 3), pop / 4);
                        e.Count = deaths > 0 ? StoryUtil.KillRandom(e.Sim, e.People, deaths, DeathCause.Violence) : 0;
                    }
                    e.UseAltTemplates = e.Count > 0;
                    StoryUtil.ShiftMood(e.State, s, -0.04f);
                    return true;
                },
                Templates = new[]
                {
                    "Bandits fell upon {settlement} by night and carried off part of its stores before vanishing into the wilds.",
                    "Outlaws raided the outskirts of the {civ}; the storehouses of {settlement} were left half empty.",
                    "Masked riders plundered {settlement}, and its people swore to build stronger walls.",
                },
                AltTemplates = new[]
                {
                    "Bandits raided {settlement}; with no soldiers to stand against them, the town buried {count} of its own.",
                    "Raiders burst into undefended {settlement}, looted its stores and left {count} dead in the streets.",
                }
            });

            // ---------------------------------------------------------------- Plague of Rats
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.PlagueOfRats,
                Title = "Plague of Rats",
                Description = "Rats devour 30% of the food of a settlement with a large stockpile and no Granary.",
                Category = ChronicleCategory.Disaster,
                Importance = 1,
                CooldownYears = 15f,
                SelectTarget = e =>
                {
                    e.Settlement = BestBy(e, s =>
                    {
                        float food = s.Stockpile.Get(ResourceType.Food);
                        if (food < Math.Max(60f, 0.45f * s.Stockpile.Capacity)) return float.MinValue;
                        if (StoryUtil.HasCompleteBuilding(e.State, s, BuildingType.Granary)) return float.MinValue;
                        return food;
                    });
                    return e.Settlement != null;
                },
                Weight = e => 1f + MathUtil.Clamp(e.Settlement.Stockpile.Get(ResourceType.Food) / Math.Max(1f, e.Settlement.Stockpile.Capacity), 0f, 2f),
                Effect = e =>
                {
                    e.Amount = StoryUtil.RemoveFraction(e.Settlement, ResourceType.Food, 0.30f);
                    return true;
                },
                Templates = new[]
                {
                    "A plague of rats swarmed the stores of {settlement} and devoured {amount} measures of food.",
                    "Rats beyond counting poured out of the walls of {settlement}; a third of its food was lost.",
                    "The {race} of {settlement} learned the hard way that a heap of grain needs a granary.",
                }
            });

            // ---------------------------------------------------------------- Wolf Winter
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.WolfWinter,
                Title = "Wolf Winter",
                Description = "In a cold-climate winter, starving wolves kill 1-3 people (fewer with soldiers) and spoil 5% of the food.",
                Category = ChronicleCategory.Disaster,
                Importance = 1,
                CooldownYears = 12f,
                SeasonMask = Seasons.Winter,
                SelectTarget = e =>
                {
                    e.Settlement = Largest(e, s => StoryUtil.IsColdClimate(e.Map, s.Center) && StoryUtil.AliveResidents(e.State, s) >= 4);
                    return e.Settlement != null;
                },
                Effect = e =>
                {
                    var s = e.Settlement;
                    int pop = StoryUtil.CollectResidents(e.State, s, e.People, null);
                    int deaths = e.Rng.RangeInclusive(1, 3);
                    if (StoryUtil.SoldierCount(e.State, s) >= 2) deaths = Math.Max(1, deaths - 1);
                    deaths = Math.Min(deaths, Math.Max(1, pop / 5));
                    e.Count = StoryUtil.KillRandom(e.Sim, e.People, deaths, DeathCause.Violence);
                    StoryUtil.RemoveFraction(s, ResourceType.Food, 0.05f);
                    return true;
                },
                Templates = new[]
                {
                    "It was a wolf winter in {settlement}: grey packs came down from the frozen wilds, and {count} of its folk never saw spring.",
                    "Howling filled the long nights of {settlement}; when the snows melted, the missing numbered {count}.",
                    "Starving wolves prowled the streets of {settlement}, and the {race} barred their doors.",
                }
            });

            // ---------------------------------------------------------------- Earth Tremor
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.EarthTremor,
                Title = "Earth Tremor",
                Description = "A minor quake damages 2-5 buildings (condition -10..30%, never below 5%; nothing collapses).",
                Category = ChronicleCategory.Disaster,
                Importance = 1,
                CooldownYears = 20f,
                SelectTarget = e =>
                {
                    e.Settlement = BestBy(e, s =>
                    {
                        if (StoryUtil.CountBuildings(e.State, s, b => b.IsComplete) < 3) return float.MinValue;
                        return StoryUtil.RuggedTilesNear(e.Map, s.Center, 8) * 10f + StoryUtil.AliveResidents(e.State, s);
                    });
                    return e.Settlement != null;
                },
                Weight = e => StoryUtil.RuggedTilesNear(e.Map, e.Settlement.Center, 8) > 0 ? 1.5f : 0.4f,
                Effect = e =>
                {
                    int n = StoryUtil.CollectBuildings(e.State, e.Settlement, e.Buildings, b => b.IsComplete);
                    if (n == 0) return false;
                    int k = Math.Min(n, e.Rng.RangeInclusive(2, 5));
                    for (int i = 0; i < k; i++)
                    {
                        var b = StoryUtil.TakeRandom(e.Rng, e.Buildings);
                        b.Condition = MathUtil.Clamp(MathUtil.Sanitize(b.Condition, 1f) - e.Rng.Range(0.10f, 0.30f), 0.05f, 1f);
                    }
                    e.Count = k;
                    return true;
                },
                Templates = new[]
                {
                    "The earth shuddered beneath {settlement}; walls cracked and roofs sagged in {count} buildings.",
                    "A tremor rattled {settlement}. No one died, but the masons had work for a season.",
                    "The ground groaned under the {civ}, and {settlement} counted the cost in broken stone.",
                }
            });

            // ---------------------------------------------------------------- Great Fire
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.GreatFire,
                Title = "Great Fire",
                Description = "Fire breaks out in 1-3 buildings of a Village+ (IsOnFire + Burning tiles); the fire module spreads it.",
                Category = ChronicleCategory.Disaster,
                Importance = 2,
                CooldownYears = 25f,
                SelectTarget = e =>
                {
                    e.Settlement = Largest(e, s =>
                        s.Tier >= SettlementTier.Village &&
                        StoryUtil.CountBuildings(e.State, s, b => b.IsOnFire) == 0 &&
                        StoryUtil.CountBuildings(e.State, s, b => IsFlammable(e.Db, b)) >= 6);
                    return e.Settlement != null;
                },
                Weight = e =>
                {
                    float w;
                    switch (e.State.Season)
                    {
                        case Season.Summer: w = 2f; break;
                        case Season.Autumn: w = 1f; break;
                        case Season.Spring: w = 0.7f; break;
                        default: w = 0.3f; break;
                    }
                    return w / MathUtil.Clamp(MathUtil.Sanitize(e.State.Weather.Rainfall, 1f), 0.5f, 2f);
                },
                Effect = e =>
                {
                    int n = StoryUtil.CollectBuildings(e.State, e.Settlement, e.Buildings, b => IsFlammable(e.Db, b));
                    if (n == 0) return false;
                    int k = Math.Min(n, e.Rng.RangeInclusive(1, 3));
                    var map = e.Map;
                    for (int i = 0; i < k; i++)
                    {
                        var b = StoryUtil.TakeRandom(e.Rng, e.Buildings);
                        b.IsOnFire = true;
                        for (int y = b.Origin.Y; y < b.Origin.Y + b.Height; y++)
                            for (int x = b.Origin.X; x < b.Origin.X + b.Width; x++)
                                if (map.InBounds(x, y)) map.Set(map.Index(x, y), TileFlags.Burning);
                    }
                    map.MarkTerrainChanged();
                    e.Count = k;
                    return true;
                },
                Templates = new[]
                {
                    "A great fire broke out in {settlement}; flames leapt from roof to roof as the {race} formed bucket lines.",
                    "An overturned lamp set {settlement} ablaze, and the night sky glowed red.",
                    "Fire! The cry rang through {settlement} as smoke rose over the {civ}.",
                }
            });

            // ---------------------------------------------------------------- Lost Expedition
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.LostExpedition,
                Title = "Lost Expedition",
                Description = "2-4 explorers set out: 55% they are lost forever, otherwise they return with maps (research + gold).",
                Category = ChronicleCategory.World,
                Importance = 1,
                CooldownYears = 25f,
                SelectTarget = e =>
                {
                    e.Settlement = Largest(e, s => StoryUtil.CountResidents(e.State, s, p => p.IsAdult && NotLeaderNorSoldier(e, p)) >= 10);
                    return e.Settlement != null;
                },
                Effect = e =>
                {
                    StoryUtil.CollectResidents(e.State, e.Settlement, e.People, p => p.IsAdult && NotLeaderNorSoldier(e, p));
                    int n = Math.Min(e.People.Count, e.Rng.RangeInclusive(2, 4));
                    if (n <= 0) return false;
                    if (e.Rng.Chance(0.55f))
                    {
                        e.Count = StoryUtil.KillRandom(e.Sim, e.People, n, DeathCause.Accident);
                        return true;
                    }
                    e.Count = n;
                    float r = StoryUtil.ResearchBoost(e.State, e.Civ, 20f, 15f, 500f);
                    StoryUtil.AddResearch(e.Civ, r);
                    StoryUtil.AddStock(e.Settlement, ResourceType.Gold, e.Rng.Range(15f, 40f));
                    e.Amount = r;
                    e.UseAltTemplates = true;
                    return true;
                },
                Templates = new[]
                {
                    "{count} explorers set out from {settlement} to map the unknown. They were never seen again.",
                    "An expedition of {count} left {settlement} for the far horizon; only silence returned.",
                    "The {civ} mourned {count} brave souls of {settlement}, lost beyond the edge of the known world.",
                },
                AltTemplates = new[]
                {
                    "Long given up for dead, {count} explorers returned to {settlement} with maps of distant lands.",
                    "An expedition from {settlement} came home at last, its packs full of charts, curiosities and gold.",
                }
            });

            // ---------------------------------------------------------------- Mine Collapse
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.MineCollapse,
                Title = "Mine Collapse",
                Description = "A staffed mine caves in: 1-3 workers die, the mine loses 20% condition and 10% of the ore is lost.",
                Category = ChronicleCategory.Disaster,
                Importance = 1,
                CooldownYears = 15f,
                MinAge = AgeId.Bronze,
                SelectTarget = e =>
                {
                    var ids = e.Civ.SettlementIds;
                    int bestWorkers = 0;
                    for (int i = 0; i < ids.Count; i++)
                    {
                        var s = StoryUtil.Live(e.State, e.Civ, ids[i]);
                        if (s == null) continue;
                        var bids = s.BuildingIds;
                        for (int j = 0; j < bids.Count; j++)
                        {
                            var b = e.State.Buildings.Get(bids[j]);
                            if (!StoryUtil.IsStanding(b) || !b.IsComplete || !IsMine(b.Type)) continue;
                            int w = AliveWorkers(e.State, b);
                            if (w > bestWorkers) { bestWorkers = w; e.Building = b; e.Settlement = s; }
                        }
                    }
                    return e.Building != null;
                },
                Effect = e =>
                {
                    var b = e.Building;
                    e.People.Clear();
                    for (int i = 0; i < b.Workers.Count; i++)
                    {
                        var p = StoryUtil.AlivePerson(e.State, b.Workers[i]);
                        if (p != null) e.People.Add(p);
                    }
                    if (e.People.Count == 0) return false;
                    int deaths = e.Rng.RangeInclusive(1, Math.Min(3, e.People.Count));
                    e.Count = StoryUtil.KillRandom(e.Sim, e.People, deaths, DeathCause.Accident);
                    b.Condition = MathUtil.Clamp(MathUtil.Sanitize(b.Condition, 1f) - 0.2f, 0.05f, 1f);
                    StoryUtil.RemoveFraction(e.Settlement, ResourceType.Ore, 0.10f);
                    return true;
                },
                Templates = new[]
                {
                    "The tunnels beneath {settlement} gave way; the dark took {count} of its miners.",
                    "A mine collapsed near {settlement}, and the {race} sang dirges for the lost diggers.",
                    "Timbers cracked and stone fell in the mine of {settlement}; {count} never came up.",
                }
            });
        }

        // ------------------------------------------------------------------ helpers
        private static float LootTotal(Settlement s)
        {
            float t = 0f;
            for (int i = 0; i < Lootable.Length; i++) t += s.Stockpile.Get(Lootable[i]);
            return t;
        }

        internal static bool IsFlammable(GameDatabase db, Building b) =>
            b.IsComplete && !b.IsDestroyed && !b.IsOnFire && !StoryUtil.IsWonder(db, b);

        internal static bool IsMine(BuildingType t) => t == BuildingType.Mine || t == BuildingType.GoldMine || t == BuildingType.CoalMine;

        private static int AliveWorkers(WorldState st, Building b)
        {
            int n = 0;
            for (int i = 0; i < b.Workers.Count; i++) if (StoryUtil.AlivePerson(st, b.Workers[i]) != null) n++;
            return n;
        }
    }
}
