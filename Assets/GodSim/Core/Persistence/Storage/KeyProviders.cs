using System;
using System.Security.Cryptography;
using System.Text;

namespace GodSim.Core.Persistence
{
    /// <summary>
    /// Supplies key material (e.g. for save-file HMACs). Implementations return a fresh copy on every call,
    /// so callers may (and should) clear the array with <see cref="Array.Clear(Array, int, int)"/> after use.
    /// </summary>
    public interface IKeyProvider
    {
        /// <summary>Returns a new copy of the key bytes.</summary>
        byte[] GetKey();
    }

    /// <summary>A fixed key held in memory (copied defensively on construction and on every read).</summary>
    public sealed class StaticKeyProvider : IKeyProvider
    {
        /// <summary>Minimum accepted key length in bytes (128 bits).</summary>
        public const int MinKeyLength = 16;

        private readonly byte[] _key;

        /// <summary>Creates the provider from a copy of <paramref name="key"/>.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="key"/> is null.</exception>
        /// <exception cref="ArgumentException">The key is shorter than <see cref="MinKeyLength"/> bytes.</exception>
        public StaticKeyProvider(byte[] key)
        {
            if (key == null) throw new ArgumentNullException(nameof(key));
            if (key.Length < MinKeyLength) throw new ArgumentException("Key must be at least " + MinKeyLength + " bytes.", nameof(key));
            _key = (byte[])key.Clone();
        }

        /// <summary>Length of the key in bytes.</summary>
        public int KeyLength => _key.Length;

        /// <inheritdoc />
        public byte[] GetKey() => (byte[])_key.Clone();
    }

    /// <summary>
    /// A 32-byte key derived with PBKDF2-HMAC-SHA256 (<see cref="Rfc2898DeriveBytes"/>) from a secret and a
    /// salt. Derivation runs once, in the constructor; the secret is not retained.
    /// </summary>
    public sealed class DerivedKeyProvider : IKeyProvider
    {
        /// <summary>Derived key length in bytes.</summary>
        public const int KeyLength = 32;
        /// <summary>Default PBKDF2 iteration count.</summary>
        public const int DefaultIterations = 100000;
        /// <summary>Lowest accepted iteration count (RFC 8018 minimum recommendation).</summary>
        public const int MinIterations = 1000;
        /// <summary>Highest accepted iteration count (bounds CPU cost when the count comes from config).</summary>
        public const int MaxIterations = 10000000;
        /// <summary>Minimum salt length (PBKDF2/RFC 2898 minimum; 16+ recommended).</summary>
        public const int MinSaltLength = 8;
        /// <summary>Maximum salt length.</summary>
        public const int MaxSaltLength = 1024;
        /// <summary>Maximum secret length in bytes (UTF-8 for string secrets).</summary>
        public const int MaxSecretLength = 4096;

        private readonly byte[] _key;

        /// <summary>Derives the key from a text secret (encoded as UTF-8).</summary>
        /// <exception cref="ArgumentException">Secret empty/too long, salt too short/long, or iterations out of range.</exception>
        public DerivedKeyProvider(string secret, byte[] salt, int iterations = DefaultIterations)
            : this(EncodeSecret(secret), salt, iterations, true)
        {
        }

        /// <summary>Derives the key from binary secret material (e.g. <see cref="InstallKeyFile.LoadOrCreate"/>).</summary>
        /// <exception cref="ArgumentException">Secret empty/too long, salt too short/long, or iterations out of range.</exception>
        public DerivedKeyProvider(byte[] secret, byte[] salt, int iterations = DefaultIterations)
            : this(CopySecret(secret), salt, iterations, true)
        {
        }

        private DerivedKeyProvider(byte[] ownedSecret, byte[] salt, int iterations, bool owned)
        {
            try
            {
                if (salt == null) throw new ArgumentNullException(nameof(salt));
                if (salt.Length < MinSaltLength || salt.Length > MaxSaltLength) throw new ArgumentException("Salt must be " + MinSaltLength + ".." + MaxSaltLength + " bytes.", nameof(salt));
                if (iterations < MinIterations || iterations > MaxIterations) throw new ArgumentOutOfRangeException(nameof(iterations), "Iterations must be within [" + MinIterations + ", " + MaxIterations + "].");
                Iterations = iterations;
                using (var kdf = new Rfc2898DeriveBytes(ownedSecret, (byte[])salt.Clone(), iterations, HashAlgorithmName.SHA256))
                    _key = kdf.GetBytes(KeyLength);
            }
            finally
            {
                if (owned) Array.Clear(ownedSecret, 0, ownedSecret.Length);
            }
        }

        /// <summary>PBKDF2 iteration count used.</summary>
        public int Iterations { get; }

        /// <inheritdoc />
        public byte[] GetKey() => (byte[])_key.Clone();

        private static byte[] EncodeSecret(string secret)
        {
            if (string.IsNullOrEmpty(secret)) throw new ArgumentException("Secret must not be empty.", nameof(secret));
            if (secret.Length > MaxSecretLength) throw new ArgumentException("Secret is too long.", nameof(secret));
            byte[] bytes;
            // Strict encoding: lone surrogates must not silently collapse distinct secrets onto U+FFFD.
            // The fallback exception's message would quote secret characters, so it is replaced.
            try { bytes = new UTF8Encoding(false, true).GetBytes(secret); }
            catch (EncoderFallbackException) { throw new ArgumentException("Secret is not valid Unicode text.", nameof(secret)); }
            if (bytes.Length > MaxSecretLength) { Array.Clear(bytes, 0, bytes.Length); throw new ArgumentException("Secret is too long.", nameof(secret)); }
            return bytes;
        }

        private static byte[] CopySecret(byte[] secret)
        {
            if (secret == null) throw new ArgumentNullException(nameof(secret));
            if (secret.Length == 0 || secret.Length > MaxSecretLength) throw new ArgumentException("Secret must be 1.." + MaxSecretLength + " bytes.", nameof(secret));
            return (byte[])secret.Clone();
        }
    }
}
