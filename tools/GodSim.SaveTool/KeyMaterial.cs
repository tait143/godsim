using System;
using System.Text;

namespace GodSim.SaveTool
{
    /// <summary>
    /// Loads HMAC keys from --key-file / --key-hex. Key bytes are never printed, logged or echoed in error
    /// messages (errors name only the option and the path); callers wipe the array with <see cref="Wipe"/>.
    /// Key-file format: either the raw key bytes, or ASCII hex (whitespace ignored, optional "hex:" prefix),
    /// or "base64:" followed by base64 text.
    /// </summary>
    public static class KeyMaterial
    {
        public const int MinKeyBytes = 16;
        public const int MaxKeyBytes = 1024;

        /// <summary>
        /// Reads the key named by <paramref name="fileOption"/> / <paramref name="hexOption"/> (at most one may
        /// be given). Returns null when neither is present.
        /// </summary>
        public static byte[] FromArgs(ArgParser args, string fileOption = "--key-file", string hexOption = "--key-hex")
        {
            bool hasFile = args.HasValue(fileOption), hasHex = hexOption != null && args.HasValue(hexOption);
            if (hasFile && hasHex) throw new ToolException(ExitCodes.Usage, "pass either " + fileOption + " or " + hexOption + ", not both");
            if (hasFile) return FromFile(args.Value(fileOption), fileOption);
            if (hasHex)
            {
                byte[] k = ParseHex(args.Value(hexOption));
                if (k == null) throw new ToolException(ExitCodes.Usage, hexOption + " is not valid hex (the value is not shown)");
                return CheckLength(k, hexOption);
            }
            return null;
        }

        public static byte[] FromFile(string path, string option)
        {
            byte[] raw = SafeFile.ReadBounded(path, SafeFile.MaxKeyFileBytes, "Key file");
            try
            {
                byte[] key = Decode(raw);
                if (key == null) throw new ToolException(ExitCodes.Usage, "key file " + TextOut.Safe(path, 300) + " is malformed (expected raw bytes, hex, or base64: text)");
                return CheckLength(key, option);
            }
            finally
            {
                Wipe(raw);
            }
        }

        /// <summary>Overwrites key material in memory.</summary>
        public static void Wipe(byte[] key)
        {
            if (key != null) Array.Clear(key, 0, key.Length);
        }

        private static byte[] CheckLength(byte[] key, string option)
        {
            if (key.Length < MinKeyBytes || key.Length > MaxKeyBytes)
            {
                int n = key.Length;
                Wipe(key);
                throw new ToolException(ExitCodes.Usage, option + ": key must be " + MinKeyBytes + ".." + MaxKeyBytes + " bytes (got " + n + ")");
            }
            return key;
        }

        private static byte[] Decode(byte[] raw)
        {
            if (raw.Length == 0) return null;
            if (!IsPrintableAscii(raw)) return (byte[])raw.Clone(); // raw binary key
            string text = Encoding.ASCII.GetString(raw).Trim();
            if (text.StartsWith("base64:", StringComparison.OrdinalIgnoreCase))
            {
                try { return Convert.FromBase64String(text.Substring(7).Trim()); }
                catch (FormatException) { return null; }
            }
            if (text.StartsWith("hex:", StringComparison.OrdinalIgnoreCase)) return ParseHex(text.Substring(4));
            // Printable text that is not hex is treated as a raw (passphrase-like) key.
            return ParseHex(text) ?? (byte[])raw.Clone();
        }

        private static bool IsPrintableAscii(byte[] b)
        {
            for (int i = 0; i < b.Length; i++)
            {
                byte c = b[i];
                if (c == (byte)'\n' || c == (byte)'\r' || c == (byte)'\t') continue;
                if (c < 0x20 || c > 0x7E) return false;
            }
            return true;
        }

        /// <summary>Parses hex (whitespace ignored). Returns null if malformed.</summary>
        public static byte[] ParseHex(string s)
        {
            if (s == null) return null;
            var digits = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsWhiteSpace(c)) continue;
                if (Nibble(c) < 0) return null;
                digits.Append(c);
            }
            if (digits.Length == 0 || digits.Length % 2 != 0) return null;
            var key = new byte[digits.Length / 2];
            for (int i = 0; i < key.Length; i++) key[i] = (byte)((Nibble(digits[2 * i]) << 4) | Nibble(digits[2 * i + 1]));
            digits.Clear();
            return key;
        }

        private static int Nibble(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }
    }
}
