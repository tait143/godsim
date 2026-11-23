using System;
using System.IO;

namespace GodSim.SaveTool
{
    /// <summary>
    /// Bounded, explicit-path file access. Paths are used exactly as given (no globbing, no expansion);
    /// the size is checked before any byte is read, and reads never go past the size limit even if the
    /// file grows concurrently. Writes go to a temporary sibling and are moved into place atomically.
    /// </summary>
    public static class SafeFile
    {
        /// <summary>Largest save file the tool reads (matches SaveOptions.MaxCompressedBytes' default).</summary>
        public const long MaxSaveBytes = 64L * 1024 * 1024;
        /// <summary>Largest replay file the tool reads.</summary>
        public const long MaxReplayBytes = 32L * 1024 * 1024;
        /// <summary>Largest key file the tool reads.</summary>
        public const long MaxKeyFileBytes = 4096;

        /// <summary>Reads a whole regular file of at most <paramref name="maxBytes"/> bytes.</summary>
        public static byte[] ReadBounded(string path, long maxBytes, string what)
        {
            string full = Resolve(path, what);
            try
            {
                var info = new FileInfo(full);
                if (!info.Exists)
                {
                    if (Directory.Exists(full)) throw new ToolException(ExitCodes.IoError, what + " is a directory: " + TextOut.Safe(path, 300));
                    throw new ToolException(ExitCodes.IoError, what + " not found: " + TextOut.Safe(path, 300));
                }
                if (info.Length > maxBytes)
                    throw new ToolException(ExitCodes.IoError, what + " is too large (" + info.Length + " bytes; limit " + maxBytes + "): " + TextOut.Safe(path, 300));

                using (var fs = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan))
                {
                    long len = fs.Length;
                    if (len > maxBytes)
                        throw new ToolException(ExitCodes.IoError, what + " is too large (" + len + " bytes; limit " + maxBytes + ")");
                    var data = new byte[len];
                    int read = 0;
                    while (read < data.Length)
                    {
                        int n = fs.Read(data, read, data.Length - read);
                        if (n <= 0) break;
                        read += n;
                    }
                    if (read != data.Length)
                        throw new ToolException(ExitCodes.IoError, what + " changed while being read: " + TextOut.Safe(path, 300));
                    // The file grew while open: refuse rather than parse a torn snapshot.
                    if (fs.ReadByte() >= 0)
                        throw new ToolException(ExitCodes.IoError, what + " changed while being read: " + TextOut.Safe(path, 300));
                    return data;
                }
            }
            catch (ToolException) { throw; }
            catch (UnauthorizedAccessException) { throw new ToolException(ExitCodes.IoError, "Access denied reading " + what.ToLowerInvariant() + ": " + TextOut.Safe(path, 300)); }
            catch (IOException e) { throw new ToolException(ExitCodes.IoError, "Cannot read " + what.ToLowerInvariant() + " '" + TextOut.Safe(path, 300) + "': " + TextOut.Safe(e.Message, 200)); }
        }

        /// <summary>
        /// Writes <paramref name="data"/> to <paramref name="path"/> via a temp file + atomic move.
        /// Refuses to overwrite an existing file unless <paramref name="overwrite"/> is true.
        /// </summary>
        public static void WriteAtomic(string path, byte[] data, bool overwrite, string what)
        {
            string full = Resolve(path, what);
            string tmp = full + ".savetool.tmp";
            try
            {
                if (Directory.Exists(full)) throw new ToolException(ExitCodes.IoError, what + " is a directory: " + TextOut.Safe(path, 300));
                if (File.Exists(full) && !overwrite)
                    throw new ToolException(ExitCodes.IoError, what + " already exists (pass --force to overwrite): " + TextOut.Safe(path, 300));
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    throw new ToolException(ExitCodes.IoError, "Output directory does not exist: " + TextOut.Safe(dir, 300));

                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(data, 0, data.Length);
                    fs.Flush(true);
                }
                File.Move(tmp, full, overwrite);
            }
            catch (ToolException) { TryDelete(tmp); throw; }
            catch (UnauthorizedAccessException) { TryDelete(tmp); throw new ToolException(ExitCodes.IoError, "Access denied writing " + what.ToLowerInvariant() + ": " + TextOut.Safe(path, 300)); }
            catch (IOException e) { TryDelete(tmp); throw new ToolException(ExitCodes.IoError, "Cannot write " + what.ToLowerInvariant() + " '" + TextOut.Safe(path, 300) + "': " + TextOut.Safe(e.Message, 200)); }
        }

        /// <summary>True if both paths resolve to the same full path (case-sensitive compare; conservative).</summary>
        public static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
            catch (Exception) { return false; }
        }

        private static string Resolve(string path, string what)
        {
            if (string.IsNullOrEmpty(path)) throw new ToolException(ExitCodes.Usage, what + " path is empty");
            if (path.IndexOf('\0') >= 0) throw new ToolException(ExitCodes.Usage, what + " path contains a NUL character");
            if (path.IndexOf('*') >= 0 || path.IndexOf('?') >= 0)
                throw new ToolException(ExitCodes.Usage, what + " path contains wildcard characters; pass an explicit file path");
            try { return Path.GetFullPath(path); }
            catch (Exception) { throw new ToolException(ExitCodes.Usage, what + " path is invalid: " + TextOut.Safe(path, 300)); }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch (Exception) { }
        }
    }
}
