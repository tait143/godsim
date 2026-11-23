using System;
using System.Collections.Generic;
using System.Globalization;

namespace GodSim.SaveTool
{
    /// <summary>
    /// Strict command-line parser for one subcommand. Every option must be declared up front; unknown
    /// options, repeated options, missing values, wrong positional counts and malformed numbers are all
    /// usage errors (exit code 2). Options accept "--name value" or "--name=value". A lone "--" ends option
    /// parsing so a file literally named "--x" can still be passed. No globbing or response files.
    /// </summary>
    public sealed class ArgParser
    {
        private readonly string _command;
        private readonly HashSet<string> _flags = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _valued = new HashSet<string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _setFlags = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<string> _positionals = new List<string>();
        private readonly string[] _positionalNames;

        /// <summary>Maximum accepted length of a single argument (defensive; paths and hex keys fit easily).</summary>
        public const int MaxArgLength = 4096;

        public ArgParser(string command, params string[] positionalNames)
        {
            _command = command;
            _positionalNames = positionalNames ?? new string[0];
            // Global options accepted by every subcommand.
            Flag("--verbose");
        }

        public ArgParser Flag(string name) { _flags.Add(name); return this; }
        public ArgParser Option(string name) { _valued.Add(name); return this; }

        public IReadOnlyList<string> Positionals => _positionals;

        /// <summary>Parses the arguments that follow the subcommand name.</summary>
        public ArgParser Parse(IReadOnlyList<string> args, int start)
        {
            bool optionsEnded = false;
            for (int i = start; i < args.Count; i++)
            {
                string a = args[i];
                if (a == null) throw Usage("null argument");
                if (a.Length > MaxArgLength) throw Usage("argument " + (i + 1) + " is longer than " + MaxArgLength + " characters");
                if (!optionsEnded && a == "--") { optionsEnded = true; continue; }
                if (!optionsEnded && a.StartsWith("-", StringComparison.Ordinal) && a.Length > 1)
                {
                    string name = a, inlineValue = null;
                    int eq = a.IndexOf('=');
                    if (eq > 0) { name = a.Substring(0, eq); inlineValue = a.Substring(eq + 1); }
                    if (_flags.Contains(name))
                    {
                        if (inlineValue != null) throw Usage("option " + name + " does not take a value");
                        if (!_setFlags.Add(name)) throw Usage("option " + name + " given more than once");
                    }
                    else if (_valued.Contains(name))
                    {
                        if (_values.ContainsKey(name)) throw Usage("option " + name + " given more than once");
                        string value = inlineValue;
                        if (value == null)
                        {
                            if (i + 1 >= args.Count) throw Usage("option " + name + " requires a value");
                            value = args[++i];
                            if (value == null || value.Length > MaxArgLength) throw Usage("invalid value for " + name);
                        }
                        if (value.Length == 0) throw Usage("option " + name + " requires a non-empty value");
                        _values[name] = value;
                    }
                    else
                    {
                        throw Usage("unknown option '" + TextOut.Safe(name, 64) + "' for '" + _command + "'");
                    }
                }
                else
                {
                    _positionals.Add(a);
                }
            }

            if (_positionals.Count != _positionalNames.Length)
            {
                throw Usage("'" + _command + "' expects " + _positionalNames.Length + " argument(s) <" +
                            string.Join("> <", _positionalNames) + ">, got " + _positionals.Count);
            }
            return this;
        }

        public bool Has(string flag) => _setFlags.Contains(flag);
        public bool HasValue(string option) => _values.ContainsKey(option);
        public string Value(string option) => _values.TryGetValue(option, out string v) ? v : null;

        public string Positional(int index) => _positionals[index];

        public string Required(string option)
        {
            string v = Value(option);
            if (v == null) throw Usage("'" + _command + "' requires " + option);
            return v;
        }

        /// <summary>Reads an integer option in [min, max]; absent -> fallback.</summary>
        public int Int(string option, int fallback, int min, int max)
        {
            string v = Value(option);
            if (v == null) return fallback;
            if (!int.TryParse(v, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int n) || n < min || n > max)
                throw Usage(option + " must be an integer in [" + min + ", " + max + "]");
            return n;
        }

        /// <summary>Reads an unsigned 64-bit option (decimal or 0x-prefixed hex); absent -> fallback.</summary>
        public ulong ULong(string option, ulong fallback)
        {
            string v = Value(option);
            if (v == null) return fallback;
            if (!TryParseULong(v, out ulong n)) throw Usage(option + " must be an unsigned 64-bit integer (decimal or 0x hex)");
            return n;
        }

        public static bool TryParseULong(string v, out ulong n)
        {
            n = 0;
            if (string.IsNullOrEmpty(v)) return false;
            if (v.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return v.Length > 2 && v.Length <= 18 && ulong.TryParse(v.Substring(2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out n);
            return ulong.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out n);
        }

        private ToolException Usage(string message) => new ToolException(ExitCodes.Usage, message + " (see 'savetool help')");
    }
}
