using System;

namespace GodSim.Core.Persistence
{
    /// <summary>
    /// Describes one save slot found by <see cref="SaveFileStore.List"/>. Purely informational: the
    /// timestamp comes from the file system (IO layer only, never read by the simulation).
    /// </summary>
    public sealed class SaveSlotInfo
    {
        /// <summary>Validated slot name (the file name without the '.gsav' extension).</summary>
        public string Slot;

        /// <summary>Size of the primary save file in bytes.</summary>
        public long SizeBytes;

        /// <summary>Last write time of the primary save file (UTC).</summary>
        public DateTime LastWriteUtc;

        /// <inheritdoc />
        public override string ToString() => Slot + " (" + SizeBytes + " bytes)";
    }
}
