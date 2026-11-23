using System;
using GodSim.Core.Persistence;
using GodSim.Core.Security;

namespace GodSim.Core.Composition
{
    /// <summary>
    /// Facade over a running world: owns the state, the frozen content database, the event bus, the
    /// engine and the validated command queue. Unity, the headless runner and tests all go through this.
    /// </summary>
    public sealed class GodSimulation
    {
        private GodSimulation(SimConfig config, GameDatabase db, WorldState state, ISimLogger log)
        {
            Config = config;
            Db = db;
            State = state;
            Log = log ?? NullLogger.Instance;
            Events = new EventBus(Log) { StrictMode = config.StrictMode };
            Ops = new WorldOps(state, db, Events);
            Commands = new CommandQueue(config.CommandQueueCapacity, new GodCommandValidator(config));
            var ctx = new SimContext { State = state, Db = db, Events = Events, Config = config, Log = Log, Ops = Ops, Commands = Commands };
            Engine = new SimulationEngine(ctx);
            Engine.AddSystems(DefaultComposition.CreateSystems());
            Engine.Initialize();
        }

        public SimConfig Config { get; }
        public GameDatabase Db { get; }
        public WorldState State { get; }
        public EventBus Events { get; }
        public SimulationEngine Engine { get; }
        public CommandQueue Commands { get; }
        public WorldOps Ops { get; }
        public ISimLogger Log { get; }
        public long Tick => State.Tick;

        /// <summary>Builds the frozen content database (all catalogs).</summary>
        public static GameDatabase BuildDatabase() => DefaultComposition.BuildDatabase();

        /// <summary>Creates a brand-new world from a (sanitized copy of the) config.</summary>
        public static GodSimulation CreateNew(SimConfig config, ISimLogger log = null)
        {
            var cfg = SecureConfig(config);
            var db = BuildDatabase();
            var state = DefaultComposition.CreateWorld(cfg, db, log);
            return new GodSimulation(cfg, db, state, log);
        }

        /// <summary>Loads a save (integrity-checked; signed saves require the matching key). Throws SaveCorruptException on bad data.</summary>
        public static GodSimulation Load(byte[] saveData, SimConfig config, ISimLogger log = null, byte[] hmacKey = null)
        {
            Guard.NotNull(saveData, nameof(saveData));
            var cfg = SecureConfig(config);
            var db = BuildDatabase();
            var state = SaveSerializer.Deserialize(saveData, db, new SaveOptions { HmacKey = hmacKey, RequireSignature = hmacKey != null });
            cfg.Seed = state.Seed;
            cfg.MapWidth = state.Map.Width;
            cfg.MapHeight = state.Map.Height;
            return new GodSimulation(cfg, db, state, log);
        }

        /// <summary>Serializes the world (signed with HMAC-SHA256 when a key is given).</summary>
        public byte[] Save(byte[] hmacKey = null, long createdUnixSeconds = 0)
        {
            return SaveSerializer.Serialize(State, new SaveOptions { HmacKey = hmacKey, CreatedUnixSeconds = createdUnixSeconds });
        }

        /// <summary>Deterministic hash of the entire persistent state (desync / determinism checks).</summary>
        public ulong ComputeHash() => StateHasher.Compute(State);

        public void Step()
        {
            Engine.Step();
            if (State.Tick % 24 == 0)
            {
                int pop = State.LivingPopulation;
                if (pop > State.Stats.PeakPopulation) State.Stats.PeakPopulation = pop;
            }
        }

        public void StepMany(int ticks)
        {
            for (int i = 0; i < ticks; i++) Step();
        }

        /// <summary>Queues a divine power. Validated immediately (fast UI feedback) and again when it executes.</summary>
        public bool TryCastPower(GodPowerType power, Vec2i target, int radius, int parameter, out string error)
        {
            var cmd = new GodCommand { Power = power, Target = target, Radius = radius, Parameter = parameter, Source = "local" };
            return Commands.TryEnqueue(cmd, State, Db, out error);
        }

        private static SimConfig SecureConfig(SimConfig config)
        {
            var cfg = (config ?? new SimConfig()).Clone();
            cfg.StartingRaces = (RaceId[])(cfg.StartingRaces ?? EnumUtil.AllRaces).Clone();
            return InputSanitizer.ClampConfig(cfg);
        }
    }
}
