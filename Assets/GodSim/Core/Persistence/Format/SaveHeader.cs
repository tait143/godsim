using System;

namespace GodSim.Core.Persistence
{
    /// <summary>Header flag bits (u16 at byte offset 6).</summary>
    [Flags]
    public enum SaveFormatFlags : ushort
    {
        /// <summary>Uncompressed, unsigned (SHA-256 checksum).</summary>
        None = 0,
        /// <summary>Payload is a GZip stream.</summary>
        Compressed = 1,
        /// <summary>Tag is HMAC-SHA256 (keyed) instead of SHA-256.</summary>
        Signed = 2,
    }

    /// <summary>
    /// The fixed, uncompressed preamble of a save file. Returned by <see cref="SaveSerializer.ReadHeader"/>
    /// for load menus without parsing or verifying the payload: treat every value as untrusted until
    /// <see cref="SaveSerializer.Deserialize"/> succeeds.
    /// </summary>
    public sealed class SaveHeader
    {
        /// <summary>Container layout version (currently 1).</summary>
        public int FormatVersion { get; internal set; }
        /// <summary>WorldState schema version the payload was written with.</summary>
        public int SchemaVersion { get; internal set; }
        /// <summary>Raw flag bits.</summary>
        public SaveFormatFlags Flags { get; internal set; }
        /// <summary>Caller-supplied creation time (Unix seconds).</summary>
        public long CreatedUnixSeconds { get; internal set; }
        /// <summary>Game build string (at most 64 UTF-8 bytes).</summary>
        public string GameVersion { get; internal set; }
        /// <summary>World seed.</summary>
        public ulong Seed { get; internal set; }
        /// <summary>Simulation tick at save time.</summary>
        public long Tick { get; internal set; }
        /// <summary>Living population at save time.</summary>
        public int Population { get; internal set; }
        /// <summary>Stored payload length in bytes (compressed length when compressed).</summary>
        public int PayloadLength { get; internal set; }
        /// <summary>Header length in bytes (offset of the 32-byte tag).</summary>
        public int HeaderLength { get; internal set; }

        /// <summary>True if the tag is an HMAC-SHA256 signature.</summary>
        public bool IsSigned => (Flags & SaveFormatFlags.Signed) != 0;
        /// <summary>True if the payload is GZip-compressed.</summary>
        public bool IsCompressed => (Flags & SaveFormatFlags.Compressed) != 0;
        /// <summary>Simulated year at save time (derived from <see cref="Tick"/>).</summary>
        public int Year => SimTime.Year(Tick);
    }
}
