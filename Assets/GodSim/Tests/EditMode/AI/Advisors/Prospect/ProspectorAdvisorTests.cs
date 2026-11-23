using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI
{
    public class ProspectorAdvisorTests
    {
        // ------------------------------------------------------------------ fixtures
        private static BuildingDefinition Extractor(BuildingType t, AgeId minAge, PlacementRule rule, JobType job, ResourceType res, float perWorker)
        {
            return new BuildingDefinition
            {
                Type = t, Name = t.ToString(), Category = BuildingCategory.Resource, MinAge = minAge,
                Job = job, JobSlots = 3, OutputsPerWorker = new[] { new ResourceAmount(res, perWorker) }, Placement = rule,
            };
        }

        private static GameDatabase MakeDb(AgeId coalMinAge = AgeId.Industrial)
        {
            var db = new GameDatabase();
            db.Register(Extractor(BuildingType.WoodcutterCamp, AgeId.Dawn, PlacementRule.NearForest | PlacementRule.Outskirts, JobType.Woodcutter, ResourceType.Wood, 0.12f));
            db.Register(Extractor(BuildingType.StoneQuarry, AgeId.Dawn, PlacementRule.NearStone, JobType.Quarrier, ResourceType.Stone, 0.08f));
            db.Register(Extractor(BuildingType.Mine, AgeId.Bronze, PlacementRule.NearOre, JobType.Miner, ResourceType.Ore, 0.07f));
            db.Register(Extractor(BuildingType.GoldMine, AgeId.Iron, PlacementRule.NearGold, JobType.Miner, ResourceType.Gold, 0.03f));
            db.Register(Extractor(BuildingType.CoalMine, coalMinAge, PlacementRule.NearCoal | PlacementRule.Isolated, JobType.Miner, ResourceType.Fuel, 0.10f));
            db.Register(Extractor(BuildingType.Sawmill, AgeId.Industrial, PlacementRule.NearForest, JobType.Laborer, ResourceType.Wood, 0.25f));
            db.Register(Extractor(BuildingType.OilWell, AgeId.Modern, PlacementRule.NearOil | PlacementRule.Isolated, JobType.Miner, ResourceType.Fuel, 0.18f));
            db.Register(new BuildingDefinition
            {
                Type = BuildingType.Market, Name = "Market", Category = BuildingCategory.Commerce, MinAge = AgeId.Iron,
                Job = JobType.Merchant, JobSlots = 2, Services = new[] { new ServiceAmount(ServiceType.Commerce, 4f) },
            });
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static void Paint(WorldMap map, int x0, int y0, int w, int h, TileFlags flag, float perTile)
        {
            for (int y = y0; y < y0 + h; y++)
                for (int x = x0; x < x0 + w; x++)
                {
                    int i = map.Index(x, y);
                    map.Flags[i] |= flag;
                    map.Resource[i] = perTile;
                }
        }

        private static Building Place(SimContext ctx, Settlement s, BuildingType t, int x, int y)
        {
            var b = ctx.Ops.CreateBuilding(s, t, new Vec2i(x, y), 0, true);
            Assert.IsNotNull(b, "test building placement failed");
            return b;
        }

        private static bool LogContains(Settlement s, string text)
        {
            for (int i = 0; i < s.AiLog.Count; i++) if (s.AiLog[i].Contains(text)) return true;
            return false;
        }

        private static string Dump(Settlement s)
        {
            var keys = new List<string>(s.Memory.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            var sb = new StringBuilder();
            for (int i = 0; i < keys.Count; i++) sb.Append(keys[i]).Append('=').Append(s.Memory[keys[i]].ToString("R")).Append(';');
            for (int i = 0; i < s.AiLog.Count; i++) sb.Append(s.AiLog[i]).Append('|');
            return sb.ToString();
        }

        // ------------------------------------------------------------------ tests
        [Test]
        public void RichUntappedGoldVein_VotesForGoldMine_WithReason()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 6, out var civ);
            civ.Age = AgeId.Iron;
            Paint(ctx.State.Map, s.Center.X + 3, s.Center.Y - 2, 2, 2, TileFlags.GoldDeposit, 460f); // 1,840 total
            s.Stockpile.AvgExpense[(int)ResourceType.Gold] = 0.2f;                                  // running a gold deficit

            var adv = new ProspectorAdvisor();
            TestWorlds.Run(adv, ctx, 48);

            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.GoldMine), 0.5f);
            Assert.IsTrue(LogContains(s, "Rich gold vein (1,840) untapped: GoldMine"), string.Join("\n", s.AiLog));
            Assert.Greater(AdvisorBoard.Urgency(s, ProspectorAdvisor.Id), 0f);
            Assert.AreEqual(1840f, ProspectorAdvisor.Remaining(s, ProspectorDeposit.Gold), 0.01f);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.Mine)); // no ore here
        }

        [Test]
        public void ExhaustedCoalSeam_VotesAgainstCoalMine_AndForOilWellAlternative()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 6, out var civ);
            civ.Age = AgeId.Modern;
            var map = ctx.State.Map;
            Paint(map, s.Center.X - 4, s.Center.Y + 2, 2, 1, TileFlags.CoalDeposit, 20f);   // 40 left
            Place(ctx, s, BuildingType.CoalMine, s.Center.X - 4, s.Center.Y + 3);
            Paint(map, s.Center.X + 3, s.Center.Y + 3, 5, 1, TileFlags.OilDeposit, 600f);   // 3,000 untapped
            s.Stockpile.AvgExpense[(int)ResourceType.Fuel] = 0.3f;

            var adv = new ProspectorAdvisor();
            TestWorlds.Run(adv, ctx, 48);

            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.CoalMine), 0f);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.OilWell), 0.5f);
            Assert.IsTrue(LogContains(s, "Coal seam nearly exhausted"), string.Join("\n", s.AiLog));
            Assert.IsTrue(LogContains(s, "OilWell on oil field"), string.Join("\n", s.AiLog));
            Assert.Greater(AdvisorBoard.Urgency(s, ProspectorAdvisor.Id), 0.5f);
        }

        [Test]
        public void ExhaustionIsMeasuredAgainstThePeak_AfterResurvey()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 6, out var civ);
            civ.Age = AgeId.Industrial;
            var map = ctx.State.Map;
            int x0 = s.Center.X - 5, y0 = s.Center.Y - 4;
            Paint(map, x0, y0, 5, 1, TileFlags.CoalDeposit, 600f); // 3,000 at first survey
            Place(ctx, s, BuildingType.CoalMine, x0, y0 + 1);

            var adv = new ProspectorAdvisor();
            TestWorlds.Run(adv, ctx, 192);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.CoalMine), "healthy seam: no warning");

            for (int x = x0; x < x0 + 5; x++) map.Resource[map.Index(x, y0)] = 40f; // 200 left: above the floor, but ~7% of peak
            TestWorlds.Run(adv, ctx, 48, false); // tick 240: the 2-year refresh notices

            Assert.Less(AdvisorBoard.BuildBias(s, BuildingType.CoalMine), 0f);
            Assert.AreEqual(3000f, ProspectorAdvisor.Peak(s, ProspectorDeposit.Coal), 0.01f);
            Assert.Less(ProspectorAdvisor.YearsLeft(s, ProspectorDeposit.Coal), 1f);
        }

        [Test]
        public void ExhaustedForest_RelocatesWoodcuttingToSisterSettlement()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var s1 = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(14, 14), 6, out var civ);
            var s2 = ctx.Ops.FoundSettlement(civ, new Vec2i(48, 48));
            var map = ctx.State.Map;
            Paint(map, s1.Center.X + 2, s1.Center.Y + 2, 2, 1, TileFlags.Trees, 30f);  // 60 wood left at home
            Place(ctx, s1, BuildingType.WoodcutterCamp, s1.Center.X + 2, s1.Center.Y + 3);
            Paint(map, s2.Center.X - 5, s2.Center.Y - 5, 8, 5, TileFlags.Trees, 100f); // 4,000 wood at the sister town
            s1.Stockpile.AvgExpense[(int)ResourceType.Wood] = 0.3f;

            TestWorlds.Run(new ProspectorAdvisor(), ctx, 48);

            Assert.Less(AdvisorBoard.BuildBias(s1, BuildingType.WoodcutterCamp), 0f);
            Assert.Greater(AdvisorBoard.BuildBias(s2, BuildingType.WoodcutterCamp), 0.5f);
            Assert.IsTrue(LogContains(s2, "Relocate Wood work here"), string.Join("\n", s2.AiLog));
        }

        [Test]
        public void NoDeposits_NoVotes()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(32, 32), 6, out var civ);
            civ.Age = AgeId.Modern;
            for (int r = 0; r < Counts.Resources; r++) s.Stockpile.AvgExpense[r] = 0.3f; // needs everything, has nothing

            TestWorlds.Run(new ProspectorAdvisor(), ctx, 96);

            foreach (var kv in s.Memory) Assert.IsFalse(kv.Key.StartsWith("ai.bias."), "unexpected vote " + kv.Key);
            Assert.AreEqual(0f, AdvisorBoard.Urgency(s, ProspectorAdvisor.Id));
        }

        [Test]
        public void ElvesPracticeForestStewardship_WhenTreesAreScarce()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var elves = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(16, 16), 6, out var elfCiv);
            var humans = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(48, 48), 6, out var humanCiv);
            var map = ctx.State.Map;
            Paint(map, elves.Center.X + 2, elves.Center.Y + 2, 3, 1, TileFlags.Trees, 45f);   // 135 wood: scarce
            Paint(map, humans.Center.X + 2, humans.Center.Y + 2, 3, 1, TileFlags.Trees, 45f);
            Place(ctx, elves, BuildingType.WoodcutterCamp, elves.Center.X + 2, elves.Center.Y + 3);
            elves.Stockpile.AvgExpense[(int)ResourceType.Wood] = 0.3f;
            humans.Stockpile.AvgExpense[(int)ResourceType.Wood] = 0.3f;

            TestWorlds.Run(new ProspectorAdvisor(), ctx, 48);

            Assert.Less(AdvisorBoard.BuildBias(elves, BuildingType.WoodcutterCamp), 0f);
            Assert.Less(AdvisorBoard.JobBias(elves, JobType.Woodcutter), 0f);
            Assert.IsTrue(LogContains(elves, "Forest stewardship"), string.Join("\n", elves.AiLog));
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(humans, BuildingType.WoodcutterCamp), "humans have no stewardship rule and no woodcutter");
        }

        [Test]
        public void Elves_StillCutSparingly_InAbundantForest()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Elf, new Vec2i(32, 32), 6, out _);
            Paint(ctx.State.Map, s.Center.X - 8, s.Center.Y - 8, 17, 5, TileFlags.Trees, 100f); // 85 groves, 8,500 wood
            s.Stockpile.AvgExpense[(int)ResourceType.Wood] = 0.3f;

            TestWorlds.Run(new ProspectorAdvisor(), ctx, 48);

            float bias = AdvisorBoard.BuildBias(s, BuildingType.WoodcutterCamp);
            Assert.Greater(bias, 0f);
            Assert.LessOrEqual(bias, 0.5f);
        }

        [Test]
        public void CoalIgnoredBeforeIndustrial()
        {
            var db = MakeDb(coalMinAge: AgeId.Dawn); // even if data made the mine buildable, coal does not matter yet
            var ctx = TestWorlds.CreateContext(db);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(32, 32), 6, out var civ);
            civ.Age = AgeId.Renaissance;
            Paint(ctx.State.Map, s.Center.X + 2, s.Center.Y - 4, 4, 2, TileFlags.CoalDeposit, 500f);
            s.Stockpile.AvgExpense[(int)ResourceType.Fuel] = 0.3f;

            var adv = new ProspectorAdvisor();
            TestWorlds.Run(adv, ctx, 48);
            Assert.AreEqual(0f, AdvisorBoard.BuildBias(s, BuildingType.CoalMine));

            civ.Age = AgeId.Industrial;
            TestWorlds.Run(adv, ctx, 48, false);
            Assert.Greater(AdvisorBoard.BuildBias(s, BuildingType.CoalMine), 0.5f);
        }

        [Test]
        public void Deterministic_SameWorldSameAdvice()
        {
            Assert.AreEqual(RunScenario(), RunScenario());
        }

        private static string RunScenario()
        {
            var db = MakeDb();
            var ctx = TestWorlds.CreateContext(db, 64, 7);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(30, 30), 8, out var civ);
            civ.Age = AgeId.Modern;
            var map = ctx.State.Map;
            Paint(map, s.Center.X + 2, s.Center.Y + 2, 3, 3, TileFlags.OreDeposit, 300f);
            Paint(map, s.Center.X - 5, s.Center.Y + 2, 3, 2, TileFlags.StoneDeposit, 400f);
            Paint(map, s.Center.X - 5, s.Center.Y - 5, 2, 2, TileFlags.CoalDeposit, 30f);
            Paint(map, s.Center.X + 3, s.Center.Y - 5, 4, 2, TileFlags.OilDeposit, 500f);
            Paint(map, s.Center.X - 2, s.Center.Y - 7, 6, 2, TileFlags.Trees, 80f);
            Place(ctx, s, BuildingType.CoalMine, s.Center.X - 5, s.Center.Y - 3);
            s.Stockpile.AvgExpense[(int)ResourceType.Ore] = 0.2f;
            s.Stockpile.AvgExpense[(int)ResourceType.Fuel] = 0.2f;
            var adv = new ProspectorAdvisor();
            TestWorlds.Run(adv, ctx, 240);
            for (int i = 0; i < map.TileCount; i++) if (map.Resource[i] > 0f) map.Resource[i] *= 0.5f;
            TestWorlds.Run(adv, ctx, 240, false);
            return Dump(s);
        }
    }
}
