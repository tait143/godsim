using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GodSim.Core.Persistence
{
    /// <summary>
    /// Per-installation random secret stored in '&lt;directory&gt;/install.key' (exactly 32 raw bytes).
    /// <para>
    /// Creation is atomic: the secret is generated with <see cref="RandomNumberGenerator"/>, written to a
    /// uniquely named temp file (created exclusively and flushed to disk) and published with a no-overwrite
    /// <see cref="File.Move(string,string)"/>. If another process wins the race, its key is loaded instead.
    /// Loading refuses links/reparse points and any file whose size is not exactly <see cref="KeyLength"/>.
    /// </para>
    /// <para>
    /// The key protects saves against casual tampering by other installs; it is not a defense against a user
    /// who controls the machine. File permissions are inherited from the directory (netstandard2.1 has no
    /// portable API to restrict them).
    /// </para>
    /// </summary>
    public static class InstallKeyFile
    {
        /// <summary>Key file name inside the directory.</summary>
        public const string FileName = "install.key";
        /// <summary>Secret length in bytes.</summary>
        public const int KeyLength = 32;

        private static readonly object Sync = new object();

        /// <summary>Loads the install secret, creating it first if absent. Returns a fresh copy.</summary>
        /// <exception cref="ArgumentException">The directory is empty or not a valid path.</exception>
        /// <exception cref="InvalidDataException">The key file exists but is corrupt (wrong size or all zeros).</exception>
        /// <exception cref="IOException">The directory/key file is unsafe (link) or inaccessible. Messages contain no paths.</exception>
        public static byte[] LoadOrCreate(string directory)
        {
            string dir = NormalizeDirectory(directory);
            if (TryLoadOrCreateCore(dir, out byte[] key, out string error, out bool corrupt)) return key;
            if (corrupt) throw new InvalidDataException(error);
            throw new IOException(error);
        }

        /// <summary>Non-throwing variant of <see cref="LoadOrCreate"/>.</summary>
        /// <returns>True with the key; otherwise false with a path-free <paramref name="error"/>.</returns>
        public static bool TryLoadOrCreate(string directory, out byte[] key, out string error)
        {
            key = null;
            string dir;
            try { dir = NormalizeDirectory(directory); }
            catch (ArgumentException ex) { error = ex.Message; return false; }
            return TryLoadOrCreateCore(dir, out key, out error, out _);
        }

        private static string NormalizeDirectory(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory)) throw new ArgumentException("A key directory is required.", nameof(directory));
            try { return StorageIo.TrimTrailingSeparators(Path.GetFullPath(directory)); }
            catch (Exception ex) when (StorageIo.IsPathFormatException(ex)) { throw new ArgumentException("The key directory is not a valid path.", nameof(directory)); }
        }

        private static bool TryLoadOrCreateCore(string dir, out byte[] key, out string error, out bool corrupt)
        {
            key = null;
            corrupt = false;
            lock (Sync)
            {
                try
                {
                    if (!StorageIo.EnsureSafeDirectory(dir, true, "Key directory", out error)) return false;
                    string path = Path.Combine(dir, FileName);
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        switch (StorageIo.GetKind(path))
                        {
                            case StorageEntryKind.Link: error = "Install key file is a symbolic link or reparse point; refusing to use it."; return false;
                            case StorageEntryKind.Directory: error = "Install key path is occupied by a directory."; return false;
                            case StorageEntryKind.File: return TryReadKey(path, out key, out error, out corrupt);
                        }
                        if (TryPublishNewKey(dir, path, out key)) return true;
                        // Lost a creation race: loop and load the winner's key.
                    }
                    error = "Install key could not be created.";
                    return false;
                }
                catch (Exception ex)
                {
                    key = null;
                    error = StorageIo.Describe("load or create the install key", ex);
                    return false;
                }
            }
        }

        private static bool TryReadKey(string path, out byte[] key, out string error, out bool corrupt)
        {
            key = null;
            error = null;
            corrupt = false;
            long length = new FileInfo(path).Length;
            if (length != KeyLength) { corrupt = true; error = "Install key file is corrupt (expected " + KeyLength + " bytes, found " + length + ")."; return false; }
            var buffer = new byte[KeyLength];
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 64, FileOptions.None))
            {
                int read = 0;
                while (read < KeyLength)
                {
                    int n = fs.Read(buffer, read, KeyLength - read);
                    if (n <= 0) break;
                    read += n;
                }
                if (read != KeyLength || fs.ReadByte() != -1) { Array.Clear(buffer, 0, buffer.Length); corrupt = true; error = "Install key file is corrupt (size changed while reading)."; return false; }
            }
            if (IsAllZero(buffer)) { corrupt = true; error = "Install key file is corrupt (empty key)."; return false; }
            key = buffer;
            return true;
        }

        /// <summary>Writes a fresh key to a unique temp file and publishes it without overwriting. False if the key file appeared meanwhile.</summary>
        private static bool TryPublishNewKey(string dir, string path, out byte[] key)
        {
            key = null;
            byte[] secret = NewSecret();
            string tmp = Path.Combine(dir, FileName + "." + RandomHex(8) + ".tmp");
            bool tmpOwned = false;
            try
            {
                using (var fs = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 64, FileOptions.None))
                {
                    tmpOwned = true;
                    fs.Write(secret, 0, secret.Length);
                    fs.Flush(true);
                }
                try
                {
                    File.Move(tmp, path); // no-overwrite: fails if another writer published first
                }
                catch (IOException) when (StorageIo.GetKind(path) != StorageEntryKind.None)
                {
                    Array.Clear(secret, 0, secret.Length);
                    return false;
                }
                tmpOwned = false;
                key = secret;
                return true;
            }
            finally
            {
                if (tmpOwned) StorageIo.TryDeleteQuietly(tmp);
            }
        }

        private static byte[] NewSecret()
        {
            var secret = new byte[KeyLength];
            using (var rng = RandomNumberGenerator.Create())
            {
                do { rng.GetBytes(secret); } while (IsAllZero(secret));
            }
            return secret;
        }

        private static string RandomHex(int bytes)
        {
            var buf = new byte[bytes];
            using (var rng = RandomNumberGenerator.Create()) rng.GetBytes(buf);
            var sb = new StringBuilder(bytes * 2);
            for (int i = 0; i < buf.Length; i++) sb.Append(buf[i].ToString("x2"));
            return sb.ToString();
        }

        private static bool IsAllZero(byte[] b)
        {
            int acc = 0;
            for (int i = 0; i < b.Length; i++) acc |= b[i];
            return acc == 0;
        }
    }
}
