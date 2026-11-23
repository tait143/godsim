using System.Collections.Generic;

namespace GodSim.Core
{
    /// <summary>A request from the player-god. Untrusted until validated.</summary>
    public sealed class GodCommand
    {
        public GodPowerType Power;
        public Vec2i Target;
        public int Radius = 1;
        /// <summary>Power-specific argument (SpawnTribe: RaceId). Must be 0 when unused.</summary>
        public int Parameter;
        public long IssuedTick;
        public int Sequence;            // assigned by CommandQueue (replay ordering)
        public string Source = "local"; // e.g. "local", "replay", "network:<peer>"
    }

    /// <summary>Validates commands against rules and state. Implemented by the security module.</summary>
    public interface ICommandValidator
    {
        bool Validate(GodCommand command, WorldState state, GameDatabase db, out string reason);
    }

    /// <summary>Accepts everything structurally sane. Used only until a real validator is installed.</summary>
    public sealed class PermissiveCommandValidator : ICommandValidator
    {
        public bool Validate(GodCommand c, WorldState s, GameDatabase db, out string reason)
        {
            reason = null;
            if (c == null) { reason = "null command"; return false; }
            if (!System.Enum.IsDefined(typeof(GodPowerType), c.Power)) { reason = "unknown power"; return false; }
            if (!s.Map.InBounds(c.Target)) { reason = "target out of bounds"; return false; }
            return true;
        }
    }

    /// <summary>
    /// Bounded FIFO of validated god commands. Commands are validated at enqueue time (fast feedback to
    /// the UI) and the divine module re-validates at execution (state may have changed: TOCTOU safety).
    /// </summary>
    public sealed class CommandQueue
    {
        private readonly Queue<GodCommand> _queue = new Queue<GodCommand>();
        private int _sequence;

        public CommandQueue(int capacity, ICommandValidator validator)
        {
            Capacity = capacity < 1 ? 1 : capacity;
            Validator = validator ?? new PermissiveCommandValidator();
        }

        public int Capacity { get; }
        public ICommandValidator Validator { get; set; }
        public int Count => _queue.Count;
        public int RejectedCount { get; private set; }
        /// <summary>Optional observer for replay recording (called for every accepted command).</summary>
        public System.Action<GodCommand> OnAccepted;

        public bool TryEnqueue(GodCommand cmd, WorldState state, GameDatabase db, out string reason)
        {
            if (_queue.Count >= Capacity) { reason = "command queue full"; RejectedCount++; return false; }
            if (!Validator.Validate(cmd, state, db, out reason)) { RejectedCount++; return false; }
            cmd.Sequence = ++_sequence;
            cmd.IssuedTick = state.Tick;
            _queue.Enqueue(cmd);
            OnAccepted?.Invoke(cmd);
            reason = null;
            return true;
        }

        public bool TryDequeue(out GodCommand cmd)
        {
            if (_queue.Count == 0) { cmd = null; return false; }
            cmd = _queue.Dequeue();
            return true;
        }

        public void Clear() => _queue.Clear();
    }
}
