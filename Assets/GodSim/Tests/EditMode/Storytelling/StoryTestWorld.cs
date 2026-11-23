using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.Storytelling;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Storytelling
{
    /// <summary>Hand-built 128x128 worlds with two civilizations for storytelling tests.</summary>
    internal static class StoryWorld
    {
        public sealed class World
        {
            public SimContext Ctx;
            public Civilization A;
            public Civilization B;
            public Settlement CapA;
            public Settlement CapB;
            public WorldState State => Ctx.State;
            public WorldOps Ops => Ctx.Ops;
        }

        public static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            db.Register(new TechDefinition { Key = "t.fire", Name = "Fire", Age = AgeId.Dawn, Cost = 10f });
            db.Register(new TechDefinition { Key = "t.bronze", Name = "Bronze", Age = AgeId.Bronze, Cost = 20f, PrerequisiteKeys = new[] { "t.fire" } });
            db.Register(new TechDefinition { Key = "t.iron", Name = "Iron", Age = AgeId.Iron, Cost = 30f, PrerequisiteKeys = new[] { "t.bronze" } });
            db.Register(new BuildingDefinition { Type = BuildingType.Mine, Name = "Mine", Category = BuildingCategory.Resource, Job = JobType.Miner, JobSlots = 4, BuildWork = 10f });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        /// <summary>Humans (civ A) at (30,30) and Elves (civ B) at (100,100); tick = year 100, spring.</summary>
        public static World Create(ulong seed = 11, int adults = 12, Biome fill = Biome.Grassland)
        {
            var ctx = TestWorlds.CreateContext(CreateDb(), 128, seed, fill);
            var w = new World { Ctx = ctx };
            w.CapA = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(30, 30), adults, out w.A);
            w.CapB = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(100, 100), adults, out w.B);
            ctx.State.Tick = SimTime.TicksPerYear * 100L;
            return w;
        }

        public static Settlement AddTown(World w, Civilization civ, Vec2i at, int people, RaceId race)
        {
            var s = w.Ops.FoundSettlement(civ, at);
            for (int i = 0; i < people; i++) w.Ops.CreatePerson(s, race, i % 2 == 0 ? Sex.Female : Sex.Male, 20f + (i % 10));
            return s;
        }

        public static List<Building> AddBuildings(World w, Settlement s, BuildingType type, int n, bool complete)
        {
            var list = new List<Building>();
            for (int i = 0; i < n; i++)
            {
                var site = w.Ops.FindFreeSite(new Vec2i(s.Center.X + 2, s.Center.Y + 2), 1, 1, 10);
                Assert.IsTrue(site.IsValid, "no free site");
                var b = w.Ops.CreateBuilding(s, type, site, 0, complete);
                Assert.IsNotNull(b);
                list.Add(b);
            }
            return list;
        }

        public static void SetSeason(World w, Season season)
        {
            long year = w.State.Tick / SimTime.TicksPerYear;
            w.State.Tick = year * SimTime.TicksPerYear + (int)season * SimTime.TicksPerSeason + 5;
        }

        public static void SetTile(World w, int x, int y, Biome biome) => w.State.Map.Biomes[w.State.Map.Index(x, y)] = biome;

        public static void SetFlag(World w, int x, int y, TileFlags f) => w.State.Map.Set(w.State.Map.Index(x, y), f);

        public static void Fire(World w, Civilization civ, string key)
        {
            Assert.IsTrue(WorldEventSystem.TryFire(w.Ctx, civ, key), "expected event to fire: " + key);
            w.Ops.FlushRemovals();
        }

        public static bool TryFire(World w, Civilization civ, string key)
        {
            bool fired = WorldEventSystem.TryFire(w.Ctx, civ, key);
            w.Ops.FlushRemovals();
            return fired;
        }

        public static int Alive(World w, Settlement s)
        {
            int n = 0;
            foreach (int id in s.ResidentIds) { var p = w.State.People.Get(id); if (p != null && p.IsAlive) n++; }
            return n;
        }

        public static List<Person> Residents(World w, Settlement s)
        {
            var list = new List<Person>();
            foreach (int id in s.ResidentIds) { var p = w.State.People.Get(id); if (p != null && p.IsAlive) list.Add(p); }
            return list;
        }

        public static void SetMood(World w, Settlement s, float happiness)
        {
            s.Happiness = happiness;
            foreach (var p in Residents(w, s)) p.Happiness = happiness;
        }

        public static ChronicleEntry LastChronicle(World w)
        {
            var e = w.State.Chronicle.Entries;
            Assert.IsTrue(e.Count > 0, "chronicle empty");
            return e[e.Count - 1];
        }

        /// <summary>A world where most events have a valid target (used by determinism / sanity tests).</summary>
        public static World CreateRich(ulong seed)
        {
            var w = Create(seed, 16);
            w.A.Age = AgeId.Iron;
            w.B.Age = AgeId.Bronze;
            w.A.KnownTechs.Add(0); w.A.KnownTechs.Add(1); w.A.KnownTechs.Add(2);
            w.CapA.Tier = SettlementTier.Town;
            SetMood(w, w.CapA, 0.8f);
            w.CapA.Stockpile.SetRaw(ResourceType.Food, 120f);
            w.CapA.Stockpile.SetRaw(ResourceType.Gold, 60f);
            w.CapA.Stockpile.SetRaw(ResourceType.Goods, 30f);
            AddBuildings(w, w.CapA, BuildingType.House, 6, true);
            var site = AddBuildings(w, w.CapA, BuildingType.House, 1, false)[0];
            site.CostPaid = true;
            site.ConstructionProgress = 0.4f;
            var mine = AddBuildings(w, w.CapA, BuildingType.Mine, 1, true)[0];
            var res = Residents(w, w.CapA);
            for (int i = 0; i < 3; i++) Assert.IsTrue(w.Ops.AssignJob(res[i], mine, JobType.Miner));
            SetTile(w, 35, 29, Biome.Hills); SetTile(w, 36, 29, Biome.Hills);
            SetFlag(w, 26, 33, TileFlags.Ruins);
            SetFlag(w, 27, 27, TileFlags.Sacred);
            for (int x = 104; x < 107; x++) SetTile(w, x, 104, Biome.Lake);

            var far = AddTown(w, w.A, new Vec2i(100, 30), 10, RaceId.Human);
            far.Tier = SettlementTier.Village;
            far.Unrest = 0.7f;
            var small = AddTown(w, w.A, new Vec2i(30, 70), 5, RaceId.Human);
            SetMood(w, small, 0.3f);
            var colB = AddTown(w, w.B, new Vec2i(75, 45), 9, RaceId.Elf);
            colB.Tier = SettlementTier.Village;
            colB.Unrest = 0.5f;

            var rel = w.State.Diplomacy.Get(w.A.Id, w.B.Id);
            rel.Stance = DiplomaticStance.Friendly;
            rel.Opinion = 20f;
            rel.HasContact = true;
            return w;
        }

        public static void AssertSane(World w)
        {
            var st = w.State;
            foreach (var s in st.Settlements.Items)
            {
                if (!s.IsAlive) continue;
                Unit(s.Happiness, "settlement happiness");
                Unit(s.Unrest, "unrest");
                Unit(s.Faith, "faith");
                Unit(s.Health, "health");
                Finite(s.DefenseStrength, "defense");
                for (int i = 0; i < s.ServiceOutput.Length; i++) Finite(s.ServiceOutput[i], "service");
                foreach (var r in EnumUtil.AllResources)
                {
                    float v = s.Stockpile.Get(r);
                    Finite(v, "stock " + r);
                    Assert.IsTrue(v >= 0f, "stock " + r);
                    Assert.IsTrue(v <= Math.Max(s.Stockpile.Capacity, 150f) + 1e-3f, "stock " + r);
                }
            }
            foreach (var p in st.People.Items)
            {
                if (!p.IsAlive) continue;
                Unit(p.Happiness, "person happiness");
                Unit(p.Faith, "person faith");
                Unit(p.Health, "person health");
                Assert.That(p.Skill >= 0.6f - 1e-4f && p.Skill <= 1.4f + 1e-4f, "skill " + p.Skill);
            }
            foreach (var b in st.Buildings.Items)
            {
                Unit(b.Condition, "condition");
                Unit(b.ConstructionProgress, "progress");
            }
            foreach (var c in st.Civilizations.Items)
            {
                Unit(c.Devotion, "devotion");
                Finite(c.ResearchPool, "pool");
                Finite(c.TotalResearch, "total research");
                Assert.IsTrue(c.ResearchPool >= 0f, "pool");
                foreach (var kv in c.Memory) Finite(kv.Value, "memory " + kv.Key);
            }
            foreach (var r in st.Diplomacy.AllRelations)
                Assert.That(r.Opinion >= -100f && r.Opinion <= 100f, "opinion " + r.Opinion);
            foreach (var a in st.Diplomacy.Armies.Items) Unit(a.Morale, "morale");
            for (int i = 0; i < st.Map.TileCount; i++) Finite(st.Map.Resource[i], "tile resource");
        }

        /// <summary>Deterministic fingerprint of everything storytelling touches.</summary>
        public static string Signature(World w)
        {
            var st = w.State;
            var sb = new StringBuilder();
            sb.Append(st.Tick).Append('|').Append(st.Rng.State).Append('|').Append(st.LivingPopulation).Append('|').Append(st.Civilizations.Count).Append('\n');
            foreach (var e in st.Chronicle.Entries)
                sb.Append(e.Tick).Append(':').Append(e.Category).Append(':').Append(e.Importance).Append(':').Append(e.CivilizationId).Append(':').Append(e.Text).Append('\n');
            foreach (var c in st.Civilizations.Items)
            {
                sb.Append(c.Id).Append(c.IsAlive).Append(c.Devotion.ToString("R")).Append(c.TotalResearch.ToString("R"));
                var keys = new List<string>(c.Memory.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var k in keys) sb.Append(k).Append('=').Append(c.Memory[k].ToString("R")).Append(';');
                sb.Append('\n');
            }
            foreach (var s in st.Settlements.Items)
                sb.Append(s.Id).Append(s.CivilizationId).Append(s.Happiness.ToString("R")).Append(s.Unrest.ToString("R")).Append(s.Stockpile.Total().ToString("R")).Append('\n');
            return sb.ToString();
        }

        private static void Unit(float v, string what)
        {
            Finite(v, what);
            Assert.That(v >= 0f && v <= 1f, what + " out of [0,1]: " + v);
        }

        private static void Finite(float v, string what) => Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), what + " not finite");
    }
}
