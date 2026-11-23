using System;
using System.IO;
using GodSim.Core.Persistence;
using NUnit.Framework;

namespace GodSim.Tests.Persistence.Storage
{
    public class SaveFileStoreTests
    {
        private StorageTestDirectory _dir;

        [SetUp]
        public void SetUp() { _dir = new StorageTestDirectory(); }

        [TearDown]
        public void TearDown() { _dir.Dispose(); }

        private static byte[] Bytes(int length, byte seed)
        {
            var b = new byte[length];
            for (int i = 0; i < b.Length; i++) b[i] = unchecked((byte)(seed + i * 31));
            return b;
        }

        private void AssertNoPathLeak(string error)
        {
            Assert.IsNotNull(error);
            Assert.IsTrue(error.IndexOf(_dir.Path, StringComparison.OrdinalIgnoreCase) < 0, "error leaks the root path: " + error);
            Assert.IsTrue(error.IndexOf(Path.GetTempPath(), StringComparison.OrdinalIgnoreCase) < 0, "error leaks the temp path: " + error);
        }

        [Test]
        public void WriteThenRead_RoundTripsExactBytes()
        {
            var store = new SaveFileStore(_dir.Path);
            var data = Bytes(10000, 7);
            Assert.IsTrue(store.TryWrite("slot_1", data, out string error), error);
            Assert.IsNull(error);
            Assert.IsTrue(store.Exists("slot_1"));
            Assert.IsTrue(store.TryRead("slot_1", out byte[] read, out error), error);
            Assert.AreEqual(data, read);
            Assert.IsTrue(File.Exists(_dir.Combine("slot_1.gsav")));
            Assert.IsFalse(File.Exists(_dir.Combine("slot_1.gsav.tmp")), "temp file must not survive a successful write");
        }

        [Test]
        public void Overwrite_ReplacesContent()
        {
            var store = new SaveFileStore(_dir.Path);
            Assert.IsTrue(store.TryWrite("a", Bytes(100, 1), out _));
            Assert.IsTrue(store.TryWrite("a", Bytes(50, 2), out string error), error);
            Assert.IsTrue(store.TryRead("a", out byte[] read, out _));
            Assert.AreEqual(Bytes(50, 2), read);
        }

        [Test]
        public void RootIsNormalizedAbsolutePath()
        {
            var store = new SaveFileStore(_dir.Path + Path.DirectorySeparatorChar);
            Assert.AreEqual(Path.GetFullPath(_dir.Path).TrimEnd(Path.DirectorySeparatorChar), store.Root);
            Assert.IsTrue(Path.IsPathRooted(store.Root));
        }

        [Test]
        public void ConstructorCreatesMissingRoot()
        {
            string nested = _dir.Combine("nested");
            var store = new SaveFileStore(nested);
            Assert.IsTrue(Directory.Exists(nested));
            Assert.IsTrue(store.TryWrite("x", Bytes(4, 0), out string error), error);
        }

        [TestCase("../x")]
        [TestCase("..")]
        [TestCase("a/b")]
        [TestCase("a\\b")]
        [TestCase("/etc/passwd")]
        [TestCase("C:")]
        [TestCase("x.gsav")]
        [TestCase("a b")]
        [TestCase("CON")]
        [TestCase("con")]
        [TestCase("Nul")]
        [TestCase("aux")]
        [TestCase("PRN")]
        [TestCase("COM1")]
        [TestCase("lpt9")]
        [TestCase("")]
        [TestCase("   ")]
        [TestCase("été")]
        [TestCase("сейв")]
        [TestCase("a\u0000b")]
        [TestCase("tab\there")]
        [TestCase("slot:stream")]
        [TestCase("*")]
        public void InvalidSlotNamesAreRejected(string slot)
        {
            Assert.IsFalse(SaveFileStore.IsValidSlotName(slot));
            var store = new SaveFileStore(_dir.Path);
            Assert.IsFalse(store.TryWrite(slot, Bytes(8, 1), out string error));
            AssertNoPathLeak(error);
            Assert.IsFalse(store.TryRead(slot, out byte[] data, out error));
            Assert.IsNull(data);
            AssertNoPathLeak(error);
            Assert.IsFalse(store.Exists(slot));
            Assert.IsFalse(store.Delete(slot));
            Assert.AreEqual(0, Directory.GetFileSystemEntries(_dir.Path).Length, "nothing may be created for an invalid name");
        }

        [Test]
        public void NullAndOverlongNamesAreRejected()
        {
            Assert.IsFalse(SaveFileStore.IsValidSlotName(null));
            Assert.IsFalse(SaveFileStore.IsValidSlotName(new string('a', 49)));
            Assert.IsTrue(SaveFileStore.IsValidSlotName(new string('a', 48)));
            var store = new SaveFileStore(_dir.Path);
            Assert.IsFalse(store.TryWrite(new string('a', 49), Bytes(8, 1), out string error));
            AssertNoPathLeak(error);
            Assert.IsFalse(store.TryWrite(null, Bytes(8, 1), out error));
            AssertNoPathLeak(error);
        }

        [TestCase("a")]
        [TestCase("Z")]
        [TestCase("0")]
        [TestCase("autosave")]
        [TestCase("Slot_01-final")]
        [TestCase("CONSOLE")]
        [TestCase("COM10")]
        [TestCase("NULL")]
        [TestCase("-")]
        [TestCase("___")]
        public void ValidSlotNamesAreAccepted(string slot)
        {
            Assert.IsTrue(SaveFileStore.IsValidSlotName(slot));
            var store = new SaveFileStore(_dir.Path);
            Assert.IsTrue(store.TryWrite(slot, Bytes(3, 9), out string error), error);
            Assert.IsTrue(store.TryRead(slot, out byte[] data, out error), error);
            Assert.AreEqual(Bytes(3, 9), data);
        }

        [Test]
        public void BackupsRotateUpToNAndOldestIsDropped()
        {
            var store = new SaveFileStore(_dir.Path, 1024 * 1024, 2);
            for (byte v = 1; v <= 4; v++) Assert.IsTrue(store.TryWrite("world", Bytes(16, v), out string error), error);

            Assert.IsTrue(store.TryRead("world", out byte[] current, out _));
            Assert.AreEqual(Bytes(16, 4), current);
            Assert.IsTrue(store.TryReadBackup("world", 1, out byte[] bak1, out string e1), e1);
            Assert.AreEqual(Bytes(16, 3), bak1);
            Assert.IsTrue(store.TryReadBackup("world", 2, out byte[] bak2, out string e2), e2);
            Assert.AreEqual(Bytes(16, 2), bak2);
            Assert.IsFalse(File.Exists(_dir.Combine("world.gsav.bak3")), "only N backups are kept");
            Assert.IsFalse(store.TryReadBackup("world", 3, out _, out string e3));
            AssertNoPathLeak(e3);
        }

        [Test]
        public void ZeroBackupsKeepsOnlyThePrimary()
        {
            var store = new SaveFileStore(_dir.Path, 1024, 0);
            Assert.IsTrue(store.TryWrite("s", Bytes(8, 1), out _));
            Assert.IsTrue(store.TryWrite("s", Bytes(8, 2), out string error), error);
            Assert.AreEqual(1, Directory.GetFiles(_dir.Path).Length);
            Assert.IsTrue(store.TryRead("s", out byte[] data, out _));
            Assert.AreEqual(Bytes(8, 2), data);
        }

        [Test]
        public void OversizedFileIsRejectedOnRead()
        {
            var store = new SaveFileStore(_dir.Path, 1024, 3);
            File.WriteAllBytes(_dir.Combine("big.gsav"), new byte[2048]);
            Assert.IsFalse(store.TryRead("big", out byte[] data, out string error));
            Assert.IsNull(data);
            AssertNoPathLeak(error);
            StringAssertContains(error, "too large");
        }

        [Test]
        public void OversizedDataIsRejectedOnWrite()
        {
            var store = new SaveFileStore(_dir.Path, 1024, 3);
            Assert.IsFalse(store.TryWrite("big", new byte[1025], out string error));
            AssertNoPathLeak(error);
            Assert.IsTrue(store.TryWrite("big", new byte[1024], out error), error);
            Assert.IsFalse(File.Exists(_dir.Combine("big.gsav.tmp")));
        }

        [Test]
        public void EmptyOrNullDataIsRejected()
        {
            var store = new SaveFileStore(_dir.Path);
            Assert.IsFalse(store.TryWrite("e", new byte[0], out string error));
            AssertNoPathLeak(error);
            Assert.IsFalse(store.TryWrite("e", null, out error));
            AssertNoPathLeak(error);
            File.WriteAllBytes(_dir.Combine("empty.gsav"), new byte[0]);
            Assert.IsFalse(store.TryRead("empty", out _, out error));
            AssertNoPathLeak(error);
        }

        [Test]
        public void ListReturnsOnlyRealSlotsSorted()
        {
            var store = new SaveFileStore(_dir.Path);
            Assert.IsTrue(store.TryWrite("beta", Bytes(20, 1), out _));
            Assert.IsTrue(store.TryWrite("alpha", Bytes(10, 1), out _));
            Assert.IsTrue(store.TryWrite("alpha", Bytes(12, 2), out _)); // creates alpha.gsav.bak1
            File.WriteAllBytes(_dir.Combine("gamma.gsav.tmp"), Bytes(5, 1));
            File.WriteAllBytes(_dir.Combine("beta.gsav.bak2"), Bytes(5, 1));
            File.WriteAllBytes(_dir.Combine("notes.txt"), Bytes(5, 1));
            File.WriteAllBytes(_dir.Combine("bad name.gsav"), Bytes(5, 1));
            File.WriteAllBytes(_dir.Combine("CON.gsav"), Bytes(5, 1));
            File.WriteAllBytes(_dir.Combine(".gsav"), Bytes(5, 1));
            File.WriteAllBytes(_dir.Combine("zero.gsav"), new byte[0]);
            Directory.CreateDirectory(_dir.Combine("folder.gsav"));

            var list = store.List();
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("alpha", list[0].Slot);
            Assert.AreEqual(12L, list[0].SizeBytes);
            Assert.AreEqual("beta", list[1].Slot);
            Assert.AreEqual(20L, list[1].SizeBytes);
            Assert.IsTrue(list[0].LastWriteUtc.Kind == DateTimeKind.Utc);
        }

        [Test]
        public void DeleteRemovesSlotTempAndBackups()
        {
            var store = new SaveFileStore(_dir.Path, 4096, 3);
            Assert.IsTrue(store.TryWrite("d", Bytes(8, 1), out _));
            Assert.IsTrue(store.TryWrite("d", Bytes(8, 2), out _));
            Assert.IsTrue(store.TryWrite("keep", Bytes(8, 3), out _));
            File.WriteAllBytes(_dir.Combine("d.gsav.tmp"), Bytes(3, 3));
            Assert.IsTrue(File.Exists(_dir.Combine("d.gsav.bak1")));

            Assert.IsTrue(store.Delete("d"));
            Assert.IsFalse(store.Exists("d"));
            Assert.IsFalse(File.Exists(_dir.Combine("d.gsav")));
            Assert.IsFalse(File.Exists(_dir.Combine("d.gsav.bak1")));
            Assert.IsFalse(File.Exists(_dir.Combine("d.gsav.tmp")));
            Assert.IsFalse(store.Delete("d"), "second delete reports nothing removed");
            Assert.IsTrue(store.Exists("keep"));
            Assert.IsFalse(store.TryRead("d", out _, out string error));
            AssertNoPathLeak(error);
        }

        [Test]
        public void StaleTempFileDoesNotBlockWrites()
        {
            var store = new SaveFileStore(_dir.Path);
            File.WriteAllBytes(_dir.Combine("s.gsav.tmp"), Bytes(999, 1));
            Assert.IsTrue(store.TryWrite("s", Bytes(10, 5), out string error), error);
            Assert.IsTrue(store.TryRead("s", out byte[] data, out _));
            Assert.AreEqual(Bytes(10, 5), data);
            Assert.IsFalse(File.Exists(_dir.Combine("s.gsav.tmp")));
        }

        [Test]
        public void TryMethodsNeverThrowOnHostileInputs()
        {
            var store = new SaveFileStore(_dir.Path, 64, 2);
            Directory.CreateDirectory(_dir.Combine("dir.gsav"));
            Directory.CreateDirectory(_dir.Combine("tmpdir.gsav.tmp"));
            Assert.IsTrue(store.TryWrite("bk", Bytes(8, 1), out _));
            Directory.CreateDirectory(_dir.Combine("bk.gsav.bak2"));

            Assert.IsFalse(store.TryWrite("dir", Bytes(8, 1), out string error));
            AssertNoPathLeak(error);
            Assert.IsFalse(store.TryRead("dir", out _, out error));
            AssertNoPathLeak(error);
            Assert.IsFalse(store.TryWrite("tmpdir", Bytes(8, 1), out error));
            AssertNoPathLeak(error);
            Assert.IsFalse(store.TryWrite("bk", Bytes(8, 2), out error), "rotation onto a directory must fail cleanly");
            AssertNoPathLeak(error);
            Assert.IsTrue(store.TryRead("bk", out byte[] bk, out _));
            Assert.AreEqual(Bytes(8, 1), bk, "failed write must leave the previous save intact");
            Assert.IsFalse(File.Exists(_dir.Combine("bk.gsav.tmp")), "failed write must clean up its temp file");

            string[] slots = { null, "", "dir", "tmpdir", "bk", "missing", "../../etc/passwd", new string('x', 10000), "CON", "\uD800" };
            foreach (var slot in slots)
            {
                Assert.DoesNotThrow(() => store.TryWrite(slot, Bytes(8, 1), out _));
                Assert.DoesNotThrow(() => store.TryWrite(slot, null, out _));
                Assert.DoesNotThrow(() => store.TryWrite(slot, new byte[100], out _));
                Assert.DoesNotThrow(() => store.TryRead(slot, out _, out _));
                Assert.DoesNotThrow(() => store.TryReadBackup(slot, 1, out _, out _));
                Assert.DoesNotThrow(() => store.TryReadBackup(slot, -5, out _, out _));
                Assert.DoesNotThrow(() => store.Exists(slot));
                Assert.DoesNotThrow(() => store.List());
                Assert.DoesNotThrow(() => store.Delete(slot));
            }

            // Root removed after construction: reads fail cleanly, writes recreate it.
            Directory.Delete(_dir.Path, true);
            Assert.DoesNotThrow(() => store.List());
            Assert.AreEqual(0, store.List().Count);
            Assert.IsFalse(store.TryRead("bk", out _, out error));
            AssertNoPathLeak(error);
            Assert.IsTrue(store.TryWrite("again", Bytes(8, 1), out error), error);
        }

        [Test]
        public void ConstructorValidatesArguments()
        {
            Assert.Throws<ArgumentException>(() => new SaveFileStore(null));
            Assert.Throws<ArgumentException>(() => new SaveFileStore("  "));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SaveFileStore(_dir.Path, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SaveFileStore(_dir.Path, SaveFileStore.AbsoluteMaxFileBytes + 1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SaveFileStore(_dir.Path, 1024, -1));
            Assert.Throws<ArgumentOutOfRangeException>(() => new SaveFileStore(_dir.Path, 1024, SaveFileStore.MaxBackups + 1));
            File.WriteAllBytes(_dir.Combine("afile"), Bytes(4, 1));
            Assert.Throws<IOException>(() => new SaveFileStore(_dir.Combine("afile")));
        }

        [Test]
        public void SymlinkedRootAndSlotsAreRefused()
        {
#if NET6_0_OR_GREATER
            string outside = _dir.Combine("outside");
            Directory.CreateDirectory(outside);
            string secret = Path.Combine(outside, "secret.gsav");
            File.WriteAllBytes(secret, Bytes(32, 42));
            string realRoot = _dir.Combine("saves");
            var store = new SaveFileStore(realRoot);

            try
            {
                Directory.CreateSymbolicLink(_dir.Combine("linkroot"), outside);
                File.CreateSymbolicLink(Path.Combine(realRoot, "evil.gsav"), secret);
                File.CreateSymbolicLink(Path.Combine(realRoot, "victim.gsav.tmp"), secret);
            }
            catch (Exception)
            {
                return; // platform does not allow unprivileged symlinks (e.g. Windows without developer mode)
            }

            Assert.Throws<IOException>(() => new SaveFileStore(_dir.Combine("linkroot")));

            Assert.IsFalse(store.TryRead("evil", out byte[] data, out string error));
            Assert.IsNull(data);
            AssertNoPathLeak(error);
            Assert.IsFalse(store.TryWrite("evil", Bytes(8, 1), out error));
            AssertNoPathLeak(error);
            Assert.IsFalse(store.Exists("evil"));

            // A planted temp link is unlinked, never written through.
            Assert.IsTrue(store.TryWrite("victim", Bytes(8, 7), out error), error);
            Assert.AreEqual(Bytes(32, 42), File.ReadAllBytes(secret), "write must not follow a planted temp link");
            Assert.IsTrue(store.TryRead("victim", out data, out _));
            Assert.AreEqual(Bytes(8, 7), data);

            foreach (var info in store.List()) Assert.AreNotEqual("evil", info.Slot);

            // Deleting a linked slot removes the link only.
            store.Delete("evil");
            Assert.IsTrue(File.Exists(secret));
            Assert.AreEqual(Bytes(32, 42), File.ReadAllBytes(secret));
#else
            Assert.IsTrue(true, "symlink creation API unavailable on this runtime");
#endif
        }

        private static void StringAssertContains(string text, string fragment)
        {
            Assert.IsTrue(text != null && text.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) >= 0, "expected '" + fragment + "' in: " + text);
        }
    }
}
