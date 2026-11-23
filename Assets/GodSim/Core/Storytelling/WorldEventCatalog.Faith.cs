using System.Collections.Generic;

namespace GodSim.Core.Storytelling
{
    public static partial class WorldEventCatalog
    {
        private static void AddFaith(List<WorldEventDefinition> list)
        {
            // ---------------------------------------------------------------- Comet Omen
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.CometOmen,
                Title = "Comet Omen",
                Description = "A comet crosses the sky (world-wide cooldown): +0.10 devotion for the civ that reads the omen, +0.04 for all others.",
                Category = ChronicleCategory.Divine,
                Importance = 2,
                CooldownYears = 75f,
                WorldCooldown = true,
                BaseWeight = 0.6f,
                SelectTarget = e => { e.Settlement = StoryUtil.Capital(e.State, e.Civ); return true; },
                Effect = e =>
                {
                    StoryUtil.ShiftDevotion(e.Civ, 0.10f);
                    var civs = e.State.Civilizations.Items;
                    for (int i = 0; i < civs.Count; i++)
                        if (civs[i].IsAlive && civs[i] != e.Civ) StoryUtil.ShiftDevotion(civs[i], 0.04f);
                    return true;
                },
                Templates = new[]
                {
                    "A blazing comet crossed the heavens; the priests of {settlement} proclaimed it a sign of the god's will.",
                    "For forty nights a comet hung over the {civ}, and the {race} filled their shrines with offerings.",
                    "The sky split with a trail of fire, and {leader} declared the comet an omen; the {civ} turned to prayer.",
                }
            });

            // ---------------------------------------------------------------- Sacred Spring
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.SacredSpring,
                Title = "Sacred Spring",
                Description = "A spring wells up on a Sacred tile in the territory: faith, health and devotion rise.",
                Category = ChronicleCategory.Divine,
                Importance = 1,
                CooldownYears = 30f,
                SelectTarget = e =>
                {
                    var map = e.Map;
                    var ids = e.Civ.SettlementIds;
                    for (int i = 0; i < ids.Count; i++)
                    {
                        var s = StoryUtil.Live(e.State, e.Civ, ids[i]);
                        if (s == null) continue;
                        int t = StoryUtil.FindTerritoryTile(map, s, k => map.Has(k, TileFlags.Sacred), false);
                        if (t < 0) continue;
                        e.Settlement = s;
                        e.TileIndex = t;
                        return true;
                    }
                    return false;
                },
                Effect = e =>
                {
                    StoryUtil.ShiftFaith(e.State, e.Settlement, 0.15f, 0.10f);
                    e.Settlement.Health = MathUtil.Clamp01(MathUtil.Sanitize(e.Settlement.Health, 1f) + 0.05f);
                    StoryUtil.ShiftDevotion(e.Civ, 0.04f);
                    return true;
                },
                Templates = new[]
                {
                    "Clear water welled up from the holy ground near {settlement}; pilgrims of the {civ} came to drink and be healed.",
                    "The {race} of {settlement} discovered a sacred spring and built a ring of stones around it.",
                    "At the sacred spring of {settlement}, the sick were said to rise from their beds.",
                }
            });

            // ---------------------------------------------------------------- Holy Visions
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.HolyVisions,
                Title = "Holy Visions",
                Description = "A resident receives visions and becomes Devout: civ devotion +0.06, settlement faith +0.10.",
                Category = ChronicleCategory.Divine,
                Importance = 1,
                CooldownYears = 30f,
                Conditions = new System.Func<WorldEventContext, bool>[] { e => MathUtil.Sanitize(e.Civ.Devotion, 0.5f) < 0.98f },
                SelectTarget = e =>
                {
                    e.Settlement = Largest(e, s => StoryUtil.CountResidents(e.State, s, IsAdult) > 0);
                    return e.Settlement != null;
                },
                Weight = e =>
                {
                    var def = StoryUtil.RaceDef(e.Db, e.Civ.Race);
                    return 0.5f + (def != null ? MathUtil.Clamp01(def.Piety) : 0.5f);
                },
                Effect = e =>
                {
                    if (StoryUtil.CollectResidents(e.State, e.Settlement, e.People, p => p.IsAdult && !p.HasTrait(PersonTraits.Devout)) == 0
                        && StoryUtil.CollectResidents(e.State, e.Settlement, e.People, IsAdult) == 0) return false;
                    var seer = StoryUtil.TakeRandom(e.Rng, e.People);
                    seer.Traits |= PersonTraits.Devout;
                    seer.Faith = MathUtil.Clamp01(MathUtil.Sanitize(seer.Faith, 0.5f) + 0.3f);
                    e.Person = seer;
                    StoryUtil.ShiftDevotion(e.Civ, 0.06f);
                    StoryUtil.ShiftFaith(e.State, e.Settlement, 0.10f, 0f);
                    return true;
                },
                Templates = new[]
                {
                    "{person} of {settlement} woke from a fever speaking of visions, and the faithful gathered to listen.",
                    "In {settlement}, {person} claimed to have seen the face of the god; the {civ} grew devout.",
                    "Visions came to {person} upon the hill above {settlement}, and a new fervour swept the {race}.",
                }
            });

            // ---------------------------------------------------------------- Birth of a Hero
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.HeroBorn,
                Title = "Birth of a Hero",
                Description = "A young adult proves themself extraordinary: Hero + Brave traits and +0.15 skill.",
                Category = ChronicleCategory.Civilization,
                Importance = 2,
                CooldownYears = 40f,
                Conditions = new System.Func<WorldEventContext, bool>[] { e => StoryUtil.CivPopulation(e.State, e.Civ) >= 15 },
                SelectTarget = e =>
                {
                    e.Settlement = Largest(e, s => StoryUtil.CountResidents(e.State, s, p => p.Stage == LifeStage.Adult && !p.HasTrait(PersonTraits.Hero)) > 0);
                    return e.Settlement != null;
                },
                Effect = e =>
                {
                    if (StoryUtil.CollectResidents(e.State, e.Settlement, e.People, p => p.Stage == LifeStage.Adult && !p.HasTrait(PersonTraits.Hero)) == 0) return false;
                    var hero = StoryUtil.TakeRandom(e.Rng, e.People);
                    hero.Traits |= PersonTraits.Hero | PersonTraits.Brave;
                    hero.Skill = MathUtil.Clamp(MathUtil.Sanitize(hero.Skill, 1f) + 0.15f, 0.6f, 1.4f);
                    e.Person = hero;
                    return true;
                },
                Templates = new[]
                {
                    "{person} of {settlement} slew a great beast with bare hands; the {race} sang of a hero.",
                    "Songs began to be sung in {settlement} of {person}, whose deeds were already legend.",
                    "The {civ} found a champion in {person} of {settlement}.",
                }
            });
        }
    }
}
