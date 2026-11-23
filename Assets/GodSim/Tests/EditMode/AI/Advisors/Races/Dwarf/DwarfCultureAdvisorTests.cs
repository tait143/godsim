using System.Collections.Generic;
using System.Text;
using GodSim.Core;
using GodSim.Core.AI;
using GodSim.Core.Races;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.AI.Advisors.Races.Dwarf
{
    public class DwarfCultureAdvisorTests
    {
        // ------------------------------------------------------------------ kit
        private static ResourceAmount R(ResourceType t, float a) => new ResourceAmount(t, a);

        private static BuildingDefinition Def(BuildingType t, AgeId min, AgeId max = AgeId.Ascension, JobType job = JobType.None,
            SettlementTier tier = SettlementTier.Camp, int maxPer = -1)
        {
            return new BuildingDefinition
            {
                Type = t, Name = t.ToString(), Category = BuildingCategory.Civic, MinAge = min, MaxAge = max, MinTier = tier,
                MaxPerSettlement = maxPer, Job = job, JobSlots = job == JobType.None ? 0 : 3, BuildWork = 20f,
            };
        }

        private static GameDatabase CreateDb()
        {
            var db = new GameDatabase();
            RaceCatalog.Register(db);
            db.Register(Def(BuildingType.StoneQuarry, AgeId.Dawn, job: JobType.Quarrier));
            db.Register(Def(BuildingType.Mine, AgeId.Bronze, job: JobType.Miner));
            db.Register(Def(BuildingType.Smelter, AgeId.Bronze, job: JobType.Smith));
            db.Register(Def(BuildingType.Blacksmith, AgeId.Iron, job: JobType.Smith));
            db.Register(Def(BuildingType.GoldMine, AgeId.Iron, job: JobType.Miner));
            db.Register(Def(BuildingType.CoalMine, AgeId.Industrial, job: JobType.Miner));
            db.Register(Def(BuildingType.Granary, AgeId.Bronze));
            db.Register(Def(BuildingType.Palisade, AgeId.Bronze, AgeId.Medieval, tier: SettlementTier.Hamlet));
            db.Register(Def(BuildingType.StoneWall, AgeId.Classical, AgeId.Industrial, tier: SettlementTier.Hamlet, maxPer: 24));
            db.Register(Def(BuildingType.Watchtower, AgeId.Iron, AgeId.Industrial, JobType.Soldier));
            db.Register(Def(BuildingType.Barracks, AgeId.Iron, job: JobType.Soldier));
            db.Register(Def(BuildingType.Armory, AgeId.Iron, job: JobType.Smith));
            db.Register(Def(BuildingType.Castle, AgeId.Medieval, maxPer: 1));
            db.Register(Def(BuildingType.Tavern, AgeId.Medieval, job: JobType.Entertainer));
            db.Register(Def(BuildingType.Arena, AgeId.Classical));
            db.Register(Def(BuildingType.Foundry, AgeId.Industrial, job: JobType.Smith));
            db.Register(Def(BuildingType.Factory, AgeId.Industrial, job: JobType.Engineer));
            db.Register(Def(BuildingType.MilitaryBase, AgeId.Modern, job: JobType.Soldier));
            var forge = Def(BuildingType.DeepForge, AgeId.Bronze, job: JobType.Smith, tier: SettlementTier.Village);
            forge.AllowedRaces = RaceMask.Dwarf;
            forge.IsWonder = true;
            forge.UniquePerCivilization = true;
            forge.Cost = new[] { R(ResourceType.Wood, 120f), R(ResourceType.Stone, 300f) };
            db.Register(forge);
            TestDb.FillMissing(db);
            db.Freeze();
            return db;
        }

        private static SimContext CreateWorld(out GameDatabase db, ulong seed = 7)
        {
            db = CreateDb();
            return TestWorlds.CreateContext(db, 64, seed);
        }

        private static void SetFlag(SimContext ctx, Vec2i c, int count, TileFlags flag, int offsetX)
        {
            var map = ctx.State.Map;
            for (int i = 0; i < count; i++) map.Set(map.Index(c.X + offsetX + (i % 3), c.Y - 2 + i / 3), flag);
        }

        private static void RunOnce(DwarfCultureAdvisor adv, SimContext ctx)
        {
            ctx.State.Tick += adv.TickInterval;
            ctx.DeltaTicks = adv.TickInterval;
            adv.Tick(ctx);
        }

        private static void ClearVotes(Settlement s) => AdvisorBoard.Decay(s, 0f);

        private static float B(Settlement s, BuildingType t) => AdvisorBoard.BuildBias(s, t);
        private static float J(Settlement s, JobType j) => AdvisorBoard.JobBias(s, j);

        // ------------------------------------------------------------------ tests
        [Test]
        public void Deposits_LeadToEarlyMineQuarryAndSmelterVotes()
        {
            var ctx = CreateWorld(out _);
            var rich = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(16, 32), 6, out var civ);
            civ.Age = AgeId.Bronze;
            SetFlag(ctx, rich.Center, 6, TileFlags.OreDeposit, 3);
            SetFlag(ctx, rich.Center, 6, TileFlags.StoneDeposit, -5);
            var poor = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(48, 32), 6, out var civ2);
            civ2.Age = AgeId.Bronze;

            var adv = new DwarfCultureAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx);

            Assert.That(B(rich, BuildingType.Mine), Is.GreaterThan(0.8f), "a camp with ore digs at once");
            Assert.That(B(rich, BuildingType.StoneQuarry), Is.GreaterThan(0.7f));
            Assert.That(B(rich, BuildingType.Smelter), Is.GreaterThan(0.3f), "the smelter follows the mine");
            Assert.AreEqual(0f, B(poor, BuildingType.Mine), "no deposit, no mine");
            Assert.AreEqual(0f, B(rich, BuildingType.GoldMine), "gold mines are not known in the Bronze age");
            Assert.That(J(rich, JobType.Miner), Is.GreaterThan(0.4f));
            Assert.That(J(rich, JobType.Merchant), Is.LessThan(0f));
            Assert.That(AdvisorBoard.Urgency(rich, DwarfCultureAdvisor.Id), Is.GreaterThan(0.5f));

            // Mine placed -> blacksmith once Iron is reached, and the smelter vote strengthens.
            ClearVotes(rich);
            civ.Age = AgeId.Iron;
            SetFlag(ctx, rich.Center, 3, TileFlags.GoldDeposit, 6);
            var site = ctx.Ops.FindFreeSite(rich.Center, 1, 1, 8);
            ctx.Ops.CreateBuilding(rich, BuildingType.Mine, site, 0, true);
            site = ctx.Ops.FindFreeSite(rich.Center, 1, 1, 8);
            ctx.Ops.CreateBuilding(rich, BuildingType.Smelter, site, 0, true);
            RunOnce(adv, ctx);
            Assert.That(B(rich, BuildingType.Blacksmith), Is.GreaterThan(0.5f));
            Assert.That(B(rich, BuildingType.GoldMine), Is.GreaterThan(0.5f));
        }

        [Test]
        public void Hoarding_VotesStorageEarlyAndMoreWhenVaultsFill()
        {
            var ctx = CreateWorld(out _);
            var empty = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(16, 32), 8, out var c1);
            var full = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(48, 32), 8, out var c2);
            c1.Age = c2.Age = AgeId.Bronze;
            full.Stockpile.SetRaw(ResourceType.Stone, full.Stockpile.Capacity * 0.95f);
            full.Stockpile.SetRaw(ResourceType.Food, full.Stockpile.Capacity * 0.9f);

            var adv = new DwarfCultureAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx);

            Assert.That(B(empty, BuildingType.Storehouse), Is.GreaterThan(0.4f), "a full hold is a happy hold");
            Assert.That(B(empty, BuildingType.Granary), Is.GreaterThan(0.4f));
            Assert.That(B(full, BuildingType.Storehouse), Is.GreaterThan(B(empty, BuildingType.Storehouse)));
            Assert.That(B(full, BuildingType.Granary), Is.GreaterThan(B(empty, BuildingType.Granary)));
            Assert.That(J(empty, JobType.Quarrier), Is.GreaterThan(J(full, JobType.Quarrier)), "thin stone stocks are refilled");
        }

        [Test]
        public void Walls_PalisadeFirstThenReplacedByStone()
        {
            var ctx = CreateWorld(out _);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(32, 32), 20, out var civ);
            s.Tier = SettlementTier.Hamlet;
            civ.Age = AgeId.Bronze;
            var adv = new DwarfCultureAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx);
            Assert.That(B(s, BuildingType.Palisade), Is.GreaterThan(0.4f), "stakes until stone is known");
            Assert.AreEqual(0f, B(s, BuildingType.StoneWall), "stone walls are not available yet");

            ctx.Ops.CreateBuilding(s, BuildingType.Palisade, ctx.Ops.FindFreeSite(s.Center, 1, 1, 8), 0, true);
            civ.Age = AgeId.Classical;
            ClearVotes(s);
            RunOnce(adv, ctx);
            float withStakes = B(s, BuildingType.StoneWall);
            Assert.That(B(s, BuildingType.Palisade), Is.LessThan(-0.5f), "no more timber stakes");
            Assert.That(withStakes, Is.GreaterThan(0.8f), "replace the stakes with granite");

            // Without stakes standing the push is still there, but calmer.
            var s2 = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(10, 10), 20, out var civ2);
            s2.Tier = SettlementTier.Hamlet;
            civ2.Age = AgeId.Classical;
            RunOnce(adv, ctx);
            Assert.That(B(s2, BuildingType.StoneWall), Is.GreaterThan(0.5f));
            Assert.That(B(s2, BuildingType.StoneWall), Is.LessThan(withStakes));

            // Watchtowers crown the hills (Iron+).
            var map = ctx.State.Map;
            for (int y = 28; y <= 36; y++) for (int x = 28; x <= 36; x++) map.Biomes[map.Index(x, y)] = Biome.Hills;
            ClearVotes(s);
            RunOnce(adv, ctx);
            Assert.That(B(s, BuildingType.Watchtower), Is.GreaterThan(0.5f));
        }

        [Test]
        public void Grudge_IsRecordedRaisesMilitaryAndDecaysSlowly()
        {
            var ctx = CreateWorld(out _);
            var hold = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(16, 32), 12, out var dwarves);
            TestWorlds.AddSettlement(ctx, RaceId.Orc, new Vec2i(44, 32), 12, out var orcs);
            dwarves.Age = AgeId.Iron;
            var adv = new DwarfCultureAdvisor();
            adv.Initialize(ctx);

            RunOnce(adv, ctx);
            Assert.AreEqual(0f, B(hold, BuildingType.Barracks), "no grudge, no muster");

            ctx.Events.Publish(new BattleEvent { AttackerCivId = orcs.Id, DefenderCivId = dwarves.Id, AttackerWon = true, DefenderLosses = 5, SettlementId = hold.Id });
            float g0 = DwarfCultureAdvisor.Grudge(dwarves, orcs.Id);
            Assert.That(g0, Is.GreaterThan(0.4f));
            Assert.IsTrue(dwarves.Memory.ContainsKey("race.dwarf.grudge." + orcs.Id));

            RunOnce(adv, ctx);
            Assert.That(B(hold, BuildingType.Barracks), Is.GreaterThan(0.2f));
            Assert.That(J(hold, JobType.Soldier), Is.GreaterThan(0.2f));

            // Twenty years on, the grudge is still more than half of what it was.
            float prev = DwarfCultureAdvisor.Grudge(dwarves, orcs.Id);
            for (int year = 0; year < 20; year++)
            {
                TestWorlds.Run(adv, ctx, SimTime.TicksPerYear, false);
                float g = DwarfCultureAdvisor.Grudge(dwarves, orcs.Id);
                Assert.That(g, Is.LessThan(prev + 1e-6f), "grudges never grow on their own");
                prev = g;
            }
            Assert.That(prev, Is.GreaterThan(0.5f * g0), "decades later the grudge remains");
            Assert.That(prev, Is.LessThan(g0));
            Assert.That(B(hold, BuildingType.Barracks), Is.GreaterThan(0.05f), "military votes stay elevated while the orcs are near");

            // A building lost to an active war deepens the grudge; a captured hold maxes it out.
            ctx.State.Diplomacy.Wars.Add(new War { Id = 1, AttackerId = orcs.Id, DefenderId = dwarves.Id, IsActive = true });
            var hut = ctx.Ops.CreateBuilding(hold, BuildingType.Storehouse, ctx.Ops.FindFreeSite(hold.Center, 1, 1, 8), 0, true);
            float before = DwarfCultureAdvisor.Grudge(dwarves, orcs.Id);
            ctx.Ops.DestroyBuilding(hut, DestroyCause.War);
            Assert.That(DwarfCultureAdvisor.Grudge(dwarves, orcs.Id), Is.GreaterThan(before));
            ctx.Events.Publish(new SettlementCapturedEvent { SettlementId = hold.Id, OldCivilizationId = dwarves.Id, NewCivilizationId = orcs.Id });
            Assert.AreEqual(1f, DwarfCultureAdvisor.Grudge(dwarves, orcs.Id), 1e-5f);
        }

        [Test]
        public void Forge_StockpilesAtCapitalUntilQueued()
        {
            var ctx = CreateWorld(out _);
            var cap = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(32, 32), 20, out var civ);
            cap.Tier = SettlementTier.Village;
            civ.Age = AgeId.Bronze;
            SetFlag(ctx, cap.Center, 6, TileFlags.StoneDeposit, 3);
            var adv = new DwarfCultureAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx);
            Assert.That(B(cap, BuildingType.DeepForge), Is.GreaterThan(0.4f));
            Assert.That(B(cap, BuildingType.Storehouse), Is.GreaterThan(0.7f), "the vault must hold 300 stone");
            Assert.That(J(cap, JobType.Quarrier), Is.GreaterThan(0.6f), "hauling stone for the forge");

            ClearVotes(cap);
            cap.BuildQueue.Add(new BuildOrder { Type = BuildingType.DeepForge, Priority = 5f });
            RunOnce(adv, ctx);
            Assert.AreEqual(0f, B(cap, BuildingType.DeepForge), "already queued");
            Assert.AreEqual(1f, civ.Mem(DwarfCultureKeys.ForgeState));
        }

        [Test]
        public void Earthquake_LeadsToRebuildingInStone()
        {
            var ctx = CreateWorld(out _);
            var shaken = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(16, 32), 10, out var c1);
            var calm = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(48, 32), 10, out var c2);
            c1.Age = c2.Age = AgeId.Bronze;
            SetFlag(ctx, shaken.Center, 6, TileFlags.StoneDeposit, 3);
            SetFlag(ctx, calm.Center, 6, TileFlags.StoneDeposit, 3);
            foreach (var s in new[] { shaken, calm })
            {
                ctx.Ops.CreateBuilding(s, BuildingType.StoneQuarry, ctx.Ops.FindFreeSite(s.Center, 1, 1, 8), 0, true);
                s.Stockpile.SetRaw(ResourceType.Stone, 100f);
            }
            var adv = new DwarfCultureAdvisor();
            adv.Initialize(ctx);
            ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Earthquake, Position = shaken.Center, Radius = 4, Severity = 0.9f });
            RunOnce(adv, ctx);
            Assert.AreEqual(0f, B(calm, BuildingType.StoneQuarry), "one quarry and a full stone vault is enough in calm times");
            Assert.That(B(shaken, BuildingType.StoneQuarry), Is.GreaterThan(0.5f), "the mountain shook; rebuild in stone");
            Assert.That(J(shaken, JobType.Quarrier), Is.GreaterThan(J(calm, JobType.Quarrier)));
            Assert.That(J(shaken, JobType.Builder), Is.GreaterThan(0.2f));

            TestWorlds.Run(adv, ctx, DwarfCultureKeys.RebuildWindowTicks + 48, false);
            Assert.IsFalse(shaken.Memory.ContainsKey(DwarfCultureKeys.QuakeTick), "the rebuilding ends");
        }

        [Test]
        public void IndustrialAge_EmbracesFoundriesAndFactories()
        {
            var ctx = CreateWorld(out _);
            var s = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(32, 32), 12, out var civ);
            civ.Age = AgeId.Medieval;
            var adv = new DwarfCultureAdvisor();
            adv.Initialize(ctx);
            RunOnce(adv, ctx);
            Assert.AreEqual(0f, B(s, BuildingType.Foundry));
            Assert.That(B(s, BuildingType.Tavern), Is.GreaterThan(0.2f), "feast halls");
            Assert.That(B(s, BuildingType.Arena), Is.LessThan(0f));
            civ.Age = AgeId.Industrial;
            ClearVotes(s);
            RunOnce(adv, ctx);
            Assert.That(B(s, BuildingType.Foundry), Is.GreaterThan(0.5f));
            Assert.That(B(s, BuildingType.Factory), Is.GreaterThan(0.4f));
        }

        [Test]
        public void NonDwarves_AreUntouched()
        {
            var ctx = CreateWorld(out _);
            var races = new[] { RaceId.Human, RaceId.Elf, RaceId.Orc, RaceId.Saurian };
            var settlements = new List<Settlement>();
            var civs = new List<Civilization>();
            for (int i = 0; i < races.Length; i++)
            {
                var s = TestWorlds.AddSettlement(ctx, races[i], new Vec2i(10 + i * 14, 32), 20, out var civ);
                civ.Age = AgeId.Classical;
                s.Tier = SettlementTier.Village;
                SetFlag(ctx, s.Center, 6, TileFlags.OreDeposit, 3);
                s.Stockpile.SetRaw(ResourceType.Stone, s.Stockpile.Capacity);
                settlements.Add(s);
                civs.Add(civ);
            }
            var adv = new DwarfCultureAdvisor();
            adv.Initialize(ctx);
            ctx.Events.Publish(new BattleEvent { AttackerCivId = civs[3].Id, DefenderCivId = civs[0].Id, AttackerWon = true });
            ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Earthquake, Position = settlements[0].Center, Radius = 30, Severity = 1f });
            TestWorlds.Run(adv, ctx, 240, false);
            for (int i = 0; i < settlements.Count; i++)
            {
                Assert.AreEqual(0, settlements[i].Memory.Count, races[i] + " settlement memory must stay empty");
                Assert.AreEqual(0, civs[i].Memory.Count, races[i] + " civ memory must stay empty");
            }
        }

        [Test]
        public void Advisor_IsDeterministic()
        {
            Assert.AreEqual(Simulate(11), Simulate(11));
        }

        private static string Simulate(ulong seed)
        {
            var ctx = CreateWorld(out _, seed);
            var a = TestWorlds.AddSettlement(ctx, RaceId.Dwarf, new Vec2i(16, 32), 18, out var dwarves);
            var b = TestWorlds.AddSettlement(ctx, RaceId.Human, new Vec2i(44, 32), 18, out var humans);
            dwarves.Age = AgeId.Classical;
            a.Tier = SettlementTier.Village;
            SetFlag(ctx, a.Center, 6, TileFlags.OreDeposit, 3);
            SetFlag(ctx, a.Center, 4, TileFlags.StoneDeposit, -5);
            var adv = new DwarfCultureAdvisor();
            adv.Initialize(ctx);
            ctx.Events.Publish(new BattleEvent { AttackerCivId = humans.Id, DefenderCivId = dwarves.Id, AttackerWon = true });
            ctx.Events.Publish(new DisasterEvent { Type = DisasterType.Earthquake, Position = a.Center, Radius = 3, Severity = 0.7f });
            TestWorlds.Run(adv, ctx, SimTime.TicksPerYear * 3, false);
            var sb = new StringBuilder();
            Dump(sb, a.Memory);
            Dump(sb, b.Memory);
            Dump(sb, dwarves.Memory);
            Assert.That(sb.Length, Is.GreaterThan(0));
            return sb.ToString();
        }

        private static void Dump(StringBuilder sb, Dictionary<string, float> mem)
        {
            var keys = new List<string>(mem.Keys);
            keys.Sort(System.StringComparer.Ordinal);
            foreach (var k in keys)
            {
                float v = mem[k];
                Assert.IsFalse(float.IsNaN(v) || float.IsInfinity(v), k);
                sb.Append(k).Append('=').Append(v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)).Append(';');
            }
            sb.Append('|');
        }
    }
}
