namespace GodSim.Core.Persistence
{
    /// <summary>
    /// One recorded, accepted god command. A value type: entries read from <see cref="ReplayLog.Entries"/>
    /// are copies, so a recorded log cannot be altered through them.
    /// </summary>
    public struct ReplayEntry
    {
        /// <summary>Tick at which the command was accepted (the tick provider's value at enqueue time).</summary>
        public long Tick;
        /// <summary>Divine power.</summary>
        public GodPowerType Power;
        /// <summary>Target tile.</summary>
        public Vec2i Target;
        /// <summary>Effect radius as issued.</summary>
        public int Radius;
        /// <summary>Power-specific argument as issued.</summary>
        public int Parameter;
        /// <summary>Origin tag (sanitized: at most 64 chars, no control characters).</summary>
        public string Source;

        /// <summary>Creates an entry from a command (does not sanitize; <see cref="ReplayLog"/> does).</summary>
        /// <exception cref="System.ArgumentNullException"><paramref name="command"/> is null.</exception>
        public static ReplayEntry FromCommand(GodCommand command, long tick)
        {
            if (command == null) throw new System.ArgumentNullException(nameof(command));
            return new ReplayEntry
            {
                Tick = tick,
                Power = command.Power,
                Target = command.Target,
                Radius = command.Radius,
                Parameter = command.Parameter,
                Source = command.Source,
            };
        }

        /// <summary>
        /// Rebuilds an untrusted <see cref="GodCommand"/> for re-submission through
        /// <see cref="CommandQueue.TryEnqueue"/> (which validates it again).
        /// </summary>
        /// <param name="sourceOverride">Source tag for the new command, e.g. "replay"; null keeps the recorded source.</param>
        public GodCommand ToCommand(string sourceOverride = null) => new GodCommand
        {
            Power = Power,
            Target = Target,
            Radius = Radius,
            Parameter = Parameter,
            IssuedTick = Tick,
            Source = sourceOverride ?? Source ?? "",
        };

        /// <inheritdoc />
        public override string ToString() => "@" + Tick + " " + Power + " " + Target + " r" + Radius + " p" + Parameter + " [" + Source + "]";
    }

    /// <summary>A state-hash checkpoint recorded alongside a replay (for replay verification).</summary>
    public struct ReplayCheckpoint
    {
        /// <summary>Tick at which the hash was taken.</summary>
        public long Tick;
        /// <summary><see cref="StateHasher.Compute"/> of the state at <see cref="Tick"/>.</summary>
        public ulong Hash;
    }
}
