using System;
using System.IO;
using System.Security;

namespace GodSim.Core.Persistence
{
    /// <summary>What a directory entry is, as far as the storage layer cares.</summary>
    internal enum StorageEntryKind : byte
    {
        /// <summary>Nothing exists at the path.</summary>
        None = 0,
        /// <summary>A plain file (not a link).</summary>
        File = 1,
        /// <summary>A directory (not a link).</summary>
        Directory = 2,
        /// <summary>A symbolic link, junction or other reparse point (never followed).</summary>
        Link = 3,
    }

    /// <summary>
    /// Shared low-level helpers for the storage layer: link detection, safe directory checks and
    /// exception-to-message mapping that never leaks absolute paths.
    /// </summary>
    internal static class StorageIo
    {
        /// <summary>True on file systems that are conventionally case-insensitive (Windows).</summary>
        internal static readonly bool CaseInsensitivePaths = Path.DirectorySeparatorChar == '\\';

        /// <summary>Comparison to use for path prefixes and file-name suffixes.</summary>
        internal static StringComparison PathComparison => CaseInsensitivePaths ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        /// <summary>
        /// Classifies a path without following links (the reparse-point attribute is checked first).
        /// Access errors propagate to the caller, which maps them to a failure result.
        /// </summary>
        internal static StorageEntryKind GetKind(string path)
        {
            FileAttributes attrs;
            try
            {
                attrs = File.GetAttributes(path);
            }
            catch (FileNotFoundException) { return StorageEntryKind.None; }
            catch (DirectoryNotFoundException) { return StorageEntryKind.None; }
            if ((int)attrs == -1) return StorageEntryKind.None;
            if ((attrs & FileAttributes.ReparsePoint) != 0) return StorageEntryKind.Link;
            if ((attrs & FileAttributes.Directory) != 0) return StorageEntryKind.Directory;
            return StorageEntryKind.File;
        }

        /// <summary>
        /// Ensures <paramref name="fullPath"/> is an existing real directory (not a symlink/junction),
        /// optionally creating it. Never throws; returns false with a path-free message.
        /// </summary>
        internal static bool EnsureSafeDirectory(string fullPath, bool create, string what, out string error)
        {
            error = null;
            try
            {
                var kind = GetKind(fullPath);
                if (kind == StorageEntryKind.None)
                {
                    if (!create) { error = what + " does not exist."; return false; }
                    Directory.CreateDirectory(fullPath);
                    kind = GetKind(fullPath);
                }
                switch (kind)
                {
                    case StorageEntryKind.Directory: return true;
                    case StorageEntryKind.Link: error = what + " is a symbolic link or reparse point; refusing to use it."; return false;
                    case StorageEntryKind.File: error = what + " is a file, not a directory."; return false;
                    default: error = what + " could not be created."; return false;
                }
            }
            catch (Exception ex)
            {
                error = Describe("access the " + what.ToLowerInvariant(), ex);
                return false;
            }
        }

        /// <summary>
        /// Removes a file or link at <paramref name="path"/> if present (links are removed, never followed).
        /// Throws <see cref="IOException"/> when the entry is a directory.
        /// </summary>
        internal static void DeleteFileOrLink(string path)
        {
            var kind = GetKind(path);
            if (kind == StorageEntryKind.None) return;
            if (kind == StorageEntryKind.Directory) throw new IOException("A directory occupies a reserved storage file name.");
            File.Delete(path);
        }

        /// <summary>Best-effort removal used in cleanup paths; swallows every error.</summary>
        internal static void TryDeleteQuietly(string path)
        {
            if (path == null) return;
            try
            {
                if (GetKind(path) == StorageEntryKind.File || GetKind(path) == StorageEntryKind.Link) File.Delete(path);
            }
            catch (Exception)
            {
                // Cleanup only: the caller already reports the primary failure.
            }
        }

        /// <summary>Removes trailing directory separators (keeps a bare file-system root intact).</summary>
        internal static string TrimTrailingSeparators(string fullPath)
        {
            string root = Path.GetPathRoot(fullPath) ?? "";
            int len = fullPath.Length;
            while (len > root.Length && (fullPath[len - 1] == Path.DirectorySeparatorChar || fullPath[len - 1] == Path.AltDirectorySeparatorChar)) len--;
            return len == fullPath.Length ? fullPath : fullPath.Substring(0, len);
        }

        /// <summary>Ensures the path ends with exactly one directory separator (for prefix containment checks).</summary>
        internal static string WithTrailingSeparator(string fullPath)
        {
            if (fullPath.Length > 0)
            {
                char last = fullPath[fullPath.Length - 1];
                if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar) return fullPath;
            }
            return fullPath + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// Maps an exception to a short, user-presentable message. Never includes <see cref="Exception.Message"/>
        /// (which for IO exceptions usually contains absolute paths).
        /// </summary>
        internal static string Describe(string action, Exception ex)
        {
            string reason;
            if (ex is UnauthorizedAccessException || ex is SecurityException) reason = "access denied";
            else if (ex is PathTooLongException) reason = "path too long";
            else if (ex is DirectoryNotFoundException) reason = "directory not found";
            else if (ex is FileNotFoundException) reason = "file not found";
            else if (ex is EndOfStreamException) reason = "unexpected end of file";
            else if (ex is IOException) reason = "I/O error 0x" + ex.HResult.ToString("X8");
            else if (ex is NotSupportedException) reason = "operation not supported";
            else if (ex is OutOfMemoryException) reason = "out of memory";
            else if (ex is ArgumentException) reason = "invalid path";
            else reason = ex.GetType().Name;
            return "Could not " + action + " (" + reason + ").";
        }

        /// <summary>True for exceptions that Path.GetFullPath raises for malformed input.</summary>
        internal static bool IsPathFormatException(Exception ex) =>
            ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException || ex is SecurityException;
    }
}
