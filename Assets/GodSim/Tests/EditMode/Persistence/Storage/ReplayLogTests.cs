using System;
using System.Collections.Generic;
using System.IO;
using GodSim.Core;
using GodSim.Core.Persistence;
using GodSim.Tests.Shared;
using NUnit.Framework;

namespace GodSim.Tests.Persistence.Storage
{
    public class ReplayLogTests
    {
        private static ReplayLog Sample()
        {
            var config = new SimConfig { Seed = 987654321UL, MapWidth = 96, MapHeight = 80, Preset = WorldPreset.Archipelago, GrowthMultiplier = 1.5f, EnableDisasters = false }.Sanitize();
            var log = new ReplayLog(ReplayHeader.FromConfig(config, 0, 0xABCDEF0123456789UL, "GodSim 0.1"));
            log.Record(new GodCommand { Power = GodPowerType.Rain, Target = new Vec2i(3, 4), Radius = 2, Source = "local" }, 5);
            log.Record(new GodCommand { Power = GodPowerType.SpawnTribe, Target = new Vec2i(10, 11), Radius = 1, Parameter = (int)RaceId.Elf, Source = "network:peeré" }, 5);
            log.Record(new GodCommand { Power = GodPowerType.Meteor, Target = new Vec2i(-1, 70), Radius = 6, Source = null }, 9);
            log.AddCheckpoint(0, 111UL);
            log.AddCheckpoint(10, 222UL);
            return log;
        }

        [Test]
        public void AttachRecordsOnlyAcceptedCommandsWithProvidedTick()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db, 32, 3);
            var queue = new CommandQueue(8, new PermissiveCommandValidator());
            int otherObserverCalls = 0;
            queue.OnAccepted += c => otherObserverCalls++;

            var log = new ReplayLog();
            long fakeTick = 42;
            log.Attach(queue, () => fakeTick);
            Assert.IsTrue(log.IsAttached);

            Assert.IsTrue(queue.TryEnqueue(new GodCommand { Power = GodPowerType.Bless, Target = new Vec2i(5, 5), Radius = 3 }, ctx.State, db, out _));
            Assert.IsFalse(queue.TryEnqueue(new GodCommand { Power = GodPowerType.Bless, Target = new Vec2i(500, 5) }, ctx.State, db, out _), "out of bounds is rejected");
            fakeTick = 43;
            Assert.IsTrue(queue.TryEnqueue(new GodCommand { Power = GodPowerType.SpawnTribe, Target = new Vec2i(1, 2), Parameter = 2, Source = "network:abc" }, ctx.State, db, out _));

            Assert.AreEqual(2, log.Entries.Count);
            Assert.AreEqual(2, otherObserverCalls, "existing observers keep working");
            Assert.AreEqual(42L, log.Entries[0].Tick);
            Assert.AreEqual(GodPowerType.Bless, log.Entries[0].Power);
            Assert.AreEqual(new Vec2i(5, 5), log.Entries[0].Target);
            Assert.AreEqual(3, log.Entries[0].Radius);
            Assert.AreEqual("local", log.Entries[0].Source);
            Assert.AreEqual(43L, log.Entries[1].Tick);
            Assert.AreEqual(2, log.Entries[1].Parameter);
            Assert.AreEqual("network:abc", log.Entries[1].Source);

            // Re-attaching does not double-record; detach stops recording.
            log.Attach(queue, () => 50);
            Assert.IsTrue(queue.TryEnqueue(new GodCommand { Power = GodPowerType.Heal, Target = new Vec2i(2, 2) }, ctx.State, db, out _));
            Assert.AreEqual(3, log.Entries.Count);
            log.Detach();
            Assert.IsFalse(log.IsAttached);
            Assert.IsTrue(queue.TryEnqueue(new GodCommand { Power = GodPowerType.Heal, Target = new Vec2i(2, 2) }, ctx.State, db, out _));
            Assert.AreEqual(3, log.Entries.Count);
            Assert.AreEqual(4, otherObserverCalls);
        }

        [Test]
        public void ThrowingTickProviderNeverBreaksTheQueue()
        {
            var db = TestDb.CreateStub();
            var ctx = TestWorlds.CreateContext(db, 32, 3);
            ctx.State.Tick = 7;
            var queue = new CommandQueue(8, null);
            var log = new ReplayLog();
            log.Attach(queue, () => throw new InvalidOperationException("boom"));
            Assert.IsTrue(queue.TryEnqueue(new GodCommand { Power = GodPowerType.Rain, Target = new Vec2i(1, 1) }, ctx.State, db, out _));
            Assert.AreEqual(1, log.Entries.Count);
            Assert.AreEqual(7L, log.Entries[0].Tick, "falls back to the command's issued tick");
            Assert.Throws<ArgumentNullException>(() => log.Attach(null, () => 0));
            Assert.Throws<ArgumentNullException>(() => log.Attach(queue, null));
        }

        [Test]
        public void BytesRoundTrip()
        {
            var log = Sample();
            byte[] bytes = log.ToBytes();
            var back = ReplayLog.FromBytes(bytes);

            Assert.AreEqual(log.Header.Seed, back.Header.Seed);
            Assert.AreEqual(96, back.Header.MapWidth);
            Assert.AreEqual(80, back.Header.MapHeight);
            Assert.AreEqual(WorldPreset.Archipelago, back.Header.Preset);
            Assert.AreEqual(1.5f, back.Header.GrowthMultiplier);
            Assert.IsFalse(back.Header.EnableDisasters);
            Assert.IsTrue(back.Header.EnableWarfare);
            Assert.AreEqual(log.Header.StartingRaces, back.Header.StartingRaces);
            Assert.AreEqual(0xABCDEF0123456789UL, back.Header.StartStateHash);
            Assert.AreEqual("GodSim 0.1", back.Header.GameVersion);
            Assert.AreEqual(log.MaxEntries, back.MaxEntries);

            Assert.AreEqual(log.Entries.Count, back.Entries.Count);
            for (int i = 0; i < log.Entries.Count; i++)
            {
                Assert.AreEqual(log.Entries[i].Tick, back.Entries[i].Tick);
                Assert.AreEqual(log.Entries[i].Power, back.Entries[i].Power);
                Assert.AreEqual(log.Entries[i].Target, back.Entries[i].Target);
                Assert.AreEqual(log.Entries[i].Radius, back.Entries[i].Radius);
                Assert.AreEqual(log.Entries[i].Parameter, back.Entries[i].Parameter);
                Assert.AreEqual(log.Entries[i].Source, back.Entries[i].Source);
            }
            Assert.AreEqual("", back.Entries[2].Source, "null source is stored as empty");
            Assert.AreEqual(2, back.Checkpoints.Count);
            Assert.IsTrue(back.TryGetCheckpoint(10, out ulong hash));
            Assert.AreEqual(222UL, hash);
            Assert.IsFalse(back.TryGetCheckpoint(5, out _));

            Assert.AreEqual(bytes, back.ToBytes(), "serialization is canonical");

            var config = back.Header.ToConfig();
            Assert.AreEqual(987654321UL, config.Seed);
            Assert.AreEqual(96, config.MapWidth);
            Assert.AreEqual(WorldPreset.Archipelago, config.Preset);
        }

        [Test]
        public void ForTickReturnsEntriesOfThatTickInOrder()
        {
            var log = Sample();
            var at5 = new List<ReplayEntry>(log.ForTick(5));
            Assert.AreEqual(2, at5.Count);
            Assert.AreEqual(GodPowerType.Rain, at5[0].Power);
            Assert.AreEqual(GodPowerType.SpawnTribe, at5[1].Power);
            Assert.AreEqual(1, new List<ReplayEntry>(log.ForTick(9)).Count);
            Assert.AreEqual(0, new List<ReplayEntry>(log.ForTick(6)).Count);

            var buffer = new List<ReplayEntry>();
            Assert.AreEqual(2, log.CopyForTick(5, buffer));
            Assert.AreEqual(GodPowerType.SpawnTribe, buffer[1].Power);

            // Out-of-order ticks still work (linear fallback).
            log.Record(new GodCommand { Power = GodPowerType.Heal }, 5);
            Assert.AreEqual(3, new List<ReplayEntry>(log.ForTick(5)).Count);

            var cmd = at5[1].ToCommand("replay");
            Assert.AreEqual(GodPowerType.SpawnTribe, cmd.Power);
            Assert.AreEqual((int)RaceId.Elf, cmd.Parameter);
            Assert.AreEqual("replay", cmd.Source);
            Assert.AreEqual(5L, cmd.IssuedTick);
        }

        [Test]
        public void CorruptBytesThrowInvalidData()
        {
            byte[] good = Sample().ToBytes();
            Assert.Throws<ArgumentNullException>(() => ReplayLog.FromBytes(null));
            Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(new byte[0]));
            Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(new byte[] { 1, 2, 3 }));

            for (int len = 0; len < good.Length; len += 7)
            {
                var truncated = new byte[len];
                Array.Copy(good, truncated, len);
                Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(truncated), "truncated at " + len);
            }
            for (int i = 0; i < good.Length; i++)
            {
                var flipped = (byte[])good.Clone();
                flipped[i] ^= 0x5A;
                Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(flipped), "flipped byte " + i);
            }
            var extended = new byte[good.Length + 1];
            Array.Copy(good, extended, good.Length);
            Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(extended));
        }

        [Test]
        public void StructurallyInvalidButChecksummedDataIsRejected()
        {
            // Entry layout after the header: tick(8) power(1) x(4) y(4) radius(4) param(4) srcLen(1) src.
            var log = new ReplayLog(new ReplayHeader());
            log.Record(new GodCommand { Power = GodPowerType.Rain, Source = "" }, 1);
            byte[] good = log.ToBytes();
            Assert.DoesNotThrow(() => ReplayLog.FromBytes(good));

            int entryStart = good.Length - 8 /*checksum*/ - 4 /*checkpoint count*/ - 26 /*one entry*/;
            int countPos = entryStart - 4;
            Assert.AreEqual(1, BitConverter.ToInt32(good, countPos), "layout assumption");

            // Unknown enum value.
            var badPower = (byte[])good.Clone();
            badPower[entryStart + 8] = 200;
            Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(Reseal(badPower)));

            // Entry count beyond the bound / beyond the data.
            var hugeCount = (byte[])good.Clone();
            WriteInt(hugeCount, countPos, ReplayLog.MaxSerializedEntries + 1);
            Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(Reseal(hugeCount)));
            WriteInt(hugeCount, countPos, 900000);
            Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(Reseal(hugeCount)));
            WriteInt(hugeCount, countPos, -1);
            Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(Reseal(hugeCount)));

            // Overlong string length.
            var longString = (byte[])good.Clone();
            longString[entryStart + 25] = 255;
            Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(Reseal(longString)));

            // Negative tick.
            var negTick = (byte[])good.Clone();
            negTick[entryStart + 7] = 0x80;
            Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(Reseal(negTick)));
        }

        [Test]
        public void InvalidUtf8AndControlCharactersAreRejected()
        {
            var log = new ReplayLog(new ReplayHeader());
            log.Record(new GodCommand { Power = GodPowerType.Rain, Source = "ab" }, 1);
            byte[] good = log.ToBytes();
            int src = good.Length - 8 - 4 - 2; // the two source bytes
            var badUtf8 = (byte[])good.Clone();
            badUtf8[src] = 0xC3; badUtf8[src + 1] = 0x28;
            Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(Reseal(badUtf8)));
            var control = (byte[])good.Clone();
            control[src] = 0x07;
            Assert.Throws<InvalidDataException>(() => ReplayLog.FromBytes(Reseal(control)));
        }

        [Test]
        public void RecordingCapIsRespectedAndOverflowCounted()
        {
            var log = new ReplayLog(null, 3);
            for (int i = 0; i < 5; i++) log.Record(new GodCommand { Power = GodPowerType.Bless, Target = new Vec2i(i, i) }, i);
            Assert.AreEqual(3, log.Entries.Count);
            Assert.AreEqual(2L, log.OverflowCount);
            Assert.AreEqual(new Vec2i(2, 2), log.Entries[2].Target, "the earliest commands are kept");
            var back = ReplayLog.FromBytes(log.ToBytes());
            Assert.AreEqual(3, back.MaxEntries);
            Assert.AreEqual(2L, back.OverflowCount);
            Assert.AreEqual(3, back.Entries.Count);

            Assert.AreEqual(ReplayLog.MaxSerializedEntries, new ReplayLog(null, int.MaxValue).MaxEntries);
            Assert.AreEqual(1, new ReplayLog(null, -4).MaxEntries);
        }

        [Test]
        public void InvalidCommandsAreNotRecordedAndSourcesAreSanitized()
        {
            var log = new ReplayLog();
            Assert.IsFalse(log.Record(null, 1));
            Assert.IsFalse(log.Record(new GodCommand { Power = (GodPowerType)250 }, 1));
            Assert.IsFalse(log.Record(new GodCommand { Power = GodPowerType.Rain }, -1));
            Assert.AreEqual(0, log.Entries.Count);
            Assert.AreEqual(0L, log.OverflowCount);

            Assert.IsTrue(log.Record(new GodCommand { Power = GodPowerType.Rain, Source = new string('s', 500) }, 1));
            Assert.AreEqual(ReplayLog.MaxTextLength, log.Entries[0].Source.Length);
            Assert.IsTrue(log.Record(new GodCommand { Power = GodPowerType.Rain, Source = "a\nb\u0000\uD800" }, 1));
            Assert.AreEqual("a?b??", log.Entries[1].Source);
            string emoji = new string('x', 63) + "😀";
            Assert.IsTrue(log.Record(new GodCommand { Power = GodPowerType.Rain, Source = emoji }, 1));
            Assert.AreEqual(new string('x', 63), log.Entries[2].Source, "surrogate pairs are never split");

            var back = ReplayLog.FromBytes(log.ToBytes());
            for (int i = 0; i < log.Entries.Count; i++) Assert.AreEqual(log.Entries[i].Source, back.Entries[i].Source);
        }

        [Test]
        public void CheckpointsMustIncrease()
        {
            var log = new ReplayLog();
            Assert.IsTrue(log.AddCheckpoint(0, 1));
            Assert.IsFalse(log.AddCheckpoint(0, 2));
            Assert.IsFalse(log.AddCheckpoint(-3, 2));
            Assert.IsTrue(log.AddCheckpoint(96, 3));
            Assert.AreEqual(2, log.Checkpoints.Count);
        }

        [Test]
        public void InvalidHeaderCannotBeSerialized()
        {
            var log = new ReplayLog(new ReplayHeader { MapWidth = 3 });
            Assert.Throws<InvalidOperationException>(() => log.ToBytes());
            var nan = new ReplayLog(new ReplayHeader { GrowthMultiplier = float.NaN });
            Assert.Throws<InvalidOperationException>(() => nan.ToBytes());
        }

        // ------------------------------------------------------------------ helpers

        private static void WriteInt(byte[] b, int pos, int v)
        {
            b[pos] = (byte)v; b[pos + 1] = (byte)(v >> 8); b[pos + 2] = (byte)(v >> 16); b[pos + 3] = (byte)(v >> 24);
        }

        /// <summary>Recomputes the trailing FNV-1a 64 checksum so structural validation (not the checksum) is exercised.</summary>
        private static byte[] Reseal(byte[] data)
        {
            int payload = data.Length - 8;
            ulong h = 14695981039346656037UL;
            for (int i = 0; i < payload; i++) h = unchecked((h ^ data[i]) * 1099511628211UL);
            for (int i = 0; i < 8; i++) data[payload + i] = (byte)(h >> (8 * i));
            return data;
        }
    }
}
