using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace GodSim.Core.Persistence.Format
{
    /// <summary>Hard limits enforced on every save read (and on write, so every file we produce is loadable).</summary>
    internal static class SaveFormatLimits
    {
        public const int MaxMapTiles = 1024 * 1024;
        public const int MaxEntities = 2000000;          // people, buildings, settlements
        public const int MaxStringChars = 512;
        public const int MaxStringBytes = MaxStringChars * 3; // UTF-16 unit -> at most 3 UTF-8 bytes
        public const int MaxDictionaryEntries = 10000;
        public const int MaxListCount = 1000000;
        public const int MaxGameVersionBytes = 64;
        public const int TagLength = 32;
        /// <summary>magic(4) + version(2) + flags(2) + schema(4) + created(8) + gvLen(1) + seed(8) + tick(8) + pop(4) + payloadLen(4).</summary>
        public const int FixedHeaderBytes = 45;
    }

    /// <summary>Container constants.</summary>
    internal static class SaveFormatConstants
    {
        public static readonly byte[] Magic = { (byte)'G', (byte)'S', (byte)'A', (byte)'V' };
        public const ushort FormatVersion = 1;
        public const ushort KnownFlags = (ushort)(SaveFormatFlags.Compressed | SaveFormatFlags.Signed);
    }

    /// <summary>
    /// Precomputed validity table for a byte-backed enum (built once with <see cref="Enum.IsDefined(Type, object)"/>),
    /// so validating a million map tiles does not box or reflect per value.
    /// </summary>
    internal static class EnumTable<T> where T : struct, Enum
    {
        public static readonly bool[] Defined = new bool[256];
        public static readonly T[] Values = new T[256];

        static EnumTable()
        {
            if (Enum.GetUnderlyingType(typeof(T)) != typeof(byte))
                throw new InvalidOperationException("EnumTable requires a byte-backed enum: " + typeof(T).Name);
            for (int v = 0; v < 256; v++)
            {
                object boxed = (byte)v;
                Defined[v] = Enum.IsDefined(typeof(T), boxed);
                Values[v] = (T)Enum.ToObject(typeof(T), boxed);
            }
        }
    }

    /// <summary>Bit masks of every defined value of the [Flags] enums stored in saves.</summary>
    internal static class FlagMasks
    {
        public static readonly ushort TileFlags = Compute(typeof(TileFlags));
        public static readonly ushort PersonTraits = Compute(typeof(PersonTraits));

        private static ushort Compute(Type t)
        {
            ulong m = 0;
            foreach (object v in Enum.GetValues(t)) m |= Convert.ToUInt64(v);
            return (ushort)m;
        }
    }

    /// <summary>
    /// Read-only pass-through stream that throws <see cref="SaveCorruptException"/> as soon as more than
    /// <c>limit</c> bytes have been produced by the inner (decompressing) stream: a decompression-bomb fuse.
    /// </summary>
    internal sealed class LimitedReadStream : Stream
    {
        private readonly Stream _inner;
        private readonly long _limit;
        private long _total;
        private uint _crc = 0xFFFFFFFFu;

        public LimitedReadStream(Stream inner, long limit)
        {
            _inner = inner;
            _limit = limit;
        }

        public long TotalRead => _total;
        /// <summary>CRC-32 (IEEE) of every byte produced so far, for verifying the GZip trailer independently of the platform's inflater.</summary>
        public uint Crc32 => ~_crc;
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => _total; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            int n = _inner.Read(buffer, offset, count);
            _total += n;
            if (_total > _limit)
                throw new SaveCorruptException("Decompressed payload exceeds the limit of " + _limit + " bytes.");
            _crc = Crc32Ieee.Update(_crc, buffer, offset, n);
            return n;
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>Table-driven CRC-32 (IEEE 802.3, reflected 0xEDB88320), as used by the GZip trailer.</summary>
    internal static class Crc32Ieee
    {
        private static readonly uint[] Table = Build();

        private static uint[] Build()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        /// <summary>Updates a running (pre-inverted) CRC state.</summary>
        public static uint Update(uint crc, byte[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++) crc = Table[(crc ^ buffer[offset + i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }
    }

    /// <summary>Tag computation (SHA-256 or HMAC-SHA256 over header || payload) and constant-time comparison.</summary>
    internal static class SaveIntegrity
    {
        /// <summary>Computes the 32-byte tag over two disjoint segments. key == null means plain SHA-256.</summary>
        public static byte[] ComputeTag(byte[] key, byte[] a, int aOffset, int aCount, byte[] b, int bOffset, int bCount)
        {
            using (HashAlgorithm h = key != null ? (HashAlgorithm)new HMACSHA256(key) : SHA256.Create())
            {
                h.TransformBlock(a, aOffset, aCount, null, 0);
                h.TransformFinalBlock(b, bOffset, bCount);
                return h.Hash;
            }
        }

        /// <summary>
        /// Compares expected against data[offset..offset+expected.Length) in time independent of where (or whether)
        /// the bytes differ, so a forger cannot learn the correct HMAC byte by byte from timing.
        /// </summary>
        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static bool FixedTimeEquals(byte[] expected, byte[] data, int offset)
        {
            if (expected == null || data == null || offset < 0 || (long)offset + expected.Length > data.Length) return false;
            int diff = 0;
            for (int i = 0; i < expected.Length; i++) diff |= expected[i] ^ data[offset + i];
            return diff == 0;
        }
    }
}
