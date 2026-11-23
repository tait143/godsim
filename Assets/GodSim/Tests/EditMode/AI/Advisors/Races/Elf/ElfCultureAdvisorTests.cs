using System;
using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Core.Races;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public sealed class ElfCultureAdvisorTests
    {
        private static readonly Vec2i ElfAt = new Vec2i(16, 16);

        // ------------------------------------------------------------------ fixtures
        private static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            RaceCatalog.Register(db);
            Reg(db, BuildingType.WoodcutterCamp, AgeId.Dawn, BuildingCategory.Resource, d => { d.Job = JobType.Woodcutter; d.JobSlots = 2; d.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Wood, 0.2f) }; });
            Reg(db, BuildingType.Sawmill, AgeId.Industrial, BuildingCategory.Resource, d => { d.Job = JobType.Laborer; d.JobSlots = 3; d.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Wood, 0.25f) }; });
            Reg(db, BuildingType.Herbalist, AgeId.Bronze, BuildingCategory.Health, d => d.Services = new[] { new ServiceAmount(ServiceType.Health, 2f) });
            Reg(db, BuildingType.Scriptorium, AgeId.Iron, BuildingCategory.Education, d => d.Services = new[] { new ServiceAmount(ServiceType.Research, 2f) });
            Reg(db, BuildingType.Library, AgeId.Classical, BuildingCategory.Education, d => d.Services = new[] { new ServiceAmount(ServiceType.Research, 3f) });
            Reg(db, BuildingType.Theater, AgeId.Classical, BuildingCategory.Culture, null);
            Reg(db, BuildingType.Arena, AgeId.Classical, BuildingCategory.Culture, null);
            Reg(db, BuildingType.Barracks, AgeId.Iron, BuildingCategory.Military, null);
            Reg(db, BuildingType.Watchtower, AgeId.Iron, BuildingCategory.Military, null);
            Reg(db, BuildingType.Factory, AgeId.Industrial, BuildingCategory.Industry, d => d.InputsPerWorker = new[] { new ResourceAmount(ResourceType.Metal, 0.1f), new ResourceAmount(ResourceType.Fuel, 0.1f) });
            Reg(db, BuildingType.Foundry, AgeId.Industrial, BuildingCategory.Industry, d => d.InputsPerWorker = new[] { new ResourceAmount(ResourceType.Ore, 0.1f), new ResourceAmount(ResourceType.Fuel, 0.1f) });
            Reg(db, BuildingType.CoalMine, AgeId.Industrial, BuildingCategory.Resource, d => d.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Fuel, 0.2f) });
            Reg(db, BuildingType.OilWell, AgeId.Modern, BuildingCategory.Resource, d => d.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Fuel, 0.3f) });
            Reg(db, BuildingType.FusionReactor, AgeId.Ascension, BuildingCategory.Infrastructure, d => d.Services = new[] { new ServiceAmount(ServiceType.Power, 20f) });
            Reg(db, BuildingType.HydroponicsTower, AgeId.Ascension, BuildingCategory.Food, null);
            Reg(db, BuildingType.WorldTree, AgeId.Iron, BuildingCategory.Wonder, d =>
            {
                d.AllowedRaces = RaceMask.Elf;
                d.MinTier = SettlementTier.Village;
                d.IsWonder = true;
                d.UniquePerCivilization = true;
                d.Width = d.Height = 3;
                d.Cost = new[] { new ResourceAmount(ResourceType.Wood, 250f), new ResourceAmount(ResourceType.Stone, 60f), new ResourceAmount(ResourceType.Cloth, 60f) };
                d.Services = new[] { new ServiceAmount(ServiceType.Faith, 8f), new ServiceAmount(ServiceType.Research, 6f) };
            });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static void Reg(GameDatabase db, BuildingType t, AgeId age, BuildingCategory cat, Action<BuildingDefinition> edit)
        {
            var d = new BuildingDefinition { Type = t, Name = t.ToString(), Category = cat, MinAge = age, BuildWork = 10f };
            edit?.Invoke(d);
            db.Register(d);
        }

        /// <summary>A forest world fully covered by Trees, with one Elf settlement whose territory is claimed.</summary>
        private static SimContext CreateForestWorld(GameDatabase db, out Settlement elves, out Civilization civ, ulong seed = 7)
        {
            var ctx = TestWorlds.CreateContext(db, 64, seed, Biome.Forest);
            var map = ctx.State.Map;
            for (int i = 0; i < map.TileCount; i++) map.Set(i, TileFlags.Trees);
            elves = TestWorlds.AddSettlement(ctx, RaceId.Elf, ElfAt, 8, out civ);
            ctx.Ops.ClaimTerritory(elves, 8);
            return ctx;
        }

        private static void RunOnce(ElfCultureAdvisor advisor, SimContext ctx)
        {
            ctx.State.Tick += advisor.TickInterval;
            ctx.DeltaTicks = advisor.TickInterval;
            advisor.Tick(ctx);
            ctx.Ops.FlushRemovals();
        }

        private static ElfCultureAdvisor CreateAdvisor(SimContext ctx)
        {
            var a = new ElfCultureAdvisor();
            ctx.DeltaTicks = a.TickInterval;
            a.Initialize(ctx);
            return a;
        }

        private static void ClearVotes(Settlement s) => AdvisorBoard.Decay(s, 0f);

        private static void Fell(SimContext ctx, Settlement s, int keepColumnsFrom)
        {
            var map = ctx.State.Map;
            for (int i = 0; i < map.TileCount; i++)
                if (map.Owner[i] == s.Id && map.PosOf(i).X < keepColumnsFrom) map.Clear(i, TileFlags.Trees);
        }

        // ------------------------------------------------------------------ tests
        [Test]
        public void Identity_IsRaceElfEvery24Ticks()
        {
            var a = new ElfCultureAdvisor();
            Assert.AreEqual("race_elf", a.AdvisorId);
            Assert.AreEqual(24, a.TickInterval);
            Assert.AreEqual("AI.Advisor.race_elf", a.Name);
        }

        [Test]
        public void ShrinkingForest_GivesStrongNegativeWoodcutterVotes()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ);
            s.Stockpile.SetRaw(ResourceType.Wood, 5f); // genuinely short of wood
            var advisor = CreateAdvisor(ctx);

            RunOnce(advisor, ctx);
            float abundant = AdvisorBoard.BuildBias(s, BuildingType.WoodcutterCamp);
            Assert.Greater(abundant, 0f, "a deep forest may spare wood for modest needs");

            Fell(ctx, s, ElfAt.X); // the western half of the grove is cut down
            ClearVotes(s);
            RunOnce(advisor, ctx);

            Assert.Less(s.Mem(ElfCultureAdvisor.KeyForestTrend), ElfCultureAdvisor.ShrinkThreshold);
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.WoodcutterCamp), -0.5f);
            Assert.Less(AdvisorBoard.JobBias(s, JobType.Woodcutter), -0.5f);
            Assert.Less(s.Mem(ElfCultureAdvisor.KeyForestEma), 300f);
        }

        [Test]
        public void ShrinkingForest_InIndustrialAge_RejectsSawmill()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ);
            civ.Age = AgeId.Industrial;
            var advisor = CreateAdvisor(ctx);
            RunOnce(advisor, ctx);
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Sawmill), -0.5f, "sawmills are unwelcome even in a deep forest");
            Fell(ctx, s, ElfAt.X);
            ClearVotes(s);
            RunOnce(advisor, ctx);
            Assert.LessOrEqual(AdvisorBoard.BuildBias(s, BuildingType.Sawmill), -0.99f);
        }

        [Test]
        public void KnowledgeAndHealing_ComeFirst()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ);
            civ.Age = AgeId.Classical;
            var advisor = CreateAdvisor(ctx);
            RunOnce(advisor, ctx);

            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Library), 0.7f);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Scriptorium), 0.7f);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Herbalist), 0.7f);
            Assert.GreaterOrEqual(AdvisorBoard.JobBias(s, JobType.Scholar), 0.6f);
            Assert.GreaterOrEqual(AdvisorBoard.JobBias(s, JobType.Healer), 0.5f);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Library), AdvisorBoard.BuildBias(s, BuildingType.Barracks));
            // serene culture
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Theater), 0.5f);
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Arena), -0.5f);
            // forest defense rather than barracks
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Watchtower), 0.5f);
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.Barracks), 0f);
        }

        [Test]
        public void EarlyAges_VoteHerbalistBeforeLibraryExists()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ);
            civ.Age = AgeId.Bronze;
            var advisor = CreateAdvisor(ctx);
            RunOnce(advisor, ctx);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Herbalist), 0.7f);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Library), "unavailable buildings never receive votes");
        }

        [Test]
        public void IndustrialAge_HeavyIndustryAversion()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ);
            civ.Age = AgeId.Industrial;
            var advisor = CreateAdvisor(ctx);
            RunOnce(advisor, ctx);
            Assert.LessOrEqual(AdvisorBoard.BuildBias(s, BuildingType.Factory), -0.99f);
            Assert.LessOrEqual(AdvisorBoard.BuildBias(s, BuildingType.Foundry), -0.99f);
            Assert.LessOrEqual(AdvisorBoard.BuildBias(s, BuildingType.CoalMine), -0.99f);
            Assert.Less(AdvisorBoard.JobBias(s, JobType.Miner), 0f);

            civ.Age = AgeId.Modern;
            ClearVotes(s);
            RunOnce(advisor, ctx);
            Assert.LessOrEqual(AdvisorBoard.BuildBias(s, BuildingType.OilWell), -0.99f);

            civ.Age = AgeId.Ascension;
            ClearVotes(s);
            RunOnce(advisor, ctx);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.FusionReactor), 0.5f);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.HydroponicsTower), 0.5f);
        }

        [Test]
        public void WorldTree_IsAGenerationalProjectAtTheCapital()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ);
            civ.Age = AgeId.Iron;
            s.Tier = SettlementTier.Village;
            var colony = ctx.Ops.FoundSettlement(civ, new Vec2i(44, 44));
            colony.Tier = SettlementTier.Village;
            Assert.IsTrue(s.IsCapital);
            var advisor = CreateAdvisor(ctx);

            RunOnce(advisor, ctx);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.WorldTree), 0.5f, "the capital saves for the WorldTree");
            Assert.IsTrue(s.Memory.ContainsKey(ElfCultureAdvisor.KeyWorldTreeSaving));
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Weaver), 0f, "cloth must be gathered for it");
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Storehouse), 0f, "the offerings exceed the storehouse");
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(colony, BuildingType.WorldTree), "only the capital plants it");

            // Offerings gathered: plant it now.
            s.Stockpile.Capacity = 500f;
            s.Stockpile.SetRaw(ResourceType.Wood, 300f);
            s.Stockpile.SetRaw(ResourceType.Stone, 100f);
            s.Stockpile.SetRaw(ResourceType.Cloth, 100f);
            ClearVotes(s);
            RunOnce(advisor, ctx);
            Assert.GreaterOrEqual(AdvisorBoard.BuildBias(s, BuildingType.WorldTree), 0.99f);

            // Already queued (here in a colony): stop voting.
            colony.BuildQueue.Add(new BuildOrder { Type = BuildingType.WorldTree, Priority = 1f, Reason = "test" });
            ClearVotes(s);
            RunOnce(advisor, ctx);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.WorldTree));
            Assert.IsFalse(s.Memory.ContainsKey(ElfCultureAdvisor.KeyWorldTreeSaving));
        }

        [Test]
        public void WorldTree_UnavailableBeforeIronAge()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ);
            s.Tier = SettlementTier.Village;
            var advisor = CreateAdvisor(ctx);
            RunOnce(advisor, ctx);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.WorldTree));
        }

        [Test]
        public void Wildfire_BringsReplantingResolve()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ);
            var advisor = CreateAdvisor(ctx);
            ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Wildfire, Position = new Vec2i(ElfAt.X + 3, ElfAt.Y), Radius = 4, Severity = 0.8f });
            Assert.Greater(s.Mem(ElfCultureAdvisor.KeyFireResolve), 0.5f);

            RunOnce(advisor, ctx);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Well), 0.5f);
            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Priest), 0.5f);
            Assert.Less(AdvisorBoard.JobBias(s, JobType.Woodcutter), 0f);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.Shrine), 0f);
        }

        [Test]
        public void GodWildfire_AndScorchedLand_TriggerResolve()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ);
            var advisor = CreateAdvisor(ctx);
            ctx.Events.Publish(new GodPowerCastEvent { Power = GodPowerType.Wildfire, Target = ElfAt, Radius = 3, Success = true });
            Assert.Greater(s.Mem(ElfCultureAdvisor.KeyFireResolve), 0.5f);

            // Fallback without events: scorched tiles in the territory.
            var ctx2 = CreateForestWorld(db, out var s2, out var civ2);
            var map = ctx2.State.Map;
            for (int i = 0; i < map.TileCount; i++) if (map.Owner[i] == s2.Id && map.PosOf(i).X < ElfAt.X - 4) map.Set(i, TileFlags.Scorched);
            var advisor2 = CreateAdvisor(ctx2);
            RunOnce(advisor2, ctx2);
            Assert.Greater(AdvisorBoard.BuildBias(s2, BuildingType.Well), 0.5f);
        }

        [Test]
        public void ForeignLoggingNearby_LowersHappiness()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ);
            var humans = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(34, 16), 4, out var humanCiv);
            var camp = ctx.Ops.CreateBuilding(humans, BuildingType.WoodcutterCamp, new Vec2i(31, 16), 0, true);
            var camp2 = ctx.Ops.CreateBuilding(humans, BuildingType.WoodcutterCamp, new Vec2i(31, 19), 0, true);
            Assert.IsNotNull(camp);
            Assert.IsNotNull(camp2);
            float before = s.Happiness;
            float humanBefore = humans.Happiness;
            var advisor = CreateAdvisor(ctx);
            RunOnce(advisor, ctx);
            RunOnce(advisor, ctx);

            Assert.Greater(s.Mem(ElfCultureAdvisor.KeyGrief), 0.05f);
            Assert.Less(s.Happiness, before);
            var resident = ctx.State.People.Get(s.ResidentIds[0]);
            Assert.Less(resident.Happiness, 0.6f);
            Assert.AreEqual(humanBefore, humans.Happiness, "the humans do not grieve");
            Assert.IsTrue(ContainsLog(s, "grief"));
        }

        [Test]
        public void NonElves_AreUntouched()
        {
            var db = CreateDb();
            var ctx = TestWorlds.CreateContext(db, 64, 3, Biome.Forest);
            var map = ctx.State.Map;
            for (int i = 0; i < map.TileCount; i++) map.Set(i, TileFlags.Trees);
            var advisor = CreateAdvisor(ctx);
            for (int r = 0; r < Counts.Races; r++)
            {
                if ((RaceId)r == RaceId.Elf) continue;
                var s = TestWorlds.AddSettlement(ctx, (RaceId)r, new Vec2i(8 + r * 12, 32), 6, out var c);
                ctx.Ops.ClaimTerritory(s, 5);
                c.Age = AgeId.Industrial;
            }
            var snapshot = new List<string>();
            foreach (var s in ctx.State.Settlements.Items) snapshot.Add(Snapshot(s));
            ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Wildfire, Position = new Vec2i(32, 32), Radius = 30, Severity = 1f });
            for (int i = 0; i < 6; i++) RunOnce(advisor, ctx);
            var items = ctx.State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                Assert.AreEqual(snapshot[i], Snapshot(items[i]), "non-elf settlement memory changed");
                Assert.AreEqual(0, items[i].AiLog.Count);
            }
        }

        [Test]
        public void SpaciousGroves_HouseEarlyButGrowSlowly()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ);
            var advisor = CreateAdvisor(ctx);
            RunOnce(advisor, ctx); // 8 residents, no houses: build a hall before anyone shares a roof
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.House), 0.5f);
            Assert.Greater(s.Mem(ElfCultureAdvisor.KeyPopTarget), 0f);

            for (int i = 0; i < 4; i++) Assert.IsNotNull(ctx.Ops.CreateBuilding(s, BuildingType.House, new Vec2i(10 + i * 2, 24), 0, true));
            ClearVotes(s);
            RunOnce(advisor, ctx); // 12-20 beds for 8 elves: plenty of room
            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.House), 0f);
        }

        [Test]
        public void Deterministic_SameInputsSameMemory()
        {
            Assert.AreEqual(RunScenario(), RunScenario());
        }

        [Test]
        public void NoNaN_OnDegenerateData()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ);
            s.Memory[ElfCultureAdvisor.KeyForestEma] = float.NaN;
            s.Memory[ElfCultureAdvisor.KeyForestTrend] = float.PositiveInfinity;
            s.ServiceCoverage[(int)ServiceType.Research] = float.NaN;
            s.Happiness = float.NaN;
            s.TerritoryRadius = 0;
            var advisor = CreateAdvisor(ctx);
            Assert.DoesNotThrow(() => RunOnce(advisor, ctx));
            foreach (var kv in s.Memory)
                Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
        }

        // ------------------------------------------------------------------ helpers
        private static string RunScenario()
        {
            var db = CreateDb();
            var ctx = CreateForestWorld(db, out var s, out var civ, 11);
            civ.Age = AgeId.Iron;
            s.Tier = SettlementTier.Village;
            var humans = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(34, 16), 4, out var hc);
            ctx.Ops.CreateBuilding(humans, BuildingType.WoodcutterCamp, new Vec2i(31, 16), 0, true);
            var advisor = CreateAdvisor(ctx);
            var sb = new StringBuilder();
            for (int r = 0; r < 12; r++)
            {
                if (r == 4) Fell(ctx, s, ElfAt.X - 2);
                if (r == 7) ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Wildfire, Position = ElfAt, Radius = 3, Severity = 0.5f });
                if (r == 9) civ.Age = AgeId.Industrial;
                RunOnce(advisor, ctx);
                sb.Append(Snapshot(s)).Append('|').Append(s.Happiness.ToString("R")).Append('\n');
            }
            return sb.ToString();
        }

        private static string Snapshot(Settlement s)
        {
            var keys = new List<string>(s.Memory.Keys);
            keys.Sort(string.CompareOrdinal);
            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++) sb.Append(keys[i]).Append('=').Append(s.Memory[keys[i]].ToString("R")).Append(';');
            return sb.ToString();
        }

        private static bool ContainsLog(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].IndexOf(text, StringComparison.Ordinal) >= 0) return true;
            return false;
        }
    }
}
