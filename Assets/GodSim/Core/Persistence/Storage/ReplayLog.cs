using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace GodSim.Core.Persistence
{
    /// <summary>
    /// Command replay log: a <see cref="ReplayHeader"/> plus every accepted <see cref="GodCommand"/> with the
    /// tick it was accepted at, and optional state-hash checkpoints for verification.
    /// <para>
    /// Recording: <see cref="Attach"/> hooks <see cref="CommandQueue.OnAccepted"/> (chained, so other
    /// observers keep working). Recording is bounded by <see cref="MaxEntries"/>; commands beyond the cap are
    /// dropped and counted in <see cref="OverflowCount"/>. The recording hook never throws into the queue.
    /// </para>
    /// <para>
    /// Playback: for each tick T, before stepping the engine from T, re-submit <see cref="ForTick"/>(T)
    /// through <see cref="CommandQueue.TryEnqueue"/> (commands are re-validated; replays are untrusted).
    /// </para>
    /// <para>
    /// Binary form (<see cref="ToBytes"/>/<see cref="FromBytes"/>): little-endian, magic "GSRP", version,
    /// header, entries, checkpoints and a trailing FNV-1a 64 checksum (detects corruption, not tampering;
    /// sign the bytes with an HMAC if authenticity matters). Parsing is strictly bounded:
    /// at most <see cref="MaxSerializedEntries"/> entries, strings at most <see cref="MaxTextLength"/> chars,
    /// validated enums; anything malformed raises <see cref="InvalidDataException"/>.
    /// </para>
    /// </summary>
    public sealed class ReplayLog
    {
        /// <summary>Hard cap on entries (recording and parsing).</summary>
        public const int MaxSerializedEntries = 1000000;
        /// <summary>Hard cap on checkpoints (recording and parsing).</summary>
        public const int MaxCheckpoints = 100000;
        /// <summary>Maximum length (UTF-16 code units) of Source / GameVersion strings.</summary>
        public const int MaxTextLength = 64;
        /// <summary>Binary format version written by <see cref="ToBytes"/>.</summary>
        public const ushort FormatVersion = 1;

        private const int MaxTextBytes = MaxTextLength * 3; // UTF-8 worst case for 64 UTF-16 code units
        private const int MinEntryBytes = 8 + 1 + 4 + 4 + 4 + 4 + 1;
        private const int CheckpointBytes = 16;
        private const int ChecksumBytes = 8;
        /// <summary>Largest byte array <see cref="FromBytes"/> accepts.</summary>
        public const int MaxSerializedBytes = 1024 + MaxSerializedEntries * (MinEntryBytes + MaxTextBytes) + MaxCheckpoints * CheckpointBytes + ChecksumBytes;

        private static readonly byte[] Magic = { (byte)'G', (byte)'S', (byte)'R', (byte)'P' };
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);
        private static readonly bool[] DefinedPowers = BuildDefinedPowers();

        private readonly List<ReplayEntry> _entries = new List<ReplayEntry>();
        private readonly List<ReplayCheckpoint> _checkpoints = new List<ReplayCheckpoint>();
        private readonly Action<GodCommand> _handler;
        private bool _sorted = true;
        private CommandQueue _queue;
        private Func<long> _tickProvider;

        /// <summary>Creates an empty log with a default header and the maximum entry cap.</summary>
        public ReplayLog() : this(null, MaxSerializedEntries) { }

        /// <summary>Creates an empty log.</summary>
        /// <param name="header">World parameters (null = defaults). The log keeps this instance.</param>
        /// <param name="maxEntries">Recording cap, clamped to [1, <see cref="MaxSerializedEntries"/>].</param>
        public ReplayLog(ReplayHeader header, int maxEntries = MaxSerializedEntries)
        {
            Header = header ?? new ReplayHeader();
            MaxEntries = maxEntries < 1 ? 1 : (maxEntries > MaxSerializedEntries ? MaxSerializedEntries : maxEntries);
            _handler = OnCommandAccepted;
        }

        /// <summary>World parameters of this replay.</summary>
        public ReplayHeader Header { get; }
        /// <summary>Recording cap.</summary>
        public int MaxEntries { get; }
        /// <summary>Number of commands dropped because the cap was reached.</summary>
        public long OverflowCount { get; private set; }
        /// <summary>Recorded entries in acceptance order.</summary>
        public IReadOnlyList<ReplayEntry> Entries => _entries;
        /// <summary>Recorded checkpoints in ascending tick order.</summary>
        public IReadOnlyList<ReplayCheckpoint> Checkpoints => _checkpoints;
        /// <summary>True while hooked to a queue.</summary>
        public bool IsAttached => _queue != null;

        // ================================================================== recording

        /// <summary>
        /// Starts recording every command accepted by <paramref name="queue"/>, stamped with
        /// <paramref name="tickProvider"/>(). Detaches from any previously attached queue first.
        /// </summary>
        public void Attach(CommandQueue queue, Func<long> tickProvider)
        {
            if (queue == null) throw new ArgumentNullException(nameof(queue));
            if (tickProvider == null) throw new ArgumentNullException(nameof(tickProvider));
            Detach();
            _queue = queue;
            _tickProvider = tickProvider;
            queue.OnAccepted += _handler;
        }

        /// <summary>Stops recording (no-op when not attached).</summary>
        public void Detach()
        {
            if (_queue != null) _queue.OnAccepted -= _handler;
            _queue = null;
            _tickProvider = null;
        }

        private void OnCommandAccepted(GodCommand command)
        {
            if (command == null) return;
            long tick;
            try { tick = _tickProvider != null ? _tickProvider() : command.IssuedTick; }
            catch (Exception) { tick = command.IssuedTick; } // never throw into CommandQueue.TryEnqueue
            Record(command, tick);
        }

        /// <summary>Records a command at a tick. Returns false if invalid (null, negative tick, unknown power) or over the cap.</summary>
        public bool Record(GodCommand command, long tick)
        {
            if (command == null) return false;
            return Add(ReplayEntry.FromCommand(command, tick));
        }

        /// <summary>
        /// Appends an entry (its Source is sanitized). Returns false for a negative tick or unknown power, or when
        /// the cap is reached (then <see cref="OverflowCount"/> is incremented).
        /// </summary>
        public bool Add(ReplayEntry entry)
        {
            if (entry.Tick < 0 || !IsDefinedPower(entry.Power)) return false;
            if (_entries.Count >= MaxEntries) { OverflowCount++; return false; }
            entry.Source = SanitizeText(entry.Source);
            if (_entries.Count > 0 && entry.Tick < _entries[_entries.Count - 1].Tick) _sorted = false;
            _entries.Add(entry);
            return true;
        }

        /// <summary>Records a state-hash checkpoint. Ticks must be non-negative and strictly increasing.</summary>
        public bool AddCheckpoint(long tick, ulong stateHash)
        {
            if (tick < 0 || _checkpoints.Count >= MaxCheckpoints) return false;
            if (_checkpoints.Count > 0 && tick <= _checkpoints[_checkpoints.Count - 1].Tick) return false;
            _checkpoints.Add(new ReplayCheckpoint { Tick = tick, Hash = stateHash });
            return true;
        }

        /// <summary>Looks up the checkpoint recorded at exactly <paramref name="tick"/>.</summary>
        public bool TryGetCheckpoint(long tick, out ulong stateHash)
        {
            int lo = 0, hi = _checkpoints.Count - 1;
            while (lo <= hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                long t = _checkpoints[mid].Tick;
                if (t == tick) { stateHash = _checkpoints[mid].Hash; return true; }
                if (t < tick) lo = mid + 1; else hi = mid - 1;
            }
            stateHash = 0;
            return false;
        }

        // ================================================================== playback

        /// <summary>Entries recorded at <paramref name="tick"/>, in acceptance order.</summary>
        public IEnumerable<ReplayEntry> ForTick(long tick)
        {
            if (_sorted)
            {
                for (int i = LowerBound(tick); i < _entries.Count && _entries[i].Tick == tick; i++) yield return _entries[i];
            }
            else
            {
                for (int i = 0; i < _entries.Count; i++) if (_entries[i].Tick == tick) yield return _entries[i];
            }
        }

        /// <summary>Allocation-free variant of <see cref="ForTick"/>: appends matches to <paramref name="output"/> and returns how many.</summary>
        public int CopyForTick(long tick, List<ReplayEntry> output)
        {
            if (output == null) throw new ArgumentNullException(nameof(output));
            int n = 0;
            if (_sorted)
            {
                for (int i = LowerBound(tick); i < _entries.Count && _entries[i].Tick == tick; i++) { output.Add(_entries[i]); n++; }
            }
            else
            {
                for (int i = 0; i < _entries.Count; i++) if (_entries[i].Tick == tick) { output.Add(_entries[i]); n++; }
            }
            return n;
        }

        private int LowerBound(long tick)
        {
            int lo = 0, hi = _entries.Count;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (_entries[mid].Tick < tick) lo = mid + 1; else hi = mid;
            }
            return lo;
        }

        // ================================================================== text sanitizing

        /// <summary>
        /// Normalizes a tag for storage: null becomes "", control characters and unpaired surrogates become '?',
        /// and the result is cut to <see cref="MaxTextLength"/> code units without splitting a surrogate pair.
        /// </summary>
        public static string SanitizeText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Length <= MaxTextLength && IsCleanText(s)) return s;
            var sb = new StringBuilder(Math.Min(s.Length, MaxTextLength));
            for (int i = 0; i < s.Length && sb.Length < MaxTextLength; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1]))
                    {
                        if (sb.Length + 2 > MaxTextLength) break;
                        sb.Append(c).Append(s[i + 1]);
                        i++;
                        continue;
                    }
                    sb.Append('?');
                }
                else if (char.IsLowSurrogate(c) || char.IsControl(c)) sb.Append('?');
                else sb.Append(c);
            }
            return sb.ToString();
        }

        /// <summary>True if the text is at most <see cref="MaxTextLength"/> long, has no control characters and no unpaired surrogates.</summary>
        public static bool IsCleanText(string s)
        {
            if (s == null || s.Length > MaxTextLength) return false;
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsControl(c)) return false;
                if (char.IsHighSurrogate(c))
                {
                    if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return false;
                    i++;
                }
                else if (char.IsLowSurrogate(c)) return false;
            }
            return true;
        }

        // ================================================================== serialization

        /// <summary>Serializes the log. Throws <see cref="InvalidOperationException"/> if the header is invalid.</summary>
        public byte[] ToBytes()
        {
            if (!Header.Validate(out string reason)) throw new InvalidOperationException("Replay header is invalid: " + reason + ".");
            var w = new Writer(256 + _entries.Count * (MinEntryBytes + 8) + _checkpoints.Count * CheckpointBytes);
            w.Bytes(Magic);
            w.U16(FormatVersion);
            w.U16(0);

            var h = Header;
            w.U64(h.Seed);
            w.I64(h.StartTick);
            w.U64(h.StartStateHash);
            w.Text(h.GameVersion);
            w.I32(h.MapWidth);
            w.I32(h.MapHeight);
            w.U8((byte)h.Preset);
            w.U8((byte)h.StartingRaces.Length);
            for (int i = 0; i < h.StartingRaces.Length; i++) w.U8((byte)h.StartingRaces[i]);
            w.I32(h.InitialPopulationPerRace);
            w.I32(h.MaxPopulation);
            w.I32(h.MaxSettlementsPerCiv);
            w.I32(h.MaxBuildingsPerSettlement);
            w.I32(h.MaxArmies);
            w.I32(h.MaxCommandsPerTick);
            w.I32(h.CommandQueueCapacity);
            w.I32(h.MaxSystemFaults);
            w.F32(h.GrowthMultiplier);
            w.F32(h.ResearchMultiplier);
            w.F32(h.ProductionMultiplier);
            w.F32(h.DisasterFrequency);
            w.F32(h.StartingMana);
            w.F32(h.ManaRegenMultiplier);
            w.U8((byte)((h.EnableWarfare ? 1 : 0) | (h.EnableDisasters ? 2 : 0) | (h.StrictMode ? 4 : 0)));

            w.I32(MaxEntries);
            w.I64(OverflowCount);
            w.I32(_entries.Count);
            for (int i = 0; i < _entries.Count; i++)
            {
                var e = _entries[i];
                w.I64(e.Tick);
                w.U8((byte)e.Power);
                w.I32(e.Target.X);
                w.I32(e.Target.Y);
                w.I32(e.Radius);
                w.I32(e.Parameter);
                w.Text(e.Source);
            }
            w.I32(_checkpoints.Count);
            for (int i = 0; i < _checkpoints.Count; i++)
            {
                w.I64(_checkpoints[i].Tick);
                w.U64(_checkpoints[i].Hash);
            }
            w.U64(Checksum(w.Buffer, w.Length));
            return w.ToArray();
        }

        /// <summary>Parses and validates a serialized log.</summary>
        /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
        /// <exception cref="InvalidDataException">The data is corrupt, truncated, oversized or out of bounds.</exception>
        public static ReplayLog FromBytes(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            if (data.Length < Magic.Length + 4 + ChecksumBytes) throw new InvalidDataException("Replay data is truncated.");
            if (data.Length > MaxSerializedBytes) throw new InvalidDataException("Replay data exceeds the maximum size.");
            for (int i = 0; i < Magic.Length; i++) if (data[i] != Magic[i]) throw new InvalidDataException("Not a replay file (bad magic).");

            int payload = data.Length - ChecksumBytes;
            var tail = new Reader(data, payload, data.Length);
            if (tail.U64() != Checksum(data, payload)) throw new InvalidDataException("Replay checksum mismatch (data is corrupt).");

            try
            {
                var r = new Reader(data, Magic.Length, payload);
                ushort version = r.U16();
                if (version != FormatVersion) throw new InvalidDataException("Unsupported replay format version " + version + ".");
                if (r.U16() != 0) throw new InvalidDataException("Unsupported replay flags.");

                var h = new ReplayHeader();
                h.Seed = r.U64();
                h.StartTick = r.I64();
                h.StartStateHash = r.U64();
                h.GameVersion = r.Text();
                h.MapWidth = r.I32();
                h.MapHeight = r.I32();
                h.Preset = (WorldPreset)r.U8();
                int races = r.U8();
                if (races > ReplayHeader.MaxRaces) throw new InvalidDataException("Too many starting races.");
                h.StartingRaces = new RaceId[races];
                for (int i = 0; i < races; i++) h.StartingRaces[i] = (RaceId)r.U8();
                h.InitialPopulationPerRace = r.I32();
                h.MaxPopulation = r.I32();
                h.MaxSettlementsPerCiv = r.I32();
                h.MaxBuildingsPerSettlement = r.I32();
                h.MaxArmies = r.I32();
                h.MaxCommandsPerTick = r.I32();
                h.CommandQueueCapacity = r.I32();
                h.MaxSystemFaults = r.I32();
                h.GrowthMultiplier = r.F32();
                h.ResearchMultiplier = r.F32();
                h.ProductionMultiplier = r.F32();
                h.DisasterFrequency = r.F32();
                h.StartingMana = r.F32();
                h.ManaRegenMultiplier = r.F32();
                byte flags = r.U8();
                if ((flags & ~7) != 0) throw new InvalidDataException("Unknown header flags.");
                h.EnableWarfare = (flags & 1) != 0;
                h.EnableDisasters = (flags & 2) != 0;
                h.StrictMode = (flags & 4) != 0;
                if (!h.Validate(out string reason)) throw new InvalidDataException("Invalid replay header: " + reason + ".");

                int maxEntries = r.I32();
                if (maxEntries < 1 || maxEntries > MaxSerializedEntries) throw new InvalidDataException("Invalid entry cap.");
                long overflow = r.I64();
                if (overflow < 0) throw new InvalidDataException("Invalid overflow count.");
                int count = r.I32();
                if (count < 0 || count > maxEntries) throw new InvalidDataException("Invalid entry count.");
                if ((long)count * MinEntryBytes > r.Remaining) throw new InvalidDataException("Entry count exceeds the data size.");

                var log = new ReplayLog(h, maxEntries) { OverflowCount = overflow };
                log._entries.Capacity = count;
                for (int i = 0; i < count; i++)
                {
                    var e = new ReplayEntry();
                    e.Tick = r.I64();
                    byte power = r.U8();
                    if (power >= DefinedPowers.Length || !DefinedPowers[power]) throw new InvalidDataException("Unknown god power in entry " + i + ".");
                    e.Power = (GodPowerType)power;
                    int x = r.I32();
                    int y = r.I32();
                    e.Target = new Vec2i(x, y);
                    e.Radius = r.I32();
                    e.Parameter = r.I32();
                    e.Source = r.Text();
                    if (e.Tick < 0) throw new InvalidDataException("Negative tick in entry " + i + ".");
                    if (!log.Add(e)) throw new InvalidDataException("Entry " + i + " was rejected.");
                }

                int checkpoints = r.I32();
                if (checkpoints < 0 || checkpoints > MaxCheckpoints) throw new InvalidDataException("Invalid checkpoint count.");
                if ((long)checkpoints * CheckpointBytes > r.Remaining) throw new InvalidDataException("Checkpoint count exceeds the data size.");
                for (int i = 0; i < checkpoints; i++)
                {
                    long tick = r.I64();
                    ulong hash = r.U64();
                    if (!log.AddCheckpoint(tick, hash)) throw new InvalidDataException("Checkpoints must have non-negative, strictly increasing ticks.");
                }
                if (r.Remaining != 0) throw new InvalidDataException("Unexpected trailing data in replay.");
                return log;
            }
            catch (InvalidDataException) { throw; }
            catch (DecoderFallbackException) { throw new InvalidDataException("Replay contains invalid UTF-8 text."); }
            catch (Exception ex) when (ex is ArgumentException || ex is OverflowException || ex is IndexOutOfRangeException)
            {
                throw new InvalidDataException("Replay data is malformed (" + ex.GetType().Name + ").");
            }
        }

        // ================================================================== helpers

        private static bool IsDefinedPower(GodPowerType p) { int i = (int)p; return i >= 0 && i < DefinedPowers.Length && DefinedPowers[i]; }

        private static bool[] BuildDefinedPowers()
        {
            var table = new bool[256];
            foreach (GodPowerType p in Enum.GetValues(typeof(GodPowerType))) table[(byte)p] = true;
            return table;
        }

        private static ulong Checksum(byte[] data, int length)
        {
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < length; i++) h = unchecked((h ^ data[i]) * 1099511628211UL);
            return h;
        }

        /// <summary>Growable little-endian byte writer.</summary>
        private sealed class Writer
        {
            public byte[] Buffer;
            public int Length;

            public Writer(int capacity) { Buffer = new byte[Math.Max(64, capacity)]; }

            private void Ensure(int extra)
            {
                long need = (long)Length + extra;
                if (need <= Buffer.Length) return;
                long size = Math.Max(need, (long)Buffer.Length * 2);
                if (size > int.MaxValue - 64) size = need;
                if (size > int.MaxValue - 64) throw new InvalidOperationException("Replay too large to serialize.");
                Array.Resize(ref Buffer, (int)size);
            }

            public void U8(byte v) { Ensure(1); Buffer[Length++] = v; }
            public void U16(ushort v) { Ensure(2); Buffer[Length++] = (byte)v; Buffer[Length++] = (byte)(v >> 8); }
            public void I32(int v) { U32(unchecked((uint)v)); }
            public void U32(uint v)
            {
                Ensure(4);
                Buffer[Length++] = (byte)v; Buffer[Length++] = (byte)(v >> 8);
                Buffer[Length++] = (byte)(v >> 16); Buffer[Length++] = (byte)(v >> 24);
            }
            public void U64(ulong v) { U32(unchecked((uint)v)); U32(unchecked((uint)(v >> 32))); }
            public void I64(long v) { U64(unchecked((ulong)v)); }
            public void F32(float v) { I32(BitConverter.SingleToInt32Bits(v)); }
            public void Bytes(byte[] b) { Ensure(b.Length); System.Buffer.BlockCopy(b, 0, Buffer, Length, b.Length); Length += b.Length; }

            public void Text(string s)
            {
                s = SanitizeText(s);
                byte[] bytes = StrictUtf8.GetBytes(s);
                if (bytes.Length > MaxTextBytes) throw new InvalidOperationException("Text too long to serialize.");
                U8((byte)bytes.Length);
                Bytes(bytes);
            }

            public byte[] ToArray()
            {
                var result = new byte[Length];
                System.Buffer.BlockCopy(Buffer, 0, result, 0, Length);
                return result;
            }
        }

        /// <summary>Bounds-checked little-endian reader over a byte range; overruns raise InvalidDataException.</summary>
        private struct Reader
        {
            private readonly byte[] _data;
            private readonly int _end;
            private int _pos;

            public Reader(byte[] data, int start, int end) { _data = data; _pos = start; _end = end; }

            public int Remaining => _end - _pos;

            private void Need(int n) { if (n < 0 || _end - _pos < n) throw new InvalidDataException("Replay data is truncated."); }

            public byte U8() { Need(1); return _data[_pos++]; }
            public ushort U16() { Need(2); ushort v = (ushort)(_data[_pos] | (_data[_pos + 1] << 8)); _pos += 2; return v; }
            public uint U32()
            {
                Need(4);
                uint v = (uint)_data[_pos] | ((uint)_data[_pos + 1] << 8) | ((uint)_data[_pos + 2] << 16) | ((uint)_data[_pos + 3] << 24);
                _pos += 4;
                return v;
            }
            public int I32() => unchecked((int)U32());
            public ulong U64() { ulong lo = U32(); ulong hi = U32(); return lo | (hi << 32); }
            public long I64() => unchecked((long)U64());
            public float F32() => BitConverter.Int32BitsToSingle(I32());

            public string Text()
            {
                int len = U8();
                if (len > MaxTextBytes) throw new InvalidDataException("Replay string too long.");
                Need(len);
                string s = len == 0 ? "" : StrictUtf8.GetString(_data, _pos, len);
                _pos += len;
                if (!IsCleanText(s)) throw new InvalidDataException("Replay string is invalid (length or characters).");
                return s;
            }
        }
    }
}
