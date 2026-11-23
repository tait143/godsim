using System;
using System.Security.Cryptography;
using GodSim.Core;
using GodSim.Core.Composition;
using GodSim.Core.Persistence;

namespace GodSim.SaveTool
{
    /// <summary>A save read from disk: bytes, untrusted header, and (after <see cref="SaveAccess.Load"/>) the parsed state.</summary>
    public sealed class LoadedSave
    {
        public string Path;
        public byte[] Bytes;
        public SaveHeader Header;
        public WorldState State;
        public GameDatabase Db;
        /// <summary>True if the HMAC was checked against a supplied key (and matched).</summary>
        public bool SignatureVerified;
    }

    /// <summary>
    /// Shared read/verify pipeline. Maps loader failures onto exit codes: header or payload corruption -> 4,
    /// anything signature-related (signed file without key, wrong key, tampered signed file, unsigned file
    /// under --require-signature) -> 5.
    /// </summary>
    public static class SaveAccess
    {
        private const int TagLength = 32;
        private static GameDatabase _db;

        /// <summary>The frozen content database (built once per process).</summary>
        public static GameDatabase Database
        {
            get
            {
                if (_db == null)
                {
                    try { _db = GodSimulation.BuildDatabase(); }
                    catch (Exception e) { throw new ToolException(ExitCodes.InternalError, "Cannot build the game database: " + TextOut.Safe(e.Message, 200), e); }
                }
                return _db;
            }
        }

        /// <summary>Reads the file (bounded) and parses only the header.</summary>
        public static LoadedSave ReadHeaderOnly(string path)
        {
            var save = new LoadedSave { Path = path, Bytes = SafeFile.ReadBounded(path, SafeFile.MaxSaveBytes, "Save file") };
            try { save.Header = SaveSerializer.ReadHeader(save.Bytes); }
            catch (SaveCorruptException e) { throw new ToolException(ExitCodes.Corrupt, "Corrupt save header (" + TextOut.Safe(path, 200) + "): " + TextOut.Safe(e.Message, 300), e); }
            if (save.Header == null) throw new ToolException(ExitCodes.Corrupt, "Corrupt save header (" + TextOut.Safe(path, 200) + ")");
            return save;
        }

        /// <summary>Reads, integrity-checks and fully parses a save.</summary>
        public static LoadedSave Load(string path, byte[] key, bool requireSignature)
        {
            LoadedSave save = ReadHeaderOnly(path);
            Parse(save, key, requireSignature);
            return save;
        }

        /// <summary>
        /// Signature policy checks that need no full parse. Returns true when the HMAC was independently
        /// recomputed and matched (so later loader failures are corruption, not key problems).
        /// </summary>
        public static bool PreCheck(LoadedSave save, byte[] key, bool requireSignature)
        {
            SaveHeader h = save.Header;
            string name = TextOut.Safe(save.Path, 200);
            bool haveKey = key != null && key.Length > 0;
            if (requireSignature && !h.IsSigned)
                throw new ToolException(ExitCodes.SignatureFailure, "Signature required but " + name + " is unsigned (SHA-256 checksum only).");
            if (h.IsSigned && !haveKey)
                throw new ToolException(ExitCodes.SignatureFailure, name + " is signed (HMAC-SHA256); pass --key-file or --key-hex to verify it.");
            if (!h.IsSigned) return false;
            int tagCheck = CheckTag(save.Bytes, h, key);
            if (tagCheck == 0)
                throw new ToolException(ExitCodes.SignatureFailure, "Signature mismatch in " + name + ": wrong key, or the file was modified after signing.");
            return tagCheck > 0;
        }

        /// <summary>Maps a loader failure onto exit code 4 or 5.</summary>
        public static ToolException Classify(LoadedSave save, SaveCorruptException e, bool tagAlreadyVerified)
        {
            string name = TextOut.Safe(save.Path, 200);
            bool sig = !tagAlreadyVerified && save.Header.IsSigned && LooksLikeSignatureError(e.Message);
            return new ToolException(sig ? ExitCodes.SignatureFailure : ExitCodes.Corrupt,
                (sig ? "Signature check failed for " : "Corrupt save ") + name + ": " + TextOut.Safe(e.Message, 300), e);
        }

        /// <summary>Integrity-checks and parses <see cref="LoadedSave.Bytes"/> into <see cref="LoadedSave.State"/>.</summary>
        public static void Parse(LoadedSave save, byte[] key, bool requireSignature)
        {
            SaveHeader h = save.Header;
            string name = TextOut.Safe(save.Path, 200);
            bool haveKey = key != null && key.Length > 0;
            bool checkedTag = PreCheck(save, key, requireSignature);

            GameDatabase db = Database;
            try
            {
                var options = new SaveOptions
                {
                    HmacKey = haveKey ? key : null,
                    RequireSignature = requireSignature,
                    MaxCompressedBytes = (int)SafeFile.MaxSaveBytes,
                };
                save.State = SaveSerializer.Deserialize(save.Bytes, db, options);
                save.Db = db;
                save.SignatureVerified = h.IsSigned;
            }
            catch (SaveCorruptException e)
            {
                // If we already proved the HMAC is good, any remaining failure is corruption, not a key problem.
                throw Classify(save, e, checkedTag);
            }
            if (save.State == null) throw new ToolException(ExitCodes.Corrupt, "Loader returned no state for " + name);
        }

        /// <summary>
        /// Independently recomputes the HMAC-SHA256 tag (header || payload; tag stored at HeaderLength).
        /// Returns 1 match, 0 mismatch, -1 when the layout is unknown (then the loader's verdict is used).
        /// </summary>
        public static int CheckTag(byte[] data, SaveHeader h, byte[] key)
        {
            int headerLen = h.HeaderLength;
            if (headerLen <= 0 || h.PayloadLength < 0) return -1;
            long payloadStart = (long)headerLen + TagLength;
            if (payloadStart + h.PayloadLength != data.Length) return -1; // truncated/extended: let the loader report it
            byte[] expected;
            using (var hmac = new HMACSHA256(key))
            {
                hmac.TransformBlock(data, 0, headerLen, null, 0);
                hmac.TransformFinalBlock(data, (int)payloadStart, h.PayloadLength);
                expected = hmac.Hash;
            }
            bool ok = CryptographicOperations.FixedTimeEquals(new ReadOnlySpan<byte>(expected), new ReadOnlySpan<byte>(data, headerLen, TagLength));
            Array.Clear(expected, 0, expected.Length);
            return ok ? 1 : 0;
        }

        /// <summary>
        /// Recomputes the plain SHA-256 checksum of an unsigned file in place (fuzz --deep). Returns false if
        /// the layout cannot be determined from the (possibly mutated) header.
        /// </summary>
        public static bool ResealChecksum(byte[] data)
        {
            SaveHeader h;
            try { h = SaveSerializer.ReadHeader(data); }
            catch (SaveCorruptException) { return false; }
            if (h == null || h.IsSigned) return false;
            int headerLen = h.HeaderLength;
            if (headerLen <= 0 || h.PayloadLength < 0) return false;
            long payloadStart = (long)headerLen + TagLength;
            if (payloadStart + h.PayloadLength != data.Length) return false;
            using (var sha = SHA256.Create())
            {
                sha.TransformBlock(data, 0, headerLen, null, 0);
                sha.TransformFinalBlock(data, (int)payloadStart, h.PayloadLength);
                Buffer.BlockCopy(sha.Hash, 0, data, headerLen, TagLength);
            }
            return true;
        }

        private static bool LooksLikeSignatureError(string message)
        {
            if (message == null) return false;
            string m = message.ToLowerInvariant();
            return m.Contains("signature") || m.Contains("hmac") || m.Contains("key") || m.Contains("signed");
        }

        /// <summary>Re-serializes <paramref name="state"/> keeping the source header's version/timestamp/compression.</summary>
        public static byte[] Reserialize(WorldState state, SaveHeader source, byte[] key, bool? compress = null)
        {
            var options = new SaveOptions
            {
                HmacKey = key,
                RequireSignature = key != null,
                Compress = compress ?? source.IsCompressed,
                GameVersion = source.GameVersion ?? "",
                CreatedUnixSeconds = source.CreatedUnixSeconds,
                MaxCompressedBytes = (int)SafeFile.MaxSaveBytes,
            };
            try { return SaveSerializer.Serialize(state, options); }
            catch (SaveCorruptException e) { throw new ToolException(ExitCodes.Corrupt, "Cannot re-serialize: " + TextOut.Safe(e.Message, 300), e); }
            catch (ArgumentException e) { throw new ToolException(ExitCodes.Corrupt, "Cannot re-serialize: " + TextOut.Safe(e.Message, 300), e); }
            catch (InvalidOperationException e) { throw new ToolException(ExitCodes.Corrupt, "Cannot re-serialize: " + TextOut.Safe(e.Message, 300), e); }
        }
    }
}
