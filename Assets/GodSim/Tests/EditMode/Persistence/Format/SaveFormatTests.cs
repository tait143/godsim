using System;
using System.Collections.Generic;
using System.Diagnostics;
using GodSim.Core;
using GodSim.Core.Persistence;
using NUnit.Framework;

namespace GodSim.Tests.Persistence.Format
{
    public class SaveFormatTests
    {
        private GameDatabase _db;
        private WorldState _world;
        private byte[] _key;

        [SetUp]
        public void SetUp()
        {
            _db = SaveTestWorld.CreateDb();
            _world = SaveTestWorld.Build(_db);
            _key = new byte[32];
            var rng = new Rng(4242);
            for (int i = 0; i < _key.Length; i++) _key[i] = (byte)rng.Range(0, 256);
        }

        private SaveOptions Options(bool compress, bool signed) =>
            new SaveOptions { Compress = compress, HmacKey = signed ? _key : null, GameVersion = "1.2.3-test", CreatedUnixSeconds = 1763550000L };

        private static void Contains(string message, string fragment) =>
            Assert.IsTrue(message != null && message.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0, "expected '" + fragment + "' in: " + message);

        private static bool BytesEqual(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            for (int i = 0; i < a.Length; i++) if (a[i] != b[i]) return false;
            return true;
        }

        private static void AssertSame(WorldState expected, WorldState actual)
        {
            var diffs = DeepComparer.Compare(expected, actual);
            Assert.AreEqual(0, diffs.Count, "Round-trip differences:\n" + string.Join("\n", diffs));
        }

        // ================================================================ fixture sanity
        [Test]
        public void Fixture_IsFullyConsistent()
        {
            var issues = StateValidator.Validate(_world, _db);
            Assert.AreEqual(0, issues.Count, string.Join("\n", issues));
        }

        [Test]
        public void Fixture_SetsEveryPersistentFieldToANonDefaultValue()
        {
            // Without this, a field the serializer forgot would still compare equal (default == default).
            var missing = new List<string>();
            var s = _world;
            Covered(missing, s.People.Items, new Person(), "Person.IsAlive");
            Covered(missing, s.Buildings.Items, new Building(), "Building.IsDestroyed");
            Covered(missing, s.Settlements.Items, new Settlement(), "Settlement.IsAlive", "Settlement.AiLog");
            Covered(missing, s.Civilizations.Items, new Civilization());
            Covered(missing, s.Diplomacy.Armies.Items, new Army(), "Army.IsAlive");
            Covered(missing, new List<Relation>(s.Diplomacy.AllRelations), new Relation());
            Covered(missing, s.Diplomacy.Wars, new War());
            var orders = new List<BuildOrder>(); foreach (var st in s.Settlements.Items) orders.AddRange(st.BuildQueue);
            Covered(missing, orders, new BuildOrder());
            Covered(missing, s.Chronicle.Entries, default(ChronicleEntry));
            var stocks = new List<Stockpile>(); var mods = new List<ModifierSet>();
            foreach (var st in s.Settlements.Items) stocks.Add(st.Stockpile);
            foreach (var c in s.Civilizations.Items) mods.Add(c.Modifiers);
            Covered(missing, stocks, new Stockpile());
            Covered(missing, mods, new ModifierSet());
            Covered(missing, new[] { s.God }, new GodState());
            Covered(missing, new[] { s.Weather }, new WeatherState());
            Covered(missing, new[] { s.Stats }, new WorldStats());
            Covered(missing, new[] { s.Ids }, new IdAllocator());
            Covered(missing, new[] { s.Map }, new WorldMap(16, 16));
            Covered(missing, new[] { s }, new WorldState(0, new WorldMap(16, 16)),
                "WorldState.PendingPersonRemovals", "WorldState.PendingBuildingRemovals", "WorldState.PendingSettlementRemovals", "WorldState.PendingArmyRemovals");
            Assert.AreEqual(0, missing.Count, "Fields never set to a non-default value by the fixture: " + string.Join(", ", missing));
        }

        private static void Covered<T>(List<string> missing, IEnumerable<T> instances, T defaults, params string[] exempt)
        {
            var ex = new HashSet<string>(exempt);
            foreach (var f in DeepComparer.Fields(typeof(T)))
            {
                string key = typeof(T).Name + "." + f.Name;
                if (ex.Contains(key)) continue;
                bool any = false;
                foreach (var inst in instances)
                    if (!DeepComparer.AreEqual(f.GetValue(inst), f.GetValue(defaults))) { any = true; break; }
                if (!any) missing.Add(key);
            }
        }

        // ================================================================ round trips
        [TestCase(true, true)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(false, false)]
        public void RoundTrip_PreservesEveryField(bool compress, bool signed)
        {
            var opts = Options(compress, signed);
            byte[] data = SaveSerializer.Serialize(_world, opts);
            var loaded = SaveSerializer.Deserialize(data, _db, opts);
            AssertSame(_world, loaded);
            Assert.AreEqual(0, StateValidator.Validate(loaded, _db).Count);
            // Canonical encoding: re-saving the loaded world yields identical bytes.
            Assert.IsTrue(BytesEqual(data, SaveSerializer.Serialize(loaded, opts)), "re-serialization differs");
        }

        [Test]
        public void RoundTrip_LoadedWorldKeepsSimulatingIdentically()
        {
            var opts = Options(true, false);
            var loaded = SaveSerializer.Deserialize(SaveSerializer.Serialize(_world, opts), _db, opts);
            for (int i = 0; i < 50; i++) Assert.AreEqual(_world.Rng.NextULong(), loaded.Rng.NextULong());
            Assert.AreEqual(_world.Ids.Person(), loaded.Ids.Person());
        }

        [Test]
        public void Compression_ShrinksTheFile()
        {
            int raw = SaveSerializer.Serialize(_world, Options(false, false)).Length;
            int gz = SaveSerializer.Serialize(_world, Options(true, false)).Length;
            Assert.IsTrue(gz < raw, gz + " >= " + raw);
        }

        [Test]
        public void NullData_And_Garbage_AreCorrupt()
        {
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(null, _db, null));
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(new byte[0], _db, null));
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(new byte[200], _db, null));
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.ReadHeader(null));
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.ReadHeader(new byte[10]));
        }

        // ================================================================ header
        [Test]
        public void ReadHeader_ReturnsMetadataWithoutParsingThePayload()
        {
            var opts = Options(true, true);
            byte[] data = SaveSerializer.Serialize(_world, opts);
            data[data.Length - 5] ^= 0x5A; // payload is now garbage, header untouched
            var h = SaveSerializer.ReadHeader(data);
            Assert.AreEqual(1, h.FormatVersion);
            Assert.AreEqual(WorldState.SchemaVersion, h.SchemaVersion);
            Assert.IsTrue(h.IsCompressed);
            Assert.IsTrue(h.IsSigned);
            Assert.AreEqual(1763550000L, h.CreatedUnixSeconds);
            Assert.AreEqual("1.2.3-test", h.GameVersion);
            Assert.AreEqual(_world.Seed, h.Seed);
            Assert.AreEqual(_world.Tick, h.Tick);
            Assert.AreEqual(_world.LivingPopulation, h.Population);
            Assert.AreEqual(data.Length - h.HeaderLength - SaveSerializer.TagLength, h.PayloadLength);
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(data, _db, opts));
        }

        [Test]
        public void Header_BadMagicVersionFlagsOrSchema_AreRejected()
        {
            byte[] good = SaveSerializer.Serialize(_world, Options(false, false));
            int hl = SaveSurgery.HeaderLength(good);
            Action<int, byte> expectReject = (offset, value) =>
            {
                var d = (byte[])good.Clone();
                d[offset] = value;
                SaveSurgery.Retag(d, hl, null);
                Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(d, _db, null), "offset " + offset);
            };
            expectReject(0, (byte)'X');   // magic
            expectReject(4, 2);           // format version 2
            expectReject(6, 0x80);        // unknown flag bit
            expectReject(8, (byte)(WorldState.SchemaVersion + 1)); // future schema version
            expectReject(8, 0);           // schema version 0 (unsupported)
            expectReject(20, 65);         // game version length > 64
            var ex = Assert.Throws<SaveCorruptException>(() => { var d = (byte[])good.Clone(); d[8] = 9; SaveSurgery.Retag(d, hl, null); SaveSerializer.Deserialize(d, _db, null); });
            Contains(ex.Message, "schema");
        }

        [Test]
        public void HeaderAndPayloadDisagreement_IsRejected()
        {
            byte[] good = SaveSerializer.Serialize(_world, Options(false, false));
            int hl = SaveSurgery.HeaderLength(good);
            var d = (byte[])good.Clone();
            d[hl - 8] ^= 1; // population (i32 before the payload length)
            SaveSurgery.Retag(d, hl, null);
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(d, _db, null));
            d = (byte[])good.Clone();
            d[hl - 16] ^= 1; // tick
            SaveSurgery.Retag(d, hl, null);
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(d, _db, null));
        }

        // ================================================================ integrity
        [TestCase(true)]
        [TestCase(false)]
        public void FlippedPayloadByte_IsRejected(bool signed)
        {
            var opts = Options(true, signed);
            byte[] data = SaveSerializer.Serialize(_world, opts);
            int po = SaveSurgery.PayloadOffset(data);
            foreach (int at in new[] { po, po + (data.Length - po) / 2, data.Length - 1 })
            {
                var d = (byte[])data.Clone();
                d[at] ^= 0x01;
                var ex = Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(d, _db, opts));
                Contains(ex.Message, signed ? "signature" : "checksum");
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void FlippedHeaderOrTagByte_IsRejected(bool signed)
        {
            var opts = Options(false, signed);
            byte[] data = SaveSerializer.Serialize(_world, opts);
            int hl = SaveSurgery.HeaderLength(data);
            foreach (int at in new[] { 12, 22, hl - 20, hl, hl + 31 }) // created, game version, seed, tag start/end
            {
                var d = (byte[])data.Clone();
                d[at] ^= 0x10;
                Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(d, _db, opts), "offset " + at);
            }
        }

        [Test]
        public void WrongKey_And_MissingKey_AreRejected()
        {
            byte[] data = SaveSerializer.Serialize(_world, Options(true, true));
            var wrong = (byte[])_key.Clone();
            wrong[0] ^= 0xFF;
            var ex = Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(data, _db, new SaveOptions { HmacKey = wrong }));
            Contains(ex.Message, "signature");
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(data, _db, new SaveOptions()));
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(data, _db, new SaveOptions { HmacKey = new byte[0] }));
            Assert.IsNotNull(SaveSerializer.Deserialize(data, _db, new SaveOptions { HmacKey = _key }));
        }

        [Test]
        public void RequireSignature_RejectsUnsignedAndDowngradedFiles()
        {
            var strict = new SaveOptions { HmacKey = _key, RequireSignature = true };
            byte[] unsigned = SaveSerializer.Serialize(_world, Options(true, false));
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(unsigned, _db, strict));

            // Downgrade attack: strip the Signed flag from a signed file and recompute a plain SHA-256 tag.
            byte[] signed = SaveSerializer.Serialize(_world, Options(true, true));
            int hl = SaveSurgery.HeaderLength(signed);
            signed[6] &= unchecked((byte)~(byte)SaveFormatFlags.Signed);
            SaveSurgery.Retag(signed, hl, null);
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(signed, _db, strict));
            Assert.IsNotNull(SaveSerializer.Deserialize(signed, _db, new SaveOptions { HmacKey = _key }), "documented: without RequireSignature, unsigned files are accepted");

            Assert.Throws<ArgumentException>(() => SaveSerializer.Serialize(_world, new SaveOptions { RequireSignature = true }));
            Assert.IsNotNull(SaveSerializer.Deserialize(SaveSerializer.Serialize(_world, strict), _db, strict));
        }

        // ================================================================ truncation, size limits
        [Test]
        public void TruncatedFiles_AtFiftyOffsets_AreRejected()
        {
            foreach (bool compress in new[] { true, false })
            {
                byte[] data = SaveSerializer.Serialize(_world, Options(compress, false));
                byte[] header = SaveSurgery.Header(data);
                byte[] payload = SaveSurgery.Payload(data);
                for (int i = 0; i < 50; i++)
                {
                    // 1) Plain truncation of the file.
                    int len = (int)((long)i * (data.Length - 1) / 49);
                    var cut = new byte[len];
                    Buffer.BlockCopy(data, 0, cut, 0, len);
                    Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(cut, _db, null), "file cut at " + len);
                    // 2) Truncated payload with a consistent header and a valid tag (reaches the parser).
                    int plen = (int)((long)i * (payload.Length - 1) / 49);
                    var p = new byte[plen];
                    Buffer.BlockCopy(payload, 0, p, 0, plen);
                    var rebuilt = SaveSurgery.Rebuild(header, p, null);
                    Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(rebuilt, _db, null), "payload cut at " + plen);
                }
            }
        }

        [Test]
        public void TrailingBytesAfterPayload_AreRejected()
        {
            byte[] data = SaveSerializer.Serialize(_world, Options(false, false));
            var padded = SaveSurgery.Rebuild(SaveSurgery.Header(data), SaveSurgery.Concat(SaveSurgery.Payload(data), new byte[] { 0 }), null);
            var ex = Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(padded, _db, null));
            Contains(ex.Message, "trailing");
        }

        [Test]
        public void MaxCompressedBytes_IsCheckedFirst()
        {
            byte[] data = SaveSerializer.Serialize(_world, Options(true, false));
            var ex = Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(data, _db, new SaveOptions { MaxCompressedBytes = data.Length - 1 }));
            Contains(ex.Message, "too large");
            Assert.Throws<InvalidOperationException>(() => SaveSerializer.Serialize(_world, new SaveOptions { MaxCompressedBytes = 100 }));
        }

        [Test]
        public void MaxUncompressedBytes_FusesDecompression()
        {
            byte[] gz = SaveSerializer.Serialize(_world, Options(true, false));
            var ex = Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(gz, _db, new SaveOptions { MaxUncompressedBytes = 4096 }));
            Contains(ex.Message, "limit");
            byte[] raw = SaveSerializer.Serialize(_world, Options(false, false));
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(raw, _db, new SaveOptions { MaxUncompressedBytes = 4096 }));
        }

        [Test]
        public void DecompressionBomb_600MB_IsRejectedQuickly()
        {
            byte[] template = SaveSerializer.Serialize(_world, Options(true, false));
            var sw = new Stopwatch();
            // (a) 600 MB of zeros: rejected by the parser on the first section tag.
            byte[] zeros = SaveSurgery.ZeroBomb(600L * 1024 * 1024, null);
            Assert.IsTrue(zeros.Length < 4 * 1024 * 1024, "bomb should be compact, was " + zeros.Length);
            byte[] zeroFile = SaveSurgery.Rebuild(SaveSurgery.Header(template), zeros, null);
            sw.Start();
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(zeroFile, _db, new SaveOptions()));
            sw.Stop();
            // (b) A plaintext that parses as a valid 1024x1024 map of zeros for ~35 MB, then 600 MB more:
            //     only the MaxUncompressedBytes fuse can stop it before the parser does.
            var prefix = new System.IO.MemoryStream();
            var w = new System.IO.BinaryWriter(prefix);
            w.Write(System.Text.Encoding.ASCII.GetBytes("WRLD")); w.Write(0UL); w.Write(0L);
            w.Write(System.Text.Encoding.ASCII.GetBytes("MAP_")); w.Write(1024); w.Write(1024); w.Write(1024 * 1024);
            w.Flush();
            byte[] bomb = SaveSurgery.ZeroBomb(600L * 1024 * 1024, prefix.ToArray());
            byte[] file = SaveSurgery.Rebuild(SaveSurgery.Header(template), bomb, null);
            sw.Start();
            var ex = Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(file, _db, new SaveOptions { MaxUncompressedBytes = 8 * 1024 * 1024 }));
            Contains(ex.Message, "limit");
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(file, _db, new SaveOptions()));
            sw.Stop();
            Assert.IsTrue(sw.ElapsedMilliseconds < 3000, "loading took " + sw.ElapsedMilliseconds + " ms");
        }

        // ================================================================ hostile counts and values
        [TestCase(int.MinValue)]
        [TestCase(int.MaxValue)]
        [TestCase(-1)]
        [TestCase(2000001)]
        public void HugeDeclaredPeopleCount_IsRejectedWithoutAllocating(int count)
        {
            byte[] data = SaveSerializer.Serialize(_world, Options(false, false));
            int hl = SaveSurgery.HeaderLength(data);
            int at = SaveSurgery.IndexOf(data, "PEOP", hl + SaveSerializer.TagLength);
            Assert.IsTrue(at > 0);
            SaveSurgery.WriteInt32(data, at + 4, count);
            SaveSurgery.Retag(data, hl, null);
            var sw = Stopwatch.StartNew();
            var ex = Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(data, _db, null));
            Assert.IsTrue(sw.ElapsedMilliseconds < 1000, "took " + sw.ElapsedMilliseconds + " ms");
            Contains(ex.Message, "count");
        }

        [Test]
        public void HugeDeclaredCounts_InEverySection_AreRejectedQuickly()
        {
            byte[] data = SaveSerializer.Serialize(_world, Options(false, false));
            int hl = SaveSurgery.HeaderLength(data);
            var sw = Stopwatch.StartNew();
            foreach (var sec in new[] { "BLDG", "SETL", "CIVS", "DIPL", "CHRN", "MEMW" })
            {
                var d = (byte[])data.Clone();
                int at = SaveSurgery.IndexOf(d, sec, hl + SaveSerializer.TagLength);
                Assert.IsTrue(at > 0, sec);
                SaveSurgery.WriteInt32(d, at + 4, int.MinValue);
                SaveSurgery.Retag(d, hl, null);
                Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(d, _db, null), sec);
                SaveSurgery.WriteInt32(d, at + 4, 1000000); // within limits but the data is not there
                SaveSurgery.Retag(d, hl, null);
                Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(d, _db, null), sec);
            }
            Assert.IsTrue(sw.ElapsedMilliseconds < 3000, "took " + sw.ElapsedMilliseconds + " ms");
        }

        [Test]
        public void HugeMapDimensions_AreRejected()
        {
            byte[] data = SaveSerializer.Serialize(_world, Options(false, false));
            int hl = SaveSurgery.HeaderLength(data);
            int at = SaveSurgery.IndexOf(data, "MAP_", hl + SaveSerializer.TagLength);
            foreach (var wh in new[] { new[] { 65536, 65536 }, new[] { 1025, 16 }, new[] { -64, -64 }, new[] { 128, 64 } })
            {
                var d = (byte[])data.Clone();
                SaveSurgery.WriteInt32(d, at + 4, wh[0]);
                SaveSurgery.WriteInt32(d, at + 8, wh[1]);
                SaveSurgery.Retag(d, hl, null);
                Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(d, _db, null), wh[0] + "x" + wh[1]);
            }
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void NonFiniteFloat_IsRejected(float bad)
        {
            byte[] data = SaveSerializer.Serialize(_world, Options(false, false));
            int hl = SaveSurgery.HeaderLength(data);
            var god = (byte[])data.Clone();
            SaveSurgery.WriteSingle(god, SaveSurgery.IndexOf(god, "GOD_", hl + 32) + 4, bad); // GodState.Mana
            SaveSurgery.Retag(god, hl, null);
            var ex = Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(god, _db, null));
            Contains(ex.Message, "non-finite");
            var map = (byte[])data.Clone();
            SaveSurgery.WriteSingle(map, SaveSurgery.IndexOf(map, "MAP_", hl + 32) + 16, bad); // first elevation
            SaveSurgery.Retag(map, hl, null);
            Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(map, _db, null));
        }

        [Test]
        public void InvalidEnumAndBool_AreRejected()
        {
            byte[] data = SaveSerializer.Serialize(_world, Options(false, false));
            int hl = SaveSurgery.HeaderLength(data);
            var d = (byte[])data.Clone();
            d[SaveSurgery.IndexOf(d, "WTHR", hl + 32) + 4] = 9; // Season 9 does not exist
            SaveSurgery.Retag(d, hl, null);
            Contains(Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(d, _db, null)).Message, "Season");
            var map = (byte[])data.Clone();
            int n = _world.Map.TileCount;
            map[SaveSurgery.IndexOf(map, "MAP_", hl + 32) + 16 + 16 * n] = 200; // first biome
            SaveSurgery.Retag(map, hl, null);
            Contains(Assert.Throws<SaveCorruptException>(() => SaveSerializer.Deserialize(map, _db, null)).Message, "Biome");
        }

        [Test]
        public void NonFiniteFloatInState_IsSavedAsZero()
        {
            _world.God.Mana = float.NaN;
            _world.Map.Elevation[5] = float.PositiveInfinity;
            var loaded = SaveSerializer.Deserialize(SaveSerializer.Serialize(_world, Options(true, false)), _db, null);
            Assert.AreEqual(0f, loaded.God.Mana);
            Assert.AreEqual(0f, loaded.Map.Elevation[5]);
        }

        // ================================================================ fuzzing
        [Test]
        public void Fuzz_300SeededMutations_OnlyCorruptOrConsistentLoads()
        {
            var bases = new[]
            {
                SaveSerializer.Serialize(_world, Options(false, false)),
                SaveSerializer.Serialize(_world, Options(true, false)),
                SaveSerializer.Serialize(_world, Options(false, true)),
            };
            var opts = new SaveOptions { HmacKey = _key };
            int[] interesting = { 0, -1, 1, 2, 3, 255, 65535, int.MaxValue, int.MinValue, 1000000, 2000001, 1024 * 1024 + 1 };
            var rng = new Rng(20261119);
            var sw = Stopwatch.StartNew();
            int loadedCount = 0, rejected = 0;
            for (int iter = 0; iter < 300; iter++)
            {
                int which = iter % 3;
                byte[] d = (byte[])bases[which].Clone();
                int hl = SaveSurgery.HeaderLength(d);
                int po = hl + SaveSerializer.TagLength;
                int entities = which == 1 ? po : Math.Max(po, SaveSurgery.IndexOf(d, "PEOP", po)); // non-map part of raw payloads
                int mode = rng.Range(0, 4);
                int edits = rng.Range(1, 5);
                for (int e = 0; e < edits; e++)
                {
                    int at = mode == 3 ? rng.Range(0, d.Length) : rng.Range(rng.Chance(0.5f) ? entities : po, d.Length);
                    if (mode == 1 && at + 4 <= d.Length) SaveSurgery.WriteInt32(d, at, interesting[rng.Range(0, interesting.Length)]);
                    else if (mode == 2) d[at] ^= (byte)(1 << rng.Range(0, 8));
                    else d[at] = (byte)rng.Range(0, 256);
                }
                bool retag = rng.Chance(0.8f);
                if (retag) SaveSurgery.Retag(d, hl, which == 2 ? _key : null);
                try
                {
                    var loaded = SaveSerializer.Deserialize(d, _db, opts);
                    loadedCount++;
                    var issues = StateValidator.Validate(loaded, _db);
                    Assert.AreEqual(0, issues.Count, "iteration " + iter + ": loaded state not consistent after repair:\n" + string.Join("\n", issues));
                }
                catch (SaveCorruptException) { rejected++; }
                catch (Exception ex) { Assert.Fail("iteration " + iter + " (mode " + mode + ") leaked " + ex.GetType().Name + ": " + ex.Message); }
            }
            Assert.IsTrue(sw.Elapsed.TotalSeconds < 10, "fuzzing took " + sw.Elapsed.TotalSeconds + " s");
            Assert.AreEqual(300, loadedCount + rejected);
            Assert.IsTrue(rejected > 0 && loadedCount > 0, "loaded " + loadedCount + ", rejected " + rejected);
        }

        // ================================================================ save-side contract
        [Test]
        public void PendingRemovals_MakeSerializeThrow()
        {
            var ctx = SaveTestWorld.BuildContext(_db);
            var victim = ctx.State.People.Items[0];
            ctx.Ops.KillPerson(victim, DeathCause.Accident);
            Assert.Throws<InvalidOperationException>(() => SaveSerializer.Serialize(ctx.State, null));
            ctx.Ops.FlushRemovals();
            Assert.IsNotNull(SaveSerializer.Deserialize(SaveSerializer.Serialize(ctx.State, null), _db, null));
        }

        [Test]
        public void SerializeArguments_AreValidated()
        {
            Assert.Throws<ArgumentNullException>(() => SaveSerializer.Serialize(null, null));
            Assert.Throws<ArgumentException>(() => SaveSerializer.Serialize(_world, new SaveOptions { GameVersion = new string('v', 65) }));
            _world.Settlements.Items[0].Memory[new string('k', 513)] = 1f;
            Assert.Throws<InvalidOperationException>(() => SaveSerializer.Serialize(_world, null));
        }

        [Test]
        public void NullOptions_UseDefaults()
        {
            var loaded = SaveSerializer.Deserialize(SaveSerializer.Serialize(_world, null), null, null);
            AssertSame(_world, loaded);
            Assert.AreEqual("0.1.0", SaveSerializer.ReadHeader(SaveSerializer.Serialize(_world, null)).GameVersion);
        }

        [Test]
        public void Migrator_CoversEverySupportedSchema()
        {
            Assert.AreEqual(SaveMigrator.CurrentSchemaVersion - SaveMigrator.MinSupportedSchemaVersion, SaveMigrator.StepCount);
            Assert.IsTrue(SaveMigrator.IsSupported(WorldState.SchemaVersion));
            Assert.IsFalse(SaveMigrator.IsSupported(0));
            Assert.IsFalse(SaveMigrator.IsSupported(WorldState.SchemaVersion + 1));
            Assert.Throws<SaveCorruptException>(() => SaveMigrator.Upgrade(_world, _db, WorldState.SchemaVersion + 1));
            SaveMigrator.Upgrade(_world, _db, WorldState.SchemaVersion); // no-op
        }
    }
}
