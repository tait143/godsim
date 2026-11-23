// STUB for build/checks/Bench only (never copied to tools/). Mirrors the composition-root surface the bench uses:
//   GodSimulation.CreateNew(SimConfig, ISimLogger = null), Step(), StepMany(int), State, Engine, Db, ComputeHash().
// The real GodSimulation (GodSim.Core.Composition) is written by the lead and wires every module.
using System;
using GodSim.Core.World;

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        private GodSimulation() { }

        public WorldState State { get; private set; }
        public SimulationEngine Engine { get; private set; }
        public GameDatabase Db { get; private set; }

        public static GodSimulation CreateNew(SimConfig config, ISimLogger log = null)
        {
            var cfg = (config ?? new SimConfig()).Clone().Sanitize();
            var db = new GameDatabase();
            foreach (var race in EnumUtil.AllRaces)
                db.Register(new RaceDefinition { Id = race, Name = race.ToString(), PluralName = race + "s", Adjective = race.ToString() });
            var map = WorldGenerator.Generate(WorldGenSettings.FromConfig(cfg), new Rng(cfg.Seed));
            var state = new WorldState(cfg.Seed, map);
            var engine = new SimulationEngine(new SimContext { State = state, Db = db, Config = cfg, Log = log ?? NullLogger.Instance });
            engine.AddSystem(new StubLifecycleSystem());
            engine.AddSystem(new StubEconomySystem());

            var ops = engine.Context.Ops;
            int k = 0;
            foreach (var race in cfg.StartingRaces)
            {
                var civ = ops.CreateCivilization(race);
                var at = FindLand(map, k++, cfg.StartingRaces.Length);
                if (!at.IsValid) continue;
                var s = ops.CreateSettlement(civ, at);
                for (int i = 0; i < cfg.InitialPopulationPerRace; i++) ops.CreatePerson(s, race, (Sex)(i & 1), 18f + i);
            }
            engine.Initialize();
            return new GodSimulation { State = state, Engine = engine, Db = db };
        }

        public void Step() => Engine.Step();
        public void StepMany(int ticks) => Engine.StepMany(ticks);

        public ulong ComputeHash()
        {
            ulong h = Hashing.Combine((ulong)State.Tick, State.Rng.State);
            var p = State.People.Items;
            for (int i = 0; i < p.Count; i++) h = Hashing.Combine(h, (ulong)p[i].Id ^ (ulong)BitConverter.SingleToInt32Bits(p[i].AgeYears));
            return h;
        }

        private static Vec2i FindLand(WorldMap map, int k, int n)
        {
            double ang = 2 * Math.PI * k / Math.Max(1, n);
            int cx = map.Width / 2 + (int)(Math.Cos(ang) * map.Width * 0.2), cy = map.Height / 2 + (int)(Math.Sin(ang) * map.Height * 0.2);
            for (int r = 0; r < map.Width; r++)
                for (int y = cy - r; y <= cy + r; y++)
                    for (int x = cx - r; x <= cx + r; x++)
                        if (map.InBounds(x, y) && map.Biomes[map.Index(x, y)].IsBuildable()) return new Vec2i(x, y);
            return Vec2i.Invalid;
        }

        /// <summary>Ages people, kills a few, births a few (exercises WorldOps churn).</summary>
        private sealed class StubLifecycleSystem : ISimSystem
        {
            public string Name => "StubLifecycle";
            public int TickInterval => 1;
            public void Initialize(SimContext ctx) { }

            public void Tick(SimContext ctx)
            {
                var people = ctx.State.People.Items;
                float dYears = ctx.DeltaTicks / (float)SimTime.TicksPerYear;
                float death = Rng.PerYearToPerTicks(0.015f, ctx.DeltaTicks);
                int n = people.Count;
                for (int i = 0; i < n; i++)
                {
                    var p = people[i];
                    if (!p.IsAlive) continue;
                    p.AgeYears += dYears;
                    p.Satiation = MathUtil.Clamp01(p.Satiation - 0.001f);
                    if (ctx.Rng.Chance(death)) ctx.Ops.KillPerson(p, DeathCause.OldAge);
                }
                var towns = ctx.State.Settlements.Items;
                float birth = Rng.PerYearToPerTicks(0.03f, ctx.DeltaTicks) * ctx.Config.GrowthMultiplier;
                for (int t = 0; t < towns.Count; t++)
                {
                    var s = towns[t];
                    if (!s.IsAlive) continue;
                    int births = 0, pop = s.Population;
                    for (int i = 0; i < pop / 10 + 1; i++) if (ctx.Rng.Chance(birth * 10f)) births++;
                    for (int b = 0; b < births && ctx.State.People.Count < ctx.Config.MaxPopulation; b++)
                        ctx.Ops.CreatePerson(s, s.Race, ctx.Rng.Chance(0.5f) ? Sex.Male : Sex.Female, 0f, -1, -1, true);
                }
            }
        }

        private sealed class StubEconomySystem : ISimSystem
        {
            public string Name => "StubEconomy";
            public int TickInterval => 4;
            public void Initialize(SimContext ctx) { }

            public void Tick(SimContext ctx)
            {
                var towns = ctx.State.Settlements.Items;
                for (int t = 0; t < towns.Count; t++)
                {
                    var s = towns[t];
                    if (!s.IsAlive) continue;
                    s.Stockpile.Add(ResourceType.Food, s.Population * 0.06f * ctx.DeltaTicks);
                    s.Stockpile.ConsumeUpTo(ResourceType.Food, s.Population * 0.05f * ctx.DeltaTicks);
                    s.Stockpile.RollStatistics();
                }
            }
        }
    }
}
