using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class InfrastructureAdvisorTests
    {
        // ------------------------------------------------------------------ fixtures
        private static ResourceAmount R(ResourceType t, float a) => new ResourceAmount(t, a);

        private static BuildingDefinition Def(BuildingType t, BuildingCategory cat, AgeId minAge, SettlementTier tier, int minPop, int w, int h, float work)
        {
            return new BuildingDefinition { Type = t, Name = t.ToString(), Category = cat, MinAge = minAge, MinTier = tier, MinPopulation = minPop, Width = w, Height = h, BuildWork = work };
        }

        /// <summary>Minimal infrastructure catalog mirroring the real balancing of the buildings this advisor reasons about.</summary>
        private static GameDatabase MakeDb()
        {
            var db = new GameDatabase();
            var well = Def(BuildingType.Well, BuildingCategory.Infrastructure, AgeId.Dawn, SettlementTier.Camp, 5, 1, 1, 12f);
            well.Name = "Well";
            well.Cost = new[] { R(ResourceType.Stone, 8f), R(ResourceType.Wood, 4f) };
            well.Services = new[] { new ServiceAmount(ServiceType.Water, 3f) };
            well.Desirability = 0.4f;
            db.Register(well);

            var aq = Def(BuildingType.Aqueduct, BuildingCategory.Infrastructure, AgeId.Classical, SettlementTier.Village, 30, 1, 3, 160f);
            aq.Cost = new[] { R(ResourceType.Stone, 60f), R(ResourceType.Wood, 10f), R(ResourceType.Tools, 5f) };
            aq.Services = new[] { new ServiceAmount(ServiceType.Water, 10f) };
            aq.UpkeepGold = 0.02f;
            db.Register(aq);

            var forum = Def(BuildingType.Forum, BuildingCategory.Civic, AgeId.Classical, SettlementTier.Village, 30, 3, 2, 180f);
            forum.MaxPerSettlement = 1;
            forum.Cost = new[] { R(ResourceType.Stone, 60f), R(ResourceType.Wood, 20f), R(ResourceType.Gold, 15f) };
            forum.Job = JobType.Administrator; forum.JobSlots = 2;
            forum.Services = new[] { new ServiceAmount(ServiceType.Administration, 6f) };
            db.Register(forum);

            var factory = Def(BuildingType.Factory, BuildingCategory.Industry, AgeId.Industrial, SettlementTier.Village, 0, 3, 3, 400f);
            factory.MaxPerSettlement = 4;
            factory.Job = JobType.Laborer; factory.JobSlots = 6;
            factory.InputsPerWorker = new[] { R(ResourceType.Metal, 0.05f), R(ResourceType.Fuel, 0.04f) };
            factory.OutputsPerWorker = new[] { R(ResourceType.Machinery, 0.05f), R(ResourceType.Goods, 0.04f) };
            factory.Desirability = -0.8f;
            db.Register(factory);

            var coal = Def(BuildingType.CoalMine, BuildingCategory.Resource, AgeId.Industrial, SettlementTier.Village, 0, 2, 2, 250f);
            coal.MaxPerSettlement = 4;
            coal.Cost = new[] { R(ResourceType.Wood, 50f), R(ResourceType.Stone, 30f), R(ResourceType.Metal, 20f), R(ResourceType.Tools, 20f) };
            coal.Job = JobType.Miner; coal.JobSlots = 4;
            coal.OutputsPerWorker = new[] { R(ResourceType.Fuel, 0.1f) };
            coal.Placement = PlacementRule.NearCoal | PlacementRule.Isolated;
            coal.Desirability = -0.8f;
            db.Register(coal);

            var rail = Def(BuildingType.RailStation, BuildingCategory.Commerce, AgeId.Industrial, SettlementTier.Town, 0, 3, 3, 450f);
            rail.MaxPerSettlement = 1;
            rail.Job = JobType.Merchant; rail.JobSlots = 3;
            rail.Services = new[] { new ServiceAmount(ServiceType.Commerce, 8f) };
            db.Register(rail);

            var pp = Def(BuildingType.PowerPlant, BuildingCategory.Infrastructure, AgeId.Modern, SettlementTier.Town, 0, 3, 3, 500f);
            pp.Name = "Power Plant";
            pp.MaxPerSettlement = 2;
            pp.Cost = new[] { R(ResourceType.Stone, 120f), R(ResourceType.Metal, 80f), R(ResourceType.Tools, 20f), R(ResourceType.Fuel, 30f), R(ResourceType.Machinery, 30f) };
            pp.Job = JobType.Engineer; pp.JobSlots = 3;
            pp.InputsPerWorker = new[] { R(ResourceType.Fuel, 0.05f) };
            pp.Services = new[] { new ServiceAmount(ServiceType.Power, 20f) };
            pp.UpkeepGold = 0.12f;
            pp.Desirability = -0.8f;
            db.Register(pp);

            var oil = Def(BuildingType.OilWell, BuildingCategory.Resource, AgeId.Modern, SettlementTier.Village, 0, 2, 2, 250f);
            oil.Job = JobType.Engineer; oil.JobSlots = 2;
            oil.OutputsPerWorker = new[] { R(ResourceType.Fuel, 0.18f) };
            oil.Placement = PlacementRule.NearOil | PlacementRule.Isolated;
            db.Register(oil);

            var airport = Def(BuildingType.Airport, BuildingCategory.Commerce, AgeId.Information, SettlementTier.City, 200, 3, 3, 800f);
            airport.MaxPerSettlement = 1;
            airport.Job = JobType.Merchant; airport.JobSlots = 6;
            airport.Services = new[] { new ServiceAmount(ServiceType.Commerce, 20f) };
            db.Register(airport);

            var fusion = Def(BuildingType.FusionReactor, BuildingCategory.Infrastructure, AgeId.Ascension, SettlementTier.Town, 0, 3, 3, 800f);
            fusion.Name = "Fusion Reactor";
            fusion.MaxPerSettlement = 2;
            fusion.Cost = new[] { R(ResourceType.Stone, 200f), R(ResourceType.Metal, 300f), R(ResourceType.Gold, 150f), R(ResourceType.Machinery, 120f), R(ResourceType.Electronics, 100f) };
            fusion.Job = JobType.Engineer; fusion.JobSlots = 4;
            fusion.Services = new[] { new ServiceAmount(ServiceType.Power, 80f) };
            fusion.Desirability = -0.3f;
            db.Register(fusion);

            var arc = Def(BuildingType.Arcology, BuildingCategory.Civic, AgeId.Ascension, SettlementTier.City, 250, 3, 3, 800f);
            arc.MaxPerSettlement = 2;
            arc.Job = JobType.Administrator; arc.JobSlots = 6;
            arc.Services = new[] { new ServiceAmount(ServiceType.Administration, 20f), new ServiceAmount(ServiceType.Entertainment, 10f), new ServiceAmount(ServiceType.Water, 20f) };
            db.Register(arc);

            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static Settlement MakeTown(GameDatabase db, AgeId age, SettlementTier tier, int adults, out SimContext ctx, out Civilization civ, ulong seed = 1)
        {
            ctx = TestWorlds.CreateContext(db, 64, seed);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), adults, out civ);
            civ.Age = age;
            s.Tier = tier;
            for (int i = 0; i < Counts.Services; i++) s.ServiceCoverage[i] = 1f; // served unless a test says otherwise
            return s;
        }

        private static Building Place(SimContext ctx, Settlement s, BuildingType t, int dx, int dy, bool complete = true)
        {
            var def = ctx.Db.Building(t);
            var site = ctx.Ops.FindFreeSite(new Vec2i(s.Center.X + dx, s.Center.Y + dy), def.Width, def.Height, 20);
            Assert.IsTrue(site.IsValid, "no site for " + t);
            var b = ctx.Ops.CreateBuilding(s, t, site, 0, complete);
            Assert.IsNotNull(b);
            return b;
        }

        private static InfrastructureAdvisor RunOnce(SimContext ctx, InfrastructureAdvisor adv = null)
        {
            if (adv == null) { adv = new InfrastructureAdvisor(); adv.Initialize(ctx); }
            ctx.State.Tick += adv.TickInterval;
            ctx.DeltaTicks = adv.TickInterval;
            adv.Tick(ctx);
            ctx.Ops.FlushRemovals();
            return adv;
        }

        private static float Bias(Settlement s, BuildingType t) => AdvisorBoard.BuildBias(s, t);

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains(text)) return true;
            return false;
        }

        private static void Coal(SimContext ctx, Settlement s)
        {
            var map = ctx.State.Map;
            for (int x = 0; x < 3; x++) map.Set(map.Index(s.Center.X - 4 + x, s.Center.Y - 4), TileFlags.CoalDeposit);
        }

        // ------------------------------------------------------------------ power
        [Test]
        public void IndustrialTownWithoutPower_VotesPowerPlant_WithQuantifiedReason()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Modern, SettlementTier.Town, 100, out var ctx, out var civ);
            Place(ctx, s, BuildingType.Factory, 8, 8);
            Place(ctx, s, BuildingType.Factory, -10, 8);
            s.ServiceCoverage[(int)ServiceType.Power] = 0f;
            s.Stockpile.SetRaw(ResourceType.Fuel, 120f);

            RunOnce(ctx);

            Assert.Greater(Bias(s, BuildingType.PowerPlant), 0.5f);
            Assert.IsTrue(LogContains(s, "idle without power: +25%"), string.Join("\n", s.AiLog));
            Assert.Greater(s.Mem(InfrastructureAdvisor.KeyPowerGain), 0f);
            Assert.Greater(AdvisorBoard.Urgency(s, InfrastructureAdvisor.Id), 0.3f);
            Assert.AreEqual(0f, Bias(s, BuildingType.CoalMine), "fuel is secured: no fuel producer push");
        }

        [Test]
        public void NoFuel_VotesFuelProducerOnDeposit()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Modern, SettlementTier.Town, 100, out var ctx, out var civ);
            Place(ctx, s, BuildingType.Factory, 8, 8);
            s.ServiceCoverage[(int)ServiceType.Power] = 0f;
            Coal(ctx, s);

            RunOnce(ctx);

            Assert.Greater(Bias(s, BuildingType.CoalMine), 0.3f);
            Assert.AreEqual(0f, Bias(s, BuildingType.OilWell), "no oil deposit: no oil well vote");
            Assert.Greater(Bias(s, BuildingType.PowerPlant), 0f, "plant planned together with its fuel chain");
            Assert.IsTrue(LogContains(s, "fuel/day"), string.Join("\n", s.AiLog));
        }

        [Test]
        public void NoFuel_PrefersFusionWhenAvailable()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Ascension, SettlementTier.City, 100, out var ctx, out var civ);
            Place(ctx, s, BuildingType.Factory, 8, 8);
            Place(ctx, s, BuildingType.Factory, -10, 8);
            s.ServiceCoverage[(int)ServiceType.Power] = 0f;

            RunOnce(ctx);

            Assert.Greater(Bias(s, BuildingType.FusionReactor), 0.5f);
            Assert.Greater(Bias(s, BuildingType.FusionReactor), Bias(s, BuildingType.PowerPlant));
            Assert.IsTrue(LogContains(s, "burns none"), string.Join("\n", s.AiLog));
        }

        [Test]
        public void NoFuelNoDepositNoFusion_DiscouragesColdPowerPlant()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Modern, SettlementTier.Town, 100, out var ctx, out var civ);
            Place(ctx, s, BuildingType.Factory, 8, 8);
            s.ServiceCoverage[(int)ServiceType.Power] = 0f;

            RunOnce(ctx);

            Assert.Less(Bias(s, BuildingType.PowerPlant), 0f);
            Assert.AreEqual(0f, Bias(s, BuildingType.CoalMine));
        }

        [Test]
        public void PreIndustrial_NoPowerVotes()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Classical, SettlementTier.Town, 100, out var ctx, out var civ);
            Place(ctx, s, BuildingType.Factory, 8, 8);
            s.ServiceCoverage[(int)ServiceType.Power] = 0f;
            Coal(ctx, s);

            RunOnce(ctx);

            Assert.AreEqual(0f, Bias(s, BuildingType.PowerPlant));
            Assert.AreEqual(0f, Bias(s, BuildingType.FusionReactor));
            Assert.AreEqual(0f, Bias(s, BuildingType.CoalMine));
            Assert.AreEqual(0f, Bias(s, BuildingType.OilWell));
            Assert.IsFalse(s.Memory.ContainsKey(InfrastructureAdvisor.KeyPowerGain));
        }

        [Test]
        public void ModernTownWithoutIndustry_DiscouragesPowerPlant()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Modern, SettlementTier.Town, 100, out var ctx, out var civ);
            s.ServiceCoverage[(int)ServiceType.Power] = 0f;
            s.Stockpile.SetRaw(ResourceType.Fuel, 120f);

            RunOnce(ctx);

            Assert.Less(Bias(s, BuildingType.PowerPlant), 0f);
        }

        [Test]
        public void UnderstaffedPowerPlant_VotesEngineersBeforeBuilding()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Modern, SettlementTier.Town, 100, out var ctx, out var civ);
            Place(ctx, s, BuildingType.Factory, 8, 8);
            Place(ctx, s, BuildingType.PowerPlant, -10, 8);
            s.ServiceCoverage[(int)ServiceType.Power] = 0f;
            s.Stockpile.SetRaw(ResourceType.Fuel, 120f);

            RunOnce(ctx);

            Assert.Greater(AdvisorBoard.JobBias(s, JobType.Engineer), 0.3f);
        }

        // ------------------------------------------------------------------ water
        [Test]
        public void LowWater_VotesAqueductOverWell()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Classical, SettlementTier.Village, 100, out var ctx, out var civ);
            s.ServiceCoverage[(int)ServiceType.Water] = 0.3f;

            RunOnce(ctx);

            Assert.Greater(Bias(s, BuildingType.Aqueduct), 0.5f);
            Assert.Greater(Bias(s, BuildingType.Aqueduct), Bias(s, BuildingType.Well));
            Assert.IsTrue(LogContains(s, "Water at 30%"), string.Join("\n", s.AiLog));
        }

        [Test]
        public void SmallCampLowWater_VotesWell()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Dawn, SettlementTier.Camp, 12, out var ctx, out var civ);
            s.ServiceCoverage[(int)ServiceType.Water] = 0.2f;

            RunOnce(ctx);

            Assert.Greater(Bias(s, BuildingType.Well), 0.3f);
            Assert.AreEqual(0f, Bias(s, BuildingType.Aqueduct));
        }

        [Test]
        public void OverCoverage_GivesNegativeVote()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Classical, SettlementTier.Village, 100, out var ctx, out var civ);
            s.ServiceCoverage[(int)ServiceType.Water] = 1.6f;

            RunOnce(ctx);

            Assert.Less(Bias(s, BuildingType.Well), 0f);
            Assert.Less(Bias(s, BuildingType.Aqueduct), 0f);
        }

        [Test]
        public void QueuedAqueduct_CountsAsPendingCoverage()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Classical, SettlementTier.Village, 100, out var ctx, out var civ);
            s.ServiceCoverage[(int)ServiceType.Water] = 0.3f;
            s.BuildQueue.Add(new BuildOrder { Type = BuildingType.Aqueduct, Priority = 1f, Reason = "test" });

            RunOnce(ctx);

            Assert.LessOrEqual(Bias(s, BuildingType.Aqueduct), 0f, "no double build while one is queued");
        }

        [Test]
        public void FastGrowth_AnticipatesWaterShortage()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Classical, SettlementTier.Village, 60, out var ctx, out var civ);
            var adv = RunOnce(ctx);
            Assert.AreEqual(0f, Bias(s, BuildingType.Aqueduct), "fully served and stable: nothing to do");

            for (int i = 0; i < 40; i++) ctx.Ops.CreatePerson(s, RaceId.Human, i % 2 == 0 ? Sex.Female : Sex.Male, 20f);
            RunOnce(ctx, adv);

            Assert.Greater(Bias(s, BuildingType.Aqueduct), 0f);
            Assert.IsTrue(LogContains(s, "Growth will cut water"), string.Join("\n", s.AiLog));
        }

        [Test]
        public void LostAqueduct_TriggersRebuild()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Classical, SettlementTier.Village, 100, out var ctx, out var civ);
            var aq = Place(ctx, s, BuildingType.Aqueduct, 6, 0);
            var adv = RunOnce(ctx);

            ctx.Ops.DestroyBuilding(aq, DestroyCause.Disaster);
            ctx.Ops.FlushRemovals();
            s.ServiceCoverage[(int)ServiceType.Water] = 0.4f;
            RunOnce(ctx, adv);

            Assert.Greater(Bias(s, BuildingType.Aqueduct), 0.5f);
            Assert.IsTrue(LogContains(s, "Rebuilding after losses"), string.Join("\n", s.AiLog));
        }

        // ------------------------------------------------------------------ administration & hubs
        [Test]
        public void LowAdministration_VotesForum()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Classical, SettlementTier.Village, 100, out var ctx, out var civ);
            s.ServiceCoverage[(int)ServiceType.Administration] = 0.2f;

            RunOnce(ctx);

            Assert.Greater(Bias(s, BuildingType.Forum), 0.5f);
        }

        [Test]
        public void IndustrialTown_VotesRailStation_ButHamletDoesNot()
        {
            var db = MakeDb();
            var town = MakeTown(db, AgeId.Industrial, SettlementTier.Town, 100, out var ctx, out var civ);
            town.ServiceCoverage[(int)ServiceType.Commerce] = 0.2f;
            RunOnce(ctx);
            Assert.Greater(Bias(town, BuildingType.RailStation), 0.3f);

            var hamlet = MakeTown(db, AgeId.Industrial, SettlementTier.Hamlet, 100, out var ctx2, out var civ2);
            hamlet.ServiceCoverage[(int)ServiceType.Commerce] = 0.2f;
            RunOnce(ctx2);
            Assert.AreEqual(0f, Bias(hamlet, BuildingType.RailStation));
        }

        // ------------------------------------------------------------------ robustness
        [Test]
        public void CorruptCoverage_NeverWritesNaN()
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Modern, SettlementTier.Town, 100, out var ctx, out var civ);
            Place(ctx, s, BuildingType.Factory, 8, 8);
            for (int i = 0; i < Counts.Services; i++) { s.ServiceCoverage[i] = float.NaN; s.ServiceOutput[i] = float.PositiveInfinity; }
            s.Unrest = float.NaN;

            RunOnce(ctx);

            foreach (var kv in s.Memory)
                Assert.IsFalse(float.IsNaN(kv.Value) || float.IsInfinity(kv.Value), kv.Key);
        }

        [Test]
        public void SameWorld_SameAdvice_Deterministic()
        {
            var a = Simulate(7);
            var b = Simulate(7);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i]);
        }

        private static List<string> Simulate(ulong seed)
        {
            var db = MakeDb();
            var s = MakeTown(db, AgeId.Modern, SettlementTier.Town, 100, out var ctx, out var civ, seed);
            Place(ctx, s, BuildingType.Factory, 8, 8);
            Coal(ctx, s);
            s.ServiceCoverage[(int)ServiceType.Power] = 0.2f;
            s.ServiceCoverage[(int)ServiceType.Water] = 0.5f;
            s.ServiceCoverage[(int)ServiceType.Administration] = 0.4f;
            s.ServiceCoverage[(int)ServiceType.Commerce] = 0.3f;
            var adv = new InfrastructureAdvisor();
            TestWorlds.Run(adv, ctx, 240);

            var keys = new List<string>(s.Memory.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var result = new List<string>();
            for (int i = 0; i < keys.Count; i++) result.Add(keys[i] + "=" + s.Memory[keys[i]].ToString("R"));
            result.AddRange(s.AiLog);
            Assert.Greater(s.AiLog.Count, 0);
            return result;
        }
    }
}
