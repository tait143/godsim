using System;
using System.Collections.Generic;
using System.Globalization;
using GodSim.Core;
using GodSim.Core.Persistence;

namespace GodSim.SaveTool
{
    /// <summary>`info &lt;file&gt;`: prints the untrusted header without parsing or verifying the payload.</summary>
    public static class InfoCommand
    {
        public static int Run(IReadOnlyList<string> args)
        {
            var p = new ArgParser("info", "file").Parse(args, 1);
            LoadedSave save = SaveAccess.ReadHeaderOnly(p.Positional(0));
            Print(save);
            return ExitCodes.Ok;
        }

        public static void Print(LoadedSave save)
        {
            SaveHeader h = save.Header;
            TextOut.Line("Save header: " + TextOut.Safe(save.Path, 300));
            TextOut.Field("File size", save.Bytes.Length + " bytes");
            TextOut.Field("Format version", h.FormatVersion.ToString(CultureInfo.InvariantCulture));
            TextOut.Field("Schema version", h.SchemaVersion + " (this build writes " + WorldState.SchemaVersion + ")");
            TextOut.Field("Flags", "0x" + ((ushort)h.Flags).ToString("X4", CultureInfo.InvariantCulture) + " (" + h.Flags + ")");
            TextOut.Field("Signed", h.IsSigned ? "yes (HMAC-SHA256)" : "no (SHA-256 checksum only)");
            TextOut.Field("Compressed", h.IsCompressed ? "yes (GZip)" : "no");
            TextOut.Field("Game version", "'" + TextOut.Safe(h.GameVersion, 64) + "'");
            TextOut.Field("Created", FormatCreated(h.CreatedUnixSeconds));
            TextOut.Field("Seed", h.Seed.ToString(CultureInfo.InvariantCulture) + " (0x" + h.Seed.ToString("X16", CultureInfo.InvariantCulture) + ")");
            TextOut.Field("Tick", h.Tick + (h.Tick >= 0 ? " (" + SimTime.Format(h.Tick) + ")" : " (negative: invalid)"));
            TextOut.Field("Population", h.Population.ToString(CultureInfo.InvariantCulture));
            TextOut.Field("Payload", h.PayloadLength + " bytes" + (h.IsCompressed ? " (compressed)" : ""));
            TextOut.Line("  (header values are unverified; run 'verify' to check integrity)");
        }

        public static string FormatCreated(long unix)
        {
            if (unix <= 0) return "unset";
            try { return DateTimeOffset.FromUnixTimeSeconds(unix).UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC (" + unix + ")"; }
            catch (ArgumentOutOfRangeException) { return unix + " (out of range)"; }
        }
    }
}
