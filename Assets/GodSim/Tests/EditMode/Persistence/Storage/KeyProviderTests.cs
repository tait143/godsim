using System;
using System.IO;
using System.Text;
using GodSim.Core.Persistence;
using NUnit.Framework;

namespace GodSim.Tests.Persistence.Storage
{
    public class KeyProviderTests
    {
        private StorageTestDirectory _dir;

        [SetUp]
        public void SetUp() { _dir = new StorageTestDirectory(); }

        [TearDown]
        public void TearDown() { _dir.Dispose(); }

        private static byte[] Hex(string hex)
        {
            var b = new byte[hex.Length / 2];
            for (int i = 0; i < b.Length; i++) b[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return b;
        }

        [Test]
        public void StaticProviderCopiesDefensively()
        {
            var key = new byte[16];
            for (int i = 0; i < key.Length; i++) key[i] = (byte)(i + 1);
            var provider = new StaticKeyProvider(key);
            key[0] = 99;
            byte[] k1 = provider.GetKey();
            Assert.AreEqual(1, k1[0], "constructor copies the input");
            k1[1] = 77;
            Assert.AreEqual(2, provider.GetKey()[1], "GetKey returns a fresh copy");
            Assert.AreNotSame(provider.GetKey(), provider.GetKey());
            Assert.AreEqual(16, provider.KeyLength);
        }

        [Test]
        public void StaticProviderRejectsShortOrNullKeys()
        {
            Assert.Throws<ArgumentNullException>(() => new StaticKeyProvider(null));
            Assert.Throws<ArgumentException>(() => new StaticKeyProvider(new byte[15]));
            Assert.DoesNotThrow(() => new StaticKeyProvider(new byte[64]));
        }

        [Test]
        public void DerivedProviderMatchesPbkdf2Sha256Vector()
        {
            // Published PBKDF2-HMAC-SHA256 test vector (salt >= 8 bytes): c=4096, dkLen=32.
            var vector = new DerivedKeyProvider("passwordPASSWORDpassword", Encoding.ASCII.GetBytes("saltSALTsaltSALTsaltSALTsaltSALTsalt"), 4096);
            Assert.AreEqual(Hex("348c89dbcbd32b2f32d814b8116e84cf2b17347ebc1800181c4e2a1fb8dd53e1"), vector.GetKey());
        }

        [Test]
        public void DerivedProviderIsDeterministicAndSaltSensitive()
        {
            byte[] salt = Encoding.ASCII.GetBytes("godsim-save-salt");
            var a = new DerivedKeyProvider("hunter2", salt, 2000);
            var b = new DerivedKeyProvider("hunter2", salt, 2000);
            Assert.AreEqual(a.GetKey(), b.GetKey());
            Assert.AreEqual(DerivedKeyProvider.KeyLength, a.GetKey().Length);
            Assert.AreNotEqual(a.GetKey(), new DerivedKeyProvider("hunter2", Encoding.ASCII.GetBytes("other-save-salt!"), 2000).GetKey());
            Assert.AreNotEqual(a.GetKey(), new DerivedKeyProvider("hunter3", salt, 2000).GetKey());
            Assert.AreNotEqual(a.GetKey(), new DerivedKeyProvider("hunter2", salt, 2001).GetKey());

            // Text and UTF-8 bytes of the same secret derive the same key; the caller's array is untouched.
            byte[] secretBytes = Encoding.UTF8.GetBytes("hunter2");
            Assert.AreEqual(a.GetKey(), new DerivedKeyProvider(secretBytes, salt, 2000).GetKey());
            Assert.AreEqual(Encoding.UTF8.GetBytes("hunter2"), secretBytes);

            var k = a.GetKey();
            k[0] ^= 0xFF;
            Assert.AreEqual(b.GetKey(), a.GetKey(), "returned keys are copies");
        }

        [Test]
        public void DerivedProviderValidatesArguments()
        {
            byte[] salt = new byte[16];
            Assert.Throws<ArgumentException>(() => new DerivedKeyProvider("", salt, 2000));
            Assert.Throws<ArgumentException>(() => new DerivedKeyProvider((string)null, salt, 2000));
            Assert.Throws<ArgumentNullException>(() => new DerivedKeyProvider("s", null, 2000));
            Assert.Throws<ArgumentException>(() => new DerivedKeyProvider("s", new byte[7], 2000));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DerivedKeyProvider("s", salt, 999));
            Assert.Throws<ArgumentOutOfRangeException>(() => new DerivedKeyProvider("s", salt, DerivedKeyProvider.MaxIterations + 1));
            Assert.Throws<ArgumentNullException>(() => new DerivedKeyProvider((byte[])null, salt, 2000));
            Assert.Throws<ArgumentException>(() => new DerivedKeyProvider(new byte[0], salt, 2000));
            var ex = Assert.Throws<ArgumentException>(() => new DerivedKeyProvider("pw\uD800", salt, 2000));
            Assert.IsTrue(ex.Message.IndexOf("D800", StringComparison.OrdinalIgnoreCase) < 0, "message must not quote secret characters");
            Assert.AreEqual(DerivedKeyProvider.DefaultIterations, new DerivedKeyProvider("s", salt).Iterations);
        }

        [Test]
        public void InstallKeyIsCreatedPersistedAndReloaded()
        {
            string dir = _dir.Combine("profile");
            byte[] first = InstallKeyFile.LoadOrCreate(dir);
            Assert.AreEqual(InstallKeyFile.KeyLength, first.Length);
            string path = Path.Combine(dir, InstallKeyFile.FileName);
            Assert.IsTrue(File.Exists(path));
            Assert.AreEqual(32L, new FileInfo(path).Length);
            Assert.AreEqual(first, File.ReadAllBytes(path));
            Assert.AreEqual(1, Directory.GetFiles(dir).Length, "no temp files are left behind");

            byte[] second = InstallKeyFile.LoadOrCreate(dir);
            Assert.AreEqual(first, second);
            Assert.AreNotSame(first, second);

            Assert.IsTrue(InstallKeyFile.TryLoadOrCreate(dir, out byte[] third, out string error), error);
            Assert.AreEqual(first, third);

            byte[] other = InstallKeyFile.LoadOrCreate(_dir.Combine("other"));
            Assert.AreNotEqual(first, other, "each install gets its own random secret");

            Assert.DoesNotThrow(() => new StaticKeyProvider(first));
            Assert.DoesNotThrow(() => new DerivedKeyProvider(first, Encoding.ASCII.GetBytes("godsim.saves"), 1000));
        }

        [Test]
        public void CorruptInstallKeyIsRejected()
        {
            string dir = _dir.Combine("corrupt");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, InstallKeyFile.FileName);

            File.WriteAllBytes(path, new byte[31]);
            Assert.Throws<InvalidDataException>(() => InstallKeyFile.LoadOrCreate(dir));
            File.WriteAllBytes(path, new byte[1 << 20]);
            Assert.Throws<InvalidDataException>(() => InstallKeyFile.LoadOrCreate(dir));
            File.WriteAllBytes(path, new byte[32]);
            Assert.Throws<InvalidDataException>(() => InstallKeyFile.LoadOrCreate(dir), "all-zero key is treated as corrupt");

            Assert.IsFalse(InstallKeyFile.TryLoadOrCreate(dir, out byte[] key, out string error));
            Assert.IsNull(key);
            Assert.IsTrue(error.IndexOf(_dir.Path, StringComparison.OrdinalIgnoreCase) < 0, "error leaks a path: " + error);

            Directory.Delete(dir, true);
            Directory.CreateDirectory(path); // a directory where the key file should be
            Assert.Throws<IOException>(() => InstallKeyFile.LoadOrCreate(dir));
            Assert.IsFalse(InstallKeyFile.TryLoadOrCreate(dir, out _, out error));
            Assert.IsTrue(error.IndexOf(_dir.Path, StringComparison.OrdinalIgnoreCase) < 0, "error leaks a path: " + error);

            Assert.Throws<ArgumentException>(() => InstallKeyFile.LoadOrCreate(null));
            Assert.Throws<ArgumentException>(() => InstallKeyFile.LoadOrCreate(" "));
            Assert.IsFalse(InstallKeyFile.TryLoadOrCreate(null, out _, out error));
        }

        [Test]
        public void SymlinkedInstallKeyIsRefused()
        {
#if NET6_0_OR_GREATER
            string dir = _dir.Combine("linked");
            Directory.CreateDirectory(dir);
            string target = _dir.Combine("elsewhere.key");
            File.WriteAllBytes(target, Hex("0102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f20"));
            try { File.CreateSymbolicLink(Path.Combine(dir, InstallKeyFile.FileName), target); }
            catch (Exception) { return; }
            Assert.Throws<IOException>(() => InstallKeyFile.LoadOrCreate(dir));
#else
            Assert.IsTrue(true, "symlink creation API unavailable on this runtime");
#endif
        }
    }
}
