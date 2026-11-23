using System;
using System.Diagnostics;
using System.Text;
using GodSim.Core;
using GodSim.Core.Modding;
using NUnit.Framework;

namespace GodSim.Tests.Modding
{
    public class SafeJsonTests
    {
        /// <summary>Backtick stands for a backslash so JSON escapes stay readable in C# source.</summary>
        private static string J(string s) => s.Replace('`', '\\');
        private static string C(int code) => ((char)code).ToString();

        private static ModLoadException Reject(string text, JsonLimits limits = null)
        {
            try
            {
                SafeJson.Parse(text, limits, "test.json");
            }
            catch (ModLoadException e) { return e; }
            catch (Exception e) { Assert.Fail("Expected ModLoadException but got " + e.GetType().Name + ": " + e.Message); }
            Assert.Fail("Expected rejection of: " + (text.Length > 80 ? text.Substring(0, 80) + "..." : text));
            return null;
        }

        // ------------------------------------------------------------------ accepted
        [Test]
        public void ParsesObjectsArraysAndScalarsInOrder()
        {
            var v = SafeJson.Parse("{\"z\": 1, \"a\": [true, false, null, -0.5e2, 0, 12345678901234], \"m\": {\"x\": \"y\"}}");
            Assert.AreEqual(JsonKind.Object, v.Kind);
            Assert.AreEqual(3, v.Count);
            Assert.AreEqual("z", v.KeyAt(0));
            Assert.AreEqual("a", v.KeyAt(1));
            Assert.AreEqual("m", v.KeyAt(2));
            var a = v.Get("a");
            Assert.AreEqual(6, a.Count);
            Assert.IsTrue(a.Item(0).BoolValue);
            Assert.IsFalse(a.Item(1).BoolValue);
            Assert.IsTrue(a.Item(2).IsNull);
            Assert.AreEqual(-50.0, a.Item(3).NumberValue);
            Assert.AreEqual(12345678901234.0, a.Item(5).NumberValue);
            Assert.AreEqual("y", v.Get("m").Get("x").StringValue);
            Assert.IsNull(v.Get("missing"));
            Assert.IsNull(a.Item(99));
        }

        [Test]
        public void DecodesAllEscapesIncludingSurrogatePairs()
        {
            var v = SafeJson.Parse(J("\"q`\" b`` s`/ `b`f`n`r`t `u00e9 `ud83d`ude00 `u0041\""));
            string expected = "q\" b\\ s/ \b\f\n\r\t " + C(0xE9) + " " + C(0xD83D) + C(0xDE00) + " A";
            Assert.AreEqual(expected, v.StringValue);
        }

        [Test]
        public void AcceptsRawNonAsciiAndValidRawSurrogatePairs()
        {
            var v = SafeJson.Parse("[\"" + C(0x4E16) + C(0xD83D) + C(0xDE00) + "\"]");
            Assert.AreEqual(3, v.Item(0).StringValue.Length);
        }

        [Test]
        public void IgnoresSingleLeadingBomInStringAndUtf8Input()
        {
            Assert.AreEqual(1.0, SafeJson.Parse(C(0xFEFF) + "{\"a\":1}").Get("a").NumberValue);
            var bytes = new byte[] { 0xEF, 0xBB, 0xBF, (byte)'[', (byte)'1', (byte)']' };
            Assert.AreEqual(1, SafeJson.Parse(bytes).Count);
        }

        [Test]
        public void TracksLineAndColumnOfValues()
        {
            var v = SafeJson.Parse("{\n  \"a\": 1,\r\n  \"b\": [\n    true\n  ]\n}");
            Assert.AreEqual(2, v.Get("a").Line);
            Assert.AreEqual(8, v.Get("a").Column);
            Assert.AreEqual(3, v.Get("b").Line);
            Assert.AreEqual(4, v.Get("b").Item(0).Line);
            Assert.AreEqual(5, v.Get("b").Item(0).Column);
        }

        [Test]
        public void ErrorCarriesPreciseLineAndColumn()
        {
            var e = Reject("{\n  \"a\": 1,\n  \"b\": tru\n}");
            Assert.AreEqual(3, e.Line);
            Assert.AreEqual(8, e.Column);
            Assert.IsTrue(e.Message.Contains("test.json:3:8"), e.Message);
        }

        [Test]
        public void DuplicateKeyErrorPointsAtSecondOccurrence()
        {
            var e = Reject("{\"a\": 1,\n \"a\": 2}");
            Assert.AreEqual(2, e.Line);
            Assert.AreEqual(2, e.Column);
            Assert.IsTrue(e.Reason.Contains("Duplicate key"), e.Reason);
        }

        [Test]
        public void DuplicateKeyDetectedInLargeObjects()
        {
            var sb = new StringBuilder("{");
            for (int i = 0; i < 500; i++) sb.Append("\"k").Append(i).Append("\":").Append(i).Append(',');
            sb.Append("\"k250\":0}");
            Assert.IsTrue(Reject(sb.ToString()).Reason.Contains("Duplicate key \"k250\""));
        }

        [Test]
        public void RoundTripsThroughToJson()
        {
            string src = J("{\"a\":[1,2.5,-3e-7,true,null],\"s\":\"x`n`u0001\",\"o\":{}}");
            var v = SafeJson.Parse(src);
            var again = SafeJson.Parse(v.ToJson());
            Assert.AreEqual(v.ToJson(), again.ToJson());
            Assert.AreEqual("x\n" + C(1), again.Get("s").StringValue);
        }

        // ------------------------------------------------------------------ rejected (malformed / hostile)
        [TestCase("", TestName = "Empty")]
        [TestCase("   \n\t ", TestName = "WhitespaceOnly")]
        [TestCase("{", TestName = "UnterminatedObject")]
        [TestCase("[1, 2", TestName = "UnterminatedArray")]
        [TestCase("{\"a\" 1}", TestName = "MissingColon")]
        [TestCase("{\"a\":1 \"b\":2}", TestName = "MissingComma")]
        [TestCase("{\"a\":1,}", TestName = "TrailingCommaObject")]
        [TestCase("[1,2,]", TestName = "TrailingCommaArray")]
        [TestCase("[,1]", TestName = "LeadingComma")]
        [TestCase("{\"a\":1,\"a\":2}", TestName = "DuplicateKeys")]
        [TestCase("{a:1}", TestName = "UnquotedKey")]
        [TestCase("{'a':1}", TestName = "SingleQuotes")]
        [TestCase("{1:1}", TestName = "NumericKey")]
        [TestCase("[NaN]", TestName = "NaN")]
        [TestCase("[Infinity]", TestName = "Infinity")]
        [TestCase("[-Infinity]", TestName = "NegativeInfinity")]
        [TestCase("[1e400]", TestName = "HugeExponentOverflow")]
        [TestCase("[-1e309]", TestName = "NegativeOverflow")]
        [TestCase("[01]", TestName = "LeadingZero")]
        [TestCase("[-]", TestName = "BareMinus")]
        [TestCase("[+1]", TestName = "PlusSign")]
        [TestCase("[.5]", TestName = "LeadingDot")]
        [TestCase("[1.]", TestName = "TrailingDot")]
        [TestCase("[1e]", TestName = "EmptyExponent")]
        [TestCase("[1e+]", TestName = "SignOnlyExponent")]
        [TestCase("[0x10]", TestName = "HexNumber")]
        [TestCase("[tru]", TestName = "TruncatedTrue")]
        [TestCase("[nul]", TestName = "TruncatedNull")]
        [TestCase("[True]", TestName = "CapitalTrue")]
        [TestCase("[undefined]", TestName = "Undefined")]
        [TestCase("{} {}", TestName = "TwoRoots")]
        [TestCase("[1] x", TestName = "TrailingGarbage")]
        [TestCase("\"abc", TestName = "UnterminatedString")]
        [TestCase("[\"abc]", TestName = "UnterminatedStringInArray")]
        [TestCase("// c\n{}", TestName = "LineComment")]
        [TestCase("{/* c */}", TestName = "BlockComment")]
        [TestCase("[1]\u0000", TestName = "TrailingNul")]
        public void RejectsMalformed(string text)
        {
            Reject(text);
        }

        [TestCase("[\"`x\"]", TestName = "InvalidEscapeX")]
        [TestCase("[\"`a\"]", TestName = "InvalidEscapeA")]
        [TestCase("[\"`0\"]", TestName = "InvalidEscapeZero")]
        [TestCase("[\"`u12\"]", TestName = "ShortUnicodeEscape")]
        [TestCase("[\"`u12G4\"]", TestName = "NonHexUnicodeEscape")]
        [TestCase("[\"`ud800\"]", TestName = "LoneHighSurrogateEscape")]
        [TestCase("[\"`udc00\"]", TestName = "LoneLowSurrogateEscape")]
        [TestCase("[\"`ud800`u0041\"]", TestName = "HighSurrogateThenNonLow")]
        [TestCase("[\"`ud800x\"]", TestName = "HighSurrogateThenChar")]
        [TestCase("[\"`ude00`ud83d\"]", TestName = "ReversedSurrogatePair")]
        [TestCase("[\"abc`", TestName = "DanglingBackslash")]
        public void RejectsInvalidEscapes(string text)
        {
            Reject(J(text));
        }

        [Test]
        public void RejectsRawControlCharactersAndLoneRawSurrogates()
        {
            Reject("[\"a\nb\"]");
            Reject("[\"a" + C(0x01) + "b\"]");
            Reject("[\"a\tb\"]");
            Reject("[\"" + C(0xD800) + "\"]");
            Reject("[\"" + C(0xDC00) + "x\"]");
            Reject("[\"x" + C(0xD83D) + "\"]");
        }

        [Test]
        public void RejectsBomOutsideTheStart()
        {
            Reject("{}" + C(0xFEFF));
            Reject("[1," + C(0xFEFF) + "2]");
            Reject(C(0xFEFF) + C(0xFEFF) + "{}");
            var e = Reject(C(0xFEFF) + "{,}");
            Assert.AreEqual(1, e.Line);
        }

        [Test]
        public void RejectsInvalidUtf8AndNonUtf8Boms()
        {
            Assert.Throws<ModLoadException>(() => SafeJson.Parse(new byte[] { (byte)'[', (byte)'"', 0xC3, 0x28, (byte)'"', (byte)']' }));
            Assert.Throws<ModLoadException>(() => SafeJson.Parse(new byte[] { 0xFF, 0xFE, (byte)'{', 0, (byte)'}', 0 }));
            Assert.Throws<ModLoadException>(() => SafeJson.Parse(new byte[] { (byte)'[', (byte)'"', 0xED, 0xA0, 0x80, (byte)'"', (byte)']' })); // CESU surrogate
            Assert.Throws<ModLoadException>(() => SafeJson.Parse(new byte[] { (byte)'[', (byte)'"', 0xC0, 0xAF, (byte)'"', (byte)']' }));   // overlong '/'
            var e = Assert.Throws<ModLoadException>(() => SafeJson.Parse(new byte[] { (byte)'[', (byte)'\n', (byte)'"', 0xFF, (byte)'"', (byte)']' }));
            Assert.AreEqual(2, e.Line);
        }

        [Test]
        public void NullInputThrowsModLoadException()
        {
            Assert.Throws<ModLoadException>(() => SafeJson.Parse((string)null));
            Assert.Throws<ModLoadException>(() => SafeJson.Parse((byte[])null));
        }

        // ------------------------------------------------------------------ limits / DoS
        [Test]
        public void DeepNestingBombIsRejectedQuickly()
        {
            var sw = Stopwatch.StartNew();
            var e = Reject(new string('[', 500000));
            Assert.IsTrue(e.Reason.Contains("Nesting deeper than 32"), e.Reason);
            Reject(new string('[', 33) + new string(']', 33));
            Reject(Repeat("{\"a\":", 40) + "1" + new string('}', 40));
            Assert.IsTrue(sw.ElapsedMilliseconds < 2000);
        }

        [Test]
        public void ExactlyMaxDepthIsAccepted()
        {
            var v = SafeJson.Parse(new string('[', 32) + new string(']', 32));
            Assert.AreEqual(JsonKind.Array, v.Kind);
        }

        [Test]
        public void OversizedInputRejectedBeforeParsing()
        {
            var huge = new string(' ', 2 * 1024 * 1024 + 1);
            Assert.IsTrue(Reject(huge).Reason.Contains("too large"));
            Assert.Throws<ModLoadException>(() => SafeJson.Parse(new byte[2 * 1024 * 1024 + 1]));
        }

        [Test]
        public void StringLengthLimitEnforced()
        {
            SafeJson.Parse("[\"" + new string('a', 4096) + "\"]");
            Reject("[\"" + new string('a', 4097) + "\"]");
            Reject("{\"" + new string('k', 5000) + "\":1}");
        }

        [Test]
        public void ArrayAndObjectSizeLimitsEnforced()
        {
            SafeJson.Parse("[" + Repeat("0,", 9999) + "0]");
            Reject("[" + Repeat("0,", 10000) + "0]");
            var sb = new StringBuilder("{");
            for (int i = 0; i <= 1000; i++) { if (i > 0) sb.Append(','); sb.Append("\"k").Append(i).Append("\":0"); }
            Assert.IsTrue(Reject(sb.Append('}').ToString()).Reason.Contains("more than 1000 members"));
        }

        [Test]
        public void TotalNodeLimitEnforced()
        {
            // 30 arrays x 9000 numbers = 270,000 nodes > 200,000
            var sb = new StringBuilder("[");
            for (int a = 0; a < 30; a++) { if (a > 0) sb.Append(','); sb.Append('[').Append(Repeat("1,", 8999)).Append("1]"); }
            sb.Append(']');
            Assert.IsTrue(Reject(sb.ToString()).Reason.Contains("more than 200000 values"));
        }

        [Test]
        public void HugeNumberLiteralsRejected()
        {
            Reject("[" + new string('9', 400) + "]");          // too long
            Reject("[1" + new string('0', 70) + "]");           // longer than 64 chars
            Reject("[1e99999]");                                 // overflow
            Assert.AreEqual(0.0, SafeJson.Parse("[1e-400]").Item(0).NumberValue); // underflow to zero is fine
        }

        [Test]
        public void CustomLimitsAreHonoredAndClamped()
        {
            var lim = new JsonLimits { MaxDepth = 2, MaxStringLength = 3 };
            SafeJson.Parse("[[1]]", lim);
            Reject("[[[1]]]", lim);
            Reject("[\"abcd\"]", lim);
            var absurd = new JsonLimits { MaxDepth = 1000000 };
            Reject(new string('[', 200) + new string(']', 200), absurd); // clamped to DepthCeiling (128)
        }

        [Test]
        public void LargeValidDocumentParsesInLinearTime()
        {
            var sb = new StringBuilder("[");
            for (int i = 0; i < 9000; i++) { if (i > 0) sb.Append(','); sb.Append("{\"name\":\"item").Append(i).Append("\",\"v\":").Append(i * 0.5).Append(",\"t\":[1,2,3]}"); }
            sb.Append(']');
            var sw = Stopwatch.StartNew();
            var v = SafeJson.Parse(sb.ToString());
            Assert.AreEqual(9000, v.Count);
            Assert.IsTrue(sw.ElapsedMilliseconds < 3000);
        }

        // ------------------------------------------------------------------ fuzz
        [Test]
        public void FuzzedMutationsOnlySucceedOrThrowModLoadException()
        {
            string seed = J("{\"formatVersion\":1,\"buildings\":{\"House\":{\"buildWork\":25.5,\"cost\":{\"Wood\":5}}}," +
                            "\"races\":{\"Elf\":{\"names\":{\"personPrefixes\":[\"Ae\",\"El`u00e9\"]},\"aggression\":0.1}}," +
                            "\"newTechs\":[{\"key\":\"m.x\",\"name\":\"X `ud83d`ude00\",\"age\":\"Dawn\",\"cost\":1e2,\"effects\":[],\"ok\":[true,false,null,-0.0]}]}");
            SafeJson.Parse(seed);
            string alphabet = J("{}[]\",:.-+eE0123456789tfnulr` `\n\t") + C(0) + C(0xD800) + C(0xDC00) + C(0xFEFF) + C(0x202E);
            var rng = new Rng(12345);
            int ok = 0, rejected = 0;
            var sw = Stopwatch.StartNew();
            for (int iter = 0; iter < 2000; iter++)
            {
                var sb = new StringBuilder(seed);
                int mutations = 1 + rng.Range(0, 4);
                for (int m = 0; m < mutations; m++)
                {
                    int op = rng.Range(0, 6);
                    int pos = sb.Length == 0 ? 0 : rng.Range(0, sb.Length);
                    switch (op)
                    {
                        case 0: if (sb.Length > 0) sb[pos] = alphabet[rng.Range(0, alphabet.Length)]; break;
                        case 1: sb.Insert(pos, alphabet[rng.Range(0, alphabet.Length)]); break;
                        case 2: if (sb.Length > 0) sb.Remove(pos, Math.Min(1 + rng.Range(0, 8), sb.Length - pos)); break;
                        case 3: { int len = Math.Min(1 + rng.Range(0, 40), sb.Length - pos); sb.Insert(pos, sb.ToString(pos, len)); break; }
                        case 4: sb.Length = pos; break;
                        default: sb.Insert(pos, rng.Chance(0.5f) ? new string('[', 1 + rng.Range(0, 60)) : "{\"k\":"); break;
                    }
                }
                string text = sb.ToString();
                try
                {
                    var v = SafeJson.Parse(text);
                    ok++;
                    // whatever parses must round-trip
                    Assert.AreEqual(v.ToJson(), SafeJson.Parse(v.ToJson()).ToJson());
                }
                catch (ModLoadException e)
                {
                    rejected++;
                    Assert.IsTrue(e.Line >= 0 && e.Column >= 0);
                }
                catch (Exception e)
                {
                    Assert.Fail("Iteration " + iter + " threw " + e.GetType().Name + ": " + e.Message);
                }
            }
            Assert.AreEqual(2000, ok + rejected);
            Assert.IsTrue(rejected > 500);
            Assert.IsTrue(sw.ElapsedMilliseconds < 10000);
        }

        private static string Repeat(string s, int n)
        {
            var sb = new StringBuilder(s.Length * n);
            for (int i = 0; i < n; i++) sb.Append(s);
            return sb.ToString();
        }
    }
}
