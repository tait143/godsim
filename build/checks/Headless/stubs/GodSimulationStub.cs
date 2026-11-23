// =====================================================================================
// COMPILE-CHECK STUB ONLY. Never copy into Assets/ or tools/. Mirrors the public API of
// Assets/GodSim/Core/Composition/GodSimulation.cs (owned by the lead) so the headless tool and
// the integration tests can be compiled before the real composition root exists. It builds a
// toy world (no real systems) so the headless runner can also be smoke-run end to end.
// =====================================================================================
using System;
using System.Collections.Generic;
using System.IO;

namespace GodSim.Core.Composition
{
    public sealed class GodSimulation
    {
        public SimConfig Config { get; private set; }
        public GameDatabase Db { get; private set; }
        public WorldState State { get; private set; }
        public EventBus Events { get; private set; }
        public SimulationEngine Engine { get; private set; }
        public CommandQueue Commands { get; private set; }
        public WorldOps Ops { get; private set; }
        public ISimLogger Log { get; private set; }
        public long Tick => State.Tick;

        public static GameDatabase BuildDatabase()
        {
            var db = new GameDatabase();
            foreach (var r in EnumUtil.AllRaces) db.Register(new RaceDefinition { Id = r, Name = r.ToString(), PluralName = r + "s", Adjective = r.ToString(), BannerColor = 0x3366CCFFu + (uint)r * 0x22000000u });
            foreach (var a in EnumUtil.AllAges) db.Register(new AgeDefinition { Id = a, Name = a.ToString(), ResearchThreshold = (int)a * 100f });
            foreach (var b in EnumUtil.AllBuildings)
            {
                var def = new BuildingDefinition { Type = b, Name = b.ToString(), Category = b == BuildingType.House ? BuildingCategory.Housing : BuildingCategory.Civic };
                if (b == BuildingType.House) { def.HousingMin = 3; def.HousingMax = 5; }
                if (b == BuildingType.TownCenter) { def.Width = 2; def.Height = 2; }
                db.Register(def);
            }
            foreach (var p in EnumUtil.AllGodPowers) db.Register(new GodPowerDefinition { Type = p, Name = p.ToString(), ManaCost = 10f, MinRadius = 1, MaxRadius = 6 });
            db.Register(new TechDefinition { Key = "stub.tech", Name = "Stub", Cost = 10f });
            db.Freeze();
            return db;
        }

        public static GodSimulation CreateNew(SimConfig config, ISimLogger log = null)
        {
            var cfg = (config ?? new SimConfig()).Clone().Sanitize();
            var sim = Wire(cfg, log, new WorldState(cfg.Seed, new WorldMap(cfg.MapWidth, cfg.MapHeight)));
            var map = sim.State.Map;
            for (int y = 0; y < map.Height; y++)
                for (int x = 0; x < map.Width; x++)
                {
                    int i = map.Index(x, y);
                    float e = (float)Math.Sin(x * 0.07 + cfg.Seed % 7) * 0.5f + (float)Math.Cos(y * 0.05) * 0.4f;
                    map.Elevation[i] = e;
                    map.Biomes[i] = e < -0.3f ? Biome.DeepOcean : e < -0.1f ? Biome.ShallowWater : e < 0.3f ? Biome.Grassland : e < 0.6f ? Biome.Forest : Biome.Hills;
                }
            int n = cfg.StartingRaces.Length;
            for (int k = 0; k < n; k++)
            {
                var civ = sim.Ops.CreateCivilization(cfg.StartingRaces[k]);
                var center = sim.Ops.FindFreeSite(new Vec2i(map.Width * (k + 1) / (n + 1), map.Height / 2), 2, 2, 40);
                if (!center.IsValid) continue;
                var s = sim.Ops.FoundSettlement(civ, center);
                for (int p = 0; p < cfg.InitialPopulationPerRace; p++) sim.Ops.CreatePerson(s, civ.Race, (Sex)(p & 1), 20f);
            }
            return sim;
        }

        private const uint StubMagic = 0x42545347u; // "GSTB"

        public static GodSimulation Load(byte[] saveData, SimConfig config, ISimLogger log = null, byte[] hmacKey = null)
        {
            if (saveData == null || saveData.Length != 28 || BitConverter.ToUInt32(saveData, 0) != StubMagic) throw new InvalidDataException("not a stub save");
            ulong mac = BitConverter.ToUInt64(saveData, 20);
            if (mac != Mac(saveData, hmacKey)) throw new InvalidDataException("stub save integrity check failed");
            var cfg = (config ?? new SimConfig()).Clone();
            cfg.Seed = BitConverter.ToUInt64(saveData, 4);
            long tick = BitConverter.ToInt64(saveData, 12);
            if (tick < 0 || tick > 3000L * SimTime.TicksPerYear) throw new InvalidDataException("stub save tick out of range");
            var sim = CreateNew(cfg, log);
            sim.StepMany((int)tick);
            return sim;
        }

        public byte[] Save(byte[] hmacKey = null)
        {
            var data = new byte[28];
            BitConverter.GetBytes(StubMagic).CopyTo(data, 0);
            BitConverter.GetBytes(State.Seed).CopyTo(data, 4);
            BitConverter.GetBytes(State.Tick).CopyTo(data, 12);
            BitConverter.GetBytes(Mac(data, hmacKey)).CopyTo(data, 20);
            return data;
        }

        private static ulong Mac(byte[] data, byte[] key)
        {
            ulong h = Hashing.Combine(BitConverter.ToUInt64(data, 4), (ulong)BitConverter.ToInt64(data, 12));
            if (key != null) for (int i = 0; i < key.Length; i++) h = Hashing.Combine(h, key[i]);
            return h;
        }

        public ulong ComputeHash() => Hashing.Combine(State.Seed, (ulong)State.Tick);

        public void Step()
        {
            Engine.Step();
            if (State.Tick % 48 == 0)
            {
                var items = State.Settlements.Items;
                for (int i = 0; i < items.Count; i++)
                {
                    var site = Ops.FindFreeSite(items[i].Center, 1, 1, 12);
                    if (site.IsValid) Ops.CreateBuilding(items[i], BuildingType.House, site, 0, true);
                }
            }
        }

        public void StepMany(int ticks) { for (int i = 0; i < ticks; i++) Step(); }

        public bool TryCastPower(GodPowerType power, Vec2i target, int radius, int parameter, out string error)
        {
            var cmd = new GodCommand { Power = power, Target = target, Radius = radius, Parameter = parameter };
            return Commands.TryEnqueue(cmd, State, Db, out error);
        }

        private static GodSimulation Wire(SimConfig cfg, ISimLogger log, WorldState state)
        {
            var db = BuildDatabase();
            var events = new EventBus(log);
            var ops = new WorldOps(state, db, events);
            var commands = new CommandQueue(cfg.CommandQueueCapacity, new PermissiveCommandValidator());
            var ctx = new SimContext { State = state, Db = db, Events = events, Config = cfg, Log = log ?? NullLogger.Instance, Ops = ops, Commands = commands };
            var engine = new SimulationEngine(ctx);
            // Fault injection for checking the runner's fault reporting / exit codes: GODSIM_STUB_FAULT=1.
            if (Environment.GetEnvironmentVariable("GODSIM_STUB_FAULT") == "1") engine.AddSystem(new FaultySystem());
            engine.Initialize();
            return new GodSimulation { Config = cfg, Db = db, State = state, Events = events, Engine = engine, Commands = commands, Ops = ops, Log = ctx.Log };
        }
    }

    internal sealed class FaultySystem : ISimSystem
    {
        public string Name => "Stub.Faulty";
        public int TickInterval => 10;
        public void Initialize(SimContext ctx) { }
        public void Tick(SimContext ctx) { if (ctx.Tick > 150) throw new InvalidOperationException("injected stub fault"); }
    }
}
