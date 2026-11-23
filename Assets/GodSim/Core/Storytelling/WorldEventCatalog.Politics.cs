using System;
using System.Collections.Generic;

namespace GodSim.Core.Storytelling
{
    public static partial class WorldEventCatalog
    {
        /// <summary>A "leader just died" stays relevant for this many ticks (2 years).</summary>
        internal const long LeaderLossWindowTicks = SimTime.TicksPerYear * 2;
        /// <summary>Settlements within this distance (tiles) of a foreign settlement are neighbours.</summary>
        internal const int NeighbourDistance = 40;

        private static void AddPolitics(List<WorldEventDefinition> list)
        {
            // ---------------------------------------------------------------- Royal Wedding
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.RoyalWedding,
                Title = "Royal Wedding",
                Description = "Two friendly/allied civilizations seal a marriage: +20 opinion, small happiness in both capitals.",
                Category = ChronicleCategory.Diplomacy,
                Importance = 2,
                CooldownYears = 25f,
                CooldownAppliesToOther = true,
                SelectTarget = e =>
                {
                    e.OtherCiv = BestPartner(e, r => !r.AtWar && r.Stance >= DiplomaticStance.Friendly && r.Opinion < 100f, false);
                    if (e.OtherCiv == null) return false;
                    e.Settlement = StoryUtil.Capital(e.State, e.Civ);
                    e.OtherSettlement = StoryUtil.Capital(e.State, e.OtherCiv);
                    return true;
                },
                Effect = e =>
                {
                    var rel = e.State.Diplomacy.Get(e.Civ.Id, e.OtherCiv.Id);
                    StoryUtil.ShiftOpinion(rel, 20f);
                    StoryUtil.ShiftMood(e.State, e.Settlement, 0.05f);
                    StoryUtil.ShiftMood(e.State, e.OtherSettlement, 0.05f);
                    e.Person = StoryUtil.AlivePerson(e.State, e.Civ.LeaderId);
                    return true;
                },
                Templates = new[]
                {
                    "Bells rang in {settlement} as {leader} blessed a royal marriage with the {other}.",
                    "A royal wedding bound the {civ} and the {other}; the feasting in {settlement} lasted a month.",
                    "The {civ} and the {other} sealed their friendship with a royal marriage in {othersettlement}.",
                }
            });

            // ---------------------------------------------------------------- Border Incident
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.BorderIncident,
                Title = "Border Incident",
                Description = "A skirmish on the border with a neighbouring civilization: -20 opinion.",
                Category = ChronicleCategory.Diplomacy,
                Importance = 1,
                CooldownYears = 15f,
                CooldownAppliesToOther = true,
                SelectTarget = SelectNeighbour,
                Effect = e =>
                {
                    var rel = e.State.Diplomacy.Get(e.Civ.Id, e.OtherCiv.Id);
                    StoryUtil.ShiftOpinion(rel, -20f);
                    rel.HasContact = true;
                    StoryUtil.ShiftUnrest(e.Settlement, 0.05f);
                    return true;
                },
                Templates = new[]
                {
                    "Herders of {settlement} and {othersettlement} came to blows over a stream; the {civ} and the {other} traded bitter words.",
                    "A patrol of the {other} crossed into the lands of {settlement}, and blood was spilled on the border.",
                    "Boundary stones near {settlement} were found moved in the night; the {civ} blamed the {other}.",
                }
            });

            // ---------------------------------------------------------------- Succession Crisis
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.SuccessionCrisis,
                Title = "Succession Crisis",
                Description = "A leaderless civilization (or one whose leader just died) is torn by claimants: capital unrest +0.3.",
                Category = ChronicleCategory.Civilization,
                Importance = 2,
                CooldownYears = 20f,
                Conditions = new Func<WorldEventContext, bool>[] { e => IsLeaderless(e.State, e.Civ) || LeaderRecentlyLost(e.State, e.Civ) },
                SelectTarget = e =>
                {
                    var cap = StoryUtil.Capital(e.State, e.Civ);
                    if (cap == null || StoryUtil.AliveResidents(e.State, cap) < 8) return false;
                    e.Settlement = cap;
                    return true;
                },
                Weight = e => LeaderRecentlyLost(e.State, e.Civ) ? 5f : 0.5f,
                Effect = e =>
                {
                    StoryUtil.ShiftUnrest(e.Settlement, 0.30f);
                    StoryUtil.ShiftMood(e.State, e.Settlement, -0.05f);
                    return true;
                },
                Templates = new[]
                {
                    "With no clear heir, rival claimants tore at the {civ}; {settlement} seethed with unrest.",
                    "Factions gathered in the halls of {settlement}, each crying that its candidate should rule the {civ}.",
                    "The throne of the {civ} stood empty, and the streets of {settlement} filled with angry crowds.",
                }
            });

            // ---------------------------------------------------------------- Heroic Defence
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.HeroicDefence,
                Title = "Heroic Defence",
                Description = "A besieged settlement rallies: defense boosted for this run, besiegers lose 25% morale, a defender becomes a Hero.",
                Category = ChronicleCategory.War,
                Importance = 2,
                CooldownYears = 5f,
                BaseWeight = 3f,
                SelectTarget = e =>
                {
                    e.Settlement = Largest(e, s => StoryUtil.IsBesieged(e.State, s));
                    return e.Settlement != null;
                },
                Effect = e =>
                {
                    var s = e.Settlement;
                    int soldiers = StoryUtil.SoldierCount(e.State, s);
                    float boost = MathUtil.Clamp(10f + 0.5f * MathUtil.Clamp(MathUtil.Sanitize(s.DefenseStrength), 0f, 1e5f) + 2f * soldiers, 10f, 400f);
                    int def = (int)ServiceType.Defense;
                    s.ServiceOutput[def] = MathUtil.Clamp(MathUtil.Sanitize(s.ServiceOutput[def]) + boost, 0f, 1e6f);
                    s.DefenseStrength = MathUtil.Clamp(MathUtil.Sanitize(s.DefenseStrength) + boost, 0f, 1e6f);
                    var armies = e.State.Diplomacy.Armies.Items;
                    for (int i = 0; i < armies.Count; i++)
                    {
                        var a = armies[i];
                        if (a.IsAlive && a.State == ArmyState.Besieging && a.TargetSettlementId == s.Id && a.CivilizationId != e.Civ.Id)
                            a.Morale = MathUtil.Clamp01(MathUtil.Sanitize(a.Morale, 1f) - 0.25f);
                    }
                    if (StoryUtil.CollectResidents(e.State, s, e.People, p => p.IsAdult && (p.Job == JobType.Soldier || p.HasTrait(PersonTraits.Brave))) == 0)
                        StoryUtil.CollectResidents(e.State, s, e.People, IsAdult);
                    var hero = StoryUtil.TakeRandom(e.Rng, e.People);
                    if (hero != null) { hero.Traits |= PersonTraits.Hero | PersonTraits.Brave; e.Person = hero; }
                    StoryUtil.ShiftUnrest(s, -0.10f);
                    s.Memory[HeroicDefenceTickKey] = e.Tick;
                    e.Amount = boost;
                    return true;
                },
                Templates = new[]
                {
                    "Led by {person}, the defenders of {settlement} sallied forth and broke the spirit of the besiegers.",
                    "{settlement} would not fall: {person} held the gate through a night of fire and steel.",
                    "Against all hope the walls of {settlement} held, and {person} was hailed a hero of the {civ}.",
                }
            });

            // ---------------------------------------------------------------- Exodus
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.Exodus,
                Title = "Exodus",
                Description = "Miserable people (happiness < 0.35) emigrate to the nearest settlement of another civilization (10-25%, max 15).",
                Category = ChronicleCategory.Settlement,
                Importance = 1,
                CooldownYears = 20f,
                SelectTarget = e =>
                {
                    e.Settlement = BestBy(e, s =>
                    {
                        float h = MathUtil.Sanitize(s.Happiness, 0.6f);
                        return h < 0.35f && StoryUtil.AliveResidents(e.State, s) >= 8 ? -h : float.MinValue;
                    });
                    if (e.Settlement == null) return false;
                    e.OtherSettlement = NearestForeignSettlement(e, e.Settlement.Center, false);
                    if (e.OtherSettlement == null) return false;
                    e.OtherCiv = e.State.Civilizations.Get(e.OtherSettlement.CivilizationId);
                    return e.OtherCiv != null;
                },
                Effect = e =>
                {
                    var s = e.Settlement;
                    int pop = StoryUtil.AliveResidents(e.State, s);
                    int n = MathUtil.Clamp(MathUtil.RoundToInt(pop * e.Rng.Range(0.10f, 0.25f)), 2, 15);
                    n = Math.Min(n, pop - 3);
                    if (n <= 0) return false;
                    StoryUtil.CollectResidents(e.State, s, e.People, p => NotLeaderNorSoldier(e, p));
                    int moved = 0;
                    while (moved < n && e.People.Count > 0)
                    {
                        var p = StoryUtil.TakeRandom(e.Rng, e.People);
                        if (!p.IsAlive || p.SettlementId != s.Id) continue;
                        e.Ops.MovePersonToSettlement(p, e.OtherSettlement);
                        if (p.SettlementId == e.OtherSettlement.Id) moved++;
                    }
                    e.Count = moved;
                    return moved > 0;
                },
                Templates = new[]
                {
                    "Despairing of the {civ}, {count} families left {settlement} for the lands of the {other}.",
                    "{count} people of {settlement} packed their belongings and walked to {othersettlement}, never to return.",
                    "Misery drove {count} souls out of {settlement}; the {other} took them in.",
                }
            });

            // ---------------------------------------------------------------- Riots
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.Riots,
                Title = "Riots",
                Description = "Unrest > 0.4 boils over: gold and goods looted (15-25%), a building damaged; unrest vents (-0.15).",
                Category = ChronicleCategory.Settlement,
                Importance = 1,
                CooldownYears = 8f,
                SelectTarget = e =>
                {
                    e.Settlement = BestBy(e, s => MathUtil.Sanitize(s.Unrest) > 0.4f ? MathUtil.Sanitize(s.Unrest) : float.MinValue);
                    return e.Settlement != null;
                },
                Effect = e =>
                {
                    var s = e.Settlement;
                    e.Amount = StoryUtil.RemoveFraction(s, ResourceType.Gold, e.Rng.Range(0.15f, 0.25f));
                    StoryUtil.RemoveFraction(s, ResourceType.Goods, e.Rng.Range(0.15f, 0.25f));
                    if (StoryUtil.CollectBuildings(e.State, s, e.Buildings, b => b.IsComplete) > 0)
                    {
                        var b = StoryUtil.TakeRandom(e.Rng, e.Buildings);
                        b.Condition = MathUtil.Clamp(MathUtil.Sanitize(b.Condition, 1f) - 0.25f, 0.05f, 1f);
                    }
                    StoryUtil.ShiftUnrest(s, -0.15f);
                    StoryUtil.ShiftMood(e.State, s, -0.03f);
                    return true;
                },
                Templates = new[]
                {
                    "Riots broke out in {settlement}; mobs smashed shops and looted the treasury of the {civ}.",
                    "Angry crowds filled the squares of {settlement} and put a hall to the torch before the anger was spent.",
                    "Bread riots shook {settlement}, and {leader} had to hear the people's grievances.",
                }
            });

            // ---------------------------------------------------------------- Rebellion & Secession
            list.Add(new WorldEventDefinition
            {
                Key = WorldEventKeys.Secession,
                Title = "Rebellion & Secession",
                Description = "A restless, remote or foreign Village+ settlement breaks away as a new hostile civilization that keeps the parent's knowledge.",
                Category = ChronicleCategory.Civilization,
                Importance = 3,
                CooldownYears = 40f,
                BaseWeight = 6f,
                MinSettlements = 2,
                SelectTarget = e =>
                {
                    e.Settlement = Secession.FindCandidate(e.State, e.Civ);
                    return e.Settlement != null;
                },
                Weight = e => 1f + Secession.RestlessnessOf(e.Settlement),
                Effect = e =>
                {
                    var nc = Secession.Execute(e.Sim, e.Civ, e.Settlement);
                    if (nc == null) return false;
                    e.OtherCiv = nc;
                    e.Person = StoryUtil.AlivePerson(e.State, nc.LeaderId);
                    return true;
                },
                Templates = new[]
                {
                    "The people of {settlement} cast off the yoke of the {civ} and proclaimed the {other}.",
                    "Rebellion! {settlement} rose against the {civ}, and {person} proclaimed the {other}.",
                    "Weary of distant masters, {settlement} broke from the {civ}; the banners of the {other} were raised over its walls.",
                }
            });
        }

        // ------------------------------------------------------------------ helpers
        /// <summary>Settlement.Memory key stamped when a Heroic Defence boost was applied (tick).</summary>
        public const string HeroicDefenceTickKey = "story.heroicDefence.tick";

        internal static bool IsLeaderless(WorldState st, Civilization civ) => StoryUtil.AlivePerson(st, civ.LeaderId) == null;

        internal static bool LeaderRecentlyLost(WorldState st, Civilization civ)
        {
            if (!civ.Memory.TryGetValue(WorldEventSystem.LeaderLostTickKey, out float t)) return false;
            long lost = (long)MathUtil.Sanitize(t, -1e9f);
            return st.Tick >= lost && st.Tick - lost <= LeaderLossWindowTicks;
        }

        /// <summary>Nearest settlement of another alive civilization that is not at war with e.Civ (or must neighbour if requireNear).</summary>
        internal static Settlement NearestForeignSettlement(WorldEventContext e, Vec2i from, bool requireNear)
        {
            Settlement best = null;
            int bestD = requireNear ? NeighbourDistance * NeighbourDistance + 1 : int.MaxValue;
            var all = e.State.Settlements.Items;
            for (int i = 0; i < all.Count; i++)
            {
                var s = all[i];
                if (!s.IsAlive || s.CivilizationId == e.Civ.Id || s.CivilizationId < 0) continue;
                var c = e.State.Civilizations.Get(s.CivilizationId);
                if (c == null || !c.IsAlive || e.State.Diplomacy.AreAtWar(e.Civ.Id, c.Id)) continue;
                int d = s.Center.DistanceSqTo(from);
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        private static bool SelectNeighbour(WorldEventContext e)
        {
            int bestD = int.MaxValue;
            var ids = e.Civ.SettlementIds;
            for (int i = 0; i < ids.Count; i++)
            {
                var s = StoryUtil.Live(e.State, e.Civ, ids[i]);
                if (s == null) continue;
                var other = NearestForeignSettlement(e, s.Center, true);
                if (other == null) continue;
                int d = other.Center.DistanceSqTo(s.Center);
                if (d < bestD) { bestD = d; e.Settlement = s; e.OtherSettlement = other; }
            }
            if (e.OtherSettlement == null) return false;
            e.OtherCiv = e.State.Civilizations.Get(e.OtherSettlement.CivilizationId);
            return e.OtherCiv != null;
        }
    }
}
