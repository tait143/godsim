using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Diplomacy;
using GodSim.Tests.Shared;

namespace GodSim.Tests.Diplomacy.Military
{
    /// <summary>A two-civilization test world with the military systems wired the way the engine runs them.</summary>
    internal sealed class Battlefield
    {
        public SimContext Ctx;
        public Civilization Attacker;
        public Civilization Defender;
        public Settlement A;
        public Settlement D;
        public War War;
        public readonly MilitarySystem Military = new MilitarySystem();
        public readonly ArmySystem Armies = new ArmySystem();
        public readonly List<BattleEvent> Battles = new List<BattleEvent>();
        public bool Initialized;

        public WorldState State => Ctx.State;

        public List<Army> LivingArmies(int civId = -1)
        {
            var list = new List<Army>();
            var items = State.Diplomacy.Armies.Items;
            for (int i = 0; i < items.Count; i++)
                if (items[i].IsAlive && (civId < 0 || items[i].CivilizationId == civId)) list.Add(items[i]);
            return list;
        }

        public Army FirstArmy(int civId)
        {
            var l = LivingArmies(civId);
            return l.Count > 0 ? l[0] : null;
        }

        public void Init()
        {
            if (Initialized) return;
            Initialized = true;
            Ctx.DeltaTicks = Military.TickInterval;
            Military.Initialize(Ctx);
            Ctx.DeltaTicks = Armies.TickInterval;
            Armies.Initialize(Ctx);
            Ctx.Ops.FlushRemovals();
        }

        /// <summary>Advances one tick: MilitarySystem on multiples of 6, ArmySystem every tick, flushing after each.</summary>
        public void Step()
        {
            Init();
            Ctx.State.Tick++;
            if (Ctx.State.Tick % Military.TickInterval == 0)
            {
                Ctx.DeltaTicks = Military.TickInterval;
                Military.Tick(Ctx);
                Ctx.Ops.FlushRemovals();
            }
            Ctx.DeltaTicks = Armies.TickInterval;
            Armies.Tick(Ctx);
            Ctx.Ops.FlushRemovals();
        }

        public void Run(int ticks) { for (int i = 0; i < ticks; i++) Step(); }

        /// <summary>Steps until the predicate holds (checked after each tick) or the budget runs out; returns success.</summary>
        public bool RunUntil(System.Func<bool> done, int maxTicks)
        {
            for (int i = 0; i < maxTicks; i++)
            {
                Step();
                if (done()) return true;
            }
            return false;
        }
    }

    internal static class MilitaryTestKit
    {
        public static readonly Vec2i AttackerAt = new Vec2i(40, 56);
        public static readonly Vec2i DefenderAt = new Vec2i(70, 56);

        /// <summary>
        /// Two civs ~30 tiles apart on a 112x112 grassland map. Soldiers are the first N residents,
        /// assigned via WorldOps.AssignJob, with s.Soldiers set to match.
        /// </summary>
        public static Battlefield Create(int atkAdults, int atkSoldiers, int defAdults, int defSoldiers, float defService = 0f,
                                         bool war = true, ulong seed = 7, GameDatabase db = null,
                                         RaceId atkRace = RaceId.Human, RaceId defRace = RaceId.Elf, int size = 112)
        {
            var b = new Battlefield();
            b.Ctx = TestWorlds.CreateContext(db ?? TestDb.CreateStub(), size, seed);
            b.A = TestWorlds.AddSettlement(b.Ctx, atkRace, AttackerAt, atkAdults, out b.Attacker);
            b.D = TestWorlds.AddSettlement(b.Ctx, defRace, DefenderAt, defAdults, out b.Defender);
            MakeSoldiers(b.Ctx, b.A, atkSoldiers);
            MakeSoldiers(b.Ctx, b.D, defSoldiers);
            b.D.ServiceOutput[(int)ServiceType.Defense] = defService;
            b.Ctx.Events.Subscribe<BattleEvent>(e => b.Battles.Add(e));
            if (war) b.War = DeclareWar(b.Ctx, b.Attacker, b.Defender);
            return b;
        }

        public static void MakeSoldiers(SimContext ctx, Settlement s, int n)
        {
            int made = 0;
            for (int i = 0; i < s.ResidentIds.Count && made < n; i++)
            {
                var p = ctx.State.People.Get(s.ResidentIds[i]);
                if (p == null || !p.IsAlive || p.Job == JobType.Soldier) continue;
                ctx.Ops.AssignJob(p, null, JobType.Soldier);
                made++;
            }
            s.Soldiers += made;
        }

        /// <summary>Founds another settlement for an existing civ with N adults and S soldiers.</summary>
        public static Settlement AddTown(SimContext ctx, Civilization civ, Vec2i at, int adults, int soldiers)
        {
            var s = ctx.Ops.FoundSettlement(civ, at);
            for (int i = 0; i < adults; i++) ctx.Ops.CreatePerson(s, civ.Race, i % 2 == 0 ? Sex.Female : Sex.Male, 22f + (i % 10));
            MakeSoldiers(ctx, s, soldiers);
            return s;
        }

        public static War DeclareWar(SimContext ctx, Civilization attacker, Civilization defender)
        {
            var war = new War { Id = ctx.State.Ids.War(), AttackerId = attacker.Id, DefenderId = defender.Id, StartTick = ctx.State.Tick, IsActive = true };
            ctx.State.Diplomacy.Wars.Add(war);
            var rel = ctx.State.Diplomacy.Get(attacker.Id, defender.Id);
            rel.WarId = war.Id;
            rel.Stance = DiplomaticStance.War;
            rel.HasContact = true;
            return war;
        }

        public static void EndWar(SimContext ctx, War war)
        {
            war.IsActive = false;
            war.EndTick = ctx.State.Tick;
            if (ctx.State.Diplomacy.TryGet(war.AttackerId, war.DefenderId, out var rel)) { rel.WarId = -1; rel.Stance = DiplomaticStance.Hostile; }
        }

        /// <summary>Paints a full-height water channel over columns [x0, x1].</summary>
        public static void WaterChannel(WorldMap map, int x0, int x1)
        {
            for (int y = 0; y < map.Height; y++)
                for (int x = x0; x <= x1; x++)
                    map.Biomes[map.Index(x, y)] = Biome.DeepOcean;
            map.MarkTerrainChanged();
        }

        public static int ViolentDeaths(SimContext ctx) => (int)ctx.State.Stats.DeathsByCause[(int)DeathCause.Violence];

        public static int SoldiersOf(SimContext ctx, Settlement s)
        {
            int n = 0;
            foreach (int id in s.ResidentIds)
            {
                var p = ctx.State.People.Get(id);
                if (p != null && p.IsAlive && p.Job == JobType.Soldier) n++;
            }
            return n;
        }
    }
}
