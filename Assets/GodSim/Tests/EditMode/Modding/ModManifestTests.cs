using System;
using GodSim.Core.Modding;
using NUnit.Framework;

namespace GodSim.Tests.Modding
{
    public class ModManifestTests
    {
        [Test]
        public void ParsesFullManifest()
        {
            var m = ModManifest.Parse("{\"id\":\"better_houses\",\"name\":\"Better Houses\",\"version\":\"1.2.3-beta.1+build.5\",\"author\":\"Ann\"," +
                "\"description\":\"Line1\\nLine2\",\"gameVersionRange\":\"^0.1.0\",\"loadOrder\":-5," +
                "\"dependencies\":[\"core_fixes\",{\"id\":\"pretty_names\",\"version\":\">=1.0.0 <2.0.0\",\"optional\":true}],\"dataFiles\":[\"b.json\",\"a.json\"],\"homepage\":\"x\"}");
            Assert.AreEqual("better_houses", m.Id);
            Assert.AreEqual("1.2.3-beta.1+build.5", m.Version.ToString());
            Assert.AreEqual(-5, m.LoadOrder);
            Assert.AreEqual("Line1\nLine2", m.Description);
            Assert.AreEqual(2, m.Dependencies.Count);
            Assert.IsTrue(m.Dependencies[1].Optional);
            Assert.AreEqual("b.json", m.DataFiles[0]);
            Assert.AreEqual(1, m.Warnings.Count); // unknown "homepage"
        }

        [TestCase("ab", false)]
        [TestCase("abc", true)]
        [TestCase("my.mod-v2_x", true)]
        [TestCase("MyMod", false)]
        [TestCase("my mod", false)]
        [TestCase("../evil", false)]
        [TestCase("my/mod", false)]
        [TestCase("...", false)]
        [TestCase("abcdefghijabcdefghijabcdefghijabcdefghijabcdefgh", true)]
        [TestCase("abcdefghijabcdefghijabcdefghijabcdefghijabcdefghi", false)]
        public void IdValidation(string id, bool valid)
        {
            Assert.AreEqual(valid, ModManifest.IsValidId(id));
        }

        [TestCase("{\"name\":\"x\",\"version\":\"1.0.0\"}", TestName = "MissingId")]
        [TestCase("{\"id\":\"abc\",\"version\":\"1.0.0\"}", TestName = "MissingName")]
        [TestCase("{\"id\":\"abc\",\"name\":\"x\"}", TestName = "MissingVersion")]
        [TestCase("{\"id\":\"abc\",\"name\":\"x\",\"version\":\"1.0\"}", TestName = "ShortVersion")]
        [TestCase("{\"id\":\"abc\",\"name\":\"x\",\"version\":\"01.0.0\"}", TestName = "LeadingZeroVersion")]
        [TestCase("{\"id\":\"abc\",\"name\":\"x\",\"version\":\"99999999999.0.0\"}", TestName = "OverflowVersion")]
        [TestCase("{\"id\":\"abc\",\"name\":\"x\",\"version\":\"1.0.0\",\"loadOrder\":1.5}", TestName = "FractionalLoadOrder")]
        [TestCase("{\"id\":\"abc\",\"name\":\"x\",\"version\":\"1.0.0\",\"loadOrder\":1e10}", TestName = "HugeLoadOrder")]
        [TestCase("{\"id\":\"abc\",\"name\":\"x\",\"version\":\"1.0.0\",\"dependencies\":[\"abc\"]}", TestName = "SelfDependency")]
        [TestCase("{\"id\":\"abc\",\"name\":\"x\",\"version\":\"1.0.0\",\"dependencies\":[\"dep\",\"dep\"]}", TestName = "DuplicateDependency")]
        [TestCase("{\"id\":\"abc\",\"name\":\"x\",\"version\":\"1.0.0\",\"dataFiles\":[\"../secret.json\"]}", TestName = "TraversalDataFile")]
        [TestCase("{\"id\":\"abc\",\"name\":\"x\",\"version\":\"1.0.0\",\"dataFiles\":[\"sub/a.json\"]}", TestName = "SubdirDataFile")]
        [TestCase("{\"id\":\"abc\",\"name\":\"x\",\"version\":\"1.0.0\",\"dataFiles\":[\"C:\\\\a.json\"]}", TestName = "AbsoluteDataFile")]
        [TestCase("{\"id\":\"abc\",\"name\":\"x\",\"version\":\"1.0.0\",\"gameVersionRange\":\">=x\"}", TestName = "BadRange")]
        [TestCase("{\"id\":\"abc\",\"name\":\"\\u0007\",\"version\":\"1.0.0\"}", TestName = "NameOnlyControlChar")]
        [TestCase("[]", TestName = "NotAnObject")]
        public void InvalidManifestsThrowModLoadException(string json)
        {
            Assert.Throws<ModLoadException>(() => ModManifest.Parse(json));
        }

        [Test]
        public void SemVerPrecedenceFollowsSpec()
        {
            string[] ordered = { "1.0.0-alpha", "1.0.0-alpha.1", "1.0.0-alpha.beta", "1.0.0-beta", "1.0.0-beta.2", "1.0.0-beta.11", "1.0.0-rc.1", "1.0.0", "1.0.1", "1.1.0", "2.0.0" };
            for (int i = 1; i < ordered.Length; i++)
                Assert.IsTrue(SemVersion.Parse(ordered[i - 1]).CompareTo(SemVersion.Parse(ordered[i])) < 0, ordered[i - 1] + " < " + ordered[i]);
            Assert.AreEqual(0, SemVersion.Parse("1.0.0+a").CompareTo(SemVersion.Parse("1.0.0+b")));
        }

        [TestCase("*", "5.0.0", true)]
        [TestCase(">=0.1.0 <1.0.0", "0.1.0", true)]
        [TestCase(">=0.1.0 <1.0.0", "1.0.0", false)]
        [TestCase("^1.2.0", "1.9.9", true)]
        [TestCase("^1.2.0", "2.0.0", false)]
        [TestCase("^0.2.3", "0.2.9", true)]
        [TestCase("^0.2.3", "0.3.0", false)]
        [TestCase("~1.2.3", "1.2.9", true)]
        [TestCase("~1.2.3", "1.3.0", false)]
        [TestCase("1.0.0 || >=3.0.0", "3.1.0", true)]
        [TestCase("1.0.0 || >=3.0.0", "2.0.0", false)]
        [TestCase("=1.0.0", "1.0.0", true)]
        public void VersionRanges(string range, string version, bool expected)
        {
            Assert.AreEqual(expected, SemVersionRange.Parse(range).Contains(SemVersion.Parse(version)));
        }

        [Test]
        public void InvalidRangesRejected()
        {
            Assert.IsFalse(SemVersionRange.TryParse(">>1.0.0", out _));
            Assert.IsFalse(SemVersionRange.TryParse("1.x", out _));
            Assert.IsFalse(SemVersionRange.TryParse(new string('1', 200), out _));
            Assert.Throws<ModLoadException>(() => SemVersionRange.Parse("abc"));
        }
    }
}
