// STUBS of GodSim.Core.Persistence for the SaveTool isolated compile check ONLY (never copy into Assets/ or tools/).
// They mirror the documented public API of GodSim.Core.Persistence (persistence team) and
// GodSim.Core.Composition.GodSimulation (lead), which are written in parallel and may not compile yet.
// The serializer is a tiny but functional stand-in (same container layout: header || tag(32) || payload,
// tag = SHA-256 or HMAC-SHA256 over header || payload) so the tool can be smoke-tested end to end.
#pragma warning disable CS1591
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace GodSim.Core.Persistence
{
    [Flags]
    public enum SaveFormatFlags : ushort { None = 0, Compressed = 1, Signed = 2 }

    public sealed class SaveOptions
    {
        public byte[] HmacKey;
        public bool Compress = true;
        public bool RequireSignature;
        public int MaxCompressedBytes = 64 * 1024 * 1024;
        public int MaxUncompressedBytes = 512 * 1024 * 1024;
        public string GameVersion = "0.1.0";
        public long CreatedUnixSeconds;
    }

    public sealed class SaveHeader
    {
        public int FormatVersion { get; internal set; }
        public int SchemaVersion { get; internal set; }
        public SaveFormatFlags Flags { get; internal set; }
        public long CreatedUnixSeconds { get; internal set; }
        public string GameVersion { get; internal set; }
        public ulong Seed { get; internal set; }
        public long Tick { get; internal set; }
        public int Population { get; internal set; }
        public int PayloadLength { get; internal set; }
        public int HeaderLength { get; internal set; }
        public bool IsSigned => (Flags & SaveFormatFlags.Signed) != 0;
        public bool IsCompressed => (Flags & SaveFormatFlags.Compressed) != 0;
        public int Year => SimTime.Year(Tick);
    }

    public sealed class SaveCorruptException : Exception
    {
        public SaveCorruptException() : base("Save data is corrupt.") { }
        public SaveCorruptException(string message) : base(message) { }
        public SaveCorruptException(string message, Exception inner) : base(message, inner) { }
    }

    public static class SaveSerializer
    {
        private const int Fixed = 45;

        public static byte[] Serialize(WorldState s, SaveOptions o)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            o = o ?? new SaveOptions();
            bool signed = o.HmacKey != null && o.HmacKey.Length > 0;
            if (o.RequireSignature && !signed) throw new InvalidOperationException("RequireSignature without a key");
            byte[] payload = Payload(s);
            if (o.Compress)
            {
                var ms = new MemoryStream();
                using (var gz = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionLevel.Fastest, true)) gz.Write(payload, 0, payload.Length);
                payload = ms.ToArray();
            }
            byte[] gv = Encoding.UTF8.GetBytes(o.GameVersion ?? "");
            if (gv.Length > 64) Array.Resize(ref gv, 64);
            var h = new MemoryStream();
            var w = new BinaryWriter(h);
            w.Write(new[] { (byte)'G', (byte)'S', (byte)'A', (byte)'V' });
            w.Write((ushort)1);
            w.Write((ushort)((o.Compress ? 1 : 0) | (signed ? 2 : 0)));
            w.Write(WorldState.SchemaVersion);
            w.Write(o.CreatedUnixSeconds);
            w.Write((byte)gv.Length); w.Write(gv);
            w.Write(s.Seed); w.Write(s.Tick); w.Write(s.LivingPopulation); w.Write(payload.Length);
            w.Flush();
            byte[] header = h.ToArray();
            byte[] tag = Tag(signed ? o.HmacKey : null, header, 0, header.Length, payload, 0, payload.Length);
            var all = new byte[header.Length + 32 + payload.Length];
            Buffer.BlockCopy(header, 0, all, 0, header.Length);
            Buffer.BlockCopy(tag, 0, all, header.Length, 32);
            Buffer.BlockCopy(payload, 0, all, header.Length + 32, payload.Length);
            return all;
        }

        public static SaveHeader ReadHeader(byte[] data)
        {
            if (data == null || data.Length < Fixed + 32) throw new SaveCorruptException("Save is truncated.");
            if (data[0] != 'G' || data[1] != 'S' || data[2] != 'A' || data[3] != 'V') throw new SaveCorruptException("Not a GodSim save (bad magic).");
            var r = new BinaryReader(new MemoryStream(data, false));
            try
            {
                r.ReadBytes(4);
                var hd = new SaveHeader { FormatVersion = r.ReadUInt16(), Flags = (SaveFormatFlags)r.ReadUInt16(), SchemaVersion = r.ReadInt32(), CreatedUnixSeconds = r.ReadInt64() };
                if (hd.FormatVersion != 1) throw new SaveCorruptException("Unsupported format version " + hd.FormatVersion + ".");
                if (((ushort)hd.Flags & ~3) != 0) throw new SaveCorruptException("Unknown header flags.");
                int gvLen = r.ReadByte();
                if (gvLen > 64) throw new SaveCorruptException("Game version too long.");
                hd.GameVersion = Encoding.UTF8.GetString(r.ReadBytes(gvLen));
                hd.Seed = r.ReadUInt64(); hd.Tick = r.ReadInt64(); hd.Population = r.ReadInt32(); hd.PayloadLength = r.ReadInt32();
                hd.HeaderLength = Fixed + gvLen;
                if (hd.PayloadLength < 0 || (long)hd.HeaderLength + 32 + hd.PayloadLength != data.Length) throw new SaveCorruptException("Payload length mismatch.");
                return hd;
            }
            catch (EndOfStreamException e) { throw new SaveCorruptException("Save header is truncated.", e); }
        }

        public static WorldState Deserialize(byte[] data, GameDatabase db, SaveOptions o)
        {
            o = o ?? new SaveOptions();
            if (data == null) throw new SaveCorruptException("No data.");
            if (data.Length > o.MaxCompressedBytes) throw new SaveCorruptException("Save is too large.");
            SaveHeader h = ReadHeader(data);
            bool haveKey = o.HmacKey != null && o.HmacKey.Length > 0;
            if (h.IsSigned && !haveKey) throw new SaveCorruptException("Save is signed but no key was supplied.");
            if (!h.IsSigned && o.RequireSignature) throw new SaveCorruptException("Save is not signed.");
            byte[] tag = Tag(h.IsSigned ? o.HmacKey : null, data, 0, h.HeaderLength, data, h.HeaderLength + 32, h.PayloadLength);
            if (!CryptographicOperations.FixedTimeEquals(tag, new ReadOnlySpan<byte>(data, h.HeaderLength, 32)))
                throw new SaveCorruptException(h.IsSigned ? "Signature mismatch." : "Checksum mismatch.");
            if (h.SchemaVersion != WorldState.SchemaVersion) throw new SaveCorruptException("Unsupported schema.");
            var payload = new byte[h.PayloadLength];
            Buffer.BlockCopy(data, h.HeaderLength + 32, payload, 0, payload.Length);
            try
            {
                if (h.IsCompressed)
                {
                    var outMs = new MemoryStream();
                    using (var gz = new System.IO.Compression.GZipStream(new MemoryStream(payload), System.IO.Compression.CompressionMode.Decompress)) gz.CopyTo(outMs);
                    payload = outMs.ToArray();
                }
                var r = new BinaryReader(new MemoryStream(payload));
                int w = r.ReadInt32(), hh = r.ReadInt32();
                if (w < WorldMap.MinSize || w > WorldMap.MaxSize || hh < WorldMap.MinSize || hh > WorldMap.MaxSize) throw new SaveCorruptException("Bad map size.");
                var s = new WorldState(h.Seed, new WorldMap(w, hh)) { Tick = r.ReadInt64() };
                int civs = r.ReadInt32();
                if (civs < 0 || civs > 1000) throw new SaveCorruptException("Bad civ count.");
                for (int i = 0; i < civs; i++)
                {
                    var c = new Civilization { Id = r.ReadInt32(), Name = r.ReadString(), Race = (RaceId)r.ReadByte(), Age = (AgeId)r.ReadByte() };
                    if (s.Civilizations.Contains(c.Id)) throw new SaveCorruptException("Duplicate civ id.");
                    s.Civilizations.Add(c);
                }
                return s;
            }
            catch (EndOfStreamException e) { throw new SaveCorruptException("Payload truncated.", e); }
            catch (InvalidDataException e) { throw new SaveCorruptException("Bad compressed payload.", e); }
            catch (FormatException e) { throw new SaveCorruptException("Bad string encoding.", e); }
            catch (ArgumentException e) { throw new SaveCorruptException("Invalid value.", e); }
        }

        private static byte[] Payload(WorldState s)
        {
            var ms = new MemoryStream();
            var w = new BinaryWriter(ms);
            w.Write(s.Map.Width); w.Write(s.Map.Height); w.Write(s.Tick);
            var civs = s.Civilizations.Items;
            w.Write(civs.Count);
            for (int i = 0; i < civs.Count; i++) { w.Write(civs[i].Id); w.Write(civs[i].Name ?? ""); w.Write((byte)civs[i].Race); w.Write((byte)civs[i].Age); }
            w.Flush();
            return ms.ToArray();
        }

        private static byte[] Tag(byte[] key, byte[] a, int ao, int ac, byte[] b, int bo, int bc)
        {
            using (HashAlgorithm alg = key != null ? (HashAlgorithm)new HMACSHA256(key) : SHA256.Create())
            {
                alg.TransformBlock(a, ao, ac, null, 0);
                alg.TransformFinalBlock(b, bo, bc);
                return alg.Hash;
            }
        }
    }

    public static class StateValidator
    {
        public static List<string> Validate(WorldState s, GameDatabase db)
        {
            var issues = new List<string>();
            if (s.Tick < 0) issues.Add("negative tick");
            return issues;
        }
    }

    public enum StateHashSection : byte
    {
        Header = 0, Map = 1, Rng = 2, Ids = 3, People = 4, Buildings = 5, Settlements = 6, Civilizations = 7,
        Diplomacy = 8, God = 9, Weather = 10, Chronicle = 11, Stats = 12, Memory = 13
    }

    public static class StateHasher
    {
        public static readonly int SectionCount = Enum.GetValues(typeof(StateHashSection)).Length;

        public static ulong Compute(WorldState s)
        {
            ulong h = 1469598103934665603UL;
            foreach (ulong v in ComputeSections(s)) h = Hashing.Combine(h, v);
            return h;
        }

        public static ulong[] ComputeSections(WorldState s)
        {
            var r = new ulong[SectionCount];
            r[0] = Hashing.Combine(s.Seed, (ulong)s.Tick);
            r[1] = Hashing.Combine((ulong)s.Map.Width, (ulong)s.Map.Height);
            ulong c = 7;
            var civs = s.Civilizations.Items;
            for (int i = 0; i < civs.Count; i++) c = Hashing.Combine(c, Hashing.Combine((ulong)civs[i].Id, Hashing.Fnv1a64(civs[i].Name)));
            r[7] = c;
            return r;
        }

        public static ulong ComputeSection(WorldState s, StateHashSection section) => ComputeSections(s)[(int)section];
        public static string Format(ulong hash) => hash.ToString("X16");
    }

    public struct ReplayEntry
    {
        public long Tick;
        public GodPowerType Power;
        public Vec2i Target;
        public int Radius;
        public int Parameter;
        public string Source;
    }

    public struct ReplayCheckpoint
    {
        public long Tick;
        public ulong Hash;
    }

    public sealed class ReplayHeader
    {
        public SimConfig ToConfig() => new SimConfig().Sanitize();
    }

    public sealed class ReplayLog
    {
        private readonly List<ReplayEntry> _entries = new List<ReplayEntry>();
        private readonly List<ReplayCheckpoint> _checkpoints = new List<ReplayCheckpoint>();
        public ReplayHeader Header { get; } = new ReplayHeader();
        public IReadOnlyList<ReplayEntry> Entries => _entries;
        public IReadOnlyList<ReplayCheckpoint> Checkpoints => _checkpoints;

        /// <summary>Stub format: "GRPL" + count(i32) + count * (tick i64, power u8, x i32, y i32, radius i32, param i32).</summary>
        public static ReplayLog FromBytes(byte[] data)
        {
            if (data == null || data.Length < 8 || data[0] != 'G' || data[1] != 'R' || data[2] != 'P' || data[3] != 'L') throw new SaveCorruptException("Not a replay.");
            var r = new BinaryReader(new MemoryStream(data));
            try
            {
                r.ReadBytes(4);
                int n = r.ReadInt32();
                if (n < 0 || n > 1000000) throw new SaveCorruptException("Bad entry count.");
                var log = new ReplayLog();
                for (int i = 0; i < n; i++)
                    log._entries.Add(new ReplayEntry { Tick = r.ReadInt64(), Power = (GodPowerType)r.ReadByte(), Target = new Vec2i(r.ReadInt32(), r.ReadInt32()), Radius = r.ReadInt32(), Parameter = r.ReadInt32(), Source = "replay" });
                return log;
            }
            catch (EndOfStreamException e) { throw new SaveCorruptException("Replay truncated.", e); }
        }
    }

    public interface IKeyProvider
    {
        byte[] GetKey();
    }

    public static class InstallKeyFile
    {
        public const string FileName = "install.key";
        public const int KeyLength = 32;
        public static byte[] LoadOrCreate(string directory) => throw new NotImplementedException();
    }
}
