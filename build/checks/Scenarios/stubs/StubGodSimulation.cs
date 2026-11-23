// STUB for build/checks/Scenarios ONLY. Never copy into tests/ or Assets/.
// Mirrors the composition-root surface the scenario tests use (namespace GodSim.Core.Composition):
//   Config, Db, State, Events, Engine, Commands, Ops, Tick, BuildDatabase(), CreateNew(), Load(), Save(),
//   ComputeHash(), Step(), StepMany(), TryCastPower().
// It builds a tiny flat world with civs/settlements/people and a minimal command-draining system, so the
// harness code paths can be smoke-run. Scenario assertions are NOT expected to pass against it.
using System;
using System.IO;

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        private GodSimulation() { }

        public SimConfig Config { get; private set; }
        public GameDatabase Db { get; private set; }
        public WorldState State { get; private set; }
        public EventBus Events { get; private set; }
        public SimulationEngine Engine { get; private set; }
        public CommandQueue Commands { get; private set; }
        public WorldOps Ops { get; private set; }
        public long Tick => State.Tick;

        public static GameDatabase BuildDatabase()
        {
            var db = new GameDatabase();
            for (int i = 0; i < Counts.Races; i++)
            {
                var r = new RaceDefinition { Id = (RaceId)i, Name = ((RaceId)i).ToString(), PluralName = ((RaceId)i) + "s", Adjective = ((RaceId)i).ToString() };
                for (int b = 0; b < Counts.Biomes; b++) r.BiomeHabitability[b] = ((Biome)b).IsWater() ? 0f : 0.6f;
                for (int a = 0; a < Counts.Races; a++) r.RaceAffinity[a] = a == i ? 1f : 0f;
                db.Register(r);
            }
            for (int i = 0; i < Counts.Ages; i++) db.Register(new AgeDefinition { Id = (AgeId)i, Name = ((AgeId)i).ToString(), ResearchThreshold = i * 1000f });
            for (int i = 0; i < Counts.Buildings; i++)
            {
                var t = (BuildingType)i;
                var d = new BuildingDefinition { Type = t, Name = t.ToString(), Category = BuildingCategory.Civic };
                if (t == BuildingType.House) { d.Category = BuildingCategory.Housing; d.HousingMin = 3; d.HousingMax = 5; }
                if (t == BuildingType.TownCenter) { d.MaxPerSettlement = 1; d.Width = 2; d.Height = 2; }
                db.Register(d);
            }
            for (int i = 0; i < Counts.GodPowers; i++)
                db.Register(new GodPowerDefinition
                {
                    Type = (GodPowerType)i, Name = ((GodPowerType)i).ToString(), ManaCost = 10f, CooldownTicks = 5, MinRadius = 0, MaxRadius = 8,
                    UsesRaceParameter = (GodPowerType)i == GodPowerType.SpawnTribe,
                });
            db.Register(new TechDefinition { Key = "stub.tech", Name = "Stub Tech", Age = AgeId.Dawn, Cost = 50f });
            db.Freeze();
            return db;
        }

        public static GodSimulation CreateNew(SimConfig config, ISimLogger log = null)
        {
            var cfg = (config ?? new SimConfig()).Clone().Sanitize();
            var map = new WorldMap(cfg.MapWidth, cfg.MapHeight);
            for (int i = 0; i < map.TileCount; i++)
            {
                var p = map.PosOf(i);
                bool water = p.X < 4 || p.Y < 4 || p.X >= map.Width - 4 || p.Y >= map.Height - 4;
                map.Biomes[i] = water ? Biome.DeepOcean : Biome.Grassland;
                map.Elevation[i] = water ? -0.3f : 0.2f;
                map.Fertility[i] = 0.6f;
            }
            var sim = Assemble(cfg, new WorldState(cfg.Seed, map), log);
            int k = 0, n = cfg.StartingRaces.Length;
            foreach (var race in cfg.StartingRaces)
            {
                double ang = 2 * Math.PI * k++ / Math.Max(1, n);
                var at = new Vec2i(map.Width / 2 + (int)(Math.Cos(ang) * map.Width * 0.25), map.Height / 2 + (int)(Math.Sin(ang) * map.Height * 0.25));
                var civ = sim.Ops.CreateCivilization(race);
                var s = sim.Ops.FoundSettlement(civ, at);
                for (int i = 0; i < cfg.InitialPopulationPerRace; i++) sim.Ops.CreatePerson(s, race, (Sex)(i & 1), 18f + i);
            }
            sim.State.God.Mana = Math.Min(sim.State.God.MaxMana, cfg.StartingMana);
            sim.Engine.Initialize();
            return sim;
        }

        private static GodSimulation Assemble(SimConfig cfg, WorldState state, ISimLogger log)
        {
            var db = BuildDatabase();
            var events = new EventBus(log);
            var ops = new WorldOps(state, db, events);
            var commands = new CommandQueue(cfg.CommandQueueCapacity, null);
            var ctx = new SimContext { State = state, Db = db, Events = events, Config = cfg, Log = log ?? NullLogger.Instance, Ops = ops, Commands = commands };
            var engine = new SimulationEngine(ctx);
            engine.AddSystem(new StubDivineSystem());
            return new GodSimulation { Config = cfg, Db = db, State = state, Events = events, Engine = engine, Commands = commands, Ops = ops };
        }

        /// <summary>Stub persistence: only the tick and seed (enough to exercise call sites, not to resume a world).</summary>
        public static GodSimulation Load(byte[] saveData, SimConfig config, ISimLogger log = null, byte[] hmacKey = null)
        {
            if (saveData == null || saveData.Length < 16) throw new SimValidationException("stub: save too short");
            var cfg = (config ?? new SimConfig()).Clone();
            using (var r = new BinaryReader(new MemoryStream(saveData)))
            {
                cfg.Seed = r.ReadUInt64();
                long tick = r.ReadInt64();
                var sim = CreateNew(cfg, log);
                sim.State.Tick = tick;
                return sim;
            }
        }

        public byte[] Save(byte[] hmacKey = null)
        {
            var ms = new MemoryStream();
            using (var w = new BinaryWriter(ms)) { w.Write(State.Seed); w.Write(State.Tick); }
            return ms.ToArray();
        }

        public ulong ComputeHash()
        {
            ulong h = Hashing.Combine((ulong)State.Tick, State.Rng.State);
            h = Hashing.Combine(h, (ulong)State.People.Count);
            h = Hashing.Combine(h, (ulong)State.Buildings.Count);
            return Hashing.Combine(h, State.Seed);
        }

        public void Step() => Engine.Step();
        public void StepMany(int ticks) => Engine.StepMany(ticks);

        public bool TryCastPower(GodPowerType power, Vec2i target, int radius, int parameter, out string error)
        {
            var cmd = new GodCommand { Power = power, Target = target, Radius = radius, Parameter = parameter };
            return Commands.TryEnqueue(cmd, State, Db, out error);
        }

        /// <summary>Drains the command queue: charges mana, sets cooldowns, publishes GodPowerCastEvent.</summary>
        private sealed class StubDivineSystem : ISimSystem
        {
            public string Name => "StubDivine";
            public int TickInterval => 1;
            public void Initialize(SimContext ctx) { }

            public void Tick(SimContext ctx)
            {
                var god = ctx.State.God;
                int executed = 0;
                while (executed < ctx.Config.MaxCommandsPerTick && ctx.Commands.TryDequeue(out var cmd))
                {
                    executed++;
                    var def = ctx.Db.GodPower(cmd.Power);
                    bool ok = god.Mana >= def.ManaCost && ctx.Tick >= god.CooldownUntil[(int)cmd.Power];
                    if (ok)
                    {
                        god.Mana -= def.ManaCost;
                        god.CooldownUntil[(int)cmd.Power] = ctx.Tick + def.CooldownTicks;
                        god.PowersCast++;
                        god.LastCastTick = ctx.Tick;
                    }
                    ctx.Events.Publish(new GodPowerCastEvent { Power = cmd.Power, Target = cmd.Target, Radius = cmd.Radius, Parameter = cmd.Parameter, Success = ok, Message = ok ? "stub" : "stub: refused" });
                }
            }
        }
    }
}
