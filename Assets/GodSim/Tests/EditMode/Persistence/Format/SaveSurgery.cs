using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using GodSim.Core.Persistence;

namespace GodSim.Tests.Persistence.Format
{
    /// <summary>
    /// Raw save-file manipulation for adversarial tests, written only against the documented layout
    /// (docs/SAVE_FORMAT.md) and the public API: split a file, mutate it, and recompute the tag so that
    /// malformed payloads actually reach the parser instead of stopping at the integrity check.
    /// </summary>
    public static class SaveSurgery
    {
        public static int HeaderLength(byte[] file) => SaveSerializer.ReadHeader(file).HeaderLength;
        public static int PayloadOffset(byte[] file) => HeaderLength(file) + SaveSerializer.TagLength;

        public static byte[] Header(byte[] file)
        {
            int n = HeaderLength(file);
            var h = new byte[n];
            Buffer.BlockCopy(file, 0, h, 0, n);
            return h;
        }

        public static byte[] Payload(byte[] file)
        {
            int o = PayloadOffset(file);
            var p = new byte[file.Length - o];
            Buffer.BlockCopy(file, o, p, 0, p.Length);
            return p;
        }

        /// <summary>Assembles header | tag | payload, patching the header's trailing payload-length field and computing the tag.</summary>
        public static byte[] Rebuild(byte[] header, byte[] payload, byte[] key)
        {
            var h = (byte[])header.Clone();
            WriteInt32(h, h.Length - 4, payload.Length);
            byte[] tag = Tag(h, payload, key);
            var file = new byte[h.Length + tag.Length + payload.Length];
            Buffer.BlockCopy(h, 0, file, 0, h.Length);
            Buffer.BlockCopy(tag, 0, file, h.Length, tag.Length);
            Buffer.BlockCopy(payload, 0, file, h.Length + tag.Length, payload.Length);
            return file;
        }

        /// <summary>Recomputes the tag in place for a file whose bytes were mutated without changing lengths.</summary>
        public static void Retag(byte[] file, int headerLength, byte[] key)
        {
            int po = headerLength + SaveSerializer.TagLength;
            if (po > file.Length) return;
            var h = new byte[headerLength];
            Buffer.BlockCopy(file, 0, h, 0, headerLength);
            var p = new byte[file.Length - po];
            Buffer.BlockCopy(file, po, p, 0, p.Length);
            Buffer.BlockCopy(Tag(h, p, key), 0, file, headerLength, SaveSerializer.TagLength);
        }

        public static byte[] Tag(byte[] header, byte[] payload, byte[] key)
        {
            using (HashAlgorithm alg = key != null ? (HashAlgorithm)new HMACSHA256(key) : SHA256.Create())
            {
                alg.TransformBlock(header, 0, header.Length, null, 0);
                alg.TransformFinalBlock(payload, 0, payload.Length);
                return alg.Hash;
            }
        }

        public static int IndexOf(byte[] data, string ascii, int start = 0)
        {
            var needle = Encoding.ASCII.GetBytes(ascii);
            for (int i = start; i <= data.Length - needle.Length; i++)
            {
                int k = 0;
                while (k < needle.Length && data[i + k] == needle[k]) k++;
                if (k == needle.Length) return i;
            }
            return -1;
        }

        public static void WriteInt32(byte[] b, int offset, int v)
        {
            b[offset] = (byte)v; b[offset + 1] = (byte)(v >> 8); b[offset + 2] = (byte)(v >> 16); b[offset + 3] = (byte)(v >> 24);
        }

        public static void WriteSingle(byte[] b, int offset, float f) => WriteInt32(b, offset, BitConverter.SingleToInt32Bits(f));

        public static byte[] Gzip(byte[] raw)
        {
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true)) gz.Write(raw, 0, raw.Length);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// GZip stream of <paramref name="size"/> zero bytes, produced by streaming one reused zero chunk
        /// (never materializing the plaintext); the output is roughly size/1000 bytes.
        /// </summary>
        public static byte[] ZeroBomb(long size, byte[] prefix)
        {
            var chunk = new byte[1 << 20];
            using (var ms = new MemoryStream())
            {
                using (var gz = new GZipStream(ms, CompressionLevel.Optimal, true))
                {
                    if (prefix != null) gz.Write(prefix, 0, prefix.Length);
                    for (long written = 0; written < size; written += chunk.Length)
                        gz.Write(chunk, 0, (int)Math.Min(chunk.Length, size - written));
                }
                return ms.ToArray();
            }
        }

        public static byte[] Concat(byte[] a, byte[] b)
        {
            var r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }
    }
}
