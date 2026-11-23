using System;
using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Persistence;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Persistence.Storage
{
    public class StateHasherTests
    {
        private static readonly string[] MemoryKeys = { "ai.lastExpand", "econ.food", "zeta", "alpha", "diplo.mood" };

        /// <summary>Builds a small but feature-complete world deterministically.</summary>
        private static WorldState Build(bool reverseMemoryOrder = false)
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db, 32, 11);
            var state = ctx.State;
            var s1 = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(8, 8), 6, out var civ1);
            var s2 = TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(24, 24), 4, out var civ2);

            var house = ctx.Ops.CreateBuilding(s1, BuildingType.House, ctx.Ops.FindFreeSite(new Vec2i(12, 8), 1, 1, 6), 0, true);
            Assert.IsNotNull(house);
            Assert.IsTrue(ctx.Ops.AssignHome(state.People.Get(s1.ResidentIds[0]), house));

            var rel = state.Diplomacy.Get(civ1.Id, civ2.Id);
            rel.Opinion = -20f;
            rel.HasContact = true;
            var war = new War { Id = state.Ids.War(), AttackerId = civ2.Id, DefenderId = civ1.Id, StartTick = 3, CasusBelli = "border" };
            state.Diplomacy.Wars.Add(war);
            rel.WarId = war.Id;

            var army = ctx.Ops.CreateArmy(civ2, s2, new List<Person> { state.People.Get(s2.ResidentIds[0]) });
            army.Path.Add(new Vec2i(20, 20));
            army.Path.Add(new Vec2i(19, 19));

            civ1.KnownTechs.Add(0);
            s1.BuildQueue.Add(new BuildOrder { Type = BuildingType.Farm, Priority = 2f, Reason = "food", RequestedTick = 4 });
            s1.Stockpile.SetRaw(ResourceType.Gold, 12f);
            state.God.CooldownUntil[3] = 40;
            state.Weather.Rainfall = 0.8f;
            state.Stats.PeakPopulation = 10;
            state.Map.Flags[5] |= TileFlags.River;
            state.Tick = 17;

            int n = MemoryKeys.Length;
            for (int k = 0; k < n; k++)
            {
                string key = MemoryKeys[reverseMemoryOrder ? n - 1 - k : k];
                float value = Array.IndexOf(MemoryKeys, key) + 0.5f;
                s1.Memory[key] = value;
                civ1.Memory[key] = value * 2f;
                state.Memory[key] = value * 3f;
            }
            return state;
        }

        private static Settlement FirstSettlement(WorldState s) => s.Settlements.Items[0];
        private static Civilization FirstCiv(WorldState s) => s.Civilizations.Items[0];

        [Test]
        public void IdenticalWorldsHashEqual()
        {
            ulong a = StateHasher.Compute(Build());
            ulong b = StateHasher.Compute(Build());
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(0UL, a);
        }

        [Test]
        public void ComputeIsPureAndRepeatable()
        {
            var w = Build();
            ulong rngBefore = w.Rng.State;
            ulong a = StateHasher.Compute(w);
            ulong b = StateHasher.Compute(w);
            Assert.AreEqual(a, b);
            Assert.AreEqual(rngBefore, w.Rng.State, "hashing must not consume randomness");
            Assert.AreEqual(16, StateHasher.Format(a).Length);
        }

        [Test]
        public void MemoryInsertionOrderDoesNotMatter()
        {
            Assert.AreEqual(StateHasher.Compute(Build(false)), StateHasher.Compute(Build(true)));

            // Removing and re-adding a key changes dictionary internals but not content.
            var w = Build();
            ulong before = StateHasher.Compute(w);
            var mem = FirstSettlement(w).Memory;
            float v = mem["econ.food"];
            mem.Remove("econ.food");
            mem["late.key"] = 1f;
            mem["econ.food"] = v;
            mem.Remove("late.key");
            Assert.AreEqual(before, StateHasher.Compute(w));
        }

        [Test]
        public void ChangingAnySampledFieldChangesHash()
        {
            ulong baseline = StateHasher.Compute(Build());
            var mutations = new List<KeyValuePair<string, Action<WorldState>>>
            {
                M("person health", s => s.People.Items[0].Health -= 0.01f),
                M("person name", s => s.People.Items[1].Name += "x"),
                M("person position", s => s.People.Items[2].Position = new Vec2i(1, 1)),
                M("person traits", s => s.People.Items[0].Traits ^= PersonTraits.Hero),
                M("map tile flag", s => s.Map.Flags[100] |= TileFlags.Sacred),
                M("map elevation", s => s.Map.Elevation[3] += 0.001f),
                M("map biome", s => s.Map.Biomes[7] = Biome.Desert),
                M("map owner", s => s.Map.Owner[0] = 99),
                M("map resource", s => s.Map.Resource[9] = 5f),
                M("sea level", s => s.Map.SeaLevel = 0.1f),
                M("settlement memory key added", s => FirstSettlement(s).Memory["new.key"] = 0f),
                M("settlement memory key renamed", s => { var m = FirstSettlement(s).Memory; float v = m["zeta"]; m.Remove("zeta"); m["zetb"] = v; }),
                M("settlement memory value", s => FirstSettlement(s).Memory["alpha"] += 1f),
                M("memory values swapped", s => { var m = FirstSettlement(s).Memory; float a = m["alpha"]; m["alpha"] = m["zeta"]; m["zeta"] = a; }),
                M("civ memory", s => FirstCiv(s).Memory["alpha"] = -1f),
                M("world memory", s => s.Memory["x.y"] = 2f),
                M("rng state", s => s.Rng.NextUInt()),
                M("stockpile amount", s => FirstSettlement(s).Stockpile.SetRaw(ResourceType.Food, 1f)),
                M("stockpile capacity", s => FirstSettlement(s).Stockpile.Capacity += 1f),
                M("stockpile averages", s => FirstSettlement(s).Stockpile.AvgIncome[2] = 0.5f),
                M("stockpile period flows", s => FirstSettlement(s).Stockpile.SetPeriodFlows(ResourceType.Wood, 0.5f, 0.25f)),
                M("relation opinion", s => s.Diplomacy.Get(1, 2).Opinion += 1f),
                M("relation stance", s => s.Diplomacy.Get(1, 2).Stance = DiplomaticStance.Allied),
                M("war score", s => s.Diplomacy.Wars[0].WarScore = 3f),
                M("war casus belli", s => s.Diplomacy.Wars[0].CasusBelli = "honor"),
                M("army path", s => s.Diplomacy.Armies.Items[0].Path.Add(new Vec2i(18, 18))),
                M("army morale", s => s.Diplomacy.Armies.Items[0].Morale = 0.5f),
                M("building residents", s => s.Buildings.Items[s.Buildings.Count - 1].Residents.Add(999)),
                M("building condition", s => s.Buildings.Items[0].Condition = 0.9f),
                M("settlement build queue", s => FirstSettlement(s).BuildQueue[0].Reason = "wood"),
                M("settlement service coverage", s => FirstSettlement(s).ServiceCoverage[1] = 0.3f),
                M("settlement name", s => FirstSettlement(s).Name = "Elsewhere"),
                M("civ known techs", s => FirstCiv(s).KnownTechs.Add(5)),
                M("civ unlocked building", s => FirstCiv(s).UnlockedBuildings[20] = true),
                M("civ modifiers", s => FirstCiv(s).Modifiers.Research = 1.1f),
                M("civ age", s => FirstCiv(s).Age = AgeId.Bronze),
                M("god cooldown", s => s.God.CooldownUntil[0] = 5),
                M("god mana", s => s.God.Mana += 1f),
                M("weather", s => s.Weather.StormIntensity = 0.2f),
                M("chronicle text", s => { var e = s.Chronicle.Entries[0]; e.Text += "!"; s.Chronicle.Entries[0] = e; }),
                M("chronicle added", s => s.Chronicle.Add(new ChronicleEntry { Tick = 1, Text = "omen" })),
                M("stats deaths", s => s.Stats.DeathsByCause[2]++),
                M("stats births", s => s.Stats.TotalBirths++),
                M("ids", s => s.Ids.NextPersonId++),
                M("tick", s => s.Tick++),
                M("seed", s => s.Seed ^= 1UL),
                M("store order", s => { var p = s.People.Items[0]; s.People.Remove(p.Id); s.People.Add(p); }),
            };

            var seen = new HashSet<ulong> { baseline };
            foreach (var m in mutations)
            {
                var w = Build();
                m.Value(w);
                ulong h = StateHasher.Compute(w);
                Assert.AreNotEqual(baseline, h, "mutation not detected: " + m.Key);
                seen.Add(h);
            }
            Assert.AreEqual(mutations.Count + 1, seen.Count, "distinct mutations should produce distinct hashes");
        }

        [Test]
        public void SectionsPinpointTheDivergence()
        {
            var a = Build();
            var b = Build();
            b.People.Items[0].Health = 0.25f;
            ulong[] sa = StateHasher.ComputeSections(a);
            ulong[] sb = StateHasher.ComputeSections(b);
            Assert.AreEqual(StateHasher.SectionCount, sa.Length);
            for (int i = 0; i < sa.Length; i++)
            {
                if ((StateHashSection)i == StateHashSection.People) Assert.AreNotEqual(sa[i], sb[i]);
                else Assert.AreEqual(sa[i], sb[i], "unexpected divergence in " + (StateHashSection)i);
            }
            Assert.AreEqual(sb[(int)StateHashSection.People], StateHasher.ComputeSection(b, StateHashSection.People));
        }

        [Test]
        public void NaNPayloadsHashTheSame()
        {
            var a = Build();
            var b = Build();
            a.People.Items[0].Health = float.NaN;
            b.People.Items[0].Health = BitConverter.Int32BitsToSingle(0x7FC12345);
            Assert.AreEqual(StateHasher.Compute(a), StateHasher.Compute(b));
        }

        [Test]
        public void MinimalWorldHashesAndNullThrows()
        {
            var w1 = new WorldState(5, new WorldMap(16, 16));
            var w2 = new WorldState(5, new WorldMap(16, 16));
            Assert.AreEqual(StateHasher.Compute(w1), StateHasher.Compute(w2));
            Assert.AreNotEqual(StateHasher.Compute(w1), StateHasher.Compute(new WorldState(6, new WorldMap(16, 16))));
            Assert.AreNotEqual(StateHasher.Compute(w1), StateHasher.Compute(new WorldState(5, new WorldMap(16, 17))));
            Assert.Throws<ArgumentNullException>(() => StateHasher.Compute(null));
        }

        private static KeyValuePair<string, Action<WorldState>> M(string name, Action<WorldState> mutate) =>
            new KeyValuePair<string, Action<WorldState>>(name, mutate);
    }
}
