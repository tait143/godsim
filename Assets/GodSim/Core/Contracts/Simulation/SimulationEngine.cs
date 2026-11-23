using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace GodSim.Core
{
    /// <summary>
    /// A unit of simulation logic. Systems are stateless with respect to persistence: anything that must
    /// survive save/load lives in WorldState (use the Memory blackboards for small extras).
    /// </summary>
    public interface ISimSystem
    {
        string Name { get; }
        /// <summary>Run every N ticks (1 = every tick). ctx.DeltaTicks equals this value when called.</summary>
        int TickInterval { get; }
        void Initialize(SimContext ctx);
        void Tick(SimContext ctx);
    }

    /// <summary>Everything a system may touch. Passed to every Initialize/Tick.</summary>
    public sealed class SimContext
    {
        public WorldState State;
        public GameDatabase Db;
        public EventBus Events;
        public SimConfig Config;
        public ISimLogger Log;
        public WorldOps Ops;
        public CommandQueue Commands;
        /// <summary>Ticks elapsed since this system last ran (= its TickInterval).</summary>
        public int DeltaTicks = 1;

        public Rng Rng => State.Rng;
        public long Tick => State.Tick;
    }

    public sealed class SystemProfile
    {
        public string Name;
        public double LastMs;
        public double AvgMs;
        public long Runs;
        public int ConsecutiveFaults;
        public int TotalFaults;
        public bool Disabled;
        public string LastError;
    }

    /// <summary>
    /// Fixed-order, deterministic tick loop. Each tick: advance clock -> calendar events -> run due systems
    /// in registration order -> flush deferred removals after each system. Faulting systems are isolated
    /// (logged and eventually disabled) unless Config.StrictMode, so one bug cannot crash a running world.
    /// </summary>
    public sealed class SimulationEngine
    {
        private readonly List<ISimSystem> _systems = new List<ISimSystem>();
        private readonly List<SystemProfile> _profiles = new List<SystemProfile>();
        private readonly Stopwatch _watch = new Stopwatch();
        private bool _initialized;

        public SimulationEngine(SimContext context)
        {
            Context = Guard.NotNull(context, nameof(context));
            Guard.NotNull(context.State, "context.State");
            Guard.NotNull(context.Db, "context.Db");
            if (context.Events == null) context.Events = new EventBus(context.Log);
            if (context.Config == null) context.Config = new SimConfig().Sanitize();
            if (context.Log == null) context.Log = NullLogger.Instance;
            if (context.Ops == null) context.Ops = new WorldOps(context.State, context.Db, context.Events);
            if (context.Commands == null) context.Commands = new CommandQueue(context.Config.CommandQueueCapacity, null);
            context.Events.StrictMode = context.Config.StrictMode;
        }

        public SimContext Context { get; }
        public IReadOnlyList<ISimSystem> Systems => _systems;
        public IReadOnlyList<SystemProfile> Profiles => _profiles;
        public long Tick => Context.State.Tick;
        public double LastStepMs { get; private set; }

        public void AddSystem(ISimSystem system)
        {
            Guard.NotNull(system, nameof(system));
            if (_initialized) throw new InvalidOperationException("Cannot add systems after Initialize().");
            if (system.TickInterval < 1) throw new ArgumentException("TickInterval must be >= 1: " + system.Name);
            _systems.Add(system);
            _profiles.Add(new SystemProfile { Name = system.Name });
        }

        public void AddSystems(IEnumerable<ISimSystem> systems) { foreach (var s in systems) AddSystem(s); }

        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;
            for (int i = 0; i < _systems.Count; i++)
            {
                Context.DeltaTicks = _systems[i].TickInterval;
                Run(i, true);
            }
            Context.Ops.FlushRemovals();
        }

        public void Step()
        {
            if (!_initialized) Initialize();
            var t0 = Stopwatch.GetTimestamp();
            var state = Context.State;
            long prevTick = state.Tick;
            state.Tick++;

            if (SimTime.SeasonOf(state.Tick) != SimTime.SeasonOf(prevTick))
                Context.Events.Publish(new SeasonChangedEvent { Season = SimTime.SeasonOf(state.Tick), Year = SimTime.Year(state.Tick) });
            if (SimTime.Year(state.Tick) != SimTime.Year(prevTick))
                Context.Events.Publish(new YearPassedEvent { Year = SimTime.Year(state.Tick) });

            for (int i = 0; i < _systems.Count; i++)
            {
                var sys = _systems[i];
                int interval = sys.TickInterval;
                // Stagger systems with the same interval across ticks using their index.
                if (interval > 1 && ((state.Tick + i) % interval) != 0) continue;
                Context.DeltaTicks = interval;
                Run(i, false);
                Context.Ops.FlushRemovals();
            }
            LastStepMs = (Stopwatch.GetTimestamp() - t0) * 1000.0 / Stopwatch.Frequency;
        }

        public void StepMany(int ticks) { for (int i = 0; i < ticks; i++) Step(); }

        private void Run(int i, bool init)
        {
            var p = _profiles[i];
            if (p.Disabled) return;
            _watch.Restart();
            try
            {
                if (init) _systems[i].Initialize(Context); else _systems[i].Tick(Context);
                p.ConsecutiveFaults = 0;
            }
            catch (Exception ex)
            {
                p.ConsecutiveFaults++;
                p.TotalFaults++;
                p.LastError = ex.GetType().Name + ": " + ex.Message;
                Context.Log.Error("System '" + p.Name + "' faulted at tick " + Context.State.Tick + ": " + ex);
                if (Context.Config.StrictMode) throw;
                if (p.ConsecutiveFaults >= Context.Config.MaxSystemFaults)
                {
                    p.Disabled = true;
                    Context.Log.Error("System '" + p.Name + "' disabled after " + p.ConsecutiveFaults + " consecutive faults.");
                }
            }
            _watch.Stop();
            double ms = _watch.Elapsed.TotalMilliseconds;
            p.LastMs = ms;
            p.Runs++;
            p.AvgMs = p.Runs == 1 ? ms : p.AvgMs * 0.95 + ms * 0.05;
        }
    }
}
