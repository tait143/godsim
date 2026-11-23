using System;
using System.Collections.Generic;

namespace GodSim.Core.Divine
{
    public static partial class GodPowerEffects
    {
        /// <summary>Bless: people in range gain +0.2 happiness, +0.1 health and a little faith.</summary>
        public static GodPowerOutcome Bless(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var people = new List<Person>();
            GodPowerArea.PeopleInRange(ctx.State, target, radius, people);
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                p.Happiness = GodPowerArea.Add01(p.Happiness, 0.2f);
                p.Health = GodPowerArea.Add01(p.Health, 0.1f);
                p.Faith = GodPowerArea.Add01(p.Faith, 0.05f);
            }
            var o = GodPowerOutcome.Ok(people.Count > 0 ? Plural(people.Count, "mortal basks", "mortals bask") + " in divine favor." : "The blessing fell on empty land.");
            o.Affected = people.Count;
            return o;
        }

        /// <summary>Rain: 48 ticks of rain, drought ended, +0.15 moisture in range, fires in range extinguished.</summary>
        public static GodPowerOutcome Rain(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var state = ctx.State;
            var map = state.Map;
            var w = state.Weather;
            w.RainTicksRemaining = Math.Max(48, w.RainTicksRemaining);
            w.DroughtTicksRemaining = 0;

            int tiles = 0, doused = 0;
            GodPowerArea.Bounds(map, target, radius, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!GodPowerArea.InCircle(target, x, y, radius)) continue;
                    int i = map.Index(x, y);
                    map.Moisture[i] = GodPowerArea.Add01(map.Moisture[i], 0.15f);
                    if (map.Has(i, TileFlags.Burning)) { map.Clear(i, TileFlags.Burning); doused++; }
                    tiles++;
                }
            var buildings = new List<Building>();
            GodPowerArea.BuildingsInRange(state, target, radius, buildings);
            for (int i = 0; i < buildings.Count; i++)
                if (buildings[i].IsOnFire) { buildings[i].IsOnFire = false; doused++; }

            GodPowerTerrain.PublishChanged(ctx, target, radius);
            var o = GodPowerOutcome.Ok(doused > 0 ? "Rain falls and quenches " + Plural(doused, "fire", "fires") + "." : "Soft rain falls upon the land.");
            o.Affected = tiles;
            o.TilesChanged = tiles;
            return o;
        }

        /// <summary>Fertility: +0.25 fertility and FertileSoil on land in range; up to 3 immediate births per settlement in range.</summary>
        public static GodPowerOutcome Fertility(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var state = ctx.State;
            var map = state.Map;
            int tiles = 0;
            GodPowerArea.Bounds(map, target, radius, out int x0, out int y0, out int x1, out int y1);
            for (int y = y0; y <= y1; y++)
                for (int x = x0; x <= x1; x++)
                {
                    if (!GodPowerArea.InCircle(target, x, y, radius)) continue;
                    int i = map.Index(x, y);
                    if (map.IsWater(i)) continue;
                    map.Fertility[i] = GodPowerArea.Add01(map.Fertility[i], 0.25f);
                    map.Set(i, TileFlags.FertileSoil);
                    tiles++;
                }
            if (tiles > 0) GodPowerTerrain.PublishChanged(ctx, target, radius);

            var settlements = new List<Settlement>();
            GodPowerArea.SettlementsInRange(state, target, radius, GodPowerArea.SettlementReach, settlements);
            int births = 0;
            int popCap = ctx.Config != null ? ctx.Config.MaxPopulation : int.MaxValue;
            for (int i = 0; i < settlements.Count; i++)
                births += BlessCouples(ctx, settlements[i], 3, popCap);

            var o = GodPowerOutcome.Ok("The land grows rich" + (births > 0 ? " and " + Plural(births, "child is", "children are") + " born." : "."));
            o.Affected = tiles;
            o.TilesChanged = tiles;
            o.Created = births;
            return o;
        }

        /// <summary>Births for up to maxCouples couples (married couples first, then unpartnered adults paired in order).</summary>
        private static int BlessCouples(SimContext ctx, Settlement s, int maxCouples, int popCap)
        {
            var state = ctx.State;
            var females = new List<Person>();
            var males = new List<Person>();
            var ids = s.ResidentIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var p = state.People.Get(ids[k]);
                if (p == null || !p.IsAlive || p.Stage != LifeStage.Adult) continue;
                var race = ctx.Db.Races[(int)p.Race];
                if (race == null) continue;
                if (p.Sex == Sex.Female) { if (p.ChildrenCount < Math.Max(1, race.MaxChildren)) females.Add(p); }
                else males.Add(p);
            }
            if (females.Count == 0 || males.Count == 0) return 0;

            var usedMales = new HashSet<int>();
            var mothers = new List<Person>();
            var fathers = new List<Person>();
            // Married couples first.
            for (int f = 0; f < females.Count && mothers.Count < maxCouples; f++)
            {
                var mom = females[f];
                if (mom.SpouseId < 0) continue;
                var dad = state.People.Get(mom.SpouseId);
                if (dad == null || !dad.IsAlive || dad.Sex != Sex.Male || dad.Stage == LifeStage.Child || usedMales.Contains(dad.Id)) continue;
                usedMales.Add(dad.Id);
                mothers.Add(mom);
                fathers.Add(dad);
            }
            // Then unpartnered adults, paired in residence order.
            int m = 0;
            for (int f = 0; f < females.Count && mothers.Count < maxCouples; f++)
            {
                var mom = females[f];
                if (mom.SpouseId >= 0 || mothers.Contains(mom)) continue;
                while (m < males.Count && (males[m].SpouseId >= 0 || usedMales.Contains(males[m].Id))) m++;
                if (m >= males.Count) break;
                var dad = males[m++];
                usedMales.Add(dad.Id);
                mothers.Add(mom);
                fathers.Add(dad);
            }

            int births = 0;
            for (int c = 0; c < mothers.Count; c++)
            {
                if (state.People.Count >= popCap) break;
                var mom = mothers[c];
                var sex = ctx.Rng.Chance(0.5f) ? Sex.Female : Sex.Male;
                ctx.Ops.CreatePerson(s, mom.Race, sex, 0f, mom.Id, fathers[c].Id, true);
                births++;
            }
            return births;
        }

        /// <summary>Inspire: each civilization with a settlement in range gains 50 + 2 x population research.</summary>
        public static GodPowerOutcome Inspire(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var state = ctx.State;
            var settlements = new List<Settlement>();
            GodPowerArea.SettlementsInRange(state, target, radius, GodPowerArea.SettlementReach, settlements);
            var civIds = new List<int>();
            GodPowerArea.CivsOf(state, settlements, civIds);
            float total = 0f;
            int lastCiv = -1;
            for (int i = 0; i < civIds.Count; i++)
            {
                var civ = state.Civilizations.Get(civIds[i]);
                if (civ == null || !civ.IsAlive) continue;
                float bonus = 50f + 2f * CivPopulation(state, civ);
                civ.ResearchPool = Math.Max(0f, MathUtil.Sanitize(civ.ResearchPool) + bonus);
                civ.TotalResearch = Math.Max(0f, MathUtil.Sanitize(civ.TotalResearch) + bonus);
                total += bonus;
                lastCiv = civ.Id;
            }
            var o = GodPowerOutcome.Ok(civIds.Count > 0 ? "Visions of insight grant " + MathUtil.RoundToInt(total) + " research." : "No scholars were near enough to hear.");
            o.Affected = civIds.Count;
            o.CivilizationId = civIds.Count == 1 ? lastCiv : -1;
            return o;
        }

        internal static int CivPopulation(WorldState state, Civilization civ)
        {
            int pop = 0;
            for (int k = 0; k < civ.SettlementIds.Count; k++)
            {
                var s = state.Settlements.Get(civ.SettlementIds[k]);
                if (s != null && s.IsAlive) pop += s.Population;
            }
            return pop;
        }

        /// <summary>Heal: people in range are restored to full health.</summary>
        public static GodPowerOutcome Heal(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var people = new List<Person>();
            GodPowerArea.PeopleInRange(ctx.State, target, radius, people);
            int healed = 0;
            for (int i = 0; i < people.Count; i++)
            {
                var p = people[i];
                if (p.Health < 1f) healed++;
                p.Health = 1f;
            }
            var o = GodPowerOutcome.Ok(people.Count > 0 ? Plural(healed, "mortal is", "mortals are") + " made whole." : "There was no one here to heal.");
            o.Affected = people.Count;
            return o;
        }

        /// <summary>Bountiful Harvest: +40 food for every settlement in range (bounded by storage capacity).</summary>
        public static GodPowerOutcome BountifulHarvest(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var settlements = new List<Settlement>();
            GodPowerArea.SettlementsInRange(ctx.State, target, radius, GodPowerArea.SettlementReach, settlements);
            float stored = 0f;
            for (int i = 0; i < settlements.Count; i++) stored += settlements[i].Stockpile.Add(ResourceType.Food, 40f);
            var o = GodPowerOutcome.Ok(settlements.Count > 0 ? "Granaries swell with " + MathUtil.RoundToInt(stored) + " food." : "The harvest blessing found no granary.");
            o.Affected = settlements.Count;
            if (settlements.Count == 1) { o.SettlementId = settlements[0].Id; o.CivilizationId = settlements[0].CivilizationId; }
            return o;
        }

        /// <summary>
        /// Prophet: the most faithful adult of the nearest settlement in range becomes a Hero, Devout and Charismatic
        /// prophet; their civilization gains +0.2 devotion. Chronicled.
        /// </summary>
        public static GodPowerOutcome Prophet(SimContext ctx, Vec2i target, int radius)
        {
            if (!Prepare(ctx, target, ref radius, out var fail)) return fail;
            var state = ctx.State;
            var prophet = FindProphetCandidate(state, target, radius, out var s);
            if (prophet == null) return GodPowerOutcome.Fail("no worthy mortal within range");

            prophet.Traits |= PersonTraits.Hero | PersonTraits.Devout | PersonTraits.Charismatic;
            prophet.Faith = 1f;
            prophet.Happiness = GodPowerArea.Add01(prophet.Happiness, 0.2f);
            s.Faith = GodPowerArea.Add01(s.Faith, 0.1f);
            var civ = state.Civilizations.Get(s.CivilizationId);
            if (civ != null)
            {
                civ.Devotion = GodPowerArea.Add01(civ.Devotion, 0.2f);
                civ.Memory["divine.prophetId"] = prophet.Id;
                civ.Memory["divine.prophetTick"] = state.Tick;
            }
            var o = GodPowerOutcome.Ok(prophet.Name + " of " + s.Name + " rises as your prophet.");
            o.Affected = 1;
            o.SettlementId = s.Id;
            o.CivilizationId = s.CivilizationId;
            o.ChronicleText = prophet.Name + " of " + s.Name + " was touched by the divine and rose as a prophet" +
                              (civ != null ? " of the " + civ.Name : "") + ".";
            o.ChronicleImportance = 2;
            return o;
        }

        /// <summary>Best adult (by faith, skill and piety traits; non-heroes first; ties by id) in the nearest settlement within radius + reach.</summary>
        internal static Person FindProphetCandidate(WorldState state, Vec2i target, int radius, out Settlement settlement)
        {
            settlement = GodPowerArea.NearestSettlement(state, target, radius + GodPowerArea.SettlementReach);
            if (settlement == null) return null;
            Person best = null;
            float bestScore = float.NegativeInfinity;
            var ids = settlement.ResidentIds;
            for (int k = 0; k < ids.Count; k++)
            {
                var p = state.People.Get(ids[k]);
                if (p == null || !p.IsAlive || !p.IsAdult) continue;
                float score = MathUtil.Sanitize(p.Faith) + 0.5f * MathUtil.Sanitize(p.Skill, 1f);
                if (p.HasTrait(PersonTraits.Devout)) score += 0.3f;
                if (p.HasTrait(PersonTraits.Wise)) score += 0.2f;
                if (p.HasTrait(PersonTraits.Charismatic)) score += 0.2f;
                if (p.Stage == LifeStage.Elder) score += 0.1f;
                if (p.HasTrait(PersonTraits.Hero)) score -= 2f;
                if (best == null || score > bestScore || (score == bestScore && p.Id < best.Id)) { best = p; bestScore = score; }
            }
            return best;
        }
    }
}
