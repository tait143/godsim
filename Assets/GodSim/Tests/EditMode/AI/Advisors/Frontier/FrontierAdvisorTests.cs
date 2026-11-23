using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class FrontierAdvisorTests
    {
        // ------------------------------------------------------------------ fixtures
        private static BuildingDefinition Def(BuildingType t, BuildingCategory cat, AgeId minAge, BuildingShape shape = BuildingShape.House)
        {
            return new BuildingDefinition { Type = t, Name = t.ToString(), Category = cat, MinAge = minAge, Shape = shape, BuildWork = 10f };
        }

        private static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            var pal = Def(BuildingType.Palisade, BuildingCategory.Military, AgeId.Bronze, BuildingShape.Wall);
            pal.MaxAge = AgeId.Medieval; pal.Services = new[] { new ServiceAmount(ServiceType.Defense, 2f) }; pal.Placement = PlacementRule.Outskirts;
            db.Register(pal);
            var wall = Def(BuildingType.StoneWall, BuildingCategory.Military, AgeId.Classical, BuildingShape.Wall);
            wall.Services = new[] { new ServiceAmount(ServiceType.Defense, 4f) }; wall.MaxPerSettlement = 12; wall.Placement = PlacementRule.Outskirts;
            db.Register(wall);
            var tower = Def(BuildingType.Watchtower, BuildingCategory.Military, AgeId.Iron, BuildingShape.Tower);
            tower.Job = JobType.Soldier; tower.JobSlots = 1; tower.Services = new[] { new ServiceAmount(ServiceType.Defense, 3f) }; tower.Placement = PlacementRule.Outskirts;
            db.Register(tower);
            var barracks = Def(BuildingType.Barracks, BuildingCategory.Military, AgeId.Iron, BuildingShape.Hall);
            barracks.Job = JobType.Soldier; barracks.JobSlots = 6; barracks.MaxPerSettlement = 2; barracks.Services = new[] { new ServiceAmount(ServiceType.Defense, 2f) };
            db.Register(barracks);
            var market = Def(BuildingType.Market, BuildingCategory.Commerce, AgeId.Iron, BuildingShape.Hall);
            market.Job = JobType.Merchant; market.JobSlots = 2; market.MaxPerSettlement = 2; market.Services = new[] { new ServiceAmount(ServiceType.Commerce, 2f) }; market.Placement = PlacementRule.Central;
            db.Register(market);
            var fish = Def(BuildingType.FishingHut, BuildingCategory.Food, AgeId.Dawn, BuildingShape.Dock);
            fish.Job = JobType.Fisher; fish.JobSlots = 2; fish.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Food, 0.15f) }; fish.Placement = PlacementRule.NearWater;
            db.Register(fish);
            var harbor = Def(BuildingType.Harbor, BuildingCategory.Commerce, AgeId.Classical, BuildingShape.Dock);
            harbor.Job = JobType.Merchant; harbor.JobSlots = 3; harbor.MaxPerSettlement = 2; harbor.Services = new[] { new ServiceAmount(ServiceType.Commerce, 3f) }; harbor.Placement = PlacementRule.Coastal;
            db.Register(harbor);
            var colossus = Def(BuildingType.Colossus, BuildingCategory.Wonder, AgeId.Classical, BuildingShape.Monument);
            colossus.IsWonder = true; colossus.UniquePerCivilization = true; colossus.MaxPerSettlement = 1; colossus.Services = new[] { new ServiceAmount(ServiceType.Commerce, 20f) }; colossus.Placement = PlacementRule.Coastal;
            db.Register(colossus);
            var store = Def(BuildingType.Storehouse, BuildingCategory.Storage, AgeId.Dawn, BuildingShape.Hall);
            store.StorageCapacity = 120f;
            db.Register(store);
            var mine = Def(BuildingType.Mine, BuildingCategory.Resource, AgeId.Bronze, BuildingShape.Cave);
            mine.Job = JobType.Miner; mine.JobSlots = 3; mine.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Ore, 0.07f) }; mine.Placement = PlacementRule.NearOre;
            db.Register(mine);
            var gold = Def(BuildingType.GoldMine, BuildingCategory.Resource, AgeId.Iron, BuildingShape.Cave);
            gold.Job = JobType.Miner; gold.JobSlots = 3; gold.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Gold, 0.03f) }; gold.Placement = PlacementRule.NearGold;
            db.Register(gold);
            var quarry = Def(BuildingType.StoneQuarry, BuildingCategory.Resource, AgeId.Dawn, BuildingShape.Cave);
            quarry.Job = JobType.Quarrier; quarry.JobSlots = 3; quarry.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Stone, 0.08f) }; quarry.Placement = PlacementRule.NearStone;
            db.Register(quarry);
            var wood = Def(BuildingType.WoodcutterCamp, BuildingCategory.Resource, AgeId.Dawn, BuildingShape.Hut);
            wood.Job = JobType.Woodcutter; wood.JobSlots = 2; wood.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Wood, 0.12f) }; wood.Placement = PlacementRule.NearForest | PlacementRule.Outskirts;
            db.Register(wood);
            var smelter = Def(BuildingType.Smelter, BuildingCategory.Industry, AgeId.Bronze, BuildingShape.Workshop);
            smelter.Job = JobType.Smith; smelter.JobSlots = 2; smelter.InputsPerWorker = new[] { new ResourceAmount(ResourceType.Ore, 0.1f) }; smelter.OutputsPerWorker = new[] { new ResourceAmount(ResourceType.Metal, 0.05f) };
            db.Register(smelter);
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Settlement Found(SimContext ctx, RaceId race, string civName, Vec2i at, int adults, AgeId age, out Civilization civ)
        {
            civ = ctx.Ops.CreateCivilization(race, civName);
            civ.Age = age;
            var s = ctx.Ops.FoundSettlement(civ, at);
            for (int i = 0; i < adults; i++) ctx.Ops.CreatePerson(s, race, i % 2 == 0 ? Sex.Female : Sex.Male, 20f + (i % 10));
            return s;
        }

        private static void RunOnce(FrontierAdvisor adv, SimContext ctx, long tick)
        {
            ctx.State.Tick = tick;
            ctx.DeltaTicks = adv.TickInterval;
            adv.Tick(ctx);
        }

        private static SimContext Border(DiplomaticStance stance, AgeId age, out Settlement mine, out Civilization me, out Civilization orcs)
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            mine = Found(ctx, RaceId.Human, "Kingdom of Aster", new Vec2i(22, 32), 14, age, out me);
            Found(ctx, RaceId.Orc, "Orc Horde", new Vec2i(36, 32), 14, age, out orcs);
            var r = ctx.State.Diplomacy.Get(me.Id, orcs.Id);
            r.Stance = stance;
            r.HasContact = true;
            r.Opinion = stance == DiplomaticStance.Friendly ? 50f : stance == DiplomaticStance.Hostile ? -60f : 0f;
            return ctx;
        }

        private static float Bias(Settlement s, BuildingType t) => AdvisorBoard.BuildBias(s, t);

        // ------------------------------------------------------------------ classification
        [Test]
        public void Classify_BorderingAnotherCiv_IsFrontier()
        {
            var ctx = Border(DiplomaticStance.Neutral, AgeId.Iron, out var s, out var me, out var orcs);
            var adv = new FrontierAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx, 24);
            Assert.AreEqual(FrontierRoles.Frontier, FrontierRoles.RoleOf(s));
            Assert.AreEqual(orcs.Id, (int)s.Mem(FrontierRoles.KeyNeighbour[0], -1f));
            Assert.IsTrue(s.Mem(FrontierRoles.KeyForeign) > 0f);
            Assert.AreEqual(1f, me.Mem(FrontierRoles.CivKeyFrontiers));
        }

        [Test]
        public void Classify_SeaAround_IsCoastalGateway()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var map = ctx.State.Map;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < 26; x++) { int i = map.Index(x, y); map.Biomes[i] = Biome.DeepOcean; map.Elevation[i] = -0.4f; }
            var s = Found(ctx, RaceId.Human, "Sea Folk", new Vec2i(31, 32), 10, AgeId.Classical, out var civ);
            var adv = new FrontierAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx, 24);
            Assert.AreEqual(FrontierRoles.CoastalGateway, FrontierRoles.RoleOf(s));
            Assert.IsTrue(adv.Scanner.Coast > 0);
            Assert.IsTrue(s.Mem(FrontierRoles.KeyWater) > 0.06f);
        }

        [Test]
        public void Classify_RichDeposits_IsResourceTown()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var map = ctx.State.Map;
            for (int x = 34; x < 38; x++) { map.Set(map.Index(x, 30), TileFlags.OreDeposit); map.Set(map.Index(x, 29), TileFlags.OreDeposit); }
            for (int y = 34; y < 37; y++) map.Set(map.Index(27, y), TileFlags.GoldDeposit);
            var s = Found(ctx, RaceId.Dwarf, "Deep Clans", new Vec2i(32, 32), 10, AgeId.Iron, out var civ);
            var adv = new FrontierAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx, 24);
            Assert.AreEqual(FrontierRoles.ResourceTown, FrontierRoles.RoleOf(s));
            int mask = (int)s.Mem(FrontierRoles.KeyDepositMask);
            Assert.IsTrue((mask & (1 << FrontierCatalog.KindOre)) != 0);
            Assert.IsTrue((mask & (1 << FrontierCatalog.KindGold)) != 0);
            Assert.IsTrue((mask & (1 << FrontierCatalog.KindStone)) == 0);
        }

        [Test]
        public void Classify_RemoteColony_IsWildernessOutpost_CapitalIsHeartland()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var capital = Found(ctx, RaceId.Human, "Kingdom", new Vec2i(10, 10), 10, AgeId.Bronze, out var civ);
            var colony = ctx.Ops.FoundSettlement(civ, new Vec2i(50, 50));
            for (int i = 0; i < 6; i++) ctx.Ops.CreatePerson(colony, RaceId.Human, Sex.Female, 25f);
            var adv = new FrontierAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx, 24);
            Assert.AreEqual(FrontierRoles.Heartland, FrontierRoles.RoleOf(capital));
            Assert.AreEqual(FrontierRoles.WildernessOutpost, FrontierRoles.RoleOf(colony));
        }

        [Test]
        public void Role_IsCached_AndRefreshedAfterFourYears()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var s = Found(ctx, RaceId.Human, "Kingdom", new Vec2i(32, 32), 10, AgeId.Iron, out var civ);
            var adv = new FrontierAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx, 24);
            Assert.AreEqual(FrontierRoles.Heartland, FrontierRoles.RoleOf(s));
            var map = ctx.State.Map;
            for (int x = 28; x < 38; x++) { map.Set(map.Index(x, 36), TileFlags.OreDeposit); map.Set(map.Index(x, 27), TileFlags.GoldDeposit); }
            RunOnce(adv, ctx, 48);
            Assert.AreEqual(FrontierRoles.Heartland, FrontierRoles.RoleOf(s), "cached role must not change before the refresh");
            RunOnce(adv, ctx, 24 + FrontierAdvisor.RefreshTicks + 96);
            Assert.AreEqual(FrontierRoles.ResourceTown, FrontierRoles.RoleOf(s));
        }

        // ------------------------------------------------------------------ votes by role
        [Test]
        public void HostileFrontier_VotesWatchtowerWallsBarracks_NotMarket()
        {
            var ctx = Border(DiplomaticStance.Hostile, AgeId.Classical, out var s, out var me, out var orcs);
            var adv = new FrontierAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx, 24);
            Assert.AreEqual(FrontierRoles.Frontier, FrontierRoles.RoleOf(s));
            Assert.IsTrue(Bias(s, BuildingType.Watchtower) >= 0.5f, "watchtower");
            Assert.IsTrue(Bias(s, BuildingType.StoneWall) >= 0.5f, "stone wall (newest wall type) on a hostile border");
            Assert.IsTrue(Bias(s, BuildingType.Barracks) > 0.4f, "barracks");
            Assert.IsTrue(Bias(s, BuildingType.Market) <= 0f, "no market on a hostile border");
            Assert.IsTrue(AdvisorBoard.JobBias(s, JobType.Soldier) > 0f);
            Assert.IsTrue(AdvisorBoard.Urgency(s, FrontierAdvisor.Id) >= 0.6f);
            bool logged = false;
            for (int i = 0; i < s.AiLog.Count; i++)
                if (s.AiLog[i].Contains("Frontier with the Orc Horde (hostile): watchtower")) logged = true;
            Assert.IsTrue(logged, "explainable reason expected in AI log");
        }

        [Test]
        public void FriendlyFrontier_VotesMarket_NoWalls()
        {
            var ctx = Border(DiplomaticStance.Friendly, AgeId.Classical, out var s, out var me, out var orcs);
            var adv = new FrontierAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx, 24);
            Assert.AreEqual(FrontierRoles.Frontier, FrontierRoles.RoleOf(s));
            Assert.IsTrue(Bias(s, BuildingType.Market) >= 0.5f, "border market with friends");
            Assert.AreEqual(0f, Bias(s, BuildingType.StoneWall));
            Assert.AreEqual(0f, Bias(s, BuildingType.Palisade));
            Assert.IsTrue(Bias(s, BuildingType.Watchtower) > 0f, "still keeps a lookout");
            float hostileUrgency;
            {
                var ctx2 = Border(DiplomaticStance.Hostile, AgeId.Classical, out var s2, out var me2, out var orcs2);
                var adv2 = new FrontierAdvisor();
                adv2.Initialize(ctx2);
                RunOnce(adv2, ctx2, 24);
                hostileUrgency = AdvisorBoard.Urgency(s2, FrontierAdvisor.Id);
                Assert.IsTrue(Bias(s2, BuildingType.Watchtower) > Bias(s, BuildingType.Watchtower), "hostile border wants more watch");
            }
            Assert.IsTrue(AdvisorBoard.Urgency(s, FrontierAdvisor.Id) < hostileUrgency);
        }

        [Test]
        public void Frontier_NeverVotesUnavailableBuildings()
        {
            var ctx = Border(DiplomaticStance.Hostile, AgeId.Dawn, out var s, out var me, out var orcs);
            var adv = new FrontierAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx, 24);
            Assert.AreEqual(FrontierRoles.Frontier, FrontierRoles.RoleOf(s));
            Assert.AreEqual(0f, Bias(s, BuildingType.Watchtower));
            Assert.AreEqual(0f, Bias(s, BuildingType.Barracks));
            Assert.AreEqual(0f, Bias(s, BuildingType.StoneWall));
            Assert.AreEqual(0f, Bias(s, BuildingType.Palisade));
            me.Age = AgeId.Bronze;
            RunOnce(adv, ctx, 48);
            Assert.IsTrue(Bias(s, BuildingType.Palisade) > 0.5f, "palisade becomes the wall of choice in the Bronze age");
            Assert.AreEqual(0f, Bias(s, BuildingType.StoneWall));
        }

        [Test]
        public void CoastalGateway_VotesHarborFishingAndColossus()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var map = ctx.State.Map;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < 26; x++) { int i = map.Index(x, y); map.Biomes[i] = Biome.DeepOcean; map.Elevation[i] = -0.4f; }
            var s = Found(ctx, RaceId.Human, "Sea Folk", new Vec2i(31, 32), 32, AgeId.Classical, out var civ);
            var adv = new FrontierAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx, 24);
            Assert.AreEqual(FrontierRoles.CoastalGateway, FrontierRoles.RoleOf(s));
            Assert.IsTrue(Bias(s, BuildingType.Harbor) >= 0.5f);
            Assert.IsTrue(Bias(s, BuildingType.FishingHut) > 0.3f);
            Assert.IsTrue(Bias(s, BuildingType.Colossus) > 0f);
            Assert.AreEqual(0f, Bias(s, BuildingType.StoneWall));
            Assert.IsTrue(AdvisorBoard.JobBias(s, JobType.Fisher) > 0f);
        }

        [Test]
        public void ResourceTown_VotesMatchingExtractorsStorageAndProcessing()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var map = ctx.State.Map;
            for (int x = 34; x < 38; x++) { map.Set(map.Index(x, 30), TileFlags.OreDeposit); map.Set(map.Index(x, 29), TileFlags.OreDeposit); }
            for (int y = 34; y < 37; y++) map.Set(map.Index(27, y), TileFlags.GoldDeposit);
            var s = Found(ctx, RaceId.Dwarf, "Deep Clans", new Vec2i(32, 32), 10, AgeId.Iron, out var civ);
            var adv = new FrontierAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx, 24);
            Assert.AreEqual(FrontierRoles.ResourceTown, FrontierRoles.RoleOf(s));
            Assert.IsTrue(Bias(s, BuildingType.Mine) >= 0.5f);
            Assert.IsTrue(Bias(s, BuildingType.GoldMine) >= 0.5f);
            Assert.AreEqual(0f, Bias(s, BuildingType.StoneQuarry), "no stone here");
            Assert.IsTrue(Bias(s, BuildingType.Storehouse) > 0f);
            Assert.IsTrue(AdvisorBoard.JobBias(s, JobType.Miner) > 0f);
            Assert.AreEqual(0f, Bias(s, BuildingType.Smelter), "processing waits for the first mine");

            var site = ctx.Ops.FindFreeSite(new Vec2i(35, 31), 1, 1, 6);
            Assert.IsTrue(ctx.Ops.CreateBuilding(s, BuildingType.Mine, site, 0, true) != null);
            RunOnce(adv, ctx, 48);
            Assert.IsTrue(Bias(s, BuildingType.Smelter) > 0f, "mine built: start the ore chain");
        }

        [Test]
        public void WildernessOutpost_VotesStorehouseAndExtractors()
        {
            var ctx = TestWorlds.CreateContext(CreateDb());
            var map = ctx.State.Map;
            for (int y = 44; y < 50; y++) for (int x = 52; x < 58; x++) map.Set(map.Index(x, y), TileFlags.Trees);
            Found(ctx, RaceId.Elf, "Sylvan Court", new Vec2i(10, 10), 10, AgeId.Bronze, out var civ);
            var colony = ctx.Ops.FoundSettlement(civ, new Vec2i(50, 50));
            for (int i = 0; i < 6; i++) ctx.Ops.CreatePerson(colony, RaceId.Elf, Sex.Female, 25f);
            var adv = new FrontierAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx, 24);
            Assert.AreEqual(FrontierRoles.WildernessOutpost, FrontierRoles.RoleOf(colony));
            Assert.IsTrue(Bias(colony, BuildingType.Storehouse) >= 0.5f);
            Assert.IsTrue(Bias(colony, BuildingType.WoodcutterCamp) > 0f);
            Assert.IsTrue(Bias(colony, BuildingType.House) > 0f, "gentle growth");
            Assert.IsTrue(Bias(colony, BuildingType.House) < 0.5f, "growth votes stay gentle");
        }

        [Test]
        public void Frontier_RespectsMaxPerSettlement()
        {
            var ctx = Border(DiplomaticStance.Hostile, AgeId.Classical, out var s, out var me, out var orcs);
            for (int i = 0; i < 2; i++)
            {
                var site = ctx.Ops.FindFreeSite(s.Center, 1, 1, 6);
                ctx.Ops.CreateBuilding(s, BuildingType.Barracks, site, 0, true);
            }
            var adv = new FrontierAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx, 24);
            Assert.AreEqual(0f, Bias(s, BuildingType.Barracks));
        }

        // ------------------------------------------------------------------ determinism & safety
        private static string Snapshot(SimContext ctx)
        {
            var sb = new StringBuilder();
            var items = ctx.State.Settlements.Items;
            for (int i = 0; i < items.Count; i++)
            {
                var keys = new List<string>(items[i].Memory.Keys);
                keys.Sort(string.CompareOrdinal);
                for (int k = 0; k < keys.Count; k++) sb.Append(items[i].Id).Append(':').Append(keys[k]).Append('=').Append(items[i].Memory[keys[k]].ToString("R")).Append(';');
            }
            return sb.ToString();
        }

        [Test]
        public void Advisor_IsDeterministic_AndNaNFree()
        {
            string a = null;
            for (int run = 0; run < 2; run++)
            {
                var ctx = Border(DiplomaticStance.Hostile, AgeId.Classical, out var s, out var me, out var orcs);
                var adv = new FrontierAdvisor();
                TestWorlds.Run(adv, ctx, 24 * 30);
                string snap = Snapshot(ctx);
                Assert.IsFalse(snap.Contains("NaN"));
                Assert.IsFalse(snap.Contains("Infinity"));
                if (a == null) a = snap; else Assert.AreEqual(a, snap);
            }
        }

        [Test]
        public void Advisor_SurvivesEmptyAndDegenerateWorlds()
        {
            var ctx = TestWorlds.CreateContext(CreateDb(), 16);
            var adv = new FrontierAdvisor();
            Assert.DoesNotThrow(() => TestWorlds.Run(adv, ctx, 48));
            var s = Found(ctx, RaceId.Saurian, "Sun Brood", new Vec2i(0, 0), 0, AgeId.Dawn, out var civ);
            s.TerritoryRadius = 500;
            s.Memory[FrontierRoles.KeyRole] = float.NaN;
            s.Memory[FrontierRoles.KeyNeighbour[0]] = 9999f;
            Assert.DoesNotThrow(() => RunOnce(adv, ctx, 72));
            int role = FrontierRoles.RoleOf(s);
            Assert.IsTrue(role >= 0 && role < FrontierRoles.Count);
        }
    }
}
