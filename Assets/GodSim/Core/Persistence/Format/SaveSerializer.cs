using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using GodSim.Core.Persistence.Format;

namespace GodSim.Core.Persistence
{
    /// <summary>
    /// Versioned, integrity-protected binary save format (layout in docs/SAVE_FORMAT.md):
    /// <c>header | 32-byte tag | payload</c>, where the tag is HMAC-SHA256 (signed) or SHA-256 (unsigned) over
    /// header || payload, and the payload is optionally GZip-compressed. Loading is hardened for untrusted
    /// files: size checks come before any work, the tag is verified (constant time) before decompression,
    /// decompression is fused at <see cref="SaveOptions.MaxUncompressedBytes"/>, every count/enum/float is
    /// validated, and the result passes <see cref="StateValidator"/>. Only <see cref="SaveCorruptException"/>
    /// escapes <see cref="Deserialize"/> and <see cref="ReadHeader"/>.
    /// </summary>
    public static class SaveSerializer
    {
        /// <summary>Container layout version written by this build.</summary>
        public const int CurrentFormatVersion = SaveFormatConstants.FormatVersion;

        /// <summary>Length of the integrity tag that follows the header.</summary>
        public const int TagLength = SaveFormatLimits.TagLength;

        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private const int GZipMinLength = 18; // 10-byte header + empty deflate block + 8-byte trailer

        // ============================================================================ save
        /// <summary>
        /// Serializes the complete persistent state. Must be called between ticks.
        /// </summary>
        /// <exception cref="ArgumentNullException">state is null.</exception>
        /// <exception cref="ArgumentException">Options are inconsistent (RequireSignature without key, GameVersion over 64 bytes).</exception>
        /// <exception cref="InvalidOperationException">Deferred removals are pending (mid-tick), or the state exceeds the format limits.</exception>
        public static byte[] Serialize(WorldState state, SaveOptions options)
        {
            if (state == null) throw new ArgumentNullException(nameof(state));
            options = options ?? new SaveOptions();
            if (state.PendingPersonRemovals.Count > 0 || state.PendingBuildingRemovals.Count > 0 ||
                state.PendingSettlementRemovals.Count > 0 || state.PendingArmyRemovals.Count > 0)
                throw new InvalidOperationException("Cannot save while entity removals are pending; save between ticks (after WorldOps.FlushRemovals).");
            if (state.Map == null) throw new InvalidOperationException("Cannot save a WorldState without a map.");
            if (state.Rng == null) throw new InvalidOperationException("Cannot save a WorldState without an Rng.");

            byte[] key = options.HmacKey != null && options.HmacKey.Length > 0 ? options.HmacKey : null;
            if (options.RequireSignature && key == null) throw new ArgumentException("SaveOptions.RequireSignature is set but no HmacKey was supplied.", nameof(options));
            byte[] gameVersion;
            try { gameVersion = StrictUtf8.GetBytes(options.GameVersion ?? ""); }
            catch (ArgumentException ex) { throw new ArgumentException("SaveOptions.GameVersion is not valid Unicode.", nameof(options), ex); }
            if (gameVersion.Length > SaveFormatLimits.MaxGameVersionBytes)
                throw new ArgumentException("SaveOptions.GameVersion exceeds " + SaveFormatLimits.MaxGameVersionBytes + " UTF-8 bytes.", nameof(options));

            byte[] raw = PayloadCodec.Write(state);
            if (raw.Length > options.MaxUncompressedBytes)
                throw new InvalidOperationException("Save payload (" + raw.Length + " bytes) exceeds SaveOptions.MaxUncompressedBytes.");
            byte[] payload = options.Compress ? GZip(raw) : raw;

            var flags = SaveFormatFlags.None;
            if (options.Compress) flags |= SaveFormatFlags.Compressed;
            if (key != null) flags |= SaveFormatFlags.Signed;
            byte[] header = BuildHeader(flags, options.CreatedUnixSeconds, gameVersion, state.Seed, state.Tick, state.LivingPopulation, payload.Length);

            long total = (long)header.Length + TagLength + payload.Length;
            if (total > options.MaxCompressedBytes)
                throw new InvalidOperationException("Save file (" + total + " bytes) exceeds SaveOptions.MaxCompressedBytes.");
            byte[] tag = SaveIntegrity.ComputeTag(key, header, 0, header.Length, payload, 0, payload.Length);
            var file = new byte[total];
            Buffer.BlockCopy(header, 0, file, 0, header.Length);
            Buffer.BlockCopy(tag, 0, file, header.Length, TagLength);
            Buffer.BlockCopy(payload, 0, file, header.Length + TagLength, payload.Length);
            return file;
        }

        private static byte[] GZip(byte[] raw)
        {
            using (var ms = new MemoryStream(raw.Length / 4 + 64))
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true)) gz.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }

        private static byte[] BuildHeader(SaveFormatFlags flags, long created, byte[] gameVersion, ulong seed, long tick, int population, int payloadLength)
        {
            var h = new byte[SaveFormatLimits.FixedHeaderBytes + gameVersion.Length];
            int o = 0;
            Buffer.BlockCopy(SaveFormatConstants.Magic, 0, h, 0, 4); o += 4;
            o = PutU64(h, o, SaveFormatConstants.FormatVersion, 2);
            o = PutU64(h, o, (ushort)flags, 2);
            o = PutU64(h, o, unchecked((uint)WorldState.SchemaVersion), 4);
            o = PutU64(h, o, unchecked((ulong)created), 8);
            h[o++] = (byte)gameVersion.Length;
            Buffer.BlockCopy(gameVersion, 0, h, o, gameVersion.Length); o += gameVersion.Length;
            o = PutU64(h, o, seed, 8);
            o = PutU64(h, o, unchecked((ulong)tick), 8);
            o = PutU64(h, o, unchecked((uint)population), 4);
            PutU64(h, o, unchecked((uint)payloadLength), 4);
            return h;
        }

        private static int PutU64(byte[] b, int o, ulong v, int bytes)
        {
            for (int i = 0; i < bytes; i++) b[o + i] = (byte)(v >> (8 * i));
            return o + bytes;
        }

        private static ulong GetLE(byte[] b, int o, int bytes)
        {
            ulong v = 0;
            for (int i = 0; i < bytes; i++) v |= (ulong)b[o + i] << (8 * i);
            return v;
        }

        // ============================================================================ header
        /// <summary>
        /// Parses only the header (cheap; no hashing, no decompression) for load menus. Values are untrusted
        /// until <see cref="Deserialize"/> succeeds. Throws <see cref="SaveCorruptException"/> if malformed.
        /// </summary>
        public static SaveHeader ReadHeader(byte[] data)
        {
            try { return ParseHeader(data); }
            catch (SaveCorruptException) { throw; }
            catch (Exception ex) { throw new SaveCorruptException("Save header is unreadable (" + ex.GetType().Name + ").", ex); }
        }

        private static SaveHeader ParseHeader(byte[] data)
        {
            if (data == null) throw new SaveCorruptException("No save data.");
            if (data.Length < SaveFormatLimits.FixedHeaderBytes + TagLength) throw new SaveCorruptException("Save data is too short (" + data.Length + " bytes).");
            for (int i = 0; i < 4; i++)
                if (data[i] != SaveFormatConstants.Magic[i]) throw new SaveCorruptException("Not a GodSim save (bad magic).");
            int format = (int)GetLE(data, 4, 2);
            if (format != SaveFormatConstants.FormatVersion) throw new SaveCorruptException("Unsupported save format version " + format + ".");
            ushort flags = (ushort)GetLE(data, 6, 2);
            if ((flags & ~SaveFormatConstants.KnownFlags) != 0) throw new SaveCorruptException("Unknown save flags 0x" + flags.ToString("X4") + ".");
            int schema = unchecked((int)(uint)GetLE(data, 8, 4));
            long created = unchecked((long)GetLE(data, 12, 8));
            int gvLen = data[20];
            if (gvLen > SaveFormatLimits.MaxGameVersionBytes) throw new SaveCorruptException("Game version string too long.");
            int headerLength = SaveFormatLimits.FixedHeaderBytes + gvLen;
            if (data.Length < headerLength + TagLength) throw new SaveCorruptException("Save data is truncated inside the header.");
            string gameVersion = StrictUtf8.GetString(data, 21, gvLen);
            int o = 21 + gvLen;
            ulong seed = GetLE(data, o, 8); o += 8;
            long tick = unchecked((long)GetLE(data, o, 8)); o += 8;
            int population = unchecked((int)(uint)GetLE(data, o, 4)); o += 4;
            int payloadLength = unchecked((int)(uint)GetLE(data, o, 4));
            if (tick < 0) throw new SaveCorruptException("Negative tick in header.");
            if (population < 0) throw new SaveCorruptException("Negative population in header.");
            if (payloadLength < 0) throw new SaveCorruptException("Negative payload length in header.");
            if ((long)headerLength + TagLength + payloadLength != data.Length)
                throw new SaveCorruptException("Save length mismatch: header declares " + payloadLength + " payload bytes, file has " + ((long)data.Length - headerLength - TagLength) + ".");
            return new SaveHeader
            {
                FormatVersion = format, SchemaVersion = schema, Flags = (SaveFormatFlags)flags, CreatedUnixSeconds = created,
                GameVersion = gameVersion, Seed = seed, Tick = tick, Population = population, PayloadLength = payloadLength,
                HeaderLength = headerLength,
            };
        }

        // ============================================================================ load
        /// <summary>
        /// Loads a save produced by <see cref="Serialize"/>. Never throws anything but <see cref="SaveCorruptException"/>.
        /// <paramref name="db"/> enables content cross-checks in <see cref="StateValidator"/> (may be null to skip them).
        /// </summary>
        public static WorldState Deserialize(byte[] data, GameDatabase db, SaveOptions options)
        {
            try { return DeserializeCore(data, db, options ?? new SaveOptions()); }
            catch (SaveCorruptException) { throw; }
            catch (Exception ex)
            {
                // EndOfStream (truncation), InvalidData (bad deflate), Decoder/Argument (bad UTF-8), Overflow,
                // InvalidOperation, OutOfMemory (should be unreachable thanks to the limits), ...
                throw new SaveCorruptException("Save data is corrupt or unreadable (" + ex.GetType().Name + ": " + ex.Message + ").", ex);
            }
        }

        private static WorldState DeserializeCore(byte[] data, GameDatabase db, SaveOptions options)
        {
            if (data == null) throw new SaveCorruptException("No save data.");
            if (data.Length > options.MaxCompressedBytes)
                throw new SaveCorruptException("Save file is too large (" + data.Length + " bytes, limit " + options.MaxCompressedBytes + ").");
            var h = ParseHeader(data);
            if (!SaveMigrator.IsSupported(h.SchemaVersion))
                throw new SaveCorruptException("Unsupported save schema version " + h.SchemaVersion + " (this build reads " + SaveMigrator.MinSupportedSchemaVersion + ".." + SaveMigrator.CurrentSchemaVersion + ").");

            byte[] key = options.HmacKey != null && options.HmacKey.Length > 0 ? options.HmacKey : null;
            if (h.IsSigned && key == null) throw new SaveCorruptException("Save is signed but no HMAC key was supplied.");
            if (!h.IsSigned && options.RequireSignature) throw new SaveCorruptException("Unsigned saves are not accepted (RequireSignature).");

            int payloadOffset = h.HeaderLength + TagLength;
            byte[] expected = SaveIntegrity.ComputeTag(h.IsSigned ? key : null, data, 0, h.HeaderLength, data, payloadOffset, h.PayloadLength);
            if (!SaveIntegrity.FixedTimeEquals(expected, data, h.HeaderLength))
                throw new SaveCorruptException(h.IsSigned ? "Save signature mismatch (tampered, corrupted, or signed with another key)." : "Save checksum mismatch (corrupted or tampered).");

            if (!h.IsCompressed && h.PayloadLength > options.MaxUncompressedBytes)
                throw new SaveCorruptException("Save payload exceeds MaxUncompressedBytes.");

            WorldState state;
            using (var raw = new MemoryStream(data, payloadOffset, h.PayloadLength, false))
            {
                if (!h.IsCompressed)
                {
                    using (var reader = new BoundedReader(raw)) state = PayloadCodec.Read(reader, h.SchemaVersion);
                }
                else
                {
                    if (h.PayloadLength < GZipMinLength) throw new SaveCorruptException("Compressed payload is too short.");
                    var fuse = new LimitedReadStream(new GZipStream(raw, CompressionMode.Decompress, true), options.MaxUncompressedBytes);
                    using (var buffered = new BufferedStream(fuse, 64 * 1024))
                    using (var reader = new BoundedReader(buffered))
                    {
                        state = PayloadCodec.Read(reader, h.SchemaVersion); // reads through end of stream
                        // Verify the GZip trailer (CRC-32 + ISIZE of the plaintext) explicitly: inflaters differ in how they
                        // treat a truncated or forged trailer, and this makes the check identical on .NET and Mono.
                        int end = payloadOffset + h.PayloadLength;
                        uint crc = (uint)GetLE(data, end - 8, 4), isize = (uint)GetLE(data, end - 4, 4);
                        if (crc != fuse.Crc32 || isize != unchecked((uint)fuse.TotalRead))
                            throw new SaveCorruptException("Compressed payload trailer does not match its contents.");
                    }
                }
            }

            if (state.Seed != h.Seed || state.Tick != h.Tick) throw new SaveCorruptException("Header seed/tick do not match the payload.");
            if (state.LivingPopulation != h.Population) throw new SaveCorruptException("Header population does not match the payload.");

            SaveMigrator.Upgrade(state, db, h.SchemaVersion);
            var report = StateValidator.ValidateAndRepair(state, db);
            if (report.Fatal.Count > 0)
                throw new SaveCorruptException("Save failed validation: " + string.Join("; ", report.Fatal.ToArray(), 0, Math.Min(5, report.Fatal.Count)) + (report.Fatal.Count > 5 ? " (+" + (report.Fatal.Count - 5) + " more)" : ""));
            return state;
        }
    }
}
