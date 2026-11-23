using System;

namespace GodSim.Core.Persistence
{
    /// <summary>
    /// The only exception <see cref="SaveSerializer.Deserialize"/> and <see cref="SaveSerializer.ReadHeader"/>
    /// let escape: the data is truncated, tampered with, signed with another key, too large, from an
    /// unsupported version, or structurally invalid. The message is safe to show in logs (it never echoes
    /// raw file bytes beyond small numbers).
    /// </summary>
    public sealed class SaveCorruptException : Exception
    {
        /// <summary>Creates the exception with a generic message.</summary>
        public SaveCorruptException() : base("Save data is corrupt.") { }

        /// <summary>Creates the exception with a specific reason.</summary>
        public SaveCorruptException(string message) : base(message) { }

        /// <summary>Creates the exception wrapping the low-level failure that revealed the corruption.</summary>
        public SaveCorruptException(string message, Exception inner) : base(message, inner) { }
    }
}
