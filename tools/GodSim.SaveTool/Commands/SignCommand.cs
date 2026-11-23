using System.Collections.Generic;
using GodSim.Core;
using GodSim.Core.Persistence;

namespace GodSim.SaveTool
{
    /// <summary>
    /// `sign` / `strip-signature`: verify + fully parse the input, re-serialize it with (or without) an
    /// HMAC-SHA256 signature, re-load the output and check its state hash matches before writing it
    /// atomically. Header metadata (game version, creation time, compression) is preserved.
    /// </summary>
    public static class SignCommand
    {
        public static int RunSign(IReadOnlyList<string> args)
        {
            var p = new ArgParser("sign", "in", "out")
                .Option("--key-file").Option("--key-hex").Option("--in-key-file").Option("--in-key-hex").Flag("--force").Parse(args, 1);
            if (!p.HasValue("--key-file") && !p.HasValue("--key-hex"))
                throw new ToolException(ExitCodes.Usage, "'sign' requires --key-file <path> (or --key-hex)");
            CheckPaths(p);

            byte[] outKey = null, inKey = null;
            try
            {
                outKey = KeyMaterial.FromArgs(p);
                inKey = KeyMaterial.FromArgs(p, "--in-key-file", "--in-key-hex");
                LoadedSave src = SaveAccess.Load(p.Positional(0), inKey ?? outKey, false);
                byte[] output = SaveAccess.Reserialize(src.State, src.Header, outKey);
                ulong hash = RoundTrip(output, outKey, src.State);
                SafeFile.WriteAtomic(p.Positional(1), output, p.Has("--force"), "Output file");
                TextOut.Line("Signed " + TextOut.Safe(p.Positional(0), 300) + " -> " + TextOut.Safe(p.Positional(1), 300));
                TextOut.Line("  input:  " + (src.Header.IsSigned ? "signed (verified)" : "unsigned") + ", " + src.Bytes.Length + " bytes");
                TextOut.Line("  output: signed (HMAC-SHA256), " + output.Length + " bytes, state hash " + TextOut.Hash(hash) + " (unchanged)");
                return ExitCodes.Ok;
            }
            finally
            {
                KeyMaterial.Wipe(outKey);
                KeyMaterial.Wipe(inKey);
            }
        }

        public static int RunStrip(IReadOnlyList<string> args)
        {
            var p = new ArgParser("strip-signature", "in", "out").Option("--key-file").Option("--key-hex").Flag("--force").Parse(args, 1);
            CheckPaths(p);
            byte[] key = KeyMaterial.FromArgs(p);
            try
            {
                // A signed input must be verified first: stripping must never launder a tampered file.
                LoadedSave src = SaveAccess.Load(p.Positional(0), key, false);
                byte[] output = SaveAccess.Reserialize(src.State, src.Header, null);
                ulong hash = RoundTrip(output, null, src.State);
                SafeFile.WriteAtomic(p.Positional(1), output, p.Has("--force"), "Output file");
                TextOut.Line("Stripped signature " + TextOut.Safe(p.Positional(0), 300) + " -> " + TextOut.Safe(p.Positional(1), 300));
                TextOut.Line("  input:  " + (src.Header.IsSigned ? "signed (verified)" : "already unsigned") + ", " + src.Bytes.Length + " bytes");
                TextOut.Line("  output: unsigned (SHA-256 checksum), " + output.Length + " bytes, state hash " + TextOut.Hash(hash) + " (unchanged)");
                return ExitCodes.Ok;
            }
            finally
            {
                KeyMaterial.Wipe(key);
            }
        }

        private static void CheckPaths(ArgParser p)
        {
            if (SafeFile.SamePath(p.Positional(0), p.Positional(1)))
                throw new ToolException(ExitCodes.Usage, "input and output must be different files");
        }

        /// <summary>Re-loads the bytes we are about to write and proves the state survived unchanged.</summary>
        private static ulong RoundTrip(byte[] output, byte[] key, WorldState original)
        {
            ulong before = StateHasher.Compute(original);
            WorldState back;
            try
            {
                back = SaveSerializer.Deserialize(output, SaveAccess.Database, new SaveOptions
                {
                    HmacKey = key,
                    RequireSignature = key != null,
                    MaxCompressedBytes = (int)SafeFile.MaxSaveBytes,
                });
            }
            catch (SaveCorruptException e)
            {
                throw new ToolException(ExitCodes.InternalError, "Re-serialized output does not load back (nothing written): " + TextOut.Safe(e.Message, 300), e);
            }
            ulong after = StateHasher.Compute(back);
            if (after != before)
                throw new ToolException(ExitCodes.InternalError, "Re-serialized state hash differs (" + TextOut.Hash(before) + " vs " + TextOut.Hash(after) + "); nothing written");
            return after;
        }
    }
}
