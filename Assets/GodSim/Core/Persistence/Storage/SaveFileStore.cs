using System;
using System.Collections.Generic;
using System.IO;

namespace GodSim.Core.Persistence
{
    /// <summary>
    /// Safe, atomic save-file storage over opaque byte arrays (the binary format is another layer's job).
    /// <para>
    /// Every slot is the single file <c>&lt;Root&gt;/&lt;slot&gt;.gsav</c>. Slot names are restricted to
    /// <c>^[A-Za-z0-9_-]{1,48}$</c> minus Windows device names, and the resolved path is re-checked for
    /// containment in <see cref="Root"/>. Symbolic links / reparse points are refused for the root and for
    /// slot files; planted links at temp/backup names are removed (never followed).
    /// </para>
    /// <para>
    /// Writes are atomic: data goes to <c>&lt;slot&gt;.gsav.tmp</c> (created exclusively, flushed to disk),
    /// existing backups rotate (<c>.bak1</c> newest .. <c>.bakN</c> oldest, the oldest is dropped), then the
    /// temp file replaces the slot (<see cref="File.Replace(string,string,string,bool)"/> moving the old
    /// slot to <c>.bak1</c>, or <see cref="File.Move(string,string)"/> for a new slot). A crash at any point
    /// leaves either the old or the new complete file in place. (Runtimes whose File.Replace is two renames,
    /// e.g. Mono on Unix, have a tiny window where only <c>.bak1</c> holds the previous save; use
    /// <see cref="TryReadBackup"/> to recover. The directory entry itself is not fsync'd: netstandard2.1 has no API.)
    /// </para>
    /// <para>
    /// The <c>Try*</c> methods, <see cref="Delete"/>, <see cref="Exists"/> and <see cref="List"/> never throw;
    /// error messages never contain absolute paths. Operations are serialized by a process-wide lock.
    /// </para>
    /// </summary>
    public sealed class SaveFileStore
    {
        /// <summary>Extension of primary save files.</summary>
        public const string Extension = ".gsav";
        /// <summary>Suffix appended to the slot file name while writing.</summary>
        public const string TempSuffix = ".tmp";
        /// <summary>Suffix prefix of backups ('.bak1' is the newest).</summary>
        public const string BackupSuffix = ".bak";
        /// <summary>Maximum slot-name length.</summary>
        public const int MaxSlotNameLength = 48;
        /// <summary>Largest supported number of rotated backups.</summary>
        public const int MaxBackups = 16;
        /// <summary>Default per-file size limit (128 MiB).</summary>
        public const long DefaultMaxFileBytes = 128L * 1024 * 1024;
        /// <summary>Hard upper bound for <see cref="MaxFileBytes"/> (largest byte[] the runtime allows).</summary>
        public const long AbsoluteMaxFileBytes = 0x7FFFFFC7L;
        /// <summary><see cref="List"/> stops after this many slots (DoS bound for huge directories).</summary>
        public const int MaxListedSlots = 4096;

        private const int BufferSize = 81920;
        private const string InvalidSlotMessage = "Invalid save slot name (allowed: 1-48 characters A-Z, a-z, 0-9, '_' or '-'; device names such as CON or NUL are reserved).";

        /// <summary>Process-wide: two stores over the same directory must not interleave temp/backup handling.</summary>
        private static readonly object IoLock = new object();

        private readonly string _rootWithSeparator;

        /// <summary>
        /// Creates a store rooted at <paramref name="rootDirectory"/> (created if missing).
        /// </summary>
        /// <param name="rootDirectory">Directory that holds the save files. Must not be a symlink/junction.</param>
        /// <param name="maxFileBytes">Largest file accepted on write and read (1 .. <see cref="AbsoluteMaxFileBytes"/>).</param>
        /// <param name="backups">Number of rotated backups kept per slot (0 .. <see cref="MaxBackups"/>).</param>
        /// <exception cref="ArgumentException">The root is empty or not a valid path.</exception>
        /// <exception cref="ArgumentOutOfRangeException">A limit is out of range.</exception>
        /// <exception cref="IOException">The root is a link, a file, or cannot be created.</exception>
        public SaveFileStore(string rootDirectory, long maxFileBytes = DefaultMaxFileBytes, int backups = 3)
        {
            if (string.IsNullOrWhiteSpace(rootDirectory)) throw new ArgumentException("A save root directory is required.", nameof(rootDirectory));
            if (maxFileBytes < 1 || maxFileBytes > AbsoluteMaxFileBytes) throw new ArgumentOutOfRangeException(nameof(maxFileBytes), "maxFileBytes must be within [1, " + AbsoluteMaxFileBytes + "].");
            if (backups < 0 || backups > MaxBackups) throw new ArgumentOutOfRangeException(nameof(backups), "backups must be within [0, " + MaxBackups + "].");

            string full;
            try { full = Path.GetFullPath(rootDirectory); }
            catch (Exception ex) when (StorageIo.IsPathFormatException(ex)) { throw new ArgumentException("The save root directory is not a valid path.", nameof(rootDirectory)); }

            Root = StorageIo.TrimTrailingSeparators(full);
            _rootWithSeparator = StorageIo.WithTrailingSeparator(Root);
            MaxFileBytes = maxFileBytes;
            BackupCount = backups;

            lock (IoLock)
            {
                if (!StorageIo.EnsureSafeDirectory(Root, true, "Save directory", out string error)) throw new IOException(error);
            }
        }

        /// <summary>Normalized absolute root directory (no trailing separator unless it is a volume root).</summary>
        public string Root { get; }

        /// <summary>Largest file size accepted on write and read.</summary>
        public long MaxFileBytes { get; }

        /// <summary>Number of rotated backups kept per slot.</summary>
        public int BackupCount { get; }

        // ================================================================== names

        /// <summary>
        /// True if <paramref name="slot"/> matches <c>^[A-Za-z0-9_-]{1,48}$</c> and is not a reserved
        /// Windows device name (CON, PRN, AUX, NUL, COM0-9, LPT0-9; case-insensitive).
        /// </summary>
        public static bool IsValidSlotName(string slot)
        {
            if (slot == null || slot.Length < 1 || slot.Length > MaxSlotNameLength) return false;
            for (int i = 0; i < slot.Length; i++)
            {
                char c = slot[i];
                bool ok = (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_' || c == '-';
                if (!ok) return false;
            }
            return !IsReservedDeviceName(slot);
        }

        private static bool IsReservedDeviceName(string s)
        {
            if (s.Length == 3)
                return Eq(s, "CON") || Eq(s, "PRN") || Eq(s, "AUX") || Eq(s, "NUL");
            if (s.Length == 4 && s[3] >= '0' && s[3] <= '9')
            {
                string head = s.Substring(0, 3);
                return Eq(head, "COM") || Eq(head, "LPT");
            }
            return false;
        }

        private static bool Eq(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

        // ================================================================== write

        /// <summary>
        /// Atomically writes <paramref name="data"/> to the slot, rotating backups. Never throws.
        /// </summary>
        /// <returns>True on success; otherwise false with a path-free <paramref name="error"/>.</returns>
        public bool TryWrite(string slot, byte[] data, out string error)
        {
            error = null;
            if (!IsValidSlotName(slot)) { error = InvalidSlotMessage; return false; }
            if (data == null) { error = "No save data was provided."; return false; }
            if (data.Length == 0) { error = "Save data is empty."; return false; }
            if (data.LongLength > MaxFileBytes) { error = "Save data is too large (" + data.LongLength + " bytes; limit " + MaxFileBytes + ")."; return false; }

            lock (IoLock)
            {
                string tmp = null;
                bool tmpOwned = false;
                try
                {
                    if (!StorageIo.EnsureSafeDirectory(Root, true, "Save directory", out error)) return false;
                    if (!TryResolve(slot, "", out string target) || !TryResolve(slot, TempSuffix, out tmp)) { error = InvalidSlotMessage; return false; }

                    var targetKind = StorageIo.GetKind(target);
                    if (targetKind == StorageEntryKind.Link) { error = "Save slot '" + slot + "' is a symbolic link or reparse point; refusing to overwrite it."; return false; }
                    if (targetKind == StorageEntryKind.Directory) { error = "Save slot '" + slot + "' is occupied by a directory."; return false; }

                    // A stale or planted temp entry (file, link or FIFO) is unlinked, never opened.
                    StorageIo.DeleteFileOrLink(tmp);
                    using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferSize, FileOptions.None))
                    {
                        tmpOwned = true;
                        fs.Write(data, 0, data.Length);
                        fs.Flush(true);
                    }

                    if (targetKind == StorageEntryKind.File)
                    {
                        RotateBackups(slot);
                        string newestBackup = null;
                        if (BackupCount > 0 && !TryResolve(slot, BackupSuffix + "1", out newestBackup)) { error = InvalidSlotMessage; return false; }
                        File.Replace(tmp, target, newestBackup, true);
                    }
                    else
                    {
                        File.Move(tmp, target);
                    }
                    tmpOwned = false;
                    return true;
                }
                catch (Exception ex)
                {
                    error = StorageIo.Describe("write save slot '" + slot + "'", ex);
                    return false;
                }
                finally
                {
                    if (tmpOwned) StorageIo.TryDeleteQuietly(tmp);
                }
            }
        }

        /// <summary>Shifts .bak(i) to .bak(i+1) from the oldest down, dropping .bakN. Planted links are deleted, not carried.</summary>
        private void RotateBackups(string slot)
        {
            if (BackupCount <= 0) return;
            if (!TryResolve(slot, BackupSuffix + BackupCount, out string oldest)) throw new IOException("Backup path escaped the save directory.");
            StorageIo.DeleteFileOrLink(oldest);
            for (int i = BackupCount - 1; i >= 1; i--)
            {
                if (!TryResolve(slot, BackupSuffix + i, out string from) || !TryResolve(slot, BackupSuffix + (i + 1), out string to))
                    throw new IOException("Backup path escaped the save directory.");
                var kind = StorageIo.GetKind(from);
                if (kind == StorageEntryKind.None) continue;
                if (kind == StorageEntryKind.Link) { File.Delete(from); continue; }
                if (kind == StorageEntryKind.Directory) throw new IOException("A directory occupies a backup file name.");
                StorageIo.DeleteFileOrLink(to); // normally already free; guards against races/manual copies
                File.Move(from, to);
            }
        }

        // ================================================================== read

        /// <summary>Reads the whole slot file (size-checked before allocation). Never throws.</summary>
        /// <returns>True on success; otherwise false with <paramref name="data"/> = null and a path-free <paramref name="error"/>.</returns>
        public bool TryRead(string slot, out byte[] data, out string error) => TryReadFile(slot, "", "Save slot", out data, out error);

        /// <summary>
        /// Reads backup number <paramref name="index"/> (1 = newest) of a slot, e.g. to recover after the
        /// primary failed integrity checks. Never throws.
        /// </summary>
        public bool TryReadBackup(string slot, int index, out byte[] data, out string error)
        {
            if (index < 1 || index > MaxBackups) { data = null; error = "Backup index must be within [1, " + MaxBackups + "]."; return false; }
            return TryReadFile(slot, BackupSuffix + index, "Backup " + index + " of save slot", out data, out error);
        }

        private bool TryReadFile(string slot, string suffix, string what, out byte[] data, out string error)
        {
            data = null;
            error = null;
            if (!IsValidSlotName(slot)) { error = InvalidSlotMessage; return false; }
            lock (IoLock)
            {
                try
                {
                    if (!StorageIo.EnsureSafeDirectory(Root, false, "Save directory", out error)) return false;
                    if (!TryResolve(slot, suffix, out string path)) { error = InvalidSlotMessage; return false; }

                    string label = what + " '" + slot + "'";
                    var kind = StorageIo.GetKind(path);
                    if (kind == StorageEntryKind.None) { error = label + " was not found."; return false; }
                    if (kind == StorageEntryKind.Link) { error = label + " is a symbolic link or reparse point; refusing to read it."; return false; }
                    if (kind != StorageEntryKind.File) { error = label + " is not a regular file."; return false; }

                    // Size check BEFORE opening: also rejects FIFOs/devices (reported size 0) that could block or never end.
                    long length = new FileInfo(path).Length;
                    if (length <= 0) { error = label + " is empty."; return false; }
                    if (length > MaxFileBytes) { error = label + " is too large (" + length + " bytes; limit " + MaxFileBytes + ")."; return false; }

                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, FileOptions.SequentialScan))
                    {
                        long actual = fs.Length;
                        if (actual <= 0 || actual > MaxFileBytes) { error = label + " changed size while being read."; return false; }
                        var buffer = new byte[actual];
                        int read = 0;
                        while (read < buffer.Length)
                        {
                            int n = fs.Read(buffer, read, buffer.Length - read);
                            if (n <= 0) break;
                            read += n;
                        }
                        if (read != buffer.Length || fs.ReadByte() != -1) { error = label + " changed while being read."; return false; }
                        data = buffer;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    data = null;
                    error = StorageIo.Describe("read save slot '" + slot + "'", ex);
                    return false;
                }
            }
        }

        // ================================================================== maintenance

        /// <summary>
        /// Deletes the slot together with its temp file and every backup. Links are removed, never followed.
        /// Never throws.
        /// </summary>
        /// <returns>True if the primary slot entry existed and was removed.</returns>
        public bool Delete(string slot)
        {
            if (!IsValidSlotName(slot)) return false;
            lock (IoLock)
            {
                try
                {
                    if (!StorageIo.EnsureSafeDirectory(Root, false, "Save directory", out _)) return false;
                    if (!TryResolve(slot, "", out string target)) return false;
                    var kind = StorageIo.GetKind(target);
                    if (kind == StorageEntryKind.Directory) return false;
                    bool existed = kind != StorageEntryKind.None;
                    if (existed) File.Delete(target);
                    if (TryResolve(slot, TempSuffix, out string tmp)) StorageIo.TryDeleteQuietly(tmp);
                    for (int i = 1; i <= MaxBackups; i++)
                        if (TryResolve(slot, BackupSuffix + i, out string bak)) StorageIo.TryDeleteQuietly(bak);
                    return existed;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>True if the slot name is valid and a regular (non-link) slot file exists. Never throws.</summary>
        public bool Exists(string slot)
        {
            if (!IsValidSlotName(slot)) return false;
            lock (IoLock)
            {
                try
                {
                    if (!TryResolve(slot, "", out string target)) return false;
                    if (StorageIo.GetKind(Root) != StorageEntryKind.Directory) return false;
                    return StorageIo.GetKind(target) == StorageEntryKind.File;
                }
                catch (Exception)
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Lists real save slots (valid names, regular non-empty '.gsav' files), sorted ordinally by slot name.
        /// Temp, backup, link and foreign files are ignored. Bounded by <see cref="MaxListedSlots"/>. Never throws.
        /// </summary>
        public IReadOnlyList<SaveSlotInfo> List()
        {
            var result = new List<SaveSlotInfo>();
            lock (IoLock)
            {
                try
                {
                    if (StorageIo.GetKind(Root) != StorageEntryKind.Directory) return result;
                    var comparison = StorageIo.PathComparison;
                    foreach (var fi in new DirectoryInfo(Root).EnumerateFiles())
                    {
                        try
                        {
                            string name = fi.Name;
                            if (name.Length <= Extension.Length || !name.EndsWith(Extension, comparison)) continue;
                            string slot = name.Substring(0, name.Length - Extension.Length);
                            if (!IsValidSlotName(slot)) continue;
                            if ((fi.Attributes & (FileAttributes.ReparsePoint | FileAttributes.Directory)) != 0) continue;
                            long size = fi.Length;
                            if (size <= 0) continue;
                            result.Add(new SaveSlotInfo { Slot = slot, SizeBytes = size, LastWriteUtc = fi.LastWriteTimeUtc });
                            if (result.Count >= MaxListedSlots) break;
                        }
                        catch (Exception)
                        {
                            // Entry vanished or is unreadable: skip it.
                        }
                    }
                }
                catch (Exception)
                {
                    // Directory unreadable: return what we have.
                }
            }
            result.Sort((a, b) => string.CompareOrdinal(a.Slot, b.Slot));
            return result;
        }

        // ================================================================== paths

        /// <summary>
        /// Resolves '&lt;Root&gt;/&lt;slot&gt;.gsav&lt;suffix&gt;' and verifies (via GetFullPath) that it lies
        /// directly inside the root. Callers validate the slot name first; this is defense in depth.
        /// </summary>
        private bool TryResolve(string slot, string suffix, out string path)
        {
            path = null;
            string candidate = Path.GetFullPath(Path.Combine(Root, slot + Extension + suffix));
            if (!candidate.StartsWith(_rootWithSeparator, StorageIo.PathComparison)) return false;
            int start = _rootWithSeparator.Length;
            if (candidate.Length <= start) return false;
            if (candidate.IndexOf(Path.DirectorySeparatorChar, start) >= 0) return false;
            if (candidate.IndexOf(Path.AltDirectorySeparatorChar, start) >= 0) return false;
            path = candidate;
            return true;
        }
    }
}
