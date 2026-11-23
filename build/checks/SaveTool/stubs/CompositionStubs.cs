// STUB of GodSim.Core.Composition.GodSimulation (lead) for the SaveTool isolated compile check ONLY
// (never copy into Assets/ or tools/). Minimal: CreateNew builds civs only, StepMany advances the tick and
// drains the command queue. Used in both "stubs" and "persistence" check modes.
#pragma warning disable CS1591
using System;
using System.Collections.Generic;

namespace GodSim.Core.Composition
{
    using GodSim.Core.Persistence;

    public sealed class GodSimulation
    {
        private GodSimulation(SimConfig config, GameDatabase db, WorldState state)
        {
            Config = config; Db = db; State = state;
            Commands = new CommandQueue(config.CommandQueueCapacity, new PermissiveCommandValidator());
        }

        public SimConfig Config { get; }
        public GameDatabase Db { get; }
        public WorldState State { get; }
        public CommandQueue Commands { get; }
        public long Tick => State.Tick;

        public static GameDatabase BuildDatabase() { var db = new GameDatabase(); return db; }

        public static GodSimulation CreateNew(SimConfig config, ISimLogger log = null)
        {
            var cfg = (config ?? new SimConfig()).Clone().Sanitize();
            var s = new WorldState(cfg.Seed, new WorldMap(cfg.MapWidth, cfg.MapHeight));
            for (int i = 0; i < cfg.StartingRaces.Length; i++)
                s.Civilizations.Add(new Civilization { Id = s.Ids.Civilization(), Name = "Civ" + i, Race = cfg.StartingRaces[i] });
            return new GodSimulation(cfg, BuildDatabase(), s);
        }

        public static GodSimulation Load(byte[] saveData, SimConfig config, ISimLogger log = null, byte[] hmacKey = null)
        {
            var db = BuildDatabase();
            var s = SaveSerializer.Deserialize(saveData, db, new SaveOptions { HmacKey = hmacKey, RequireSignature = hmacKey != null });
            return new GodSimulation((config ?? new SimConfig()).Clone().Sanitize(), db, s);
        }

        public byte[] Save(byte[] hmacKey = null, long createdUnixSeconds = 0) =>
            SaveSerializer.Serialize(State, new SaveOptions { HmacKey = hmacKey, CreatedUnixSeconds = createdUnixSeconds });

        public ulong ComputeHash() => StateHasher.Compute(State);

        public void Step() => StepMany(1);

        public void StepMany(int ticks)
        {
            if (ticks <= 0) return;
            for (int i = 0; i < ticks; i++)
            {
                while (Commands.TryDequeue(out GodCommand c)) State.God.PowersCast++;
                State.Tick++;
            }
        }

        public bool TryCastPower(GodPowerType power, Vec2i target, int radius, int parameter, out string error)
        {
            var cmd = new GodCommand { Power = power, Target = target, Radius = radius, Parameter = parameter, Source = "local" };
            return Commands.TryEnqueue(cmd, State, Db, out error);
        }
    }
}
