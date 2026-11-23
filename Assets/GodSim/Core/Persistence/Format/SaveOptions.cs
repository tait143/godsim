namespace GodSim.Core.Persistence
{
    /// <summary>
    /// Options for <see cref="SaveSerializer"/>. The same instance (or equivalent values) should be used
    /// for saving and loading. Core never reads the clock: the caller supplies <see cref="CreatedUnixSeconds"/>.
    /// </summary>
    public sealed class SaveOptions
    {
        /// <summary>
        /// HMAC-SHA256 key. When non-null and non-empty, <see cref="SaveSerializer.Serialize"/> signs the file
        /// and <see cref="SaveSerializer.Deserialize"/> can verify signed files. Null/empty = unsigned
        /// (SHA-256 checksum only). A random 32-byte, per-install key is recommended.
        /// </summary>
        public byte[] HmacKey;

        /// <summary>GZip the payload (map arrays compress very well). Default true.</summary>
        public bool Compress = true;

        /// <summary>
        /// Load: reject unsigned files. Save: requires <see cref="HmacKey"/> (Serialize throws otherwise).
        /// Without this flag, a key-holding loader still accepts unsigned files (downgrade is possible).
        /// </summary>
        public bool RequireSignature;

        /// <summary>Maximum total file size accepted by Deserialize (checked before anything else) and produced by Serialize.</summary>
        public int MaxCompressedBytes = 64 * 1024 * 1024;

        /// <summary>Maximum decompressed payload size; decompression stops and the load fails past this many bytes.</summary>
        public int MaxUncompressedBytes = 512 * 1024 * 1024;

        /// <summary>Free-form game build string stored in the header (UTF-8, at most 64 bytes).</summary>
        public string GameVersion = "0.1.0";

        /// <summary>Creation timestamp stored in the header (supplied by the caller; informational only).</summary>
        public long CreatedUnixSeconds;
    }
}
